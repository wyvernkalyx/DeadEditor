using SetlistFetcher;

namespace DeadEditor.Tests;

/// <summary>
/// Covers the eight Phase A cases for the SetlistFetcher arg parser guard
/// (concert-verification-spec.md follow-up; closes the 2026-06-10 silent-misfire
/// where a missing value swallowed the next flag as its value).
/// </summary>
public class ArgParserTests
{
    // 1. --output X --concerts Y -> both set
    [Fact]
    public void OutputAndConcerts_BothSet()
    {
        var result = ArgParser.Parse(new[] { "--output", "X", "--concerts", "Y" });
        Assert.Equal("X", result.DataDir);
        Assert.Equal("Y", result.ConcertsDir);
    }

    // 2. --output ./Data -> dataDir set, concertsDir null
    [Fact]
    public void OutputOnly_DataDirSet_ConcertsNull()
    {
        var result = ArgParser.Parse(new[] { "--output", "./Data" });
        Assert.Equal("./Data", result.DataDir);
        Assert.Null(result.ConcertsDir);
    }

    // 3. --concerts --output -> rejected (value is a flag)
    [Fact]
    public void ConcertsValueIsFlag_Rejected()
    {
        var ex = Assert.Throws<ArgParserException>(
            () => ArgParser.Parse(new[] { "--concerts", "--output" }));
        Assert.Equal("flag '--concerts' expects a path value but got '--output'", ex.Message);
    }

    // 4. --concerts (trailing) -> rejected (missing value)
    [Fact]
    public void ConcertsTrailing_MissingValue_Rejected()
    {
        var ex = Assert.Throws<ArgParserException>(
            () => ArgParser.Parse(new[] { "--concerts" }));
        Assert.Equal("flag '--concerts' expects a path value but got none", ex.Message);
    }

    // 5. --output (trailing) -> rejected (missing value)
    [Fact]
    public void OutputTrailing_MissingValue_Rejected()
    {
        var ex = Assert.Throws<ArgParserException>(
            () => ArgParser.Parse(new[] { "--output" }));
        Assert.Equal("flag '--output' expects a path value but got none", ex.Message);
    }

    // 6. --output --concerts /path -> rejected (no swallow)
    [Fact]
    public void OutputFollowedByConcerts_NoSwallow_Rejected()
    {
        var ex = Assert.Throws<ArgParserException>(
            () => ArgParser.Parse(new[] { "--output", "--concerts", "/path" }));
        Assert.Equal("flag '--output' expects a path value but got '--concerts'", ex.Message);
    }

    // 7. [] -> valid, both null
    [Fact]
    public void EmptyArgs_BothNull()
    {
        var result = ArgParser.Parse(Array.Empty<string>());
        Assert.Null(result.DataDir);
        Assert.Null(result.ConcertsDir);
    }

    // 8. --date X -> rejected (unknown argument)
    [Fact]
    public void UnknownArgument_Rejected()
    {
        var ex = Assert.Throws<ArgParserException>(
            () => ArgParser.Parse(new[] { "--date", "X" }));
        Assert.Equal("unknown argument '--date'", ex.Message);
    }
}
