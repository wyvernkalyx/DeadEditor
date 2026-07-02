using System;
using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="SetlistProjection.Build"/> — the pure flatten/canonicalize/position
/// projection shared by the Match Setlist flatten and the reference side-panel
/// (reference-side-panel-spec.md §5.1). Plain data types only; no WPF, no file I/O. The canonical
/// resolver is supplied per-call so the helper stays service-free.
/// </summary>
public class SetlistProjectionTests
{
    // Echo resolver: mirrors "GetOfficialTitle(name) ?? name" for a name with no alias mapping.
    private static readonly Func<string, string?> EchoResolver = name => name;

    private static List<SetInfo> MultiSet() => new()
    {
        new SetInfo
        {
            Label = "Set 1",
            Songs = new List<SetlistSong>
            {
                new() { Name = "Bertha", Segue = false },
                new() { Name = "Playing In The Band", Segue = false },
                new() { Name = "Good Lovin'", Segue = true },   // segue on the set's last song
            }
        },
        new SetInfo
        {
            Label = "Set 2",
            Songs = new List<SetlistSong>
            {
                new() { Name = "China Cat Sunflower", Segue = true },
                new() { Name = "I Know You Rider", Segue = false },
            }
        },
        new SetInfo
        {
            Label = "Encore",
            Songs = new List<SetlistSong>
            {
                new() { Name = "Johnny B. Goode", Segue = false },
            }
        },
    };

    [Fact]
    public void FlattensAllSets_WithGlobalZeroBasedPositions()
    {
        var projection = SetlistProjection.Build(MultiSet(), EchoResolver);

        Assert.Equal(6, projection.Count);
        // Position is a single global counter across set boundaries, 0-based and dense.
        Assert.Equal(Enumerable.Range(0, 6), projection.Select(e => e.Position));
        Assert.Equal(
            new[] { "Bertha", "Playing In The Band", "Good Lovin'", "China Cat Sunflower", "I Know You Rider", "Johnny B. Goode" },
            projection.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void CarriesSegueVerbatim_IncludingLastSongOfASet()
    {
        var projection = SetlistProjection.Build(MultiSet(), EchoResolver);

        Assert.False(projection[0].Segue);  // Bertha
        Assert.False(projection[1].Segue);  // Playing In The Band
        Assert.True(projection[2].Segue);   // Good Lovin' — segue on the set's last song
        Assert.True(projection[3].Segue);   // China Cat Sunflower
        Assert.False(projection[4].Segue);  // I Know You Rider
        Assert.False(projection[5].Segue);  // Johnny B. Goode
    }

    [Fact]
    public void CanonicalRunsThroughSuppliedResolver_EchoesOnMiss()
    {
        // Resolver maps one alias to its official title and returns null (miss) for everything else,
        // exercising the "resolver(name) ?? name" echo path.
        Func<string, string?> resolver = name =>
            name == "China Cat Sunflower" ? "China Cat Sunflower [official]" : null;

        var projection = SetlistProjection.Build(MultiSet(), resolver);

        Assert.Equal("China Cat Sunflower [official]", projection[3].Canonical); // resolved
        Assert.Equal("Bertha", projection[0].Canonical);                          // echoed on miss
        // The stored display Name is never rewritten by canonicalization.
        Assert.Equal("China Cat Sunflower", projection[3].Name);
    }

    [Fact]
    public void SetLabel_IsSetNamePlusOneBasedPositionWithinSet()
    {
        var projection = SetlistProjection.Build(MultiSet(), EchoResolver);

        // "{set.Label}, #{trackWithinSet}" — track counter resets per set, 1-based.
        Assert.Equal("Set 1, #1", projection[0].SetLabel);
        Assert.Equal("Set 1, #2", projection[1].SetLabel);
        Assert.Equal("Set 1, #3", projection[2].SetLabel);
        Assert.Equal("Set 2, #1", projection[3].SetLabel);
        Assert.Equal("Set 2, #2", projection[4].SetLabel);
        Assert.Equal("Encore, #1", projection[5].SetLabel);
    }

    [Fact]
    public void SingleSet_FlattensToOneRun()
    {
        var single = new List<SetInfo>
        {
            new()
            {
                Label = "Set 1",
                Songs = new List<SetlistSong>
                {
                    new() { Name = "Cold Rain and Snow", Segue = false },
                    new() { Name = "Beat It On Down The Line", Segue = false },
                }
            }
        };

        var projection = SetlistProjection.Build(single, EchoResolver);

        Assert.Equal(2, projection.Count);
        Assert.Equal(new[] { 0, 1 }, projection.Select(e => e.Position).ToArray());
        Assert.Equal("Set 1, #1", projection[0].SetLabel);
        Assert.Equal("Set 1, #2", projection[1].SetLabel);
    }

    [Fact]
    public void NullSetlist_YieldsEmpty()
    {
        var projection = SetlistProjection.Build(null, EchoResolver);
        Assert.Empty(projection);
    }

    [Fact]
    public void EmptySetlist_YieldsEmpty()
    {
        var projection = SetlistProjection.Build(new List<SetInfo>(), EchoResolver);
        Assert.Empty(projection);
    }

    [Fact]
    public void SetWithNoSongs_ContributesNothing_AndDoesNotAdvancePosition()
    {
        var withEmptySet = new List<SetInfo>
        {
            new() { Label = "Soundcheck", Songs = new List<SetlistSong>() },  // empty, contributes nothing
            new()
            {
                Label = "Set 1",
                Songs = new List<SetlistSong>
                {
                    new() { Name = "Truckin'", Segue = false },
                }
            }
        };

        var projection = SetlistProjection.Build(withEmptySet, EchoResolver);

        Assert.Single(projection);
        Assert.Equal(0, projection[0].Position);      // position not advanced by the empty set
        Assert.Equal("Set 1, #1", projection[0].SetLabel);
    }
}
