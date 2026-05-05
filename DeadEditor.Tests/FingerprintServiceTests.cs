using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for the shared <see cref="FingerprintService"/>. Coverage focuses on the
/// offline path (MusicBrainzService with no FpcalcPath) — the success path requires
/// real audio plus fpcalc.exe and is not unit-testable, mirroring the gap in
/// <see cref="LibraryImportServiceFingerprintTests"/>.
/// </summary>
public class FingerprintServiceTests
{
    private const string PreexistingFingerprint = "AQADtBpZ8axBhXJzNH1ENcTRdNRTNB1qPYjUJTuOPMnRJEeOPP2RPMdxJEf6oXmO_kj-INKR9OOR_DiOPjmO_kgenkme40hyJEd-JEeOJDmO5MdxHMdxHEdyHEdyHMlxJEdyJMeRHMlxHMdxJEdyHMdxJEdyHMlxHMdxHMdxHMdxJMdxHMdxHEdyHMdxHEdyHMdxJMdxJEdyHMdxJMeRHMdxJMlxHMdxHMdxHEdyHMdxHEdyJMdxHMdxJEdy";
    private const string TestFingerprint = "AQADtFRSXcS-MUz1nZHk6_jM50dPJI3qPM-ho-mD7Eum6Yt-9MWP9MGPHj1y9MfJOEd_Iv2Q9Mhx9EePHkdy9OmTPMd_PHmO_PiR8seRP9HxoaeS9MeJPshxJEf6IH3Q4z967IePPUePoz-S5_jR50d_5MOPI3l-fOidI3lyJDfyHk2OfsiPHE-OHs9R9DiOI8eR48iRH8mTJ8eR48iPI8mPHsfx40iSI8mP5MeR48jx5DiOIzny40hyJEd-HMlxJD-O5DhyHEdyHMmRHEdyJEdyHEdyJMdxJEdyHMmRHMdx";

    // ===== PrecomputeFingerprintsAsync =====

    [Fact]
    public async Task PrecomputeFingerprintsAsync_AllTracksAlreadyFingerprinted_SkipsAll()
    {
        var service = new FingerprintService(CreateOfflineMusicBrainzService());
        var tracks = new List<TrackInfo>
        {
            new() { FilePath = "/dev/null/a", AcoustIdFingerprint = PreexistingFingerprint },
            new() { FilePath = "/dev/null/b", AcoustIdFingerprint = PreexistingFingerprint },
            new() { FilePath = "/dev/null/c", AcoustIdFingerprint = PreexistingFingerprint },
        };

        bool callbackFired = false;
        var result = await service.PrecomputeFingerprintsAsync(
            tracks,
            progress: null,
            onTrackComplete: _ => { callbackFired = true; return Task.CompletedTask; });

        Assert.Equal(0, result.Computed);
        Assert.Equal(3, result.SkippedExisting);
        Assert.Equal(0, result.Failed);
        Assert.True(result.FpcalcAvailable, "no fpcalc determination was made (no tracks attempted)");
        Assert.False(callbackFired, "onTrackComplete must not fire for skipped-existing tracks");
    }

    [Fact]
    public async Task PrecomputeFingerprintsAsync_FpcalcNotConfigured_FailsAttempted_SetsFpcalcAvailableFalse()
    {
        var service = new FingerprintService(CreateOfflineMusicBrainzService());
        var tracks = new List<TrackInfo>
        {
            new() { FilePath = "/dev/null/a" }, // bare — will attempt and fail
            new() { FilePath = "/dev/null/b", AcoustIdFingerprint = PreexistingFingerprint }, // skipped
            new() { FilePath = "/dev/null/c" }, // bare — sticky-flag short-circuit, also fails
        };

        var result = await service.PrecomputeFingerprintsAsync(tracks);

        Assert.Equal(0, result.Computed);
        Assert.Equal(1, result.SkippedExisting);
        Assert.Equal(2, result.Failed);
        Assert.False(result.FpcalcAvailable);
    }

    [Fact]
    public async Task PrecomputeFingerprintsAsync_ReportsProgress()
    {
        var service = new FingerprintService(CreateOfflineMusicBrainzService());
        var tracks = new List<TrackInfo>
        {
            new() { FilePath = "/dev/null/a" },
            new() { FilePath = "/dev/null/b" },
        };

        // Use a synchronous IProgress so we don't race with Progress<T>'s async dispatch.
        var progress = new SyncProgress<(int current, int total, string message)>();

        await service.PrecomputeFingerprintsAsync(tracks, progress);

        // First track: "Fingerprinting tracks (1 of 2)..." then "fpcalc not configured..."
        Assert.Contains(progress.Reports, r => r.message.Contains("Fingerprinting tracks (1 of 2)"));
        Assert.Contains(progress.Reports, r => r.message.Contains("fpcalc not configured"));
    }

