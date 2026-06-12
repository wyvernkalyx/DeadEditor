using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace DeadEditor
{
    /// <summary>
    /// Multi-step wizard for authoring a <see cref="BoxSetDefinition"/>. Single
    /// UserControl; step transitions toggle <c>Visibility</c> on three content panels
    /// rather than navigating between views. Step 1 is the top-level info form, step 2
    /// the editable track grid, and step 3 the read-only Review summary.
    ///
    /// Shell-agnostic by design: raises <see cref="Completed"/> (Save success or
    /// Cancel) and <see cref="StepChanged"/> for the ShellWindow to plumb back to
    /// navigation and the HeaderBar's Back/Next/Save button state.
    /// </summary>
    public partial class BoxSetWizardView : System.Windows.Controls.UserControl
    {
        private readonly BoxSetService _boxSetService;
        private readonly NormalizationService _normalizationService = new();
        private readonly BoxSetDefinition _definition;
        private readonly ListCollectionView _tracksView;
        // Edit-mode (commit 2): true when the wizard was opened on a saved box. _originalSlug is
        // the slug it was loaded from, so Save can resolve overwrite vs. rename-move vs. collision
        // via BoxSetSaveResolution. Both null/false for the new-box path.
        private readonly bool _isEditingExisting;
        private readonly string? _originalSlug;
        private int _currentStep = 1;

        // Verification baseline (commit 3): a serialized snapshot of the normalized definition,
        // captured at ctor and re-captured by Mark-Verified. The Save diff and the Review panel's
        // effectiveVerified both compare this against the current serialized definition. MUST be a
        // string, never a reference to _definition/.Tracks — the grid mutates those instances live,
        // so a reference baseline would always equal current and never fire (spec refinement 1).
        private string _baselineJson = "";

        // yyyy-MM-dd validation regex — same pattern as EditSetlistView.xaml.cs:197.
        private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        /// <summary>The currently visible step (1-3).</summary>
        public int CurrentStep => _currentStep;

        /// <summary>True when the wizard was opened to edit a saved box set (drives the header
        /// title "Edit Box Set" vs. "New Box Set"). Read by <c>HeaderBar.ShowBoxSetWizardHeader</c>.</summary>
        public bool IsEditingExisting => _isEditingExisting;

        /// <summary>Fired when the wizard finishes — either Save succeeded or the user
        /// cancelled. ShellWindow uses this to navigate back to the Box Sets list.</summary>
        public event EventHandler? Completed;

        /// <summary>Fired whenever the visible step changes. ShellWindow forwards the new
        /// step to <c>HeaderBar.UpdateBoxSetWizardStep</c> so Back/Next/Save buttons update.</summary>
        public event EventHandler<int>? StepChanged;

        /// <summary>
        /// The single date group that is currently expanded — the wizard's accordion (commit
        /// H2). Bound into each group <c>Expander</c>'s <c>IsExpanded</c> via
        /// <c>GroupActiveConverter</c>. A <c>DependencyProperty</c> (not INPC) so the
        /// MultiBinding re-evaluates automatically when it changes.
        /// <para>
        /// <c>null</c> = nothing expanded (a fresh box is all-collapsed); a deliberate empty
        /// string matches the no-date group (set by Add). The null-vs-empty rule itself lives
        /// in <see cref="Helpers.BoxSetGroupHeader.IsActiveGroup"/>. Set this BEFORE each
        /// <c>_tracksView.Refresh()</c> so regenerated group containers realize already in the
        /// right state (no open-then-collapse flash). Transient UI state — never serialized.
        /// </para>
        /// </summary>
        public static readonly DependencyProperty ActiveGroupDateProperty =
            DependencyProperty.Register(nameof(ActiveGroupDate), typeof(string),
                typeof(BoxSetWizardView), new PropertyMetadata(null));

        public string? ActiveGroupDate
        {
            get => (string?)GetValue(ActiveGroupDateProperty);
            set => SetValue(ActiveGroupDateProperty, value);
        }

        /// <summary>New-box-set entry: a fresh, empty definition, discarded on Cancel. Chains
        /// to the shared ctor with no original slug (the not-editing case).</summary>
        public BoxSetWizardView(BoxSetService boxSetService)
            : this(boxSetService, new BoxSetDefinition(), null) { }

        /// <summary>
        /// Shared ctor. <paramref name="definition"/> is the object the wizard edits; for
        /// edit-mode entry (commit 2) it is a fresh read-from-disk copy (read by slug), so edits
        /// are isolated from the saved file until Save and discarded on Cancel. <paramref
        /// name="originalSlug"/> is that box's slug when editing, or null for a new box — it
        /// drives Save's overwrite/rename/collision resolution. The grid + view wiring binds to
        /// <c>definition.Tracks</c>, so the definition MUST be assigned before that wiring.
        /// </summary>
        public BoxSetWizardView(BoxSetService boxSetService, BoxSetDefinition definition, string? originalSlug)
        {
            InitializeComponent();
            _boxSetService = boxSetService;
            _definition = definition;
            _originalSlug = originalSlug;
            _isEditingExisting = !string.IsNullOrEmpty(originalSlug);

            // Bind the step-2 grid directly to the definition's track list. Edits flow live into
            // _definition.Tracks (approach (a), INPC on BoxSetTrack), so Save() needs no separate
            // step-2 sync. In edit-mode the definition is a read-fresh copy, so this live binding
            // stays safe — the on-disk file is untouched until Write.
            TracksDataGrid.ItemsSource = _definition.Tracks;

            // Group the grid by Date with a deterministic within-group order. Configure the
            // view ONCE and keep it: structural mutations (Add/Pull/Delete) call
            // _tracksView.Refresh() rather than reassigning ItemsSource — reassigning would
            // rebuild the view and discard this group/sort config. IsLiveGrouping moves a row
            // to its date group when its Date commits (BoxSetTrack raises PropertyChanged on
            // Date; the Date column commits on edit-end, so the row regroups after the edit,
            // not mid-keystroke). No IsLiveSorting: TrackNumber re-sorts only on Refresh.
            _tracksView = (ListCollectionView)CollectionViewSource.GetDefaultView(_definition.Tracks);
            _tracksView.SortDescriptions.Add(new SortDescription(nameof(BoxSetTrack.TrackNumber), ListSortDirection.Ascending));
            _tracksView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BoxSetTrack.Date)));
            _tracksView.IsLiveGrouping = true;
            _tracksView.LiveGroupingProperties.Add(nameof(BoxSetTrack.Date));

            // Prefill the step-1 form from the loaded box (inverse of SyncStep1FieldsToDefinition).
            // New-box path leaves the fields blank. Tracks need no prefill — they bind live above.
            if (_isEditingExisting)
                LoadStep1FieldsFromDefinition();

            // Initial step. Edit-mode lands on step 2, but the shell drives that AFTER subscribing
            // StepChanged (a ctor-set step is not seen by the HeaderBar) — see GoToStep.
            ShowStep(1);

            // Capture the verification baseline (commit 3). Normalize first — SyncStep1FieldsToDefinition
            // trims the step-1 fields into _definition — so an untrimmed on-disk box does not
            // false-unverify on open-and-save with no user edit (spec refinement 3). Serialized to a
            // string, never a live reference (see _baselineJson). New-box mode: empty fields + empty
            // def, so the baseline is the normalized-empty form; both paths coherent.
            SyncStep1FieldsToDefinition();
            _baselineJson = BoxSetService.Serialize(_definition);
        }

        // ===== STEP NAVIGATION (called from HeaderBar via ShellWindow) =====

        /// <summary>Advances to the next step if the current step's validation passes.
        /// Step 1 has real validation; step 2 (track grid) and step 3 (the built Review summary)
        /// have no Next-gate, so advancing from them always passes.</summary>
        public void GoNext()
        {
            if (_currentStep == 1 && !ValidateStep1())
                return;

            if (_currentStep < 3)
                ShowStep(_currentStep + 1);
        }

        /// <summary>Moves back one step. No validation — back-navigation always allowed.</summary>
        public void GoBack()
        {
            if (_currentStep > 1)
                ShowStep(_currentStep - 1);
        }

        /// <summary>Jumps directly to a step (no validation). Used by the shell to land edit-mode
        /// on step 2 AFTER it has subscribed <see cref="StepChanged"/>, so the HeaderBar receives
        /// the step. The box being edited was already valid when saved; per-field validation still
        /// runs on Save.</summary>
        public void GoToStep(int step) => ShowStep(step);

        /// <summary>
        /// Validates step 1, checks for slug collision against existing definitions, and
        /// writes via <see cref="BoxSetService.Write"/>. On any failure surfaces the error
        /// (jumping back to step 1 so the user can see the in-form ValidationMessage) and
        /// does not raise Completed. On success raises Completed so ShellWindow navigates
        /// back to the list view, which reloads and shows the new definition.
        /// </summary>
        public void Save()
        {
            // Flush any in-progress cell edit so a half-typed value (the Date cell
            // especially) isn't dropped before validation/write.
            TracksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // Per-track Date validation: empty is allowed (date may not be known yet);
            // any non-empty malformed date blocks Save, naming the offending row(s).
            // Mirrors the step-1 date-block pattern.
            var badTracks = _definition.Tracks
                .Where(t => !string.IsNullOrEmpty(t.Date) && !DateRegex.IsMatch(t.Date))
                .ToList();
            if (badTracks.Count > 0)
            {
                var rows = string.Join(", ", badTracks.Select(t => $"#{t.TrackNumber}"));
                App.Alerts.Notify(
                    $"Track date(s) must be in yyyy-MM-dd format (leave blank if unknown).\n\nFix track(s): {rows}",
                    AlertSeverity.Warning, "Invalid Track Date");
                if (_currentStep != 2) ShowStep(2);
                return;
            }

            if (!ValidateStep1())
            {
                if (_currentStep != 1) ShowStep(1);
                return;
            }

            var slug = BoxSetService.DeriveSlug(_definition.Name);

            // Route the save through the pure identity resolver (BoxSetSaveResolution). For a new
            // box (_originalSlug null) the outcomes are SaveNew / NameCollision; in edit-mode they
            // are Overwrite (name unchanged), MoveRename (name changed to a free slug), or
            // NameCollision (the new slug belongs to a different box — refuse, don't clobber it).
            var outcome = BoxSetSaveResolution.Resolve(
                _originalSlug, slug, _boxSetService.Read(slug) != null);

            if (outcome == BoxSetSaveOutcome.NameCollision)
            {
                ValidationMessage.Text = "A box set with this name already exists. Please use a different name.";
                if (_currentStep != 1) ShowStep(1);
                return;
            }

            // Unverify-on-edit (commit 3, spec decision 4): if the box is verified and any tracked
            // field changed since the baseline snapshot, drop Verified before the write. One
            // serialized compare covers every edit path uniformly — step-1 fields, cell edits,
            // Add/Remove/Renumber/date-delete/pull-setlist. Runs after the flush + Sync (ValidateStep1
            // normalized step-1 into _definition) and immediately before Write, which carries Verified
            // through whole-object serialization. Mark-Verified rebaselines, so a just-verified box
            // diffs equal here and stays verified; a later edit re-dirties and unverifies.
            if (_definition.Verified &&
                EditUnverifyRule.IsDirty(_baselineJson, BoxSetService.Serialize(_definition)))
                _definition.Verified = false;

            try
            {
                // SaveNew / Overwrite / MoveRename all write the (whole) definition to the new
                // slug. Verified and every other field persist because the read-fresh definition
                // is serialized whole.
                _boxSetService.Write(_definition, slug);

                // Rename: the name changed to a free slug, so the box moved to a new file. Delete
                // the old file AFTER the new write succeeds — write-then-delete means a failed
                // delete leaves a recoverable orphan, whereas delete-first would risk data loss.
                if (outcome == BoxSetSaveOutcome.MoveRename)
                    _boxSetService.Delete(_originalSlug!);
            }
            catch (Exception ex)
            {
                // Pattern from EditSetlistView.xaml.cs — surface via App.Alerts.Notify,
                // do not raise Completed.
                App.Alerts.Notify($"Error saving box set:\n\n{ex.Message}", AlertSeverity.Error, "Save Failed");
                return;
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Discards the in-progress definition and signals Completed. No
        /// confirmation prompt (out of scope per the commit brief).</summary>
        public void Cancel()
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }

        // ===== STEP SWITCHING =====

        private void ShowStep(int step)
        {
            _currentStep = step;

            Step1Content.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Content.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Content.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

            StepIndicatorText.Text = step switch
            {
                1 => "Step 1 of 3 — Top-Level Info",
                2 => "Step 2 of 3 — Concerts and Tracks",
                3 => "Step 3 of 3 — Review",
                _ => $"Step {step} of 3"
            };

            // Populate the read-only Review summary whenever step 3 becomes active. The
            // Visibility-toggling and StepChanged logic above is unchanged.
            if (step == 3)
                PopulateReview();

            StepChanged?.Invoke(this, step);
        }

        // ===== STEP 3 — REVIEW SUMMARY (read-only) =====

        /// <summary>
        /// Populates the read-only Review panel from the current definition. Called by
        /// <see cref="ShowStep"/> when step 3 becomes active. Syncs the step-1 TextBoxes into
        /// <see cref="_definition"/> first so the summary reflects the latest edits even on a
        /// path that skipped validation; Sync only normalizes the current definition (no
        /// baseline/diff — that is commit 3). Rebuilds the per-date list on every call.
        /// </summary>
        private void PopulateReview()
        {
            SyncStep1FieldsToDefinition();

            ReviewNameText.Text = string.IsNullOrWhiteSpace(_definition.Name)
                ? "(untitled)"
                : _definition.Name;

            ReviewReleaseDateText.Text = string.IsNullOrWhiteSpace(_definition.ReleaseDate)
                ? "—"
                : _definition.ReleaseDate;

            // Label / Catalog: omit either if blank; hide the whole line if both blank.
            var label = _definition.Label?.Trim() ?? "";
            var catalog = _definition.CatalogNumber?.Trim() ?? "";
            if (label.Length == 0 && catalog.Length == 0)
            {
                ReviewLabelCatalogSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                ReviewLabelCatalogSection.Visibility = Visibility.Visible;
                ReviewLabelCatalogText.Text =
                    label.Length > 0 && catalog.Length > 0 ? $"{label} · {catalog}"
                    : label.Length > 0 ? label
                    : catalog;
            }

            PopulateReviewDates();

            // Notes: surfaced only when present.
            if (string.IsNullOrWhiteSpace(_definition.Notes))
            {
                ReviewNotesSection.Visibility = Visibility.Collapsed;
                ReviewNotesText.Text = "";
            }
            else
            {
                ReviewNotesSection.Visibility = Visibility.Visible;
                ReviewNotesText.Text = _definition.Notes;
            }

            RefreshVerifyControls();
        }

        // ===== STEP 3 — VERIFICATION SURFACE (commit 3) =====

        /// <summary>
        /// Refreshes the verify badge + Mark-Verified button + reason in <c>VerifyActionSlot</c>
        /// from the current definition. Called on panel-show (from <see cref="PopulateReview"/>,
        /// after its Sync) and by the Mark-Verified handler. A one-shot evaluation, not live
        /// tracking — it stays inside the "no amber markers" deferral (spec refinement 5).
        /// <para>
        /// <c>effectiveVerified</c> is the HONEST state: the stored bool AND the definition being
        /// clean vs. the baseline (the same diff the Save uses). A verified box that has been edited
        /// shows as not-verified here, which is the truth. Three states:
        /// verified -> green badge, button hidden (you unverify by editing), no reason;
        /// unverified + complete -> grey badge, enabled "Mark Verified", no reason;
        /// unverified + incomplete -> grey badge, disabled button, first failing reason shown.
        /// </para>
        /// </summary>
        private void RefreshVerifyControls()
        {
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(_definition);
            bool effectiveVerified = _definition.Verified
                && !EditUnverifyRule.IsDirty(_baselineJson, BoxSetService.Serialize(_definition));

            if (effectiveVerified)
            {
                VerifyBadge.Background = (SolidColorBrush)FindResource("BadgeVerifiedBg");
                VerifyBadgeText.Text = "✓ Verified";
                VerifyBadgeText.Foreground = (SolidColorBrush)FindResource("BadgeVerifiedFg");
                MarkVerifiedButton.Visibility = Visibility.Collapsed;
                VerifyReasonText.Visibility = Visibility.Collapsed;
                return;
            }

            VerifyBadge.Background = (SolidColorBrush)FindResource("BadgeUnverifiedBg");
            VerifyBadgeText.Text = "Unverified";
            VerifyBadgeText.Foreground = (SolidColorBrush)FindResource("BadgeUnverifiedFg");
            MarkVerifiedButton.Visibility = Visibility.Visible;
            MarkVerifiedButton.IsEnabled = canVerify;

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
        /// Marks the box verified in memory (Option 1: persist-on-Save). Re-checks the gate
        /// defensively (the button is disabled when it fails, but a stale click must not verify an
        /// incomplete box), sets <c>Verified = true</c>, and rebaselines so the same-pass Save diff
        /// sees baseline == current and Verified persists. No disk write here — the HeaderBar Save
        /// writes; Cancel discards. A subsequent edit re-dirties the baseline and unverifies at Save.
        /// </summary>
        private void MarkVerifiedButton_Click(object sender, RoutedEventArgs e)
        {
            var (canVerify, _) = BoxSetVerifyGate.Evaluate(_definition);
            if (!canVerify) return;

            _definition.Verified = true;
            _baselineJson = BoxSetService.Serialize(_definition);
            RefreshVerifyControls();
        }

        /// <summary>
        /// Rebuilds the per-date track summary: a count lead line plus one read-only line per
        /// date, each formatted by <see cref="BoxSetGroupHeader.FormatGroupHeader"/> with the
        /// venue derived from <see cref="ShowLookupService"/> — the same path Step 2's group
        /// headers use (<c>GroupHeaderConverter</c>). No-date tracks fold into a single
        /// "(no date)" line; dates are ordered chronologically with no-date last. Zero tracks
        /// renders "0 tracks" with no per-date lines.
        /// </summary>
        private void PopulateReviewDates()
        {
            ReviewDatesPanel.Children.Clear();

            var tracks = _definition.Tracks;
            int total = tracks.Count;

            if (total == 0)
            {
                ReviewTrackCountText.Text = "0 tracks";
                return;
            }

            var groups = tracks
                .GroupBy(t => t.Date ?? "")
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(g => string.IsNullOrWhiteSpace(g.Date) ? 1 : 0)
                .ThenBy(g => g.Date, StringComparer.Ordinal)
                .ToList();

            int dateCount = groups.Count(g => !string.IsNullOrWhiteSpace(g.Date));

            ReviewTrackCountText.Text =
                $"{total} {(total == 1 ? "track" : "tracks")} across " +
                $"{dateCount} {(dateCount == 1 ? "date" : "dates")}";

            var lineBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
            foreach (var g in groups)
            {
                string? venue = ShowLookupService.Instance.GetShowByDate(g.Date)?.FormattedVenueLocation;
                ReviewDatesPanel.Children.Add(new TextBlock
                {
                    Text = BoxSetGroupHeader.FormatGroupHeader(g.Date, venue, g.Count),
                    Foreground = lineBrush,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
        }

        // ===== STEP 1 VALIDATION =====

        /// <summary>
        /// Reads step-1 field text into <see cref="_definition"/> and validates required
        /// fields + formats. Returns true on success; on failure sets
        /// <see cref="ValidationMessage"/> to the first error message and returns false.
        /// Reads on demand rather than via per-field LostFocus handlers (matches
        /// EditSetlistView.xaml.cs:212-219's read-on-save pattern).
        /// </summary>
        private bool ValidateStep1()
        {
            SyncStep1FieldsToDefinition();
            ValidationMessage.Text = "";

            if (string.IsNullOrWhiteSpace(_definition.Name))
            {
                ValidationMessage.Text = "Name is required.";
                return false;
            }

            if (!DateRegex.IsMatch(_definition.ReleaseDate))
            {
                ValidationMessage.Text = "Release Date must be in yyyy-MM-dd format.";
                return false;
            }

            return true;
        }

        /// <summary>Copies step-1 TextBox values into <see cref="_definition"/>. Trimming
        /// applied so trailing whitespace doesn't leak into the slug or saved JSON.</summary>
        private void SyncStep1FieldsToDefinition()
        {
            _definition.Name = NameTextBox.Text?.Trim() ?? "";
            _definition.ReleaseDate = ReleaseDateTextBox.Text?.Trim() ?? "";
            _definition.Label = LabelTextBox.Text?.Trim() ?? "";
            _definition.CatalogNumber = CatalogNumberTextBox.Text?.Trim() ?? "";
            _definition.Notes = NotesTextBox.Text?.Trim() ?? "";
        }

        /// <summary>Populates the step-1 TextBoxes from <see cref="_definition"/> — the exact
        /// inverse of <see cref="SyncStep1FieldsToDefinition"/>. Called once at construction in
        /// edit-mode. Verified and the track list are not step-1 fields: Verified rides through
        /// untouched on the read-fresh definition and is persisted whole by Save; tracks bind live.</summary>
        private void LoadStep1FieldsFromDefinition()
        {
            NameTextBox.Text = _definition.Name;
            ReleaseDateTextBox.Text = _definition.ReleaseDate;
            LabelTextBox.Text = _definition.Label;
            CatalogNumberTextBox.Text = _definition.CatalogNumber;
            NotesTextBox.Text = _definition.Notes;
        }

        // ===== STEP 2 — TRACK GRID (add / delete) =====

        /// <summary>Appends a blank track. TrackNumber defaults to the next sequential value
        /// (max existing + 1; 1 for the first row). Date is left blank — it is the
        /// performance date, not the release date, and is unknown until the curator enters
        /// it. The new row is selected and scrolled into view for immediate entry.</summary>
        private void AddTrackButton_Click(object sender, RoutedEventArgs e)
        {
            var nextNumber = _definition.Tracks.Count == 0
                ? 1
                : _definition.Tracks.Max(t => t.TrackNumber) + 1;

            var track = new BoxSetTrack
            {
                TrackNumber = nextNumber,
                SongName = "",
                Date = "",
                SegueOut = false
            };
            _definition.Tracks.Add(track);

            // Make the new row's group active so it auto-expands (commit H2). The blank-date
            // row lands in the "(no date)" group, whose key is "" — a deliberate empty string,
            // distinct from null (see ActiveGroupDate). Set before Refresh so the container
            // realizes expanded.
            ActiveGroupDate = track.Date;

            // Refresh the grouped view after mutating the backing List (List<T> raises no
            // collection-changed notification; reassigning ItemsSource would drop the
            // group/sort config — see ctor).
            _tracksView.Refresh();

            TracksDataGrid.UpdateLayout();
            SelectAndRevealAppended(track);
        }

        /// <summary>Reads the gdshowsdb reference setlist for the entered date and appends
        /// one track per song to <see cref="_definition"/>'s track list. Append-only and
        /// non-destructive: no dedup, existing rows never cleared or renumbered. Silent on
        /// success (the appended rows are the feedback); messages only on an invalid date or
        /// when no setlist exists for the date.</summary>
        private void PullSetlistButton_Click(object sender, RoutedEventArgs e)
        {
            var date = PullDateTextBox.Text.Trim();

            if (!DateRegex.IsMatch(date))
            {
                App.Alerts.Notify("Enter a date as yyyy-MM-dd.", AlertSeverity.Warning, "Invalid Date");
                return;
            }

            var sets = ShowLookupService.Instance.GetSetlist(date);
            if (sets == null)
            {
                App.Alerts.Notify($"No setlist found for {date}.", AlertSeverity.Info, "No Setlist");
                return;
            }

            // Same-date collision: pull is no longer unconditionally append-only. If the target
            // date already has rows, ask the curator (Replace / Append / Cancel). Replace clears
            // that date's rows here — BEFORE the startNumber max below, so the removed rows are
            // not counted and numbering resumes from the remaining max+1 (the accepted
            // non-renumber behavior). Append falls through unchanged; Cancel aborts untouched.
            // No prompt when the date has no existing tracks — pull behaves exactly as before.
            if (BoxSetPullCollision.HasTracksForDate(_definition.Tracks, date))
            {
                var existingCount = _definition.Tracks.Count(t => t.Date == date);
                var dialog = new PullCollisionDialog(date, existingCount)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();

                if (dialog.Result == PullCollisionAction.Cancel)
                    return;

                if (dialog.Result == PullCollisionAction.Replace)
                    _definition.Tracks.RemoveAll(t => t.Date == date);
            }

            int startNumber = _definition.Tracks.Count == 0
                ? 1
                : _definition.Tracks.Max(t => t.TrackNumber) + 1;

            var newTracks = SetlistTrackBuilder.BuildTracksFromSetlist(sets, date, startNumber);
            _definition.Tracks.AddRange(newTracks);

            // Make the pulled date the active group so it auto-expands (commit H2). Set before
            // Refresh so the (possibly new) container realizes expanded.
            ActiveGroupDate = date;

            // Refresh the grouped view once after the batch (see ctor: Refresh replaces the
            // old null-rebind so the group/sort config survives).
            _tracksView.Refresh();

            TracksDataGrid.UpdateLayout();
            var first = newTracks.FirstOrDefault();
            if (first != null)
                SelectAndRevealAppended(first);
        }

        /// <summary>Del removes the selected rows when the grid is not mid-edit (so Delete
        /// inside a cell edit doesn't nuke the row). TrackNumbers are NOT renumbered — they
        /// are curator data matching the physical release, not a display ordinal.</summary>
        private void TracksDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Delete && !TracksDataGrid.IsEditing())
            {
                var selected = TracksDataGrid.SelectedItems.OfType<BoxSetTrack>().ToList();
                if (selected.Count == 0) return;

                // Keep the group being worked in expanded after the delete (commit H2 — the
                // original rough edge: Refresh used to re-collapse it). Anchor on the last
                // selected row's date; a multi-row delete that spans dates keeps the last
                // row's group open (one group is active at a time by design).
                ActiveGroupDate = selected[selected.Count - 1].Date;

                foreach (var track in selected)
                    _definition.Tracks.Remove(track);

                _tracksView.Refresh();
                e.Handled = true;
            }
        }

        /// <summary>Removes every track for one date in a single action via the ✕ on that
        /// date's group header (commit H3) — the whole-date analog of the per-row Del above.
        /// The date comes from the clicked header's <see cref="CollectionViewGroup"/> context
        /// (proven reachable by the header's IsExpanded MultiBinding). A Yes/No confirm guards
        /// the destructive single click (per-row delete needs no confirm because it's a
        /// deliberate multi-select gesture). Accordion rule: collapse to all-collapsed only
        /// when the removed date was the open group; deleting a different (collapsed) group
        /// leaves the open one expanded. Set ActiveGroupDate BEFORE the single Refresh, per the
        /// accordion contract. TrackNumbers are not renumbered (matches per-row delete).</summary>
        private async void RemoveDateButton_Click(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as CollectionViewGroup;
            string date = group?.Name as string ?? "";

            int count = _definition.Tracks.Count(
                t => string.Equals(t?.Date ?? "", date, StringComparison.Ordinal));
            if (count == 0) return; // defensive; a visible group always has >=1

            // e.Handled moved AHEAD of the await (was set at the end of the confirmed path): the
            // confirm is now async, so the Click event finishes bubbling at the await — marking it
            // handled afterward would be a no-op. Setting it synchronously here preserves the
            // intent (the ✕ click is ours). FLAG: the old code left the click unhandled on the
            // No/count==0 paths; it is now handled on every path past the count guard.
            e.Handled = true;

            // [Q1 CONFIRM BLOCK] -- remove this block for no-confirm parity with per-row delete
            // Bucket-B confirm (alert-system-spec.md #21). Branch mapping preserved from the old
            // YesNo/Warning MessageBox: Yes (true) -> remove; No/Esc (false) -> return.
            string label = string.IsNullOrWhiteSpace(date) ? "(no date)" : date;
            bool remove = await App.Alerts.ConfirmAsync(
                $"Remove all {count} track(s) for {label}?",
                "Remove date");
            if (!remove) return;
            // [/Q1 CONFIRM BLOCK]

            // Q2 conditional active-group rule: only collapse if we deleted the open group.
            if (string.Equals(ActiveGroupDate, date, StringComparison.Ordinal))
                ActiveGroupDate = null;

            BoxSetTrackMutations.RemoveTracksForDate(_definition.Tracks, date);
            _tracksView.Refresh();
        }

        /// <summary>Re-sequences every track's TrackNumber to a contiguous 1..N ordered by
        /// (Date asc, then existing TrackNumber asc), with no-date tracks last (the Renumber
        /// action). Fixes out-of-order pulls — a later-pulled date no longer keeps lower numbers
        /// than an earlier date — and closes gaps left by deletes, without delete-and-reimport.
        /// Non-destructive (no row added/removed), so no confirm prompt. Dates are unchanged, so
        /// the accordion's active group stays valid and needs no ActiveGroupDate touch; a single
        /// Refresh re-renders the groups in the new order. TrackNumber re-sorts on Refresh.</summary>
        private void RenumberButton_Click(object sender, RoutedEventArgs e)
        {
            BoxSetTrackMutations.RenumberByDate(_definition.Tracks);
            _tracksView.Refresh();
        }

        /// <summary>Toggles date grouping on the shared view. Grouped (checked) adds the Date
        /// <see cref="PropertyGroupDescription"/>; flat (unchecked) clears it. The TrackNumber
        /// <see cref="SortDescription"/> stays in both modes, so the flat list is still
        /// TrackNumber-ordered.</summary>
        private void GroupByDateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Fires during InitializeComponent (IsChecked="True" in XAML) before the ctor
            // configures the view — ignore until the view exists; the ctor sets up grouping.
            if (_tracksView == null) return;

            _tracksView.GroupDescriptions.Clear();
            if (GroupByDateCheckBox.IsChecked == true)
                _tracksView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BoxSetTrack.Date)));

            _tracksView.Refresh();
        }

        /// <summary>Selects a freshly appended row and scrolls it into view. The row's group is
        /// now auto-expanded (the caller sets <see cref="ActiveGroupDate"/> before Refresh), so
        /// in grouped mode the row is reachable too — H2 lifts H's flat-only restriction. The
        /// scroll is deferred to <c>Background</c> priority because
        /// <see cref="DataGrid.ScrollIntoView"/> into a just-expanded, virtualized group is
        /// unreliable synchronously (the group's rows may not be realized yet); in flat mode the
        /// deferral is harmless.</summary>
        private void SelectAndRevealAppended(BoxSetTrack track)
        {
            TracksDataGrid.SelectedItem = track;
            Dispatcher.BeginInvoke(new Action(() => TracksDataGrid.ScrollIntoView(track)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>When a Date cell commits, make the row's (post-regroup) date the active
        /// group so it stays expanded and visible after the live-regroup (commit H2). Hooked
        /// via <c>CellEditEnding</c> rather than per-track <c>PropertyChanged</c> — no
        /// subscription lifecycle to manage on the plain backing <c>List</c>. The work is
        /// deferred to <c>Background</c> priority because at <c>CellEditEnding</c> the edited
        /// text has not yet been pushed to <see cref="BoxSetTrack.Date"/> (nor has live-grouping
        /// moved the row); reading <c>track.Date</c> after commit is reliable.</summary>
        private void TracksDataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Column != DateColumn) return;
            if (e.Row.Item is not BoxSetTrack track) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ActiveGroupDate = track.Date;
                TracksDataGrid.ScrollIntoView(track);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
