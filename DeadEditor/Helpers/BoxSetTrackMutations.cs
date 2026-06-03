using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Re-sequences every track's <see cref="BoxSetTrack.TrackNumber"/> to a contiguous
        /// <c>1..N</c>, ordered by <c>(Date ascending, then existing TrackNumber ascending)</c>,
        /// with no-date (empty) tracks sorted LAST (the box-set Renumber action). Fixes
        /// out-of-order pulls (a date pulled later no longer keeps lower numbers than an earlier
        /// date) and closes gaps left by whole-date / per-row deletes, without delete-and-reimport.
        /// <para>
        /// Flat and unconditional: any prior numbering — including hand-entered disc-prefixed
        /// values (101, 503) — is overwritten. A disc-prefixed-preserving variant is deferred
        /// until import wiring lands (see box-set-design-memo.md positions 10/11).
        /// </para>
        /// <para>
        /// The backing list is physically reordered so the persisted track array matches the new
        /// numbering (array order == TrackNumber order == chronological). Null/empty input is a
        /// no-op, matching <see cref="RemoveTracksForDate"/>'s null tolerance. Non-destructive: no
        /// tracks are added or removed, so the caller needs no confirm prompt.
        /// </para>
        /// </summary>
        public static void RenumberByDate(List<BoxSetTrack>? tracks)
        {
            if (tracks == null || tracks.Count == 0) return;

            // D2: empty/no-date sorts LAST. A bare ordinal compare would put "" first, so an
            // explicit is-empty primary key is required — the easy-to-miss correctness point.
            var ordered = tracks
                .OrderBy(t => string.IsNullOrEmpty(t?.Date) ? 1 : 0)
                .ThenBy(t => t?.Date ?? "", StringComparer.Ordinal)
                .ThenBy(t => t?.TrackNumber ?? 0)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
                ordered[i].TrackNumber = i + 1; // same object refs as in `tracks`

            // D4: reorder the backing list so the persisted array matches the numbers.
            // Materialized into `ordered` above before clearing, since it derives from `tracks`.
            tracks.Clear();
            tracks.AddRange(ordered);
        }
    }
}
