using DeadEditor.Models;
using DeadEditor.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Integration coverage for the manifest sidecar written at the end of
/// <see cref="LibraryImportService.ImportToLibrary"/>. The key assertion is that
/// the manifest's <c>filename</c> fields point at the library-side filenames
/// (post-rename), not the source filenames — without the FilePath mutate/restore
/// in the manifest-write epilogue, the EditMetadataView read-back would silently
/// fail to apply track-level overrides.
/// </summary>
public class LibraryImportServiceManifestTests
{
    [Fact]
    public void ImportToLibrary_WritesManifestWithLibrarySideFilenames()
    {
        var sourceDir = CreateTempDir();
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            // Source file uses a generic name; the import will rename it to
            // "01 - Morning Dew (1971-02-19).flac".
            var sourceFlac = Path.Combine(sourceDir, "track01.flac");
            CreateMinimalFlacFile(sourceFlac);

            var album = new AlbumInfo
            {
                Artist = "Grateful Dead",
                AlbumDate = "1971-02-19",
                Venue = "Capitol Theatre",
                CityState = "Port Chester, NY",
                AlbumName = "",
                Year = "1971",
                Type = AlbumType.AudienceRecording,
            };
            var track = new TrackInfo
            {
                FilePath = sourceFlac,
                FileName = "track01.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Morning Dew",
            };

            var service = new LibraryImportService(new MetadataService(), CreateOfflineMusicBrainzService());
            service.ImportToLibrary(libraryRoot, album, new List<TrackInfo> { track });

            // The library-side filename per ComputeLibraryFilename for an
            // AudienceRecording: "{TrackNumber:D2} - {SongName} ({date}){ext}"
            const string expectedLibraryFilename = "01 - Morning Dew (1971-02-19).flac";

            // Sidecar manifest lives next to the album folder, named {AlbumFolder}.json.
            var manifestPath = FindManifestFile(libraryRoot);
            Assert.NotNull(manifestPath);

            var manifestJson = JObject.Parse(File.ReadAllText(manifestPath!));
            Assert.Equal(2, manifestJson["version"]!.Value<int>());
            Assert.False(manifestJson["verified"]!.Value<bool>());
            Assert.Equal("", manifestJson["archivistNote"]!.Value<string>());

            var tracks = (JArray)manifestJson["tracks"]!;
            Assert.Single(tracks);

            var manifestTrack = (JObject)tracks[0]!;
            Assert.Equal(expectedLibraryFilename, manifestTrack["filename"]!.Value<string>());
            Assert.NotEqual("track01.flac", manifestTrack["filename"]!.Value<string>());

            // The track passed in retained its source FilePath after import (the
            // mutate/restore in WriteManifestAfterImport is unconditional).
            Assert.Equal(sourceFlac, track.FilePath);
        }
        finally
        {
            SafeDeleteDirectory(sourceDir);
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void ImportToLibrary_WritesManifestWithOfficialReleaseFilenameFormat()
    {
        var sourceDir = CreateTempDir();
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var sourceFlac = Path.Combine(sourceDir, "track01.flac");
            CreateMinimalFlacFile(sourceFlac);

            var album = new AlbumInfo
            {
                Artist = "Grateful Dead",
                AlbumDate = "",
                Venue = "",
                CityState = "",
                AlbumName = "American Beauty",
                Year = "1970",
                Type = AlbumType.OfficialRelease,
            };
            var track = new TrackInfo
            {
                FilePath = sourceFlac,
                FileName = "track01.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Box of Rain",
            };

            var service = new LibraryImportService(new MetadataService(), CreateOfflineMusicBrainzService());
            service.ImportToLibrary(libraryRoot, album, new List<TrackInfo> { track });

            // OfficialRelease format omits the date suffix.
            const string expectedLibraryFilename = "01 - Box of Rain.flac";

            var manifestPath = FindManifestFile(libraryRoot);
            Assert.NotNull(manifestPath);

            var manifestJson = JObject.Parse(File.ReadAllText(manifestPath!));
            var tracks = (JArray)manifestJson["tracks"]!;
            Assert.Single(tracks);
            Assert.Equal(expectedLibraryFilename, ((JObject)tracks[0]!)["filename"]!.Value<string>());
        }
        finally
        {
            SafeDeleteDirectory(sourceDir);
            SafeDeleteDirectory(libraryRoot);
        }
    }

    // ===== Helpers (mirroring LibraryImportServiceFingerprintTests) =====

    private static MusicBrainzService CreateOfflineMusicBrainzService()
        => new MusicBrainzService("test-key", new LibrarySettings());

    private static string? FindManifestFile(string libraryRoot)
    {
        var files = Directory.GetFiles(libraryRoot, "*.json", SearchOption.AllDirectories);
        return files.FirstOrDefault();
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
