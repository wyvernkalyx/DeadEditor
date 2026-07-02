using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="AliasRowBuilder"/> — the pure composition backing the setlist editor's
/// combine display + staged removal (alias-setlists-spec.md §6.2). Verifies the net row set
/// (recorded, with staged removals flagged and kept; plus pending appends), the origin/state flags
/// the ✕/↺ control branches on, ordering (recorded first), and SequenceEqual-based removal identity.
/// WPF-free: the label is an injected function, so no UI runtime is touched.
/// </summary>
public class AliasRowBuilderTests
{
    // Identity labeler: renders the covered set so assertions can see which row is which.
    private static string Label(IReadOnlyList<int> covered) => string.Join(",", covered);

    private static IReadOnlyList<int> Run(params int[] xs) => xs;

    [Fact]
    public void Build_AllEmpty_ReturnsEmptyRowList()
    {
        var rows = AliasRowBuilder.Build(
            recorded: null, pendingRemovals: null, pendingAppends: null, labeler: Label);

        Assert.Empty(rows);
    }

    [Fact]
    public void Build_RecordedOnly_ProducesRecordedRowsWithNoFlags()
    {
        var rows = AliasRowBuilder.Build(
            recorded: new[] { Run(0, 1), Run(4, 5) },
            pendingRemovals: null, pendingAppends: null, labeler: Label);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.False(r.IsPendingAppend));
        Assert.All(rows, r => Assert.False(r.IsStagedForRemoval));
        Assert.Equal("0,1", rows[0].Label);
        Assert.Equal("4,5", rows[1].Label);
    }

    [Fact]
    public void Build_RecordedWithOneStagedForRemoval_FlagsThatRowAndKeepsIt()
    {
        var rows = AliasRowBuilder.Build(
            recorded: new[] { Run(0, 1), Run(4, 5) },
            pendingRemovals: new[] { Run(0, 1) },
            pendingAppends: null, labeler: Label);

        Assert.Equal(2, rows.Count);                              // staged row is KEPT, not hidden
        var staged = rows.Single(r => r.CoveredIndices.SequenceEqual(Run(0, 1)));
        Assert.True(staged.IsStagedForRemoval);
        Assert.False(staged.IsPendingAppend);
        var other = rows.Single(r => r.CoveredIndices.SequenceEqual(Run(4, 5)));
        Assert.False(other.IsStagedForRemoval);
    }

    [Fact]
    public void Build_PendingAppendPresent_FlagsRowAsPendingAppend()
    {
        var rows = AliasRowBuilder.Build(
            recorded: null, pendingRemovals: null,
            pendingAppends: new[] { Run(2, 3) }, labeler: Label);

        var row = Assert.Single(rows);
        Assert.True(row.IsPendingAppend);
        Assert.False(row.IsStagedForRemoval);
        Assert.Equal("2,3", row.Label);
    }

    [Fact]
    public void Build_Mixed_HasCorrectFlagsCountAndOrder()
    {
        // Two recorded (one staged for removal) + one pending append.
        var rows = AliasRowBuilder.Build(
            recorded: new[] { Run(0, 1), Run(4, 5) },
            pendingRemovals: new[] { Run(4, 5) },
            pendingAppends: new[] { Run(7, 8) },
            labeler: Label);

        Assert.Equal(3, rows.Count);

        // Recorded rows come first, in order, then the pending append.
        Assert.Equal(new[] { "0,1", "4,5", "7,8" }, rows.Select(r => r.Label).ToArray());

        Assert.False(rows[0].IsPendingAppend);
        Assert.False(rows[0].IsStagedForRemoval);   // 0,1 recorded, untouched

        Assert.False(rows[1].IsPendingAppend);
        Assert.True(rows[1].IsStagedForRemoval);     // 4,5 recorded, staged for removal

        Assert.True(rows[2].IsPendingAppend);        // 7,8 pending append
        Assert.False(rows[2].IsStagedForRemoval);
    }

    [Fact]
    public void Build_RemovalIdentityIsSequenceEqual_ReorderedSetDoesNotMatch()
    {
        // A reordered covered set is a different identity, so it must NOT flag the recorded [0,1] row.
        var rows = AliasRowBuilder.Build(
            recorded: new[] { Run(0, 1) },
            pendingRemovals: new[] { Run(1, 0) },
            pendingAppends: null, labeler: Label);

        var row = Assert.Single(rows);
        Assert.False(row.IsStagedForRemoval);
    }
}
