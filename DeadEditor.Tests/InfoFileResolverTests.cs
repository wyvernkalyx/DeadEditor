using DeadEditor.Helpers;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="InfoFileResolver.ResolveFirstTextFile"/>, the pure "first .txt wins"
/// seam behind the Edit-side View Info affordance (reference-side-panel-spec.md §8). Mirrors
/// Import's <c>Directory.GetFiles(folder, "*.txt")[0]</c>: first .txt in enumeration order,
/// case-insensitive extension, null when none.
/// </summary>
public class InfoFileResolverTests
{
    [Fact]
    public void ReturnsFirstTxt_InEnumerationOrder()
    {
        var files = new[]
        {
            @"C:\show\track01.flac",
            @"C:\show\notes.txt",
            @"C:\show\second.txt",
        };

        Assert.Equal(@"C:\show\notes.txt", InfoFileResolver.ResolveFirstTextFile(files));
    }

    [Fact]
    public void MatchesExtensionCaseInsensitively()
    {
        var files = new[] { @"C:\show\cover.jpg", @"C:\show\INFO.TXT" };

        Assert.Equal(@"C:\show\INFO.TXT", InfoFileResolver.ResolveFirstTextFile(files));
    }

    [Fact]
    public void ReturnsNull_WhenNoTxtPresent()
    {
        var files = new[] { @"C:\show\track01.flac", @"C:\show\cover.jpg" };

        Assert.Null(InfoFileResolver.ResolveFirstTextFile(files));
    }

    [Fact]
    public void ReturnsNull_ForEmptyList()
    {
        Assert.Null(InfoFileResolver.ResolveFirstTextFile(new string[0]));
    }

    [Fact]
    public void ReturnsNull_ForNull()
    {
        Assert.Null(InfoFileResolver.ResolveFirstTextFile(null));
    }

    [Fact]
    public void SkipsNullAndEmptyEntries()
    {
        var files = new[] { "", null!, @"C:\show\liner.txt" };

        Assert.Equal(@"C:\show\liner.txt", InfoFileResolver.ResolveFirstTextFile(files));
    }

    [Fact]
    public void DoesNotMatchTxtSubstringWithoutExtension()
    {
        // A name containing "txt" but not ending in ".txt" must not match.
        var files = new[] { @"C:\show\txt-readme.doc", @"C:\show\about.text" };

        Assert.Null(InfoFileResolver.ResolveFirstTextFile(files));
    }
}
