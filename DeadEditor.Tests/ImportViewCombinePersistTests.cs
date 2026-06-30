using DeadEditor.Models;
using System.Collections.Generic;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="DeadEditor.ImportView.ShouldPersistCombines"/> — the safety gate for
/// Option B import-commit alias persistence (alias-setlists-spec.md §4). The predicate is pure and
/// static, so it is exercised here without constructing the WPF view. It must persist ONLY when the
/// album being committed is the one that was matched, at the date it was matched against — a stale
/// match (match album A, then import a different album/date without re-matching) must NOT persist.
/// </summary>
public class ImportViewCombinePersistTests
{
    private static IReadOnlyList<AliasEntry> OneCombine() => new List<AliasEntry>
    {
        new AliasEntry { CoveredOfficialIndices = new List<int> { 0, 1 } },
    };

    [Fact]
    public void MatchingDate_WithStash_PersistsTrue()
    {
        Assert.True(DeadEditor.ImportView.ShouldPersistCombines("1975-08-13", "1975-08-13", OneCombine()));
    }

    [Fact]
    public void MismatchedDate_MatchedAImportingB_False()
    {
        // Matched album A (1975-08-13), now committing a different album/date B (1977-05-08)
        // without re-matching — the stale coverage must not be persisted for B.
        Assert.False(DeadEditor.ImportView.ShouldPersistCombines("1975-08-13", "1977-05-08", OneCombine()));
    }

    [Fact]
    public void NullStash_False()
    {
        Assert.False(DeadEditor.ImportView.ShouldPersistCombines("1975-08-13", "1975-08-13", null));
    }

    [Fact]
    public void EmptyStash_False()
    {
        Assert.False(DeadEditor.ImportView.ShouldPersistCombines("1975-08-13", "1975-08-13", new List<AliasEntry>()));
    }

    [Fact]
    public void NullLastMatchDate_False()
    {
        Assert.False(DeadEditor.ImportView.ShouldPersistCombines(null, "1975-08-13", OneCombine()));
    }

    [Fact]
    public void EmptyLastMatchDate_False()
    {
        Assert.False(DeadEditor.ImportView.ShouldPersistCombines("", "", OneCombine()));
    }
}
