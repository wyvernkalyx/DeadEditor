using DeadEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Unit tests for <see cref="PathGuard"/>'s pure containment logic. Exercise the
/// explicit-root overload so we don't need to manipulate %APPDATA% to vary the
/// library root under test.
/// </summary>
public class PathGuardTests
{
    [Fact]
    public void EnsureWithinRoot_PathInsideRoot_DoesNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        var inside = Path.Combine(root, "Album", "track.flac");

        PathGuard.EnsureWithinRoot(root, inside, "TestService");
    }

    [Fact]
    public void EnsureWithinRoot_PathOutsideRoot_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "track.flac");

        var ex = Assert.Throws<InvalidOperationException>(
            () => PathGuard.EnsureWithinRoot(root, outside, "TestService"));

        Assert.Contains("TestService", ex.Message);
        Assert.Contains(outside, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureWithinRoot_LibraryRootNotConfigured_Throws(string? root)
    {
        var path = Path.Combine(Path.GetTempPath(), "anywhere", "track.flac");

        var ex = Assert.Throws<InvalidOperationException>(
            () => PathGuard.EnsureWithinRoot(root, path, "TestService"));

        Assert.Contains("Library root not configured", ex.Message);
        Assert.Contains("TestService", ex.Message);
    }

    [Fact]
    public void EnsureWithinRoot_MultipleOffenders_AllListedInMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        var managed = Path.Combine(root, "Album", "track1.flac");
        var bad1 = Path.Combine(Path.GetTempPath(), "src1", "track.flac");
        var bad2 = Path.Combine(Path.GetTempPath(), "src2", "track.flac");

        var ex = Assert.Throws<InvalidOperationException>(
            () => PathGuard.EnsureWithinRoot(
                root,
                new[] { managed, bad1, bad2 },
                "TestService"));

        Assert.DoesNotContain(managed, ex.Message);
        Assert.Contains(bad1, ex.Message);
        Assert.Contains(bad2, ex.Message);
    }

    [Fact]
    public void EnsureWithinRoot_DotDotSegmentResolvingInside_DoesNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        // Path.Combine yields ...\lib_root\Album\..\Album\track.flac → resolves to ...\lib_root\Album\track.flac
        var winding = Path.Combine(root, "Album", "..", "Album", "track.flac");

        PathGuard.EnsureWithinRoot(root, winding, "TestService");
    }

    [Fact]
    public void EnsureWithinRoot_DotDotSegmentResolvingOutside_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        // Resolves to a sibling of lib_root, outside.
        var escaping = Path.Combine(root, "..", "elsewhere", "track.flac");

        Assert.Throws<InvalidOperationException>(
            () => PathGuard.EnsureWithinRoot(root, escaping, "TestService"));
    }

    [Fact]
    public void EnsureWithinRoot_SiblingDirectoryWithRootPrefix_Throws()
    {
        // Guards against the classic bug where root="C:\Lib" wrongly matches "C:\Lib2\..."
        // because StartsWith without a trailing separator accepts the sibling.
        var root = Path.Combine(Path.GetTempPath(), "lib");
        var sibling = Path.Combine(Path.GetTempPath(), "lib2", "track.flac");

        Assert.Throws<InvalidOperationException>(
            () => PathGuard.EnsureWithinRoot(root, sibling, "TestService"));
    }

    [Fact]
    public void EnsureWithinRoot_CaseInsensitiveMatch_DoesNotThrow()
    {
        // Windows filesystems are case-insensitive; the guard reflects that.
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        var insideUpper = Path.Combine(root.ToUpperInvariant(), "Album", "track.flac");

        PathGuard.EnsureWithinRoot(root, insideUpper, "TestService");
    }

    [Fact]
    public void EnsureWithinRoot_RootWithTrailingSeparator_HandledIdenticallyToWithout()
    {
        var rootNoSlash = Path.Combine(Path.GetTempPath(), "lib_root");
        var rootSlash = rootNoSlash + Path.DirectorySeparatorChar;
        var inside = Path.Combine(rootNoSlash, "Album", "track.flac");

        PathGuard.EnsureWithinRoot(rootNoSlash, inside, "TestService");
        PathGuard.EnsureWithinRoot(rootSlash, inside, "TestService");
    }

    [Fact]
    public void EnsureWithinRoot_NullPathInList_TreatedAsOffender()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");

        var ex = Assert.Throws<InvalidOperationException>(
            () => PathGuard.EnsureWithinRoot(
                root,
                new string[] { null! },
                "TestService"));

        Assert.Contains("TestService", ex.Message);
    }

    [Fact]
    public void EnsureWithinRoot_EmptyPathList_DoesNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");

        PathGuard.EnsureWithinRoot(root, new List<string>(), "TestService");
    }

    [Fact]
    public void IsPathUnderRoot_PathInsideRoot_ReturnsTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        var inside = Path.Combine(root, "Album", "track.flac");

        Assert.True(PathGuard.IsPathUnderRoot(root, inside));
    }

    [Fact]
    public void IsPathUnderRoot_PathOutsideRoot_ReturnsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "track.flac");

        Assert.False(PathGuard.IsPathUnderRoot(root, outside));
    }

    [Fact]
    public void IsPathUnderRoot_SiblingDirectoryWithRootPrefix_ReturnsFalse()
    {
        // Same trailing-separator semantics as EnsureWithinRoot — the sibling
        // "lib2" must NOT match root "lib".
        var root = Path.Combine(Path.GetTempPath(), "lib");
        var sibling = Path.Combine(Path.GetTempPath(), "lib2", "track.flac");

        Assert.False(PathGuard.IsPathUnderRoot(root, sibling));
    }

    [Theory]
    [InlineData(null, "/some/path")]
    [InlineData("", "/some/path")]
    [InlineData("   ", "/some/path")]
    [InlineData("/library", null)]
    [InlineData("/library", "")]
    [InlineData("/library", "   ")]
    public void IsPathUnderRoot_NullOrWhitespaceInputs_ReturnFalse(string? root, string? candidate)
    {
        Assert.False(PathGuard.IsPathUnderRoot(root, candidate));
    }

    [Fact]
    public void IsPathUnderRoot_CaseInsensitiveMatch_ReturnsTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), "lib_root");
        var insideUpper = Path.Combine(root.ToUpperInvariant(), "Album", "track.flac");

        Assert.True(PathGuard.IsPathUnderRoot(root, insideUpper));
    }

    [Fact]
    public void OverrideLibraryRootForTesting_RestoresPreviousValue_OnDispose()
    {
        var root1 = Path.Combine(Path.GetTempPath(), "outer_root");
        var root2 = Path.Combine(Path.GetTempPath(), "inner_root");
        var insideOuter = Path.Combine(root1, "track.flac");
        var insideInner = Path.Combine(root2, "track.flac");

        using (PathGuard.OverrideLibraryRootForTesting(root1))
        {
            // outer scope: root1 active
            PathGuard.EnsureWithinLibrary(insideOuter, "TestService");

            using (PathGuard.OverrideLibraryRootForTesting(root2))
            {
                // inner scope: root2 active
                PathGuard.EnsureWithinLibrary(insideInner, "TestService");
                Assert.Throws<InvalidOperationException>(
                    () => PathGuard.EnsureWithinLibrary(insideOuter, "TestService"));
            }

            // back to outer scope: root1 restored
            PathGuard.EnsureWithinLibrary(insideOuter, "TestService");
            Assert.Throws<InvalidOperationException>(
                () => PathGuard.EnsureWithinLibrary(insideInner, "TestService"));
        }
    }
}
