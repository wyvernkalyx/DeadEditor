using System.Collections.Generic;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="CombinedTrackDecomposer"/>: the pure combined-track decomposition engine
/// (alias-setlists-spec.md 5.1 atomicity guard + 5.2 validation gate). The resolver is a fake
/// dictionary so no songs.json is loaded — mirroring the SetlistMatcher tests' injected-resolver
/// style. Accept cases assert exact covered indices; reject cases assert null (the data-integrity
/// guarantees: never split an atomic name, never fabricate a song, never accept a non-contiguous
/// or reordered run).
/// </summary>
public class CombinedTrackDecomposerTests
{
    // Known titles/aliases -> official, including the two slice-0 separator blockers.
    private const string Medley =
        "Devil With the Blue Dress/Good Golly Miss Molly/Devil With the Blue Dress";

    private static readonly Dictionary<string, string> Known = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Ordinary songs (resolve to themselves).
        { "Help On The Way", "Help On The Way" },
        { "Slipknot!", "Slipknot!" },
        { "Space", "Space" },
        { "The Other One", "The Other One" },
        { "Sugaree", "Sugaree" },
        { "A", "A" },
        { "B", "B" },
        { "C", "C" },
        { "X", "X" },
        // Slice-0 blockers: both must resolve WHOLE so the atomicity guard keeps them intact.
        { Medley, Medley },
        { "I Know You Rider> High Time tease", "I Know You Rider" },
    };

    private static string? Resolve(string s) => Known.TryGetValue(s, out var official) ? official : null;

    // ===== Accept cases (exact indices) =====

    [Fact]
    public void Slash_TwoAdjacent_ReturnsBothIndices()
    {
        var official = new[] { "Sugaree", "Help On The Way", "Slipknot!", "Space" };
        var result = CombinedTrackDecomposer.Decompose("Help On The Way/Slipknot!", official, Resolve);
        Assert.Equal(new[] { 1, 2 }, result);
    }

    [Fact]
    public void Gt_TwoAdjacent_ReturnsBothIndices()
    {
        var official = new[] { "Sugaree", "Space", "The Other One" };
        var result = CombinedTrackDecomposer.Decompose("Space > The Other One", official, Resolve);
        Assert.Equal(new[] { 1, 2 }, result);
    }

    [Fact]
    public void Arrow_TwoAdjacent_ReturnsBothIndices()
    {
        var official = new[] { "X", "A", "B" };
        var result = CombinedTrackDecomposer.Decompose("A->B", official, Resolve);
        Assert.Equal(new[] { 1, 2 }, result);
    }

    [Fact]
    public void ThreeComponents_AllAdjacent_ReturnsThreeIndices()
    {
        var official = new[] { "X", "A", "B", "C" };
        var result = CombinedTrackDecomposer.Decompose("A > B > C", official, Resolve);
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void WhitespaceVariants_YieldSameIndices()
    {
        var official = new[] { "A", "B" };
        var tight = CombinedTrackDecomposer.Decompose("A>B", official, Resolve);
        var spaced = CombinedTrackDecomposer.Decompose("A > B", official, Resolve);
        var wide = CombinedTrackDecomposer.Decompose("A  >  B", official, Resolve);

        Assert.Equal(new[] { 0, 1 }, tight);
        Assert.Equal(new[] { 0, 1 }, spaced);
        Assert.Equal(new[] { 0, 1 }, wide);
    }

    // ===== Reject cases (null) =====

    [Fact]
    public void AtomicMedley_NeverSplit_ReturnsNull()
    {
        // CRITICAL: the medley official title contains "/" but resolves whole -> atomic.
        var official = new[] { "Sugaree", Medley, "Space" };
        var result = CombinedTrackDecomposer.Decompose(Medley, official, Resolve);
        Assert.Null(result);
    }

    [Fact]
    public void AtomicAliasWithSeparator_NeverSplit_ReturnsNull()
    {
        // CRITICAL: the rider-tease alias contains ">" but resolves whole -> atomic.
        var official = new[] { "Sugaree", "I Know You Rider", "Space" };
        var result = CombinedTrackDecomposer.Decompose("I Know You Rider> High Time tease", official, Resolve);
        Assert.Null(result);
    }

    [Fact]
    public void UnknownComponent_ReturnsNull()
    {
        var official = new[] { "Space", "The Other One" };
        var result = CombinedTrackDecomposer.Decompose("Space > Not A Real Song", official, Resolve);
        Assert.Null(result);
    }

    [Fact]
    public void NonContiguous_ReturnsNull()
    {
        // Both real and present, but separated by X in the official order.
        var official = new[] { "A", "X", "B" };
        var result = CombinedTrackDecomposer.Decompose("A > B", official, Resolve);
        Assert.Null(result);
    }

    [Fact]
    public void Reordered_ReturnsNull()
    {
        var official = new[] { "A", "B" };
        var result = CombinedTrackDecomposer.Decompose("B > A", official, Resolve);
        Assert.Null(result);
    }

    [Fact]
    public void ComponentsAbsentFromSetlist_ReturnsNull()
    {
        // A and B are real songs but neither appears in this setlist.
        var official = new[] { "Sugaree", "Space", "The Other One" };
        var result = CombinedTrackDecomposer.Decompose("A > B", official, Resolve);
        Assert.Null(result);
    }

    [Fact]
    public void SingleComponent_NoSeparator_ReturnsNull()
    {
        var official = new[] { "Sugaree", "Space" };
        var result = CombinedTrackDecomposer.Decompose("Sugaree", official, Resolve);
        Assert.Null(result);
    }
}
