using System.Collections.Generic;
using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for the slice-5b D10a enforcement (<see cref="RowLabelRule"/>) and the shared type
/// display-label helper (<see cref="SetlistEntryType.DisplayLabel"/>). D10a (widened 2026-07-07):
/// every entry of ANY type must carry a non-empty name — the extras-only D10 left the default-"song"
/// front door open, so an unnamed song row could save.
/// </summary>
public class RowLabelRuleTests
{
    private static ConcertTrackInput Row(string name, string type)
        => new ConcertTrackInput(name, "1971-02-21", false, "Set 1", "", type);

    [Fact]
    public void EmptySongRow_IsNowCaught_D10a()
    {
        // The widening: a song-typed row with an empty name is now blocked (was allowed under D10).
        var tracks = new List<ConcertTrackInput> { Row("", SetlistEntryType.Song) };
        Assert.Equal(1, RowLabelRule.FirstUnnamedRow(tracks));
    }

    [Fact]
    public void EmptyExtraRow_StillCaught()
    {
        var tracks = new List<ConcertTrackInput>
        {
            Row("Bertha", SetlistEntryType.Song),
            Row("", SetlistEntryType.Tuning),   // row 2
        };
        Assert.Equal(2, RowLabelRule.FirstUnnamedRow(tracks));
    }

    [Fact]
    public void WhitespaceRow_IsCaught()
    {
        var tracks = new List<ConcertTrackInput> { Row("   ", SetlistEntryType.FalseStart) };
        Assert.Equal(1, RowLabelRule.FirstUnnamedRow(tracks));
    }

    [Fact]
    public void NamedSongAndExtra_ReturnNull()
    {
        var tracks = new List<ConcertTrackInput>
        {
            Row("Bertha", SetlistEntryType.Song),
            Row("Ripple", SetlistEntryType.FalseStart),
            Row("Tuning", SetlistEntryType.Tuning),
        };
        Assert.Null(RowLabelRule.FirstUnnamedRow(tracks));
    }

    [Fact]
    public void ReturnsFirstOffender_RegardlessOfType()
    {
        // An empty SONG row (2) precedes an empty EXTRA row (3): the song is the first offender now.
        var tracks = new List<ConcertTrackInput>
        {
            Row("Bertha", SetlistEntryType.Song),
            Row("", SetlistEntryType.Song),     // row 2 — first offender (a song)
            Row("", SetlistEntryType.Tuning),   // row 3 — also empty, not returned
        };
        Assert.Equal(2, RowLabelRule.FirstUnnamedRow(tracks));
    }

    [Fact]
    public void AllNamed_ReturnsNull()
    {
        var tracks = new List<ConcertTrackInput>
        {
            Row("Bertha", SetlistEntryType.Song),
            Row("Ripple", SetlistEntryType.Song),
        };
        Assert.Null(RowLabelRule.FirstUnnamedRow(tracks));
    }

    [Fact]
    public void EmptyList_ReturnsNull()
    {
        Assert.Null(RowLabelRule.FirstUnnamedRow(new List<ConcertTrackInput>()));
    }

    // ===== SetlistEntryType.DisplayLabel =====

    [Theory]
    [InlineData("false-start", "FALSE START")]
    [InlineData("tuning", "TUNING")]
    [InlineData("other-extra", "OTHER EXTRA")]
    [InlineData("song", "SONG")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void DisplayLabel_UppercasesAndDehyphenates(string? type, string expected)
    {
        Assert.Equal(expected, SetlistEntryType.DisplayLabel(type));
    }
}
