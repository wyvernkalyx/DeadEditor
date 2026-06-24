using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression tests for ACOUSTID_FINGERPRINT write during import. Verifies that
/// <see cref="LibraryImportService"/> writes the cached <see cref="TrackInfo.AcoustIdFingerprint"/>
/// to FLAC Xiph comments when supplied, and preserves any pre-existing fingerprint tag when
/// the in-memory value is empty (matching the MBID write-or-preserve pattern from Commit 1).
///
/// fpcalc-driven end-to-end fingerprint computation is intentionally NOT exercised here —
/// the minimal FLAC fixture has no decodable audio frames. With no FpcalcPath configured,
/// the fingerprint pre-step takes the "fpcalc not configured" path.
/// </summary>
public class LibraryImportServiceFingerprintTests
{
    private const string TestFingerprint = "AQADtFRSXcS-MUz1nZHk6_jM50dPJI3qPM-ho-mD7Eum6Yt-9MWP9MGPHj1y9MfJOEd_Iv2Q9Mhx9EePHkdy9OmTPMd_PHmO_PiR8seRP9HxoaeS9MeJPshxJEf6IH3Q4z967IePPUePoz-S5_jR50d_5MOPI3l-fOidI3lyJDfyHk2OfsiPHE-OHs9R9DiOI8eR48iRH8mTJ8eR48iPI8mPHsfx40iSI8mP5MeR48jx5DiOIzny40hyJEd-HMlxJD-O5DhyHEdyHMmRHEdyJEdyHEdyJMdxJEdyHEdyHMmRHMdxJEdyHEdyJMdxJEdyHMmRHMdx";
    private const string PreexistingFingerprint = "AQADtBpZ8axBhXJzNH1ENcTRdNRTNB1qPYjUJTuOPMnRJEeOPP2RPMdxJEf6oXmO_kj-INKR9OOR_DiOPjmO_kgenkme40hyJEd-JEeOJDmO5MdxHMdxHEdyHEdyHMlxJEdyJMeRHMlxHMdxJEdyHMdxJEdyHMlxHMdxHMdxHMdxJMdxHMdxHEdyHMdxHEdyHMdxJMdxJEdyHMdxJMeRHMdxJMlxHMdxHMdxHEdyHMdxHEdyJMdxHMdxJEdy";

    [Fact]
    public void ImportToLibrary_WithFingerprintSupplied_WritesFingerprintToFlacXiph()
    {
        var sourceDir = CreateTempDir();
        var libraryRoot = CreateTempDir();

        try
        {
            var sourceFlac = Path.Combine(sourceDir, "track1.flac");
            CreateMinimalFlacFile(sourceFlac);

            var album = BuildTestAlbumInfo();
            var track = new TrackInfo
            {
                FilePath = sourceFlac,
                FileName = "track1.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test Song",
                AcoustIdFingerprint = TestFingerprint,
            };

            var service = new LibraryImportService(new MetadataService());
            service.ImportToLibrary(libraryRoot, album, new List<TrackInfo> { track });

            var importedFile = FindImportedFlacFile(libraryRoot);
            Assert.NotNull(importedFile);

            var actual = ReadXiphFingerprint(importedFile!);
            Assert.Equal(TestFingerprint, actual);
        }
        finally
        {
            SafeDeleteDirectory(sourceDir);
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void ImportToLibrary_WithoutFingerprint_PreservesExistingFingerprintTag()
    {
        var sourceDir = CreateTempDir();
        var libraryRoot = CreateTempDir();

        try
        {
            var sourceFlac = Path.Combine(sourceDir, "track1.flac");
            CreateMinimalFlacFile(sourceFlac);
            WriteXiphFingerprint(sourceFlac, PreexistingFingerprint);

            var album = BuildTestAlbumInfo();
            var track = new TrackInfo
            {
                FilePath = sourceFlac,
                FileName = "track1.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test Song",
                AcoustIdFingerprint = null,
            };

            var service = new LibraryImportService(new MetadataService());
            service.ImportToLibrary(libraryRoot, album, new List<TrackInfo> { track });

            var importedFile = FindImportedFlacFile(libraryRoot);
            Assert.NotNull(importedFile);

            var actual = ReadXiphFingerprint(importedFile!);
            Assert.Equal(PreexistingFingerprint, actual);
        }
        finally
        {
            SafeDeleteDirectory(sourceDir);
            SafeDeleteDirectory(libraryRoot);
        }
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

    private static string? FindImportedFlacFile(string libraryRoot)
    {
        var files = Directory.GetFiles(libraryRoot, "*.flac", SearchOption.AllDirectories);
        return files.Length > 0 ? files[0] : null;
    }

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

    /// <summary>
    /// Creates a minimal valid FLAC file that TagLib can open and tag.
    /// Mirrors the helper in LibraryImportServiceMbidTests.
    /// </summary>
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
