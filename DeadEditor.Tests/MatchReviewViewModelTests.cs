using DeadEditor;
using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for the Match Setlist review surface's pure core:
/// <see cref="ReviewRowViewModel"/> (per-field resolution, no-op detection,
/// effective-value resolution) and <see cref="MatchReviewViewModel"/>
/// (visible/hidden split, edited-proposal rebuild).
///
/// The central guarantee under test is the 1977-05-11 data-loss case: a blind
/// Apply must NOT clear a real segue. With per-field defaults, a segue-remove
/// row defaults Ignore, so the effective segue stays true and the rebuilt
/// proposal carries NewSegue == true (protected).
/// </summary>
public class MatchReviewViewModelTests
{
    // ===== ReviewRowViewModel: no-op detection =====

    [Fact]
    public void Row_NameEqual_SegueEqual_IsNoOp()
    {
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "Sugar Magnolia", newName: "Sugar Magnolia",
            oldSegue: false, newSegue: false));

        Assert.True(row.IsNoOp);
    }

    [Fact]
    public void Row_NameEqual_SegueDiffers_IsNotNoOp_StaysVisible()
    {
        // Conjunction rule (spec §4): a name-equal row whose segue would change
        // is NOT a no-op and must stay visible.
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "Sugar Magnolia", newName: "Sugar Magnolia",
            oldSegue: false, newSegue: true));

        Assert.False(row.IsNoOp);
    }

    // ===== ReviewRowViewModel: per-field changed flags =====

    [Fact]
    public void Row_SongNameChanged_TrueWhenNamesDiffer_FalseWhenEqual()
    {
        var changed = new ReviewRowViewModel(MakeProposal(
            "Watchtower", "All Along The Watchtower", false, false));
        Assert.True(changed.SongNameChanged);

        var equal = new ReviewRowViewModel(MakeProposal(
            "Sugar Magnolia", "Sugar Magnolia", false, true));
        Assert.False(equal.SongNameChanged);
    }

    [Fact]
    public void Row_SegueChanged_TrueForFlip_FalseWhenEqual()
    {
        var added = new ReviewRowViewModel(MakeProposal(
            "China Cat Sunflower", "China Cat Sunflower", false, true));
        Assert.True(added.SegueChanged);

        var removed = new ReviewRowViewModel(MakeProposal(
            "Scarlet Begonias", "Scarlet Begonias", true, false));
        Assert.True(removed.SegueChanged);

        var equal = new ReviewRowViewModel(MakeProposal(
            "Eyes Of The World", "Eyes Of The World", true, true));
        Assert.False(equal.SegueChanged);
    }

    // ===== ReviewRowViewModel: current-identity pass-throughs (mirror the grid) =====

    [Fact]
    public void Row_IdentityDisplays_MirrorUnderlyingTrackInfo()
    {
        var track = new TrackInfo
        {
            FilePath = "/fake/t.flac",
            FileName = "t.flac",
            DiscNumber = 2,
            TrackNumber = 9,
            SongName = "Eyes Of The World",
            Duration = "12:34",
        };
        var proposal = new SetlistMatcher.TrackProposal
        {
            Track = track,
            OldSongName = "Eyes Of The World",
            NewSongName = "Eyes Of The World",
            OldSegue = true,
            NewSegue = false,
            CoveredEntryIndices = new List<int> { 0 },
        };

        var row = new ReviewRowViewModel(proposal);

        // Uses the grid's plain TrackNumber + DiscNumber (9 / 2), NOT the
        // disc-concatenated DisplayTrackNumber ("209").
        Assert.Equal("#9 · Disc 2", row.TrackNumberDisplay);
        Assert.Equal("Eyes Of The World", row.TrackTitleDisplay);
        Assert.Equal("12:34", row.TrackDurationDisplay);

        // Reflects current state: a renumber before the dialog is mirrored.
        track.TrackNumber = 1;
        track.DiscNumber = 1;
        Assert.Equal("#1 · Disc 1", row.TrackNumberDisplay);
    }

    // ===== ReviewRowViewModel: segue resolution =====

    [Fact]
    public void Row_SegueRemove_DefaultsIgnore_EffectiveSegueStaysTrue()
    {
        // The 1977-05-11 case: setlist proposes clearing a real segue.
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "Scarlet Begonias", newName: "Scarlet Begonias",
            oldSegue: true, newSegue: false));

        Assert.Equal(ReviewDecision.Ignore, row.SegueDecision);
        Assert.True(row.EffectiveSegue); // protected — keeps the real segue
    }

    [Fact]
    public void Row_SegueAdd_DefaultsAccept_EffectiveSegueTrue()
    {
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "China Cat Sunflower", newName: "China Cat Sunflower",
            oldSegue: false, newSegue: true));

        Assert.Equal(ReviewDecision.Accept, row.SegueDecision);
        Assert.True(row.EffectiveSegue);
    }

    // ===== ReviewRowViewModel: SongName resolution =====

    [Fact]
    public void Row_SongName_Accept_EffectiveIsNew()
    {
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "Watchtower", newName: "All Along The Watchtower",
            oldSegue: false, newSegue: false));

        Assert.Equal(ReviewDecision.Accept, row.SongNameDecision); // variant -> canonical
        Assert.Equal("All Along The Watchtower", row.EffectiveSongName);
    }

    [Fact]
    public void Row_SongName_HandTuned_EffectiveIsEditedValue()
    {
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "Watchtower", newName: "All Along The Watchtower",
            oldSegue: false, newSegue: false));

        row.EditableSongName = "All Along the Watchtower (Reprise)";

        Assert.Equal("All Along the Watchtower (Reprise)", row.EffectiveSongName);
    }

    [Fact]
    public void Row_SongName_Ignore_EffectiveIsOld()
    {
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "Watchtower", newName: "All Along The Watchtower",
            oldSegue: false, newSegue: false));

        row.SongNameDecision = ReviewDecision.Ignore;

        Assert.Equal("Watchtower", row.EffectiveSongName);
    }

    [Fact]
    public void Row_EditableSongName_SeededWithProposedCanonical()
    {
        var row = new ReviewRowViewModel(MakeProposal(
            oldName: "Watchtower", newName: "All Along The Watchtower",
            oldSegue: false, newSegue: false));

        Assert.Equal("All Along The Watchtower", row.EditableSongName);
    }

    // ===== MatchReviewViewModel: visible/hidden split =====

    [Fact]
    public void Container_SplitsVisibleAndHidden_AndSummary()
    {
        var set = MakeSet(
            MakeProposal("Watchtower", "All Along The Watchtower", false, false), // visible (name)
            MakeProposal("Sugar Magnolia", "Sugar Magnolia", false, false),       // no-op (hidden)
            MakeProposal("Eyes", "Eyes Of The World", true, true));               // visible (name)

        var vm = new MatchReviewViewModel(set);

        Assert.Equal(2, vm.VisibleRows.Count);
        Assert.Equal(1, vm.HiddenCount);
        Assert.True(vm.HasHidden);
        Assert.Equal("1 track already matches", vm.HiddenSummary);
        Assert.Equal(3, vm.AllRows.Count);
    }

    [Fact]
    public void Container_NoHidden_HasHiddenFalse()
    {
        var set = MakeSet(
            MakeProposal("Watchtower", "All Along The Watchtower", false, false));

        var vm = new MatchReviewViewModel(set);

        Assert.DoesNotContain(vm.VisibleRows, r => r.IsNoOp);
        Assert.Equal(0, vm.HiddenCount);
        Assert.False(vm.HasHidden);
        Assert.Equal("0 tracks already match", vm.HiddenSummary);
    }

    [Fact]
    public void Container_HiddenSummary_SingularForOne_PluralOtherwise()
    {
        var visible = MakeProposal("Watchtower", "All Along The Watchtower", false, false);

        var oneHidden = new MatchReviewViewModel(MakeSet(
            visible,
            MakeProposal("Sugar Magnolia", "Sugar Magnolia", false, false)));
        Assert.Equal("1 track already matches", oneHidden.HiddenSummary);

        var twoHidden = new MatchReviewViewModel(MakeSet(
            visible,
            MakeProposal("Sugar Magnolia", "Sugar Magnolia", false, false),
            MakeProposal("Eyes Of The World", "Eyes Of The World", true, true)));
        Assert.Equal("2 tracks already match", twoHidden.HiddenSummary);
    }

    // ===== MatchReviewViewModel: combined rows always shown (spec §6.1) =====

    [Fact]
    public void Container_CombinedNoOpRow_AlwaysVisible_NotCounted()
    {
        // Spec §6.1: a combine (CoveredEntryIndices.Count > 1) is confirmed even
        // when its derived name and segue equal the existing tag (a name no-op) —
        // the human confirms the combine, not a text diff.
        var combinedNoOp = MakeProposal(
            "Help On The Way > Slipknot!", "Help On The Way > Slipknot!",
            oldSegue: false, newSegue: false,
            covered: new List<int> { 0, 1 });

        var vm = new MatchReviewViewModel(MakeSet(combinedNoOp));

        var row = Assert.Single(vm.VisibleRows);
        Assert.True(row.IsNoOp);     // name + segue unchanged
        Assert.True(row.IsCombined); // but it's a combine -> exempt
        Assert.Equal(0, vm.HiddenCount);
        Assert.False(vm.HasHidden);
    }

    [Fact]
    public void Container_SingleEntryNoOpRow_StillHidden()
    {
        // Guard against over-exempting: a single-entry no-op (Count == 1) is hidden.
        var singleNoOp = MakeProposal(
            "Sugar Magnolia", "Sugar Magnolia", false, false,
            covered: new List<int> { 0 });

        var vm = new MatchReviewViewModel(MakeSet(singleNoOp));

        Assert.False(vm.AllRows[0].IsCombined);
        Assert.True(vm.AllRows[0].IsNoOp);
        Assert.Empty(vm.VisibleRows);
        Assert.Equal(1, vm.HiddenCount);
    }

    [Fact]
    public void Container_CombinedChangedRow_VisibleAsBefore()
    {
        // Regression guard: a combine that DID rename is visible exactly as before.
        var combinedChanged = MakeProposal(
            "Help On The Way/Slipknot!", "Help On The Way > Slipknot!",
            oldSegue: false, newSegue: false,
            covered: new List<int> { 0, 1 });

        var vm = new MatchReviewViewModel(MakeSet(combinedChanged));

        var row = Assert.Single(vm.VisibleRows);
        Assert.False(row.IsNoOp);
        Assert.True(row.IsCombined);
        Assert.Equal(0, vm.HiddenCount);
    }

    // ===== MatchReviewViewModel: BuildEditedProposalSet =====

    [Fact]
    public void Build_CarriesClaimedPositionsUnchanged()
    {
        var set = MakeSet(
            new[] { 3, 7, 11 },
            MakeProposal("Watchtower", "All Along The Watchtower", false, false));

        var vm = new MatchReviewViewModel(set);
        var built = vm.BuildEditedProposalSet();

        Assert.Equal(new HashSet<int> { 3, 7, 11 }, built.ClaimedPositions);
    }

    [Fact]
    public void Build_IncludesHiddenNoOpRows_WithNewEqualsOld()
    {
        var noOp = MakeProposal("Sugar Magnolia", "Sugar Magnolia", false, false);
        var set = MakeSet(noOp);

        var vm = new MatchReviewViewModel(set);
        var built = vm.BuildEditedProposalSet();

        // Hidden no-op row is still present (never dropped), New == Old.
        Assert.Single(built.Proposals);
        var p = built.Proposals[0];
        Assert.Equal("Sugar Magnolia", p.NewSongName);
        Assert.Equal("Sugar Magnolia", p.OldSongName);
        Assert.False(p.NewSegue);
    }

    [Fact]
    public void Build_SegueRemoveRow_RebuiltProposalProtectsSegue()
    {
        // End-to-end of the data-loss guard: a segue-remove row, left at its
        // Ignore default, rebuilds to NewSegue == true (the real segue survives).
        var set = MakeSet(
            MakeProposal("Scarlet Begonias", "Scarlet Begonias", oldSegue: true, newSegue: false));

        var vm = new MatchReviewViewModel(set);
        var built = vm.BuildEditedProposalSet();

        Assert.True(built.Proposals[0].NewSegue);
        Assert.True(built.Proposals[0].OldSegue);
    }

    [Fact]
    public void Build_AcceptedAndIgnoredRows_ResolveCorrectly()
    {
        var acceptRow = MakeProposal("Watchtower", "All Along The Watchtower", false, false);
        var removeRow = MakeProposal("Scarlet Begonias", "Scarlet Begonias", true, false);

        var vm = new MatchReviewViewModel(MakeSet(acceptRow, removeRow));

        // Hand-ignore the name on the first row; leave the second at defaults.
        vm.AllRows[0].SongNameDecision = ReviewDecision.Ignore;

        var built = vm.BuildEditedProposalSet();

        Assert.Equal("Watchtower", built.Proposals[0].NewSongName); // ignored -> old
        Assert.True(built.Proposals[1].NewSegue);                   // segue-remove protected
    }

    [Fact]
    public void Build_DoesNotMutateSourceProposals()
    {
        var source = MakeProposal("Scarlet Begonias", "Scarlet Begonias", oldSegue: true, newSegue: false);
        var set = MakeSet(source);

        var vm = new MatchReviewViewModel(set);
        vm.BuildEditedProposalSet();

        // The compute output is untouched — still carries the original New values.
        Assert.False(source.NewSegue);
        Assert.Equal("Scarlet Begonias", source.NewSongName);
    }

    [Fact]
    public void Build_PreservesTrackAndCoveredEntryIndices()
    {
        var source = MakeProposal("Watchtower", "All Along The Watchtower", false, false,
            covered: new List<int> { 5 });
        var set = MakeSet(source);

        var vm = new MatchReviewViewModel(set);
        var built = vm.BuildEditedProposalSet();

        Assert.Same(source.Track, built.Proposals[0].Track);
        Assert.Equal(new List<int> { 5 }, built.Proposals[0].CoveredEntryIndices);
    }

    // ===== Helpers =====

    private static SetlistMatcher.TrackProposal MakeProposal(
        string oldName, string newName, bool oldSegue, bool newSegue,
        List<int>? covered = null)
    {
        return new SetlistMatcher.TrackProposal
        {
            Track = new TrackInfo
            {
                FilePath = "/fake/t.flac",
                FileName = "t.flac",
                DiscNumber = 1,
                TrackNumber = 1,
                SongName = oldName,
                Segue = oldSegue,
                IsModified = false,
            },
            OldSongName = oldName,
            NewSongName = newName,
            OldSegue = oldSegue,
            NewSegue = newSegue,
            CoveredEntryIndices = covered ?? new List<int> { 0 },
        };
    }

    private static SetlistMatcher.ProposalSet MakeSet(params SetlistMatcher.TrackProposal[] proposals)
        => MakeSet(new[] { 0 }, proposals);

    private static SetlistMatcher.ProposalSet MakeSet(int[] claimed, params SetlistMatcher.TrackProposal[] proposals)
        => new SetlistMatcher.ProposalSet
        {
            Proposals = proposals.ToList(),
            ClaimedPositions = new HashSet<int>(claimed),
        };
}
