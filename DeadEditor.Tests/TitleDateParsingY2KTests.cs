using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression tests for the Y2K bug at the title-parsing sites that previously used
/// the fixed pivot <c>year &gt;= 70 ? 1900 : 2000</c>. Year 69 (e.g. "12/31/69") used to
/// resolve to 2069; it now resolves to 1969 — both via album-date context and via the
/// current-year-anchored pivot fallback.
///
/// Sites covered:
///   1. <see cref="NormalizationService"/> NormalizeAll → ExtractDateFromRawTitle → ParseSlashDate
///   2. <see cref="MetadataService.ParseTitleAndDate"/> via <see cref="TitleStructureParser"/>
///
/// See documentation/12-normalization-service.md § Two-Digit Year Resolution and
/// DeadEditor.Tests/TitleStructureParserTests.cs § Y2K resolution via parser.
/// </summary>
public class TitleDateParsingY2KTests
{
    // ---------- ParseTitleAndDate: bracketed slash date with venue ----------

    [Fact]
    public void Pattern1C_TwoDigitYear69_NoAlbumDate_ResolvesTo1969()
    {
        var svc = new MetadataService();
        var (_, _, date) = svc.ParseTitleAndDate("Bertha [12/31/69, Winterland Arena, San Francisco, CA]");
        Assert.Equal("1969-12-31", date);
    }

    [Fact]
    public void Pattern1C_TwoDigitYear69_WithAlbumDate_ResolvesTo1969()
    {
        var svc = new MetadataService();
        var (_, _, date) = svc.ParseTitleAndDate("Bertha [12/31/69, Venue]", albumDate: "1969-12-31");
        Assert.Equal("1969-12-31", date);
    }

    [Fact]
    public void Pattern1C_FourDigitYear_AlbumDateIgnored()
    {
        var svc = new MetadataService();
        var (_, _, date) = svc.ParseTitleAndDate("Bertha [12/31/1969, Venue]", albumDate: "2020-01-01");
        Assert.Equal("1969-12-31", date);
    }

    // ---------- ParseTitleAndDate: parenthetical slash date with venue ----------

    [Fact]
    public void Pattern1D_TwoDigitYear71_NoAlbumDate_ResolvesTo1971()
    {
        var svc = new MetadataService();
        var (_, _, date) = svc.ParseTitleAndDate("Sugar Magnolia (12/9/71 Fox Theatre)");
        Assert.Equal("1971-12-09", date);
    }

    [Fact]
    public void Pattern1D_TwoDigitYear72_WithAlbumDate1971_FuzzPrefersAlbumCentury()
    {
        var svc = new MetadataService();
        var (_, _, date) = svc.ParseTitleAndDate("Bird Song (1/2/72 Venue)", albumDate: "1971-12-31");
        Assert.Equal("1972-01-02", date);
    }

    // ---------- NormalizeAll → ExtractDateFromRawTitle → ParseSlashDate ----------

    [Fact]
    public void NormalizeAll_ExtractsBracketDate_TwoDigitYear69_ResolvesTo1969()
    {
        // ExtractDateFromRawTitle is private; exercise it via NormalizeAll. When
        // TrackDate is empty, NormalizeAll falls back to scanning RawTitle/SongName for
        // a slash date — Pattern A (bracket) routes through ParseSlashDate.
        var svc = new NormalizationService();
        var track = new TrackInfo
        {
            FilePath = "test.flac",
            RawTitle = "Bertha [12/31/69 Winterland]",
            SongName = "Bertha [12/31/69 Winterland]",
            TrackDate = "",
            AlbumDate = "1969-12-31",
        };
        svc.NormalizeAll(new List<TrackInfo> { track });
        Assert.Equal("1969-12-31", track.TrackDate);
    }

    [Fact]
    public void NormalizeAll_ExtractsParenthesizedDate_TwoDigitYear71_ResolvesTo1971()
    {
        var svc = new NormalizationService();
        var track = new TrackInfo
        {
            FilePath = "test.flac",
            RawTitle = "Sugar Magnolia (10/18/72 Fox Theatre)",
            SongName = "Sugar Magnolia",
            TrackDate = "",
            AlbumDate = "",   // exercise pivot fallback
        };
        svc.NormalizeAll(new List<TrackInfo> { track });
        Assert.Equal("1972-10-18", track.TrackDate);
    }
}
