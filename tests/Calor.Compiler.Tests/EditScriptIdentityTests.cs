using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Full-vs-incremental identity gate (roadmap §2.5 gate 2, diagnostics leg).
///
/// For every registered edit script, compiling the sequence incrementally must
/// produce byte-identical diagnostics to compiling each state from scratch —
/// and must do it while reusing the cache. Identity alone is not enough: a
/// compiler that never reuses anything is trivially identical to itself, so
/// every script also carries an incrementality witness.
///
/// Corpus and its registration rules: tests/TestData/EditScripts/README.md.
/// </summary>
public sealed class EditScriptIdentityTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    public static TheoryData<string> RegisteredScripts()
    {
        var data = new TheoryData<string>();
        foreach (var directory in EnumerateScriptDirectories())
            data.Add(Path.GetFileName(directory));
        return data;
    }

    [Theory]
    [MemberData(nameof(RegisteredScripts))]
    public void IncrementalDiagnosticsAreIdenticalToFullRebuild(string scriptName)
    {
        var script = LoadScript(scriptName);

        // Full: every step compiled from scratch, in its own workspace, with no
        // prior cache state. This is the oracle.
        var full = new List<StepOutcome>();
        foreach (var step in script.Steps)
        {
            var workspace = CreateTempDir();
            SyncWorkspace(workspace, step.SourceDirectory);
            full.Add(RunStep(workspace, step, clearFirst: true));
        }

        // Incremental: one workspace and one cache carried across the whole
        // sequence, mutated in place the way an editing session mutates it.
        var incremental = new List<StepOutcome>();
        var incrementalWorkspace = CreateTempDir();
        foreach (var step in script.Steps)
        {
            SyncWorkspace(incrementalWorkspace, step.SourceDirectory);
            incremental.Add(RunStep(incrementalWorkspace, step, clearFirst: false));
        }

        for (var index = 0; index < script.Steps.Count; index++)
        {
            Assert.Equal(
                full[index].Diagnostics,
                incremental[index].Diagnostics);
            Assert.Equal(
                full[index].AnyErrors,
                incremental[index].AnyErrors);
        }
    }

    [Theory]
    [MemberData(nameof(RegisteredScripts))]
    public void CacheReuseMatchesTheScriptsRegisteredExpectation(string scriptName)
    {
        // The anti-vacuity leg. Byte-identical diagnostics are free if the
        // "incremental" path silently rebuilds everything, so a script that is
        // supposed to reuse the cache must demonstrably reuse it.
        //
        // The assertion is two-sided. Some scripts legitimately reuse nothing:
        // a change to the file set moves the cross-module map hash, and a change
        // to the options token invalidates by construction, so those scripts
        // rebuild everything *by design*. Registering that expectation makes it a
        // tested claim — if global invalidation ever narrows or widens, the
        // script whose behaviour changed says so, instead of the gate quietly
        // passing on a weaker guarantee.
        var script = LoadScript(scriptName);
        var workspace = CreateTempDir();
        var skipped = 0;

        foreach (var step in script.Steps)
        {
            SyncWorkspace(workspace, step.SourceDirectory);
            skipped += RunStep(workspace, step, clearFirst: false).SkippedCount;
        }

        if (script.ExpectsReuse)
        {
            Assert.True(
                skipped > 0,
                $"{scriptName}: registered as reusing the cache, but no file was "
                    + $"ever served from it across {script.Steps.Count} steps. The "
                    + "identity result for this script is vacuous — it compares a "
                    + "full build against a full build.");
        }
        else
        {
            Assert.True(
                skipped == 0,
                $"{scriptName}: registered as rebuilding everything ({script.ReuseNote}), "
                    + $"but {skipped} file(s) were served from cache. Invalidation "
                    + "narrowed; confirm the diagnostics are still correct and "
                    + "update the registration.");
        }
    }

    [Fact]
    public void EveryStepRewritingIdenticalContentRecompilesNothing()
    {
        // ES-06 states the strong form of the witness: when no content changed,
        // a warm build must recompile nothing at all. Kept separate from the
        // per-script witness because only this script's steps are content-equal.
        var script = LoadScript("ES-06-touch-noop");
        var workspace = CreateTempDir();

        SyncWorkspace(workspace, script.Steps[0].SourceDirectory);
        var cold = RunStep(workspace, script.Steps[0], clearFirst: true);
        Assert.True(cold.CompiledCount > 0);

        foreach (var step in script.Steps.Skip(1))
        {
            SyncWorkspace(workspace, step.SourceDirectory);
            var warm = RunStep(workspace, step, clearFirst: false);
            Assert.Equal(0, warm.CompiledCount);
            Assert.Equal(cold.CompiledCount, warm.SkippedCount);
        }
    }

    [Fact]
    public void RegisteredScriptIdsAreStable()
    {
        // The denominator is pinned so it cannot shrink quietly: dropping a
        // script to make the gate pass has to edit this list in the diff.
        Assert.Equal(
            new[]
            {
                "ES-01-local-edit",
                "ES-02-add-file",
                "ES-03-delete-file",
                "ES-04-cross-module-effect",
                "ES-05-options-flip",
                "ES-06-touch-noop",
                "ES-07-persistent-finding",
            },
            EnumerateScriptDirectories().Select(Path.GetFileName).ToArray());
    }

    [Theory]
    [MemberData(nameof(RegisteredScripts))]
    public void ScriptStatesDifferFromEachOther(string scriptName)
    {
        // A step that changes nothing — no file edit and no option change —
        // exercises no invalidation path, so it would pad the corpus without
        // testing anything. ES-06 is the deliberate exception: its whole point
        // is that identical content is rewritten, and its option profile is
        // constant, so it is checked by the dedicated test above instead.
        if (scriptName == "ES-06-touch-noop")
            return;

        var script = LoadScript(scriptName);
        var states = script.Steps
            .Select(step => (
                Files: string.Join(
                    "\n",
                    Directory.GetFiles(step.SourceDirectory, "*.calr")
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .Select(path =>
                            $"{Path.GetFileName(path)}\n{File.ReadAllText(path)}")),
                step.Options))
            .ToArray();

        for (var index = 1; index < states.Length; index++)
        {
            Assert.False(
                states[index] == states[index - 1],
                $"{scriptName}: step {index} is identical to step {index - 1} "
                    + "in both files and options, so it exercises nothing.");
        }
    }

    // --- harness -----------------------------------------------------------

    private sealed record StepOutcome(
        IReadOnlyList<string> Diagnostics,
        bool AnyErrors,
        int CompiledCount,
        int SkippedCount);

    private sealed record ScriptStep(string SourceDirectory, string Options);

    private sealed record EditScript(
        string Id,
        IReadOnlyList<ScriptStep> Steps,
        bool ExpectsReuse,
        string ReuseNote);

    private StepOutcome RunStep(string workspace, ScriptStep step, bool clearFirst)
    {
        var sources = Directory.GetFiles(workspace, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FileInfo(path))
            .ToList();
        var sink = new DiagnosticBag();
        var compiled = 0;

        var result = CompilationDriver.CompileAll(
            sources,
            _ => new CompilationOptions
            {
                EnforceEffects = step.Options != "effects-off",
                RequireDocs = step.Options == "docs-required",
            },
            crossModuleEnforcement: true,
            crossModulePolicy: UnknownCallPolicy.Strict,
            onCompiled: (file, compileResult) =>
            {
                compiled++;
                File.WriteAllText(
                    Path.ChangeExtension(file.FullName, ".g.cs"),
                    compileResult.GeneratedCode);
            },
            diagnosticSink: sink,
            cache: new CompilationDriver.DriverCacheSettings(
                workspace,
                // The options token is the compiler's own record of which
                // inputs affect diagnostics. ES-05 exists to catch a profile
                // that changes diagnostics without changing this string.
                step.Options,
                clearFirst,
                file => Path.ChangeExtension(file.FullName, ".g.cs")));

        return new StepOutcome(
            Canonicalize(sink, workspace),
            result.AnyErrors,
            compiled,
            result.Skipped.Count);
    }

    /// <summary>
    /// Diagnostics as comparable text: workspace-relative paths (the two runs
    /// use different temp directories) and a fixed ordering, so a difference in
    /// emission order is not reported as a difference in findings.
    /// </summary>
    private static IReadOnlyList<string> Canonicalize(
        DiagnosticBag diagnostics,
        string workspace)
    {
        return diagnostics
            .Select(diagnostic =>
            {
                var file = diagnostic.FilePath is { Length: > 0 } path
                    ? Path.GetRelativePath(workspace, path).Replace('\\', '/')
                    : "";
                return string.Join(
                    "|",
                    file,
                    diagnostic.Code,
                    diagnostic.Severity,
                    diagnostic.Span.Line,
                    diagnostic.Span.Column,
                    diagnostic.Message);
            })
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Makes the workspace match a step's file set exactly: additions and edits
    /// are copied in, and files absent from the step are deleted along with
    /// their generated output.
    /// </summary>
    private static void SyncWorkspace(string workspace, string stepDirectory)
    {
        var wanted = Directory.GetFiles(stepDirectory, "*.calr")
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var existing in Directory.GetFiles(workspace, "*.calr"))
        {
            if (wanted.Contains(Path.GetFileName(existing)))
                continue;
            File.Delete(existing);
            var generated = Path.ChangeExtension(existing, ".g.cs");
            if (File.Exists(generated))
                File.Delete(generated);
        }

        foreach (var source in Directory.GetFiles(stepDirectory, "*.calr"))
        {
            File.Copy(
                source,
                Path.Combine(workspace, Path.GetFileName(source)),
                overwrite: true);
        }
    }

    private static EditScript LoadScript(string scriptName)
    {
        var directory = Path.Combine(CorpusRoot, scriptName);
        using var json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, "script.json")));
        var root = json.RootElement;
        var steps = root.GetProperty("steps")
            .EnumerateArray()
            .Select(step => new ScriptStep(
                Path.Combine(directory, step.GetProperty("dir").GetString()!),
                step.GetProperty("options").GetString()!))
            .ToArray();

        Assert.NotEmpty(steps);
        foreach (var step in steps)
        {
            Assert.True(
                Directory.Exists(step.SourceDirectory),
                $"{scriptName}: step directory missing: {step.SourceDirectory}");
            Assert.Contains(
                step.Options,
                new[] { "effects-on", "effects-off", "docs-required" });
        }

        return new EditScript(
            root.GetProperty("id").GetString()!,
            steps,
            root.GetProperty("expectsReuse").GetBoolean(),
            root.GetProperty("reuseNote").GetString()!);
    }

    private static string[] EnumerateScriptDirectories() =>
        Directory.GetDirectories(CorpusRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string CorpusRoot
    {
        get
        {
            var directory = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(directory, "Calor.sln")))
                directory = Directory.GetParent(directory)!.FullName;
            return Path.Combine(directory, "tests", "TestData", "EditScripts");
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "calor-editscript-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
