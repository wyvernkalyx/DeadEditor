using DeadEditor.Helpers;
using DeadEditor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Guards the concert-writer fix: EditSetlistView serializes a <see cref="ConcertReference"/>
/// through <see cref="CanonicalJson"/> (the same shape BoxSetService uses) so app-edited
/// concerts/ files use camelCase keys and drop the computed get-only properties
/// (FormattedLocation, SongCount) that a bare SerializeObject would have leaked. Exercises the
/// model + shared serializer directly — no EditSetlistView/WPF instantiation.
/// </summary>
public class ConcertReferenceSerializationTests
{
    private static ConcertReference SampleConcert() => new()
    {
        Date = "1971-05-30",
        Venue = "Winterland",
        City = "San Francisco",
        State = "CA",
        Country = "US",
        SetlistFmId = "abc123",
        SetlistFmUrl = "https://www.setlist.fm/x",
        LastUpdated = "2026-06-09T00:00:00Z",
        HasSetlist = true,
        Sets =
        {
            new ConcertSet
            {
                Name = "Set 1",
                Songs =
                {
                    new ConcertSong { Name = "Bertha", Date = "1971-05-30", Segue = true, Info = ">" },
                    new ConcertSong { Name = "Me and Bobby McGee", Date = "1971-05-30", Segue = false, Info = "" },
                }
            }
        },
        Tracks =
        {
            new ConcertTrack { Position = 1, SongName = "Bertha", Date = "1971-05-30", Segue = true, Set = "Set 1" },
            new ConcertTrack { Position = 2, SongName = "Me and Bobby McGee", Date = "1971-05-30", Segue = false, Set = "Set 1" },
        }
    };

    [Fact]
    public void Serialization_UsesCamelCaseKeys()
    {
        var json = CanonicalJson.Serialize(SampleConcert());
        var root = JObject.Parse(json);

        // Top-level camelCase keys present.
        Assert.NotNull(root["date"]);
        Assert.NotNull(root["venue"]);
        Assert.NotNull(root["setlistFmId"]);
        Assert.NotNull(root["hasSetlist"]);
        Assert.NotNull(root["sets"]);
        Assert.NotNull(root["tracks"]);

        // Nested camelCase keys present.
        var track = Assert.IsType<JObject>(Assert.IsType<JArray>(root["tracks"])[0]);
        Assert.NotNull(track["songName"]);
        Assert.NotNull(track["position"]);
        Assert.NotNull(track["segue"]);
    }

    [Fact]
    public void Serialization_DropsComputedKeys()
    {
        var json = CanonicalJson.Serialize(SampleConcert());

        // Neither casing of the computed get-only properties may appear anywhere.
        Assert.DoesNotContain("formattedLocation", json);
        Assert.DoesNotContain("FormattedLocation", json);
        Assert.DoesNotContain("songCount", json);
        Assert.DoesNotContain("SongCount", json);
    }

    [Fact]
    public void Concert_SurvivesRoundTrip()
    {
        var json = CanonicalJson.Serialize(SampleConcert());
        var restored = JsonConvert.DeserializeObject<ConcertReference>(json, CanonicalJson.Settings);

        Assert.NotNull(restored);
        Assert.Equal("1971-05-30", restored!.Date);
        Assert.Equal("Winterland", restored.Venue);
        Assert.Equal("San Francisco", restored.City);
        Assert.Equal("CA", restored.State);
        Assert.Equal("US", restored.Country);
        Assert.Equal("abc123", restored.SetlistFmId);
        Assert.True(restored.HasSetlist);

        Assert.Single(restored.Sets);
        Assert.Equal("Set 1", restored.Sets[0].Name);
        Assert.Equal(2, restored.Sets[0].Songs.Count);
        Assert.Equal("Bertha", restored.Sets[0].Songs[0].Name);
        Assert.True(restored.Sets[0].Songs[0].Segue);

        Assert.Equal(2, restored.Tracks.Count);
        var first = restored.Tracks[0];
        Assert.Equal(1, first.Position);
        Assert.Equal("Bertha", first.SongName);
        Assert.Equal("1971-05-30", first.Date);
        Assert.True(first.Segue);
        Assert.Equal("Set 1", first.Set);

        // Computed properties still work after round-trip (recomputed, not persisted).
        Assert.Equal("San Francisco, CA", restored.FormattedLocation);
        Assert.Equal(2, restored.SongCount);
    }
}
