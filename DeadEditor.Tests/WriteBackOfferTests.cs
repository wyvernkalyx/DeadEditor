using System;
using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for the slice-7a write-back offer population (setlist-extras-writeback-spec.md §6 P2):
/// <see cref="WriteBackOffer.ComputeOffer"/> and the <see cref="StagedSetlistExtra"/> DTO that feeds
/// the setlist editor on accept. Pure — no WPF, no view, no gate. The load-bearing rule under test is
/// that the offer is NOT a bare unmatched count: a track whose canonical title is present in ANY
/// setlist entry (song OR extra) is excluded, so an extras-covered track (unmatched by D2) can never
/// re-fire the offer forever.
/// </summary>
public class WriteBackOfferTests
{
    // ===== ComputeOffer: population semantics =====

    [Fact]
    public void TitleAbsentFromSetlist_TrackIncluded()
    {
        // Truckin' is in no entry of any type -> genuinely off-list -> offered.
        var tracks = new List<TrackInfo> { Unmatched(MakeTrack(1, 1, "Truckin'")) };
        var entries = new List<SetlistEntryVm> { Vm("Bertha", 0), Vm("Deal", 1) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Single(offer);
        Assert.Equal("Truckin'", offer[0].CanonicalName);
        Assert.Same(tracks[0], offer[0].Track);
    }

    [Fact]
    public void TitleCoveredByExtraEntry_TrackExcluded()
    {
        // The offer's whole point: a "Tuning" track over a `tuning` entry is unmatched by D2, but its
        // title IS in the setlist, so it must NOT count (else the offer re-fires forever).
        var tracks = new List<TrackInfo> { Unmatched(MakeTrack(1, 1, "Tuning")) };
        var entries = new List<SetlistEntryVm>
        {
            Vm("Bertha", 0),
            Vm("Tuning", 1, SetlistEntryType.Tuning),
        };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Empty(offer);
    }

    [Fact]
    public void RecognizedButUnclaimedSongTitle_TrackExcluded()
    {
        // A false-start "Ripple" track sits unmatched (its one Ripple slot is claimed by the real take),
        // but "Ripple" is a setlist title -> recognized -> excluded. Only genuinely absent titles offer.
        var tracks = new List<TrackInfo> { Unmatched(MakeTrack(1, 1, "Ripple")) };
        var entries = new List<SetlistEntryVm> { Vm("Ripple", 0), Vm("Deal", 1) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Empty(offer);
    }

    [Fact]
    public void MatchedTrack_ExcludedEvenIfTitleAbsent()
    {
        // Post-Match matched tracks already have a home — never offered, regardless of title presence.
        var matched = MakeTrack(1, 1, "Truckin'");
        matched.IsMatched = true;
        var tracks = new List<TrackInfo> { matched };
        var entries = new List<SetlistEntryVm> { Vm("Bertha", 0) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Empty(offer);
    }

    [Fact]
    public void AllTitlesPresent_EmptyPopulation()
    {
        // Two unmatched tracks whose titles both exist in the setlist -> nothing to offer.
        var tracks = new List<TrackInfo>
        {
            Unmatched(MakeTrack(1, 1, "Tuning")),
            Unmatched(MakeTrack(1, 2, "Ripple")),
        };
        var entries = new List<SetlistEntryVm>
        {
            Vm("Ripple", 0),
            Vm("Tuning", 1, SetlistEntryType.Tuning),
        };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Empty(offer);
    }

    [Fact]
    public void CanonicalComparison_IsOrdinalIgnoreCase()
    {
        // "tuning" (lowercase track title) matches the "Tuning" entry under the matcher's
        // OrdinalIgnoreCase equality -> recognized -> excluded.
        var tracks = new List<TrackInfo> { Unmatched(MakeTrack(1, 1, "tuning")) };
        var entries = new List<SetlistEntryVm> { Vm("Tuning", 0, SetlistEntryType.Tuning) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Empty(offer);
    }

    [Fact]
    public void PerTrack_NoDedupe_TwoOffListTracksSameTitle_YieldTwoCandidates()
    {
        // Two separate tuning passages, neither in the setlist -> two candidates (two tuning events).
        var tracks = new List<TrackInfo>
        {
            Unmatched(MakeTrack(1, 1, "Tuning")),
            Unmatched(MakeTrack(1, 5, "Tuning")),
        };
        var entries = new List<SetlistEntryVm> { Vm("Bertha", 0), Vm("Deal", 1) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Equal(2, offer.Count);
        Assert.All(offer, c => Assert.Equal("Tuning", c.CanonicalName));
        Assert.Same(tracks[0], offer[0].Track);
        Assert.Same(tracks[1], offer[1].Track);
    }

    [Fact]
    public void BlankTrackName_Skipped()
    {
        var tracks = new List<TrackInfo> { Unmatched(MakeTrack(1, 1, "   ")) };
        var entries = new List<SetlistEntryVm> { Vm("Bertha", 0) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Empty(offer);
    }

    [Fact]
    public void NullResolvedCanonical_Skipped()
    {
        // A resolver that returns null (unknown name) contributes no candidate.
        var tracks = new List<TrackInfo> { Unmatched(MakeTrack(1, 1, "Truckin'")) };
        var entries = new List<SetlistEntryVm> { Vm("Bertha", 0) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, _ => null);

        Assert.Empty(offer);
    }

    [Fact]
    public void CandidateOrder_FollowsTrackOrder()
    {
        var tracks = new List<TrackInfo>
        {
            Unmatched(MakeTrack(1, 1, "Truckin'")),
            Unmatched(MakeTrack(1, 2, "Bertha")),   // present -> excluded
            Unmatched(MakeTrack(1, 3, "Jack Straw")),
        };
        var entries = new List<SetlistEntryVm> { Vm("Bertha", 0) };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Equal(new[] { "Truckin'", "Jack Straw" }, offer.Select(c => c.CanonicalName).ToArray());
    }

    // ===== Consistency with the real matcher =====

    [Fact]
    public void ConsistentWithMatcher_OnlyGenuinelyOffListOffered()
    {
        // Drive the real matcher first (it stamps IsMatched), then compute the offer over the SAME
        // tracks + the projection-equivalent entries with the SAME resolver. Bertha/Deal match; the
        // Tuning track is unmatched by D2 but covered by the tuning entry; Truckin' is genuinely off-list.
        var tracks = new List<TrackInfo>
        {
            MakeTrack(1, 1, "Bertha"),
            MakeTrack(1, 2, "Deal"),
            MakeTrack(1, 3, "Tuning"),
            MakeTrack(1, 4, "Truckin'"),
        };
        var matcherSetlist = new List<SetlistMatcher.SetlistEntry>
        {
            MEntry("Bertha", 0),
            MEntry("Tuning", 1, SetlistEntryType.Tuning),
            MEntry("Deal", 2),
        };
        SetlistMatcher.MatchAndDecorate(tracks, matcherSetlist, Echo, Echo);

        var entries = new List<SetlistEntryVm>
        {
            Vm("Bertha", 0),
            Vm("Tuning", 1, SetlistEntryType.Tuning),
            Vm("Deal", 2),
        };

        var offer = WriteBackOffer.ComputeOffer(tracks, entries, Echo);

        Assert.Single(offer);
        Assert.Equal("Truckin'", offer[0].CanonicalName);
        Assert.Same(tracks[3], offer[0].Track);
    }

    // ===== StagedSetlistExtra + D10a interaction =====

    [Fact]
    public void StagedExtra_Construction_LabelAndTypeDefaults()
    {
        var a = new StagedSetlistExtra("Truckin'", SetlistEntryType.OtherExtra);
        Assert.Equal("Truckin'", a.Label);
        Assert.Equal(SetlistEntryType.OtherExtra, a.Type);

        // Blank/whitespace type falls back to other-extra (the offer's default kind).
        var blankType = new StagedSetlistExtra("X", "   ");
        Assert.Equal(SetlistEntryType.OtherExtra, blankType.Type);

        // Null label normalizes to empty (so D10a, not an NRE, catches it downstream).
        var nullLabel = new StagedSetlistExtra(null!, SetlistEntryType.Tuning);
        Assert.Equal("", nullLabel.Label);
        Assert.Equal(SetlistEntryType.Tuning, nullLabel.Type);
    }

    [Fact]
    public void StagedExtra_NamedLabel_PassesD10a()
    {
        // A staged extra with a real label becomes a named editor row -> D10a lets the save through.
        var staged = new StagedSetlistExtra("Truckin'", SetlistEntryType.OtherExtra);
        var rows = new List<ConcertTrackInput>
        {
            new("Bertha", "1971-02-21", false, "Set 1", "", SetlistEntryType.Song),
            new(staged.Label, "1971-02-21", false, "Set 1", "", staged.Type),
        };

        Assert.Null(RowLabelRule.FirstUnnamedRow(rows));
    }

    [Fact]
    public void StagedExtra_BlankedLabel_BlockedByD10a()
    {
        // If the user clears a staged row's label in the editor, D10a blocks the save at that row —
        // the staged row is subject to the same save-time name gate as any other row.
        var staged = new StagedSetlistExtra("", SetlistEntryType.OtherExtra);
        var rows = new List<ConcertTrackInput>
        {
            new("Bertha", "1971-02-21", false, "Set 1", "", SetlistEntryType.Song),
            new(staged.Label, "1971-02-21", false, "Set 1", "", staged.Type),
        };

        Assert.Equal(2, RowLabelRule.FirstUnnamedRow(rows));
    }

    // ===== argument guards =====

    [Fact]
    public void NullArguments_Throw()
    {
        var tracks = new List<TrackInfo>();
        var entries = new List<SetlistEntryVm>();
        Assert.Throws<ArgumentNullException>(() => WriteBackOffer.ComputeOffer(null!, entries, Echo));
        Assert.Throws<ArgumentNullException>(() => WriteBackOffer.ComputeOffer(tracks, null!, Echo));
        Assert.Throws<ArgumentNullException>(() => WriteBackOffer.ComputeOffer(tracks, entries, null!));
    }

    // ===== helpers =====

    private static string? Echo(string s) => s;

    private static TrackInfo Unmatched(TrackInfo t)
    {
        t.IsMatched = null;   // matcher leaves misses untouched (null)
        return t;
    }

    private static TrackInfo MakeTrack(int disc, int track, string song) => new()
    {
        FilePath = $"/fake/d{disc}_t{track}.flac",
        FileName = $"d{disc}_t{track}.flac",
        DiscNumber = disc,
        TrackNumber = track,
        SongName = song,
        IsModified = false,
    };

    private static SetlistEntryVm Vm(string name, int pos, string type = SetlistEntryType.Song) =>
        new(name, name, pos, false, $"Set 1, #{pos + 1}", type);

    private static SetlistMatcher.SetlistEntry MEntry(string name, int pos, string type = SetlistEntryType.Song) =>
        new() { Name = name, Canonical = name, Position = pos, Type = type };
}
