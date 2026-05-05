using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Integration tests verifying that each guarded write service rejects file paths
/// outside the configured library root. Happy-path coverage for
/// <see cref="MetadataService.WriteMetadata"/> and
/// <see cref="FingerprintService.WriteFingerprintToTrackFile"/> already lives in
/// <see cref="MetadataServiceFingerprintTests"/>, <see cref="MetadataServiceMbidTests"/>,
/// and <see cref="FingerprintServiceTests"/> — those tests now use the
/// <see cref="PathGuard.OverrideLibraryRootForTesting(string)"/> seam, so they double
/// as confirmation that the guard is invisible to correct callers.
/// </summary>
public class WriteServiceGuardTests
{
    [Fact]
    public void MetadataService_WriteMetadata_PathOutsideLibrary_Throws()
    {
        var libraryRoot = CreateTempDir();
        var sourceDir = CreateTempDir(); // outside library
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var sourcePath = Path.Combine(sourceDir, "track.flac");
            CreateMinimalFlacFile(sourcePath);

            var album = BuildTestAlbumInfo();
            var track = new TrackInfo
            {
                FilePath = sourcePath,
                FileName = "track.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test",
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => new MetadataService().WriteMetadata(album, new List<TrackInfo> { track }));

            Assert.Contains("MetadataService.WriteMetadata", ex.Message);
            Assert.Contains(sourcePath, ex.Message);
        }
        finally
        {
            SafeDeleteDirectory(sourceDir);
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void MetadataService_WriteMetadata_MultipleTracksMixedPaths_AllOffendersListed()
    {
        var libraryRoot = CreateTempDir();
        var sourceDir = CreateTempDir(); // outside library
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var managedPath = Path.Combine(libraryRoot, "track_managed.flac");
            CreateMinimalFlacFile(managedPath);
            var sourcePath1 = Path.Combine(sourceDir, "track_src1.flac");
            CreateMinimalFlacFile(sourcePath1);
            var sourcePath2 = Path.Combine(sourceDir, "track_src2.flac");
            CreateMinimalFlacFile(sourcePath2);

            var album = BuildTestAlbumInfo();
            var tracks = new List<TrackInfo>
            {
                new() { FilePath = managedPath, FileName = "track_managed.flac", TrackNumber = 1, DiscNumber = 1, SongName = "OK" },
                new() { FilePath = sourcePath1, FileName = "track_src1.flac",   TrackNumber = 2, DiscNumber = 1, SongName = "Bad1" },
                new() { FilePath = sourcePath2, FileName = "track_src2.flac",   TrackNumber = 3, DiscNumber = 1, SongName = "Bad2" },
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => new MetadataService().WriteMetadata(album, tracks));

            Assert.DoesNotContain(managedPath, ex.Message);
            Assert.Contains(sourcePath1, ex.Message);
            Assert.Contains(sourcePath2, ex.Message);
        }
        finally
        {
            SafeDeleteDirectory(sourceDir);
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void FingerprintService_WriteFingerprintToTrackFile_PathOutsideLibrary_Throws()
    {
        var libraryRoot = CreateTempDir();
        var sourceDir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var sourcePath = Path.Combine(sourceDir, "track.flac");
            CreateMinimalFlacFile(sourcePath);

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                AcoustIdFingerprint = "AQADtTestFingerprint",
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => FingerprintService.WriteFingerprintToTrackFile(track));

            Assert.Contains("FingerprintService.WriteFingerprintToTrackFile", ex.Message);
            Assert.Contains(sourcePath, ex.Message);
        }
        finally
        {
            SafeDeleteDirectory(sourceDir);
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void ManifestService_WriteManifest_PathInsideLibrary_WritesFile()
    {
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Audience", "1977-05-08 Cornell");
            Directory.CreateDirectory(albumFolderPath);

            var album = BuildTestAlbumInfo();
            var track = new TrackInfo
            {
                FilePath = Path.Combine(albumFolderPath, "track.flac"),
                FileName = "track.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test",
            };

            new ManifestService().WriteManifest(albumFolderPath, album, new List<TrackInfo> { track });

            var expectedManifest = Path.Combine(libraryRoot, "Audience", "1977-05-08 Cornell.json");
            Assert.True(File.Exists(expectedManifest), $"manifest not written at {expectedManifest}");
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void ManifestService_WriteManifest_PathOutsideLibrary_Throws()
    {
        var libraryRoot = CreateTempDir();
        var sourceParent = CreateTempDir(); // outside library
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(sourceParent, "1977-05-08 Cornell");
            Directory.CreateDirectory(albumFolderPath);

            var album = BuildTestAlbumInfo();
            var track = new TrackInfo
            {
                FilePath = Path.Combine(albumFolderPath, "track.flac"),
                FileName = "track.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test",
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => new ManifestService().WriteManifest(albumFolderPath, album, new List<TrackInfo> { track }));

            Assert.Contains("ManifestService.WriteManifest", ex.Message);
            // Manifest target path is the input folder + ".json" sibling; it must appear in the message.
            var expectedManifestPath = Path.Combine(sourceParent, "1977-05-08 Cornell.json");
            Assert.Contains(expectedManifestPath, ex.Message);
        }
        finally
        {
            SafeDeleteDirectory(sourceParent);
            SafeDeleteDirectory(libraryRoot);
        }
    }

    // ===== Helpers =====

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
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

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