    private class SyncProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = new();
        public void Report(T value) => Reports.Add(value);
    }

    [Fact]
    public async Task PrecomputeFingerprintsAsync_EmptyTrackList_ReturnsZeroCounts()
    {
        var service = new FingerprintService(CreateOfflineMusicBrainzService());
        var result = await service.PrecomputeFingerprintsAsync(new List<TrackInfo>());

        Assert.Equal(0, result.Computed);
        Assert.Equal(0, result.SkippedExisting);
        Assert.Equal(0, result.Failed);
        Assert.True(result.FpcalcAvailable, "no fpcalc determination was made (no tracks)");
    }

    // ===== WriteFingerprintToTrackFile =====

    [Fact]
    public void WriteFingerprintToTrackFile_WithFingerprint_WritesToFlacXiph()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);

            var track = new TrackInfo
            {
                FilePath = flacPath,
                AcoustIdFingerprint = TestFingerprint,
            };

            FingerprintService.WriteFingerprintToTrackFile(track);

            Assert.Equal(TestFingerprint, ReadXiphFingerprint(flacPath));
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void WriteFingerprintToTrackFile_WithEmptyFingerprint_PreservesExistingTag()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphFingerprint(flacPath, PreexistingFingerprint);

            var track = new TrackInfo
            {
                FilePath = flacPath,
                AcoustIdFingerprint = null,
            };

            FingerprintService.WriteFingerprintToTrackFile(track);

            Assert.Equal(PreexistingFingerprint, ReadXiphFingerprint(flacPath));
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void WriteFingerprintToTrackFile_OverwritesExistingValue()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphFingerprint(flacPath, PreexistingFingerprint);

            var track = new TrackInfo
            {
                FilePath = flacPath,
                AcoustIdFingerprint = TestFingerprint,
            };

            FingerprintService.WriteFingerprintToTrackFile(track);

            Assert.Equal(TestFingerprint, ReadXiphFingerprint(flacPath));
        }
        finally { SafeDeleteDirectory(dir); }
    }

    // ===== Helpers =====

    private static MusicBrainzService CreateOfflineMusicBrainzService()
        => new MusicBrainzService("test-key", new LibrarySettings());

    private static string? ReadXiphFingerprint(string filePath)
    {
        using var tagFile = TagLib.File.Create(filePath, TagLib.ReadStyle.PictureLazy);
        if (tagFile is not TagLib.Flac.File flacFile) return null;
        var xiph = (TagLib.Ogg.XiphComment?)flacFile.GetTag(TagLib.TagTypes.Xiph);
        return xiph?.GetFirstField("ACOUSTID_FINGERPRINT");
    }

    private static void WriteXiphFingerprint(string filePath, string fingerprint)
    {
        using var file = TagLib.File.Create(filePath);
        if (file is TagLib.Flac.File flacFile)
        {
            var xiph = (TagLib.Ogg.XiphComment?)flacFile.GetTag(TagLib.TagTypes.Xiph);
            xiph?.SetField("ACOUSTID_FINGERPRINT", fingerprint);
            file.Save();
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deadeditor_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; temp dir will eventually be reaped.
        }
    }

    private static void CreateMinimalFlacFile(string path)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        {
            fs.Write(new byte[] { 0x66, 0x4C, 0x61, 0x43 }, 0, 4);
            fs.Write(new byte[] { 0x80, 0x00, 0x00, 0x22 }, 0, 4);

            var streaminfo = new byte[34];
            streaminfo[0] = 0x10; streaminfo[1] = 0x00;
            streaminfo[2] = 0x10; streaminfo[3] = 0x00;
            streaminfo[10] = 0x0A; streaminfo[11] = 0xC4; streaminfo[12] = 0x42;
            const long totalSamples = 44100;
            streaminfo[13] = (byte)((totalSamples >> 32) & 0x0F);
            streaminfo[14] = (byte)((totalSamples >> 24) & 0xFF);
            streaminfo[15] = (byte)((totalSamples >> 16) & 0xFF);
            streaminfo[16] = (byte)((totalSamples >> 8) & 0xFF);
            streaminfo[17] = (byte)(totalSamples & 0xFF);
            fs.Write(streaminfo, 0, 34);

            fs.Write(new byte[] { 0xFF, 0xF8, 0x69, 0x04 }, 0, 4);
            fs.Write(new byte[100], 0, 100);
        }

        using var file = TagLib.File.Create(path);
        file.Save();
    }
}
