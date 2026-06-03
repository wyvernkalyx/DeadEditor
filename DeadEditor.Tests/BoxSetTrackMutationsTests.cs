using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests
{
    public class BoxSetTrackMutationsTests
    {
        private static List<BoxSetTrack> Tracks(params string[] dates)
        {
            var list = new List<BoxSetTrack>();
            int n = 1;
            foreach (var d in dates)
                list.Add(new BoxSetTrack { TrackNumber = n++, SongName = "Song", Date = d });
            return list;
        }

        [Fact]
        public void RemoveTracksForDate_RemovesOnlyMatchingRows()
        {
            var tracks = Tracks("1971-02-24", "1971-04-25", "1971-02-24");
            BoxSetTrackMutations.RemoveTracksForDate(tracks, "1971-02-24");
            Assert.Single(tracks);
            Assert.Equal("1971-04-25", tracks[0].Date);
        }

        [Fact]
        public void RemoveTracksForDate_ReturnsRemovedCount()
        {
            var tracks = Tracks("1971-02-24", "1971-04-25", "1971-02-24");
            Assert.Equal(2, BoxSetTrackMutations.RemoveTracksForDate(tracks, "1971-02-24"));
        }

        [Fact]
        public void RemoveTracksForDate_NoMatch_ReturnsZero_ListUnchanged()
        {
            var tracks = Tracks("1971-02-24", "1971-04-25");
            Assert.Equal(0, BoxSetTrackMutations.RemoveTracksForDate(tracks, "1972-09-15"));
            Assert.Equal(2, tracks.Count);
        }

        [Fact]
        public void RemoveTracksForDate_EmptyDate_RemovesOnlyNoDateRows()
        {
            var tracks = Tracks("", "1971-02-24", "");
            Assert.Equal(2, BoxSetTrackMutations.RemoveTracksForDate(tracks, ""));
            Assert.Single(tracks);
            Assert.Equal("1971-02-24", tracks[0].Date);
        }

        [Fact]
        public void RemoveTracksForDate_DatedQuery_DoesNotTouchNoDateRows()
        {
            var tracks = Tracks("", "1971-02-24", "");
            Assert.Equal(1, BoxSetTrackMutations.RemoveTracksForDate(tracks, "1971-02-24"));
            Assert.Equal(2, tracks.Count);
            Assert.All(tracks, t => Assert.Equal("", t.Date));
        }

        [Fact]
        public void RemoveTracksForDate_NullList_ReturnsZero_NoThrow()
        {
            Assert.Equal(0, BoxSetTrackMutations.RemoveTracksForDate(null, "1971-02-24"));
        }

        // ===== RenumberByDate (date-keyed 1..N, no-date last) =====

        [Fact]
        public void RenumberByDate_OutOfOrderDates_GetChronologicalNumbers()
        {
            // 12-09 pulled first (1,2), 12-08 pulled second (3,4) — list order is non-chronological.
            var tracks = new List<BoxSetTrack>
            {
                new BoxSetTrack { TrackNumber = 1, SongName = "A", Date = "1971-12-09" },
                new BoxSetTrack { TrackNumber = 2, SongName = "B", Date = "1971-12-09" },
                new BoxSetTrack { TrackNumber = 3, SongName = "C", Date = "1971-12-08" },
                new BoxSetTrack { TrackNumber = 4, SongName = "D", Date = "1971-12-08" },
            };

            BoxSetTrackMutations.RenumberByDate(tracks);

            // The earlier date (12-08) now holds the lower numbers.
            var byDate = tracks.ToDictionary(t => t.SongName, t => (t.Date, t.TrackNumber));
            Assert.Equal(("1971-12-08", 1), byDate["C"]);
            Assert.Equal(("1971-12-08", 2), byDate["D"]);
            Assert.Equal(("1971-12-09", 3), byDate["A"]);
            Assert.Equal(("1971-12-09", 4), byDate["B"]);
        }

        [Fact]
        public void RenumberByDate_PreservesWithinDateOrder()
        {
            // Within 12-08, original ascending TrackNumber order (10 before 20) must survive.
            var tracks = new List<BoxSetTrack>
            {
                new BoxSetTrack { TrackNumber = 20, SongName = "second", Date = "1971-12-08" },
                new BoxSetTrack { TrackNumber = 10, SongName = "first", Date = "1971-12-08" },
            };

            BoxSetTrackMutations.RenumberByDate(tracks);

            Assert.Equal(1, tracks.Single(t => t.SongName == "first").TrackNumber);
            Assert.Equal(2, tracks.Single(t => t.SongName == "second").TrackNumber);
        }

        [Fact]
        public void RenumberByDate_ClosesGaps()
        {
            // Gappy numbers (a middle date was deleted) become contiguous 1..N.
            var tracks = new List<BoxSetTrack>
            {
                new BoxSetTrack { TrackNumber = 1, SongName = "A", Date = "1971-12-08" },
                new BoxSetTrack { TrackNumber = 7, SongName = "B", Date = "1971-12-09" },
                new BoxSetTrack { TrackNumber = 8, SongName = "C", Date = "1971-12-09" },
            };

            BoxSetTrackMutations.RenumberByDate(tracks);

            Assert.Equal(new[] { 1, 2, 3 }, tracks.Select(t => t.TrackNumber).OrderBy(n => n));
        }

        [Fact]
        public void RenumberByDate_AlreadyOrdered_IsIdempotent()
        {
            var tracks = new List<BoxSetTrack>
            {
                new BoxSetTrack { TrackNumber = 1, SongName = "A", Date = "1971-12-08" },
                new BoxSetTrack { TrackNumber = 2, SongName = "B", Date = "1971-12-08" },
                new BoxSetTrack { TrackNumber = 3, SongName = "C", Date = "1971-12-09" },
            };

            BoxSetTrackMutations.RenumberByDate(tracks);

            Assert.Equal(new[] { 1, 2, 3 }, tracks.Select(t => t.TrackNumber));
            Assert.Equal(new[] { "A", "B", "C" }, tracks.Select(t => t.SongName));
        }

        [Fact]
        public void RenumberByDate_NoDateRows_SortLast()
        {
            var tracks = new List<BoxSetTrack>
            {
                new BoxSetTrack { TrackNumber = 1, SongName = "orphan", Date = "" },
                new BoxSetTrack { TrackNumber = 2, SongName = "dated", Date = "1971-12-08" },
            };

            BoxSetTrackMutations.RenumberByDate(tracks);

            Assert.Equal(1, tracks.Single(t => t.SongName == "dated").TrackNumber);
            Assert.Equal(2, tracks.Single(t => t.SongName == "orphan").TrackNumber);
        }

        [Fact]
        public void RenumberByDate_ListOrderMatchesTrackNumber()
        {
            // D4: after renumber the backing list element order equals ascending TrackNumber.
            var tracks = new List<BoxSetTrack>
            {
                new BoxSetTrack { TrackNumber = 1, SongName = "A", Date = "1971-12-09" },
                new BoxSetTrack { TrackNumber = 2, SongName = "orphan", Date = "" },
                new BoxSetTrack { TrackNumber = 3, SongName = "B", Date = "1971-12-08" },
            };

            BoxSetTrackMutations.RenumberByDate(tracks);

            Assert.Equal(new[] { 1, 2, 3 }, tracks.Select(t => t.TrackNumber));
            // chronological with the orphan last: 12-08 (B), 12-09 (A), then no-date (orphan).
            Assert.Equal(new[] { "B", "A", "orphan" }, tracks.Select(t => t.SongName));
        }

        [Fact]
        public void RenumberByDate_NullList_NoThrow()
        {
            BoxSetTrackMutations.RenumberByDate(null);
        }

        [Fact]
        public void RenumberByDate_EmptyList_NoThrow()
        {
            var tracks = new List<BoxSetTrack>();
            BoxSetTrackMutations.RenumberByDate(tracks);
            Assert.Empty(tracks);
        }
    }
}
