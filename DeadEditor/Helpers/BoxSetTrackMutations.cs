using System;
using System.Collections.Generic;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, WPF-free mutations over a box set's flat track list. Kept out of the
    /// code-behind so the operation is unit-testable in isolation, mirroring the
    /// <see cref="BoxSetPullCollision"/> pure-helper pattern.
    /// </summary>
    public static class BoxSetTrackMutations
    {
        /// <summary>
        /// Removes every track carrying <paramref name="date"/> from
        /// <paramref name="tracks"/> and returns the number removed (commit H3 —
        /// "remove a whole date" header action). Uses the same exact-string identity as
        /// the wizard's date grouping and <see cref="BoxSetPullCollision.HasTracksForDate"/>
        /// (ordinal; null-safe per row), so an empty <paramref name="date"/> removes the
        /// no-date group's rows the same way the grid groups them. TrackNumbers on the
        /// surviving rows are NOT renumbered — they are curator data matching the physical
        /// release, consistent with the per-row delete path.
        /// </summary>
        public static int RemoveTracksForDate(List<BoxSetTrack>? tracks, string date)
        {
            if (tracks == null)
                return 0;

            return tracks.RemoveAll(t => string.Equals(t?.Date ?? "", date ?? "", StringComparison.Ordinal));
        }
    }
}
