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
}
