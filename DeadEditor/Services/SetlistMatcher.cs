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
        /// For each track in <paramref name="tracks"/>, find the first
        /// unclaimed entry in <paramref name="setlist"/> whose canonical
        /// title matches the track's canonical. On match: set the track's
        /// SongName to the setlist's canonical, copy the segue flag, mark
        /// IsMatched=true and IsModified=true. On no match: leave the track
        /// untouched (do not flip IsMatched — Normalize's prior verdict
        /// stands).
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
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            if (setlist == null) throw new ArgumentNullException(nameof(setlist));
            if (resolveCanonical == null) throw new ArgumentNullException(nameof(resolveCanonical));

            var claimed = new HashSet<int>();
            int matched = 0;
            int segues = 0;

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
                track.SongName = setlist[matchedIndex].Canonical;
                track.Segue = setlist[matchedIndex].Segue;
                track.IsMatched = true;
                track.IsModified = true;
                matched++;
                if (setlist[matchedIndex].Segue) segues++;
            }

            return new MatchResult
            {
                MatchedCount = matched,
                SegueCount = segues,
                ClaimedPositions = claimed,
            };
        }
    }
}
