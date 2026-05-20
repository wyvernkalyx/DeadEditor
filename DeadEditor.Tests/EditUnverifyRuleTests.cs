using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Rules tests for <see cref="EditUnverifyRule"/>: the strict-diff dirty
/// check that drives the amber left-edge marker, and the unverify-on-edit
/// gating that drops the verified flag.
///
/// Memo § 5 (unverify-on-edit) names the Archivist Note as the one carve-
/// out. § 6 Scenario A names strict equality as the marker comparison.
/// These tests pin both decisions.
/// </summary>
public class EditUnverifyRuleTests
{
    // ===== IsDirty: strict string equality =====

    [Fact]
    public void IsDirty_EmptyBaselineAndEmptyCurrent_NotDirty()
    {
        Assert.False(EditUnverifyRule.IsDirty("", ""));
    }

    [Fact]
    public void IsDirty_NullBaselineAndEmptyCurrent_NotDirty()
    {
        // Null is treated as the empty string so callers can pass either.
        Assert.False(EditUnverifyRule.IsDirty(null, ""));
        Assert.False(EditUnverifyRule.IsDirty("", null));
        Assert.False(EditUnverifyRule.IsDirty(null, null));
    }

    [Fact]
    public void IsDirty_IdenticalNonEmptyValues_NotDirty()
    {
        Assert.False(EditUnverifyRule.IsDirty("Capitol Theatre", "Capitol Theatre"));
    }

    [Fact]
    public void IsDirty_DifferentValues_IsDirty()
    {
        Assert.True(EditUnverifyRule.IsDirty("Capitol Theatre", "Fillmore East"));
    }

    [Fact]
    public void IsDirty_CaseDifference_IsDirty()
    {
        // No case-folding — the user typing a different case has changed
        // the value as far as the tag/manifest are concerned.
        Assert.True(EditUnverifyRule.IsDirty("Capitol Theatre", "capitol theatre"));
    }

    [Fact]
    public void IsDirty_TrailingWhitespaceDifference_IsDirty()
    {
        // No trimming. Whitespace differences count as edits because they
        // round-trip through the manifest verbatim.
        Assert.True(EditUnverifyRule.IsDirty("Capitol Theatre", "Capitol Theatre "));
        Assert.True(EditUnverifyRule.IsDirty(" Capitol Theatre", "Capitol Theatre"));
    }

    [Fact]
    public void IsDirty_RevertingMakesNotDirty()
    {
        // Reverting a typo: type "Capitol Theatr" then put the 'e' back.
        // Current matches baseline again — should be clean.
        const string baseline = "Capitol Theatre";
        Assert.True(EditUnverifyRule.IsDirty(baseline, "Capitol Theatr"));
        Assert.False(EditUnverifyRule.IsDirty(baseline, "Capitol Theatre"));
    }

    // ===== WouldUnverify: gating rules =====

    [Fact]
    public void WouldUnverify_AlbumNotVerified_NeverUnverifies()
    {
        // If the album isn't verified, no edit can unverify it.
        Assert.False(EditUnverifyRule.WouldUnverify(
            baseline: "old", current: "new", isVerified: false, isArchivistNote: false));
    }

    [Fact]
    public void WouldUnverify_ArchivistNoteEdit_NeverUnverifies()
    {
        // § 2 / § 5 carve-out: editing the Archivist Note does NOT unverify,
        // even when the album is verified and the note content changed.
        Assert.False(EditUnverifyRule.WouldUnverify(
            baseline: "old note", current: "new note", isVerified: true, isArchivistNote: true));
    }

    [Fact]
    public void WouldUnverify_VerifiedAlbumNonNoteFieldChanged_DoesUnverify()
    {
        Assert.True(EditUnverifyRule.WouldUnverify(
            baseline: "Capitol Theatre", current: "Fillmore East",
            isVerified: true, isArchivistNote: false));
    }

    [Fact]
    public void WouldUnverify_VerifiedAlbumNonNoteFieldUnchanged_DoesNotUnverify()
    {
        // No actual diff — even with isVerified=true, nothing to unverify.
        Assert.False(EditUnverifyRule.WouldUnverify(
            baseline: "Capitol Theatre", current: "Capitol Theatre",
            isVerified: true, isArchivistNote: false));
    }

    [Fact]
    public void WouldUnverify_VerifiedAlbumNoteUnchanged_DoesNotUnverify()
    {
        Assert.False(EditUnverifyRule.WouldUnverify(
            baseline: "same note", current: "same note",
            isVerified: true, isArchivistNote: true));
    }
}
