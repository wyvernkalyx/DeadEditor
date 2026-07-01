using DeadEditor.Helpers;
using DeadEditor.Models;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Coverage for <see cref="ConcertLookupService"/> cache coherence: rekey-on-save
/// (<see cref="ConcertLookupService.NotifySaved"/>) and evict-on-delete
/// (<see cref="ConcertLookupService.Evict"/>). These use the internal no-I/O
/// constructor (InternalsVisibleTo) so the in-memory dictionary and sorted-date
/// index can be exercised without touching the real AppData concerts directory or
/// the lazy production singleton.
/// </summary>
public class ConcertLookupServiceCacheTests
{
    private static ConcertReference Concert(string date, string venue = "Venue") =>
        new() { Date = date, Venue = venue };

    private static ConcertLookupService Seed(params string[] dates)
    {
        var concerts = new List<ConcertReference>();
        foreach (var d in dates)
            concerts.Add(Concert(d));
        return new ConcertLookupService(concerts);
    }

    // ===== Rekey on date change =====

    [Fact]
    public void NotifySaved_DateChanged_RekeysToNewDate()
    {
        var svc = Seed("1971-05-30", "1972-08-27");
        var concert = svc.GetConcertByDate("1971-05-30")!;

        // The editor mutated the live instance's date before saving.
        concert.Date = "1971-06-01";
        svc.NotifySaved("1971-05-30", concert);

        // New date resolves to the concert; old date misses.
        Assert.Same(concert, svc.GetConcertByDate("1971-06-01"));
        Assert.Null(svc.GetConcertByDate("1971-05-30"));
        Assert.False(svc.HasConcert("1971-05-30"));
        Assert.True(svc.HasConcert("1971-06-01"));
    }

    [Fact]
    public void NotifySaved_DateChanged_UpdatesSortedDatesInOrder()
    {
        var svc = Seed("1971-05-30", "1972-08-27", "1973-02-09");
        var concert = svc.GetConcertByDate("1971-05-30")!;

        concert.Date = "1972-12-31"; // moves between the other two
        svc.NotifySaved("1971-05-30", concert);

        Assert.Equal(
            new[] { "1972-08-27", "1972-12-31", "1973-02-09" },
            svc.GetAllDates());
        Assert.DoesNotContain("1971-05-30", svc.GetAllDates());
    }

    [Fact]
    public void NotifySaved_SameDate_IsNoOpButKeepsEntry()
    {
        var svc = Seed("1971-05-30", "1972-08-27");
        var concert = svc.GetConcertByDate("1971-05-30")!;

        svc.NotifySaved("1971-05-30", concert);

        Assert.Same(concert, svc.GetConcertByDate("1971-05-30"));
        Assert.Equal(new[] { "1971-05-30", "1972-08-27" }, svc.GetAllDates());
        Assert.Equal(2, svc.Count);
    }

    [Fact]
    public void NotifySaved_OldDateAbsent_InsertsNewDateWithoutThrowing()
    {
        var svc = Seed("1972-08-27");
        var concert = Concert("1971-05-30");

        // oldDate not in the cache (e.g. a concert created outside the cache).
        var ex = Record.Exception(() => svc.NotifySaved("1969-01-01", concert));

        Assert.Null(ex);
        Assert.Same(concert, svc.GetConcertByDate("1971-05-30"));
        Assert.Equal(new[] { "1971-05-30", "1972-08-27" }, svc.GetAllDates());
    }

    // ===== Evict on delete =====

    [Fact]
    public void Evict_RemovesEntryAndSortedDate()
    {
        var svc = Seed("1971-05-30", "1972-08-27", "1973-02-09");

        svc.Evict("1972-08-27");

        Assert.Null(svc.GetConcertByDate("1972-08-27"));
        Assert.False(svc.HasConcert("1972-08-27"));
        Assert.Equal(new[] { "1971-05-30", "1973-02-09" }, svc.GetAllDates());
        Assert.Equal(2, svc.Count);
    }

