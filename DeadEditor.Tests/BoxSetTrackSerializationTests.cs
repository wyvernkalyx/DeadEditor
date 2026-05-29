using DeadEditor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Guards the G2 approach-(a) claim: adding <see cref="System.ComponentModel.INotifyPropertyChanged"/>
/// to <see cref="BoxSetTrack"/> leaves serialization unchanged. Uses the same Newtonsoft
/// settings <c>BoxSetService</c> uses (camelCase, indented, ignore-null) but exercises the
/// model directly — no <c>BoxSetService</c> file I/O (its hardcoded AppData path is the
/// banked testability blocker).
/// </summary>
public class BoxSetTrackSerializationTests
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    private static BoxSetDefinition SampleDefinition() => new()
    {
        Name = "Round Trip Test",
        ReleaseDate = "2021-10-08",
        Tracks =
        {
            new BoxSetTrack { TrackNumber = 101, SongName = "Truckin'", Date = "1971-12-09", SegueOut = true },
            new BoxSetTrack { TrackNumber = 201, SongName = "Brown-Eyed Women", Date = "", SegueOut = false },
        }
    };

    [Fact]
    public void Tracks_SurviveRoundTrip_WithValues()
    {
        var json = JsonConvert.SerializeObject(SampleDefinition(), Settings);
        var restored = JsonConvert.DeserializeObject<BoxSetDefinition>(json, Settings);

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Tracks.Count);

        var first = restored.Tracks[0];
        Assert.Equal(101, first.TrackNumber);
        Assert.Equal("Truckin'", first.SongName);
        Assert.Equal("1971-12-09", first.Date);
        Assert.True(first.SegueOut);

        var second = restored.Tracks[1];
        Assert.Equal(201, second.TrackNumber);              // disc-prefixed numbering preserved
        Assert.Equal("Brown-Eyed Women", second.SongName);
        Assert.Equal("", second.Date);                      // empty date stays empty (not back-filled)
        Assert.False(second.SegueOut);
    }

    [Fact]
    public void Serialization_UsesCamelCaseKeys_AndOmitsStaleShape()
    {
        var json = JsonConvert.SerializeObject(SampleDefinition(), Settings);
        var root = JObject.Parse(json);

        var tracks = Assert.IsType<JArray>(root["tracks"]);
        var track = Assert.IsType<JObject>(tracks[0]);

        // camelCase property keys present.
        Assert.NotNull(track["trackNumber"]);
        Assert.NotNull(track["songName"]);
        Assert.NotNull(track["date"]);
        Assert.NotNull(track["segueOut"]);

        // INPC adds no serialized surface, and no stale concerts/discs shape leaks in.
        Assert.Null(track["propertyChanged"]);
        Assert.Null(root["concerts"]);
        Assert.Null(root["discs"]);
        Assert.Null(root["discCount"]);
    }
}
