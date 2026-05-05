using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression tests for TRACKTOTAL/DISCTOTAL (FLAC Vorbis) and the TRCK/TPOS
/// denominators (ID3v2) — exposed via TagLib's <c>Tag.TrackCount</c> and
/// <c>Tag.DiscCount</c>. Prior to Commit 1 these fields were passthroughs;
/// <see cref="MetadataService.WriteMetadata"/> now derives them from the
/// in-memory track list at write time.
/// </summary>
public class MetadataServiceTotalsTests
{
    [Fact]
    public void WriteMetadata_SingleDiscFiveTracks_WritesTrackCount5_DiscCount1()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var tracks = CreateFlacTracks(dir, disc: 1, count: 5);

            new MetadataService().WriteMetadata(BuildTestAlbumInfo(), tracks);

            foreach (var t in tracks)
            {
                var (trackCount, discCount) = ReadCounts(t.FilePath);
                Assert.Equal((uint)5, trackCount);
                Assert.Equal((uint)1, discCount);
            }
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void WriteMetadata_ThreeDiscEvenSplit_WritesPerDiscCounts()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var tracks = new List<TrackInfo>();
            tracks.AddRange(CreateFlacTracks(dir, disc: 1, count: 4));
            tracks.AddRange(CreateFlacTracks(dir, disc: 2, count: 4));
            tracks.AddRange(CreateFlacTracks(dir, disc: 3, count: 4));

            new MetadataService().WriteMetadata(BuildTestAlbumInfo(), tracks);

            foreach (var t in tracks)
            {
                var (trackCount, discCount) = ReadCounts(t.FilePath);
                Assert.Equal((uint)4, trackCount);
                Assert.Equal((uint)3, discCount);
            }
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void WriteMetadata_ThreeDiscUnevenSplit_WritesCountForOwnDisc()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var tracks = new List<TrackInfo>();
            tracks.AddRange(CreateFlacTracks(dir, disc: 1, count: 10));
            tracks.AddRange(CreateFlacTracks(dir, disc: 2, count: 12));
            tracks.AddRange(CreateFlacTracks(dir, disc: 3, count: 8));

            new MetadataService().WriteMetadata(BuildTestAlbumInfo(), tracks);

            foreach (var t in tracks)
            {
                var (trackCount, discCount) = ReadCounts(t.FilePath);
                var expected = t.DiscNumber switch
                {
                    1 => (uint)10,
                    2 => (uint)12,
                    3 => (uint)8,
                    _ => throw new InvalidOperationException($"unexpected disc {t.DiscNumber}"),
                };
                Assert.Equal(expected, trackCount);
                Assert.Equal((uint)3, discCount);
            }
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void WriteMetadata_NonContiguousDiscNumbers_DiscCountIsDistinctCount()
    {
        // Tracks on discs 1, 1, 5 → DiscCount must be 2, not 5.
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var tracks = new List<TrackInfo>();
            tracks.AddRange(CreateFlacTracks(dir, disc: 1, count: 2));
            tracks.AddRange(CreateFlacTracks(dir, disc: 5, count: 1));

            new MetadataService().WriteMetadata(BuildTestAlbumInfo(), tracks);

            foreach (var t in tracks)
            {
                var (trackCount, discCount) = ReadCounts(t.FilePath);
                Assert.Equal((uint)2, discCount);
                var expectedTrackCount = t.DiscNumber == 1 ? (uint)2 : (uint)1;
                Assert.Equal(expectedTrackCount, trackCount);
            }
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void WriteMetadata_StaleTotals_GetCorrected()
    {
        // The actual passthrough-bug-fix test: pre-stamp 99/99 onto the file,
        // run WriteMetadata against a 3-track / 1-disc list, confirm we wrote
        // 3/1 — not preserved 99/99 from passthrough.
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var tracks = CreateFlacTracks(dir, disc: 1, count: 3);
            foreach (var t in tracks)
            {
                StampStaleTotals(t.FilePath, trackTotal: 99, discTotal: 99);
                var (preTrack, preDisc) = ReadCounts(t.FilePath);
                Assert.Equal((uint)99, preTrack);
                Assert.Equal((uint)99, preDisc);
            }

            new MetadataService().WriteMetadata(BuildTestAlbumInfo(), tracks);

            foreach (var t in tracks)
            {
                var (trackCount, discCount) = ReadCounts(t.FilePath);
                Assert.Equal((uint)3, trackCount);
                Assert.Equal((uint)1, discCount);
            }
        }
        finally { SafeDeleteDirectory(dir); }
    }

    // ===== Helpers =====

    private static List<TrackInfo> CreateFlacTracks(string dir, int disc, int count)
    {
        var tracks = new List<TrackInfo>(count);
        for (int n = 1; n <= count; n++)
        {
            var name = $"d{disc}_t{n:D2}.flac";
            var path = Path.Combine(dir, name);
            CreateMinimalFlacFile(path);
            tracks.Add(new TrackInfo
            {
                FilePath = path,
                FileName = name,
                TrackNumber = n,
                DiscNumber = disc,
                SongName = $"Song {disc}-{n}",
            });
        }
        return tracks;
    }

    private static AlbumInfo BuildTestAlbumInfo() => new()
    {
        Artist = "Test Artist",
        AlbumDate = "2026-01-01",
        Venue = "Test Venue",
        CityState = "Test City, TS",
        AlbumName = "Test Album",
        Year = "2026",
        Type = AlbumType.AudienceRecording,
    };

    private static (uint trackCount, uint discCount) ReadCounts(string filePath)
    {
        using var file = TagLib.File.Create(filePath);
        return (file.Tag.TrackCount, file.Tag.DiscCount);
    }

    /// <summary>
    /// Writes explicit TRACKTOTAL/DISCTOTAL values to a FLAC file via TagLib's
    /// <c>TrackCount</c>/<c>DiscCount</c> setters — same code path the SUT uses,
    /// so we know the test stamp will match what the SUT would read back.
    /// </summary>
    private static void StampStaleTotals(string filePath, uint trackTotal, uint discTotal)
    {
        using var file = TagLib.File.Create(filePath);
        file.Tag.TrackCount = trackTotal;
        file.Tag.DiscCount = discTotal;
        file.Save();
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
