using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeadEditor.Models;

namespace DeadEditor
{
    /// <summary>
    /// Shared collapsible reference panel docked beside the track grid (reference-side-panel-spec.md).
    /// Hosts the read-only Setlist view under an always-visible 24px toggle strip. The control owns
    /// its own collapse mechanism (§3) — the strip + expand/collapse were extracted from the Import
    /// host so both hosts share one mechanism; hosts just place the control in their middle column.
    ///
    /// Pure display + event surface: the control never reads a service, never touches
    /// <c>TrackInfo</c>, and never mutates dirty state. The hosting view owns all data-fetching and
    /// all writes and drives the panel through <see cref="SetSetlist"/>.
    /// </summary>
    public partial class SetlistReferencePanel : System.Windows.Controls.UserControl
    {
        // Collapse mechanism (imperative bool + Width toggle, the PlaylistPanel idiom adapted to a
        // horizontal column Width, spec §3). Collapsed by default on both hosts.
        private bool _expanded;
        private const double ExpandedWidth = 320;

        // True when the current setlist has entries — cached so the ShowEditSetlistLink setter can
        // re-evaluate the deep-link button's visibility without re-running SetSetlist.
        private bool _hasEntries;

        // Host policy: Import shows the "Edit setlist ↗" deep-link; Edit suppresses it (§9 banking).
        private bool _showEditSetlistLink = true;

        // Drag-source state for DnD assign (spec §10.2-i). PreviewMouseLeftButtonDown arms with the row
        // + start point; PreviewMouseMove begins DoDragDrop only past the system drag threshold, so the
        // stationary double-click assign gesture (SetlistRow_MouseLeftButtonDown) never trips a drag.
        private System.Windows.Point _dragStartPoint;
        private SetlistRow? _dragRow;
        private bool _dragArmed;

        /// <summary>
        /// Whether the header's "Edit setlist ↗" deep-link is offered. Import leaves it true; Edit
        /// sets it false (a round-trip from Edit would silently lose in-progress metadata edits, so
        /// the affordance is banked there, spec §9). The button is shown only when this is true AND
        /// the panel currently has entries.
        /// </summary>
        public bool ShowEditSetlistLink
        {
            get => _showEditSetlistLink;
            set
            {
                _showEditSetlistLink = value;
                if (EditSetlistButton != null)
                    EditSetlistButton.Visibility =
                        (value && _hasEntries) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Raised when the user activates the header's "Edit setlist ↗" affordance. The hosting
        /// view performs the concert-detail deep-link (reference-side-panel-spec.md §9); the panel
        /// holds no date and performs no navigation (§4 contract).
        /// </summary>
        public event Action? EditSetlistRequested;

        /// <summary>
        /// Raised when the user double-clicks a setlist entry to assign it to the hosting view's
        /// currently-selected track (reference-side-panel-spec.md §7, §7.5). Carries the entry's
        /// flattened 0-based <see cref="SetlistEntryVm.Position"/>. The panel holds no track selection
        /// and performs no write — the host resolves the selected track and applies the assignment
        /// (§4 contract). Available whenever the panel is populated, independent of any match run.
        /// </summary>
        public event Action<int>? AssignRequested;

        public SetlistReferencePanel()
        {
            InitializeComponent();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);

        /// <summary>
        /// Double-click on a setlist row raises <see cref="AssignRequested"/> with the row's flattened
        /// position. Single-clicks (selection/scroll) are ignored so an accidental click never assigns.
        /// </summary>
        private void SetlistRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
                return;
            // A double-click assigns — cancel any armed drag so a pending drag-start can't hijack the
            // gesture (spec §10.2-i: the double-click gesture wins over the drag source).
            _dragArmed = false;
            _dragRow = null;
            if (sender is FrameworkElement fe && fe.DataContext is SetlistRow row)
                AssignRequested?.Invoke(row.Position);
        }

        /// <summary>
        /// Arm a potential drag on a setlist row (spec §10.2-i): record the row and the start point.
        /// Only a genuine single-click press (<c>ClickCount == 1</c>) arms — the second press of a
        /// double-click must NOT arm, or its modal <c>DoDragDrop</c> could consume the mouse-input
        /// stream and starve the bubbling <see cref="SetlistRow_MouseLeftButtonDown"/> of its
        /// <c>ClickCount == 2</c>, killing the assign. No drag starts here; only
        /// <see cref="SetlistRow_PreviewMouseMove"/> past the drag threshold starts one.
        /// </summary>
        private void SetlistRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1)
            {
                _dragArmed = false;
                _dragRow = null;
                return;
            }
            if (sender is FrameworkElement fe && fe.DataContext is SetlistRow row)
            {
                _dragStartPoint = e.GetPosition(null);
                _dragRow = row;
                _dragArmed = true;
            }
        }

