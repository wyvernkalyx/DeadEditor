using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression tests for the Y2K bug at the four title-parsing sites that previously used
/// the fixed pivot <c>year &gt;= 70 ? 1900 : 2000</c>. Year 69 (e.g. "12/31/69") used to
/// resolve to 2069; it now resolves to 1969 — both via album-date context and via the
/// current-year-anchored pivot fallback.
///
/// Sites covered:
///   1. <see cref="NormalizationService.NormalizeDateInTitle"/>
///   2. <see cref="NormalizationService"/> NormalizeAll → ExtractDateFromRawTitle → ParseSlashDate
///   3. <see cref="MetadataService.ParseTitleAndDate"/> PATTERN 1C (square bracket venue + slash date)
///   4. <see cref="MetadataService.ParseTitleAndDate"/> PATTERN 1D (parenthetical with slash date and venue)
///
/// See documentation/12-normalization-service.md § Two-Digit Year Resolution.
/// </summary>
public class TitleDateParsingY2KTests
{
    // ---------- Site 3: ParseTitleAndDate PATTERN 1C ----------

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

    // ---------- Site 4: ParseTitleAndDate PATTERN 1D ----------

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

    // ---------- Site 1: NormalizeDateInTitle ----------

    [Fact]
    public void NormalizeDateInTitle_TwoDigitYear71_ResolvesTo1971()
    {
        var svc = new NormalizationService();
        var result = svc.NormalizeDateInTitle("Some Song (12/9/71 Fox Theatre)");
        Assert.Equal("Some Song (1971-12-09)", result);
    }

    [Fact]
    public void NormalizeDateInTitle_TwoDigitYear69_ResolvesTo1969()
    {
        var svc = new NormalizationService();
        // Y2K regression case: 69 used to become 2069.
        var result = svc.NormalizeDateInTitle("Dark Star (12/31/69 Winterland)");
        Assert.Equal("Dark Star (1969-12-31)", result);
    }

    [Fact]
    public void NormalizeDateInTitle_FourDigitYear_LeftAsIs()
    {
        var svc = new NormalizationService();
        var result = svc.NormalizeDateInTitle("Some Song (12/9/1971 Fox Theatre)");
        Assert.Equal("Some Song (1971-12-09)", result);
    }

    // ---------- Site 2: NormalizeAll → ExtractDateFromRawTitle → ParseSlashDate ----------

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
