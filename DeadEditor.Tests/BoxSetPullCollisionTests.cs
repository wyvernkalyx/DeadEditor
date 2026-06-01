using System.Collections.Generic;
using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests
{
    public class BoxSetPullCollisionTests
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
        public void HasTracksForDate_MatchingDatePresent_True()
        {
            var tracks = Tracks("1971-02-24", "1971-04-25");
            Assert.True(BoxSetPullCollision.HasTracksForDate(tracks, "1971-04-25"));
        }

        [Fact]
        public void HasTracksForDate_DateAbsent_False()
        {
            var tracks = Tracks("1971-02-24", "1971-04-25");
            Assert.False(BoxSetPullCollision.HasTracksForDate(tracks, "1972-09-15"));
        }

        [Fact]
        public void HasTracksForDate_EmptySequence_False()
        {
            Assert.False(BoxSetPullCollision.HasTracksForDate(new List<BoxSetTrack>(), "1971-02-24"));
        }

        [Fact]
        public void HasTracksForDate_NullSequence_False()
        {
            Assert.False(BoxSetPullCollision.HasTracksForDate(null, "1971-02-24"));
        }

        [Fact]
        public void HasTracksForDate_EmptyDateMatchesNoDateRows()
        {
            // "" is the no-date group key (set by Add). A pull-side "" must collide with
            // existing no-date rows, consistent with how the grid groups them.
            var tracks = Tracks("", "1971-02-24");
            Assert.True(BoxSetPullCollision.HasTracksForDate(tracks, ""));
        }

        [Fact]
        public void HasTracksForDate_EmptyDate_NoNoDateRows_False()
        {
            var tracks = Tracks("1971-02-24", "1971-04-25");
            Assert.False(BoxSetPullCollision.HasTracksForDate(tracks, ""));
        }

        [Fact]
        public void HasTracksForDate_DatedQuery_DoesNotMatchNoDateRows()
        {
            var tracks = Tracks("", "");
            Assert.False(BoxSetPullCollision.HasTracksForDate(tracks, "1971-02-24"));
        }
    }
}
