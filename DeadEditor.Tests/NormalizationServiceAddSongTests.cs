using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeadEditor.Models;
using DeadEditor.Services;
using Newtonsoft.Json;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Follow-ups (RESOLVED 2026-06-19): <see cref="NormalizationService.AddSong"/> must read fresh
/// from disk and merge-append (mirroring <see cref="NormalizationService.AddAlias"/>), NOT blind-
/// overwrite the in-memory <c>_database</c>. Each view holds its own NormalizationService whose
/// <c>_database</c> is loaded once and never refreshed; a blind overwrite clobbers aliases/songs
/// another instance wrote since this one loaded. Confirmed repro: an alias learned in the Import
/// view was lost when a song was added via the cached Settings instance.
///
/// Uses the internal path-seam ctor against a throwaway temp songs.json so the write paths run
/// without mutating the shipped fixture.
/// </summary>
public class NormalizationServiceAddSongTests : IDisposable
{
    private readonly string _dir;
    private readonly string _songsPath;

    public NormalizationServiceAddSongTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "DeadEditorAddSongTests_" + Guid.NewGuid().ToString("N"));
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

    private SongDatabase ReadDb() =>
        JsonConvert.DeserializeObject<SongDatabase>(File.ReadAllText(_songsPath))!;

    private List<SongEntry> AllSongs() =>
        ReadDb().Artists.SelectMany(a => a.Songs ?? new List<SongEntry>()).ToList();

    private List<string> AliasesOf(string officialTitle) =>
        AllSongs().First(s => s.OfficialTitle == officialTitle).Aliases ?? new List<string>();

    /// <summary>
    /// The bug: a stale instance Y adds a song while another instance X has written an alias to
    /// the same file in between. The read-fresh-merge means Y's AddSong preserves X's alias — both
    /// the out-of-band alias and the new song survive.
    /// </summary>
    [Fact]
    public void AddSong_DoesNotClobberConcurrentAliasWrittenByAnotherInstance()
    {
        // Y loads snapshot S0 (Peggy-O has only the "Peggy O" alias).
        var y = new NormalizationService(_songsPath);

        // X (a separate instance on the same file) learns a new alias -> disk now has "Fennario".
        var x = new NormalizationService(_songsPath);
        Assert.True(x.AddAlias("Peggy-O", "Fennario"));
        Assert.Contains("Fennario", AliasesOf("Peggy-O")); // disk has it; Y's in-memory copy does not

        // Y adds a brand-new song from its stale snapshot.
        y.AddSong("Brokedown Palace", null, "Test Artist");

        // Both survive: nothing was clobbered.
        Assert.Contains("Fennario", AliasesOf("Peggy-O"));
        Assert.Contains(AllSongs(), s => s.OfficialTitle == "Brokedown Palace");
    }

    [Fact]
    public void AddSong_ExistingTitle_IsNotDuplicated()
    {
        var svc = new NormalizationService(_songsPath);
        var before = AllSongs().Count;

        svc.AddSong("Peggy-O", null, "Test Artist");          // already present (case-insensitive)
        svc.AddSong("peggy-o", null, "Test Artist");          // case variant

        Assert.Equal(before, AllSongs().Count);               // no duplicate appended
        Assert.Single(AllSongs(), s => s.OfficialTitle.Equals("Peggy-O", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddSong_NovelSong_IsPersistedAndResolves()
    {
        var svc = new NormalizationService(_songsPath);

        Assert.Null(svc.Normalize("Sugaree"));                // precondition: unknown
        svc.AddSong("Sugaree", null, "Test Artist");

        Assert.Contains(AllSongs(), s => s.OfficialTitle == "Sugaree");
        Assert.Equal("Sugaree", svc.Normalize("Sugaree"));    // now resolves via the reloaded lookup
    }
}
