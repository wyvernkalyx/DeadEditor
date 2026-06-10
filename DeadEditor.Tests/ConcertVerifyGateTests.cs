using DeadEditor.Helpers;
using DeadEditor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DeadEditor.Tests
{
    /// <summary>
    /// Coverage for <see cref="ConcertVerifyGate.Evaluate"/> — the pure concert verify gate.
    /// Exercises the passing case and every failing branch in the documented order (Date, Venue,
    /// track count, every track SongName), the order-precedence edge, and the defensive null cases.
    /// Plus a serialization guard that the new <see cref="ConcertReference.Verified"/> flag survives
    /// a CanonicalJson round-trip as camelCase and defaults to false for legacy files with no key.
    /// See documentation/concert-verification-spec.md decisions 2–3.
    /// </summary>
    public class ConcertVerifyGateTests
    {
        /// <summary>A minimal concert that passes every gate condition.</summary>
        private static ConcertReference ValidConcert()
        {
            var concert = new ConcertReference
            {
                Date = "1971-05-30",
                Venue = "Winterland"
            };
            concert.Tracks.Add(new ConcertTrack { Position = 1, SongName = "Bertha", Date = "1971-05-30", Set = "Set 1" });
            return concert;
        }

        [Fact]
        public void CompleteConcert_CanVerify()
        {
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(ValidConcert());
            Assert.True(canVerify);
            Assert.Null(reason);
        }

        [Fact]
        public void EmptyDate_Blocked()
        {
            var concert = ValidConcert();
            concert.Date = "";
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Date must be yyyy-MM-dd", reason);
        }

        [Fact]
        public void MalformedDate_Blocked()
        {
            var concert = ValidConcert();
            concert.Date = "5/30/1971";
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Date must be yyyy-MM-dd", reason);
        }

        [Fact]
        public void EmptyVenue_Blocked()
        {
            var concert = ValidConcert();
            concert.Venue = "";
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Venue is required", reason);
        }

        [Fact]
        public void WhitespaceVenue_Blocked()
        {
            var concert = ValidConcert();
            concert.Venue = "   ";
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Venue is required", reason);
        }

        [Fact]
        public void NoTracks_Blocked()
        {
            var concert = ValidConcert();
            concert.Tracks.Clear();
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Add at least one track", reason);
        }

        [Fact]
        public void NullTracks_BlockedGracefully()
        {
            var concert = ValidConcert();
            concert.Tracks = null!;
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Add at least one track", reason);
        }

        [Fact]
        public void TrackWithBlankSongName_Blocked()
        {
            var concert = ValidConcert();
            concert.Tracks[0].SongName = "";
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Track 1 has no song name", reason);
        }

        [Fact]
        public void TrackWithWhitespaceSongName_Blocked()
        {
            var concert = ValidConcert();
            concert.Tracks[0].SongName = "  ";
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Track 1 has no song name", reason);
        }

        [Fact]
        public void OffendingTrackPosition_NamedInReason()
        {
            // The reason names the offending track's Position — the first SongName failure wins,
            // here track at position 5.
            var concert = ValidConcert();
            concert.Tracks.Add(new ConcertTrack { Position = 5, SongName = "", Date = "1971-05-30", Set = "Set 2" });
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Track 5 has no song name", reason);
        }

        [Fact]
        public void NullConcert_BlockedGracefully()
        {
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(null!);
            Assert.False(canVerify);
            Assert.Equal("Concert data is missing", reason);
        }

        [Fact]
        public void ConditionsCheckedInOrder_DateBeforeVenue()
        {
            // Both Date and Venue are bad; Date is checked first, so its reason wins.
            var concert = ValidConcert();
            concert.Date = "";
            concert.Venue = "";
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Date must be yyyy-MM-dd", reason);
        }

        [Fact]
        public void ConditionsCheckedInOrder_VenueBeforeTracks()
        {
            // Both Venue and track-presence are bad; Venue is checked first, so its reason wins.
            var concert = ValidConcert();
            concert.Venue = "";
            concert.Tracks.Clear();
            var (canVerify, reason) = ConcertVerifyGate.Evaluate(concert);
            Assert.False(canVerify);
            Assert.Equal("Venue is required", reason);
        }

        // ===== Serialization of the new Verified flag =====

        [Fact]
        public void Verified_SerializesAsCamelCaseAndSurvivesRoundTrip()
        {
            var concert = ValidConcert();
            concert.Verified = true;

            var json = CanonicalJson.Serialize(concert);
            var root = JObject.Parse(json);

            // camelCase key present and true.
            Assert.NotNull(root["verified"]);
            Assert.True(root["verified"]!.Value<bool>());

            var restored = JsonConvert.DeserializeObject<ConcertReference>(json, CanonicalJson.Settings);
            Assert.NotNull(restored);
            Assert.True(restored!.Verified);
        }

        [Fact]
        public void Verified_DefaultsToFalse_WhenKeyAbsent()
        {
            // A legacy fetcher-sourced file with no "verified" key must deserialize to false.
            var json = @"{ ""date"": ""1971-05-30"", ""venue"": ""Winterland"", ""tracks"": [] }";

            var restored = JsonConvert.DeserializeObject<ConcertReference>(json, CanonicalJson.Settings);

            Assert.NotNull(restored);
            Assert.False(restored!.Verified);
        }
    }
}
