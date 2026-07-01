using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure, WPF-free selector for the tracks the Match Setlist compute left
    /// unmatched. <see cref="SetlistMatcher.ComputeProposals"/> emits a proposal
    /// only for MATCHED tracks (unmatched ones are silently skipped), so the
    /// review surface can recover the unmatched set only by subtracting the
    /// proposals' tracks from the full input list. This mirrors how the host
    /// derives its "N unmatched" count (<c>_tracks.Count - MatchedCount</c>), but
    /// keeps the actual track identities so the dialog can list them.
    ///
    /// Identity is by <b>reference</b> (<see cref="object.ReferenceEquals"/>) — the
    /// same TrackInfo instances <see cref="SetlistMatcher.Apply"/> writes back
    /// through (<see cref="SetlistMatcher.TrackProposal.Track"/>), not value
    /// equality. Input order is preserved (grid order). Null inputs are treated
    /// as empty. Kept out of the WPF layer so it is unit-testable in isolation
    /// (mirrors <see cref="AliasRowBuilder"/>).
    /// </summary>
    public static class UnmatchedTracks
    {
        public static List<TrackInfo> Compute(
            IReadOnlyList<TrackInfo>? tracks,
            SetlistMatcher.ProposalSet? proposals)
        {
            var result = new List<TrackInfo>();
            if (tracks == null) return result;

            var matched = new HashSet<TrackInfo>(ReferenceEqualityComparer.Instance);
            if (proposals?.Proposals != null)
            {
                foreach (var p in proposals.Proposals)
                {
                    if (p?.Track != null) matched.Add(p.Track);
                }
            }

            foreach (var track in tracks)
            {
                if (track != null && !matched.Contains(track)) result.Add(track);
            }

            return result;
        }
    }
}
