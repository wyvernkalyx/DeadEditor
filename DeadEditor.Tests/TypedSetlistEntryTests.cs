using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for the typed-setlist-entry model + serialization (setlist-extras-writeback-spec.md
/// slice 1, D1/D3/D8a). Exercises the pure layers only — model serialization, the concerts→projection
/// adapter chain, and <see cref="ConcertSnapshot"/> — with no WPF, no matcher, no editor, no gate
/// changes (those are later slices). Two invariants are load-bearing here:
///   * Empty-omit: an untyped ("song") entry serializes with NO "type" key, so the 2,293 existing
///     concert files stay byte-stable and no verified baseline spuriously flips.
///   * Lockstep (D8a): the same type lands on both sets[] (ConcertSong) and tracks[] (ConcertTrack).
/// </summary>
public class TypedSetlistEntryTests
{
    // ===== Model: default + serialization empty-omit / presence =====

    [Fact]
    public void ConcertSong_And_ConcertTrack_DefaultType_IsSong()
    {
        Assert.Equal("song", new ConcertSong().Type);
        Assert.Equal("song", new ConcertTrack().Type);
    }

    [Fact]
    public void Serialize_DefaultSongType_OmitsTypeKey()
    {
        var concert = new ConcertReference
        {
            Date = "1971-05-30",
            Venue = "Winterland",
            HasSetlist = true,
            Sets = { new ConcertSet { Name = "Set 1", Songs = { new ConcertSong { Name = "Bertha" } } } },
            Tracks = { new ConcertTrack { Position = 1, SongName = "Bertha", Set = "Set 1" } },
        };

        var json = CanonicalJson.Serialize(concert);

        // Neither casing of the type key may appear when every entry is the "song" default.
        Assert.DoesNotContain("\"type\"", json);
        Assert.DoesNotContain("\"Type\"", json);
    }

    [Fact]
    public void Serialize_ExtraType_PresentOnBothShapes()
    {
        var concert = new ConcertReference
        {
            Date = "1971-02-21",
            Venue = "Capitol Theatre",
            HasSetlist = true,
            Sets = { new ConcertSet { Name = "Set 1", Songs = { new ConcertSong { Name = "Ripple", Type = "false-start" } } } },
            Tracks = { new ConcertTrack { Position = 1, SongName = "Ripple", Set = "Set 1", Type = "false-start" } },
        };

        var root = JObject.Parse(CanonicalJson.Serialize(concert));

        // D8a lockstep: the type rides on BOTH the nested sets[] entry and the flattened tracks[] entry.
        var song = (JObject)((JArray)((JObject)((JArray)root["sets"]!)[0])["songs"]!)[0];
        var track = (JObject)((JArray)root["tracks"]!)[0];
        Assert.Equal("false-start", (string?)song["type"]);
        Assert.Equal("false-start", (string?)track["type"]);
    }

    [Fact]
    public void Deserialize_MissingType_DefaultsToSong()
    {
        // A legacy fetcher-sourced file carries no "type" key on its entries.
        const string legacy = """
            {
              "date": "1971-05-30",
              "venue": "Winterland",
              "hasSetlist": true,
              "sets": [ { "name": "Set 1", "songs": [ { "name": "Bertha", "segue": true } ] } ],
              "tracks": [ { "position": 1, "songName": "Bertha", "segue": true, "set": "Set 1" } ]
            }
            """;

        var restored = JsonConvert.DeserializeObject<ConcertReference>(legacy)!;

        Assert.Equal("song", restored.Sets[0].Songs[0].Type);
        Assert.Equal("song", restored.Tracks[0].Type);
    }

    [Fact]
    public void RoundTrip_ExtraType_Preserved_OnBothShapes()
    {
        var concert = new ConcertReference
        {
            Date = "1971-02-21",
            Venue = "Capitol Theatre",
            HasSetlist = true,
            Sets = { new ConcertSet { Name = "Set 1", Songs = { new ConcertSong { Name = "Tuning", Type = "tuning" } } } },
            Tracks = { new ConcertTrack { Position = 1, SongName = "Tuning", Set = "Set 1", Type = "tuning" } },
        };

        var json = CanonicalJson.Serialize(concert);
        var restored = JsonConvert.DeserializeObject<ConcertReference>(json, CanonicalJson.Settings)!;

        Assert.Equal("tuning", restored.Sets[0].Songs[0].Type);
        Assert.Equal("tuning", restored.Tracks[0].Type);
    }

    /// <summary>
    /// GATE 3 (round-trip stability): a representative untyped concert deserialized and RE-serialized
    /// in memory (never a disk write to Data/concerts) must gain no "type" key — proof that merely
    /// opening/saving an existing file cannot materialize "type":"song" noise.
    /// </summary>
    [Fact]
    public void RoundTrip_UntypedConcert_ReserializeAddsNoTypeKey()
    {
        const string untyped = """
            {
              "date": "1971-05-30",
              "venue": "Winterland",
              "city": "San Francisco",
              "state": "CA",
              "country": "US",
              "hasSetlist": true,
              "sets": [
                { "name": "Set 1", "songs": [
                  { "name": "Bertha", "date": "1971-05-30", "segue": true },
                  { "name": "Me and Bobby McGee", "date": "1971-05-30", "segue": false }
                ] }
              ],
              "tracks": [
                { "position": 1, "songName": "Bertha", "date": "1971-05-30", "segue": true, "set": "Set 1" },
                { "position": 2, "songName": "Me and Bobby McGee", "date": "1971-05-30", "segue": false, "set": "Set 1" }
              ]
            }
            """;

        var restored = JsonConvert.DeserializeObject<ConcertReference>(untyped, CanonicalJson.Settings)!;
        var reserialized = CanonicalJson.Serialize(restored);

        Assert.DoesNotContain("\"type\"", reserialized);
        Assert.DoesNotContain("\"Type\"", reserialized);
    }

    // ===== Adapter: concerts → SetInfo/SetlistSong carry-through =====

    [Fact]
    public void Adapter_CarriesType_Through()
    {
        var concert = new ConcertReference
        {
            Date = "1971-02-21",
            HasSetlist = true,
            Sets = new List<ConcertSet>
            {
                new()
                {
                    Name = "Set 1",
                    Songs = new List<ConcertSong>
                    {
                        new() { Name = "Tuning", Type = "tuning" },
                        new() { Name = "Ripple" }, // default song
                    }
                }
            }
        };

        var sets = ConcertSetlistAdapter.ToSetInfoList(concert)!;

        Assert.Equal("tuning", sets[0].Songs[0].Type);
        Assert.Equal("song", sets[0].Songs[1].Type);
    }

    // ===== Projection: SetInfo → SetlistEntryVm carry-through =====

    [Fact]
    public void Projection_CarriesType_ToEntryVm()
    {
        var setlist = new List<SetInfo>
        {
            new()
            {
                Label = "Set 1",
                Songs = new List<SetlistSong>
                {
                    new() { Name = "Tuning", Type = "tuning" },
                    new() { Name = "Ripple" }, // default song
                }
            }
        };

        var projection = SetlistProjection.Build(setlist, name => name);

        Assert.Equal("tuning", projection[0].Type);
        Assert.Equal("song", projection[1].Type);
    }

    // ===== ConcertSnapshot.Project: lockstep + diff-at-save sensitivity (D3) =====

    private static IReadOnlyList<ConcertTrackInput> TracksWith(string type) => new List<ConcertTrackInput>
    {
        new("Ripple", "1971-02-21", false, "Set 1", "", type),
    };

    [Fact]
    public void Snapshot_Project_CarriesType_ToBothShapes()
    {
        var concert = ConcertSnapshot.Project("1971-02-21", "Capitol Theatre", "Port Chester, NY",
            TracksWith("false-start"));

        // D8a lockstep: the projected ConcertSong and ConcertTrack both carry the input type.
        Assert.Equal("false-start", concert.Sets[0].Songs[0].Type);
        Assert.Equal("false-start", concert.Tracks[0].Type);
    }

    [Fact]
    public void Snapshot_Project_DefaultType_IsSong()
    {
        var concert = ConcertSnapshot.Project("1971-02-21", "Capitol Theatre", "Port Chester, NY",
            new List<ConcertTrackInput> { new("Ripple", "1971-02-21", false, "Set 1", "") });

        Assert.Equal("song", concert.Sets[0].Songs[0].Type);
        Assert.Equal("song", concert.Tracks[0].Type);
    }

    [Fact]
    public void Snapshot_Serialize_TypeChange_ChangesSnapshot()
    {
        // Diff-at-save (D3): retyping one entry is a content change, so the snapshot differs and the
        // editor's IsDirty compare unverifies.
        var asSong = ConcertSnapshot.Serialize("1971-02-21", "Capitol Theatre", "Port Chester, NY",
            TracksWith("song"));
        var asFalseStart = ConcertSnapshot.Serialize("1971-02-21", "Capitol Theatre", "Port Chester, NY",
            TracksWith("false-start"));

        Assert.NotEqual(asSong, asFalseStart);
    }

    [Fact]
    public void Snapshot_Serialize_SameType_IdenticalSnapshot()
    {
        // Insensitivity when type is unchanged: a no-op reopen/save produces a byte-identical snapshot.
        var a = ConcertSnapshot.Serialize("1971-02-21", "Capitol Theatre", "Port Chester, NY",
            TracksWith("false-start"));
        var b = ConcertSnapshot.Serialize("1971-02-21", "Capitol Theatre", "Port Chester, NY",
            TracksWith("false-start"));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Snapshot_Serialize_DefaultSongType_OmitsTypeKey()
    {
        // A default-type projection carries no type key, so an untyped concert's diff baseline is
        // byte-identical to the pre-slice-1 baseline (no spurious unverify on existing verified files).
        var json = ConcertSnapshot.Serialize("1971-02-21", "Capitol Theatre", "Port Chester, NY",
            TracksWith("song"));

        Assert.DoesNotContain("\"type\"", json);
        Assert.DoesNotContain("\"Type\"", json);
    }
}
