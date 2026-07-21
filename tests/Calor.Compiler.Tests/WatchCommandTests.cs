using System.Threading.Channels;
using Calor.Compiler.Commands;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for the <c>calor watch</c> debounce logic (factored into
/// <see cref="WatchDebouncer"/> precisely so it is testable without a real
/// <see cref="FileSystemWatcher"/>). The quiet period is measured via an injected
/// <see cref="TimeProvider"/>: pre-loaded/burst/cancel cases exercise the real
/// <see cref="TimeProvider.System"/> timer, and the window-timing case drives a
/// <see cref="FakeTimeProvider"/> on virtual time so it is deterministic (#714).
/// </summary>
public class WatchDebouncerTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Burst_IsCollapsedIntoOneDeduplicatedBatch()
    {
        var channel = Channel.CreateUnbounded<string>();
        // Editor-style burst: several events for the same save, before any waiter.
        channel.Writer.TryWrite("/p/a.calr");
        channel.Writer.TryWrite("/p/a.calr");
        channel.Writer.TryWrite("/p/b.calr");

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, TimeProvider.System, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Count);
        Assert.Contains("/p/a.calr", batch);
        Assert.Contains("/p/b.calr", batch);
    }

    [Fact]
    public async Task EventWithinQuietPeriod_ExtendsBatch_AndQuietPeriodDurationIsHonored()
    {
        // Virtual-time version (no wall-clock race, #714). Also verifies the quiet
        // period's DURATION is honored: advancing only part of the window must NOT
        // close the batch — which guards against quietPeriod being TimeSpan.Zero or
        // the wrong variable (the batch would close immediately and this would fail).
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");

        // Runs synchronously until it suspends on the quiet-timer/event WhenAny with
        // the first quiet timer armed (due at Quiet from now).
        var read = WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, CancellationToken.None);

        time.Advance(TimeSpan.FromMilliseconds(20));                 // 20ms < 100ms quiet
        Assert.False(read.IsCompleted, "quiet period must not elapse before its full duration");

        channel.Writer.TryWrite("/p/b.calr");                       // arrives inside the window

        // Let the read drain "b" and re-arm the quiet timer, then elapse the window.
        // Bounded, deterministic convergence: each iteration yields (so the drain +
        // re-arm continuation runs) then advances a full quiet period.
        for (var i = 0; i < 50 && !read.IsCompleted; i++)
        {
            await Task.Yield();
            time.Advance(Quiet);
        }

        var batch = await read.WaitAsync(TestTimeout);
        Assert.NotNull(batch);
        Assert.Contains("/p/a.calr", batch!);
        Assert.Contains("/p/b.calr", batch!);
    }

    [Fact]
    public async Task SeparateBursts_YieldSeparateBatches()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");
        var first = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, TimeProvider.System, CancellationToken.None)
            .WaitAsync(TestTimeout);
        Assert.NotNull(first);
        Assert.Single(first!);

        channel.Writer.TryWrite("/p/b.calr");
        var second = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, TimeProvider.System, CancellationToken.None)
            .WaitAsync(TestTimeout);
        Assert.NotNull(second);
        Assert.Contains("/p/b.calr", second!);
    }

    [Fact]
    public async Task Cancellation_WhileWaiting_ReturnsNull()
    {
        var channel = Channel.CreateUnbounded<string>();
        using var cts = new CancellationTokenSource(50);

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, TimeProvider.System, cts.Token)
            .WaitAsync(TestTimeout);

        Assert.Null(batch);
    }

    [Fact]
    public async Task Cancellation_DuringQuietPeriod_ReturnsNull()
    {
        // Covers the "surface cancellation" path (WatchDebouncer's `await quiet`):
        // an event has been received and we are parked in the quiet period when
        // cancellation arrives. FakeTimeProvider keeps the quiet timer from firing
        // on its own, so this exercises cancellation, not a timeout.
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");
        using var cts = new CancellationTokenSource();

        var read = WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, cts.Token);
        // Parked in the quiet period (timer armed, not fired). Cancel now.
        cts.Cancel();

        var batch = await read.WaitAsync(TestTimeout);
        Assert.Null(batch);
    }

    [Fact]
    public async Task CompletedChannel_WithNoEvents_ReturnsNull()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.Complete();

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, TimeProvider.System, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Null(batch);
    }

    [Fact]
    public async Task CompletedChannel_WithPendingEvents_DeliversThemFirst()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");
        channel.Writer.Complete();

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, TimeProvider.System, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.NotNull(batch);
        Assert.Contains("/p/a.calr", batch!);
    }
}
