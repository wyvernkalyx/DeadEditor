using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for extras slice 2 (setlist-extras-writeback-spec.md §4): D2 — auto-match NEVER claims a
/// non-song entry (excluded from both the direct pass and the combine pass; never a ClaimsByTrack
/// source) — and D4 — "complete match" = every song-typed entry claimed, extras optional. Pure: the
/// matcher and <see cref="SetlistCompleteness"/> only; no WPF, no editor, no gate. The all-song path
/// (the entire current corpus) must behave byte-identically — proven by the default-type regression.
/// </summary>
public class ExtrasMatchingCompletenessTests
{
    // ===== D2: auto-match extra-exclusion =====

    [Fact]
    public void Extra_NotProposed_EvenWhenTrackTitleMatches()
    {
        // A "Tuning" track must not auto-claim a `tuning` entry (§4: several like-named extras could
        // otherwise bind the wrong one).
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Tuning") };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            Entry("Tuning", 0, SetlistEntryType.Tuning),
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, Echo, Echo);

        Assert.Equal(0, result.MatchedCount);
        Assert.Empty(result.ClaimedPositions);
        Assert.Null(tracks[0].IsMatched);   // left untouched
        Assert.False(tracks[0].IsModified);
    }

    [Fact]
    public void SongsClaimed_ExtraSkipped_AmidSongs()
    {
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "Bertha"),
            MakeTrack(1, 2, "Tuning"),
            MakeTrack(1, 3, "Deal"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            Entry("Bertha", 0),                              // song
            Entry("Tuning", 1, SetlistEntryType.Tuning),     // extra between the songs
            Entry("Deal",   2),                              // song
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, Echo, Echo);

        // Only the two songs are claimed; the extra position (1) is never claimed.
        Assert.Equal(new HashSet<int> { 0, 2 }, result.ClaimedPositions);
        Assert.DoesNotContain(1, result.ClaimedPositions);
        Assert.Null(tracks[1].IsMatched);   // the Tuning track is left unmatched
    }

    [Fact]
    public void Extra_AbsentFromClaimsByTrack()
    {
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "Bertha"),
            MakeTrack(1, 2, "Tuning"),
            MakeTrack(1, 3, "Deal"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            Entry("Bertha", 0),
            Entry("Tuning", 1, SetlistEntryType.Tuning),
            Entry("Deal",   2),
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, Echo, Echo);

        // The extra position is never a claim source, and the Tuning track never appears as a claimant.
        Assert.DoesNotContain(result.ClaimsByTrack, c => c.Position == 1);
        Assert.DoesNotContain(result.ClaimsByTrack, c => ReferenceEquals(c.Track, tracks[1]));
        Assert.Equal(new[] { 0, 2 }, result.ClaimsByTrack.Select(c => c.Position).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void TwoLikeNamedExtras_NeitherAutoClaimed()
    {
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Tuning"), MakeTrack(1, 2, "Tuning") };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            Entry("Tuning", 0, SetlistEntryType.Tuning),
            Entry("Tuning", 1, SetlistEntryType.Tuning),
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, Echo, Echo);

        Assert.Equal(0, result.MatchedCount);
        Assert.Empty(result.ClaimedPositions);
    }

    [Fact]
    public void Combine_ClaimsTwoSongs_AdjacentExtraUntouched()
    {
        // A genuine 2-song combine still fires; an extra elsewhere is not pulled in.
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Alpha/Beta") };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            Entry("Alpha", 0),
            Entry("Beta",  1),
            Entry("Tuning", 2, SetlistEntryType.Tuning),
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, Echo, KnownOrNull("Alpha", "Beta"));

        Assert.Equal(new HashSet<int> { 0, 1 }, result.ClaimedPositions);
        Assert.DoesNotContain(2, result.ClaimedPositions);
        Assert.Equal("Alpha > Beta", tracks[0].SongName);
    }

    [Fact]
    public void Combine_DoesNotClaimExtra_ThatWouldCompleteTheWindow()
    {
        // The load-bearing exclusion: an extra whose canonical would complete a combine window must be
        // skipped exactly as a claimed position — so the window [Alpha(0), Beta-extra(1)] cannot match
        // "Alpha/Beta" and pull the extra in. Result: the combine finds no clean window and the track
        // is left unmatched, rather than auto-claiming the extra at position 1.
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Alpha/Beta") };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            Entry("Alpha", 0),
            Entry("Beta",  1, SetlistEntryType.Banter),   // extra that title-matches the 2nd component
            Entry("Beta",  2),                            // the real Beta song, non-adjacent to Alpha
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, Echo, KnownOrNull("Alpha", "Beta"));

        Assert.Empty(result.ClaimedPositions);
        Assert.DoesNotContain(1, result.ClaimedPositions);
        Assert.Null(tracks[0].IsMatched);
    }

    [Fact]
    public void AllSongSetlist_MatchesIdentically_DefaultType()
    {
        // Regression: entries built WITHOUT a type (default "song") match exactly as before typed
        // entries existed — the entire current corpus shape.
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "China Cat Sunflower"),
            MakeTrack(1, 2, "I Know You Rider"),
            MakeTrack(1, 3, "Sugar Magnolia"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "China Cat Sunflower", Canonical = "China Cat Sunflower", Position = 0, Segue = true },
            new() { Name = "I Know You Rider",    Canonical = "I Know You Rider",    Position = 1 },
            new() { Name = "Sugar Magnolia",      Canonical = "Sugar Magnolia",      Position = 2 },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, Echo, Echo);

        Assert.Equal(3, result.MatchedCount);
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, result.ClaimedPositions);
        Assert.Equal(new[] { 0, 1, 2 }, result.ClaimsByTrack.Select(c => c.Position).OrderBy(p => p).ToArray());
    }

    // ===== D4: completeness over the song-typed subset =====

    private static List<SetlistEntryVm> Projection(params (string name, string type)[] entries)
    {
        var list = new List<SetlistEntryVm>();
        for (int i = 0; i < entries.Length; i++)
            list.Add(new SetlistEntryVm(entries[i].name, entries[i].name, i, false, $"Set 1, #{i + 1}", entries[i].type));
        return list;
    }

    [Fact]
    public void Complete_WhenAllSongsClaimed_ExtraUnclaimedIgnored()
    {
        var setlist = Projection(("Bertha", SetlistEntryType.Song), ("Deal", SetlistEntryType.Song),
            ("Tuning", SetlistEntryType.Tuning));
        var claimed = new HashSet<int> { 0, 1 };   // both songs; extra (2) unclaimed

        var r = SetlistCompleteness.Evaluate(setlist, claimed);

        Assert.True(r.IsComplete);
        Assert.Equal(2, r.SongTotal);
        Assert.Equal(2, r.SongClaimed);
        Assert.Equal(1, r.ExtraTotal);
        Assert.Equal(0, r.ExtraClaimed);
        Assert.Equal(0, r.SongUnclaimed);
    }

    [Fact]
    public void Incomplete_WhenOneSongUnclaimed()
    {
        var setlist = Projection(("Bertha", SetlistEntryType.Song), ("Deal", SetlistEntryType.Song),
            ("Tuning", SetlistEntryType.Tuning));
        var claimed = new HashSet<int> { 0 };   // one song missing

        var r = SetlistCompleteness.Evaluate(setlist, claimed);

        Assert.False(r.IsComplete);
        Assert.Equal(1, r.SongClaimed);
        Assert.Equal(1, r.SongUnclaimed);
    }

    [Fact]
    public void Complete_WithUnclaimedExtrasOnly()
    {
        // A soundboard that cut the tuning is still complete (§5).
        var setlist = Projection(("Bertha", SetlistEntryType.Song), ("Tuning", SetlistEntryType.Tuning));
        var claimed = new HashSet<int> { 0 };

        Assert.True(SetlistCompleteness.IsComplete(setlist, claimed));
    }

    [Fact]
    public void ClaimedExtra_Counted_NotRequired()
    {
        var setlist = Projection(("Bertha", SetlistEntryType.Song), ("Ripple", SetlistEntryType.FalseStart));
        var claimed = new HashSet<int> { 0, 1 };   // extra also placed

        var r = SetlistCompleteness.Evaluate(setlist, claimed);

        Assert.True(r.IsComplete);
        Assert.Equal(1, r.ExtraTotal);
        Assert.Equal(1, r.ExtraClaimed);
    }

    [Fact]
    public void Complete_Vacuous_WhenNoSongs()
    {
        var setlist = Projection(("Tuning", SetlistEntryType.Tuning));
        var r = SetlistCompleteness.Evaluate(setlist, new HashSet<int>());

        Assert.True(r.IsComplete);   // vacuous: no song-typed entries to place
        Assert.Equal(0, r.SongTotal);
        Assert.Equal(1, r.ExtraTotal);
    }

    [Fact]
    public void Evaluate_NullSetlist_IsEmptyAndComplete()
    {
        var r = SetlistCompleteness.Evaluate(null, null);

        Assert.True(r.IsComplete);
        Assert.Equal(0, r.SongTotal);
        Assert.Equal(0, r.ExtraTotal);
    }

    // ===== helpers =====

    private static string? Echo(string s) => s;

    // A resolveOfficialOrNull that returns the name for known songs and null for anything else
    // (GetOfficialTitle semantics the combine pass requires).
    private static System.Func<string, string?> KnownOrNull(params string[] known)
    {
        var set = new HashSet<string>(known);
        return s => set.Contains(s) ? s : null;
    }

    private static SetlistMatcher.SetlistEntry Entry(string name, int pos, string type = SetlistEntryType.Song) =>
        new() { Name = name, Canonical = name, Position = pos, Type = type };

    private static TrackInfo MakeTrack(int disc, int track, string song) => new()
    {
        FilePath = $"/fake/d{disc}_t{track}.flac",
        FileName = $"d{disc}_t{track}.flac",
        DiscNumber = disc,
        TrackNumber = track,
        SongName = song,
        IsModified = false,
    };
}
