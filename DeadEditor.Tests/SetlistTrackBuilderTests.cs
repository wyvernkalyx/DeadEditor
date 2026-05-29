using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="SetlistTrackBuilder.BuildTracksFromSetlist"/> — the pure
/// flatten/map behind the box-set wizard's "pull setlist for date" action (commit G3).
/// Plain data types only; no WPF, no file I/O.
/// </summary>
public class SetlistTrackBuilderTests
{
    private static List<SetInfo> TwoSets() => new()
    {
        new SetInfo
        {
            Label = "Set 1",
            Songs = new List<SetlistSong>
            {
                new() { Name = "Bertha", Segue = true },
                new() { Name = "Good Lovin'", Segue = false },
            }
        },
        new SetInfo
        {
            Label = "Encore",
            Encore = true,
            Songs = new List<SetlistSong>
            {
                new() { Name = "Casey Jones", Segue = false },
            }
        },
    };

    [Fact]
    public void FlattensSetThenSongOrder_WithCorrectCount_AndNoSetLabels()
    {
        var tracks = SetlistTrackBuilder.BuildTracksFromSetlist(TwoSets(), "1972-05-04", 1);

        Assert.Equal(3, tracks.Count);
        Assert.Equal(new[] { "Bertha", "Good Lovin'", "Casey Jones" },
            tracks.Select(t => t.SongName).ToArray());
        // Set labels ("Set 1", "Encore") map to nothing on the flat track type.
    }

    [Fact]
    public void NumbersSequentiallyFromStartNumber_One()
    {
        var tracks = SetlistTrackBuilder.BuildTracksFromSetlist(TwoSets(), "1972-05-04", 1);
        Assert.Equal(new[] { 1, 2, 3 }, tracks.Select(t => t.TrackNumber).ToArray());
    }

    [Fact]
    public void NumbersSequentiallyFromStartNumber_AppendToNonEmpty()
    {
        var tracks = SetlistTrackBuilder.BuildTracksFromSetlist(TwoSets(), "1972-05-04", 103);
        Assert.Equal(new[] { 103, 104, 105 }, tracks.Select(t => t.TrackNumber).ToArray());
    }

    [Fact]
    public void MapsSegueVerbatim()
    {
        var tracks = SetlistTrackBuilder.BuildTracksFromSetlist(TwoSets(), "1972-05-04", 1);
        Assert.True(tracks[0].SegueOut);    // Bertha, Segue = true
        Assert.False(tracks[1].SegueOut);   // Good Lovin', Segue = false
        Assert.False(tracks[2].SegueOut);   // Casey Jones, Segue = false
    }

    [Fact]
    public void StampsEveryTrackWithTheGivenDate()
    {
        var tracks = SetlistTrackBuilder.BuildTracksFromSetlist(TwoSets(), "1972-05-04", 1);
        Assert.All(tracks, t => Assert.Equal("1972-05-04", t.Date));
    }

    [Fact]
    public void EmptyInput_YieldsEmptyList()
    {
        Assert.Empty(SetlistTrackBuilder.BuildTracksFromSetlist(new List<SetInfo>(), "1972-05-04", 1));

        var setsWithNoSongs = new List<SetInfo> { new() { Label = "Set 1" } };
        Assert.Empty(SetlistTrackBuilder.BuildTracksFromSetlist(setsWithNoSongs, "1972-05-04", 1));
    }
}