    [Fact]
    public void Evict_DateAbsent_DoesNotThrowOrChangeCache()
    {
        var svc = Seed("1971-05-30", "1972-08-27");

        var ex = Record.Exception(() => svc.Evict("1980-01-01"));

        Assert.Null(ex);
        Assert.Equal(new[] { "1971-05-30", "1972-08-27" }, svc.GetAllDates());
        Assert.Equal(2, svc.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Evict_NullOrEmpty_DoesNotThrow(string? date)
    {
        var svc = Seed("1971-05-30");

        var ex = Record.Exception(() => svc.Evict(date!));

        Assert.Null(ex);
        Assert.Equal(1, svc.Count);
    }

    // ===== Alias persistence: pure in-memory append/dedup (TryAppendAliasEntry) =====
    // These exercise the dedup/append logic with ZERO I/O (alias-setlists-spec.md §4). The
    // successful-Persisted disk write + rollback are NOT unit-tested here (they would dirty
    // AppData / repo-source) — that path rides commit (iii)'s WPF gate.

    [Fact]
    public void TryAppendAliasEntry_NoExistingAliases_CreatesSingleSetlistAndAppends()
    {
        var concert = new ConcertReference { Date = "1971-05-30" };

        var added = ConcertLookupService.TryAppendAliasEntry(
            concert, new AliasEntry { CoveredOfficialIndices = { 1, 2 } });

        Assert.True(added);
        Assert.Single(concert.AliasSetlists);              // one shared AliasSetlist created
        Assert.Single(concert.AliasSetlists[0].Entries);
        Assert.Equal(new[] { 1, 2 }, concert.AliasSetlists[0].Entries[0].CoveredOfficialIndices);
    }

    [Fact]
    public void TryAppendAliasEntry_DuplicateCoveredRun_ReturnsFalseAndDoesNotMutate()
    {
        var concert = new ConcertReference { Date = "1971-05-30" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 1, 2 } } }
        });

        var added = ConcertLookupService.TryAppendAliasEntry(
            concert, new AliasEntry { CoveredOfficialIndices = { 1, 2 } });

        Assert.False(added);                               // dedup by order-sensitive index set
        Assert.Single(concert.AliasSetlists);
        Assert.Single(concert.AliasSetlists[0].Entries);   // nothing appended
    }

    [Fact]
    public void TryAppendAliasEntry_DifferentCoveredRun_AppendsToSameSingleSetlist()
    {
        var concert = new ConcertReference { Date = "1971-05-30" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 1, 2 } } }
        });

        var added = ConcertLookupService.TryAppendAliasEntry(
            concert, new AliasEntry { CoveredOfficialIndices = { 5, 6 } });

        Assert.True(added);
        Assert.Single(concert.AliasSetlists);              // STILL one shared AliasSetlist
        Assert.Equal(2, concert.AliasSetlists[0].Entries.Count);
        Assert.Equal(new[] { 5, 6 }, concert.AliasSetlists[0].Entries[1].CoveredOfficialIndices);
    }

    // ===== Alias removal: pure in-memory removal/drop (TryRemoveAliasEntry) =====
    // Mirror-symmetric to TryAppendAliasEntry, ZERO I/O. Removal identifies the entry by the same
    // order-sensitive covered-index set append dedups on; removing a container's last entry DROPS the
    // container so the concert file stays byte-pristine (alias-setlists-spec.md §4, Q4).

    [Fact]
    public void TryRemoveAliasEntry_ExistingEntryInMultiEntryContainer_RemovesOnlyThatEntry()
    {
        var concert = new ConcertReference { Date = "1971-05-30" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries =
            {
                new AliasEntry { CoveredOfficialIndices = { 1, 2 } },
                new AliasEntry { CoveredOfficialIndices = { 5, 6 } }
            }
        });

        var removed = ConcertLookupService.TryRemoveAliasEntry(concert, new[] { 1, 2 });

        Assert.True(removed);
        Assert.Single(concert.AliasSetlists);              // container survives
        Assert.Single(concert.AliasSetlists[0].Entries);   // only the matched entry gone
        Assert.Equal(new[] { 5, 6 }, concert.AliasSetlists[0].Entries[0].CoveredOfficialIndices);
    }

    [Fact]
    public void TryRemoveAliasEntry_LastEntry_DropsTheContainer()
    {
        var concert = new ConcertReference { Date = "1971-05-30" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 1, 2 } } }
        });

        var removed = ConcertLookupService.TryRemoveAliasEntry(concert, new[] { 1, 2 });

        Assert.True(removed);
        Assert.Empty(concert.AliasSetlists);               // container dropped, not left empty
    }

    [Fact]
    public void TryRemoveAliasEntry_LastEntryDropped_SerializesWithoutAliasSetlistsKey()
    {
        var concert = new ConcertReference { Date = "1971-05-30", Venue = "Winterland" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 1, 2 } } }
        });

        ConcertLookupService.TryRemoveAliasEntry(concert, new[] { 1, 2 });

        // The whole reason for drop-vs-empty: no aliasSetlists noise in an otherwise-pristine file.
        var json = CanonicalJson.Serialize(concert);
        Assert.DoesNotContain("aliasSetlists", json);
    }

    [Fact]
    public void TryRemoveAliasEntry_CoveredSetNotPresent_ReturnsFalseAndDoesNotMutate()
    {
        var concert = new ConcertReference { Date = "1971-05-30" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 1, 2 } } }
        });

        var removed = ConcertLookupService.TryRemoveAliasEntry(concert, new[] { 5, 6 });

        Assert.False(removed);                             // idempotent no-op
        Assert.Single(concert.AliasSetlists);
        Assert.Single(concert.AliasSetlists[0].Entries);
    }

    [Fact]
    public void TryRemoveAliasEntry_NullAliasSetlists_ReturnsFalseWithoutThrowing()
    {
        var concert = new ConcertReference { Date = "1971-05-30", AliasSetlists = null! };

        var removed = ConcertLookupService.TryRemoveAliasEntry(concert, new[] { 1, 2 });

        Assert.False(removed);
    }

    [Fact]
    public void TryRemoveAliasEntry_EmptyAliasSetlists_ReturnsFalseWithoutThrowing()
    {
        var concert = new ConcertReference { Date = "1971-05-30" }; // AliasSetlists initialized-empty

        var removed = ConcertLookupService.TryRemoveAliasEntry(concert, new[] { 1, 2 });

        Assert.False(removed);
        Assert.Empty(concert.AliasSetlists);
    }

    [Fact]
    public void TryRemoveAliasEntry_ReorderedCoveredSet_DoesNotMatch()
    {
        // SequenceEqual is order-sensitive — mirror of append's identity contract. A reordered set
        // is a different identity, so it must NOT match the recorded [1, 2] entry.
        var concert = new ConcertReference { Date = "1971-05-30" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 1, 2 } } }
        });

        var removed = ConcertLookupService.TryRemoveAliasEntry(concert, new[] { 2, 1 });

        Assert.False(removed);
        Assert.Single(concert.AliasSetlists[0].Entries);   // untouched
    }

    // ===== Alias persistence: no-write paths of PersistAliasSetlist =====

    [Fact]
    public void PersistAliasSetlist_UnknownDate_ReturnsConcertNotFound()
    {
        var svc = Seed("1971-05-30");

        // No concert cached for this date — returns before any write, no file fabricated.
        var result = svc.PersistAliasSetlist(
            "1999-12-31", new AliasEntry { CoveredOfficialIndices = { 1, 2 } });

        Assert.Equal(AliasPersistResult.ConcertNotFound, result);
    }

    [Fact]
    public void PersistAliasSetlist_DuplicateEntry_ReturnsDuplicateNoOpWithoutWriting()
    {
        // Seed a concert that ALREADY contains the entry, so the dedup hit precedes any write —
        // no disk I/O, no AppData/repo pollution.
        var concert = new ConcertReference { Date = "1971-05-30", Venue = "Winterland" };
        concert.AliasSetlists.Add(new AliasSetlist
        {
            Entries = { new AliasEntry { CoveredOfficialIndices = { 1, 2 } } }
        });
        var svc = new ConcertLookupService(new[] { concert });

        var result = svc.PersistAliasSetlist(
            "1971-05-30", new AliasEntry { CoveredOfficialIndices = { 1, 2 } });

        Assert.Equal(AliasPersistResult.DuplicateNoOp, result);
        Assert.Single(concert.AliasSetlists);              // unchanged in memory
        Assert.Single(concert.AliasSetlists[0].Entries);
    }
}
