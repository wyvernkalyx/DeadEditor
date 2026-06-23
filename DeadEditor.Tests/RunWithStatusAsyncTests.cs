using System;
using System.Threading.Tasks;
using DeadEditor.Services;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="AlertService.RunWithStatusAsync"/>: the scoped please-wait primitive
/// (alert-system-spec.md § Status overlay). Verifies the show/hide pairing, hide-on-exception, the
/// overlapping-scope refcount, and last-write-wins on the displayed title — the logic that owns the
/// overlay independent of the WPF view.
/// <para>
/// These drive the real <see cref="AlertService.Instance"/> singleton (its ctor is private, mirroring
/// the existing <c>AudioPlayerService</c> pattern — there is no parallel test instance) with a
/// <see cref="FakeStatusHost"/> registered fresh at the top of each test. That is safe because xUnit
/// runs the tests in a class sequentially (no intra-class parallelism), and every scope balances the
/// active-scope counter back to 0 in its <c>finally</c>, so no state leaks between tests. With no
/// <c>Application.Current</c> in the headless test host, the service's UI marshalling falls through to
/// a direct synchronous call, so <c>ShowStatus</c>/<c>HideStatus</c> are observable inline. (The
/// <see cref="IProgress{T}"/> UI-thread marshalling needs a real SynchronizationContext and is left to
/// Commit B's WPF gate, per the spec.)
/// </para>
/// </summary>
public class RunWithStatusAsyncTests
{
    /// <summary>Records IStatusHost calls + a shown/hidden flag and show/hide counts.</summary>
    private sealed class FakeStatusHost : IStatusHost
    {
        public bool IsShown { get; private set; }
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public string? LastTitle { get; private set; }
        public string? LastMessage { get; private set; }

        public bool IsShowing => IsShown;

        public void ShowStatus(string title, string? message)
        {
            IsShown = true;
            ShowCount++;
            LastTitle = title;
            LastMessage = message;
        }

        public void UpdateStatus(string message) => LastMessage = message;

        public void HideStatus()
        {
            IsShown = false;
            HideCount++;
        }
    }

    private static FakeStatusHost Register()
    {
        var host = new FakeStatusHost();
        AlertService.Instance.RegisterStatusHost(host);
        return host;
    }

    [Fact]
    public async Task HappyPath_ShowsTitleThenHides()
    {
        var host = Register();

        await AlertService.Instance.RunWithStatusAsync("Loading", _ => Task.CompletedTask);

        Assert.Equal("Loading", host.LastTitle);
        Assert.Equal(1, host.ShowCount);
        Assert.Equal(1, host.HideCount);
        Assert.False(host.IsShown);     // ends hidden
    }

    [Fact]
    public async Task Exception_StillHides_AndRethrows()
    {
        var host = Register();
        var boom = new InvalidOperationException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AlertService.Instance.RunWithStatusAsync("Loading", _ => Task.FromException(boom)));

        Assert.Same(boom, thrown);      // rethrown unchanged
        Assert.Equal(1, host.HideCount); // finally still hid it
        Assert.False(host.IsShown);
    }

    [Fact]
    public async Task Overlap_StaysShownUntilLastScopeCompletes()
    {
        var host = Register();
        var gate1 = new TaskCompletionSource();
        var gate2 = new TaskCompletionSource();

        var op1 = AlertService.Instance.RunWithStatusAsync("First", _ => gate1.Task);
        var op2 = AlertService.Instance.RunWithStatusAsync("Second", _ => gate2.Task);

        // Both scopes active: overlay shown, never hidden yet.
        Assert.True(host.IsShown);
        Assert.Equal(2, host.ShowCount);
        Assert.Equal(0, host.HideCount);

        // First completes — overlay must REMAIN (the second is still running).
        gate1.SetResult();
        await op1;
        Assert.True(host.IsShown);
        Assert.Equal(0, host.HideCount);

        // Second (last) completes — only now does it hide.
        gate2.SetResult();
        await op2;
        Assert.False(host.IsShown);
        Assert.Equal(1, host.HideCount);
    }

    [Fact]
    public async Task Overlap_LastWriteWins_OnTitle()
    {
        var host = Register();
        var gate1 = new TaskCompletionSource();
        var gate2 = new TaskCompletionSource();

        var op1 = AlertService.Instance.RunWithStatusAsync("First", _ => gate1.Task);
        var op2 = AlertService.Instance.RunWithStatusAsync("Second", _ => gate2.Task);

        // The later scope's title is the one displayed.
        Assert.Equal("Second", host.LastTitle);

        gate1.SetResult();
        gate2.SetResult();
        await Task.WhenAll(op1, op2);
    }
}
