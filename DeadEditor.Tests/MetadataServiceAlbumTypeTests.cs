using DeadEditor.Models;
using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Regression tests for the load-time signal that drives album-type inference:
/// <see cref="MetadataService.ReadAlbumInfo"/>'s <c>typeFromTag</c> out parameter.
/// Pins the contract that lets ImportView decide whether the source's ALBUMTYPE
/// tag is authoritative or whether to fall back to the inference heuristic.
/// </summary>
public class MetadataServiceAlbumTypeTests
{
    [Fact]
    public void ReadAlbumInfo_WithAlbumTypeTag_ReportsTypeFromTagTrue()
    {
        var dir = CreateTempDir();
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphFields(flacPath, new Dictionary<string, string>
            {
                { "ALBUMDATE", "1977-05-08" },
                { "VENUE", "Barton Hall" },
                { "CITYSTATE", "Ithaca, NY" },
                { "ALBUMNAME", "" },
                { "ALBUMTYPE", "AudienceRecording" },
            });

            var tracks = new List<TrackInfo>
            {
                new TrackInfo { FilePath = flacPath, FileName = "track1.flac" },
            };

            var service = new MetadataService();
            var info = service.ReadAlbumInfo(dir, tracks, out var typeFromTag);

            Assert.True(typeFromTag);
            Assert.Equal(AlbumType.AudienceRecording, info.Type);
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void ReadAlbumInfo_WithOfficialReleaseAlbumTypeTag_ReportsTypeFromTagTrue()
    {
        var dir = CreateTempDir();
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphFields(flacPath, new Dictionary<string, string>
            {
                { "ALBUMDATE", "1972-09-15" },
                { "VENUE", "Boston Music Hall" },
                { "CITYSTATE", "Boston, MA" },
                { "ALBUMNAME", "Dave's Picks Vol. 1" },
                { "ALBUMTYPE", "OfficialRelease" },
            });

            var tracks = new List<TrackInfo>
            {
                new TrackInfo { FilePath = flacPath, FileName = "track1.flac" },
            };

            var service = new MetadataService();
            var info = service.ReadAlbumInfo(dir, tracks, out var typeFromTag);

            Assert.True(typeFromTag);
            Assert.Equal(AlbumType.OfficialRelease, info.Type);
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void ReadAlbumInfo_WithoutAlbumTypeTag_ReportsTypeFromTagFalse()
    {
        var dir = CreateTempDir();
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            // Write album fields but NO ALBUMTYPE — simulates a source folder
            // tagged by another tool.
            WriteXiphFields(flacPath, new Dictionary<string, string>
            {
                { "ALBUMDATE", "1977-05-08" },
                { "VENUE", "Barton Hall" },
                { "CITYSTATE", "Ithaca, NY" },
            });

            var tracks = new List<TrackInfo>
            {
                new TrackInfo { FilePath = flacPath, FileName = "track1.flac" },
            };

            var service = new MetadataService();
            _ = service.ReadAlbumInfo(dir, tracks, out var typeFromTag);

            Assert.False(typeFromTag);
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void ReadAlbumInfo_WithUnparseableAlbumType_ReportsTypeFromTagFalse()
    {
        var dir = CreateTempDir();
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphFields(flacPath, new Dictionary<string, string>
            {
                { "ALBUMDATE", "1977-05-08" },
                { "VENUE", "Barton Hall" },
                { "CITYSTATE", "Ithaca, NY" },
                { "ALBUMTYPE", "Garbage" },
            });

            var tracks = new List<TrackInfo>
            {
                new TrackInfo { FilePath = flacPath, FileName = "track1.flac" },
            };

            var service = new MetadataService();
            _ = service.ReadAlbumInfo(dir, tracks, out var typeFromTag);

            Assert.False(typeFromTag);
        }
        finally { SafeDeleteDirectory(dir); }
    }

    [Fact]
    public void ReadAlbumInfo_BackCompatOverload_StillWorks()
    {
        var dir = CreateTempDir();
        try
        {
            var flacPath = Path.Combine(dir, "track1.flac");
            CreateMinimalFlacFile(flacPath);
            WriteXiphFields(flacPath, new Dictionary<string, string>
            {
                { "ALBUMDATE", "1977-05-08" },
                { "VENUE", "Barton Hall" },
                { "CITYSTATE", "Ithaca, NY" },
                { "ALBUMTYPE", "AudienceRecording" },
            });

            var tracks = new List<TrackInfo>
            {
                new TrackInfo { FilePath = flacPath, FileName = "track1.flac" },
            };

            var service = new MetadataService();
            var info = service.ReadAlbumInfo(dir, tracks);

            Assert.Equal(AlbumType.AudienceRecording, info.Type);
            Assert.Equal("Barton Hall", info.Venue);
        }
        finally { SafeDeleteDirectory(dir); }
    }

    private static void WriteXiphFields(string filePath, Dictionary<string, string> fields)
    {
        using var file = TagLib.File.Create(filePath);
        if (file is TagLib.Flac.File flacFile)
        {
            var xiph = (TagLib.Ogg.XiphComment?)flacFile.GetTag(TagLib.TagTypes.Xiph);
            if (xiph != null)
            {
                foreach (var kv in fields)
                {
                    xiph.SetField(kv.Key, kv.Value);
                }
                file.Save();
            }
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
