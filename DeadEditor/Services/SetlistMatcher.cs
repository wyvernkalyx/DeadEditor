using DeadEditor.Models;
using System;
using System.Collections.Generic;

namespace DeadEditor.Services
{
    /// <summary>
    /// Matches audio tracks against a flattened setlist by canonical title and
    /// decorates matched tracks with the setlist's canonical name and segue
    /// flag. Unmatched tracks are left entirely untouched.
    ///
    /// This is the Model B core: the audio is the archive. The setlist
    /// documents what songs were performed; the audio documents what was
    /// recorded. They overlap but are not identical, so Match Setlist's job
    /// is decoration, not alignment. Disc and track numbers are never touched
    /// by this method.
    ///
    /// Pure function — no UI, no I/O, no service singletons. The
    /// canonical-resolver callback is injected so tests don't need to load
    /// songs.json.
    /// </summary>
    public static class SetlistMatcher
    {
        public sealed class SetlistEntry
        {
            public string Name { get; init; } = "";
            public string Canonical { get; init; } = "";
            public int Position { get; init; }
            public bool Segue { get; init; }
        }

        public sealed class MatchResult
        {
            public int MatchedCount { get; init; }
            public int SegueCount { get; init; }
            public HashSet<int> ClaimedPositions { get; init; } = new();
        }

        /// <summary>
        /// One proposed decoration for a single matched track: the captured
        /// old values (the track's current SongName/Segue) and the would-be
        /// new values (the claimed setlist entry's canonical name/segue),
        /// plus the setlist indices this track claims.
        ///
        /// <see cref="CoveredEntryIndices"/> is a <b>list</b> (length 1 for a
        /// single-song match) so a future combined-track row can carry
        /// <c>[i, i+1, ...]</c> with no model change — the one addition Sec.
        /// 3.2 of the review-surface spec identifies as serving both the
        /// review surface and combined-track matching.
        ///
        /// <see cref="Track"/> is the reference <see cref="Apply"/> writes
        /// back to.
        /// </summary>
        public sealed class TrackProposal
        {
            public TrackInfo Track { get; init; } = null!;
            public string OldSongName { get; init; } = "";
            public string NewSongName { get; init; } = "";
            public bool OldSegue { get; init; }
            public bool NewSegue { get; init; }
            public List<int> CoveredEntryIndices { get; init; } = new();
        }

        /// <summary>
        /// The output of <see cref="ComputeProposals"/>: the per-track
        /// proposals plus the claimed-at-compute set. Claiming is fixed here
        /// and is independent of any later per-field accept/ignore (spec Sec.
        /// 4.1) — <see cref="Apply"/> never re-opens a claim.
        /// </summary>
        public sealed class ProposalSet
        {
            public List<TrackProposal> Proposals { get; init; } = new();
            public HashSet<int> ClaimedPositions { get; init; } = new();
        }

        /// <summary>
        /// For each track in <paramref name="tracks"/>, find the first
        /// unclaimed entry in <paramref name="setlist"/> whose canonical
        /// title matches the track's canonical. On match: set the track's
        /// SongName to the setlist's canonical, copy the segue flag, mark
        /// IsMatched=true and IsModified=true. On no match: leave the track
        /// untouched (do not flip IsMatched — Normalize's prior verdict
        /// stands).
        ///
        /// This is the thin compose of <see cref="ComputeProposals"/> +
        /// <see cref="Apply"/>; it applies <b>every</b> proposal, preserving
        /// the original blind-apply behavior. Per-field selection arrives in
        /// the B2 review surface and routes through the same two methods.
        /// </summary>
        /// <param name="tracks">Audio tracks in grid order.</param>
        /// <param name="setlist">Flattened setlist with canonical titles.</param>
        /// <param name="resolveCanonical">Maps a track's SongName to its
        /// canonical title (or null if unknown). Typically wraps
        /// <c>NormalizationService.Normalize</c> followed by
        /// <c>GetOfficialTitle</c>.</param>
        public static MatchResult MatchAndDecorate(
            IReadOnlyList<TrackInfo> tracks,
            IReadOnlyList<SetlistEntry> setlist,
            Func<string, string?> resolveCanonical)
        {
            return Apply(ComputeProposals(tracks, setlist, resolveCanonical));
        }

