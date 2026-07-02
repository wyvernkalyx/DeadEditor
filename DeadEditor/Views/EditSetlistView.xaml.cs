using DeadEditor.Models;
using DeadEditor.Services;
using DeadEditor.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeadEditor
{
    public partial class EditSetlistView : System.Windows.Controls.UserControl
    {
        private readonly ShellWindow _shell;
        private readonly ConcertReference _concert;
        private readonly NormalizationService _normalizationService;

        // The concert's date at view construction. The grid mutates the live cached
        // instance, so by save time _concert.Date already holds the NEW date — the
        // original must be snapshotted up front to detect a date change and rekey the cache.
        private string _originalDate;

        // Serialized baseline for diff-at-save unverify (concert-verification-spec.md decision 4).
        // Captured from the editor's loaded state (LoadTrackData), normalized through the same
        // ConcertSnapshot projection the persisted form uses, with LastUpdated excluded. Never an
        // object reference — the grid mutates live instances, so a reference baseline would never
        // diff dirty.
        private string _baselineJson = "";

        // Suppresses change-tracking + verify refresh while LoadTrackData populates the fields
        // (setting TextBox.Text fires TextChanged). Mirrors the existing recursion-guard pattern.
        private bool _suppressChangeTracking;
        private List<EditableTrack> _tracks = new();
        private bool _hasUnsavedChanges;

        // Combine aliases authored this session, STAGED — not applied to the live cached _concert
        // until Save (alias-setlists-spec.md §6.2). Mirrors how grid edits stage in _tracks: the
        // editor's atomic Save applies these via ConcertLookupService.TryAppendAliasEntry just before
        // the whole-object serialize, and Cancel simply drops the buffer so the cache stays clean
        // (the property the earlier click-time-mutation design broke).
        private readonly List<AliasEntry> _pendingAliasEntries = new();

        // Recorded combines the user has staged for REMOVAL this session (covered-index sets), applied
        // at the editor's atomic Save via ConcertLookupService.TryRemoveAliasEntry immediately before
        // the whole-object serialize, then cleared on success. Disjoint from _pendingAliasEntries by
        // construction: the ✕ on a pending-append row drops it from _pendingAliasEntries directly, so a
        // covered set is never in both buffers. Cancel drops both (view teardown), mirroring authoring.
        private readonly List<List<int>> _pendingAliasRemovals = new();

        // Distinct from _hasUnsavedChanges: tracks only STRUCTURAL/content edits since the last Save
        // (Add/Remove/Normalize/cell edits — anything routed through OnEditorChanged). It gates the
        // combine dirty-block: CoveredOfficialIndices are positional, so authoring may run only
        // against the persisted positions. Staging a combine moves no positions, so it does NOT set
        // this flag — multiple combines can be authored in one session.
        private bool _structuralEditsSinceSave;

        // Structural labels for set assignment UI — these are display values, not song data
        private static readonly string[] SetChoices =
            { "Set 1", "Set 2", "Set 3", "Encore", "Encore 2" };

        public string VenueName => _concert.Venue;
        public bool HasUnsavedChanges => _hasUnsavedChanges;

        /// <summary>
        /// Drives the combine-row ✕/↺ controls' enabled state. Removal/un-stage is refused while
        /// structural edits are unsaved (Q3): the row labels derive from live <c>_tracks</c> positions
        /// via <see cref="DescribeRun"/>, which go stale mid-structural-edit, so acting on a
        /// stale-looking row is unsafe. Bound from the row DataTemplate via RelativeSource; re-read on
        /// each <see cref="RefreshAliasDisplay"/> ItemsSource reassignment (which fires when the
        /// structural flag toggles), so no change-notification is needed on this plain CLR property.
        /// </summary>
        public bool CombineControlsEnabled => !_structuralEditsSinceSave;

        /// <summary>Fired when save completes so the shell can refresh the detail view.</summary>
        public event EventHandler? SaveCompleted;

        public EditSetlistView(ShellWindow shell, ConcertReference concert)
        {
            InitializeComponent();
            _shell = shell;
            _concert = concert;
            _originalDate = concert.Date ?? "";
            _normalizationService = new NormalizationService();
        }

        private void EditSetlistView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTrackData();

            // Set up the Set column's combo box items
            var setColumn = TracksDataGrid.Columns[3] as DataGridComboBoxColumn;
            if (setColumn != null)
            {
                setColumn.ItemsSource = SetChoices;
            }
        }

        private void LoadTrackData()
        {
            _suppressChangeTracking = true;

            // Populate metadata fields
            VenueTextBox.Text = _concert.Venue;
            var location = _concert.FormattedLocation;
            CityStateTextBox.Text = !string.IsNullOrEmpty(_concert.State) && _concert.Country == "US"
                ? $"{_concert.City}, {_concert.State}"
                : !string.IsNullOrEmpty(_concert.Country)
                    ? $"{_concert.City}, {_concert.Country}"
                    : _concert.City;
            DateTextBox.Text = _concert.Date;

            // Build editable track list from the concert's sets
            _tracks.Clear();
            int position = 0;

            foreach (var set in _concert.Sets)
            {
                // Normalize set name: strip trailing colon
                var setName = set.Name.TrimEnd(':', ' ');

                foreach (var song in set.Songs)
                {
                    position++;
                    var track = new EditableTrack
                    {
                        Position = position,
                        SongName = song.Name,
                        Date = !string.IsNullOrEmpty(song.Date) ? song.Date : _concert.Date,
                        Segue = song.Segue,
                        Set = setName,
                        Info = song.Info
                    };
                    track.PropertyChanged += Track_PropertyChanged;
                    _tracks.Add(track);
                }
            }

            TracksDataGrid.ItemsSource = _tracks;
            BottomStatusText.Text = $"{_tracks.Count} tracks";

            // Capture the diff-at-save baseline now that the editor reflects the loaded concert,
            // before any user edit — then show the verify surface. Done here (not the ctor) because
            // the fields are populated in this Loaded-time method, not in the constructor.
            _suppressChangeTracking = false;
            _baselineJson = BuildSnapshotJson();
            RefreshVerifyControls();
            RefreshAliasDisplay();
        }

        private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnEditorChanged();
        }

        /// <summary>
        /// Common editor-change hook: marks unsaved and refreshes the verify surface (honest badge +
        /// gate). Routed from every tracked edit path (metadata fields, track property changes, cell
        /// edits, Add/Remove/Normalize). Suppressed during LoadTrackData's field population.
        /// </summary>
        private void OnEditorChanged()
        {
            if (_suppressChangeTracking) return;
            _hasUnsavedChanges = true;
            _structuralEditsSinceSave = true;
            RefreshVerifyControls();
            // The structural flag just flipped true — re-render the combine rows so their ✕/↺ controls
            // disable (Q3 gate). Reassigning ItemsSource re-reads CombineControlsEnabled.
            RefreshAliasDisplay();
        }

        private void MetadataField_TextChanged(object sender, TextChangedEventArgs e)
        {
            OnEditorChanged();
        }

        // ===== ADD / REMOVE SONGS =====

        private void AddSongButton_Click(object sender, RoutedEventArgs e)
        {
            var lastSet = _tracks.LastOrDefault()?.Set ?? "Set 1";
            var newTrack = new EditableTrack
            {
                Position = _tracks.Count + 1,
                SongName = "",
                Date = DateTextBox.Text.Trim(),
                Segue = false,
                Set = lastSet
            };
            newTrack.PropertyChanged += Track_PropertyChanged;
            _tracks.Add(newTrack);
            TracksDataGrid.ItemsSource = null;
            TracksDataGrid.ItemsSource = _tracks;
            OnEditorChanged();

            BottomStatusText.Text = $"{_tracks.Count} tracks";

            // Focus the new row's song name cell
            TracksDataGrid.UpdateLayout();
            TracksDataGrid.SelectedItem = newTrack;
            TracksDataGrid.ScrollIntoView(newTrack);
        }

        private void TracksDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Delete && !TracksDataGrid.IsEditing())
            {
                RemoveSelectedTracks();
                e.Handled = true;
            }
        }

        private void RemoveSelectedTracks()
        {
            var selected = TracksDataGrid.SelectedItems.OfType<EditableTrack>().ToList();
            if (selected.Count == 0) return;

            foreach (var track in selected)
            {
                track.PropertyChanged -= Track_PropertyChanged;
                _tracks.Remove(track);
            }

            // Renumber
            for (int i = 0; i < _tracks.Count; i++)
                _tracks[i].Position = i + 1;

            TracksDataGrid.ItemsSource = null;
            TracksDataGrid.ItemsSource = _tracks;
            OnEditorChanged();
            BottomStatusText.Text = $"{_tracks.Count} tracks";
        }

        // ===== COMBINE AUTHORING (alias-setlists-spec.md §6.2) =====

        /// <summary>
        /// Authors a combined alias from the selected contiguous within-set run. STAGES it into
        /// <see cref="_pendingAliasEntries"/> — does NOT mutate the live cached <c>_concert</c> here;
        /// the editor's existing atomic Save applies the buffer via
        /// <see cref="ConcertLookupService.TryAppendAliasEntry"/>, and Cancel drops it. Refused while
        /// structural edits are unsaved (positions may have moved). Invalid selections and dedup hits
        /// report an inline message and author nothing.
        /// </summary>
        private void CombineSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            // Dirty-block: author only against the persisted setlist positions. Staged combines do
            // not set _structuralEditsSinceSave, so authoring multiple combines in one session is OK.
            if (_structuralEditsSinceSave)
            {
                StatusText.Text = "Save your setlist changes before combining.";
                return;
            }

            var rows = TracksDataGrid.SelectedItems.OfType<EditableTrack>()
                .Select(t => new CombineRow(t.Position, t.Set))
                .ToList();

            var result = CombineSelectionRule.Evaluate(rows);
            if (!result.IsValid)
            {
                StatusText.Text = result.Message;
                return;
            }

            // Dedup against the UNION of recorded (cached) + pending (staged this session) runs, using
            // the same SequenceEqual identity Save-time TryAppendAliasEntry applies.
            var existingRuns = _concert.AliasSetlists
                .SelectMany(a => a.Entries)
                .Concat(_pendingAliasEntries)
                .Select(en => (IReadOnlyList<int>)en.CoveredOfficialIndices);
            if (CombineSelectionRule.IsDuplicateRun(existingRuns, result.CoveredOfficialIndices))
            {
                StatusText.Text = "That combine is already recorded.";
                return;
            }

            _pendingAliasEntries.Add(new AliasEntry
            {
                CoveredOfficialIndices = result.CoveredOfficialIndices.ToList()
            });

            // Mark unsaved work (drives the Cancel-discard prompt) WITHOUT the structural flag.
            _hasUnsavedChanges = true;
            RefreshAliasDisplay();
            StatusText.Text = $"Combined {result.CoveredOfficialIndices.Count} songs (applies on Save).";
        }

        /// <summary>
        /// Refreshes the combine display: builds per-entry rows via the pure
        /// <see cref="AliasRowBuilder"/> from the net set — recorded (in <c>_concert.AliasSetlists</c>)
        /// with those staged for removal flagged (kept, struck-through), plus pending appends staged
        /// this session — and binds them to the ItemsControl. Reassigning ItemsSource re-realizes the
        /// row controls so each button's <see cref="CombineControlsEnabled"/> binding re-reads the
        /// structural gate. The whole COMBINED TRACKS section is hidden when the NET row list is empty
        /// (a lone staged-for-removal row still renders, so it keeps the section visible).
        /// </summary>
        private void RefreshAliasDisplay()
        {
            var recorded = _concert.AliasSetlists
                .SelectMany(a => a.Entries)
                .Select(en => (IReadOnlyList<int>)en.CoveredOfficialIndices);
            var removals = _pendingAliasRemovals.Select(r => (IReadOnlyList<int>)r);
            var appends = _pendingAliasEntries
                .Select(en => (IReadOnlyList<int>)en.CoveredOfficialIndices);

            var rows = AliasRowBuilder.Build(recorded, removals, appends, DescribeRun);

            AliasRowsControl.ItemsSource = null;
            AliasRowsControl.ItemsSource = rows;

            // Show the whole COMBINED TRACKS section (header + rows) only when at least one recorded
            // or pending combine exists; a lone staged-for-removal row still counts as a row, so it
            // keeps the section visible (struck-through) rather than hiding it mid-stage.
            CombineSection.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Row ✕/↺ handler. Branches on the row's origin/state to keep the append and removal buffers
        /// disjoint: a pending-append row's ✕ drops it from <see cref="_pendingAliasEntries"/> (never a
        /// removal); a staged-for-removal row's ↺ un-stages it from <see cref="_pendingAliasRemovals"/>;
        /// a plain recorded row's ✕ stages it into <see cref="_pendingAliasRemovals"/>. Refused while
        /// structural edits are unsaved (defensive — the button is also disabled in that state). Marks
        /// unsaved work WITHOUT the structural flag (removal is position-stable), mirroring authoring.
        /// </summary>
        private void AliasRowActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not AliasRow row)
                return;
            if (_structuralEditsSinceSave)
                return;

            if (row.IsPendingAppend)
            {
                var idx = _pendingAliasEntries.FindIndex(en =>
                    en.CoveredOfficialIndices.SequenceEqual(row.CoveredIndices));
                if (idx >= 0) _pendingAliasEntries.RemoveAt(idx);
            }
            else if (row.IsStagedForRemoval)
            {
                var idx = _pendingAliasRemovals.FindIndex(r => r.SequenceEqual(row.CoveredIndices));
                if (idx >= 0) _pendingAliasRemovals.RemoveAt(idx);
            }
            else
            {
                _pendingAliasRemovals.Add(row.CoveredIndices.ToList());
            }

            _hasUnsavedChanges = true;
            RefreshAliasDisplay();
        }

        /// <summary>
        /// Renders a covered run as "Song &gt; Song (firstPos–lastPos)" for display, resolving each
        /// covered index against the live edit grid. Delegates the string composition to the shared
        /// <see cref="Helpers.CombineLabel.Describe"/> so the read-only detail view renders identically.
        /// </summary>
        private string DescribeRun(IReadOnlyList<int> coveredIndices)
        {
            return Helpers.CombineLabel.Describe(coveredIndices,
                i => _tracks.FirstOrDefault(t => t.Position == i + 1)?.SongName ?? $"#{i + 1}");
        }

        // ===== NORMALIZE =====

        private void NormalizeButton_Click(object sender, RoutedEventArgs e)
        {
            int normalized = 0;
            foreach (var track in _tracks)
            {
                if (string.IsNullOrWhiteSpace(track.SongName)) continue;

                var result = _normalizationService.Normalize(track.SongName);
                if (result != null && result != track.SongName)
                {
                    track.SongName = result;
                    normalized++;
                }
            }

            TracksDataGrid.ItemsSource = null;
            TracksDataGrid.ItemsSource = _tracks;

            StatusText.Text = normalized > 0
                ? $"Normalized {normalized} song{(normalized == 1 ? "" : "s")}"
                : "All songs already normalized";
            if (normalized > 0)
            {
                _hasUnsavedChanges = true;
                _structuralEditsSinceSave = true; // renames change song identity at a position
            }
            RefreshVerifyControls();
            // Renames shift the labels DescribeRun derives and flip the structural gate — re-render the
            // combine rows so labels refresh and the ✕/↺ controls disable.
            RefreshAliasDisplay();
        }

        private void TracksDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            OnEditorChanged();
        }

        // ===== SAVE =====

        public async System.Threading.Tasks.Task SaveChangesAsync()
        {
            // Validate
            var date = DateTextBox.Text.Trim();
            if (!Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                App.Alerts.Notify("Date must be in yyyy-MM-dd format.", AlertSeverity.Warning, "Invalid Date");
                return;
            }

            // Duplicate-date refusal (add-concert-spec.md Decision 1): never clobber a DIFFERENT
            // existing record. The _originalDate exclusion is load-bearing — a no-change re-save of
            // this concert (date unchanged) is saving itself, not a collision, and must pass. This one
            // check covers both the New-Concert case (_originalDate == "") and the pre-existing
            // mid-edit silent-overwrite (editing one concert's date onto another's).
            if (DuplicateDateRule.IsCollision(
                    ConcertLookupService.Instance.HasConcert(date), date, _originalDate))
            {
                var existing = ConcertLookupService.Instance.GetConcertByDate(date);
                var venueNote = !string.IsNullOrWhiteSpace(existing?.Venue)
                    ? $" ({existing!.Venue})"
                    : "";
                App.Alerts.Notify(
                    $"A concert already exists for {date}{venueNote}.\n\n" +
                    "Change the date, or edit the existing record instead.",
                    AlertSeverity.Warning, "Date Already Exists");
                return;
            }

            if (_tracks.Count == 0)
            {
                App.Alerts.Notify("Setlist must have at least one song.", AlertSeverity.Warning, "Empty Setlist");
                return;
            }

            // Build the persisted object THROUGH the shared projection (same rebuild logic the
            // baseline/diff snapshot uses, so they cannot drift), then apply onto the live cached
            // _concert instance — preserving identity fields (Country, SetlistFmId, …) and the
            // shared-instance cache coherence (NotifySaved below rekeys this same object).
            var projected = ConcertSnapshot.Project(date, VenueTextBox.Text.Trim(),
                CityStateTextBox.Text.Trim(), BuildTrackInputs());
            var currentSnapshot = ConcertSnapshot.Serialize(projected);

            _concert.Date = projected.Date;
            _concert.Venue = projected.Venue;
            _concert.City = projected.City;
            _concert.State = projected.State;
            _concert.Sets = projected.Sets;
            _concert.Tracks = projected.Tracks;
            _concert.HasSetlist = projected.HasSetlist;
            _concert.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Unverify-on-edit (spec decision 4): if the concert is verified and any tracked field
            // changed since the baseline, drop Verified before the write. The compare is over the
            // editor-content snapshot (LastUpdated excluded by construction), so a no-edit save does
            // NOT unverify. Mark-Verified rebaselines, so a just-verified concert diffs equal here.
            if (_concert.Verified && EditUnverifyRule.IsDirty(_baselineJson, currentSnapshot))
                _concert.Verified = false;

            // Write to AppData concerts directory (atomic: temp file + rename)
            try
            {
                var concertsDir = ConcertLookupService.ActiveConcertsPath;
                Directory.CreateDirectory(concertsDir);

                var targetPath = Path.Combine(concertsDir, $"{date}.json");
                var tempPath = targetPath + ".tmp";

                // Apply staged combine edits onto the live _concert immediately BEFORE serialize, so
                // the authored/removed runs ride the existing whole-object write (alias-setlists-spec.md
                // §6.2, staged authoring). ConcertSnapshot.Project does not touch AliasSetlists, so the
                // applied changes survive the projection above. Removals run FIRST, then appends:
                // removals-before-appends is self-healing — a removal that empties+drops the shared
                // AliasSetlist container is re-created by a later append's FirstOrDefault()==null branch.
                // Both helpers are idempotent (dedup / not-found no-op), so a retry after a failed write
                // re-applies harmlessly; the buffers are cleared only on success.
                foreach (var covered in _pendingAliasRemovals)
                    ConcertLookupService.TryRemoveAliasEntry(_concert, covered);
                foreach (var entry in _pendingAliasEntries)
                    ConcertLookupService.TryAppendAliasEntry(_concert, entry);

                // Canonical camelCase, indented, computed keys dropped — matches the bundled
                // concerts/ schema (shared with BoxSetService via CanonicalJson).
                var json = CanonicalJson.Serialize(_concert);

                await System.Threading.Tasks.Task.Run(() =>
                {
                    File.WriteAllText(tempPath, json);
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);
                    File.Move(tempPath, targetPath);
                });

                // If the date changed, the old-date file is now an orphan (we wrote
                // {newDate}.json above). Recycle it with the same mechanism the delete
                // path uses, then reconcile the in-memory cache.
                if (!string.Equals(_originalDate, date, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(_originalDate))
                {
                    var oldPath = Path.Combine(concertsDir, $"{_originalDate}.json");
                    if (File.Exists(oldPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            oldPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                }

                // Rekey the cache on a date change (value is already current — same
                // live instance). Idempotent for an unchanged date.
                ConcertLookupService.Instance.NotifySaved(_originalDate, _concert);
                _originalDate = date;

                _hasUnsavedChanges = false;
                // The staged combines are now persisted on _concert; clear the buffer (so a re-save
                // does not re-walk them) and the structural-edit flag (Save is the new baseline, so
                // authoring is allowed again). Refresh the display to the now-recorded state.
                _pendingAliasEntries.Clear();
                _pendingAliasRemovals.Clear();
                _structuralEditsSinceSave = false;
                RefreshAliasDisplay();

                // Re-baseline: the saved editor state is the new reference, so a reopened-or-reused
                // view does not see phantom dirt. Refresh the verify surface to the persisted state.
                _baselineJson = currentSnapshot;
                RefreshVerifyControls();

                StatusText.Text = "Setlist saved";
                Debug.WriteLine($"[CONCERT EDIT] Saved {date} to {targetPath}");

                SaveCompleted?.Invoke(this, EventArgs.Empty);

                // Navigate back to detail view
                _shell.Navigation.GoBack();
            }
            catch (Exception ex)
            {
                App.Alerts.Notify($"Error saving setlist:\n\n{ex.Message}", AlertSeverity.Error, "Save Failed");
                Debug.WriteLine($"[CONCERT EDIT] Save error: {ex}");
            }
        }

        public async void CancelEdit()
        {
            // Bucket-B confirm (alert-system-spec.md #32). Branch mapping preserved from the old
            // YesNo/Question MessageBox: Yes (true) -> discard + GoBack; No/Esc (false) -> stay.
            // async void mirrors the existing UI-handler pattern; the HeaderBar Cancel/Back click
            // handlers call this fire-and-forget (nothing runs after it), so the signature change is
            // contained — NavigateBack() still delegates here unchanged.
            if (_hasUnsavedChanges)
            {
                bool discard = await App.Alerts.ConfirmAsync(
                    "You have unsaved changes. Discard them?",
                    "Discard Changes?");

                if (!discard) return;
            }

            _shell.Navigation.GoBack();
        }

        public void NavigateBack()
        {
            CancelEdit();
        }

        // ===== VERIFICATION SURFACE (spec decision 7) =====

        /// <summary>Current editor rows as WPF-free projection inputs.</summary>
        private IReadOnlyList<ConcertTrackInput> BuildTrackInputs() =>
            _tracks.Select(t => new ConcertTrackInput(t.SongName, t.Date, t.Segue, t.Set, t.Info)).ToList();

        /// <summary>
        /// Serialized snapshot of the current editor state — the same projection the persisted
        /// object is built through, with LastUpdated excluded by construction. Used for the
        /// baseline, the diff-at-save, and the honest badge.
        /// </summary>
        private string BuildSnapshotJson() =>
            ConcertSnapshot.Serialize(DateTextBox.Text.Trim(), VenueTextBox.Text.Trim(),
                CityStateTextBox.Text.Trim(), BuildTrackInputs());

        /// <summary>
        /// Refreshes the verify badge + Mark-Verified button + reason from the current editor state.
        /// The badge shows the HONEST effectiveVerified: the stored bool AND the editor being clean
        /// vs. the baseline (the same diff Save uses). The gate is evaluated against the current
        /// snapshot projection, not the stale loaded object. Three states mirror the box-set surface:
        /// verified → green badge, button hidden, no reason; unverified + complete → grey badge,
        /// enabled button, no reason; unverified + incomplete → grey badge, disabled button, reason.
        /// </summary>
        private void RefreshVerifyControls()
        {
            var projected = ConcertSnapshot.Project(DateTextBox.Text.Trim(), VenueTextBox.Text.Trim(),
                CityStateTextBox.Text.Trim(), BuildTrackInputs());
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(projected);
            bool effectiveVerified = _concert.Verified
                && !EditUnverifyRule.IsDirty(_baselineJson, ConcertSnapshot.Serialize(projected));

            if (effectiveVerified)
            {
                VerifyBadge.Background = (SolidColorBrush)FindResource("BadgeVerifiedBg");
                VerifyBadgeText.Text = "✓ Verified";
                VerifyBadgeText.Foreground = (SolidColorBrush)FindResource("BadgeVerifiedFg");
                MarkVerifiedButton.Visibility = Visibility.Collapsed;
                // Unverify is the mirror of Mark Verified: visible only in the verified state
                // (spec decision 6, amended). Mutually exclusive with the Mark Verified button.
                UnverifyButton.Visibility = Visibility.Visible;
                VerifyReasonText.Visibility = Visibility.Collapsed;
                return;
            }

            VerifyBadge.Background = (SolidColorBrush)FindResource("BadgeUnverifiedBg");
            VerifyBadgeText.Text = "Unverified";
            VerifyBadgeText.Foreground = (SolidColorBrush)FindResource("BadgeUnverifiedFg");
            MarkVerifiedButton.Visibility = Visibility.Visible;
            MarkVerifiedButton.IsEnabled = canVerify;
            UnverifyButton.Visibility = Visibility.Collapsed;

            if (canVerify)
            {
                VerifyReasonText.Visibility = Visibility.Collapsed;
            }
            else
            {
                VerifyReasonText.Text = reason;
                VerifyReasonText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Marks the concert verified in memory (persist-on-Save). Re-checks the gate defensively
        /// against the current projection (a stale click must not verify an incomplete concert),
        /// sets Verified, and rebaselines so the same-pass Save diff sees baseline == current and
        /// Verified persists. No disk write here — the toolbar Save writes; navigating away without
        /// saving discards the verify.
        /// </summary>
        private void MarkVerifiedButton_Click(object sender, RoutedEventArgs e)
        {
            var projected = ConcertSnapshot.Project(DateTextBox.Text.Trim(), VenueTextBox.Text.Trim(),
                CityStateTextBox.Text.Trim(), BuildTrackInputs());
            var (canVerify, _) = ConcertVerifyGate.Evaluate(projected);
            if (!canVerify) return;

            _concert.Verified = true;
            _baselineJson = ConcertSnapshot.Serialize(projected);
            RefreshVerifyControls();
        }

        /// <summary>
        /// Withdraws verification in memory (spec decision 6, amended). Symmetric with Mark Verified:
        /// drops the flag and refreshes the surface, with no disk write — the toolbar Save persists it,
        /// and navigating away without saving leaves the concert verified on disk. No rebaseline:
        /// Verified is not part of the content snapshot and the content has not changed, so the existing
        /// baseline stays valid and the badge correctly returns to Unverified with Mark Verified enabled.
        /// </summary>
        private void UnverifyButton_Click(object sender, RoutedEventArgs e)
        {
            _concert.Verified = false;
            RefreshVerifyControls();
        }
    }

    /// <summary>
    /// Editable track for the setlist editor DataGrid.
    /// </summary>
    public class EditableTrack : INotifyPropertyChanged
    {
        private int _position;
        private string _songName = "";
        private string _date = "";
        private bool _segue;
        private string _set = "Set 1";
        private string _info = "";

        public int Position
        {
            get => _position;
            set { if (_position != value) { _position = value; OnPropertyChanged(nameof(Position)); } }
        }

        public string SongName
        {
            get => _songName;
            set { if (_songName != value) { _songName = value; OnPropertyChanged(nameof(SongName)); OnPropertyChanged(nameof(SegueMarker)); OnPropertyChanged(nameof(DateDisplay)); } }
        }

        public string Date
        {
            get => _date;
            set { if (_date != value) { _date = value; OnPropertyChanged(nameof(Date)); OnPropertyChanged(nameof(DateDisplay)); } }
        }

        public bool Segue
        {
            get => _segue;
            set { if (_segue != value) { _segue = value; OnPropertyChanged(nameof(Segue)); OnPropertyChanged(nameof(SegueMarker)); } }
        }

        public string SegueMarker => Segue ? " >" : "";
        public string DateDisplay => !string.IsNullOrEmpty(Date) ? $" ({Date})" : "";

        public string Set
        {
            get => _set;
            set { if (_set != value) { _set = value; OnPropertyChanged(nameof(Set)); } }
        }

        public string Info
        {
            get => _info;
            set { if (_info != value) { _info = value; OnPropertyChanged(nameof(Info)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Extension method to check if DataGrid is in editing mode.
    /// </summary>
    public static class DataGridExtensions
    {
        public static bool IsEditing(this DataGrid dataGrid)
        {
            return dataGrid.CommitEdit(DataGridEditingUnit.Row, true) == false;
        }
    }
}
