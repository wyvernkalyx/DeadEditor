using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Contract for <see cref="TrackNumberPrefix.Strip"/>. The strip is match-key-only
/// (Option B): it feeds the canonical lookup, never the stored tag, so a false strip
/// is graceful (failed lookup), not corruption. See
/// documentation/track-number-prefix-normalization.md.
/// </summary>
public class TrackNumberPrefixTests
{
    // === Positive: structurally unambiguous separator / token forms (strip) ===

    [Fact]
    public void Strip_DigitsSpaceDashSpace() =>
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("01 - Bertha"));

    [Fact]
    public void Strip_PreservesApostropheRemainder() =>
        Assert.Equal("Good Lovin'", TrackNumberPrefix.Strip("02 - Good Lovin'"));

    [Fact]
    public void Strip_DotSeparator() =>
        Assert.Equal("Good Lovin'", TrackNumberPrefix.Strip("1. Good Lovin'"));

    [Fact]
    public void Strip_ParenSeparator() =>
        Assert.Equal("Brown Eyed Women", TrackNumberPrefix.Strip("01) Brown Eyed Women"));

    [Fact]
    public void Strip_DashSeparator_Cassidy() =>
        Assert.Equal("Cassidy", TrackNumberPrefix.Strip("04 - Cassidy"));

    [Fact]
    public void Strip_PreservesInternalHyphen() =>
        // Critical: only the leading prefix goes; the internal hyphen of "Peggy-O" stays.
        Assert.Equal("Peggy-O", TrackNumberPrefix.Strip("05 - Peggy-O"));

    [Fact]
    public void Strip_ColonSeparator() =>
        Assert.Equal("Sugaree", TrackNumberPrefix.Strip("01: Sugaree"));

    [Fact]
    public void Strip_UnderscoreSeparator_NoTrailingSpace() =>
        Assert.Equal("Sugaree", TrackNumberPrefix.Strip("01_Sugaree"));

    [Fact]
    public void Strip_EnDashSeparator() =>
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("01 – Bertha"));

    [Fact]
    public void Strip_EmDashSeparator() =>
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("01 — Bertha"));

    [Fact]
    public void Strip_DiscTrackToken() =>
        Assert.Equal("Cassidy", TrackNumberPrefix.Strip("d1t01 Cassidy"));

    [Fact]
    public void Strip_DiscHyphenTrackToken() =>
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("1-01 Bertha"));

    [Fact]
    public void Strip_LeadingWhitespace() =>
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("  01 - Bertha"));

    // === Positive: number-titled song after a track prefix (resolves) ===

    [Fact]
    public void Strip_NumberTitleAfterPrefix_46Days() =>
        Assert.Equal("46 Days", TrackNumberPrefix.Strip("03 - 46 Days"));

    [Fact]
    public void Strip_NumberTitleAfterPrefix_2120() =>
        Assert.Equal("2120 South Michigan Avenue",
            TrackNumberPrefix.Strip("05 - 2120 South Michigan Avenue"));

    [Fact]
    public void Strip_NumberTitleAfterPrefix_2001() =>
        Assert.Equal("2001", TrackNumberPrefix.Strip("03 - 2001"));

    [Fact]
    public void Strip_NumberTitleAfterPrefix_555() =>
        Assert.Equal("555", TrackNumberPrefix.Strip("07 - 555"));

    // === Negative: time codes are not track prefixes ===

    [Fact]
    public void NoStrip_ColonTimeCode_NoFollowingSpace() =>
        Assert.Equal("8:05", TrackNumberPrefix.Strip("8:05"));

    [Fact]
    public void NoStrip_LeadingTimeCode() =>
        Assert.Equal("12:34 Jam", TrackNumberPrefix.Strip("12:34 Jam"));

    // === Positive: bare-space form, gated by track number ===

    [Fact]
    public void Strip_BareSpace_GateMatches() =>
        Assert.Equal("Sugaree", TrackNumberPrefix.Strip("10 Sugaree", 10));

    [Fact]
    public void Strip_BareSpace_GateMatches_ZeroPadded() =>
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("01 Bertha", 1));

    [Fact]
    public void Strip_BareSpace_DiscEncodedTrackNumber() =>
        // 101 % 100 == 1 matches the leading "01".
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("01 Bertha", 101));

    // === Negative: pass through unchanged ===

    [Fact]
    public void NoStrip_BareTitle() =>
        Assert.Equal("Bertha", TrackNumberPrefix.Strip("Bertha"));

    [Fact]
    public void NoStrip_LeadingLetterWithDot() =>
        // "U.S. Blues" starts with a letter — the dot separator must not fire.
        Assert.Equal("U.S. Blues", TrackNumberPrefix.Strip("U.S. Blues"));

    [Fact]
    public void NoStrip_NumberTitle_GateFails_16Tons() =>
        Assert.Equal("16 Tons", TrackNumberPrefix.Strip("16 Tons", 3));

    [Fact]
    public void NoStrip_NumberTitle_GateFails_50Ways() =>
        Assert.Equal("50 Ways to Leave Your Lover",
            TrackNumberPrefix.Strip("50 Ways to Leave Your Lover", 4));

    [Fact]
    public void NoStrip_NumberTitle_GateFails_2120() =>
        Assert.Equal("2120 South Michigan Avenue",
            TrackNumberPrefix.Strip("2120 South Michigan Avenue", 2));

    [Fact]
    public void NoStrip_NumberTitle_GateFails_99Luftballons() =>
        Assert.Equal("99 Luftballons", TrackNumberPrefix.Strip("99 Luftballons", 5));

    [Fact]
    public void NoStrip_BareSpace_NoGateAvailable() =>
        // No track number supplied — the ambiguous bare-space form is left intact.
        Assert.Equal("16 Tons", TrackNumberPrefix.Strip("16 Tons", null));

    // === Edge: documented intentional behavior ===

    [Fact]
    public void Edge_LeadingNumberEqualsTrackNumber_Strips()
    {
        // Rare collision: leading number == track number. Acceptable because the strip is
        // match-key-only (Option B): "Tons" fails the canonical lookup and the original
        // RawTitle is preserved in the dialog — graceful, not corruption. Documented so the
        // behavior is intentional.
        Assert.Equal("Tons", TrackNumberPrefix.Strip("16 Tons", 16));
    }
}
