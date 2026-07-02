using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DeadEditor.Models;

namespace DeadEditor
{
    /// <summary>
    /// Shared collapsible reference panel docked beside the track grid (reference-side-panel-spec.md).
    /// Slice 1 hosts only the read-only Setlist tab (the Info tab arrives in slice 3).
    ///
    /// Pure display + event surface: the control never reads a service, never touches
    /// <c>TrackInfo</c>, and never mutates dirty state. The hosting view owns all data-fetching and
    /// all writes and drives the panel through <see cref="SetSetlist"/>.
    /// </summary>
    public partial class SetlistReferencePanel : System.Windows.Controls.UserControl
    {
        public SetlistReferencePanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Populate or refresh the Setlist tab. <paramref name="claimedPositions"/> is null pre-match
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
            SetlistItems.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
            SetlistCountText.Text = hasEntries
                ? (rows.Count == 1 ? "1 song" : $"{rows.Count} songs")
                : "";
        }

        /// <summary>Display row bound by the Setlist tab's ItemTemplate.</summary>
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
