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
        Assert.Equal("1 tracks already match", vm.HiddenSummary);
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
