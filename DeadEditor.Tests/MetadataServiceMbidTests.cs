using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression tests for MBID write during in-place "Write Metadata".
/// Verifies that <see cref="MetadataService.WriteMetadata"/> writes
/// MUSICBRAINZ_ALBUMID to FLAC Xiph comments when
/// <see cref="AlbumInfo.MusicBrainzReleaseId"/> is supplied, and preserves
/// any pre-existing MBID tag when it is not.
///
/// Parity with <see cref="LibraryImportServiceMbidTests"/>: Commit 1 covered
/// the import-copy write path; Commit 1.6 closes the asymmetry on the
/// in-place write path used by the ImportView "Write Metadata" button.
/// </summary>
public class MetadataServiceMbidTests
{
    private const string TestMbid = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    private const string PreexistingMbid = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void WriteMetadata_WithMbidSupplied_WritesMbidToFlacXiph()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);

            var album = BuildTestAlbumInfo();
            album.MusicBrainzReleaseId = TestMbid;

            var track = new TrackInfo
            {
                FilePath = flacPath,
                FileName = "track1.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test Song",
            };

            var service = new MetadataService();
            service.WriteMetadata(album, new List<TrackInfo> { track });

            var actual = ReadXiphMbid(flacPath);
            Assert.Equal(TestMbid, actual);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void WriteMetadata_WithoutMbid_PreservesExistingMbidTag()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphMbid(flacPath, PreexistingMbid);

            var album = BuildTestAlbumInfo();
            album.MusicBrainzReleaseId = null;

            var track = new TrackInfo
            {
                FilePath = flacPath,
                FileName = "track1.flac",
                TrackNumber = 1,
                DiscNumber = 1,
                SongName = "Test Song",
            };

            var service = new MetadataService();
            service.WriteMetadata(album, new List<TrackInfo> { track });

            var actual = ReadXiphMbid(flacPath);
            Assert.Equal(PreexistingMbid, actual);
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

    /// <summary>
    /// Reads MUSICBRAINZ_ALBUMID using the same accessor pattern as
    /// LibraryGridView.ReadCustomFieldsIntoShow. We are deliberately exercising
    /// the writer's output through the reader's contract.
    /// </summary>
    private static string? ReadXiphMbid(string filePath)
    {
        using var tagFile = TagLib.File.Create(filePath, TagLib.ReadStyle.PictureLazy);
        if (tagFile is not TagLib.Flac.File flacFile) return null;
        var xiph = (TagLib.Ogg.XiphComment?)flacFile.GetTag(TagLib.TagTypes.Xiph);
        return xiph?.GetFirstField("MUSICBRAINZ_ALBUMID");
    }

    private static void WriteXiphMbid(string filePath, string mbid)
    {
        using var file = TagLib.File.Create(filePath);
        if (file is TagLib.Flac.File flacFile)
        {
            var xiph = (TagLib.Ogg.XiphComment?)flacFile.GetTag(TagLib.TagTypes.Xiph);
            xiph?.SetField("MUSICBRAINZ_ALBUMID", mbid);
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
    /// Mirrors the helper in LibraryImportServiceMbidTests; intentionally
    /// duplicated rather than extracted to keep this test self-contained.
    /// </summary>
    private static void CreateMinimalFlacFile(string path)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        {
            // FLAC signature: "fLaC"
            fs.Write(new byte[] { 0x66, 0x4C, 0x61, 0x43 }, 0, 4);

            // STREAMINFO block header (last metadata block flag + type 0 + length 34)
            fs.Write(new byte[] { 0x80, 0x00, 0x00, 0x22 }, 0, 4);

            // STREAMINFO data (34 bytes) with minimal valid values
            var streaminfo = new byte[34];
            streaminfo[0] = 0x10; streaminfo[1] = 0x00;            // min block size 4096
            streaminfo[2] = 0x10; streaminfo[3] = 0x00;            // max block size 4096
            // Sample rate 44100 packed across bytes 10..12
            streaminfo[10] = 0x0A; streaminfo[11] = 0xC4; streaminfo[12] = 0x42;
            // Total samples = 44100 (1 second), packed in lower 36 bits
            const long totalSamples = 44100;
            streaminfo[13] = (byte)((totalSamples >> 32) & 0x0F);
            streaminfo[14] = (byte)((totalSamples >> 24) & 0xFF);
            streaminfo[15] = (byte)((totalSamples >> 16) & 0xFF);
            streaminfo[16] = (byte)((totalSamples >> 8) & 0xFF);
            streaminfo[17] = (byte)(totalSamples & 0xFF);
            fs.Write(streaminfo, 0, 34);

            // Minimal frame header + padding so the file isn't degenerate
            fs.Write(new byte[] { 0xFF, 0xF8, 0x69, 0x04 }, 0, 4);
            fs.Write(new byte[100], 0, 100);
        }

        // Touch the file with TagLib to ensure it's valid for subsequent operations.
        using var file = TagLib.File.Create(path);
        file.Save();
    }
}
