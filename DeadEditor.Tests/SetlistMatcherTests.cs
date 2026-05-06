using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="SetlistMatcher"/>: Model B "audio is the archive"
/// behavior. The matcher decorates matched tracks with canonical setlist
/// names and segue flags, and leaves unmatched tracks entirely untouched
/// (DiscNumber and TrackNumber preserved).
///
/// Core invariant: regardless of how many tracks match the setlist, no
/// track's DiscNumber or TrackNumber is ever changed by MatchAndDecorate.
/// </summary>
public class SetlistMatcherTests
{
    [Fact]
    public void CleanRecording_AllMatched_NumberingPreserved()
    {
        var tracks = new List<TrackInfo>
        {
            MakeTrack(disc: 1, track: 1, song: "China Cat Sunflower"),
            MakeTrack(disc: 1, track: 2, song: "I Know You Rider"),
            MakeTrack(disc: 1, track: 3, song: "Sugar Magnolia"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "China Cat Sunflower", Canonical = "China Cat Sunflower", Position = 0, Segue = true },
            new() { Name = "I Know You Rider",    Canonical = "I Know You Rider",    Position = 1, Segue = false },
            new() { Name = "Sugar Magnolia",      Canonical = "Sugar Magnolia",      Position = 2, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(3, result.MatchedCount);
        Assert.Equal(1, result.SegueCount);
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, result.ClaimedPositions);

        // SongName + IsMatched + IsModified decorated; numbering preserved.
        Assert.Equal("China Cat Sunflower", tracks[0].SongName);
        Assert.True(tracks[0].Segue);
        Assert.True(tracks[0].IsMatched);
        Assert.True(tracks[0].IsModified);

        Assert.Equal(1, tracks[0].DiscNumber);
        Assert.Equal(1, tracks[0].TrackNumber);
        Assert.Equal(1, tracks[1].DiscNumber);
        Assert.Equal(2, tracks[1].TrackNumber);
        Assert.Equal(1, tracks[2].DiscNumber);
        Assert.Equal(3, tracks[2].TrackNumber);
    }

    [Fact]
    public void OneTuningTrack_LeftUntouched_NoOverflowDisc()
    {
        // Audience recording with a Tuning track between songs 2 and 3.
        // Numbering is the source recording's: 1/1, 1/2, 1/3 (tuning), 1/4.
        var tracks = new List<TrackInfo>
        {
            MakeTrack(disc: 1, track: 1, song: "China Cat Sunflower"),
            MakeTrack(disc: 1, track: 2, song: "I Know You Rider"),
            MakeTrack(disc: 1, track: 3, song: "Tuning"),
            MakeTrack(disc: 1, track: 4, song: "Sugar Magnolia"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "China Cat Sunflower", Canonical = "China Cat Sunflower", Position = 0, Segue = true },
            new() { Name = "I Know You Rider",    Canonical = "I Know You Rider",    Position = 1, Segue = false },
            new() { Name = "Sugar Magnolia",      Canonical = "Sugar Magnolia",      Position = 2, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(3, result.MatchedCount);

        // Tuning track is fully untouched: position, song name, segue, IsMatched/IsModified.
        Assert.Equal(1, tracks[2].DiscNumber);
        Assert.Equal(3, tracks[2].TrackNumber);
        Assert.Equal("Tuning", tracks[2].SongName);
        Assert.False(tracks[2].Segue);
        Assert.Null(tracks[2].IsMatched);   // never touched by matcher
        Assert.False(tracks[2].IsModified); // never touched by matcher

        // No overflow disc was ever created — no track has DiscNumber > 1.
        Assert.All(tracks, t => Assert.Equal(1, t.DiscNumber));
    }

    [Fact]
    public void NoSetlistMatch_AllTracksLeftAlone()
    {
        var tracks = new List<TrackInfo>
        {
            MakeTrack(disc: 1, track: 1, song: "Some Song That Doesnt Match"),
            MakeTrack(disc: 1, track: 2, song: "Another Mystery Song"),
            MakeTrack(disc: 1, track: 3, song: "Yet Another"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Foo", Canonical = "Foo", Position = 0, Segue = false },
            new() { Name = "Bar", Canonical = "Bar", Position = 1, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(0, result.MatchedCount);
        Assert.Empty(result.ClaimedPositions);

        // All tracks fully untouched.
        Assert.All(tracks, t =>
        {
            Assert.False(t.IsModified);
            Assert.Null(t.IsMatched);
        });
        Assert.Equal("Some Song That Doesnt Match", tracks[0].SongName);
        Assert.Equal(1, tracks[0].TrackNumber);
    }

    [Fact]
    public void RepeatedSongInSetlist_GreedyFirstUnclaimed()
    {
        // Setlist: A, B, A. Audio: A (#1), B (#2), A (#3).
        var tracks = new List<TrackInfo>
        {
            MakeTrack(disc: 1, track: 1, song: "Playing in the Band"),
            MakeTrack(disc: 1, track: 2, song: "Drums"),
            MakeTrack(disc: 1, track: 3, song: "Playing in the Band"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Playing in the Band", Canonical = "Playing in the Band", Position = 0, Segue = true },
            new() { Name = "Drums",               Canonical = "Drums",               Position = 1, Segue = true },
            new() { Name = "Playing in the Band", Canonical = "Playing in the Band", Position = 2, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(3, result.MatchedCount);
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, result.ClaimedPositions);

        // First Playing claims position 0 (segue=true), second Playing claims position 2 (segue=false).
        Assert.True(tracks[0].Segue);
        Assert.False(tracks[2].Segue);
    }

    [Fact]
    public void RecordingMissingSongs_UnclaimedPositionsRemain()
    {
        // Setlist has 5 songs. Audio has 3 tracks matching positions 0, 2, 4.
        var tracks = new List<TrackInfo>
        {
            MakeTrack(disc: 1, track: 1, song: "Song A"),
            MakeTrack(disc: 1, track: 2, song: "Song C"),
            MakeTrack(disc: 1, track: 3, song: "Song E"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Song A", Canonical = "Song A", Position = 0, Segue = false },
            new() { Name = "Song B", Canonical = "Song B", Position = 1, Segue = false },
            new() { Name = "Song C", Canonical = "Song C", Position = 2, Segue = false },
            new() { Name = "Song D", Canonical = "Song D", Position = 3, Segue = false },
            new() { Name = "Song E", Canonical = "Song E", Position = 4, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(3, result.MatchedCount);
        Assert.Equal(new HashSet<int> { 0, 2, 4 }, result.ClaimedPositions);

        // Positions 1 and 3 (Song B, Song D) remain unclaimed — Match-to-Song
        // dialog will surface these to the user.
        Assert.DoesNotContain(1, result.ClaimedPositions);
        Assert.DoesNotContain(3, result.ClaimedPositions);
    }

    [Fact]
    public void ExtraAudioTracks_LeftUntouched()
    {
        // Setlist has 3 songs; audio has 5 tracks (3 matching + 2 tuning).
        var tracks = new List<TrackInfo>
        {
            MakeTrack(disc: 1, track: 1, song: "Tuning"),
            MakeTrack(disc: 1, track: 2, song: "Truckin'"),
            MakeTrack(disc: 1, track: 3, song: "Crowd Noise"),
            MakeTrack(disc: 1, track: 4, song: "Casey Jones"),
            MakeTrack(disc: 1, track: 5, song: "U.S. Blues"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Truckin'",    Canonical = "Truckin'",    Position = 0, Segue = false },
            new() { Name = "Casey Jones", Canonical = "Casey Jones", Position = 1, Segue = false },
            new() { Name = "U.S. Blues",  Canonical = "U.S. Blues",  Position = 2, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(3, result.MatchedCount);

        // The 2 tuning/crowd tracks keep their original numbering, names, segue.
        Assert.Equal(1, tracks[0].DiscNumber);
        Assert.Equal(1, tracks[0].TrackNumber);
        Assert.Equal("Tuning", tracks[0].SongName);
        Assert.False(tracks[0].IsModified);

        Assert.Equal(1, tracks[2].DiscNumber);
        Assert.Equal(3, tracks[2].TrackNumber);
        Assert.Equal("Crowd Noise", tracks[2].SongName);
        Assert.False(tracks[2].IsModified);

        // No track was renumbered to a non-existent overflow disc.
        Assert.All(tracks, t => Assert.Equal(1, t.DiscNumber));
    }

    [Fact]
    public void ResolverCanonicalizesAlias_MatchSucceeds()
    {
        // Audio carries the alias "Watchtower"; setlist canonical is "All Along The Watchtower".
        // The resolver bridges the gap.
        var tracks = new List<TrackInfo>
        {
            MakeTrack(disc: 1, track: 1, song: "Watchtower"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "All Along The Watchtower", Canonical = "All Along The Watchtower", Position = 0, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist,
            input => input == "Watchtower" ? "All Along The Watchtower" : input);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal("All Along The Watchtower", tracks[0].SongName);
        Assert.True(tracks[0].IsMatched);
        // Numbering preserved even with rename.
        Assert.Equal(1, tracks[0].DiscNumber);
        Assert.Equal(1, tracks[0].TrackNumber);
    }

    [Fact]
    public void EmptyTracksAndSetlist_NoExceptions()
    {
        var result1 = SetlistMatcher.MatchAndDecorate(
            new List<TrackInfo>(),
            new List<SetlistMatcher.SetlistEntry>(),
            IdentityResolver);
        Assert.Equal(0, result1.MatchedCount);

        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "X") };
        var result2 = SetlistMatcher.MatchAndDecorate(
            tracks,
            new List<SetlistMatcher.SetlistEntry>(),
            IdentityResolver);
        Assert.Equal(0, result2.MatchedCount);
        Assert.Equal(1, tracks[0].TrackNumber); // untouched
    }

    // ===== Helpers =====

    private static string? IdentityResolver(string s) => s;

    private static TrackInfo MakeTrack(int disc, int track, string song)
    {
        return new TrackInfo
        {
            FilePath = $"/fake/d{disc}_t{track}.flac",
            FileName = $"d{disc}_t{track}.flac",
            DiscNumber = disc,
            TrackNumber = track,
            SongName = song,
            IsModified = false,
        };
    }
}
