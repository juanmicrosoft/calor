using System.Threading.Channels;
using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for the <c>calor watch</c> debounce logic (factored into
/// <see cref="WatchDebouncer"/> precisely so it is testable without a real
/// <see cref="FileSystemWatcher"/>).
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

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.NotNull(batch);
        Assert.Equal(2, batch!.Count);
        Assert.Contains("/p/a.calr", batch);
        Assert.Contains("/p/b.calr", batch);
    }

    [Fact]
    public async Task EventsWithinQuietPeriod_ExtendTheSameBatch()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");

        var read = WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, CancellationToken.None);
        // A follow-up event landing well inside the quiet period joins the batch.
        await Task.Delay(20);
        channel.Writer.TryWrite("/p/b.calr");

        var batch = await read.WaitAsync(TestTimeout);
        Assert.NotNull(batch);
        Assert.Contains("/p/b.calr", batch!);
    }

    [Fact]
    public async Task SeparateBursts_YieldSeparateBatches()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");
        var first = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, CancellationToken.None)
            .WaitAsync(TestTimeout);
        Assert.NotNull(first);
        Assert.Single(first!);

        channel.Writer.TryWrite("/p/b.calr");
        var second = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, CancellationToken.None)
            .WaitAsync(TestTimeout);
        Assert.NotNull(second);
        Assert.Contains("/p/b.calr", second!);
    }

    [Fact]
    public async Task Cancellation_WhileWaiting_ReturnsNull()
    {
        var channel = Channel.CreateUnbounded<string>();
        using var cts = new CancellationTokenSource(50);

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, cts.Token)
            .WaitAsync(TestTimeout);

        Assert.Null(batch);
    }

    [Fact]
    public async Task CompletedChannel_WithNoEvents_ReturnsNull()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.Complete();

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Null(batch);
    }

    [Fact]
    public async Task CompletedChannel_WithPendingEvents_DeliversThemFirst()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("/p/a.calr");
        channel.Writer.Complete();

        var batch = await WatchDebouncer.ReadBatchAsync(channel.Reader, Quiet, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.NotNull(batch);
        Assert.Contains("/p/a.calr", batch!);
    }
}

/// <summary>
/// Smoke tests for the watch session loop. The session's change events are injected
/// through <see cref="WatchSession.InjectChange"/> instead of a real
/// <see cref="FileSystemWatcher"/> — FSW event delivery latency is platform-dependent
/// (FSEvents on macOS can lag by seconds), which would make wall-clock-bounded tests
/// flaky; the FSW wiring itself is thin (event handler → channel write) and is
/// covered by manual verification.
/// </summary>
public class WatchSessionTests : IDisposable
{
    private readonly string _tempDir;

    public WatchSessionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-watch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static WatchSession.WatchSettings Settings(string format = "json") => new(
        Format: format,
        Verbose: false,
        NoCache: false,
        ClearCache: false,
        StrictApi: false,
        RequireDocs: false,
        EnforceEffects: false,
        StrictEffects: false,
        PermissiveEffects: false,
        ContractMode: "debug",
        DebounceMs: 50);

    [Fact]
    public async Task InitialCompile_ThenInjectedChange_RebuildsIncrementally()
    {
        var a = Path.Combine(_tempDir, "a.calr");
        var b = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(a, """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "hello"
            """);
        File.WriteAllText(b, """
            §M{m002:Beta}
              §F{f001:Wave:pub} () -> void
                §E{cw}
                §P "wave"
            """);

        var output = new StringWriter();
        var status = new StringWriter();
        var session = new WatchSession([_tempDir], Settings(), output, status);

        var rebuilds = new List<WatchSession.RebuildResult>();
        var rebuildSignal = new SemaphoreSlim(0);
        session.RebuildCompleted += result =>
        {
            lock (rebuilds) { rebuilds.Add(result); }
            rebuildSignal.Release();
        };

        using var cts = new CancellationTokenSource();
        var runTask = session.RunAsync(cts.Token, useFileSystemWatchers: false);

        // Initial compile: both files built.
        Assert.True(await rebuildSignal.WaitAsync(TimeSpan.FromSeconds(30)), "initial compile did not complete");
        lock (rebuilds)
        {
            Assert.Equal(2, rebuilds[0].Compiled);
            Assert.Equal(0, rebuilds[0].Skipped);
            Assert.False(rebuilds[0].AnyErrors);
        }

        // Change one file; the rebuild must recompile it and skip the other.
        File.WriteAllText(a, """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "changed"
            """);
        session.InjectChange(a);

        Assert.True(await rebuildSignal.WaitAsync(TimeSpan.FromSeconds(30)), "rebuild after change did not complete");
        lock (rebuilds)
        {
            Assert.Equal(1, rebuilds[1].Compiled);
            Assert.Equal(1, rebuilds[1].Skipped);
            Assert.False(rebuilds[1].AnyErrors);
        }

        cts.Cancel();
        var exitCode = await runTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, exitCode);

        // json mode is NDJSON: one compact, independently parseable JSON document
        // per line, one line per rebuild. Parse the lines — substring counting
        // cannot prove the stream is splittable.
        var lines = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            Assert.Equal("1.0", doc.RootElement.GetProperty("version").GetString());
            Assert.True(doc.RootElement.TryGetProperty("summary", out _));
        }

        // Status stream (stderr surrogate) carries the human-readable summaries.
        var statusText = status.ToString();
        Assert.Contains("Initial compile: 2 compiled, 0 up-to-date", statusText);
        Assert.Contains("1 compiled, 1 up-to-date", statusText);
        Assert.Contains("Watch stopped.", statusText);
    }

    [Fact]
    public async Task BrokenEdit_ReportsErrors_ThenRecoversOnFix()
    {
        var a = Path.Combine(_tempDir, "a.calr");
        File.WriteAllText(a, """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "hello"
            """);

        var session = new WatchSession([_tempDir], Settings(), new StringWriter(), new StringWriter());
        var rebuilds = new List<WatchSession.RebuildResult>();
        var rebuildSignal = new SemaphoreSlim(0);
        session.RebuildCompleted += result =>
        {
            lock (rebuilds) { rebuilds.Add(result); }
            rebuildSignal.Release();
        };

        using var cts = new CancellationTokenSource();
        var runTask = session.RunAsync(cts.Token, useFileSystemWatchers: false);
        Assert.True(await rebuildSignal.WaitAsync(TimeSpan.FromSeconds(30)));

        // Break the file — the rebuild reports errors but the session keeps running.
        File.WriteAllText(a, "§M{m001:Alpha\n  broken");
        session.InjectChange(a);
        Assert.True(await rebuildSignal.WaitAsync(TimeSpan.FromSeconds(30)));
        lock (rebuilds) { Assert.True(rebuilds[1].AnyErrors); }

        // Fix it — the next rebuild succeeds (failed files are never cached).
        File.WriteAllText(a, """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "fixed"
            """);
        session.InjectChange(a);
        Assert.True(await rebuildSignal.WaitAsync(TimeSpan.FromSeconds(30)));
        lock (rebuilds)
        {
            Assert.False(rebuilds[2].AnyErrors);
            Assert.Equal(1, rebuilds[2].Compiled);
        }

        cts.Cancel();
        Assert.Equal(0, await runTask.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task NonexistentPath_FailsFast()
    {
        var session = new WatchSession(
            [Path.Combine(_tempDir, "missing.calr")], Settings(), new StringWriter(), new StringWriter());
        var exitCode = await session.RunAsync(CancellationToken.None, useFileSystemWatchers: false)
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, exitCode);
    }
}
