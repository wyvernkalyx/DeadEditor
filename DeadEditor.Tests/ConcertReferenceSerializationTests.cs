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

    // ===== Alias setlists (alias-setlists-spec.md §3.2) =====

    private static ConcertReference SampleWithAlias()
    {
        var concert = SampleConcert();
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Id = "ofv-1",
            Label = "One From The Vault",
            Entries =
            {
                new AliasEntry { CoveredOfficialIndices = { 1, 2 } },
                new AliasEntry { CoveredOfficialIndices = { 10, 11 } },
            }
        });
        return concert;
    }

    [Fact]
    public void AliasSetlists_RoundTrip_PreservesCoverageInOrder()
    {
        var json = CanonicalJson.Serialize(SampleWithAlias());
        var restored = JsonConvert.DeserializeObject<ConcertReference>(json, CanonicalJson.Settings);

        Assert.NotNull(restored);
        var alias = Assert.Single(restored!.AliasSetlists);
        Assert.Equal("ofv-1", alias.Id);
        Assert.Equal("One From The Vault", alias.Label);
        Assert.Equal(2, alias.Entries.Count);
        Assert.Equal(new[] { 1, 2 }, alias.Entries[0].CoveredOfficialIndices);
        Assert.Equal(new[] { 10, 11 }, alias.Entries[1].CoveredOfficialIndices);
    }

    [Fact]
    public void AliasSetlists_LegacyRead_IsEmptyNonNull()
    {
        // A concert JSON with NO aliasSetlists key (every existing fetcher-sourced file). Deserialize
        // through the real load path's default settings (ConcertLookupService.LoadConcerts).
        const string legacy = """
            { "date": "1971-05-30", "venue": "Winterland", "hasSetlist": true }
            """;

        var restored = JsonConvert.DeserializeObject<ConcertReference>(legacy);

        Assert.NotNull(restored);
        Assert.NotNull(restored!.AliasSetlists);
        Assert.Empty(restored.AliasSetlists);
    }

    [Fact]
    public void AliasSetlists_Serialize_UsesCamelCaseKeys()
    {
        var json = CanonicalJson.Serialize(SampleWithAlias());
        var root = JObject.Parse(json);

        var aliases = Assert.IsType<JArray>(root["aliasSetlists"]);
        var alias = Assert.IsType<JObject>(aliases[0]);
        var entries = Assert.IsType<JArray>(alias["entries"]);
        var entry = Assert.IsType<JObject>(entries[0]);
        Assert.NotNull(entry["coveredOfficialIndices"]);

        // PascalCase variants must not leak.
        Assert.DoesNotContain("AliasSetlists", json);
        Assert.DoesNotContain("CoveredOfficialIndices", json);
    }

    [Fact]
    public void AliasSetlists_Empty_OmittedFromJson()
    {
        var json = CanonicalJson.Serialize(SampleConcert());

        // Neither casing may appear when the list is empty (ShouldSerializeAliasSetlists).
        Assert.DoesNotContain("aliasSetlists", json);
        Assert.DoesNotContain("AliasSetlists", json);
    }

    [Fact]
    public void AliasSetlists_Populated_PresentInJson()
    {
        var json = CanonicalJson.Serialize(SampleWithAlias());

        Assert.Contains("aliasSetlists", json);
    }
}
