using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests
{
    /// <summary>
    /// Coverage for <see cref="BoxSetVerifyGate.Evaluate"/> — the pure verify gate. Exercises the
    /// passing case and every failing branch in the documented order, plus the order-precedence
    /// edge (Name is reported before a later failure). See
    /// documentation/box-set-verification-spec.md decision 3.
    /// </summary>
    public class BoxSetVerifyGateTests
    {
        /// <summary>A minimal box that passes every gate condition.</summary>
        private static BoxSetDefinition ValidDef()
        {
            var def = new BoxSetDefinition
            {
                Name = "Europe '72: The Complete Recordings",
                ReleaseDate = "2011-09-13"
            };
            def.Tracks.Add(new BoxSetTrack { TrackNumber = 1, SongName = "Cumberland Blues", Date = "1972-05-04" });
            return def;
        }

        [Fact]
        public void CompleteBox_CanVerify()
        {
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(ValidDef());
            Assert.True(canVerify);
            Assert.Null(reason);
        }

        [Fact]
        public void BlankName_Blocked()
        {
            var def = ValidDef();
            def.Name = "";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Name is required", reason);
        }

        [Fact]
        public void WhitespaceName_Blocked()
        {
            var def = ValidDef();
            def.Name = "   ";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Name is required", reason);
        }

        [Fact]
        public void EmptyReleaseDate_Blocked()
        {
            var def = ValidDef();
            def.ReleaseDate = "";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Release date must be yyyy-MM-dd", reason);
        }

        [Fact]
        public void MalformedReleaseDate_Blocked()
        {
            var def = ValidDef();
            def.ReleaseDate = "9/13/2011";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Release date must be yyyy-MM-dd", reason);
        }

        [Fact]
        public void NoTracks_Blocked()
        {
            var def = ValidDef();
            def.Tracks.Clear();
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Add at least one track", reason);
        }

        [Fact]
        public void TrackWithBlankSongName_Blocked()
        {
            var def = ValidDef();
            def.Tracks[0].SongName = "";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Track 1 has no song name", reason);
        }

        [Fact]
        public void TrackWithWhitespaceSongName_Blocked()
        {
            var def = ValidDef();
            def.Tracks[0].SongName = "  ";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Track 1 has no song name", reason);
        }

        [Fact]
        public void TrackWithNoDate_Blocked()
        {
            var def = ValidDef();
            def.Tracks[0].Date = "";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Track 1 has no date", reason);
        }

        [Fact]
        public void TrackWithInvalidDate_Blocked()
        {
            var def = ValidDef();
            def.Tracks[0].Date = "1972-5-4";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Track 1 date is invalid", reason);
        }

        [Fact]
        public void OffendingTrackNumber_NamedInReason()
        {
            // The reason names the offending track's TrackNumber (opaque, not a row index) — the
            // first SongName failure wins, here track #207.
            var def = ValidDef();
            def.Tracks.Add(new BoxSetTrack { TrackNumber = 207, SongName = "", Date = "1972-05-11" });
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Track 207 has no song name", reason);
        }

        [Fact]
        public void ConditionsCheckedInOrder_NameBeforeTracks()
        {
            // Both Name and a track are bad; Name is checked first, so its reason wins.
            var def = ValidDef();
            def.Name = "";
            def.Tracks[0].SongName = "";
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Name is required", reason);
        }

        [Fact]
        public void AllSongNamesCheckedBeforeAnyDate()
        {
            // A later track is missing a date AND an earlier track is missing a song name. The
            // SongName pass runs entirely before the Date pass, so the song-name reason wins.
            var def = ValidDef();
            def.Tracks[0].SongName = "";
            def.Tracks.Add(new BoxSetTrack { TrackNumber = 2, SongName = "Jack Straw", Date = "" });
            var (canVerify, reason) = BoxSetVerifyGate.Evaluate(def);
            Assert.False(canVerify);
            Assert.Equal("Track 1 has no song name", reason);
        }
    }
}
