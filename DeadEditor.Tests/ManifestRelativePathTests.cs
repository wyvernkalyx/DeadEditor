using DeadEditor.Models;
using DeadEditor.Services;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Unit coverage for Fix B: the composite manifest key
/// (<see cref="ManifestService.ComputeManifestRelativePath"/>) and the duplicate-tolerant override
/// resolver (<see cref="ManifestService.BuildOverrideResolver"/>). The resolver prefers the composite
/// "{immediateFolderName}/{filename}" (exact for multi-folder/merged albums) and falls back to bare
/// Filename for legacy manifests, never throwing on a legacy bare-name collision.
/// </summary>
public class ManifestRelativePathTests
{
    private static ManifestTrack Row(string filename, string relativePath, string songName) => new()
    {
        Filename = filename,
        RelativePath = relativePath,
        SongName = songName,
    };

    // ===== ComputeManifestRelativePath =====

    [Fact]
    public void Compute_SingleFolder_ReturnsFolderSlashFile()
    {
        var path = Path.Combine("C:\\lib", "Grateful Dead", "AlbumX", "05 - Drums.flac");
        Assert.Equal("AlbumX/05 - Drums.flac", ManifestService.ComputeManifestRelativePath(path));
    }

    [Fact]
    public void Compute_SameFilenameDifferentFolders_ProducesDistinctComposites()
    {
        var inA = Path.Combine("C:\\lib", "Artist", "Disc A", "Drums.flac");
        var inB = Path.Combine("C:\\lib", "Artist", "Disc B", "Drums.flac");

        var a = ManifestService.ComputeManifestRelativePath(inA);
        var b = ManifestService.ComputeManifestRelativePath(inB);

        Assert.Equal("Disc A/Drums.flac", a);
        Assert.Equal("Disc B/Drums.flac", b);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", ManifestService.ComputeManifestRelativePath(null!));
        Assert.Equal("", ManifestService.ComputeManifestRelativePath(""));
    }

    // ===== BuildOverrideResolver =====

    [Fact]
    public void Resolver_NewManifest_SingleFolder_ResolvesByComposite()
    {
        var manifest = new AlbumManifest
        {
            Tracks = new List<ManifestTrack>
            {
                Row("01 - Bertha.flac", "AlbumX/01 - Bertha.flac", "Bertha"),
                Row("05 - Drums.flac", "AlbumX/05 - Drums.flac", "Drums"),
            }
        };
        var resolve = ManifestService.BuildOverrideResolver(manifest);

        var hit = resolve(Path.Combine("C:\\lib", "Artist", "AlbumX", "05 - Drums.flac"));
        Assert.NotNull(hit);
        Assert.Equal("Drums", hit!.SongName);
    }

    [Fact]
    public void Resolver_MultiFolder_SameBareName_EachResolvesToItsOwnRow()
    {
        // The key correctness test: two folders each have "Drums.flac"; without the composite the
        // bare-name key collides and applies the wrong folder's overrides (or crashes).
        var manifest = new AlbumManifest
        {
            Tracks = new List<ManifestTrack>
            {
                Row("Drums.flac", "Disc A/Drums.flac", "Drums-A"),
                Row("Drums.flac", "Disc B/Drums.flac", "Drums-B"),
            }
        };
        var resolve = ManifestService.BuildOverrideResolver(manifest);

        var inA = resolve(Path.Combine("C:\\lib", "Artist", "Disc A", "Drums.flac"));
        var inB = resolve(Path.Combine("C:\\lib", "Artist", "Disc B", "Drums.flac"));

        Assert.NotNull(inA);
        Assert.NotNull(inB);
        Assert.Equal("Drums-A", inA!.SongName);
        Assert.Equal("Drums-B", inB!.SongName);
    }

    [Fact]
    public void Resolver_LegacyManifest_SingleFolder_ResolvesByBareFilenameFallback()
    {
        // Legacy: no RelativePath. Single folder -> bare filenames unique -> exact fallback.
        var manifest = new AlbumManifest
        {
            Tracks = new List<ManifestTrack>
            {
                Row("01 - Bertha.flac", "", "Bertha"),
                Row("05 - Drums.flac", "", "Drums"),
            }
        };
        var resolve = ManifestService.BuildOverrideResolver(manifest);

        var hit = resolve(Path.Combine("C:\\lib", "Artist", "AlbumX", "01 - Bertha.flac"));
        Assert.NotNull(hit);
        Assert.Equal("Bertha", hit!.SongName);
    }

    [Fact]
    public void Resolver_LegacyMultiFolder_DuplicateBareName_DoesNotThrow_ReturnsARow()
    {
        // Legacy multi-folder duplicate (the old ToDictionary crash). Group-and-first must tolerate
        // it: no throw, returns the first matching row. Self-heals on next save (writes RelativePath).
        var manifest = new AlbumManifest
        {
            Tracks = new List<ManifestTrack>
            {
                Row("Drums.flac", "", "Drums-first"),
                Row("Drums.flac", "", "Drums-second"),
            }
        };

        var resolve = ManifestService.BuildOverrideResolver(manifest); // must not throw

        var hit = resolve(Path.Combine("C:\\lib", "Artist", "Disc A", "Drums.flac"));
        Assert.NotNull(hit);
        Assert.Equal("Drums-first", hit!.SongName);
    }
}
