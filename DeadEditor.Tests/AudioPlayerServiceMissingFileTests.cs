using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression gate for the missing-file playback crash: <see cref="AudioPlayerService"/> handed a
/// track's path straight to NAudio's <c>AudioFileReader</c> with no existence check, so a moved/
/// deleted file threw out of the ctor and — with no unhandled-exception handler — crashed the app.
///
/// The fix guards existence before touching NAudio and raises <see cref="AudioPlayerService.PlaybackFailed"/>
/// instead of throwing. A DIRECT play of a missing track stays put; the ADVANCE paths
/// (Next/Previous/auto-advance) skip missing files to the next playable track.
///
/// These tests exercise the guard and the skip DECISION at seams reachable without decoding audio:
/// <see cref="AudioPlayerService.Play(TrackInfo)"/>'s early guard (returns before AudioFileReader on a
/// missing path) and the internal <c>NextPlayableIndex</c> scan (pure existence checks over the
/// playlist). They do not construct a real WaveOut device or decode a FLAC — the "playable" tracks
/// only need to exist on disk for the existence check, which is what the skip logic consults.
/// </summary>
public class AudioPlayerServiceMissingFileTests
{
    /// <summary>
    /// Builds a fresh, isolated <see cref="AudioPlayerService"/> via its private singleton
    /// constructor (only initializes the playlist + state — no NAudio device, no media keys).
    /// </summary>
    private static AudioPlayerService NewIsolatedService()
    {
        var ctor = typeof(AudioPlayerService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        Assert.NotNull(ctor);
        return (AudioPlayerService)ctor!.Invoke(null);
    }

    private static string NewTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deadeditor_playback_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
    }

    private static string NewMissingPath()
        => Path.Combine(Path.GetTempPath(), $"deadeditor_missing_{Guid.NewGuid():N}.flac");

    [Fact]
    public void Play_MissingFile_RaisesPlaybackFailed_DoesNotThrow_StaysStopped()
    {
        var svc = NewIsolatedService();
        var track = new TrackInfo { FilePath = NewMissingPath(), FileName = "gone.flac", SongName = "Gone" };

        PlaybackFailedEventArgs? failed = null;
        svc.PlaybackFailed += (_, e) => failed = e;

        // Must not throw (the pre-fix crash) and must not enter Playing.
        var ex = Record.Exception(() => svc.Play(track));

        Assert.Null(ex);
        Assert.NotNull(failed);
        Assert.Same(track, failed!.Track);
        Assert.Equal(PlaybackState.Stopped, svc.State);
    }

    [Fact]
    public void NextPlayableIndex_MiddleTrackMissing_SkipsToNextPlayable_RaisesForSkipped()
    {
        var svc = NewIsolatedService();
        var present0 = NewTempFile();
        var missing1 = NewMissingPath();
        var present2 = NewTempFile();

        try
        {
            svc.Playlist.Add(new TrackInfo { FilePath = present0, FileName = "0", SongName = "Zero" });
            svc.Playlist.Add(new TrackInfo { FilePath = missing1, FileName = "1", SongName = "One" });
            svc.Playlist.Add(new TrackInfo { FilePath = present2, FileName = "2", SongName = "Two" });

            var skipped = new List<TrackInfo>();
            svc.PlaybackFailed += (_, e) => { if (e.Track != null) skipped.Add(e.Track); };

            // Advancing forward from index 0 skips the missing index 1 and lands on the playable index 2.
            int idx = svc.NextPlayableIndex(0, +1);

            Assert.Equal(2, idx);
            Assert.Single(skipped);
            Assert.Same(svc.Playlist[1], skipped[0]);
        }
        finally
        {
            File.Delete(present0);
            File.Delete(present2);
        }
    }

    [Fact]
    public void NextPlayableIndex_AllRemainingMissing_ReturnsMinusOne_NoThrow()
    {
        var svc = NewIsolatedService();
        var present0 = NewTempFile();

        try
        {
            svc.Playlist.Add(new TrackInfo { FilePath = present0, FileName = "0", SongName = "Zero" });
            svc.Playlist.Add(new TrackInfo { FilePath = NewMissingPath(), FileName = "1", SongName = "One" });
            svc.Playlist.Add(new TrackInfo { FilePath = NewMissingPath(), FileName = "2", SongName = "Two" });

            int raised = 0;
            svc.PlaybackFailed += (_, __) => raised++;

            int idx = 0;
            var ex = Record.Exception(() => { idx = svc.NextPlayableIndex(0, +1); });

            Assert.Null(ex);          // no throw, no hang
            Assert.Equal(-1, idx);    // nothing playable ahead
            Assert.Equal(2, raised);  // both missing tail tracks reported
        }
        finally
        {
            File.Delete(present0);
        }
    }
}
