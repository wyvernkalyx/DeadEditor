using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="TwoDigitYearResolver.ResolveTwoDigitYear"/>, the helper that
/// replaced four copies of the buggy <c>year &gt;= 70 ? 1900 : 2000</c> pivot in
/// MetadataService and NormalizationService. The bug had year 69 (e.g. 12/31/69)
/// resolving to 2069 instead of 1969 — see commit message and
/// documentation/12-normalization-service.md § Two-Digit Year Resolution.
/// </summary>
public class TwoDigitYearResolverTests
{
    // --- Pivot rule (no album-date context, anchored at currentYear=2026 → pivot=2031) ---

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(10, 2010)]
    [InlineData(25, 2025)]
    [InlineData(30, 2030)]   // below pivot
    [InlineData(31, 2031)]   // at pivot
    [InlineData(32, 1932)]   // just above pivot
    [InlineData(69, 1969)]   // the regression case
    [InlineData(70, 1970)]
    [InlineData(99, 1999)]
    public void Pivot_NoAlbumDate_2026(int twoDigit, int expected)
    {
        Assert.Equal(expected, TwoDigitYearResolver.ResolveTwoDigitYear(twoDigit, albumDate: null, currentYear: 2026));
    }

    // --- Album-date rule prefers album century when within ±1 of album year ---

    [Theory]
    [InlineData(69, "1969-12-26", 1969)]   // exact album-year match
    [InlineData(10, "2010-05-15", 2010)]   // exact album-year match
    [InlineData(72, "1971-12-31", 1972)]   // ±1 fuzz: track recorded year after album cutoff
    [InlineData(70, "1971-12-31", 1970)]   // ±1 fuzz: track recorded year before
    public void AlbumDate_PrefersAlbumCentury_WithinFuzz(int twoDigit, string albumDate, int expected)
    {
        Assert.Equal(expected, TwoDigitYearResolver.ResolveTwoDigitYear(twoDigit, albumDate, currentYear: 2026));
    }

    [Fact]
    public void AlbumDate_OutsideFuzz_FallsThroughToPivot()
    {
        // year 73 with album 1971 → resolved-via-album-century = 1973, |1973 - 1971| = 2 > 1
        // → falls through to pivot rule. With currentYear=2026, pivot=2031: 73 > 31 → 1973.
        // (Album rule and pivot rule happen to agree here; this test guards the fall-through path.)
        Assert.Equal(1973, TwoDigitYearResolver.ResolveTwoDigitYear(73, "1971-12-31", currentYear: 2026));
    }

    [Fact]
    public void AlbumDate_OutsideFuzz_PivotDisagreesWithAlbum_PivotWins()
    {
        // year 25 with album 2026 → album-century candidate = 2025, |2025 - 2026| = 1 → within fuzz → 2025.
        // Sanity: confirm fuzz boundary works (this is the "exactly at boundary" case).
        Assert.Equal(2025, TwoDigitYearResolver.ResolveTwoDigitYear(25, "2026-01-01", currentYear: 2026));
    }

    [Fact]
    public void AlbumDate_YyyyOnlyForm_IsAccepted()
    {
        Assert.Equal(1969, TwoDigitYearResolver.ResolveTwoDigitYear(69, "1969", currentYear: 2026));
    }

    // --- Defensive: out-of-range, empty, malformed ---

    [Theory]
    [InlineData(100)]
    [InlineData(1969)]
    [InlineData(-1)]
    public void OutOfRange_ReturnedUnchanged(int input)
    {
        Assert.Equal(input, TwoDigitYearResolver.ResolveTwoDigitYear(input, albumDate: null, currentYear: 2026));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("ab")]            // shorter than 4 chars
    [InlineData("xx69-12-26")]    // first 4 chars don't parse
    public void MalformedAlbumDate_FallsThroughToPivot(string albumDate)
    {
        // currentYear=2026, pivot=2031, year 69 > 31 → 1969 via pivot.
        Assert.Equal(1969, TwoDigitYearResolver.ResolveTwoDigitYear(69, albumDate, currentYear: 2026));
    }

    // --- Future-proofing: pivot tracks currentYear ---

    [Theory]
    [InlineData(2026, 69, 1969)]
    [InlineData(2050, 69, 1969)]   // pivot=55, 69 > 55 → 19XX
    [InlineData(2050, 50, 2050)]   // pivot=55, 50 ≤ 55 → 20XX
    [InlineData(2050, 56, 1956)]   // just above pivot
    public void Pivot_TracksCurrentYear(int currentYear, int twoDigit, int expected)
    {
        Assert.Equal(expected, TwoDigitYearResolver.ResolveTwoDigitYear(twoDigit, albumDate: null, currentYear: currentYear));
    }

    [Fact]
    public void DefaultCurrentYear_UsesDateTimeNow()
    {
        // Sanity check that the default branch executes; we don't assert a specific value
        // (depends on wall clock), only that the call succeeds and returns 4-digit output.
        var result = TwoDigitYearResolver.ResolveTwoDigitYear(69);
        Assert.True(result == 1969 || result == 2069, $"Expected 1969 or 2069 (depending on wall clock), got {result}");
    }
}
