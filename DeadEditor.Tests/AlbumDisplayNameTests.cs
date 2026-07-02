using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Unit tests for <see cref="AlbumDisplayName.Compose"/>, the pure composer behind the
/// library grid's "Album Name" column. Official rows show their real name; audience rows
/// reconstruct the composed "Date - Venue - City, ST[ - Source]" that lives in their ALBUM
/// tag, so the column is no longer blank. Display-only — never written back to persistence.
/// </summary>
public class AlbumDisplayNameTests
{
    [Fact]
    public void Official_ReturnsRealAlbumNameVerbatim()
    {
        var result = AlbumDisplayName.Compose(
            AlbumType.OfficialRelease, "1972-09-15", "Boston Music Hall", "Boston, MA",
            "Dave's Picks Vol. 1");
        Assert.Equal("Dave's Picks Vol. 1", result);
    }

    [Fact]
    public void Official_WithNoName_ReturnsEmpty()
    {
        var result = AlbumDisplayName.Compose(
            AlbumType.OfficialRelease, "1972-09-15", "Boston Music Hall", "Boston, MA", "");
        Assert.Equal("", result);
    }

    [Fact]
    public void Audience_NoDifferentiator_ComposesDateVenueLocation()
    {
        var result = AlbumDisplayName.Compose(
            AlbumType.AudienceRecording, "1971-02-20", "Capitol Theatre", "Port Chester, NY", "");
        Assert.Equal("1971-02-20 - Capitol Theatre - Port Chester, NY", result);
    }

    [Fact]
    public void Audience_WithDifferentiator_AppendsSourceOnce()
    {
        var result = AlbumDisplayName.Compose(
            AlbumType.AudienceRecording, "1971-02-20", "Capitol Theatre", "Port Chester, NY", "Matrix");
        Assert.Equal("1971-02-20 - Capitol Theatre - Port Chester, NY - Matrix", result);
    }

    [Fact]
    public void Audience_MatchesAlbumTitleTag()
    {
        // The grid display must match the ALBUM tag written at import (AlbumInfo.AlbumTitle).
        var album = new AlbumInfo
        {
            Type = AlbumType.AudienceRecording,
            AlbumDate = "1971-02-20",
            Venue = "Capitol Theatre",
            CityState = "Port Chester, NY",
            AlbumName = "SBD - Cantor",
        };
        var display = AlbumDisplayName.Compose(
            AlbumType.AudienceRecording, album.AlbumDate, album.Venue, album.CityState, album.AlbumName);
        Assert.Equal(album.AlbumTitle, display);
    }

    [Fact]
    public void Audience_NoCoordinates_FallsBackToDifferentiator()
    {
        var result = AlbumDisplayName.Compose(
            AlbumType.AudienceRecording, "", "", "", "Some Tape");
        Assert.Equal("Some Tape", result);
    }

    [Fact]
    public void Audience_NullFields_DoNotThrow()
    {
        var result = AlbumDisplayName.Compose(
            AlbumType.AudienceRecording, null, null, null, null);
        Assert.Equal("", result);
    }
}
