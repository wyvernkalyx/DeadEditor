using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="CombineSelectionRule"/> — the setlist editor's combine-authoring validity
/// predicate (alias-setlists-spec.md §6.2) — and its union-dedup, plus the authoring round-trip
/// through <see cref="ConcertLookupService.TryAppendAliasEntry"/> (no disk). All WPF-free.
/// </summary>
public class CombineSelectionRuleTests
{
    private static CombineRow Row(int pos, string set = "Set 1") => new(pos, set);

    // ===== Validity predicate =====

    [Fact]
    public void Evaluate_ContiguousWithinSetRun_IsValidWithZeroBasedIndices()
    {
        var result = CombineSelectionRule.Evaluate(new[] { Row(1), Row(2) });

        Assert.True(result.IsValid);
        Assert.Equal(new[] { 0, 1 }, result.CoveredOfficialIndices); // Position p -> index p-1
        Assert.Equal("", result.Message);
    }

    [Fact]
    public void Evaluate_SelectionOrderIndependent_SortsBeforeChecking()
    {
        // Rows can arrive in selection (not positional) order — the rule sorts first.
        var result = CombineSelectionRule.Evaluate(new[] { Row(7), Row(5), Row(6) });

        Assert.True(result.IsValid);
        Assert.Equal(new[] { 4, 5, 6 }, result.CoveredOfficialIndices);
    }

    [Fact]
    public void Evaluate_NonContiguousRun_IsInvalid()
    {
        var result = CombineSelectionRule.Evaluate(new[] { Row(1), Row(3) });

        Assert.False(result.IsValid);
        Assert.Equal(CombineSelectionRule.NotContiguousMessage, result.Message);
        Assert.Empty(result.CoveredOfficialIndices);
    }

    [Fact]
    public void Evaluate_SingleRow_IsInvalid()
    {
        var result = CombineSelectionRule.Evaluate(new[] { Row(1) });

        Assert.False(result.IsValid);
        Assert.Equal(CombineSelectionRule.NeedTwoMessage, result.Message);
    }

    [Fact]
    public void Evaluate_EmptyOrNull_IsInvalid()
    {
        Assert.False(CombineSelectionRule.Evaluate(new CombineRow[0]).IsValid);
        Assert.False(CombineSelectionRule.Evaluate(null).IsValid);
    }

    [Fact]
    public void Evaluate_CrossSetBoundaryRun_IsInvalid_EvenWhenPositionsContiguous()
    {
        // Positions 8,9 are contiguous but straddle the Set 1 / Set 2 boundary — rejected for v1.
        var result = CombineSelectionRule.Evaluate(new[] { Row(8, "Set 1"), Row(9, "Set 2") });

        Assert.False(result.IsValid);
        Assert.Equal(CombineSelectionRule.CrossSetMessage, result.Message);
    }

    [Fact]
    public void Evaluate_DuplicatePositions_IsInvalid()
    {
        var result = CombineSelectionRule.Evaluate(new[] { Row(1), Row(1) });

        Assert.False(result.IsValid);
        Assert.Equal(CombineSelectionRule.NotContiguousMessage, result.Message);
    }

    [Fact]
    public void Evaluate_LongerContiguousRun_MapsAllPositions()
    {
        var result = CombineSelectionRule.Evaluate(new[] { Row(2), Row(3), Row(4), Row(5) });

        Assert.True(result.IsValid);
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.CoveredOfficialIndices);
    }

    // ===== Union dedup =====

    [Fact]
    public void IsDuplicateRun_MatchesExistingRun()
    {
        var existing = new IReadOnlyList<int>[] { new[] { 0, 1 }, new[] { 4, 5 } };

        Assert.True(CombineSelectionRule.IsDuplicateRun(existing, new[] { 0, 1 }));
        Assert.True(CombineSelectionRule.IsDuplicateRun(existing, new[] { 4, 5 }));
    }

    [Fact]
    public void IsDuplicateRun_DistinctRun_IsNotDuplicate()
    {
        var existing = new IReadOnlyList<int>[] { new[] { 0, 1 } };

        Assert.False(CombineSelectionRule.IsDuplicateRun(existing, new[] { 2, 3 }));
        Assert.False(CombineSelectionRule.IsDuplicateRun(null, new[] { 0, 1 }));
    }

    [Fact]
    public void IsDuplicateRun_OverUnionOfRecordedAndPending()
    {
        // Mirror the handler's union: recorded entries (on the concert) + pending (staged).
        var concert = new ConcertReference { Date = "1975-08-13" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 0, 1 } } }
        });
        var pending = new List<AliasEntry>
        {
            new() { CoveredOfficialIndices = { 4, 5 } }
        };

        var union = concert.AliasSetlists
            .SelectMany(a => a.Entries)
            .Concat(pending)
            .Select(en => (IReadOnlyList<int>)en.CoveredOfficialIndices);

        Assert.True(CombineSelectionRule.IsDuplicateRun(union, new[] { 0, 1 }));  // recorded hit
        Assert.True(CombineSelectionRule.IsDuplicateRun(union, new[] { 4, 5 }));  // pending hit
        Assert.False(CombineSelectionRule.IsDuplicateRun(union, new[] { 2, 3 })); // novel run
    }

    // ===== Authoring round-trip: predicate -> AliasEntry -> Save-time apply (no disk) =====

    [Fact]
    public void AuthoredRun_AppliedTwice_IsDedupNoOp()
    {
        // The Save path applies each staged entry via TryAppendAliasEntry. Re-authoring (or a retry)
        // the same covered run must be a dedup no-op — the union-dedup at author time and the
        // SequenceEqual dedup at apply time agree.
        var concert = new ConcertReference { Date = "1975-08-13" };
        var run = CombineSelectionRule.Evaluate(new[] { Row(1), Row(2) }).CoveredOfficialIndices.ToList();

        var first = ConcertLookupService.TryAppendAliasEntry(
            concert, new AliasEntry { CoveredOfficialIndices = run });
        var second = ConcertLookupService.TryAppendAliasEntry(
            concert, new AliasEntry { CoveredOfficialIndices = run.ToList() });

        Assert.True(first);
        Assert.False(second);                               // idempotent
        Assert.Single(concert.AliasSetlists);
        Assert.Single(concert.AliasSetlists[0].Entries);    // one shared AliasSetlist, one entry
        Assert.Equal(new[] { 0, 1 }, concert.AliasSetlists[0].Entries[0].CoveredOfficialIndices);
    }
}
