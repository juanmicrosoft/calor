using System.Threading.Channels;
using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration coverage for the <see cref="WatchSession"/> — the core of
/// <c>calor watch</c> — closing the gap that <see cref="WatchDebouncerTests"/>
/// left open on batched change handling and cache-warmth reporting (issue #1002,
/// audit finding F1 / R6).
///
/// <para>Scripted tests inject changes for deterministic batching assertions.
/// Real filesystem tests separately cover watcher delivery for directory and
/// individual-file roots without relying on platform-specific event counts.</para>
///
/// <para><b>Test-hook flow.</b> <see cref="WatchSession.RebuildCompleted"/>
/// signals each rebuild's completion so the test can await batches
/// through a channel, including rebuilds completed before the test starts reading.</para>
///
/// <para><b>Cache-hit signal.</b> <see cref="WatchSession.RebuildResult.Compiled"/>
/// and <see cref="WatchSession.RebuildResult.Skipped"/> are the load-bearing signal:
/// <c>CompilationDriver</c> only increments <c>Skipped</c> on the cache-hit branch,
/// so asserting the <c>(Compiled, Skipped)</c> pair after a targeted edit proves
/// both the batching and the cache warmth.</para>
///
/// <para><b>Timing budget.</b> The debounce window is set to 50ms — small enough
/// that four sequential rebuilds finish inside a single test run, large enough
/// that back-to-back <c>InjectChange</c> calls land in the same batch on a busy
/// CI runner. The per-rebuild wait is capped at 60s as a stuck-session guard;
/// see <c>RebuildTimeout</c> for the corrected rationale.</para>
/// </summary>
public sealed class WatchSessionIntegrationTests : IDisposable
{
    // 50ms is deliberately generous relative to WatchDebouncerTests' 100ms virtual
    // clock: the fake-clock tests can pick any value, but we are on the real
    // TimeProvider here and want two InjectChange calls executed back-to-back on
    // the test thread to reliably land in the same batch. Values under ~20ms have
    // proven flaky on GitHub-hosted runners; 50ms is the audit-recommended floor.
    private const int DebounceMs = 50;

    // 60s is a stuck-session guard, NOT a cold-Roslyn budget. Adversarial
    // review corrected the earlier justification: CompilationDriver sets
    // DeferGeneratedOutputValidation = true, so per-file Roslyn validation
    // does not run in this path and warm rebuilds finish in <500ms. The cap
    // exists so a stuck session fails as a normal test failure instead of
    // exhausting the xUnit per-collection timeout.
    private static readonly TimeSpan RebuildTimeout = TimeSpan.FromSeconds(60);

    private readonly string _root;

