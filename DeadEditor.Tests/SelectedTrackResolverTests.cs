using DeadEditor;
using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="SelectedTrackResolver.Resolve"/>, the panel-assign current-track resolver
/// (reference-side-panel-spec.md §7.5). Regression cover for the slice-5b gate-b failure: Edit's
/// cell-selection grid left SelectedItem/CurrentItem null on a bare cell click, so resolution must
/// fall through to CurrentCell.Item (modeled here as the middle candidate).
/// </summary>
public class SelectedTrackResolverTests
{
    private static TrackInfoViewModel Vm(string song)
        => new TrackInfoViewModel(new TrackInfo { SongName = song }, new AlbumInfo());

    [Fact]
    public void ReturnsFirstCandidate_WhenSelectedItemIsTheTrack()
    {
        // Full-row grid (Import): SelectedItem holds the VM — first candidate wins.
        var vm = Vm("Ripple");

        Assert.Same(vm, SelectedTrackResolver.Resolve(vm, null, null));
    }

    [Fact]
    public void FallsThroughToCurrentCellItem_WhenSelectedItemNull()
    {
        // Cell-selection grid (Edit): SelectedItem null, CurrentCell.Item holds the VM. This is the
        // exact gate-b scenario the fix targets.
        var vm = Vm("Sugar Magnolia");

        Assert.Same(vm, SelectedTrackResolver.Resolve(null, vm, null));
    }

    [Fact]
    public void FallsThroughToCurrentItem_WhenEarlierCandidatesNonVm()
    {
        var vm = Vm("Bertha");

        Assert.Same(vm, SelectedTrackResolver.Resolve(null, null, vm));
    }

    [Fact]
    public void SkipsNonVmSentinels()
    {
        // A DataGrid can hand back null or a non-VM sentinel (e.g. DependencyProperty.UnsetValue,
        // modeled by a bare object) — these are skipped, not matched.
        var vm = Vm("Loser");

        Assert.Same(vm, SelectedTrackResolver.Resolve(new object(), null, vm));
    }

    [Fact]
    public void ReturnsNull_WhenNoCandidateIsATrack()
    {
        Assert.Null(SelectedTrackResolver.Resolve(null, new object(), null));
    }

    [Fact]
    public void ReturnsNull_ForEmptyCandidates()
    {
        Assert.Null(SelectedTrackResolver.Resolve());
    }
}
