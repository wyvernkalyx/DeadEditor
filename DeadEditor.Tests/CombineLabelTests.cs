using System.Collections.Generic;
using DeadEditor.Helpers;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="CombineLabel"/> — the pure formatter shared by the setlist editor's
/// combine list and the read-only concert detail view, so both render an alias entry identically.
/// Verifies the "Name &gt; Name (first–last)" form (1-based positions, en-dash range), the empty
/// fallback, and the out-of-range name fallback the callers supply.
/// </summary>
public class CombineLabelTests
{
    private static IReadOnlyList<int> Run(params int[] xs) => xs;

    // Resolver over a fixed flattened setlist; out-of-range falls back to "#N" (1-based).
    private static string NameAt(IReadOnlyList<string> flat, int i) =>
        i >= 0 && i < flat.Count ? flat[i] : $"#{i + 1}";

    [Fact]
    public void Describe_MultiSongRun_JoinsWithSegueAndEnDashRange()
    {
        var flat = new List<string> { "A", "B", "C", "Dark Star", "St. Stephen", "The Eleven" };

        var label = CombineLabel.Describe(Run(3, 4, 5), i => NameAt(flat, i));

        // Positions are 1-based (indices + 1), so covered 3..5 -> "(4–6)". Range uses an en dash.
        Assert.Equal("Dark Star > St. Stephen > The Eleven (4–6)", label);
    }

    [Fact]
    public void Describe_SingleIndex_RendersSamePositionBothSides()
    {
        var flat = new List<string> { "Bertha", "Playing in the Band" };

        var label = CombineLabel.Describe(Run(1), i => NameAt(flat, i));

        Assert.Equal("Playing in the Band (2–2)", label);
    }

    [Fact]
    public void Describe_EmptyRun_ReturnsEmptyPlaceholder()
    {
        Assert.Equal("(empty)", CombineLabel.Describe(Run(), i => "unused"));
    }

    [Fact]
    public void Describe_NullRun_ReturnsEmptyPlaceholder()
    {
        Assert.Equal("(empty)", CombineLabel.Describe(null!, i => "unused"));
    }

    [Fact]
    public void Describe_IndexOutOfRange_UsesCallerFallbackName()
    {
        var flat = new List<string> { "Only One" };

        // Index 0 resolves; index 2 is past the flattened list -> "#3" fallback.
        var label = CombineLabel.Describe(Run(0, 2), i => NameAt(flat, i));

        Assert.Equal("Only One > #3 (1–3)", label);
    }
}
