using DeadEditor.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Unit coverage for <see cref="LibraryImportService.ResolveImportTargetName"/>, the
/// data-loss-critical resolver behind Fix A's audio-copy collision guard. The first use of a
/// derived name within an import keeps the name (a pre-existing file is overwritten — idempotent
/// re-import); a second use within the same import is two distinct tracks deriving the same
/// "{TrackNumber:D2} - {SongName}", which must rename to "(2)" rather than silently overwrite.
/// Existence is injected, so these tests touch no file system.
/// </summary>
public class ResolveImportTargetNameTests
{
    private static ISet<string> Batch(params string[] names)
        => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private static readonly Func<string, bool> NoFiles = _ => false;

    [Fact]
    public void FirstUse_FileDoesNotExist_ReturnsName()
    {
        var used = Batch();
        var result = LibraryImportService.ResolveImportTargetName("05 - Drums.flac", used, NoFiles);
        Assert.Equal("05 - Drums.flac", result);
    }

    [Fact]
    public void FirstUse_FileExists_ReturnsSameName_IdempotentReImport()
    {
        // No within-batch clash, but the managed file already exists from a prior import.
        // This MUST NOT rename — the caller overwrites in place (idempotent re-import).
        var used = Batch();
        var result = LibraryImportService.ResolveImportTargetName(
            "05 - Drums.flac", used, name => name == "05 - Drums.flac");
        Assert.Equal("05 - Drums.flac", result);
    }

    [Fact]
    public void SecondUseThisBatch_RenamesTo2_DataLossAverted()
    {
        var used = Batch("05 - Drums.flac");
        var result = LibraryImportService.ResolveImportTargetName("05 - Drums.flac", used, NoFiles);
        Assert.Equal("05 - Drums (2).flac", result);
    }

    [Fact]
    public void SecondUseThisBatch_When2AlreadyTaken_RenamesTo3()
    {
        // "(2)" is taken — either already used this batch or already on disk — so skip to "(3)".
        var usedHasTwo = Batch("05 - Drums.flac", "05 - Drums (2).flac");
        Assert.Equal("05 - Drums (3).flac",
            LibraryImportService.ResolveImportTargetName("05 - Drums.flac", usedHasTwo, NoFiles));

        var twoOnDisk = Batch("05 - Drums.flac");
        Assert.Equal("05 - Drums (3).flac",
            LibraryImportService.ResolveImportTargetName(
                "05 - Drums.flac", twoOnDisk, name => name == "05 - Drums (2).flac"));
    }

    [Fact]
    public void ThreeTracksSameDesiredName_InOneBatch_ProduceNameThen2Then3()
    {
        var used = Batch();
        var first = LibraryImportService.ResolveImportTargetName("05 - Drums.flac", used, NoFiles);
        used.Add(first);
        var second = LibraryImportService.ResolveImportTargetName("05 - Drums.flac", used, NoFiles);
        used.Add(second);
        var third = LibraryImportService.ResolveImportTargetName("05 - Drums.flac", used, NoFiles);
        used.Add(third);

        Assert.Equal("05 - Drums.flac", first);
        Assert.Equal("05 - Drums (2).flac", second);
        Assert.Equal("05 - Drums (3).flac", third);
    }
}
