using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end coverage for <c>calor watch</c> (issue #1002, audit finding F1 / R6):
/// exercises the full session — initial baseline compile, debounced change batches,
/// and interaction with the incremental build cache — through the same
/// <see cref="WatchSession"/> entry point that the CLI wraps.
///
/// <para><b>Test-hook flow.</b> <see cref="WatchSession.InjectChange"/> feeds change
/// events as if the file-system watcher had raised them, so the test bypasses the
/// real <see cref="FileSystemWatcher"/> (whose latency varies by platform). The
/// session is driven with <c>useFileSystemWatchers: false</c> to avoid double-feeds.
/// <see cref="WatchSession.RebuildCompleted"/> signals each rebuild's completion so
/// the test can await batches deterministically rather than polling stdout.</para>
///
/// <para><b>Cache-hit signal.</b> <see cref="WatchSession.RebuildResult.Compiled"/>
/// and <see cref="WatchSession.RebuildResult.Skipped"/> are the load-bearing signal:
/// a cache-hit file is reported as skipped, so asserting the (Compiled, Skipped)
/// pair after a targeted edit proves both the batching and the cache warmth.</para>
///
/// <para><b>Timing budget.</b> The debounce window is set to 50ms — small enough
/// that four sequential rebuilds finish inside a single test run, large enough
/// that back-to-back <c>InjectChange</c> calls land in the same batch on a busy
/// CI runner. The per-rebuild wait is capped at 60s so a stuck rebuild fails
/// loudly instead of exhausting the xUnit per-collection timeout.</para>
/// </summary>
public sealed class WatchCommandE2ETests : IDisposable
{
    // 50ms is deliberately generous relative to WatchDebouncerTests' 100ms virtual
    // clock: the fake-clock tests can pick any value, but we are on the real
    // TimeProvider here and want two InjectChange calls executed back-to-back on
    // the test thread to reliably land in the same batch. Values under ~20ms have
    // proven flaky on GitHub-hosted runners; 50ms is the audit-recommended floor.
    private const int DebounceMs = 50;

    // 60s per rebuild is generous: the initial compile spins up Roslyn to
    // validate generated C#, which can take double-digit seconds cold on CI.
    // Warm rebuilds finish in <500ms locally; the cap only exists so a stuck
    // session surfaces as a test failure instead of a collection-level hang.
    private static readonly TimeSpan RebuildTimeout = TimeSpan.FromSeconds(60);

    private readonly string _root;

    public WatchCommandE2ETests()
    {
        _root = Path.Combine(Path.GetTempPath(), "calor-watch-e2e-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static WatchSession.WatchSettings Settings() => new(
        Format: "text",
        Verbose: false,
        // Cache MUST be enabled — the whole point of these tests is to observe
        // that unchanged files are cache-skipped on subsequent rebuilds.
        NoCache: false,
        ClearCache: false,
        StrictApi: false,
        RequireDocs: false,
        EnforceEffects: false,
        StrictEffects: false,
        PermissiveEffects: false,
        ContractMode: "off",
        DebounceMs: DebounceMs);

    // Three tiny modules that compile independently: no cross-module refs, so
    // an edit to one must not force recompilation of the other two. Distinct
    // module IDs (m001/m002/m003) keep the compiler happy under the
    // cross-module map that CompilationDriver builds even for warm caches.
    private static string ModuleSource(string moduleId, string moduleName, string funcId, int answer)
    {
        // Written with string concatenation because C#'s raw-string interpolation
        // and Calor's own '§X{...}' brace syntax fight each other unpleasantly.
        return "§M{" + moduleId + ":" + moduleName + "}\n"
            + "  §F{" + funcId + ":answer:pub}\n"
            + "    §O{i32}\n"
            + "    §R INT:" + answer.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
    }

    private void WriteAllModules()
    {
        File.WriteAllText(Path.Combine(_root, "a.calr"), ModuleSource("m001", "ModA", "f001", 1));
        File.WriteAllText(Path.Combine(_root, "b.calr"), ModuleSource("m002", "ModB", "f002", 2));
        File.WriteAllText(Path.Combine(_root, "c.calr"), ModuleSource("m003", "ModC", "f003", 3));
    }

    /// <summary>
    /// Bumps a module file's answer (touches content so the incremental cache
    /// picks it up as changed) then feeds one <c>InjectChange</c> per touched file.
    /// A distinct <paramref name="newAnswer"/> per rebuild guarantees the content
    /// hash actually changes even if the filesystem timestamps do not.
    /// </summary>
    private void EditModule(string fileName, string moduleId, string moduleName, string funcId, int newAnswer)
    {
        File.WriteAllText(Path.Combine(_root, fileName), ModuleSource(moduleId, moduleName, funcId, newAnswer));
    }

    /// <summary>
    /// Runs a scripted watch session: awaits the initial compile, then for each
    /// scripted step invokes <paramref name="drive"/> (which mutates files and
    /// calls <see cref="WatchSession.InjectChange"/>) before awaiting the next
    /// rebuild. Returns the ordered rebuild results — [0] is the initial compile.
    /// </summary>
    private async Task<IReadOnlyList<WatchSession.RebuildResult>> RunScriptAsync(
        int rebuildCount, Action<WatchSession, int> drive)
    {
        var session = new WatchSession(new[] { _root }, Settings(),
            TextWriter.Null, TextWriter.Null);
        using var cts = new CancellationTokenSource();

        var results = new List<WatchSession.RebuildResult>();
        var next = new TaskCompletionSource<WatchSession.RebuildResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.RebuildCompleted += result =>
        {
            // Snapshot the completion source, replace, then complete — completing
            // first would race the next rebuild's WaitAsync against the swap.
            var current = next;
            next = new TaskCompletionSource<WatchSession.RebuildResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            current.TrySetResult(result);
        };

        // Run on a worker to isolate any sync-over-async in the compile pipeline
        // from the test's own async context (defensive; not strictly required).
        var runTask = Task.Run(() => session.RunAsync(cts.Token, useFileSystemWatchers: false), cts.Token);

        // Initial compile is rebuild 0.
        results.Add(await next.Task.WaitAsync(RebuildTimeout));

        for (int i = 1; i < rebuildCount; i++)
        {
            drive(session, i);
            results.Add(await next.Task.WaitAsync(RebuildTimeout));
        }

        cts.Cancel();
        try { await runTask.WaitAsync(RebuildTimeout); }
        catch (OperationCanceledException) { /* cancellation is the shutdown path */ }

        return results;
    }

    [Fact]
    public async Task InitialCompile_CompilesAllFiles()
    {
        WriteAllModules();

        var results = await RunScriptAsync(rebuildCount: 1, drive: (_, _) => { });

        var initial = Assert.Single(results);
        Assert.Equal(3, initial.Compiled);
        Assert.Equal(0, initial.Skipped);
        Assert.False(initial.AnyErrors);
    }

    [Fact]
    public async Task SingleFileEdit_RecompilesOnlyThatFile_OthersAreCacheHits()
    {
        WriteAllModules();

        var results = await RunScriptAsync(rebuildCount: 2, drive: (session, step) =>
        {
            // Step 1: edit file a.calr, inject its change. Files b/c must be
            // reported as cache-skipped — that is the load-bearing assertion.
            EditModule("a.calr", "m001", "ModA", "f001", 11);
            session.InjectChange(Path.Combine(_root, "a.calr"));
        });

        Assert.Equal(2, results.Count);
        // Initial: all three cold.
        Assert.Equal(3, results[0].Compiled);
        Assert.Equal(0, results[0].Skipped);
        // Rebuild 1: only a.calr changed → 1 compiled, 2 cache hits.
        Assert.Equal(1, results[1].Compiled);
        Assert.Equal(2, results[1].Skipped);
        Assert.False(results[1].AnyErrors);
    }

    [Fact]
    public async Task BatchedEdits_RecompileTogether_UnchangedFileStillCacheHits()
    {
        WriteAllModules();

        var results = await RunScriptAsync(rebuildCount: 2, drive: (session, step) =>
        {
            // Edit files a and b, inject BOTH changes before the debounce window
            // closes. Because InjectChange is synchronous and the debounce window
            // is 50ms, the two events land in the same batch → one rebuild.
            EditModule("a.calr", "m001", "ModA", "f001", 12);
            EditModule("b.calr", "m002", "ModB", "f002", 22);
            session.InjectChange(Path.Combine(_root, "a.calr"));
            session.InjectChange(Path.Combine(_root, "b.calr"));
        });

        Assert.Equal(2, results.Count);
        Assert.Equal(3, results[0].Compiled);
        // Rebuild 1: both edits batched → 2 compiled, c.calr is the sole cache hit.
        Assert.Equal(2, results[1].Compiled);
        Assert.Equal(1, results[1].Skipped);
        Assert.False(results[1].AnyErrors);
    }

    [Fact]
    public async Task SequentialEdits_ProduceSeparateRebuilds_WithCorrectCacheHitPattern()
    {
        // The full R6 scenario: baseline → single-file edit → batched two-file
        // edit. Three total rebuilds after the initial compile assert the
        // batching and cache warmth interact correctly across a session.
        WriteAllModules();

        var results = await RunScriptAsync(rebuildCount: 3, drive: (session, step) =>
        {
            if (step == 1)
            {
                EditModule("a.calr", "m001", "ModA", "f001", 13);
                session.InjectChange(Path.Combine(_root, "a.calr"));
            }
            else if (step == 2)
            {
                EditModule("a.calr", "m001", "ModA", "f001", 14);
                EditModule("b.calr", "m002", "ModB", "f002", 24);
                session.InjectChange(Path.Combine(_root, "a.calr"));
                session.InjectChange(Path.Combine(_root, "b.calr"));
            }
        });

        Assert.Equal(3, results.Count);
        // Initial: cold cache — all three compile.
        Assert.Equal(3, results[0].Compiled);
        Assert.Equal(0, results[0].Skipped);
        // Step 1: only a.calr changed → 1 compiled, b + c are cache hits.
        Assert.Equal(1, results[1].Compiled);
        Assert.Equal(2, results[1].Skipped);
        // Step 2: a + b batched together → 2 compiled, c is the sole cache hit.
        Assert.Equal(2, results[2].Compiled);
        Assert.Equal(1, results[2].Skipped);
        Assert.All(results, r => Assert.False(r.AnyErrors));
    }
}