        /// <summary>
        /// Start the drag once the pointer leaves the system drag threshold with the button held. The
        /// drag carries the row's <see cref="SetlistRow"/> as payload (spec §10.1); the host's grid
        /// drop branch reads <see cref="SetlistRow.Position"/> and assigns.
        /// </summary>
        private void SetlistRow_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragArmed || _dragRow == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
                return;

            // Disarm before the blocking DoDragDrop loop so it can't re-enter and start a second drag.
            var payload = _dragRow;
            _dragArmed = false;
            _dragRow = null;
            if (sender is DependencyObject source)
                System.Windows.DragDrop.DoDragDrop(source, payload, System.Windows.DragDropEffects.Move);
        }

        /// <summary>
        /// Release without crossing the threshold (a click / double-click): drop the armed state so a
        /// later stray move can't start a drag from a stale start point (spec §10.2-i).
        /// </summary>
        private void SetlistRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragArmed = false;
            _dragRow = null;
        }

        /// <summary>Expand or collapse the panel body (Width 320 &lt;-&gt; 0) and swap the chevron.</summary>
        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            PanelBody.Width = expanded ? ExpandedWidth : 0;
            Chevron.Text = expanded ? "◀" : "▶";
        }

        private void EditSetlistButton_Click(object sender, RoutedEventArgs e)
            => EditSetlistRequested?.Invoke();

        /// <summary>
        /// Populate or refresh the Setlist view. <paramref name="claimedPositions"/> is null pre-match
        /// (nothing dimmed) and a set of flattened positions post-match (members render dimmed).
        /// Re-calling with an updated claimed set is the re-dim path (after a match run or a manual
        /// assign). An empty <paramref name="entries"/> list renders the empty state.
        /// </summary>
        public void SetSetlist(IReadOnlyList<SetlistEntryVm> entries, ISet<int>? claimedPositions)
        {
            var rows = new List<SetlistRow>();
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    rows.Add(new SetlistRow
                    {
                        Position = e.Position,
                        PositionText = (e.Position + 1).ToString(),
                        Name = e.Name,
                        SetLabel = e.SetLabel,
                        SegueMarker = e.Segue ? ">" : "",
                        IsClaimed = claimedPositions?.Contains(e.Position) ?? false,
                    });
                }
            }

            SetlistItems.ItemsSource = rows;

            bool hasEntries = rows.Count > 0;
            _hasEntries = hasEntries;
            SetlistItems.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
            // Deep-link affordance is offered only when there is a setlist to edit AND the host
            // allows it (Import yes, Edit no — spec §9).
            EditSetlistButton.Visibility =
                (hasEntries && _showEditSetlistLink) ? Visibility.Visible : Visibility.Collapsed;
            SetlistCountText.Text = hasEntries
                ? (rows.Count == 1 ? "1 song" : $"{rows.Count} songs")
                : "";
        }

        /// <summary>Display row bound by the Setlist view's ItemTemplate.</summary>
        public sealed class SetlistRow
        {
            /// <summary>Flattened 0-based position — the claim axis, carried for AssignRequested.</summary>
            public int Position { get; set; }
            public string PositionText { get; set; } = "";
            public string Name { get; set; } = "";
            public string SetLabel { get; set; } = "";
            public string SegueMarker { get; set; } = "";
            public bool IsClaimed { get; set; }
        }
    }
}
