using System;
using System.Collections.Generic;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure computation of the recording-to-canon "add as extras" write-back offer population
    /// (setlist-extras-writeback-spec.md §6 P2, slice 7a).
    ///
    /// The trigger is deliberately NOT <c>unmatchedCount &gt; 0</c>. Extra-typed entries are
    /// matcher-excluded by D2, so a track sitting over an extra (or over an already-claimed song of
    /// the same title) stays unmatched BY DESIGN — a naive count would re-offer the same tracks
    /// forever. The population is instead the post-Match unmatched tracks whose canonical song title
    /// appears in NO setlist entry of ANY type (song AND extra), compared with the same
    /// OrdinalIgnoreCase canonical equality the matcher uses (SetlistMatcher.cs:193). A
    /// recognized-but-unclaimed track — its title exists somewhere in the typed setlist — is
    /// excluded; only genuinely off-list titles are offered as new extras.
    /// </summary>
    public static class WriteBackOffer
    {
        /// <summary>One proposed extra: an unmatched track with no setlist home, and its canonical title.</summary>
        public sealed class OfferCandidate
        {
            public TrackInfo Track { get; }
            public string CanonicalName { get; }

            public OfferCandidate(TrackInfo track, string canonicalName)
            {
                Track = track;
                CanonicalName = canonicalName;
            }
        }

        /// <summary>
        /// Returns one candidate per post-Match unmatched track whose resolved canonical title is
        /// absent from every entry's <see cref="SetlistEntryVm.Canonical"/> (any type). Candidate
        /// order follows <paramref name="tracks"/>; the population is per-track with no dedupe, so two
        /// genuinely off-list "Tuning" tracks yield two candidates (two tuning events).
        /// </summary>
        /// <param name="tracks">All album tracks after a Match run; matched tracks carry <c>IsMatched == true</c>.</param>
        /// <param name="entries">The full typed setlist projection — songs AND extras.</param>
        /// <param name="resolveCanonical">
        /// The matcher's direct-match resolver (Normalize → GetOfficialTitle, may echo its input on a
        /// miss). Passing the same resolver the matcher used keeps the comparison consistent.
        /// </param>
        public static IReadOnlyList<OfferCandidate> ComputeOffer(
            IReadOnlyList<TrackInfo> tracks,
            IReadOnlyList<SetlistEntryVm> entries,
            Func<string, string?> resolveCanonical)
        {
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (resolveCanonical == null) throw new ArgumentNullException(nameof(resolveCanonical));

            // Canonical titles present in the setlist, ANY type (song OR extra). OrdinalIgnoreCase to
            // mirror SetlistMatcher's claim equality exactly.
            var entryCanonicals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Canonical))
                    entryCanonicals.Add(entry.Canonical);
            }

            var result = new List<OfferCandidate>();
            foreach (var track in tracks)
            {
                if (track == null) continue;

                // Post-Match unmatched only: a matched track already has a home.
                if (track.IsMatched == true) continue;

                var name = track.SongName;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var canonical = resolveCanonical(name);
                if (string.IsNullOrWhiteSpace(canonical)) continue;

                // Recognized (title exists as some entry, any type) → not "not in the setlist" → skip.
                if (entryCanonicals.Contains(canonical)) continue;

                result.Add(new OfferCandidate(track, canonical));
            }

            return result;
        }
    }
}
