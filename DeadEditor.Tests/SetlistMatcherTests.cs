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

    // ===== 2026-06-26 divergence-findings characterization tests =====
    // These pin the "degrades to unmatched, never mis-names" guarantee that the
    // Edit Metadata path previously exercised through its (now-deleted) inline
    // claim loop. Edit and Import both delegate to MatchAndDecorate, so they
    // cover both call sites.

    [Fact]
    public void BanterTrack_BetweenSongs_LeftUnmatchedAndUntouched()
    {
        // Taper kept stage banter as its own track; it matches no setlist entry.
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "Jack Straw"),
            MakeTrack(1, 2, "Crowd Banter"),
            MakeTrack(1, 3, "Deal"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Jack Straw", Canonical = "Jack Straw", Position = 0, Segue = false },
            new() { Name = "Deal",       Canonical = "Deal",       Position = 1, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(2, result.MatchedCount);
        Assert.Equal("Crowd Banter", tracks[1].SongName);
        Assert.Null(tracks[1].IsMatched);
        Assert.False(tracks[1].IsModified);
        Assert.False(tracks[1].Segue);
    }

    [Fact]
    public void CombinedSegueTrack_OneUnmatchableKey_LeftIntact_NeverRenamed()
    {
        // Media combines a whole segue run into one track. Its canonical key is the
        // entire string, which equals no single setlist entry -> unmatched, untouched.
        // Critically: it is NOT silently renamed to just "Dark Star".
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "Dark Star > St. Stephen > The Eleven"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Dark Star",   Canonical = "Dark Star",   Position = 0, Segue = true },
            new() { Name = "St. Stephen", Canonical = "St. Stephen", Position = 1, Segue = true },
            new() { Name = "The Eleven",  Canonical = "The Eleven",  Position = 2, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(0, result.MatchedCount);
        Assert.Empty(result.ClaimedPositions);
        Assert.Equal("Dark Star > St. Stephen > The Eleven", tracks[0].SongName);
        Assert.Null(tracks[0].IsMatched);
        Assert.False(tracks[0].IsModified);
    }

    [Fact]
    public void OutOfOrderReprise_PartialCoverage_DegradesToUnmatched_NotMisnamed()
    {
        // Setlist plays "The Other One" twice (positions 0 and 3). The media carries
        // the song once plus an unlabeled jam that diverges from the setlist. The real
        // match greedily claims the first unclaimed occurrence; nothing is mis-named.
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "Bertha"),
            MakeTrack(1, 2, "The Other One"),
            MakeTrack(1, 3, "Unlabeled Jam"),
        };
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "The Other One", Canonical = "The Other One", Position = 0, Segue = true },
            new() { Name = "Bertha",        Canonical = "Bertha",        Position = 1, Segue = false },
            new() { Name = "Wharf Rat",     Canonical = "Wharf Rat",     Position = 2, Segue = false },
            new() { Name = "The Other One", Canonical = "The Other One", Position = 3, Segue = false },
        };

        var result = SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.Equal(2, result.MatchedCount); // Bertha + one The Other One
        Assert.Equal("Unlabeled Jam", tracks[2].SongName);
        Assert.Null(tracks[2].IsMatched);
        Assert.False(tracks[2].IsModified);
        Assert.Contains(0, result.ClaimedPositions); // reprise claimed first unclaimed occurrence
        Assert.Equal("The Other One", tracks[1].SongName);
    }

    [Fact]
    public void EditAndImport_SameAlbumAndResolver_ProduceIdenticalDecoration()
    {
        // Convergence guarantee: EditMetadataView and ImportView both delegate to
        // SetlistMatcher.MatchAndDecorate with an equivalent Normalize->GetOfficialTitle
        // resolver. Given the same album and resolver, the decoration is identical by
        // construction. This pins the shared matcher's determinism so the two call sites
        // cannot silently diverge (the maintenance hazard this convergence removed).
        Func<string, string?> resolver = name => Aliases.TryGetValue(name, out var c) ? c : name;

        var importAlbum = SampleAlbum();
        var editAlbum = SampleAlbum();
        var setlist = SampleSetlist();

        var importResult = SetlistMatcher.MatchAndDecorate(importAlbum, setlist, resolver);
        var editResult = SetlistMatcher.MatchAndDecorate(editAlbum, setlist, resolver);

        Assert.Equal(importResult.MatchedCount, editResult.MatchedCount);
        Assert.Equal(importResult.SegueCount, editResult.SegueCount);
        Assert.Equal(importResult.ClaimedPositions, editResult.ClaimedPositions);

        for (int i = 0; i < importAlbum.Count; i++)
        {
            Assert.Equal(importAlbum[i].SongName, editAlbum[i].SongName);
            Assert.Equal(importAlbum[i].Segue, editAlbum[i].Segue);
            Assert.Equal(importAlbum[i].IsMatched, editAlbum[i].IsMatched);
            Assert.Equal(importAlbum[i].IsModified, editAlbum[i].IsModified);
            Assert.Equal(importAlbum[i].DiscNumber, editAlbum[i].DiscNumber);
            Assert.Equal(importAlbum[i].TrackNumber, editAlbum[i].TrackNumber);
        }

        // Sanity: the alias track resolved and the unmatchable track was left alone.
        Assert.Equal("All Along The Watchtower", editAlbum[3].SongName);
        Assert.Equal("Tuning", editAlbum[2].SongName);
        Assert.Null(editAlbum[2].IsMatched);
    }

    // ===== B1 seam: ComputeProposals / Apply =====
    // The split must be a pure internal seam: ComputeProposals writes nothing
    // and captures correct old-to-new pairs; Apply(ComputeProposals(x)) is
    // bit-for-bit equivalent to MatchAndDecorate(x).

    [Fact]
    public void ComputeProposals_WritesNothing_TracksUnmutated()
    {
        var tracks = SampleAlbum();
        var setlist = SampleSetlist();
        Func<string, string?> resolver = name => Aliases.TryGetValue(name, out var c) ? c : name;

        // Snapshot the mutable fields ComputeProposals must NOT touch.
        var beforeNames = tracks.Select(t => t.SongName).ToList();
        var beforeSegues = tracks.Select(t => t.Segue).ToList();
        var beforeMatched = tracks.Select(t => t.IsMatched).ToList();
        var beforeModified = tracks.Select(t => t.IsModified).ToList();

        var set = SetlistMatcher.ComputeProposals(tracks, setlist, resolver);

        Assert.NotEmpty(set.Proposals); // it did find matches...
        for (int i = 0; i < tracks.Count; i++)
        {
            Assert.Equal(beforeNames[i], tracks[i].SongName);   // ...but wrote nothing
            Assert.Equal(beforeSegues[i], tracks[i].Segue);
            Assert.Equal(beforeMatched[i], tracks[i].IsMatched);
            Assert.Equal(beforeModified[i], tracks[i].IsModified);
        }
    }

    [Fact]
    public void ComputeProposals_CapturesOldToNew_NameVariantAndBothSegueDirections()
    {
        // Track 1: name variant ("Watchtower" -> canonical) with a segue ADD (false -> true).
        // Track 2: name already canonical with a segue REMOVE (true -> false).
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "Watchtower"),
            MakeTrack(1, 2, "Sugar Magnolia"),
        };
        tracks[1].Segue = true; // pre-existing segue the setlist would clear
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "All Along The Watchtower", Canonical = "All Along The Watchtower", Position = 0, Segue = true },
            new() { Name = "Sugar Magnolia",           Canonical = "Sugar Magnolia",           Position = 1, Segue = false },
        };
        Func<string, string?> resolver = name => name == "Watchtower" ? "All Along The Watchtower" : name;

        var set = SetlistMatcher.ComputeProposals(tracks, setlist, resolver);

        Assert.Equal(2, set.Proposals.Count);

        var add = set.Proposals[0];
        Assert.Same(tracks[0], add.Track);
        Assert.Equal("Watchtower", add.OldSongName);
        Assert.Equal("All Along The Watchtower", add.NewSongName);
        Assert.False(add.OldSegue);
        Assert.True(add.NewSegue);                       // segue add captured
        Assert.Equal(new List<int> { 0 }, add.CoveredEntryIndices);

        var remove = set.Proposals[1];
        Assert.Same(tracks[1], remove.Track);
        Assert.Equal("Sugar Magnolia", remove.OldSongName);
        Assert.Equal("Sugar Magnolia", remove.NewSongName);
        Assert.True(remove.OldSegue);
        Assert.False(remove.NewSegue);                   // segue remove captured (the 1977-05-11 hazard)
        Assert.Equal(new List<int> { 1 }, remove.CoveredEntryIndices);
    }

    [Fact]
    public void ComputeProposals_CoveredEntryIndices_LengthOnePerSingleSongMatch()
    {
        var tracks = SampleAlbum();
        var setlist = SampleSetlist();
        Func<string, string?> resolver = name => Aliases.TryGetValue(name, out var c) ? c : name;

        var set = SetlistMatcher.ComputeProposals(tracks, setlist, resolver);

        Assert.All(set.Proposals, p => Assert.Single(p.CoveredEntryIndices));
        // The claimed set is the union of the per-proposal indices.
        Assert.Equal(
            set.Proposals.SelectMany(p => p.CoveredEntryIndices).ToHashSet(),
            set.ClaimedPositions);
    }

    [Fact]
    public void ApplyOfComputeProposals_EqualsMatchAndDecorate_SameFinalStateAndResult()
    {
        var setlist = SampleSetlist();
        Func<string, string?> resolver = name => Aliases.TryGetValue(name, out var c) ? c : name;

        var direct = SampleAlbum();
        var seam = SampleAlbum();

        var directResult = SetlistMatcher.MatchAndDecorate(direct, setlist, resolver);
        var seamResult = SetlistMatcher.Apply(SetlistMatcher.ComputeProposals(seam, setlist, resolver));

        // Identical MatchResult.
        Assert.Equal(directResult.MatchedCount, seamResult.MatchedCount);
        Assert.Equal(directResult.SegueCount, seamResult.SegueCount);
        Assert.Equal(1, directResult.SegueCount); // pin the absolute value so a fixture edit can't revert this to a vacuous 0 == 0
        Assert.Equal(directResult.ClaimedPositions, seamResult.ClaimedPositions);

        // Identical final track state, field by field.
        for (int i = 0; i < direct.Count; i++)
        {
            Assert.Equal(direct[i].SongName, seam[i].SongName);
            Assert.Equal(direct[i].Segue, seam[i].Segue);
            Assert.Equal(direct[i].IsMatched, seam[i].IsMatched);
            Assert.Equal(direct[i].IsModified, seam[i].IsModified);
            Assert.Equal(direct[i].DiscNumber, seam[i].DiscNumber);
            Assert.Equal(direct[i].TrackNumber, seam[i].TrackNumber);
        }
    }

    // ===== B2a: IsModified only on a real change (spec Sec. 6) =====
    // A match that re-asserts the album's existing values must not flag
    // modified (the "already-correct album shows unsaved changes" bug).

    [Fact]
    public void MatchedNoOp_NameAndSegueAlreadyEqual_IsMatchedButNotModified()
    {
        // Track already equals the setlist exactly: same canonical name, same segue.
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Sugar Magnolia") }; // Segue defaults false
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Sugar Magnolia", Canonical = "Sugar Magnolia", Position = 0, Segue = false },
        };

        SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.True(tracks[0].IsMatched);    // a setlist entry was claimed
        Assert.False(tracks[0].IsModified);  // ...but nothing actually changed
    }

    [Fact]
    public void MatchedNameOnlyChange_IsModified()
    {
        // Name canonicalizes (Watchtower -> All Along The Watchtower); segue unchanged.
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Watchtower") }; // Segue false
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "All Along The Watchtower", Canonical = "All Along The Watchtower", Position = 0, Segue = false },
        };

        SetlistMatcher.MatchAndDecorate(tracks, setlist,
            name => name == "Watchtower" ? "All Along The Watchtower" : name);

        Assert.True(tracks[0].IsMatched);
        Assert.True(tracks[0].IsModified);
        Assert.Equal("All Along The Watchtower", tracks[0].SongName);
    }

    [Fact]
    public void MatchedSegueOnlyChange_IsModified()
    {
        // Name already canonical; only the segue flips (false -> true).
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Sugar Magnolia") }; // Segue false
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Sugar Magnolia", Canonical = "Sugar Magnolia", Position = 0, Segue = true },
        };

        SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.True(tracks[0].IsMatched);
        Assert.True(tracks[0].IsModified);
        Assert.True(tracks[0].Segue);
    }

    [Fact]
    public void PreexistingModified_NoOpMatch_StaysModified_NotCleared()
    {
        // A track the user already edited (IsModified true) matches as a no-op.
        // Sec. 6: matching never clears IsModified.
        var tracks = new List<TrackInfo> { MakeTrack(1, 1, "Sugar Magnolia") };
        tracks[0].IsModified = true; // a pre-existing edit
        var setlist = new List<SetlistMatcher.SetlistEntry>
        {
            new() { Name = "Sugar Magnolia", Canonical = "Sugar Magnolia", Position = 0, Segue = false },
        };

        SetlistMatcher.MatchAndDecorate(tracks, setlist, IdentityResolver);

        Assert.True(tracks[0].IsMatched);
        Assert.True(tracks[0].IsModified); // left as-is, not cleared by the no-op match
    }

    // ===== Helpers =====

    private static string? IdentityResolver(string s) => s;

    private static readonly Dictionary<string, string> Aliases = new()
    {
        { "Watchtower", "All Along The Watchtower" },
    };

    private static List<TrackInfo> SampleAlbum() => new()
    {
        MakeTrack(1, 1, "China Cat Sunflower"),
        MakeTrack(1, 2, "I Know You Rider"),
        MakeTrack(1, 3, "Tuning"),       // unmatchable
        MakeTrack(1, 4, "Watchtower"),   // alias -> resolved to canonical
    };

    private static List<SetlistMatcher.SetlistEntry> SampleSetlist() => new()
    {
        new() { Name = "China Cat Sunflower",      Canonical = "China Cat Sunflower",      Position = 0, Segue = true },
        new() { Name = "I Know You Rider",         Canonical = "I Know You Rider",         Position = 1, Segue = false },
        new() { Name = "All Along The Watchtower", Canonical = "All Along The Watchtower", Position = 2, Segue = false },
    };

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
