using System.Collections.Generic;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Result of a completeness evaluation (setlist-extras-writeback-spec.md D4/§4/§5). Carries the
    /// song-typed and extra-typed tallies so a later slice can surface the "23/23 songs placed, 3
    /// extras" readout on the album Verify surface without reshaping; this slice computes only, it does
    /// not display, and completeness does NOT gate album Verify (D4).
    /// </summary>
    public readonly record struct SetlistCompletenessResult(
        int SongTotal, int SongClaimed, int ExtraTotal, int ExtraClaimed)
    {
        /// <summary>
        /// "Complete match": every song-typed entry is claimed (§4). Extras are excluded from the
        /// denominator — an unclaimed extra never blocks completeness. Vacuously true for a setlist
        /// with no song-typed entries.
        /// </summary>
        public bool IsComplete => SongClaimed == SongTotal;

        /// <summary>Song-typed entries still unplaced — the completeness shortfall.</summary>
        public int SongUnclaimed => SongTotal - SongClaimed;
    }

    /// <summary>
    /// Pure, WPF-free, I/O-free completeness over a flattened setlist projection (D4/§4). Redefines
    /// "complete" as <c>songPositions ⊆ claimedPositions</c>, where <c>songPositions</c> are the
    /// flattened positions of song-typed entries; extras are optional placement targets, never
    /// obligations. Operates on the same flattened <see cref="SetlistEntryVm.Position"/> axis the
    /// matcher claims on (<c>MatchResult.ClaimedPositions</c>), so a caller pairs the projection it
    /// rendered with the claimed set the matcher returned.
    /// </summary>
    public static class SetlistCompleteness
    {
        /// <summary>
        /// Tallies song/extra totals and how many of each are claimed. A null setlist or null claim
        /// set is treated as empty. An entry's claimed-ness is decided by whether its
        /// <see cref="SetlistEntryVm.Position"/> is in <paramref name="claimedPositions"/>.
        /// </summary>
        public static SetlistCompletenessResult Evaluate(
            IReadOnlyList<SetlistEntryVm>? setlist, ISet<int>? claimedPositions)
        {
            int songTotal = 0, songClaimed = 0, extraTotal = 0, extraClaimed = 0;
            if (setlist == null)
                return new SetlistCompletenessResult(0, 0, 0, 0);

            foreach (var entry in setlist)
            {
                bool claimed = claimedPositions != null && claimedPositions.Contains(entry.Position);
                if (SetlistEntryType.IsSong(entry.Type))
                {
                    songTotal++;
                    if (claimed) songClaimed++;
                }
                else
                {
                    extraTotal++;
                    if (claimed) extraClaimed++;
                }
            }

            return new SetlistCompletenessResult(songTotal, songClaimed, extraTotal, extraClaimed);
        }

        /// <summary>Convenience predicate: true when every song-typed entry is claimed (§4).</summary>
        public static bool IsComplete(
            IReadOnlyList<SetlistEntryVm>? setlist, ISet<int>? claimedPositions)
            => Evaluate(setlist, claimedPositions).IsComplete;
    }
}
