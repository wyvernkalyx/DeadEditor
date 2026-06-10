using DeadEditor.Helpers;
using DeadEditor.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DeadEditor.Tests
{
    /// <summary>
    /// Coverage for <see cref="ConcertSnapshot"/> — the pure projection EditSetlistView builds both
    /// its persisted concert and its diff-at-save baseline through. Guards the spec decision-4
    /// invariants: deterministic output, per-track date fill-in, LastUpdated excluded, and a no-edit
    /// snapshot pair that is not dirty. See documentation/concert-verification-spec.md decision 4.
    /// </summary>
    public class ConcertSnapshotTests
    {
        private static IReadOnlyList<ConcertTrackInput> SampleTracks() => new List<ConcertTrackInput>
        {
            new("Bertha", "1971-05-30", true, "Set 1", ">"),
            new("Me and Bobby McGee", "", false, "Set 1", ""),
        };

        [Fact]
        public void Project_FillsTrackDateFromConcertDate_WhenTrackDateEmpty()
        {
            var concert = ConcertSnapshot.Project("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());

            Assert.Equal(2, concert.Tracks.Count);
            Assert.Equal("1971-05-30", concert.Tracks[0].Date); // explicit date preserved
            Assert.Equal("1971-05-30", concert.Tracks[1].Date); // empty filled from concert date
            // Same fill-in on the Sets/Songs projection.
            Assert.Equal("1971-05-30", concert.Sets[0].Songs[1].Date);
        }

        [Fact]
        public void Project_ParsesCityState_AndSetsHasSetlist()
        {
            var concert = ConcertSnapshot.Project("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());

            Assert.Equal("Winterland", concert.Venue);
            Assert.Equal("San Francisco", concert.City);
            Assert.Equal("CA", concert.State);
            Assert.True(concert.HasSetlist);
        }

        [Fact]
        public void Project_EmptyTracks_HasSetlistFalse()
        {
            var concert = ConcertSnapshot.Project("1971-05-30", "Winterland", "San Francisco, CA",
                new List<ConcertTrackInput>());

            Assert.Empty(concert.Tracks);
            Assert.False(concert.HasSetlist);
        }

        [Fact]
        public void Serialize_DeterministicForIdenticalInput()
        {
            var a = ConcertSnapshot.Serialize("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());
            var b = ConcertSnapshot.Serialize("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());

            Assert.Equal(a, b);
        }

        [Fact]
        public void Serialize_ZeroesLastUpdated()
        {
            var json = ConcertSnapshot.Serialize("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());
            var root = JObject.Parse(json);

            // LastUpdated is left at its zeroed default (PINNED exclusion) — never a real timestamp,
            // so it cancels out on both sides of the diff. It is the empty string default here (the
            // model initializes it to ""; NullValueHandling.Ignore only drops nulls), never absent
            // with a value. The invariant that matters: no UtcNow timestamp leaks into the snapshot.
            Assert.Equal("", root["lastUpdated"]?.Value<string>() ?? "");
            Assert.Null(root["LastUpdated"]); // never the PascalCase form
        }

        [Fact]
        public void NoEditInvariant_TwoSnapshotsOfSameState_NotDirty()
        {
            var baseline = ConcertSnapshot.Serialize("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());
            var current = ConcertSnapshot.Serialize("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());

            Assert.False(EditUnverifyRule.IsDirty(baseline, current));
        }

        [Fact]
        public void EditedState_IsDirty()
        {
            var baseline = ConcertSnapshot.Serialize("1971-05-30", "Winterland", "San Francisco, CA", SampleTracks());

            var edited = new List<ConcertTrackInput>(SampleTracks()) { new("Jack Straw", "1971-05-30", false, "Set 1", "") };
            var current = ConcertSnapshot.Serialize("1971-05-30", "Winterland", "San Francisco, CA", edited);

            Assert.True(EditUnverifyRule.IsDirty(baseline, current));
        }

        [Fact]
        public void GetSetOrder_OrdersStructuralSetsThenOthersLast()
        {
            Assert.Equal(0, ConcertSnapshot.GetSetOrder("Set 1"));
            Assert.Equal(1, ConcertSnapshot.GetSetOrder("Set 2"));
            Assert.Equal(3, ConcertSnapshot.GetSetOrder("Encore"));
            Assert.Equal(5, ConcertSnapshot.GetSetOrder("Soundcheck"));
        }
    }
}
