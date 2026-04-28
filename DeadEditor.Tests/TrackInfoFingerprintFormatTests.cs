using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="TrackInfo.FormatFingerprintForDisplay"/>, the static helper
/// shared by the Track Info dialog and (indirectly) the Edit Metadata sidebar.
///
/// Truncation rule: null/empty/whitespace -> em-dash; length &lt;= 12 -> full value;
/// length &gt; 12 -> first 12 chars + horizontal-ellipsis.
/// </summary>
public class TrackInfoFingerprintFormatTests
{
    [Fact]
    public void FormatFingerprintForDisplay_Null_ReturnsEmDash()
    {
        var result = TrackInfo.FormatFingerprintForDisplay(null);
        Assert.Equal("—", result);
    }

    [Fact]
    public void FormatFingerprintForDisplay_Empty_ReturnsEmDash()
    {
        var result = TrackInfo.FormatFingerprintForDisplay("");
        Assert.Equal("—", result);
    }

    [Fact]
    public void FormatFingerprintForDisplay_Whitespace_ReturnsEmDash()
    {
        var result = TrackInfo.FormatFingerprintForDisplay("   ");
        Assert.Equal("—", result);
    }

    [Fact]
    public void FormatFingerprintForDisplay_ShorterThanWindow_ReturnsFullValue()
    {
        // Real Chromaprint output is always longer than 12 chars; this case is for
        // robustness - we want the cleaner "no ellipsis" form when the input fits.
        var result = TrackInfo.FormatFingerprintForDisplay("AQADtMm");
        Assert.Equal("AQADtMm", result);
    }

    [Fact]
    public void FormatFingerprintForDisplay_ExactlyWindow_ReturnsFullValueNoEllipsis()
    {
        var input = "AQADtMmS_RGY"; // 12 chars
        var result = TrackInfo.FormatFingerprintForDisplay(input);
        Assert.Equal("AQADtMmS_RGY", result);
        Assert.DoesNotContain("…", result);
    }

    [Fact]
    public void FormatFingerprintForDisplay_LongerThanWindow_ReturnsTruncatedWithEllipsis()
    {
        var input = "AQADtMmS_RGYabcdefghijklmnopqrstuvwxyz";
        var result = TrackInfo.FormatFingerprintForDisplay(input);
        Assert.Equal("AQADtMmS_RGY…", result);
    }

    [Fact]
    public void FormatFingerprintForDisplay_RealChromaprintOutput_TruncatesToTwelveCharsPlusEllipsis()
    {
        // Excerpt from MetadataServiceFingerprintTests.TestFingerprint - representative
        // shape and length of real fpcalc output (hundreds of base64-like chars).
        var realFingerprint = "AQADtFRSXcS-MUz1nZHk6_jM50dPJI3qPM-ho-mD7Eum6Yt-9MWP9MGPHj1y9MfJOEd_Iv2Q9Mhx9EePHkdy9OmTPMd_PHmO_PiR8seRP9HxoaeS9MeJPshxJEf6IH3Q4z967IePPUePoz-S5_jR50d_5MOPI3l-fOidI3lyJDfyHk2OfsiPHE-OHs9R9DiOI8eR48iRH8mTJ8eR48iPI8mPHsfx40iSI8mP5MeR48jx5DiOIzny40hyJEd-HMlxJD-O5DhyHEdyHMmRHEdyJEdyHMdxJEdyHMmRHMdx";
        var result = TrackInfo.FormatFingerprintForDisplay(realFingerprint);
        Assert.Equal(13, result.Length); // 12 chars + 1 ellipsis char
        Assert.EndsWith("…", result);
        Assert.Equal(realFingerprint.Substring(0, 12), result.Substring(0, 12));
    }
}
