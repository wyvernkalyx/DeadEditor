using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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

        public SetlistReferencePanel()
        {
            InitializeComponent();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);

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
            public string PositionText { get; set; } = "";
            public string Name { get; set; } = "";
            public string SetLabel { get; set; } = "";
            public string SegueMarker { get; set; } = "";
            public bool IsClaimed { get; set; }
        }
    }
}
