using System.Collections.Generic;
using System.Linq;
using DeadEditor.Models;
using DeadEditor.Services;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// Pure mapping from a gdshowsdb reference setlist (<see cref="SetInfo"/> /
    /// <see cref="SetlistSong"/>, from <c>ShowLookupService</c>) to a flat list of
    /// <see cref="BoxSetTrack"/> for the box-set wizard's "pull setlist for date" action
    /// (commit G3).
    ///
    /// Deliberately WPF-free and side-effect-free (no file I/O, no singleton access) so it
    /// can be unit-tested directly — the caller fetches the setlist and passes it in.
    /// </summary>
    public static class SetlistTrackBuilder
    {
        /// <summary>
        /// Flattens <paramref name="sets"/> in set-then-song order and produces one
        /// <see cref="BoxSetTrack"/> per song. Each track is stamped with
        /// <paramref name="date"/>, gets a sequential <c>TrackNumber</c> starting at
        /// <paramref name="startNumber"/> (so an append-to-non-empty grid continues from the
        /// current max), and carries the song's <c>Segue</c> through to <c>SegueOut</c>
        /// verbatim. Set labels and the encore flag are discarded — the box-set model has no
        /// set concept. An empty input (or sets with no songs) yields an empty list.
        /// </summary>
        public static List<BoxSetTrack> BuildTracksFromSetlist(List<SetInfo> sets, string date, int startNumber)
        {
            return sets
                .SelectMany(s => s.Songs)
                .Select((song, i) => new BoxSetTrack
                {
                    TrackNumber = startNumber + i,
                    SongName = song.Name,
                    SegueOut = song.Segue,
                    Date = date
                })
                .ToList();
        }
    }
}
