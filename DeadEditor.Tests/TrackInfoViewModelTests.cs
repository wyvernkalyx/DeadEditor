using System.Collections.Generic;
using System.ComponentModel;
using DeadEditor;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="TrackInfoViewModel"/>'s property-change mirroring.
///
/// The VM wraps a <see cref="TrackInfo"/> and must re-raise its own
/// <c>SongName</c>/<c>Segue</c> when the underlying model raises them — so that
/// direct model writes (Match Setlist / Normalize / Match-to-Song), which bypass
/// the VM proxy setters, are still seen by subscribers (the Edit Metadata
/// unverify-on-edit channel). The model setters are change-guarded, so a no-op
/// write raises nothing and the VM never re-raises: that is the
/// "unverify only on a real change" guarantee, pinned at the source here.
/// </summary>
public class TrackInfoViewModelTests
{
    private static (TrackInfoViewModel vm, List<string?> raised) MakeVm(TrackInfo track)
    {
        var vm = new TrackInfoViewModel(track, new AlbumInfo());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        return (vm, raised);
    }

    [Fact]
    public void TrackInfoViewModel_ChangingModelSongName_RaisesVmSongName()
    {
        var (vm, raised) = MakeVm(new TrackInfo { SongName = "Dark Star" });

        vm.Track.SongName = "Bertha";

        Assert.Contains(nameof(TrackInfoViewModel.SongName), raised);
    }

    [Fact]
    public void TrackInfoViewModel_ChangingModelSegue_RaisesVmSegue()
    {
        var (vm, raised) = MakeVm(new TrackInfo { Segue = false });

        vm.Track.Segue = true;

        Assert.Contains(nameof(TrackInfoViewModel.Segue), raised);
    }

    [Fact]
    public void TrackInfoViewModel_SettingModelSongNameToSameValue_DoesNotRaise()
    {
        var (vm, raised) = MakeVm(new TrackInfo { SongName = "Dark Star" });

        // Change-guarded model setter: writing the current value raises nothing,
        // so the VM never re-raises SongName (no-op match must not unverify).
        vm.Track.SongName = "Dark Star";

        Assert.DoesNotContain(nameof(TrackInfoViewModel.SongName), raised);
    }

    [Fact]
    public void TrackInfoViewModel_SettingModelSegueToSameValue_DoesNotRaise()
    {
        var (vm, raised) = MakeVm(new TrackInfo { Segue = true });

        vm.Track.Segue = true;

        Assert.DoesNotContain(nameof(TrackInfoViewModel.Segue), raised);
    }
}
