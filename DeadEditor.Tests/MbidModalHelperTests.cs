using System.Collections.Generic;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for the modal-MBID computation that drives the import view's
/// "use existing MBID vs. fingerprint" decision when source files carry
/// MUSICBRAINZ_ALBUMID. See documentation/mbid-foundation-spec.md § Commit 1.5.
/// </summary>
public class MbidModalHelperTests
{
    private const string MbidA = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    private const string MbidB = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void ComputeModal_AllSameNonEmpty_ReturnsThatValueWithAgreement()
    {
        var values = new List<string?> { MbidA, MbidA, MbidA };

        var result = MbidModalHelper.ComputeModal(values);

        Assert.Equal(MbidA, result.ModalMbid);
        Assert.True(result.AllAgree);
        Assert.True(result.AnyPresent);
    }

    [Fact]
    public void ComputeModal_AllEmpty_ReturnsNullAndNotPresent()
    {
        var values = new List<string?> { null, "", "   ", null };

        var result = MbidModalHelper.ComputeModal(values);

        Assert.Null(result.ModalMbid);
        Assert.True(result.AllAgree);
        Assert.False(result.AnyPresent);
    }

    [Fact]
    public void ComputeModal_TwoThirdsSameOneThirdDifferent_ReturnsMajorityWithDisagreement()
    {
        var values = new List<string?> { MbidA, MbidA, MbidB };

        var result = MbidModalHelper.ComputeModal(values);

        Assert.Equal(MbidA, result.ModalMbid);
        Assert.False(result.AllAgree);
        Assert.True(result.AnyPresent);
    }

    [Fact]
    public void ComputeModal_AllDifferent_ReturnsFirstByCountWithDisagreement()
    {
        var values = new List<string?>
        {
            MbidA,
            MbidB,
            "22222222-3333-4444-5555-666666666666"
        };

        var result = MbidModalHelper.ComputeModal(values);

        Assert.NotNull(result.ModalMbid);
        Assert.False(result.AllAgree);
        Assert.True(result.AnyPresent);
    }

    [Fact]
    public void ComputeModal_PartiallyTagged_TreatsEmptiesAsAbsentNotDisagreement()
    {
        // 3 tracks tagged with the same MBID, 5 tracks empty.
        // Per spec: empties are absences, not disagreements. The single distinct
        // present value means AllAgree = true (no inconsistent MBIDs to warn about).
        var values = new List<string?> { MbidA, null, MbidA, "", MbidA, null, "", null };

        var result = MbidModalHelper.ComputeModal(values);

        Assert.Equal(MbidA, result.ModalMbid);
        Assert.True(result.AllAgree);
        Assert.True(result.AnyPresent);
    }

    [Fact]
    public void ComputeModal_NormalizesCasing()
    {
        // External tools may write MBIDs in upper case; we group case-insensitively
        // since UUIDs are case-insensitive identifiers.
        var values = new List<string?>
        {
            MbidA,
            MbidA.ToUpperInvariant(),
            "  " + MbidA + "  "
        };

        var result = MbidModalHelper.ComputeModal(values);

        Assert.Equal(MbidA, result.ModalMbid);
        Assert.True(result.AllAgree);
        Assert.True(result.AnyPresent);
    }
}
