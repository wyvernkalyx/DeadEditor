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
    }
}
