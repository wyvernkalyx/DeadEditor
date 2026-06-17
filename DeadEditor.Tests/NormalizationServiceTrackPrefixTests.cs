using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Phase B-2 wiring tests: <see cref="NormalizationService.Normalize"/> strips a leading
/// track-number prefix from its match key (Option B) so prefixed titles auto-resolve to the
/// canonical name. Uses the real songs.json fixture copied to the test output, so the songs
/// asserted here (Bertha, Sugaree) must exist in the database.
///
/// Number-titled-after-prefix resolution (e.g. "03 - 46 Days" -> "46 Days") is locked by the
/// helper contract in <see cref="TrackNumberPrefixTests"/>; no number-titled song exists in the
/// shipped songs.json, so it cannot be asserted DB-backed here without polluting the fixture.
/// </summary>
public class NormalizationServiceTrackPrefixTests
{
    private readonly NormalizationService _svc = new();

    [Fact]
    public void Normalize_SeparatorPrefix_WithTrackNumber_ResolvesToCanonical() =>
        Assert.Equal("Bertha", _svc.Normalize("01 - Bertha", null, 1));

    [Fact]
    public void Normalize_SeparatorPrefix_WithoutTrackNumber_StillResolves() =>
        // Separator form strips unconditionally — no track number needed.
        Assert.Equal("Bertha", _svc.Normalize("01 - Bertha"));

    [Fact]
    public void Normalize_DiscToken_ResolvesToCanonical() =>
        Assert.Equal("Sugaree", _svc.Normalize("d1t01 Sugaree"));

    [Fact]
    public void Normalize_BareSpacePrefix_GatedByTrackNumber_Resolves() =>
        Assert.Equal("Sugaree", _svc.Normalize("10 Sugaree", null, 10));

    [Fact]
    public void Normalize_BareSpacePrefix_NoTrackNumber_StaysUnmatched() =>
        // Gate off -> "01 Bertha" is not a separator form, so the prefix survives and misses.
        Assert.Null(_svc.Normalize("01 Bertha"));

    [Fact]
    public void Normalize_NumberTitle_GateFails_NoSpuriousMatch() =>
        // "16 Tons" is not in the database and 16 != 3, so the bare-space strip never fires and
        // there is no canonical match — confirms auto-resolve does not over-reach.
        Assert.Null(_svc.Normalize("16 Tons", null, 3));

    [Fact]
    public void Normalize_NoPrefix_StillMatches_RegressionGuard() =>
        Assert.Equal("Bertha", _svc.Normalize("Bertha"));
}
