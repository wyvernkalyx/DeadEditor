using System.Collections.Generic;
using System.Linq;
using DeadEditor.Models;
using DeadEditor.Services;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure mapping from a <see cref="ConcertReference"/> (loaded from
    /// <c>Data/concerts/</c> via <c>ConcertLookupService</c>) to the
    /// <see cref="SetInfo"/> / <see cref="SetlistSong"/> shape that
    /// <c>ShowLookupService.GetSetlist</c> historically returned from
    /// <c>shows.json</c>.
    ///
    /// This is the seam that lets <c>ShowLookupService</c> source setlist data from
    /// the richer, setlist-complete <c>concerts/</c> store instead of <c>shows.json</c>'s
    /// venue-only stubs, without changing any of the three downstream consumers
    /// (box-set pull, Import "Match Setlist", Edit-Metadata "Match Setlist").
    ///
    /// Mapping notes:
    /// - Source is <see cref="ConcertReference.Sets"/>, which is reliably populated for
    ///   every setlist-bearing concert file (verified across the bundled dataset; no
    ///   file carries tracks without sets). Set boundaries are preserved so the
    ///   set-size arithmetic in <c>GetDiscTrack</c> stays correct.
    /// - Per-song <c>Segue</c> is carried through verbatim.
    /// - <c>ConcertSet</c> has no encore flag; <see cref="SetInfo.Encore"/> is derived
    ///   from the set name (case-insensitive "Encore"). No live consumer reads it today.
    ///
    /// Deliberately WPF-free and side-effect-free (no file I/O, no singleton access) so
    /// it can be unit-tested directly — the caller fetches the concert and passes it in.
    /// </summary>
    public static class ConcertSetlistAdapter
    {
        /// <summary>
        /// Maps a concert reference to a list of <see cref="SetInfo"/>, preserving set
        /// boundaries and per-song segues. Returns <c>null</c> when there is no setlist
        /// (null reference, or no sets / no songs) — mirroring the historical
        /// "no setlist data" contract of <c>GetSetlist</c>.
        /// </summary>
        public static List<SetInfo>? ToSetInfoList(ConcertReference? concert)
        {
            if (concert?.Sets == null || concert.Sets.Count == 0)
                return null;

            var result = concert.Sets
                .Select(set => new SetInfo
                {
                    Label = set.Name,
                    Encore = string.Equals(set.Name, "Encore", System.StringComparison.OrdinalIgnoreCase),
                    Songs = (set.Songs ?? new List<ConcertSong>())
                        .Select(song => new SetlistSong
                        {
                            Name = song.Name,
                            Segue = song.Segue
                        })
                        .ToList()
                })
                .ToList();

            // Guard the degenerate case of sets that contain no songs at all —
            // treat it as "no setlist" so callers see the same null they used to.
            return result.Any(s => s.Songs.Count > 0) ? result : null;
        }
    }
}
