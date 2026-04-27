using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression tests for ACOUSTID_FINGERPRINT write during in-place "Write Metadata".
/// Verifies that <see cref="MetadataService.WriteMetadata"/> writes
/// <see cref="TrackInfo.AcoustIdFingerprint"/> to FLAC Xiph comments when supplied,
/// and preserves any pre-existing fingerprint tag when the in-memory value is empty.
///
/// Parity with <see cref="LibraryImportServiceFingerprintTests"/>: the import-copy path
/// originates fingerprint values via the pre-compute step; the in-place WriteMetadata path
/// only writes whatever is already cached on TrackInfo. Both paths share the same
/// guarded write contract.
/// </summary>
public class MetadataServiceFingerprintTests
{
    private const string TestFingerprint = "AQADtFRSXcS-MUz1nZHk6_jM50dPJI3qPM-ho-mD7Eum6Yt-9MWP9MGPHj1y9MfJOEd_Iv2Q9Mhx9EePHkdy9OmTPMd_PHmO_PiR8seRP9HxoaeS9MeJPshxJEf6IH3Q4z967IePPUePoz-S5_jR50d_5MOPI3l-fOidI3lyJDfyHk2OfsiPHE-OHs9R9DiOI8eR48iRH8mTJ8eR48iPI8mPHsfx40iSI8mP5MeR48jx5DiOIzny40hyJEd-HMlxJD-O5DhyHEdyHMmRHEdyJEdyHEdyJMdxJEdyHMmRHMdx";
    private const string PreexistingFingerprint = "AQADtBpZ8axBhXJzNH1ENcTRdNRTNB1qPYjUJTuOPMnRJEeOPP2RPMdxJEf6oXmO_kj-INKR9OOR_DiOPjmO_kgenkme40hyJEd-JEeOJDmO5MdxHMdxHEdyHEdyHMlxJEdyJMeRHMlxHMdxJEdyHMdxJEdyHMlxHMdxHMdxHMdxJMdxHMdxHEdyHMdxHEdyHMdxJMdxJEdyHMdxJMeRHMdxJMlxHMdxHMdxHEdyHMdxHEdyJMdxHMdxJEdy";

    [Fact]
    public void WriteMetadata_WithFingerprintSupplied_WritesFingerprintToFlacXiph()
    {
        var dir = CreateTempDir();

        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);

            var album = BuildTestAlbumInfo();
            var track = new TrackInfo
            {
                FilePath = flacPath,
                FileName = "track1.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test Song",
                AcoustIdFingerprint = TestFingerprint,
            };

            var service = new MetadataService();
            service.WriteMetadata(album, new List<TrackInfo> { track });

            var actual = ReadXiphFingerprint(flacPath);
            Assert.Equal(TestFingerprint, actual);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void WriteMetadata_WithoutFingerprint_PreservesExistingFingerprintTag()
    {
        var dir = CreateTempDir();

        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphFingerprint(flacPath, PreexistingFingerprint);

            var album = BuildTestAlbumInfo();
            var track = new TrackInfo
            {
                FilePath = flacPath,
                FileName = "track1.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test Song",
                AcoustIdFingerprint = null,
            };

            var service = new MetadataService();
            service.WriteMetadata(album, new List<TrackInfo> { track });

            var actual = ReadXiphFingerprint(flacPath);
            Assert.Equal(PreexistingFingerprint, actual);
        }
        finally
        {
            SafeDeleteDirectory(dir);
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
    /// Mirrors the helper in MetadataServiceMbidTests.
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