    public WatchSessionIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "calor-watch-integration-tests", Guid.NewGuid().ToString("N"));
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

    private static string TransitionalNullableAssignmentViolation() => """
        §M{m001:N0}
          §F{f001:Probe:pub} () -> void
            §E{}
            §B{x:?str} null
            §B{y:str} x
        """;

    private void WriteAllModules()
    {
        File.WriteAllText(Path.Combine(_root, "a.calr"), ModuleSource("m001", "ModA", "f001", 1));
        File.WriteAllText(Path.Combine(_root, "b.calr"), ModuleSource("m002", "ModB", "f002", 2));
        File.WriteAllText(Path.Combine(_root, "c.calr"), ModuleSource("m003", "ModC", "f003", 3));
    }

    /// <summary>
    /// Bumps a module file's answer so the incremental cache picks it up as changed.
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
        var completed = Channel.CreateUnbounded<WatchSession.RebuildResult>();
        session.RebuildCompleted += result => completed.Writer.TryWrite(result);
        var runTask = session.RunAsync(cts.Token, useFileSystemWatchers: false);
        try
        {
            results.Add(await completed.Reader.ReadAsync().AsTask().WaitAsync(RebuildTimeout));
            for (int i = 1; i < rebuildCount; i++)
            {
                drive(session, i);
                results.Add(await completed.Reader.ReadAsync().AsTask().WaitAsync(RebuildTimeout));
            }
        }
        finally
        {
            cts.Cancel();
            Assert.Equal(0, await runTask.WaitAsync(RebuildTimeout));
        }

        return results;
    }

    [Fact]
    public async Task InitialCompile_DoesNotRunOnCallersSynchronizationContext()
    {
        WriteAllModules();
        var session = new WatchSession([_root], Settings(), TextWriter.Null, TextWriter.Null);
        using var cts = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<SynchronizationContext?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.RebuildCompleted += _ =>
        {
            entered.TrySetResult(SynchronizationContext.Current);
            Assert.True(release.Wait(RebuildTimeout), "RunAsync blocked its caller during the initial compile.");
        };

        Task<int> runTask;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            runTask = session.RunAsync(cts.Token, useFileSystemWatchers: false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            release.Set();
        }

        try
        {
            Assert.Null(await entered.Task.WaitAsync(RebuildTimeout));
        }
        finally
        {
            cts.Cancel();
            Assert.Equal(0, await runTask.WaitAsync(RebuildTimeout));
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("The watch loop must not capture the caller's context.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealFileSystemWatcher_Edit_RecompilesChangedFile(bool individualFileRoots)
    {
        WriteAllModules();
        IReadOnlyList<string> roots = individualFileRoots
            ? Directory.GetFiles(_root, "*.calr")
            : [_root];
        var session = new WatchSession(roots, Settings(), TextWriter.Null, TextWriter.Null);
        var completed = Channel.CreateUnbounded<WatchSession.RebuildResult>();
        session.RebuildCompleted += result => completed.Writer.TryWrite(result);
        using var cts = new CancellationTokenSource();
        var runTask = session.RunAsync(cts.Token, useFileSystemWatchers: true);
        try
        {
            var initial = await completed.Reader.ReadAsync().AsTask().WaitAsync(RebuildTimeout);
            Assert.Equal(3, initial.Compiled);
            Assert.False(initial.AnyErrors);

            EditModule("a.calr", "m001", "ModA", "f001", 101);
            var changed = await ReadUntilAsync(completed.Reader, result => result.Compiled > 0);
            Assert.Equal(1, changed.Compiled);
            Assert.Equal(2, changed.Skipped);
            Assert.False(changed.AnyErrors);
            Assert.Contains("return 101;", File.ReadAllText(Path.Combine(_root, "a.g.cs")));
        }
        finally
        {
            cts.Cancel();
            Assert.Equal(0, await runTask.WaitAsync(RebuildTimeout));
        }
    }

    [Fact]
    public async Task RealDirectoryWatcher_CreateRenameDelete_RefreshesSourceSet()
    {
        WriteAllModules();
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        var session = new WatchSession([_root], Settings(), TextWriter.Null, TextWriter.Null);
        var completed = Channel.CreateUnbounded<WatchSession.RebuildResult>();
        session.RebuildCompleted += result => completed.Writer.TryWrite(result);
        using var cts = new CancellationTokenSource();
        var runTask = session.RunAsync(cts.Token, useFileSystemWatchers: true);
        try
        {
            await completed.Reader.ReadAsync().AsTask().WaitAsync(RebuildTimeout);
            var source = Path.Combine(nested, "d.calr");
            File.WriteAllText(source, ModuleSource("m004", "ModD", "f004", 4));
            // Source-set changes can invalidate the cross-module map and recompile
            // unchanged modules; only ordinary content edits promise cache hits.
            var created = await ReadUntilAsync(completed.Reader, result =>
                result.Compiled > 0 && result.Compiled + result.Skipped == 4);
            Assert.False(created.AnyErrors);
            Assert.True(File.Exists(Path.ChangeExtension(source, ".g.cs")));

            var renamed = Path.Combine(nested, "renamed.calr");
            File.Move(source, renamed);
            var moved = await ReadUntilAsync(completed.Reader, result =>
                result.Compiled > 0 && File.Exists(Path.ChangeExtension(renamed, ".g.cs")));
            Assert.Equal(4, moved.Compiled + moved.Skipped);
            Assert.False(moved.AnyErrors);

            File.Delete(renamed);
            var deleted = await ReadUntilAsync(completed.Reader, result =>
                result.Compiled + result.Skipped == 3);
            Assert.False(deleted.AnyErrors);
        }
        finally
        {
            cts.Cancel();
            Assert.Equal(0, await runTask.WaitAsync(RebuildTimeout));
        }
    }

    private static async Task<WatchSession.RebuildResult> ReadUntilAsync(
        ChannelReader<WatchSession.RebuildResult> reader, Func<WatchSession.RebuildResult, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(RebuildTimeout);
        while (true)
        {
            var result = await reader.ReadAsync(timeout.Token);
            if (predicate(result))
                return result;
        }
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

    [Fact]
    public async Task ActiveBindingError_InChangedWarmSourceIsNotSkipped()
    {
        var source = Path.Combine(_root, "routing.calr");
        var safe = ModuleSource("m1", "Routing", "f1", 1);
        File.WriteAllText(source, safe);
        var results = await RunScriptAsync(rebuildCount: 3, drive: (session, step) =>
        {
            File.WriteAllText(source, step == 1
                ? safe + "  §F{f2:answer:pub} () -> i32\n    §R 2\n"
                : safe);
            session.InjectChange(source);
        });
        Assert.Equal(3, results.Count);
        Assert.False(results[0].AnyErrors);
        Assert.True(results[1].AnyErrors);
        Assert.Equal(0, results[1].Skipped);
        Assert.False(results[2].AnyErrors);
    }

    [Fact]
    public async Task EnvironmentTypeCheckTransition_DoesNotBypassActiveScalarBinding()
    {
        var previous = Environment.GetEnvironmentVariable("CALOR_NO_TYPE_CHECK");
        try
        {
            var source = Path.Combine(_root, "n0.calr");
            File.WriteAllText(source, TransitionalNullableAssignmentViolation());
            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", "1");

            var results = await RunScriptAsync(rebuildCount: 2, drive: (session, step) =>
            {
                Assert.Equal(1, step);
                Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", "0");
                session.InjectChange(source);
            });

            Assert.Equal(2, results.Count);
            Assert.Equal(0, results[0].Compiled);
            Assert.Equal(0, results[0].Skipped);
            Assert.True(results[0].AnyErrors);
            Assert.Equal(0, results[1].Compiled);
            Assert.Equal(0, results[1].Skipped);
            Assert.True(results[1].AnyErrors);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", previous);
        }
    }
}
