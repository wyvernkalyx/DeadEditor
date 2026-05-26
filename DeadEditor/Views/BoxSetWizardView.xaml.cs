using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace DeadEditor
{
    /// <summary>
    /// Multi-step wizard for authoring a <see cref="BoxSetDefinition"/>. Single
    /// UserControl; step transitions toggle <c>Visibility</c> on four content panels
    /// rather than navigating between views. Step 1 (top-level info) ships in this
    /// commit; steps 2-4 are placeholders.
    ///
    /// Shell-agnostic by design: raises <see cref="Completed"/> (Save success or
    /// Cancel) and <see cref="StepChanged"/> for the ShellWindow to plumb back to
    /// navigation and the HeaderBar's Back/Next/Save button state.
    /// </summary>
    public partial class BoxSetWizardView : System.Windows.Controls.UserControl
    {
        private readonly BoxSetService _boxSetService;
        private readonly NormalizationService _normalizationService = new();
        private readonly BoxSetDefinition _definition = new();
        private int _currentStep = 1;

        // Step-2 state: editing VMs (separate from the underlying model).
        // _definition.Concerts is rebuilt from these in SyncConcertsToDefinition() at
        // Save time, matching EditSetlistView's "build models from EditableTracks" idiom
        // (EditSetlistView.xaml.cs:222-262). Keeps the data models plain (no INPC per
        // commit 1's directive) while still getting INPC-driven list refresh in the UI.
        private readonly ObservableCollection<BoxSetConcertVm> _concertVms = new();
        private BoxSetConcertVm? _selectedConcertVm;

        // Step-3 state: disc shells are pre-created from _definition.DiscCount the first
        // time step 3 is entered. Re-entries extend (option (c) in the brief) but never
        // shrink, so the user's disc data is preserved if step-1 DiscCount is later reduced.
        private readonly ObservableCollection<BoxSetDiscVm> _discVms = new();
        private int _selectedDiscIndex;

        /// <summary>Concert options for the step-3 track grid's Concert dropdown. Bound from
        /// the ComboBox via <c>{RelativeSource AncestorType=UserControl}</c> so the per-row
        /// VM doesn't need a reference to the wizard. Rebuilt on each step-3 entry from the
        /// current <see cref="_concertVms"/>.</summary>
        public ObservableCollection<ConcertOption> ConcertOptions { get; } = new();

        // Step-4 state: review/validation surface. Rebuilt fresh on every step-4 entry.
        private readonly ObservableCollection<ValidationIssue> _validationIssues = new();
        private readonly ObservableCollection<ReviewDiscRow> _reviewDiscs = new();
        private bool _hasBlockingIssues;

        /// <summary>True when step-4 validation found at least one hard-block issue.
        /// Read by ShellWindow on <see cref="StepChanged"/> to disable the header's Save
        /// button. Default false — only meaningful after <see cref="InitializeStep4"/> has
        /// run, but the Save button is only visible (content="Save") on step 4 anyway.</summary>
        public bool IsSaveBlocked => _hasBlockingIssues;

        // yyyy-MM-dd validation regex — same pattern as EditSetlistView.xaml.cs:197.
        private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

        /// <summary>The currently visible step (1-4).</summary>
        public int CurrentStep => _currentStep;

        /// <summary>Fired when the wizard finishes — either Save succeeded or the user
        /// cancelled. ShellWindow uses this to navigate back to the Box Sets list.</summary>
        public event EventHandler? Completed;

        /// <summary>Fired whenever the visible step changes. ShellWindow forwards the new
        /// step to <c>HeaderBar.UpdateBoxSetWizardStep</c> so Back/Next/Save buttons update.</summary>
        public event EventHandler<int>? StepChanged;

        public BoxSetWizardView(BoxSetService boxSetService)
        {
            InitializeComponent();
            _boxSetService = boxSetService;
            ConcertListBox.ItemsSource = _concertVms;
            ValidationItemsControl.ItemsSource = _validationIssues;
            ReviewDiscsItemsControl.ItemsSource = _reviewDiscs;
            ShowStep(1);
        }

        // ===== STEP NAVIGATION (called from HeaderBar via ShellWindow) =====

        /// <summary>Advances to the next step if the current step's validation passes.
        /// Step 1 has real validation; later steps' content is a placeholder and always
        /// passes.</summary>
        public void GoNext()
        {
            if (_currentStep == 1 && !ValidateStep1())
                return;

            if (_currentStep < 4)
                ShowStep(_currentStep + 1);
        }

        /// <summary>Moves back one step. No validation — back-navigation always allowed.</summary>
        public void GoBack()
        {
            if (_currentStep > 1)
                ShowStep(_currentStep - 1);
        }

        /// <summary>
        /// Validates step 1, checks for slug collision against existing definitions, and
        /// writes via <see cref="BoxSetService.Write"/>. On any failure surfaces the error
        /// (jumping back to step 1 so the user can see the in-form ValidationMessage) and
        /// does not raise Completed. On success raises Completed so ShellWindow navigates
        /// back to the list view, which reloads and shows the new definition.
        /// </summary>
        public void Save()
        {
            // Defensive: the header Save button is disabled when _hasBlockingIssues is true
            // (see HeaderBar.UpdateBoxSetWizardSaveEnabled), but guard anyway in case the
            // method is reached programmatically.
            if (_hasBlockingIssues) return;

            if (!ValidateStep1())
            {
                if (_currentStep != 1) ShowStep(1);
                return;
            }

            var slug = BoxSetService.DeriveSlug(_definition.Name);

            // Collision check — Write would silently overwrite. Surface as a validation
            // error so the user picks a different name. Edit-existing (commit 6) will use
            // a different code path that does want to overwrite.
            if (_boxSetService.Read(slug) != null)
            {
                ValidationMessage.Text = "A box set with this name already exists. Please use a different name.";
                if (_currentStep != 1) ShowStep(1);
                return;
            }

            // Soft-warning confirmation: step 4's amber-state validation issues (incomplete
            // tracks, disc-count mismatch, concerts missing date/venue) are non-blocking
            // but worth confirming before write.
            var warnings = _validationIssues
                .Where(i => i.Severity == ValidationSeverity.Warning)
                .ToList();
            if (warnings.Count > 0)
            {
                var summary = string.Join("\n", warnings.Select(w => "• " + w.Message));
                var result = MessageBox.Show(
                    $"There are validation warnings:\n\n{summary}\n\nSave anyway?",
                    "Save with warnings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            // Rebuild _definition.Concerts + _definition.Discs from the step-2/3 VMs.
            SyncWizardToDefinition();

            try
            {
                _boxSetService.Write(_definition, slug);
            }
            catch (Exception ex)
            {
                // Pattern from EditSetlistView.xaml.cs:296-300 — surface as MessageBox,
                // do not raise Completed.
                MessageBox.Show($"Error saving box set:\n\n{ex.Message}", "Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            Step4Content.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

            if (step == 3) InitializeStep3();
            if (step == 4) InitializeStep4();

            StepIndicatorText.Text = step switch
            {
                1 => "Step 1 of 4 — Top-Level Info",
                2 => "Step 2 of 4 — Concerts",
                3 => "Step 3 of 4 — Discs",
                4 => "Step 4 of 4 — Review",
                _ => $"Step {step} of 4"
            };

            StepChanged?.Invoke(this, step);
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

            if (_definition.DiscCount < 1 || _definition.DiscCount > 99)
            {
                ValidationMessage.Text = "Disc Count must be between 1 and 99.";
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

            // int.TryParse leaves the out value at 0 on failure, which is intentionally
            // out-of-range so validation catches it as "must be between 1 and 99."
            _ = int.TryParse(DiscCountTextBox.Text?.Trim(), out var dc);
            _definition.DiscCount = dc;
        }

        // ===== STEP 2 — CONCERTS =====

        private void AddConcertButton_Click(object sender, RoutedEventArgs e)
        {
            // Country default matches the existing concert-data convention. User-overridable;
            // not a hardcoded artist locale.
            var vm = new BoxSetConcertVm { Country = "USA" };
            _concertVms.Add(vm);
            ConcertListBox.SelectedItem = vm;
        }

        private void RemoveConcertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConcertVm == null) return;
            var label = string.IsNullOrEmpty(_selectedConcertVm.Date) ? "(unsaved)" : _selectedConcertVm.Date;
            var result = MessageBox.Show($"Remove concert {label}?", "Remove Concert",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _concertVms.Remove(_selectedConcertVm);
        }

        private void ConcertListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedConcertVm = ConcertListBox.SelectedItem as BoxSetConcertVm;
            if (_selectedConcertVm == null)
            {
                ConcertDetailScroller.Visibility = Visibility.Collapsed;
                NoConcertSelectedText.Visibility = Visibility.Visible;
                ConcertDetailPanel.DataContext = null;
            }
            else
            {
                ConcertDetailPanel.DataContext = _selectedConcertVm;
                ConcertDetailScroller.Visibility = Visibility.Visible;
                NoConcertSelectedText.Visibility = Visibility.Collapsed;
            }
        }

        private void AddSetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConcertVm == null) return;
            // Default label: next sequential integer ("1", "2", "3", ...). User can rename.
            var nextIndex = _selectedConcertVm.Setlist.Count + 1;
            _selectedConcertVm.Setlist.Add(new BoxSetSetVm { Set = nextIndex.ToString() });
        }

        private void RemoveSetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetSetVm setVm && _selectedConcertVm != null)
            {
                var label = string.IsNullOrEmpty(setVm.Set) ? "(unnamed)" : setVm.Set;
                var result = MessageBox.Show($"Remove set \"{label}\" and all its songs?", "Remove Set",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
                _selectedConcertVm.Setlist.Remove(setVm);
            }
        }

        /// <summary>
        /// Per-set Normalize. Walks every song in the set and replaces its name with the
        /// canonical form returned by NormalizationService, matching the iteration pattern
        /// in EditSetlistView.xaml.cs:163-184 (NormalizeButton_Click).
        /// </summary>
        private void NormalizeSetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetSetVm setVm)
            {
                foreach (var songVm in setVm.Songs)
                {
                    if (string.IsNullOrWhiteSpace(songVm.Name)) continue;
                    var normalized = _normalizationService.Normalize(songVm.Name);
                    if (!string.IsNullOrEmpty(normalized) && normalized != songVm.Name)
                    {
                        songVm.Name = normalized;
                    }
                }
            }
        }

        private void AddSongButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetSetVm setVm)
            {
                setVm.Songs.Add(new BoxSetSetSongVm());
            }
        }

        private void RemoveSongButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetSetSongVm song && _selectedConcertVm != null)
            {
                foreach (var setVm in _selectedConcertVm.Setlist)
                {
                    if (setVm.Songs.Remove(song)) return;
                }
            }
        }

        private void MoveSongUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetSetSongVm song && _selectedConcertVm != null)
            {
                foreach (var setVm in _selectedConcertVm.Setlist)
                {
                    var idx = setVm.Songs.IndexOf(song);
                    if (idx >= 0)
                    {
                        if (idx > 0) setVm.Songs.Move(idx, idx - 1);
                        return;
                    }
                }
            }
        }

        private void MoveSongDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetSetSongVm song && _selectedConcertVm != null)
            {
                foreach (var setVm in _selectedConcertVm.Setlist)
                {
                    var idx = setVm.Songs.IndexOf(song);
                    if (idx >= 0)
                    {
                        if (idx < setVm.Songs.Count - 1) setVm.Songs.Move(idx, idx + 1);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Rebuilds <see cref="_definition"/>.<c>Concerts</c> and <c>Discs</c> from
        /// <see cref="_concertVms"/> and <see cref="_discVms"/> just before
        /// <see cref="BoxSetService.Write"/>. Step-1 fields are already synced by
        /// <see cref="ValidateStep1"/>. The model stays plain (no INPC, no ObservableCollection);
        /// the wizard's UI lives entirely in VMs.
        /// </summary>
        private void SyncWizardToDefinition()
        {
            _definition.Concerts.Clear();
            foreach (var vm in _concertVms)
                _definition.Concerts.Add(vm.ToModel());

            _definition.Discs.Clear();
            foreach (var vm in _discVms)
                _definition.Discs.Add(vm.ToModel());
        }

        // ===== STEP 3 — DISCS =====

        /// <summary>
        /// Called from <see cref="ShowStep"/> whenever step 3 becomes visible. Always
        /// rebuilds the concert dropdown options (step 2 may have changed). Extends the
        /// disc shells per the brief's option (c): grows to match <c>_definition.DiscCount</c>
        /// but never shrinks, so user-entered disc data survives a step-1 DiscCount decrease.
        /// </summary>
        private void InitializeStep3()
        {
            // Rebuild concert dropdown options from the current step-2 state. The Id is the
            // concert's Date (matching BoxSetConcertVm.ToModel() which writes Id = Date).
            ConcertOptions.Clear();
            foreach (var c in _concertVms)
            {
                var label = string.IsNullOrEmpty(c.Date) && string.IsNullOrEmpty(c.Venue)
                    ? "(unconfigured concert)"
                    : $"{(string.IsNullOrEmpty(c.Date) ? "(no date)" : c.Date)} — {(string.IsNullOrEmpty(c.Venue) ? "(no venue)" : c.Venue)}";
                ConcertOptions.Add(new ConcertOption { Id = c.Date, DisplayLabel = label });
            }

            // Pre-create or extend disc shells. Append-only — never shrink.
            while (_discVms.Count < _definition.DiscCount)
            {
                _discVms.Add(new BoxSetDiscVm { DiscNumber = _discVms.Count + 1 });
            }

            // Clamp the selected index in case the list shrank in a previous session
            // (defensive — we don't currently shrink).
            if (_selectedDiscIndex >= _discVms.Count)
                _selectedDiscIndex = Math.Max(0, _discVms.Count - 1);

            UpdateDiscSelector();
        }

        /// <summary>
        /// Refreshes the disc-selector indicator, button enable states, and the per-disc
        /// bindings (<see cref="DiscNameTextBox"/>'s DataContext and the
        /// <see cref="TrackGrid"/>'s ItemsSource).
        /// </summary>
        private void UpdateDiscSelector()
        {
            var count = _discVms.Count;
            var hasDiscs = count > 0;

            if (hasDiscs)
            {
                if (_selectedDiscIndex < 0) _selectedDiscIndex = 0;
                if (_selectedDiscIndex >= count) _selectedDiscIndex = count - 1;
                var current = _discVms[_selectedDiscIndex];

                DiscSelectorText.Text = $"Disc {_selectedDiscIndex + 1} of {count}";
                DiscNameTextBox.DataContext = current;
                TrackGrid.ItemsSource = current.Tracks;
            }
            else
            {
                DiscSelectorText.Text = "No discs";
                DiscNameTextBox.DataContext = null;
                TrackGrid.ItemsSource = null;
            }

            PrevDiscButton.IsEnabled = hasDiscs && _selectedDiscIndex > 0;
            NextDiscButton.IsEnabled = hasDiscs && _selectedDiscIndex < count - 1;
            RemoveDiscButton.IsEnabled = hasDiscs;
            DiscNameTextBox.IsEnabled = hasDiscs;
            AddTrackButton.IsEnabled = hasDiscs;

            UpdateTrackEmptyState();
        }

        /// <summary>
        /// Shows the empty-state TextBlock overlaying the track grid when the current disc
        /// has zero tracks; hides it when tracks exist or when there are no discs at all
        /// (in the latter case the disc selector itself reads "No discs", so a second
        /// message is redundant).
        /// </summary>
        private void UpdateTrackEmptyState()
        {
            if (_discVms.Count == 0)
            {
                TrackEmptyState.Visibility = Visibility.Collapsed;
                return;
            }
            var current = _discVms[_selectedDiscIndex];
            TrackEmptyState.Visibility = current.Tracks.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PrevDiscButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDiscIndex > 0)
            {
                _selectedDiscIndex--;
                UpdateDiscSelector();
            }
        }

        private void NextDiscButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDiscIndex < _discVms.Count - 1)
            {
                _selectedDiscIndex++;
                UpdateDiscSelector();
            }
        }

        private void AddDiscButton_Click(object sender, RoutedEventArgs e)
        {
            var newDisc = new BoxSetDiscVm { DiscNumber = _discVms.Count + 1 };
            _discVms.Add(newDisc);
            _selectedDiscIndex = _discVms.Count - 1;
            UpdateDiscSelector();
        }

        private void RemoveDiscButton_Click(object sender, RoutedEventArgs e)
        {
            if (_discVms.Count == 0) return;
            var current = _discVms[_selectedDiscIndex];
            var trackCountNote = current.Tracks.Count > 0 ? $" and its {current.Tracks.Count} track(s)" : "";
            var result = MessageBox.Show($"Remove Disc {current.DiscNumber}{trackCountNote}?",
                "Remove Disc", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _discVms.RemoveAt(_selectedDiscIndex);
            UpdateDiscSelector();
        }

        /// <summary>
        /// Appends a new track to the currently-selected disc. TrackNumber auto-fills:
        /// last-track + 1 if the disc has tracks, otherwise <c>DiscNumber * 100 + 1</c>
        /// (Disc 1 → 101, Disc 5 → 501, Disc 20 → 2001 — matches the disc-prefixed
        /// encoding from the design memo).
        /// </summary>
        private void AddTrackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_discVms.Count == 0) return;
            var disc = _discVms[_selectedDiscIndex];

            int nextNumber = disc.Tracks.Count == 0
                ? disc.DiscNumber * 100 + 1
                : disc.Tracks[disc.Tracks.Count - 1].TrackNumber + 1;

            disc.Tracks.Add(new BoxSetDiscTrackVm { TrackNumber = nextNumber });
            UpdateTrackEmptyState();
        }

        private void MoveTrackUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetDiscTrackVm trackVm && _discVms.Count > 0)
            {
                var disc = _discVms[_selectedDiscIndex];
                var idx = disc.Tracks.IndexOf(trackVm);
                if (idx > 0) disc.Tracks.Move(idx, idx - 1);
            }
        }

        private void MoveTrackDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BoxSetDiscTrackVm trackVm && _discVms.Count > 0)
            {
                var disc = _discVms[_selectedDiscIndex];
                var idx = disc.Tracks.IndexOf(trackVm);
                if (idx >= 0 && idx < disc.Tracks.Count - 1) disc.Tracks.Move(idx, idx + 1);
            }
        }

        private void RemoveTrackButton_Click(object sender, RoutedEventArgs e)
        {
            // No confirm — single-track removal is low-stakes and the ✕ button is
            // intentional. Matches the song-row remove behavior in step 2.
            if (sender is Button btn && btn.Tag is BoxSetDiscTrackVm trackVm && _discVms.Count > 0)
            {
                _discVms[_selectedDiscIndex].Tracks.Remove(trackVm);
                UpdateTrackEmptyState();
            }
        }

        // ===== STEP 4 — REVIEW =====

        /// <summary>
        /// Called from <see cref="ShowStep"/> whenever step 4 becomes visible. Re-runs
        /// every entry (Back-and-Next traversals could have changed data). Builds the
        /// summary card, runs validation, and rebuilds the color-coded track list.
        /// </summary>
        private void InitializeStep4()
        {
            BuildSummary();
            BuildValidation();
            BuildReviewDiscs();
        }

        private void BuildSummary()
        {
            SummaryNameText.Text = string.IsNullOrEmpty(_definition.Name)
                ? "(unnamed box set)"
                : _definition.Name;

            var releaseParts = new List<string>();
            if (!string.IsNullOrEmpty(_definition.ReleaseDate)) releaseParts.Add(_definition.ReleaseDate);
            if (!string.IsNullOrEmpty(_definition.Label)) releaseParts.Add(_definition.Label);
            if (!string.IsNullOrEmpty(_definition.CatalogNumber)) releaseParts.Add(_definition.CatalogNumber);
            SummaryReleaseInfoText.Text = releaseParts.Count == 0
                ? "(no release info)"
                : string.Join(" · ", releaseParts);

            var concertCount = _concertVms.Count;
            SummaryConcertsText.Text = concertCount == 0
                ? "0 concerts — none added"
                : $"{concertCount} concert{(concertCount == 1 ? "" : "s")}";

            var discCount = _discVms.Count;
            var trackCount = _discVms.Sum(d => d.Tracks.Count);
            var discsInfo = _definition.DiscCount == discCount
                ? $"{discCount} disc{(discCount == 1 ? "" : "s")}"
                : $"{_definition.DiscCount} declared, {discCount} entered";
            SummaryDiscsText.Text = $"{discsInfo} · {trackCount} track{(trackCount == 1 ? "" : "s")}";
        }

        /// <summary>
        /// Runs the three-state validation rules, populates <see cref="_validationIssues"/>,
        /// sets <see cref="_hasBlockingIssues"/>, and styles the panel header + border by
        /// overall severity. Brushes resolved fresh per call from App.xaml.
        /// </summary>
        private void BuildValidation()
        {
            _validationIssues.Clear();

            var greenBrush = (Brush)FindResource("BadgeVerifiedGlyph");
            var amberBrush = (Brush)FindResource("MarkerAmberAccent");
            var redBrush = (Brush)FindResource("BadgeErrorGlyph");

            // Concert ids that actually exist (used to detect dangling track references).
            // Concerts with an empty Date have no usable Id; they don't contribute.
            var validIds = new HashSet<string>(
                _concertVms.Where(c => !string.IsNullOrEmpty(c.Date)).Select(c => c.Date));

            // ===== Hard blocks =====
            if (_concertVms.Count == 0)
            {
                _validationIssues.Add(new ValidationIssue
                {
                    Message = "No concerts. Add at least one concert in step 2.",
                    SeverityBrush = redBrush,
                    Severity = ValidationSeverity.Block,
                });
            }

            if (_discVms.Count == 0)
            {
                _validationIssues.Add(new ValidationIssue
                {
                    Message = "No discs. Add at least one disc with tracks in step 3.",
                    SeverityBrush = redBrush,
                    Severity = ValidationSeverity.Block,
                });
            }

            var danglingTrackNumbers = new List<string>();
            foreach (var disc in _discVms)
            {
                foreach (var track in disc.Tracks)
                {
                    if (!string.IsNullOrEmpty(track.ConcertId) && !validIds.Contains(track.ConcertId))
                        danglingTrackNumbers.Add(track.TrackNumber.ToString());
                }
            }
            if (danglingTrackNumbers.Count > 0)
            {
                var listing = string.Join(", ", danglingTrackNumbers.Take(8));
                if (danglingTrackNumbers.Count > 8)
                    listing += $" (+{danglingTrackNumbers.Count - 8} more)";
                _validationIssues.Add(new ValidationIssue
                {
                    Message = $"Tracks {listing} reference concerts that no longer exist. Fix in step 3 or restore the concert in step 2.",
                    SeverityBrush = redBrush,
                    Severity = ValidationSeverity.Block,
                });
            }

            // ===== Soft warnings =====
            // Amber-state tracks: missing required fields, but ConcertId (if set) is valid.
            var amberCount = 0;
            foreach (var disc in _discVms)
            {
                foreach (var track in disc.Tracks)
                {
                    // Skip tracks already counted as dangling.
                    if (!string.IsNullOrEmpty(track.ConcertId) && !validIds.Contains(track.ConcertId))
                        continue;

                    var missing = track.TrackNumber <= 0
                               || string.IsNullOrWhiteSpace(track.Title)
                               || string.IsNullOrWhiteSpace(track.ConcertId)
                               || string.IsNullOrWhiteSpace(track.SongName);
                    if (missing) amberCount++;
                }
            }
            if (amberCount > 0)
            {
                _validationIssues.Add(new ValidationIssue
                {
                    Message = $"{amberCount} track{(amberCount == 1 ? "" : "s")} {(amberCount == 1 ? "has" : "have")} missing required fields (track number, title, concert, or song name).",
                    SeverityBrush = amberBrush,
                    Severity = ValidationSeverity.Warning,
                });
            }

            if (_definition.DiscCount != _discVms.Count)
            {
                _validationIssues.Add(new ValidationIssue
                {
                    Message = $"Disc count mismatch: step 1 says {_definition.DiscCount}, but step 3 has {_discVms.Count} disc{(_discVms.Count == 1 ? "" : "s")}.",
                    SeverityBrush = amberBrush,
                    Severity = ValidationSeverity.Warning,
                });
            }

            var concertsWithIssues = _concertVms.Count(c =>
                string.IsNullOrWhiteSpace(c.Date) || string.IsNullOrWhiteSpace(c.Venue));
            if (concertsWithIssues > 0)
            {
                _validationIssues.Add(new ValidationIssue
                {
                    Message = $"{concertsWithIssues} concert{(concertsWithIssues == 1 ? "" : "s")} {(concertsWithIssues == 1 ? "is" : "are")} missing date or venue.",
                    SeverityBrush = amberBrush,
                    Severity = ValidationSeverity.Warning,
                });
            }

            _hasBlockingIssues = _validationIssues.Any(i => i.Severity == ValidationSeverity.Block);

            // Header text + panel border reflect overall state.
            if (_validationIssues.Count == 0)
            {
                _validationIssues.Add(new ValidationIssue
                {
                    Message = "Ready to save — no issues found.",
                    SeverityBrush = greenBrush,
                    Severity = ValidationSeverity.Info,
                });
                ValidationHeaderText.Text = "Ready to save";
                ValidationPanel.BorderBrush = greenBrush;
            }
            else if (_hasBlockingIssues)
            {
                ValidationHeaderText.Text = "Save blocked — fix these issues";
                ValidationPanel.BorderBrush = redBrush;
            }
            else
            {
                ValidationHeaderText.Text = "Ready to save with warnings";
                ValidationPanel.BorderBrush = amberBrush;
            }
        }

        /// <summary>
        /// Rebuilds the per-disc / per-track read-only display rows for the color-coded
        /// track list. Uses the same three-state rule as <see cref="BuildValidation"/>:
        /// red = dangling concert reference, amber = missing required field, green = OK.
        /// </summary>
        private void BuildReviewDiscs()
        {
            _reviewDiscs.Clear();

            var greenBrush = (Brush)FindResource("BadgeVerifiedGlyph");
            var amberBrush = (Brush)FindResource("MarkerAmberAccent");
            var redBrush = (Brush)FindResource("BadgeErrorGlyph");

            var validIds = new HashSet<string>(
                _concertVms.Where(c => !string.IsNullOrEmpty(c.Date)).Select(c => c.Date));

            foreach (var disc in _discVms)
            {
                var name = string.IsNullOrEmpty(disc.DiscName) ? "" : " — " + disc.DiscName;
                var trackCount = disc.Tracks.Count;
                var discRow = new ReviewDiscRow
                {
                    DiscHeaderText = $"Disc {disc.DiscNumber}{name} · {trackCount} track{(trackCount == 1 ? "" : "s")}",
                };

                foreach (var track in disc.Tracks)
                {
                    discRow.TrackRows.Add(BuildTrackRow(track, validIds, greenBrush, amberBrush, redBrush));
                }

                _reviewDiscs.Add(discRow);
            }
        }

        private static ReviewTrackRow BuildTrackRow(
            BoxSetDiscTrackVm track, HashSet<string> validIds,
            Brush green, Brush amber, Brush red)
        {
            var dangling = !string.IsNullOrEmpty(track.ConcertId) && !validIds.Contains(track.ConcertId);
            var missingFields = track.TrackNumber <= 0
                             || string.IsNullOrWhiteSpace(track.Title)
                             || string.IsNullOrWhiteSpace(track.ConcertId)
                             || string.IsNullOrWhiteSpace(track.SongName);

            Brush status = dangling ? red : (missingFields ? amber : green);

            var concertLabel = string.IsNullOrEmpty(track.ConcertId)
                ? "(unassigned)"
                : (dangling ? $"(dangling: {track.ConcertId})" : track.ConcertId);

            return new ReviewTrackRow
            {
                TrackNumberDisplay = track.TrackNumber > 0 ? track.TrackNumber.ToString() : "—",
                TitleDisplay = string.IsNullOrEmpty(track.Title) ? "(untitled)" : track.Title,
                ConcertLabelDisplay = concertLabel,
                SongNameDisplay = string.IsNullOrEmpty(track.SongName) ? "" : track.SongName,
                SegueDisplay = track.SegueOut ? "↪" : "",
                StatusBrush = status,
            };
        }
    }

    // ===== STEP-2 VIEW MODELS =====
    // Mirrors EditSetlistView.xaml.cs's EditableTrack (lines 341-395): tiny INPC wrappers
    // around the underlying data shapes so the wizard's UI can bind two-way and refresh
    // dependent labels (the ListBox's DisplayLabel updates when Date/Venue change).
    // ToModel() / FromModel() handle the conversion at save and load time.

    /// <summary>Wraps <see cref="BoxSetConcert"/> for two-way binding in the wizard's
    /// step-2 right pane and the ListBox row template's <c>DisplayLabel</c>.</summary>
    public class BoxSetConcertVm : INotifyPropertyChanged
    {
        private string _date = "";
        private string _venue = "";
        private string _city = "";
        private string _state = "";
        private string _country = "";

        public string Date
        {
            get => _date;
            set { if (_date != value) { _date = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayLabel)); } }
        }

        public string Venue
        {
            get => _venue;
            set { if (_venue != value) { _venue = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayLabel)); } }
        }

        public string City  { get => _city;  set { if (_city  != value) { _city  = value; OnPropertyChanged(); } } }
        public string State { get => _state; set { if (_state != value) { _state = value; OnPropertyChanged(); } } }
        public string Country { get => _country; set { if (_country != value) { _country = value; OnPropertyChanged(); } } }

        public ObservableCollection<BoxSetSetVm> Setlist { get; } = new();

        /// <summary>Composite label shown in the concert ListBox. Refreshes when
        /// Date or Venue change because their setters raise PropertyChanged on this.</summary>
        public string DisplayLabel
        {
            get
            {
                var d = string.IsNullOrEmpty(Date) ? "(no date)" : Date;
                var v = string.IsNullOrEmpty(Venue) ? "(no venue)" : Venue;
                return d == "(no date)" && v == "(no venue)"
                    ? "(new concert)"
                    : $"{d} — {v}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

        /// <summary>Builds a <see cref="BoxSetConcert"/> from this VM. <c>Id</c> defaults
        /// to <c>Date</c> per the memo ("Using the date as the ID works for a band that
        /// played at most once a day").</summary>
        public BoxSetConcert ToModel() => new()
        {
            Id = Date,
            Date = Date,
            Venue = Venue,
            City = City,
            State = State,
            Country = Country,
            Setlist = Setlist.Select(s => s.ToModel()).ToList(),
        };
    }

    /// <summary>Wraps <see cref="BoxSetSet"/>. <c>Set</c> is a free string (the memo's
    /// Position 4 lets the wizard recommend a vocabulary but the model accepts anything).</summary>
    public class BoxSetSetVm : INotifyPropertyChanged
    {
        private string _set = "";
        public string Set { get => _set; set { if (_set != value) { _set = value; OnPropertyChanged(); } } }

        public ObservableCollection<BoxSetSetSongVm> Songs { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

        public BoxSetSet ToModel() => new()
        {
            Set = Set,
            Songs = Songs.Select(s => s.ToModel()).ToList(),
        };
    }

    /// <summary>Wraps <see cref="BoxSetSetSong"/>. <c>SegueOut=true</c> means this song
    /// segues into the next one in the same set.</summary>
    public class BoxSetSetSongVm : INotifyPropertyChanged
    {
        private string _name = "";
        private bool _segueOut;

        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
        public bool SegueOut { get => _segueOut; set { if (_segueOut != value) { _segueOut = value; OnPropertyChanged(); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

        public BoxSetSetSong ToModel() => new() { Name = Name, SegueOut = SegueOut };
    }

    /// <summary>Wraps <see cref="BoxSetDisc"/>. <c>DiscNumber</c> is set on creation and
    /// not user-editable from the wizard's UI (it's positional). <c>DiscName</c> is
    /// optional; null/empty values are skipped on save by <c>NullValueHandling.Ignore</c>
    /// in the JSON resolver.</summary>
    public class BoxSetDiscVm : INotifyPropertyChanged
    {
        private int _discNumber;
        private string? _discName;

        public int DiscNumber
        {
            get => _discNumber;
            set { if (_discNumber != value) { _discNumber = value; OnPropertyChanged(); } }
        }

        public string? DiscName
        {
            get => _discName;
            set { if (_discName != value) { _discName = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<BoxSetDiscTrackVm> Tracks { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

        public BoxSetDisc ToModel() => new()
        {
            DiscNumber = DiscNumber,
            DiscName = string.IsNullOrEmpty(DiscName) ? null : DiscName,
            Tracks = Tracks.Select(t => t.ToModel()).ToList(),
        };
    }

    /// <summary>Wraps <see cref="BoxSetDiscTrack"/>. <c>ConcertId</c> is the date string of
    /// the selected concert (matching <see cref="BoxSetConcertVm.ToModel"/>); the ComboBox
    /// in the wizard's track grid binds via the wizard's <c>ConcertOptions</c> collection.
    /// <c>Fingerprint</c> is not exposed in the wizard — it's populated during import.</summary>
    public class BoxSetDiscTrackVm : INotifyPropertyChanged
    {
        private int _trackNumber;
        private string _title = "";
        private string _concertId = "";
        private string _songName = "";
        private bool _segueOut;

        public int TrackNumber
        {
            get => _trackNumber;
            set { if (_trackNumber != value) { _trackNumber = value; OnPropertyChanged(); } }
        }

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public string ConcertId
        {
            get => _concertId;
            set { if (_concertId != value) { _concertId = value; OnPropertyChanged(); } }
        }

        public string SongName
        {
            get => _songName;
            set { if (_songName != value) { _songName = value; OnPropertyChanged(); } }
        }

        public bool SegueOut
        {
            get => _segueOut;
            set { if (_segueOut != value) { _segueOut = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

        public BoxSetDiscTrack ToModel() => new()
        {
            TrackNumber = TrackNumber,
            Title = Title,
            ConcertId = ConcertId,
            SongName = SongName,
            SegueOut = SegueOut,
            // Duration and Fingerprint are import-time fields, not authored in the wizard.
        };
    }

    /// <summary>Item type for the wizard's concert dropdown in step 3. <c>Id</c> matches
    /// the concert's <see cref="BoxSetConcert.Id"/>; <c>DisplayLabel</c> is what the user
    /// sees ("yyyy-MM-dd — Venue"). Rebuilt on each step-3 entry.</summary>
    public class ConcertOption
    {
        public string Id { get; set; } = "";
        public string DisplayLabel { get; set; } = "";
    }

    // ===== STEP-4 REVIEW VIEW MODELS =====
    // Plain POCOs (no INPC) — rebuilt fresh on every step-4 entry and displayed read-only.

    public enum ValidationSeverity { Block, Warning, Info }

    /// <summary>One line in the wizard's step-4 validation panel. <see cref="SeverityBrush"/>
    /// drives the bullet color; <see cref="Severity"/> drives the Save-gating logic.</summary>
    public class ValidationIssue
    {
        public string Message { get; set; } = "";
        public Brush SeverityBrush { get; set; } = Brushes.Gray;
        public ValidationSeverity Severity { get; set; }
    }

    /// <summary>A disc as rendered in the step-4 color-coded track list. Contains its own
    /// pre-formatted header text plus the per-track display rows.</summary>
    public class ReviewDiscRow
    {
        public string DiscHeaderText { get; set; } = "";
        public ObservableCollection<ReviewTrackRow> TrackRows { get; } = new();
    }

    /// <summary>One row of the step-4 color-coded track list. <see cref="StatusBrush"/>
    /// is the per-row color glyph (green/amber/red). All display strings are
    /// pre-formatted to avoid converters on a read-only surface.</summary>
    public class ReviewTrackRow
    {
        public string TrackNumberDisplay { get; set; } = "";
        public string TitleDisplay { get; set; } = "";
        public string ConcertLabelDisplay { get; set; } = "";
        public string SongNameDisplay { get; set; } = "";
        public string SegueDisplay { get; set; } = "";
        public Brush StatusBrush { get; set; } = Brushes.Gray;
    }
}
