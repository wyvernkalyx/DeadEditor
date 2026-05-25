using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="BoxSetService.DeriveSlug"/> — a pure, deterministic
/// function. The Read/List/Write/Delete round-trip is intentionally not tested here:
/// <c>BoxSetService</c> writes to a hardcoded %APPDATA% path that is not injectable,
/// and the service is deliberately not refactored for testability (see Phase B
/// commit 1 brief). Exercising those would write to the real user AppData directory.
/// </summary>
public class BoxSetServiceTests
{
    [Theory]
    [InlineData("Listen to the River: St. Louis '71 '72 '73", "listen-to-the-river-st-louis-71-72-73")]
    [InlineData("", "untitled")]
    [InlineData("!@#$%^&*()", "untitled")]
    [InlineData("  Dave's Picks  ", "dave-s-picks")]
    [InlineData("Already-Slugged-123", "already-slugged-123")]
    public void DeriveSlug_ProducesExpectedSlug(string input, string expected)
    {
        Assert.Equal(expected, BoxSetService.DeriveSlug(input));
    }
}
