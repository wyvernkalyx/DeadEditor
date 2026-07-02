using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Locks the ALBUM / RELEASE field tooltip mapping shared by ImportView and EditMetadataView:
/// only audience recordings get the source/taper hint; official (and null/unknown) get the
/// known-release search hint.
/// </summary>
public class AlbumFieldHintTests
{
    [Fact]
    public void Audience_ReturnsSourceTaperHint()
    {
        Assert.Equal(AlbumFieldHint.Audience, AlbumFieldHint.ForType(AlbumType.AudienceRecording));
    }

    [Fact]
    public void Official_ReturnsKnownReleaseHint()
    {
        Assert.Equal(AlbumFieldHint.Official, AlbumFieldHint.ForType(AlbumType.OfficialRelease));
    }

    [Fact]
    public void NullType_FallsBackToOfficialHint()
    {
        Assert.Equal(AlbumFieldHint.Official, AlbumFieldHint.ForType(null));
    }
}
