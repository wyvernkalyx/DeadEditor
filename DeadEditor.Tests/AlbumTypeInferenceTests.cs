using DeadEditor.Models;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Unit tests for <see cref="AlbumTypeInference.Infer"/>. The heuristic is the
/// load-time fallback for albums imported from sources without an ALBUMTYPE
/// tag. After load, the user's combobox selection is the source of truth — see
/// the album-type-combobox bug fix that introduced this helper.
/// </summary>
public class AlbumTypeInferenceTests
{
    [Fact]
    public void Infer_NullAlbum_ReturnsAudienceRecording()
    {
        Assert.Equal(AlbumType.AudienceRecording, AlbumTypeInference.Infer(null));
    }

    [Fact]
    public void Infer_EmptyAlbum_ReturnsAudienceRecording()
    {
        var info = new AlbumInfo();
        Assert.Equal(AlbumType.AudienceRecording, AlbumTypeInference.Infer(info));
    }

    [Fact]
    public void Infer_AlbumNameOnly_ReturnsOfficialRelease()
    {
        var info = new AlbumInfo { AlbumName = "American Beauty" };
        Assert.Equal(AlbumType.OfficialRelease, AlbumTypeInference.Infer(info));
    }

    [Fact]
    public void Infer_DateAndVenueOnly_ReturnsAudienceRecording()
    {
        var info = new AlbumInfo
        {
            AlbumDate = "1977-05-08",
            Venue = "Barton Hall",
        };
        Assert.Equal(AlbumType.AudienceRecording, AlbumTypeInference.Infer(info));
    }

    [Fact]
    public void Infer_DateVenueAndAlbumName_ReturnsOfficialRelease()
    {
        var info = new AlbumInfo
        {
            AlbumDate = "1972-09-15",
            Venue = "Boston Music Hall",
            AlbumName = "Dave's Picks Vol. 1",
        };
        Assert.Equal(AlbumType.OfficialRelease, AlbumTypeInference.Infer(info));
    }

    [Fact]
    public void Infer_DateOnly_ReturnsAudienceRecording()
    {
        var info = new AlbumInfo { AlbumDate = "1977-05-08" };
        Assert.Equal(AlbumType.AudienceRecording, AlbumTypeInference.Infer(info));
    }

    [Fact]
    public void Infer_VenueOnly_ReturnsAudienceRecording()
    {
        var info = new AlbumInfo { Venue = "Barton Hall" };
        Assert.Equal(AlbumType.AudienceRecording, AlbumTypeInference.Infer(info));
    }
}
