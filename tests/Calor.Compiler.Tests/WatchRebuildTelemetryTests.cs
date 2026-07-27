using System.Text.Json;
using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the watch rebuild latency instrumentation (loop plan WS3 D3.2):
/// RebuildResult carries elapsed wall time, and a configured rebuild log
/// receives one watch-rebuild/1 JSONL record per rebuild.
/// </summary>
public sealed class WatchRebuildTelemetryTests : IDisposable
{
    private readonly string _root;

    public WatchRebuildTelemetryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "calor-watch-telemetry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static WatchSession.WatchSettings Settings(string format) => new(
        Format: format,
        Verbose: false,
        NoCache: true,
        ClearCache: false,
        StrictApi: false,
        RequireDocs: false,
        EnforceEffects: false,
        StrictEffects: false,
        PermissiveEffects: false,
        ContractMode: "off",
        DebounceMs: 25);

    /// <summary>
    /// Runs a WatchSession through its initial rebuild only: the session is
    /// cancelled from the RebuildCompleted hook, so RunAsync returns after
    /// the first compile without waiting on the change channel.
    /// </summary>
    private async Task<WatchSession.RebuildResult> RunInitialRebuildAsync(string format, string? rebuildLogPath)
    {
        var session = new WatchSession(new[] { _root }, Settings(format),
            TextWriter.Null, TextWriter.Null, rebuildLogPath);
        using var cts = new CancellationTokenSource();
        WatchSession.RebuildResult? observed = null;
        session.RebuildCompleted += result =>
        {
            observed = result;
            cts.Cancel();
        };

        await session.RunAsync(cts.Token, useFileSystemWatchers: false);
        Assert.NotNull(observed);
        return observed!;
    }

    [Fact]
    public async Task Rebuild_ReportsElapsedWallTime()
    {
        File.WriteAllText(Path.Combine(_root, "mod.calr"), """
            §M{m001:WatchMod}
              §F{f001:answer:pub}
                §O{i32}
                §R INT:42
            """);

        var result = await RunInitialRebuildAsync("text", rebuildLogPath: null);

        Assert.Equal(1, result.Compiled);
        Assert.False(result.AnyErrors);
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Rebuild_WritesWatchRebuildRecord_WhenLogConfigured()
    {
        File.WriteAllText(Path.Combine(_root, "mod.calr"), """
            §M{m001:WatchMod}
              §F{f001:answer:pub}
                §O{i32}
                §R INT:42
            """);
        var logPath = Path.Combine(_root, "watch-rebuilds.jsonl");

        await RunInitialRebuildAsync("json", logPath);

        var record = JsonDocument.Parse(File.ReadAllLines(logPath).Single()).RootElement;
        Assert.Equal("watch-rebuild/1", record.GetProperty("schema").GetString());
        Assert.Equal(1, record.GetProperty("rebuild").GetInt32());
        Assert.True(record.GetProperty("initial").GetBoolean());
        Assert.Equal(1, record.GetProperty("compiled").GetInt32());
        Assert.Equal(0, record.GetProperty("skipped").GetInt32());
        Assert.False(record.GetProperty("anyErrors").GetBoolean());
        Assert.True(record.GetProperty("latencyMs").GetInt64() >= 0);
        Assert.Equal(25, record.GetProperty("debounceMs").GetInt32());
    }

    [Fact]
    public async Task Rebuild_NoLogConfigured_WritesNoTelemetry()
    {
        File.WriteAllText(Path.Combine(_root, "mod.calr"), """
            §M{m001:WatchMod}
              §F{f001:answer:pub}
                §O{i32}
                §R INT:42
            """);

        await RunInitialRebuildAsync("text", rebuildLogPath: null);

        Assert.False(File.Exists(Path.Combine(_root, "watch-rebuilds.jsonl")));
    }
}
