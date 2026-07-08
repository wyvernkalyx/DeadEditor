using System.Collections.Generic;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// D10a label rule (setlist-extras-writeback-spec.md §5, widened 2026-07-07): NO entry of ANY type
    /// may save with an empty/whitespace name. Pure and WPF-free so the setlist editor's save-time gate
    /// is unit-testable. Supersedes the extras-only D10 scoping — a fresh row defaults to type "song",
    /// so an extras-only block left the front door open (an unnamed song row persisted to canon). Every
    /// entry needs a display label in the panel/editor/grids, and <c>ConcertVerifyGate</c> already
    /// refuses to verify a nameless row, so blocking at save closes the gap between save-legal and
    /// verify-legal for names.
    /// </summary>
    public static class RowLabelRule
    {
        /// <summary>
        /// The 1-based grid position of the first row whose <c>SongName</c> is empty or whitespace —
        /// regardless of type — or <c>null</c> when every row is named. 1-based to match the editor's
        /// displayed "#" column so the block message can point at the offending row.
        /// </summary>
        public static int? FirstUnnamedRow(IReadOnlyList<ConcertTrackInput> tracks)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(tracks[i].SongName))
                    return i + 1;
            }
            return null;
        }
    }
}