        /// <summary>
        /// Runs the exact same name-gated, first-unclaimed match as
        /// <see cref="MatchAndDecorate"/> but <b>writes nothing</b>. For each
        /// matched track it captures the old (current) and would-be-new
        /// SongName/Segue values and the claimed entry index, returning the
        /// proposals and the claimed set. The input tracks are left
        /// unmutated.
        /// </summary>
        public static ProposalSet ComputeProposals(
            IReadOnlyList<TrackInfo> tracks,
            IReadOnlyList<SetlistEntry> setlist,
            Func<string, string?> resolveCanonical)
        {
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            if (setlist == null) throw new ArgumentNullException(nameof(setlist));
            if (resolveCanonical == null) throw new ArgumentNullException(nameof(resolveCanonical));

            var claimed = new HashSet<int>();
            var proposals = new List<TrackProposal>();

            foreach (var track in tracks)
            {
                var trackName = track.SongName;
                if (string.IsNullOrEmpty(trackName)) continue;

                var trackCanonical = resolveCanonical(trackName);
                if (string.IsNullOrEmpty(trackCanonical)) continue;

                int matchedIndex = -1;
                for (int i = 0; i < setlist.Count; i++)
                {
                    if (claimed.Contains(i)) continue;
                    if (string.Equals(trackCanonical, setlist[i].Canonical,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        matchedIndex = i;
                        break;
                    }
                }

                if (matchedIndex < 0) continue;

                claimed.Add(matchedIndex);
                proposals.Add(new TrackProposal
                {
                    Track = track,
                    OldSongName = track.SongName,
                    NewSongName = setlist[matchedIndex].Canonical,
                    OldSegue = track.Segue,
                    NewSegue = setlist[matchedIndex].Segue,
                    CoveredEntryIndices = new List<int> { matchedIndex },
                });
            }

            return new ProposalSet
            {
                Proposals = proposals,
                ClaimedPositions = claimed,
            };
        }

        /// <summary>
        /// Writes SongName, Segue, and IsMatched for every proposal in
        /// <paramref name="proposalSet"/>, and returns the <see cref="MatchResult"/>
        /// derived from it (MatchedCount = proposal count, SegueCount = proposals
        /// whose new segue is true, ClaimedPositions = the claimed set).
        ///
        /// IsModified is set only on a real change — when the proposal's new
        /// SongName or Segue differs from its captured old value (spec Sec. 6).
        /// A no-op match (the album already equals the setlist) leaves IsModified
        /// untouched, so it is never spuriously raised and a pre-existing edit's
        /// flag is never cleared.
        ///
        /// This still applies all proposals; the B2 surface will hand it only
        /// the resolved subset.
        /// </summary>
        public static MatchResult Apply(ProposalSet proposalSet)
        {
            if (proposalSet == null) throw new ArgumentNullException(nameof(proposalSet));

            int segues = 0;
            foreach (var p in proposalSet.Proposals)
            {
                p.Track.SongName = p.NewSongName;
                p.Track.Segue = p.NewSegue;
                p.Track.IsMatched = true;
                // Flag modified only on a real change (spec Sec. 6): a match
                // that re-asserts the album's existing values must not show
                // unsaved changes. Never cleared here — a pre-existing edit's
                // IsModified is left as-is.
                if (!string.Equals(p.NewSongName, p.OldSongName, StringComparison.Ordinal)
                    || p.NewSegue != p.OldSegue)
                {
                    p.Track.IsModified = true;
                }
                if (p.NewSegue) segues++;
            }

            return new MatchResult
            {
                MatchedCount = proposalSet.Proposals.Count,
                SegueCount = segues,
                ClaimedPositions = proposalSet.ClaimedPositions,
            };
        }
    }
}
