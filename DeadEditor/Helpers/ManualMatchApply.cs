using System.Collections.Generic;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure manual-match write core (reference-side-panel-spec.md §7.1). Applies the exact
    /// Match-to-Song write set to a <see cref="TrackInfo"/> and records the claimed setlist position,
    /// returning the data the hosting view needs for its after-core hooks (alias-from-prior-title,
    /// claimed position for panel re-dim + status).
    ///
    /// WPF-free and singleton-free, so both surfaces' right-click Match-to-Song handlers — and, in
    /// slice 5b, the panel click/DnD assign path — route through one unit-tested state transition,
    /// with no third inline copy of the write set (§7.2). The pure claimed-position add is folded in
    /// because it is a plain <see cref="ISet{T}"/> mutation (no view/service touch), which keeps the
    /// whole "this track now claims this position" transition testable in one call; the impure hooks
    /// (alias persistence, panel re-dim, status, surface-specific validation) stay in the host and are
    /// driven by the returned <see cref="ManualMatchResult"/>.
    ///
    /// Segue is set-true-only, never cleared (§7.3): write-identical to the existing manual path and
    /// deliberately distinct from the clear-capable batch <c>SetlistMatcher.Apply</c>.
    /// </summary>
    public static class ManualMatchApply
    {
        /// <summary>
        /// Applies the manual-match write set to <paramref name="track"/> and adds
        /// <paramref name="claimedPosition"/> to <paramref name="claimedPositions"/>. Captures the
        /// track's prior <c>SongName</c> before overwriting it so the host can learn it as an alias.
        /// </summary>
        public static ManualMatchResult Apply(
            TrackInfo track,
            ISet<int> claimedPositions,
            int claimedPosition,
            string canonical,
            bool segue)
        {
            var previousSongName = track.SongName ?? "";

            track.SongName = canonical;
            track.IsMatched = true;
            track.IsModified = true;
            if (segue)
                track.Segue = true;

            claimedPositions.Add(claimedPosition);

            return new ManualMatchResult(previousSongName, canonical, claimedPosition);
        }

        /// <summary>
        /// Panel/DnD re-assign transition (reference-side-panel-spec.md §7.5.1): frees the track's
        /// previously claimed position (so the old setlist entry un-dims) and claims
        /// <paramref name="newPosition"/>, applying the same write set as <see cref="Apply"/>. When
        /// <paramref name="previousPosition"/> is null (the track claimed nothing) or equals
        /// <paramref name="newPosition"/> (re-affirming the same entry), no free happens and this is
        /// exactly <see cref="Apply"/>. Pure over <see cref="TrackInfo"/> + the claimed set; the host
        /// supplies <paramref name="previousPosition"/> from its per-track back-reference.
        /// </summary>
        public static ManualMatchResult Reassign(
            TrackInfo track,
            ISet<int> claimedPositions,
            int? previousPosition,
            int newPosition,
            string canonical,
            bool segue)
        {
            if (previousPosition.HasValue && previousPosition.Value != newPosition)
                claimedPositions.Remove(previousPosition.Value);

            return Apply(track, claimedPositions, newPosition, canonical, segue);
        }
    }
}
