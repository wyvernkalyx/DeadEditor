using System.Collections.Generic;
using System.Linq;
using DeadEditor.Models;

namespace DeadEditor.Helpers
{
    /// <summary>
    /// One editable setlist row, projected from <c>EditSetlistView</c>'s grid. A WPF-free input
    /// record so the projection below can be unit-tested without a UI runtime.
    /// </summary>
    public readonly record struct ConcertTrackInput(string SongName, string Date, bool Segue, string Set, string Info);

    /// <summary>
    /// Pure, WPF-free, I/O-free projection of the concert setlist editor's current state into a
    /// <see cref="ConcertReference"/>, using the same rebuild logic the editor's Save applies:
    /// set regrouping (ordered by <see cref="GetSetOrder"/>), per-track date fill-in from the
    /// concert date, and contiguous track positions. <see cref="LastUpdated"/> is deliberately left
    /// at its default (never set here) so it cannot enter a verification snapshot — the diff-at-save
    /// baseline compares editor content only (concert-verification-spec.md decision 4, PINNED).
    /// EditSetlistView builds BOTH its persisted object and its baseline/diff snapshot through this
    /// one method, so the two cannot drift.
    /// </summary>
    public static class ConcertSnapshot
    {
        /// <summary>
        /// Projects editor state into a fresh <see cref="ConcertReference"/>. Only the
        /// editor-controlled fields are populated (Date, Venue, City, State, Sets, Tracks,
        /// HasSetlist); identity fields (Country, SetlistFmId, …), LastUpdated, and Verified are
        /// left default — the snapshot represents content, not provenance.
        /// </summary>
        public static ConcertReference Project(
            string date, string venue, string cityState, IReadOnlyList<ConcertTrackInput> tracks)
        {
            var concert = new ConcertReference
            {
                Date = date,
                Venue = venue
            };

            // Parse city/state from the combined text box (same split the editor's Save uses).
            var parts = cityState.Split(',', 2);
            concert.City = parts.Length > 0 ? parts[0].Trim() : "";
            concert.State = parts.Length > 1 ? parts[1].Trim() : "";

            // Rebuild sets and tracks, grouped by set in canonical set order.
            var setGroups = tracks
                .GroupBy(t => t.Set)
                .OrderBy(g => GetSetOrder(g.Key));

            int position = 0;
            foreach (var group in setGroups)
            {
                var concertSet = new ConcertSet
                {
                    Name = group.Key,
                    Songs = new List<ConcertSong>()
                };

                foreach (var track in group)
                {
                    position++;
                    var trackDate = !string.IsNullOrEmpty(track.Date) ? track.Date : date;

                    concertSet.Songs.Add(new ConcertSong
                    {
                        Name = track.SongName,
                        Date = trackDate,
                        Segue = track.Segue,
                        Info = track.Info
                    });

                    concert.Tracks.Add(new ConcertTrack
                    {
                        Position = position,
                        SongName = track.SongName,
                        Date = trackDate,
                        Segue = track.Segue,
                        Set = group.Key
                    });
                }

                concert.Sets.Add(concertSet);
            }

            concert.HasSetlist = concert.Tracks.Count > 0;
            return concert;
        }

        /// <summary>Serializes a projected concert to the canonical snapshot string.</summary>
        public static string Serialize(ConcertReference concert) => CanonicalJson.Serialize(concert);

        /// <summary>Convenience: project then serialize in one step.</summary>
        public static string Serialize(
            string date, string venue, string cityState, IReadOnlyList<ConcertTrackInput> tracks)
            => Serialize(Project(date, venue, cityState, tracks));

        /// <summary>
        /// Canonical set ordering: Set 1 → Set 2 → Set 3 → Encore → Encore 2 → anything else.
        /// Moved here from EditSetlistView so the projection is fully self-contained and testable.
        /// </summary>
        public static int GetSetOrder(string setName)
        {
            return setName switch
            {
                "Set 1" => 0,
                "Set 2" => 1,
                "Set 3" => 2,
                "Encore" => 3,
                "Encore 2" => 4,
                _ => 5
            };
        }
    }
}
