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
    public void IndexContentsAreIdenticalToAFullRebuild(string scriptName)
    {
        // Gate 2's index-contents leg (roadmap §2.5 gate 2), which became live
        // when the index shipped. It rides the corpus that already exists rather
        // than adding a second one.
        //
        // v1 rebuilds the index wholesale, so this leg is not yet load-bearing
        // against an incremental index — it pins the *canonical form*: the index
        // built for a given workspace state must serialise byte-identically no
        // matter what states preceded it. That is what makes the comparison
        // meaningful the day an incremental path exists.
        var script = LoadScript(scriptName);

        var fresh = new List<string>();
        foreach (var step in script.Steps)
        {
            var workspace = CreateTempDir();
            SyncWorkspace(workspace, step.SourceDirectory);
            fresh.Add(SerializeIndex(workspace, step));
        }

        var sequential = new List<string>();
        var reused = CreateTempDir();
        foreach (var step in script.Steps)
        {
            SyncWorkspace(reused, step.SourceDirectory);
            sequential.Add(SerializeIndex(reused, step));
        }

        for (var index = 0; index < script.Steps.Count; index++)
            Assert.Equal(fresh[index], sequential[index]);
    }

    /// <summary>
    /// The index for a workspace, serialised canonically with the volatile
    /// header stripped. Compiler and manifest hashes depend on the machine and
    /// the temp path, not on the workspace's contents, so comparing them would
    /// make the test fail for reasons that have nothing to do with the index.
    /// </summary>
    private static string SerializeIndex(string workspace, ScriptStep step)
    {
        var sources = Directory.GetFiles(workspace, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var index = Indexing.ProjectIndexBuilder.Build(
            new Indexing.ProjectIndexBuilder.Options(workspace, step.Options, sources));

        var lines = new List<string>();
        foreach (var declaration in index.Declarations)
        {
            lines.Add(
                $"decl|{declaration.File}|{declaration.Line}|{declaration.Column}"
                    + $"|{declaration.Kind}|{declaration.Name}");
        }
        foreach (var edge in index.CallEdges)
            lines.Add($"edge|{edge.File}|{edge.Line}|{edge.Column}");
        foreach (var occurrence in index.Occurrences)
        {
            lines.Add(
                $"occ|{occurrence.File}|{occurrence.Line}|{occurrence.Column}|{occurrence.Kind}");
        }
        // v0.15 E5's effects facet (ProjectIndex format 4.0). Gate 3 observes
        // effects "as diagnostics and index bytes"; without these lines an
        // effect-row edit (ES-08) that moved only the facet would be invisible
        // to this leg.
        //
        // Symbol ids are not printed: they embed the canonicalised source
        // identity (Binder: `source/<identity>/module/...`), which differs
        // between the two temp workspaces the way the header hashes do. The
        // row is identified by file, line, kind and name instead.
        foreach (var row in index.EffectRows)
        {
            lines.Add(
                $"effrow|{row.File}|{row.Line}|{row.Kind}|{row.Name}"
                    + $"|declared={row.Declared}"
                    + $"|{DescribeRow(row.DeclaredRow)}|{DescribeRow(row.InferredRow)}"
                    + $"|{row.Verdict}|{row.DiagnosticCode}"
                    + $"|forbidden={string.Join(",", row.Forbidden)}|bound={row.BoundRow}");
        }
        foreach (var unavailable in index.Residual.EffectRowsUnavailable)
            lines.Add($"residual-effect-rows-unavailable|{unavailable}");
        foreach (var unreadable in index.Residual.UnreadableFiles)
            lines.Add($"residual-unreadable|{unreadable}");
        foreach (var unresolved in index.Residual.UnresolvedCalls)
            lines.Add($"residual-unresolved|{unresolved}");
        foreach (var ambiguous in index.Residual.AmbiguousCallees)
            lines.Add($"residual-ambiguous|{ambiguous}");

        return string.Join("\n", lines);
    }

    private static string DescribeRow(Indexing.IndexedRow? row)
    {
        if (row is null)
            return "-";
        var variables = string.Join(
            ",",
            row.Variables.Select(variable => $"{variable.Ordinal}:{variable.Name}"));
        return $"{row.State}{{{string.Join(",", row.Effects)}}}<{variables}>"
            + $"[{string.Join(",", row.Reasons)}]";
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
                // v0.16 kickoff sweep: the effect-row script (roadmap-v0.13-v0.15
                // §4.4 gate 3), registered under F-3′ §4 of
                // docs/plans/v0.13-freeze-registrations.md with its breach
                // disclosed (it was to land before E2 merged; E2 merged first).
                "ES-08-effect-row-edit",
            },
            EnumerateScriptDirectories().Select(Path.GetFileName).ToArray());
    }

    // --- ES-08: the effect-row script ---------------------------------------

    /// <summary>
    /// ES-08's per-step diagnostic outcome, pinned by file, severity, code and
    /// POSITION so the script's registered meaning (F-3′ §6) is a tested claim
    /// and not prose: the row edits in <c>combinators.calr</c> move the UNEDITED
    /// caller's Calor0410 (step 1) and the callee's own Calor0425 plus the
    /// fail-closed Calor0410 (step 2), and <c>bystander.calr</c> never reports
    /// anything. Each entry is <c>file: severity Calor####@line,column</c>,
    /// ordinally sorted, over a from-scratch compile — PP-E1's shape
    /// (<c>SpikeVerdictTests</c>), because the registration text claims severity
    /// and a code-only multiset cannot tell an error from a demoted warning, or
    /// a moved span from a stable one.
    /// </summary>
    private static readonly IReadOnlyList<string>[] Es08ExpectedDiagnosticsPerStep =
    [
        // step-00-clean: Map<eff e> is instantiated with a pure function by
        // UsePure and with a printing one by UseImpure; every row fits.
        [],
        // step-01-callee-row-widens: Map's declared row gains `cw`. UsePure
        // declares §E{alloc, mut} and was not edited — the cross-module charge
        // now names an effect it does not declare. UseImpure declares `cw`
        // and stays clean.
        // …at `§F{f003:UsePure:pub}` (12,5) — the declaration in the file that
        // was NOT edited, reported as an ERROR under the shipped 0.15 rule.
        ["app.calr: error Calor0410@12,5"],
        // step-02-callee-row-erased: Map loses `<eff e>` and its parameter's
        // §E{e}. Invoking the row-less `f` inside Map is "cannot tell"
        // (Calor0425, a warning at the invocation) plus the fail-closed
        // Unknown charge on Map itself (Calor0410, the shipped 0.15 rule:
        // `--permissive-effects` is the only waiver); the callers no longer
        // instantiate a row and Map's declared row covers what they declare.
        // No Calor1002 on app.calr: the callee's failure excludes its output
        // from generated-C# validation, and CompilationDriver now skips that
        // validation when any file failed — on cold and warm builds alike.
        // …Calor0410 at `Map`'s own `§E` row (3,5), an error; Calor0425 at the
        // row-less invocation `§C{f}` (7,22), a warning.
        [
            "combinators.calr: error Calor0410@3,5",
            "combinators.calr: warning Calor0425@7,22",
        ],
    ];

    [Fact]
    public void Es08_EffectRowEdit_DiagnosticsMoveAsRegistered()
    {
        var script = LoadScript("ES-08-effect-row-edit");
        Assert.Equal(Es08ExpectedDiagnosticsPerStep.Length, script.Steps.Count);

        for (var index = 0; index < script.Steps.Count; index++)
        {
            var workspace = CreateTempDir();
            SyncWorkspace(workspace, script.Steps[index].SourceDirectory);
            var outcome = RunStep(workspace, script.Steps[index], clearFirst: true);

            var reported = outcome.Diagnostics
                .Select(line =>
                {
                    // Canonical form is file|code|severity|line|column|message;
                    // re-shaped to PP-E1's `severity Calor####@line,column`.
                    var parts = line.Split('|');
                    return $"{parts[0]}: {parts[2].ToLowerInvariant()} {parts[1]}"
                        + $"@{parts[3]},{parts[4]}";
                })
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                Es08ExpectedDiagnosticsPerStep[index].SequenceEqual(reported, StringComparer.Ordinal),
                $"ES-08 step {index}: expected [{string.Join(", ", Es08ExpectedDiagnosticsPerStep[index])}] "
                    + $"but the compiler reported [{string.Join(", ", reported)}]. The script's "
                    + "registered outcome (F-3′ §6) moved — re-register with disclosure; do "
                    + "not edit the fixture to fit. Full diagnostics:\n  "
                    + string.Join("\n  ", outcome.Diagnostics));
        }
    }

    [Fact]
    public void Es08_EffectRowEdit_DeltaIsConfinedToTheCalleeAndItsCallers()
    {
        // Gate 3's "confined to the affected declarations and their callers"
        // clause, made observable: the bystander module is never edited and
        // never calls Map, so neither its diagnostics nor its index lines
        // (declarations, occurrences, effect rows, residuals) may move across
        // the three steps — while the callee's own effect-row entry MUST move,
        // or the EffectRows facet is not observing the edit at all.
        //
        // The LIMITS of the facet leg are pinned here rather than assumed
        // (review round 1, M1; disclosed in F-3′ §6):
        //
        //   * `app.calr` — the CALLER file, the one whose diagnostics move —
        //     contributes NO `effrow` line at any step. Its `§C{Map}` is
        //     cross-module, so the index records it under
        //     `residual-effect-rows-unavailable` instead. The facet claim
        //     therefore rests on `combinators.calr` alone, and the assertion
        //     below says so in both directions: zero rows, and the residual
        //     present, at every step.
        //   * The index leg is fresh-vs-fresh (ProjectIndexBuilder holds no
        //     cache), so no ES-08 assertion can observe a STALE facet. What it
        //     observes is that the facet is a function of the workspace state
        //     and moves with the row edit.
        var script = LoadScript("ES-08-effect-row-edit");

        var bystanderIndexPerStep = new List<string>();
        var calleeRowPerStep = new List<string>();
        for (var index = 0; index < script.Steps.Count; index++)
        {
            var workspace = CreateTempDir();
            SyncWorkspace(workspace, script.Steps[index].SourceDirectory);
            var outcome = RunStep(workspace, script.Steps[index], clearFirst: true);

            Assert.DoesNotContain(
                outcome.Diagnostics,
                line => line.StartsWith("bystander.calr|", StringComparison.Ordinal));

            var lines = SerializeIndex(workspace, script.Steps[index]).Split('\n');

            // Probe A, inverted: the caller's rows are absent and its residual
            // is present. If the index ever starts recording cross-module
            // positions as rows, this fails and the F-3′ §6 residual is stale.
            Assert.DoesNotContain(
                lines,
                line => line.StartsWith("effrow|app.calr|", StringComparison.Ordinal));
            Assert.Contains(
                lines,
                line => line.StartsWith("residual-effect-rows-unavailable|", StringComparison.Ordinal)
                    && line.Contains("app.calr", StringComparison.Ordinal));

            // Both index shapes name a file: `<kind>|bystander.calr|…` for rows
            // and declarations, `residual-…|bystander.calr: …` for residuals.
            bystanderIndexPerStep.Add(string.Join(
                "\n",
                lines.Where(line => line.Contains("bystander.calr", StringComparison.Ordinal))));
            calleeRowPerStep.Add(string.Join(
                "\n",
                lines.Where(line =>
                    line.StartsWith("effrow|combinators.calr|", StringComparison.Ordinal))));
        }

        Assert.NotEmpty(bystanderIndexPerStep[0]);
        for (var index = 1; index < script.Steps.Count; index++)
            Assert.Equal(bystanderIndexPerStep[0], bystanderIndexPerStep[index]);

        Assert.NotEmpty(calleeRowPerStep[0]);
        // All three states differ: widening the row is not the same facet as
        // erasing it, and without the last comparison a facet that only noticed
        // "declared vs not declared" would pass (review round 1).
        Assert.NotEqual(calleeRowPerStep[0], calleeRowPerStep[1]);
        Assert.NotEqual(calleeRowPerStep[0], calleeRowPerStep[2]);
        Assert.NotEqual(calleeRowPerStep[1], calleeRowPerStep[2]);
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
