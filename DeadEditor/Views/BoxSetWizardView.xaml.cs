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
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;

namespace DeadEditor
{
    /// <summary>
    /// Multi-step wizard for authoring a <see cref="BoxSetDefinition"/>. Single
    /// UserControl; step transitions toggle <c>Visibility</c> on three content panels
    /// rather than navigating between views. Step 1 (top-level info) ships in this
    /// commit; steps 2-3 are placeholders.
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
        private readonly ListCollectionView _tracksView;
        private int _currentStep = 1;

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

        public BoxSetWizardView(BoxSetService boxSetService)
        {
            InitializeComponent();
            _boxSetService = boxSetService;

            // Bind the step-2 grid directly to the long-lived definition's track list.
            // Edits flow live into _definition.Tracks (approach (a), INPC on BoxSetTrack),
            // so Save() needs no separate step-2 sync. Safe for the new-box-set flow: a
            // fresh _definition per wizard, discarded on Cancel. Edit-mode entry (banked)
            // will revisit with a load-a-copy / discard-on-cancel approach.
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

            if (_currentStep < 3)
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
                MessageBox.Show(
                    $"Track date(s) must be in yyyy-MM-dd format (leave blank if unknown).\n\nFix track(s): {rows}",
                    "Invalid Track Date", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (_currentStep != 2) ShowStep(2);
                return;
            }

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

            StepIndicatorText.Text = step switch
            {
                1 => "Step 1 of 3 — Top-Level Info",
                2 => "Step 2 of 3 — Concerts and Tracks",
                3 => "Step 3 of 3 — Review",
                _ => $"Step {step} of 3"
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
                MessageBox.Show("Enter a date as yyyy-MM-dd.", "Invalid Date",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sets = ShowLookupService.Instance.GetSetlist(date);
            if (sets == null)
            {
                MessageBox.Show($"No setlist found for {date}.", "No Setlist",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
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
