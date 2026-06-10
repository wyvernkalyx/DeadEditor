using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Rule tests for <see cref="DuplicateDateRule.IsCollision"/>: the duplicate-date refusal that
/// guards the setlist editor's save path (add-concert-spec.md Decision 1). The load-bearing case
/// is the no-change re-save exclusion — a record saving itself under its own date must NOT be
/// treated as a collision. One predicate covers both the New-Concert create path and the
/// pre-existing mid-edit silent-overwrite.
/// </summary>
public class DuplicateDateRuleTests
{
    [Fact]
    public void NewConcert_DateAlreadyTaken_IsCollision()
    {
        // New Concert carries originalDate == "" (no prior file); a taken date collides.
        Assert.True(DuplicateDateRule.IsCollision(dateExistsInStore: true, date: "1977-05-08", originalDate: ""));
    }

    [Fact]
    public void NewConcert_FreeDate_NotCollision()
    {
        Assert.False(DuplicateDateRule.IsCollision(dateExistsInStore: false, date: "2099-01-01", originalDate: ""));
    }

    [Fact]
    public void NoChangeReSave_SameDate_NotCollision()
    {
        // The load-bearing exclusion: re-saving an existing concert without changing its date is
        // the record saving ITSELF (its date is in the store), not a collision. Must pass.
        Assert.False(DuplicateDateRule.IsCollision(dateExistsInStore: true, date: "1977-05-08", originalDate: "1977-05-08"));
    }

    [Fact]
    public void MidEditCollision_DateChangedOntoAnotherRecord_IsCollision()
    {
        // Editing concert A's date onto concert B's existing date — the pre-existing
        // silent-overwrite path. Both dates non-empty, they differ, and the target exists.
        Assert.True(DuplicateDateRule.IsCollision(dateExistsInStore: true, date: "1977-05-08", originalDate: "1977-05-07"));
    }

    [Fact]
    public void FreeDateRekey_DateChangedToUnusedDate_NotCollision()
    {
        // Changing an existing concert's date to a date no record holds — a clean rekey, not a
        // collision. The store-existence fact is false, so it passes regardless of the date diff.
        Assert.False(DuplicateDateRule.IsCollision(dateExistsInStore: false, date: "2099-01-03", originalDate: "2099-01-02"));
    }

    // NOTE: a dedicated Ordinal-sensitivity test (case 6 in the commit plan) is intentionally
    // omitted. Concert keys are yyyy-MM-dd strings — pure ASCII digits and hyphens — so an
    // Ordinal comparison and a culture-aware one can never diverge for them (no case-folding,
    // no locale-specific letter collapsing applies). A test purporting to prove Ordinal matters
    // here would be hollow. The Ordinal choice is still correct and documented on the rule (the
    // date IS the filename/cache key, so identity is the exact string); it just isn't observable
    // through the date domain the rule operates on.
}
