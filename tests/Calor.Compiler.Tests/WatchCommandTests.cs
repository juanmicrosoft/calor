using System.Threading.Channels;
using Calor.Compiler.Commands;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for the <c>calor watch</c> debounce logic (factored into
/// <see cref="WatchDebouncer"/> precisely so it is testable without a real
/// <see cref="FileSystemWatcher"/>). Every case drives the quiet period via a
/// <see cref="FakeTimeProvider"/> on virtual time, so batch boundaries are
/// deterministic instead of racing a real clock under parallel test load
/// (#714, #729). No test depends on wall-clock timing.
/// </summary>
public class WatchDebouncerTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Drives <paramref name="time"/> forward until the debouncer's quiet timer fires
    /// and the batch closes.
    ///
    /// <para><b>Race-freedom invariant:</b> the clock is advanced ONLY when the channel
    /// is empty. While an event is still buffered the clock stays put, so the quiet
    /// timer cannot fire and <c>WhenAny</c> must resolve the event first — draining it
    /// into the batch and re-arming. The batch therefore never closes with an un-drained
    /// event, no matter how starved the thread pool is (<c>TryRead</c> is what empties
    /// the channel, so <c>Count == 0</c> implies every written event is already batched).</para>
    ///
    /// <para><b>Precondition:</b> no events may be written to the channel after this
    /// helper starts. Every write must happen before the drive; a mid-drive write from
    /// another task would reintroduce the Count-check race. The loop is wall-clock
    /// bounded so it keeps advancing as long as the runner is alive rather than
    /// exhausting a fixed iteration budget and then parking on a timer no one fires.</para>
    /// </summary>
    private static async Task<IReadOnlyCollection<string>?> DriveToClose(
        Task<IReadOnlyCollection<string>?> read, FakeTimeProvider time, ChannelReader<string> reader)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!read.IsCompleted && deadline.Elapsed < TestTimeout)
        {
            await Task.Yield();
            if (reader.Count == 0)
            {
                time.Advance(Quiet);
            }
        }

        return await read.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Burst_IsCollapsedIntoOneDeduplicatedBatch()
    {
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();
        // Editor-style burst: several events for the same save, before any waiter.
        channel.Writer.TryWrite("/p/a.calr");
        channel.Writer.TryWrite("/p/a.calr");
        channel.Writer.TryWrite("/p/b.calr");

        var read = WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, CancellationToken.None);
        var batch = await DriveToClose(read, time, channel.Reader);

        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Count);
        Assert.Contains("/p/a.calr", batch);
        Assert.Contains("/p/b.calr", batch);
    }

    [Fact]
    public async Task EventWithinQuietPeriod_ExtendsBatch_AndQuietPeriodDurationIsHonored()
    {
        // Verifies the quiet period's DURATION is honored: advancing only part of the
        // window must NOT close the batch — which guards against quietPeriod being
        // TimeSpan.Zero or the wrong variable (the batch would close immediately and
        // this would fail).
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");

        // Runs synchronously until it suspends on the quiet-timer/event WhenAny with
        // the first quiet timer armed (due at Quiet from now).
        var read = WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, CancellationToken.None);

        time.Advance(TimeSpan.FromMilliseconds(20));                 // 20ms < 100ms quiet
        Assert.False(read.IsCompleted, "quiet period must not elapse before its full duration");

        channel.Writer.TryWrite("/p/b.calr");                       // arrives inside the window

        var batch = await DriveToClose(read, time, channel.Reader);
        Assert.NotNull(batch);
        Assert.Contains("/p/a.calr", batch!);
        Assert.Contains("/p/b.calr", batch!);
    }

    [Fact]
    public async Task SeparateBursts_YieldSeparateBatches()
    {
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();

        channel.Writer.TryWrite("/p/a.calr");
        var first = await DriveToClose(
            WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, CancellationToken.None), time, channel.Reader);
        Assert.NotNull(first);
        Assert.Single(first!);

        channel.Writer.TryWrite("/p/b.calr");
        var second = await DriveToClose(
            WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, CancellationToken.None), time, channel.Reader);
        Assert.NotNull(second);
        Assert.Contains("/p/b.calr", second!);
    }

    [Fact]
    public async Task Cancellation_WhileWaiting_ReturnsNull()
    {
        // No events: the read is parked on the first WaitToReadAsync. Cancel it
        // explicitly (no timed CancellationTokenSource, so no wall-clock race).
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();
        using var cts = new CancellationTokenSource();

        // Sound, not racy: ReadBatchAsync runs synchronously up to its first await
        // (WaitToReadAsync), so the token is registered before Cancel() is called.
        var read = WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, cts.Token);
        cts.Cancel();

        var batch = await read.WaitAsync(TestTimeout);
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
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.Complete();

        // Channel completion short-circuits the first WaitToReadAsync — returns
        // without arming the quiet timer, so no clock advance is needed.
        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Null(batch);
    }

    [Fact]
    public async Task CompletedChannel_WithPendingEvents_DeliversThemFirst()
    {
        var time = new FakeTimeProvider();
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");
        channel.Writer.Complete();

        // Pending events drain, then the drain loop's WaitToReadAsync sees the
        // completed channel and returns the batch before the quiet timer fires — so
        // this is deterministic without advancing the clock.
        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, time, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.NotNull(batch);
        Assert.Contains("/p/a.calr", batch!);
    }
}
