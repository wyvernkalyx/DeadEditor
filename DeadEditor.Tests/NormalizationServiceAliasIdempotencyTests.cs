using System;
using System.IO;
using System.Linq;
using DeadEditor.Models;
using DeadEditor.Services;
using Newtonsoft.Json;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Follow-ups #13: <see cref="NormalizationService.AddAlias"/> is idempotent against variants.
/// A candidate that already canonicalizes to the OfficialTitle via the cascade (dash / case /
/// whitespace the parser folds before lookup) must NOT be stored — it would be a dead alias key
/// the parser never reaches at match time.
///
/// Uses the internal path-seam ctor against a throwaway temp songs.json so the accept/idempotent
/// write paths run without mutating the shipped fixture (xUnit runs test classes in parallel, so
/// the real Data/songs.json must stay untouched).
/// </summary>
public class NormalizationServiceAliasIdempotencyTests : IDisposable
{
    private readonly string _dir;
    private readonly string _songsPath;

    public NormalizationServiceAliasIdempotencyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "DeadEditorAliasTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _songsPath = Path.Combine(_dir, "songs.json");
        WriteDb(new SongDatabase
        {
            Artists = new()
            {
                new ArtistEntry
                {
                    Name = "Test Artist",
                    Songs = new()
                    {
                        new SongEntry { OfficialTitle = "Peggy-O", Aliases = new() { "Peggy O" } },
                        new SongEntry { OfficialTitle = "Dancing in the Street", Aliases = new() }
                    }
                }
            }
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private void WriteDb(SongDatabase db) =>
        File.WriteAllText(_songsPath, JsonConvert.SerializeObject(db, Formatting.Indented));

    private List<string> AliasesOf(string officialTitle)
    {
        var db = JsonConvert.DeserializeObject<SongDatabase>(File.ReadAllText(_songsPath))!;
        var song = db.Artists.SelectMany(a => a.Songs)
                             .First(s => s.OfficialTitle == officialTitle);
        return song.Aliases ?? new List<string>();
    }

    [Theory]
    [InlineData("Peggy-O", "Peggy─O")]                       // BOX DRAWINGS LIGHT HORIZONTAL -> hyphen
    [InlineData("Peggy-O", "Peggy–O")]                       // EN DASH -> hyphen
    [InlineData("Peggy-O", "peggy o")]                            // case + space variant of the "Peggy O" alias
    [InlineData("Dancing in the Street", "Dancing in the Street")] // NBSP whitespace
    public void AddAlias_VariantThatCanonicalizes_IsRejectedAndNotStored(string official, string variant)
    {
        var svc = new NormalizationService(_songsPath);
        var before = AliasesOf(official).Count;

        Assert.False(svc.AddAlias(official, variant));
        Assert.Equal(before, AliasesOf(official).Count); // nothing appended
    }

    [Fact]
    public void AddAlias_NovelTitle_IsAppendedOnceAndBecomesLive()
    {
        var svc = new NormalizationService(_songsPath);

        Assert.Null(svc.Normalize("Fennario"));                  // precondition: unknown
        Assert.True(svc.AddAlias("Peggy-O", "Fennario"));        // accepted

        Assert.Contains("Fennario", AliasesOf("Peggy-O"));
        Assert.Equal("Peggy-O", svc.Normalize("Fennario"));      // now resolves via the new alias
    }

    [Fact]
    public void AddAlias_SameNovelTitleTwice_AppendsOnlyOnce()
    {
        var svc = new NormalizationService(_songsPath);
        var before = AliasesOf("Peggy-O").Count;

        Assert.True(svc.AddAlias("Peggy-O", "Fennario"));        // first add
        Assert.False(svc.AddAlias("Peggy-O", "Fennario"));       // second now canonicalizes -> rejected

        Assert.Equal(before + 1, AliasesOf("Peggy-O").Count);    // exactly one appended
    }
}
