using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="ConcertSetlistAdapter.ToSetInfoList"/> — the pure mapping
/// that lets <c>ShowLookupService.GetSetlist</c> source setlist data from the
/// <c>concerts/</c> store (a <see cref="ConcertReference"/>) instead of <c>shows.json</c>.
/// Plain data types only; no WPF, no file I/O.
/// </summary>
public class ConcertSetlistAdapterTests
{
    private static ConcertReference MultiSet() => new()
    {
        Date = "1971-05-30",
        HasSetlist = true,
        Sets = new List<ConcertSet>
        {
            new()
            {
                Name = "Set 1",
                Songs = new List<ConcertSong>
                {
                    new() { Name = "Bertha", Segue = false },
                    new() { Name = "Playing In The Band", Segue = false },
                    new() { Name = "Good Lovin'", Segue = true },   // segue on last song of the set
                }
            },
            new()
            {
                Name = "Set 2",
                Songs = new List<ConcertSong>
                {
                    new() { Name = "China Cat Sunflower", Segue = true },
                    new() { Name = "I Know You Rider", Segue = false },
                }
            },
            new()
            {
                Name = "Encore",
                Songs = new List<ConcertSong>
                {
                    new() { Name = "Johnny B. Goode", Segue = false },
                }
            },
        }
    };

    [Fact]
    public void PreservesSetBoundaries_AndLabels()
    {
        var sets = ConcertSetlistAdapter.ToSetInfoList(MultiSet());

        Assert.NotNull(sets);
        Assert.Equal(3, sets!.Count);
        Assert.Equal(new[] { "Set 1", "Set 2", "Encore" }, sets.Select(s => s.Label).ToArray());
        // Boundaries preserved so GetDiscTrack's set-size arithmetic stays correct.
        Assert.Equal(new[] { 3, 2, 1 }, sets.Select(s => s.Songs.Count).ToArray());
    }

    [Fact]
    public void PreservesSongNamesInOrder()
    {
        var sets = ConcertSetlistAdapter.ToSetInfoList(MultiSet())!;

        Assert.Equal(new[] { "Bertha", "Playing In The Band", "Good Lovin'" },
            sets[0].Songs.Select(s => s.Name).ToArray());
        Assert.Equal(new[] { "China Cat Sunflower", "I Know You Rider" },
            sets[1].Songs.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void CarriesSegueVerbatim_IncludingLastSongOfASet()
    {
        var sets = ConcertSetlistAdapter.ToSetInfoList(MultiSet())!;

        Assert.False(sets[0].Songs[0].Segue);  // Bertha
        Assert.False(sets[0].Songs[1].Segue);  // Playing In The Band
        Assert.True(sets[0].Songs[2].Segue);   // Good Lovin' — segue on the set's last song
        Assert.True(sets[1].Songs[0].Segue);   // China Cat Sunflower
        Assert.False(sets[1].Songs[1].Segue);  // I Know You Rider
    }

    [Fact]
    public void DerivesEncoreFlagFromSetName()
    {
        var sets = ConcertSetlistAdapter.ToSetInfoList(MultiSet())!;

        Assert.False(sets[0].Encore);  // "Set 1"
        Assert.False(sets[1].Encore);  // "Set 2"
        Assert.True(sets[2].Encore);   // "Encore"
    }

    [Fact]
    public void NullConcert_YieldsNull()
    {
        Assert.Null(ConcertSetlistAdapter.ToSetInfoList(null));
    }

    [Fact]
    public void NoSets_YieldsNull()
    {
        var noSetlist = new ConcertReference { Date = "1969-01-01", HasSetlist = false };
        Assert.Null(ConcertSetlistAdapter.ToSetInfoList(noSetlist));
    }

    [Fact]
    public void SetsWithNoSongs_YieldNull()
    {
        // A setlist whose sets exist but contain no songs is treated as "no setlist",
        // matching the historical GetSetlist null contract.
        var emptySongs = new ConcertReference
        {
            Date = "1969-01-01",
            HasSetlist = true,
            Sets = new List<ConcertSet> { new() { Name = "Set 1", Songs = new List<ConcertSong>() } }
        };
        Assert.Null(ConcertSetlistAdapter.ToSetInfoList(emptySongs));
    }
}
