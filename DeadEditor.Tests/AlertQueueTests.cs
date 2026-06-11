using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="AlertQueue"/>: the pure queue + dismissal policy behind the in-window
/// alert banner (alert-system-spec.md Ruling 1). One alert shows at a time; the rest queue FIFO
/// and surface on dismiss. Severity decides auto-dismiss (Info) vs persist (Warning/Error).
/// </summary>
public class AlertQueueTests
{
    private static AlertItem Info(string m) => new(m, AlertSeverity.Info, null);
    private static AlertItem Warn(string m) => new(m, AlertSeverity.Warning, null);

    [Fact]
    public void Enqueue_EmptyQueue_BecomesCurrentAndIsReturned()
    {
        var q = new AlertQueue();

        var shown = q.Enqueue(Info("a"));

        Assert.NotNull(shown);
        Assert.Equal("a", shown!.Message);
        Assert.Equal("a", q.Current!.Message);
        Assert.Equal(0, q.PendingCount);
    }

    [Fact]
    public void Enqueue_WhileShowing_QueuesBehindAndReturnsNull()
    {
        var q = new AlertQueue();
        q.Enqueue(Info("a"));

        var shown = q.Enqueue(Info("b"));

        Assert.Null(shown);                       // current render is left untouched
        Assert.Equal("a", q.Current!.Message);
        Assert.Equal(1, q.PendingCount);
    }

    [Fact]
    public void Dismiss_AdvancesInFifoOrder()
    {
        var q = new AlertQueue();
        q.Enqueue(Info("a"));
        q.Enqueue(Info("b"));
        q.Enqueue(Info("c"));

        var next = q.Dismiss();
        Assert.Equal("b", next!.Message);
        Assert.Equal("b", q.Current!.Message);
        Assert.Equal(1, q.PendingCount);

        var next2 = q.Dismiss();
        Assert.Equal("c", next2!.Message);
        Assert.Equal(0, q.PendingCount);
    }

    [Fact]
    public void Dismiss_LastItem_ClearsCurrentAndReturnsNull()
    {
        var q = new AlertQueue();
        q.Enqueue(Info("only"));

        var next = q.Dismiss();

        Assert.Null(next);
        Assert.Null(q.Current);
        Assert.Equal(0, q.PendingCount);
    }

    [Fact]
    public void Dismiss_EmptyQueue_IsNoOp()
    {
        var q = new AlertQueue();

        var next = q.Dismiss();

        Assert.Null(next);
        Assert.Null(q.Current);
    }

    [Fact]
    public void Enqueue_AfterDrain_ShowsImmediatelyAgain()
    {
        var q = new AlertQueue();
        q.Enqueue(Info("a"));
        q.Dismiss();                              // queue now empty

        var shown = q.Enqueue(Warn("b"));

        Assert.NotNull(shown);
        Assert.Equal("b", q.Current!.Message);
        Assert.Equal(0, q.PendingCount);
    }

    [Theory]
    [InlineData(AlertSeverity.Info, true)]
    [InlineData(AlertSeverity.Warning, false)]
    [InlineData(AlertSeverity.Error, false)]
    public void ShouldAutoDismiss_OnlyInfoAutoDismisses(AlertSeverity severity, bool expected)
    {
        Assert.Equal(expected, AlertQueue.ShouldAutoDismiss(severity));
    }
}
