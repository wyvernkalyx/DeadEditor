using DeadEditor.Models;
using DeadEditor.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Schema and round-trip coverage for <see cref="ManifestService"/>. The
/// PathGuard happy-path and rejection-path tests live in
/// <see cref="WriteServiceGuardTests"/>; these tests focus on v2 schema
/// shape, camelCase JSON keys, and v1 → v2 backward-compatible reads.
/// </summary>
public class ManifestServiceTests
{
    [Fact]
    public void WriteManifest_RoundTripV2_PreservesEveryField()
    {
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Grateful Dead", "Grateful Dead - 1971-02-19 - Capitol Theatre - Port Chester, NY");
            Directory.CreateDirectory(albumFolderPath);

            var album = new AlbumInfo
            {
                Artist = "Grateful Dead",
                AlbumDate = "1971-02-19",
                Venue = "Capitol Theatre",
                CityState = "Port Chester, NY",
                AlbumName = "",
                Year = "1971",
                Edition = "",
                Type = AlbumType.AudienceRecording,
            };

            var tracks = new List<TrackInfo>
            {
                new()
                {
                    FilePath = Path.Combine(albumFolderPath, "01 - Morning Dew (1971-02-19).flac"),
                    TrackNumber = 1,
                    DiscNumber = 1,
                    Title = "Morning Dew (1971-02-19)",
                    SongName = "Morning Dew",
                    TrackDate = "1971-02-19",
                    Segue = false,
                    AcoustIdFingerprint = "AQADtMmSRJGSxxxxFingerprintOne",
                },
                new()
                {
                    FilePath = Path.Combine(albumFolderPath, "02 - Dark Star > (1971-02-19).flac"),
                    TrackNumber = 2,
                    DiscNumber = 1,
                    Title = "Dark Star > (1971-02-19)",
                    SongName = "Dark Star",
                    TrackDate = "1971-02-19",
                    Segue = true,
                    AcoustIdFingerprint = "AQADtMmSRJGSxxxxFingerprintTwo",
                },
            };

            var service = new ManifestService();
            service.WriteManifest(albumFolderPath, album, tracks);

            var roundTripped = service.ReadManifest(albumFolderPath);
            Assert.NotNull(roundTripped);

            Assert.Equal(2, roundTripped!.Version);
            Assert.Equal("Grateful Dead - 1971-02-19 - Capitol Theatre - Port Chester, NY", roundTripped.FolderName);
            Assert.Equal("", roundTripped.AlbumName);
            Assert.Equal("AudienceRecording", roundTripped.AlbumType);
            Assert.Equal("Grateful Dead", roundTripped.Artist);
            Assert.Equal("1971-02-19", roundTripped.Date);
            Assert.Equal("Capitol Theatre", roundTripped.Venue);
            Assert.Equal("Port Chester", roundTripped.City);
            Assert.Equal("NY", roundTripped.State);
            Assert.Equal("", roundTripped.Edition);
            Assert.False(roundTripped.Verified);
            Assert.Equal("", roundTripped.ArchivistNote);

            Assert.Equal(2, roundTripped.Tracks.Count);
            Assert.Equal("01 - Morning Dew (1971-02-19).flac", roundTripped.Tracks[0].Filename);
            Assert.Equal(1, roundTripped.Tracks[0].TrackNumber);
            Assert.Equal(1, roundTripped.Tracks[0].DiscNumber);
            Assert.Equal("Morning Dew (1971-02-19)", roundTripped.Tracks[0].Title);
            Assert.Equal("Morning Dew", roundTripped.Tracks[0].SongName);
            Assert.Equal("1971-02-19", roundTripped.Tracks[0].TrackDate);
            Assert.False(roundTripped.Tracks[0].Segue);
            Assert.Equal("AQADtMmSRJGSxxxxFingerprintOne", roundTripped.Tracks[0].AcoustIdFingerprint);

            Assert.True(roundTripped.Tracks[1].Segue);
            Assert.Equal("AQADtMmSRJGSxxxxFingerprintTwo", roundTripped.Tracks[1].AcoustIdFingerprint);
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void WriteManifest_VerifiedOverload_RoundTripsVerifiedAndNote()
    {
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Artist", "Album");
            Directory.CreateDirectory(albumFolderPath);

            const string note = "Sources: archive.org/12345\nOpen questions: venue uncertain";
            var service = new ManifestService();
            service.WriteManifest(
                albumFolderPath,
                BuildTestAlbumInfo(),
                new List<TrackInfo> { BuildTestTrack(albumFolderPath) },
                verified: true,
                archivistNote: note);

            var roundTripped = service.ReadManifest(albumFolderPath);
            Assert.NotNull(roundTripped);
            Assert.True(roundTripped!.Verified);
            Assert.Equal(note, roundTripped.ArchivistNote);
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void WriteManifest_DefaultOverload_WritesUnverifiedAndEmptyNote()
    {
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Artist", "Album");
            Directory.CreateDirectory(albumFolderPath);

            new ManifestService().WriteManifest(
                albumFolderPath,
                BuildTestAlbumInfo(),
                new List<TrackInfo> { BuildTestTrack(albumFolderPath) });

            var roundTripped = new ManifestService().ReadManifest(albumFolderPath);
            Assert.NotNull(roundTripped);
            Assert.False(roundTripped!.Verified);
            Assert.Equal("", roundTripped.ArchivistNote);
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void WriteManifest_WritesCamelCaseV2Json()
    {
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Artist", "Album");
            Directory.CreateDirectory(albumFolderPath);

            var album = BuildTestAlbumInfo();
            var track = BuildTestTrack(albumFolderPath, fingerprint: "AQADtFingerprint");

            new ManifestService().WriteManifest(albumFolderPath, album, new List<TrackInfo> { track });

            var manifestPath = Path.Combine(libraryRoot, "Artist", "Album.json");
            var raw = File.ReadAllText(manifestPath);
            var parsed = JObject.Parse(raw);

            Assert.Equal(2, parsed["version"]!.Value<int>());
            Assert.Equal("Album", parsed["folderName"]!.Value<string>());
            Assert.False(parsed["verified"]!.Value<bool>());
            Assert.Equal("", parsed["archivistNote"]!.Value<string>());
            Assert.NotNull(parsed["manifestSavedAt"]);
            Assert.Null(parsed["VerifiedAt"]);
            Assert.Null(parsed["verifiedAt"]);

            var trackJson = (JObject)parsed["tracks"]![0]!;
            Assert.Equal("AQADtFingerprint", trackJson["acoustIdFingerprint"]!.Value<string>());
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void WriteManifest_PopulatesManifestSavedAtToUtcNow()
    {
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Artist", "Album");
            Directory.CreateDirectory(albumFolderPath);

            var before = DateTime.UtcNow;
            new ManifestService().WriteManifest(
                albumFolderPath,
                BuildTestAlbumInfo(),
                new List<TrackInfo> { BuildTestTrack(albumFolderPath) });
            var after = DateTime.UtcNow;

            var manifest = new ManifestService().ReadManifest(albumFolderPath);
            Assert.NotNull(manifest);

            Assert.InRange(
                manifest!.ManifestSavedAt,
                before.AddSeconds(-1),
                after.AddSeconds(1));
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void WriteManifest_EmptyFingerprintAndArchivistNote_RoundTripAsEmptyStringNotNull()
    {
        var libraryRoot = CreateTempDir();
        using var _ = PathGuard.OverrideLibraryRootForTesting(libraryRoot);

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Artist", "Album");
            Directory.CreateDirectory(albumFolderPath);

            var album = BuildTestAlbumInfo();
            var trackNullFp = BuildTestTrack(albumFolderPath, fingerprint: null);

            new ManifestService().WriteManifest(albumFolderPath, album, new List<TrackInfo> { trackNullFp });

            var manifestPath = Path.Combine(libraryRoot, "Artist", "Album.json");
            var raw = File.ReadAllText(manifestPath);
            var parsed = JObject.Parse(raw);

            Assert.Equal("", parsed["archivistNote"]!.Value<string>());
            var trackJson = (JObject)parsed["tracks"]![0]!;
            Assert.Equal("", trackJson["acoustIdFingerprint"]!.Value<string>());

            var roundTripped = new ManifestService().ReadManifest(albumFolderPath);
            Assert.NotNull(roundTripped);
            Assert.Equal("", roundTripped!.ArchivistNote);
            Assert.Equal("", roundTripped.Tracks[0].AcoustIdFingerprint);
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void ReadManifest_V1CamelCase_MapsVerifiedAtToManifestSavedAt()
    {
        var libraryRoot = CreateTempDir();

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Artist", "Album");
            Directory.CreateDirectory(albumFolderPath);

            var manifestPath = Path.Combine(libraryRoot, "Artist", "Album.json");
            File.WriteAllText(manifestPath, V1ManifestJsonCamelCase);

            var manifest = new ManifestService().ReadManifest(albumFolderPath);

            Assert.NotNull(manifest);
            Assert.Equal(1, manifest!.Version);
            Assert.Equal("Album", manifest.FolderName);
            Assert.Equal("Test Artist", manifest.Artist);
            Assert.False(manifest.Verified);
            Assert.Equal("", manifest.ArchivistNote);
            Assert.Equal(
                new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc),
                manifest.ManifestSavedAt.ToUniversalTime());
            Assert.Single(manifest.Tracks);
            Assert.Equal("Morning Dew", manifest.Tracks[0].SongName);
            Assert.Equal("", manifest.Tracks[0].AcoustIdFingerprint);
        }
        finally
        {
            SafeDeleteDirectory(libraryRoot);
        }
    }

    [Fact]
    public void ReadManifest_V1PascalCase_StillReadsViaCaseInsensitivity()
    {
        var libraryRoot = CreateTempDir();

        try
        {
            var albumFolderPath = Path.Combine(libraryRoot, "Artist", "Album");
            Directory.CreateDirectory(albumFolderPath);

            var manifestPath = Path.Combine(libraryRoot, "Artist", "Album.json");
            File.WriteAllText(manifestPath, V1ManifestJsonPascalCase);

            var manifest = new ManifestService().ReadManifest(albumFolderPath);

            Assert.NotNull(manifest);
            Assert.Equal(1, manifest!.Version);
            Assert.Equal("Album", manifest.FolderName);
            Assert.Equal("Test Artist", manifest.Artist);
            Assert.Equal("Test Venue", manifest.Venue);
            Assert.False(manifest.Verified);
            Assert.Equal("", manifest.ArchivistNote);
            Assert.Equal(
                new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc),
                manifest.ManifestSavedAt.ToUniversalTime());
            Assert.Single(manifest.Tracks);
            Assert.Equal("Morning Dew", manifest.Tracks[0].SongName);
            Assert.Equal("", manifest.Tracks[0].AcoustIdFingerprint);
        }
        finally
        {
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
        Edition = "",
        Type = AlbumType.AudienceRecording,
    };

    private static TrackInfo BuildTestTrack(string albumFolderPath, string? fingerprint = "")
    {
        return new TrackInfo
        {
            FilePath = Path.Combine(albumFolderPath, "01 - Test.flac"),
            TrackNumber = 1,
            DiscNumber = 1,
            Title = "Test",
            SongName = "Test",
            TrackDate = "2026-01-01",
            Segue = false,
            AcoustIdFingerprint = fingerprint,
        };
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deadeditor_manifest_test_{Guid.NewGuid()}");
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

    private const string V1ManifestJsonCamelCase = @"{
  ""version"": 1,
  ""folderName"": ""Album"",
  ""albumName"": """",
  ""albumType"": ""AudienceRecording"",
  ""artist"": ""Test Artist"",
  ""date"": ""2026-01-01"",
  ""venue"": ""Test Venue"",
  ""city"": ""Test City"",
  ""state"": ""TS"",
  ""edition"": """",
  ""officialRelease"": """",
  ""verifiedAt"": ""2026-04-04T12:00:00Z"",
  ""tracks"": [
    {
      ""filename"": ""01 - Morning Dew.flac"",
      ""trackNumber"": 1,
      ""discNumber"": 1,
      ""title"": ""Morning Dew"",
      ""songName"": ""Morning Dew"",
      ""trackDate"": ""2026-01-01"",
      ""segue"": false
    }
  ]
}";

    private const string V1ManifestJsonPascalCase = @"{
  ""Version"": 1,
  ""FolderName"": ""Album"",
  ""AlbumName"": """",
  ""AlbumType"": ""AudienceRecording"",
  ""Artist"": ""Test Artist"",
  ""Date"": ""2026-01-01"",
  ""Venue"": ""Test Venue"",
  ""City"": ""Test City"",
  ""State"": ""TS"",
  ""Edition"": """",
  ""OfficialRelease"": """",
  ""VerifiedAt"": ""2026-04-04T12:00:00Z"",
  ""Tracks"": [
    {
      ""Filename"": ""01 - Morning Dew.flac"",
      ""TrackNumber"": 1,
      ""DiscNumber"": 1,
      ""Title"": ""Morning Dew"",
      ""SongName"": ""Morning Dew"",
      ""TrackDate"": ""2026-01-01"",
      ""Segue"": false
    }
  ]
}";
}
