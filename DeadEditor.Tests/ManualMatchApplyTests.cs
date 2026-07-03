using System.Collections.Generic;
using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="ManualMatchApply.Apply"/>, the pure manual-match write core shared by both
/// surfaces' right-click Match-to-Song handlers (reference-side-panel-spec.md §7.1). Covers the
/// state transition: canonical/flag writes, set-true-only segue (§7.3), claimed-position add, and
/// prior-title capture for the alias hook.
/// </summary>
public class ManualMatchApplyTests
{
    private static TrackInfo Track(string songName, bool segue = false, bool? matched = null,
        bool modified = false, int disc = 1, int track = 5)
        => new TrackInfo
        {
            SongName = songName,
            Segue = segue,
            IsMatched = matched,
            IsModified = modified,
            DiscNumber = disc,
            TrackNumber = track,
        };

    [Fact]
    public void NormalAssign_WritesCanonicalAndFlags_AndClaimsPosition()
    {
        var track = Track("ripple", matched: false, modified: false);
        var claimed = new HashSet<int>();

        var result = ManualMatchApply.Apply(track, claimed, 3, "Ripple", segue: false);

        Assert.Equal("Ripple", track.SongName);
        Assert.True(track.IsMatched);
        Assert.True(track.IsModified);
        Assert.Contains(3, claimed);
        Assert.Equal("ripple", result.PreviousSongName);
        Assert.Equal("Ripple", result.Canonical);
        Assert.Equal(3, result.ClaimedPosition);
    }

    [Fact]
    public void SegueTrue_PropagatesToTrack()
    {
        var track = Track("China Cat", segue: false);

        ManualMatchApply.Apply(track, new HashSet<int>(), 0, "China Cat Sunflower", segue: true);

        Assert.True(track.Segue);
    }

    [Fact]
    public void SegueFalse_DoesNotClearExistingSegue()
    {
        // Set-true-only, never cleared (§7.3): an entry without a segue must leave a track's
        // existing segue intact — write-identical to the manual path.
        var track = Track("I Know You Rider", segue: true);

        ManualMatchApply.Apply(track, new HashSet<int>(), 1, "I Know You Rider", segue: false);

        Assert.True(track.Segue);
    }

    [Fact]
    public void SegueFalse_LeavesUnseguedTrackUnsegued()
    {
        var track = Track("Casey Jones", segue: false);

        ManualMatchApply.Apply(track, new HashSet<int>(), 2, "Casey Jones", segue: false);

        Assert.False(track.Segue);
    }

    [Fact]
    public void ClaimedAdd_IsIdempotent()
    {
        var claimed = new HashSet<int> { 4 };

        ManualMatchApply.Apply(Track("x"), claimed, 4, "X", segue: false);

        Assert.Single(claimed);
        Assert.Contains(4, claimed);
    }

    [Fact]
    public void PreviousSongName_NullSongName_ReturnsEmptyString()
    {
        var track = Track(null!);

        var result = ManualMatchApply.Apply(track, new HashSet<int>(), 0, "Truckin'", segue: false);

        Assert.Equal("", result.PreviousSongName);
    }

    [Fact]
    public void DoesNotReassignDiscOrTrackNumber()
    {
        // Both surfaces decorate only — the audio's position in the recording is archival truth
        // and neither Import nor Edit touches disc/track here (§7.1).
        var track = Track("mornin dew", disc: 2, track: 7);

        ManualMatchApply.Apply(track, new HashSet<int>(), 0, "Morning Dew", segue: false);

        Assert.Equal(2, track.DiscNumber);
        Assert.Equal(7, track.TrackNumber);
    }
}
