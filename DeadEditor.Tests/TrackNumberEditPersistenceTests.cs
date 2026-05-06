using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// End-to-end test for the editable # column: simulate a user edit by mutating
/// TrackNumber on the in-memory model, save via WriteMetadata, reload from
/// disk, confirm the new value persisted AND TrackCount/DiscCount remain
/// self-consistent (Commit 1's contract).
/// </summary>
public class TrackNumberEditPersistenceTests
{
    [Fact]
    public void EditTrackNumber_RoundTripsThroughDisk_AndKeepsTotalsConsistent()
    {
        var dir = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(dir);

        try
        {
            var tracks = CreateFlacTracks(dir, disc: 1, count: 5);
            var album = BuildTestAlbumInfo();
            var svc = new MetadataService();

            // Initial save: tracks numbered 1..5 on disc 1.
            svc.WriteMetadata(album, tracks);

            // Simulate user edit in the # column: change track 3 to 99.
            // Pre-Commit 2 this would be impossible (column was IsReadOnly).
            // Post-Commit 2 the model accepts the new value via the same path
            // the DataGrid binding now uses.
            var edited = tracks.First(t => t.TrackNumber == 3);
            edited.TrackNumber = 99;

            svc.WriteMetadata(album, tracks);

            // Reload from disk — the edited value must persist, and the stale
            // total written for the original 5-track disc must remain 5 (since
            // disc-1 still has 5 tracks).
            using var file = TagLib.File.Create(edited.FilePath);
            Assert.Equal((uint)99, file.Tag.Track);
            Assert.Equal((uint)5, file.Tag.TrackCount);
            Assert.Equal((uint)1, file.Tag.DiscCount);

            // Confirm the unrelated tracks didn't get clobbered.
            foreach (var t in tracks.Where(t => t != edited))
            {
                using var other = TagLib.File.Create(t.FilePath);
                Assert.Equal((uint)t.TrackNumber, other.Tag.Track);
                Assert.Equal((uint)5, other.Tag.TrackCount);
                Assert.Equal((uint)1, other.Tag.DiscCount);
            }
        }
        finally { SafeDeleteDirectory(dir); }
    }

    // ===== Helpers (mirrors MetadataServiceTotalsTests) =====

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
        catch { }
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
