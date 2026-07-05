using DeadEditor.Models;
using System;
using System.Collections.Generic;
using System.Linq;

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

            /// <summary>
            /// Typed-entry kind (setlist-extras-writeback-spec.md D1/§3.1); default "song". Auto-match
            /// NEVER claims a non-song entry (D2, §4): extras are excluded as candidates in both the
            /// direct pass and the combine pass, so an extra is only ever placed by manual / panel
            /// assignment. Default keeps every all-song setlist (the entire current corpus) matching
            /// byte-identically to before.
            /// </summary>
            public string Type { get; init; } = SetlistEntryType.Song;
        }

        /// <summary>
        /// Associates one applied track with the primary setlist position it claimed. Lets the host
        /// build a track→claimed-position back-reference (reference-side-panel-spec.md §7.5.1) so a
        /// later panel re-assign can free the track's prior position and un-dim it. For a combined
        /// track (2+ covered indices) this carries the first covered index only.
        /// </summary>
        public readonly record struct TrackClaim(TrackInfo Track, int Position);

        public sealed class MatchResult
        {
            public int MatchedCount { get; init; }
            public int SegueCount { get; init; }
            public HashSet<int> ClaimedPositions { get; init; } = new();

            /// <summary>
            /// Per-track primary claimed position for every applied proposal (single and combined),
            /// in proposal order. Additive; empty by default so consumers that ignore it are
            /// unaffected. The host maps each <see cref="TrackClaim.Track"/> back to its VM to stamp
            /// the claimed-position back-reference used by panel re-assign (§7.5.1).
            /// </summary>
            public IReadOnlyList<TrackClaim> ClaimsByTrack { get; init; } = new List<TrackClaim>();

            /// <summary>
            /// The applied proposals that collapse 2+ official entries into one media track
            /// (<see cref="TrackProposal.CoveredEntryIndices"/> count &gt; 1). The alias-persistence
            /// seam reads each one's covered run to record a confirmed combine
            /// (alias-setlists-spec.md §4); single-song matches are not included. Element type is
            /// <see cref="TrackProposal"/> by design — the matcher stays free of the persistence
            /// model (<c>AliasEntry</c>). Empty by default, so consumers that ignore it are
            /// unaffected.
            /// </summary>
            public IReadOnlyList<TrackProposal> ConfirmedCombines { get; init; } = new List<TrackProposal>();
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
            Func<string, string?> resolveCanonical,
            Func<string, string?> resolveOfficialOrNull)
        {
            return Apply(ComputeProposals(tracks, setlist, resolveCanonical, resolveOfficialOrNull));
        }

        /// <summary>
        /// Runs the exact same name-gated, first-unclaimed match as
        /// <see cref="MatchAndDecorate"/> but <b>writes nothing</b>. For each
        /// matched track it captures the old (current) and would-be-new
        /// SongName/Segue values and the claimed entry index, returning the
        /// proposals and the claimed set. The input tracks are left
        /// unmutated.
        ///
        /// Two resolvers, by deliberate contract: <paramref name="resolveCanonical"/> is the
        /// direct-match resolver and may ECHO its input on a miss (the call sites'
        /// Normalize-then-GetOfficialTitle-or-input chain) — pass 1 only equality-compares the
        /// result against setlist canonicals, so an echo can never spuriously match.
        /// <paramref name="resolveOfficialOrNull"/> MUST return null on an unknown name
        /// (GetOfficialTitle semantics): pass 2 hands it to <see cref="CombinedTrackDecomposer"/>,
        /// whose atomicity guard (5.1) and component-validation gate (5.2) both depend on null
        /// meaning "not a known song". Passing an echoing resolver here would make every combined
        /// name look atomic and split nothing — the slice-3 gate bug this contract prevents.
        /// </summary>
        public static ProposalSet ComputeProposals(
            IReadOnlyList<TrackInfo> tracks,
            IReadOnlyList<SetlistEntry> setlist,
            Func<string, string?> resolveCanonical,
            Func<string, string?> resolveOfficialOrNull)
        {
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            if (setlist == null) throw new ArgumentNullException(nameof(setlist));
            if (resolveCanonical == null) throw new ArgumentNullException(nameof(resolveCanonical));
            if (resolveOfficialOrNull == null) throw new ArgumentNullException(nameof(resolveOfficialOrNull));

            var claimed = new HashSet<int>();
            var proposals = new List<TrackProposal>();

            // Pass 1 (direct greedy): each track claims the first unclaimed entry whose Canonical
            // equals the track's canonical. High-confidence exact matches establish all claims first
            // so a later combine can never steal a position a direct match wanted (spec 5.3).
            var matchedTracks = new HashSet<TrackInfo>();
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
                    // D2: auto-match never claims an extra, even when the track's canonical title
                    // equals the extra's (a "Tuning" track does not auto-claim a `tuning` entry).
                    if (SetlistEntryType.IsExtra(setlist[i].Type)) continue;
                    if (string.Equals(trackCanonical, setlist[i].Canonical,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        matchedIndex = i;
                        break;
                    }
                }

                if (matchedIndex < 0) continue;

                claimed.Add(matchedIndex);
                matchedTracks.Add(track);
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

            // Pass 2 (claim-aware decomposition): for each still-unmatched track, try to decompose a
            // combined name (e.g. "Help On The Way/Slipknot!") into a contiguous run of still-unclaimed
            // official entries. A combine claims the WHOLE run; its NewSongName is the covered canonical
            // names joined with " > " (11.3) and its NewSegue is the LAST covered entry's boundary segue
            // (internal segues are informational only, 3.1). Order-independent: direct matches already
            // claimed their positions in pass 1 (spec 5.3).
            var officialCanonicalNames = new List<string>(setlist.Count);
            foreach (var entry in setlist)
                officialCanonicalNames.Add(entry.Canonical);

            // D2 for the combine pass: an extra index must never fall inside a combine run. Feed the
            // decomposer every extra index as if already claimed, so any window overlapping an extra is
            // skipped exactly as a claimed position is — WITHOUT polluting the real `claimed` set (which
            // becomes ClaimedPositions / ClaimsByTrack, both of which must stay extra-free). For an
            // all-song setlist this stays empty and the combine pass is byte-identical to before.
            var extraIndices = new HashSet<int>();
            for (int i = 0; i < setlist.Count; i++)
                if (SetlistEntryType.IsExtra(setlist[i].Type)) extraIndices.Add(i);

            foreach (var track in tracks)
            {
                if (matchedTracks.Contains(track)) continue;

                var trackName = track.SongName;
                if (string.IsNullOrEmpty(trackName)) continue;

                // For an all-song setlist, pass `claimed` itself (no allocation, identical behavior);
                // otherwise pass claimed ∪ extras so combine windows never cover an extra.
                ISet<int> combineClaimed = claimed;
                if (extraIndices.Count > 0)
                {
                    combineClaimed = new HashSet<int>(claimed);
                    combineClaimed.UnionWith(extraIndices);
                }

                var run = CombinedTrackDecomposer.Decompose(
                    trackName, officialCanonicalNames, resolveOfficialOrNull, combineClaimed);
                if (run == null || run.Count < 2) continue;

                var coveredNames = new List<string>(run.Count);
                foreach (var idx in run)
                    coveredNames.Add(setlist[idx].Canonical);

                claimed.UnionWith(run);
                matchedTracks.Add(track);
                proposals.Add(new TrackProposal
                {
                    Track = track,
                    OldSongName = track.SongName,
                    NewSongName = string.Join(" > ", coveredNames),
                    OldSegue = track.Segue,
                    NewSegue = setlist[run[run.Count - 1]].Segue,
                    CoveredEntryIndices = new List<int>(run),
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
                // Surface the confirmed combines (covered runs of 2+ official entries) so the
                // alias-persistence seam can record them; single-song matches are excluded.
                ConfirmedCombines = proposalSet.Proposals
                    .Where(p => p.CoveredEntryIndices.Count > 1)
                    .ToList(),
                // Per-track primary claimed position (first covered index) for the host's
                // panel-reassign back-reference (§7.5.1). Every applied proposal that claimed at
                // least one position is included.
                ClaimsByTrack = proposalSet.Proposals
                    .Where(p => p.CoveredEntryIndices.Count > 0)
                    .Select(p => new TrackClaim(p.Track, p.CoveredEntryIndices[0]))
                    .ToList(),
            };
        }
    }
}
