using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
using Calor.Compiler.Indexing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// CLI incremental-build cache tests, driven in-process through
/// <see cref="CompilationDriver.CompileAll"/> with <c>DriverCacheSettings</c>:
/// unchanged files are skipped (cache-hit evidence via the skip callback and
/// <c>DriverResult.Skipped</c>), option/compiler-hash/manifest changes invalidate
/// globally, and cross-module effect enforcement keeps working from cached
/// per-module summaries on fully-skipped warm builds.
/// </summary>
public class IncrementalCliBuildTests : IDisposable
{
    private readonly string _tempDir;

    public IncrementalCliBuildTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-incr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteSource(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private (string APath, string BPath) WriteIndependentPair()
    {
        var a = WriteSource("a.calr", """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "hello"
            """);
        var b = WriteSource("b.calr", """
            §M{m002:Beta}
              §F{f001:Wave:pub} () -> void
                §E{cw}
                §P "wave"
            """);
        return (a, b);
    }

    private sealed record RunOutcome(
        CompilationDriver.DriverResult Result,
        List<string> CompiledFiles,
        List<string> SkippedOutputs,
        DiagnosticBag Diagnostics);

    private RunOutcome Run(
        string[] files, string optionsToken = "opts", bool clearFirst = false,
        Action<FileInfo>? afterCompile = null)
    {
        var sources = files.Select(f => new FileInfo(f)).ToList();
        var compiled = new List<string>();
        var skippedOutputs = new List<string>();
        var sink = new DiagnosticBag();

        var result = CompilationDriver.CompileAll(
            sources,
            _ => new CompilationOptions { EnforceEffects = false },
            crossModuleEnforcement: true,
            crossModulePolicy: UnknownCallPolicy.Strict,
            onCompiled: (file, compileResult) =>
            {
                compiled.Add(file.FullName);
                File.WriteAllText(Path.ChangeExtension(file.FullName, ".g.cs"), compileResult.GeneratedCode);
                afterCompile?.Invoke(file);
            },
            diagnosticSink: sink,
            cache: new CompilationDriver.DriverCacheSettings(
                _tempDir,
                optionsToken,
                clearFirst,
                file => Path.ChangeExtension(file.FullName, ".g.cs")),
            onSkipped: (_, outputPath) => skippedOutputs.Add(outputPath));

        return new RunOutcome(result, compiled, skippedOutputs, sink);
    }

    [Fact]
    public void WarmBuild_SkipsAllUnchangedFiles()
    {
        var (a, b) = WriteIndependentPair();

        var cold = Run([a, b]);
        Assert.Equal(2, cold.CompiledFiles.Count);
        Assert.Empty(cold.SkippedOutputs);
        Assert.True(File.Exists(BuildStateCache.GetCachePath(_tempDir)),
            "state file should be written next to the outputs");

        var warm = Run([a, b]);
        Assert.Empty(warm.CompiledFiles);
        // Cache-hit evidence: both files reported through the skip callback.
        Assert.Equal(2, warm.SkippedOutputs.Count);
        Assert.Contains(Path.ChangeExtension(a, ".g.cs"), warm.SkippedOutputs);
        Assert.Contains(Path.ChangeExtension(b, ".g.cs"), warm.SkippedOutputs);
        Assert.Equal(2, warm.Result.Skipped.Count);
        Assert.False(warm.Result.AnyErrors);
    }

    [Fact]
    public void ChangedFile_IsRecompiled_OthersStaySkipped()
    {
        var (a, b) = WriteIndependentPair();
        Run([a, b]);

        File.WriteAllText(a, """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "changed"
            """);

        var warm = Run([a, b]);
        Assert.Equal([a], warm.CompiledFiles);
        Assert.Equal([Path.ChangeExtension(b, ".g.cs")], warm.SkippedOutputs);
    }

    [Fact]
    public void MidCompileEdit_IsNotRecordedAsCompiled_NextRunRecompiles()
    {
        // Adversarial TOCTOU probe: an editor save landing mid-compile (here: from
        // the onCompiled callback, i.e. after the source was read but before the
        // cache entry is recorded) must not poison the cache. The entry has to hash
        // the bytes that were actually compiled — if it re-read the file, the next
        // run would skip and the edited content would never be compiled.
        var (a, b) = WriteIndependentPair();
        var edited = """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "edited mid-compile"
            """;

        var cold = Run([a, b], afterCompile: file =>
        {
            if (file.FullName == a)
            {
                File.WriteAllText(a, edited);
            }
        });
        Assert.Equal(2, cold.CompiledFiles.Count);

        // The mid-compile edit was never compiled, so the next run must recompile it.
        var warm = Run([a, b]);
        Assert.Equal([a], warm.CompiledFiles);
        Assert.Contains("edited mid-compile", File.ReadAllText(Path.ChangeExtension(a, ".g.cs")));

        // And once the edited content has genuinely been compiled, it caches normally.
        var settled = Run([a, b]);
        Assert.Empty(settled.CompiledFiles);
        Assert.Equal(2, settled.SkippedOutputs.Count);
    }

    [Fact]
    public void OptionsChange_InvalidatesAllCachedFiles()
    {
        var (a, b) = WriteIndependentPair();
        Run([a, b], optionsToken: "enforceEffects:False");

        var flipped = Run([a, b], optionsToken: "enforceEffects:True");
        Assert.Equal(2, flipped.CompiledFiles.Count);
        Assert.Empty(flipped.SkippedOutputs);
    }

    [Fact]
    public void CompilerHashChange_InvalidatesAllCachedFiles()
    {
        var (a, b) = WriteIndependentPair();
        Run([a, b]);

        // Simulate a compiler upgrade by tampering the persisted compiler hash.
        var state = BuildStateCache.Load(_tempDir);
        Assert.NotNull(state);
        state!.CompilerHash = "stale-compiler-hash";
        BuildStateCache.Save(state, _tempDir);

        var warm = Run([a, b]);
        Assert.Equal(2, warm.CompiledFiles.Count);
        Assert.Empty(warm.SkippedOutputs);
    }

    [Fact]
    public void ManifestChange_InvalidatesAllCachedFiles()
    {
        var (a, b) = WriteIndependentPair();
        Run([a, b]);

        // A new effect manifest next to the sources changes the manifest hash.
        File.WriteAllText(Path.Combine(_tempDir, "custom.calor-effects.json"), "{}");

        var warm = Run([a, b]);
        Assert.Equal(2, warm.CompiledFiles.Count);
        Assert.Empty(warm.SkippedOutputs);
    }

    [Fact]
    public void ClearFirst_DiscardsPriorState()
    {
        var (a, b) = WriteIndependentPair();
        Run([a, b]);

        var cleared = Run([a, b], clearFirst: true);
        Assert.Equal(2, cleared.CompiledFiles.Count);
        Assert.Empty(cleared.SkippedOutputs);
    }

    [Fact]
    public void MissingOutput_ForcesRecompileOfThatFileOnly()
    {
        var (a, b) = WriteIndependentPair();
        Run([a, b]);

        File.Delete(Path.ChangeExtension(b, ".g.cs"));

        var warm = Run([a, b]);
        Assert.Equal([b], warm.CompiledFiles);
        Assert.Equal([Path.ChangeExtension(a, ".g.cs")], warm.SkippedOutputs);
    }

    [Fact]
    public void CorruptedOutput_IsNotTrusted_ForcesRecompile()
    {
        // Adversarial probe: the warm path used to check only File.Exists(output),
        // so a corrupted/truncated .g.cs survived as "Up-to-date". The entry now
        // records the output's content hash; any mismatch is a miss.
        var (a, b) = WriteIndependentPair();
        Run([a, b]);

        var aOutput = Path.ChangeExtension(a, ".g.cs");
        File.WriteAllText(aOutput, "// corrupted by an errant process");

        var warm = Run([a, b]);
        Assert.Equal([a], warm.CompiledFiles);
        Assert.Equal([Path.ChangeExtension(b, ".g.cs")], warm.SkippedOutputs);
        // The recompile restored a real output.
        Assert.DoesNotContain("corrupted", File.ReadAllText(aOutput));

        // And the restored output is trusted again on the next run.
        var settled = Run([a, b]);
        Assert.Empty(settled.CompiledFiles);
        Assert.Equal(2, settled.SkippedOutputs.Count);
    }

    [Fact]
    public void FailedFile_IsNotCached_AndRecompilesNextRun()
    {
        var (a, _) = WriteIndependentPair();
        var broken = WriteSource("broken.calr", "§M{m003:Broken\n  not valid calor");

        var cold = Run([a, broken]);
        Assert.True(cold.Result.AnyErrors);

        var warm = Run([a, broken]);
        // The broken file was re-processed, not skipped: only a.calr is a cache hit,
        // and the failure (with its diagnostics) is re-reported on the warm run.
        Assert.Equal([Path.ChangeExtension(a, ".g.cs")], warm.SkippedOutputs);
        Assert.Single(warm.Result.Skipped);
        Assert.True(warm.Result.AnyErrors);
        Assert.True(warm.Diagnostics.HasErrors);
    }

    [Fact]
    public void CrossModuleViolation_IsNeverPublishedOrCached()
    {
        var callee = WriteSource("callee.calr", """
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub} () -> void
                §E{db:w}
            """);
        var caller = WriteSource("caller.calr", """
            §M{m002:App}
              §F{f001:Main:pub} () -> void
                §E{}
                §C{OrderService.SaveOrder} §/C
            """);

        var cold = Run([callee, caller]);
        Assert.True(cold.Result.AnyErrors);
        Assert.Contains(cold.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);

        // Failed aggregate enforcement is not cached or published, so the warm
        // build recompiles both files and reports the violation again.
        var warm = Run([callee, caller]);
        Assert.Empty(warm.Result.Skipped);
        Assert.Empty(warm.CompiledFiles);
        Assert.True(warm.Result.AnyErrors);
        Assert.Contains(warm.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void EntryWithoutEffectSummary_IsNotACacheHit_SoViolationsStillSurface()
    {
        // Adversarial probe: a cached entry whose effect summary is missing (older
        // cache, corruption, manual edit) must not be skipped — skipping would drop
        // the module from cross-module enforcement and Calor0410 would silently
        // disappear on warm builds. Rule: no summary, no hit.
        var callee = WriteSource("callee2.calr", """
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub} () -> void
                §E{db:w}
            """);
        var caller = WriteSource("caller2.calr", """
            §M{m002:App}
              §F{f001:Main:pub} () -> void
                §E{}
                §C{OrderService.SaveOrder} §/C
            """);

        var cold = Run([callee, caller]);
        Assert.True(cold.Result.AnyErrors);

        // Cross-module failures do not persist cache state.
        var state = BuildStateCache.Load(_tempDir);
        Assert.Null(state);

        var warm = Run([callee, caller]);
        Assert.Empty(warm.Result.Skipped);
        Assert.Empty(warm.CompiledFiles);
        Assert.True(warm.Result.AnyErrors);
        Assert.Contains(warm.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void FileWithNonErrorDiagnostics_IsNeverSkipped_SoTheyReappearOnWarmRuns()
    {
        // The pilot-hello-world experimental flag deterministically emits one Info
        // diagnostic (Calor1200) per compilation. A skipped file emits nothing, so
        // diagnostic-producing files must not be cached — otherwise warm builds
        // silently drop their warnings/info.
        var (a, _) = WriteIndependentPair();
        var sources = new List<FileInfo> { new(a) };

        for (var run = 0; run < 2; run++)
        {
            var sink = new DiagnosticBag();
            var result = CompilationDriver.CompileAll(
                sources,
                _ => new CompilationOptions
                {
                    EnforceEffects = false,
                    ExperimentalFlags = new ExperimentalFlags(["pilot-hello-world"])
                },
                crossModuleEnforcement: true,
                crossModulePolicy: UnknownCallPolicy.Strict,
                onCompiled: (file, compileResult) =>
                    File.WriteAllText(Path.ChangeExtension(file.FullName, ".g.cs"), compileResult.GeneratedCode),
                diagnosticSink: sink,
                cache: new CompilationDriver.DriverCacheSettings(
                    _tempDir, "opts", ClearFirst: false,
                    file => Path.ChangeExtension(file.FullName, ".g.cs")));

            Assert.Empty(result.Skipped);
            Assert.Contains(sink, d => d.Code == DiagnosticCode.ExperimentalFlagPilot);
        }
    }

    [Fact]
    public void NoCacheSettings_AlwaysRecompiles()
    {
        var (a, b) = WriteIndependentPair();
        var sources = new List<FileInfo> { new(a), new(b) };

        for (var i = 0; i < 2; i++)
        {
            var result = CompilationDriver.CompileAll(
                sources,
                _ => new CompilationOptions { EnforceEffects = false },
                crossModuleEnforcement: true,
                crossModulePolicy: UnknownCallPolicy.Strict,
                diagnosticSink: new DiagnosticBag());
            Assert.Equal(2, result.Compiled.Count);
            Assert.Empty(result.Skipped);
        }
    }
}

/// <summary>
/// End-to-end incrementality through the real CLI (subprocess): with --cache the
/// second identical invocation reports cache hits; caching is opt-in (a plain
/// compile never caches) and --no-cache overrides --cache.
/// </summary>
public class IncrementalCliEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public IncrementalCliEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-incr-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SecondRun_WithCacheFlag_ReportsUpToDate_AndNoCacheOverrides()
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

        var cold = CliTestHarness.RunCli(_tempDir, "--input", a, "--input", b, "--cache");
        Assert.Equal(0, cold.ExitCode);
        Assert.DoesNotContain("Up-to-date (cached)", cold.StdOut);

        var warm = CliTestHarness.RunCli(_tempDir, "--input", a, "--input", b, "--cache");
        Assert.Equal(0, warm.ExitCode);
        Assert.Contains("Up-to-date (cached)", warm.StdOut);
        Assert.DoesNotContain("Compilation successful", warm.StdOut);

        // --no-cache is the explicit off switch and wins over --cache.
        var uncached = CliTestHarness.RunCli(_tempDir, "--input", a, "--input", b, "--cache", "--no-cache");
        Assert.Equal(0, uncached.ExitCode);
        Assert.DoesNotContain("Up-to-date (cached)", uncached.StdOut);
        Assert.Contains("Compilation successful", uncached.StdOut);
    }

    [Fact]
    public void PlainCompile_DoesNotCache_ByDefault()
    {
        // Policy: incremental caching is opt-in for plain compiles. Without
        // --cache no state file is written and repeat runs always recompile.
        var a = Path.Combine(_tempDir, "a.calr");
        File.WriteAllText(a, """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "hello"
            """);

        for (var run = 0; run < 2; run++)
        {
            var result = CliTestHarness.RunCli(_tempDir, "--input", a);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Compilation successful", result.StdOut);
            Assert.DoesNotContain("Up-to-date (cached)", result.StdOut);
        }

        Assert.False(File.Exists(Path.Combine(_tempDir, ".calor-build-state.json")),
            "plain compile must not write build state without --cache");
    }

    /// <summary>
    /// Design-doc pin <b>P23</b> — <c>BuildStateCache</c>'s three version
    /// constants (<c>BuildStateCache.cs:121-123</c>), frozen with a value each.
    ///
    /// <para>The one that matters is
    /// <c>CurrentCompilerSemanticsVersion</c>. <b>G-CODEGEN</b> (§12.2) is a
    /// feature-wide BLOCKING gate: effect rows must not change a byte of emitted
    /// C#. If they did, the semantics stamp would have to move, so a moved stamp
    /// and an unchanged one cannot both be right — pinning it here means E3 cannot
    /// quietly contradict G-CODEGEN by bumping it.</para>
    ///
    /// <para><c>CurrentFormatVersion</c> moved <c>"3.0"</c> → <c>"4.0"</c> at
    /// <b>E5</b> (v0.15, design §8.5): <c>BuildFileEntry.EffectSummary</c>'s
    /// shape changed — <c>EffectCallerSummary</c> is keyed by structural id
    /// (<c>CallerId</c> + <c>DisplayName</c> replaced <c>CallerName</c>, P26).
    /// One cold rebuild on the first 0.15 build is the mechanism's design.
    /// <c>CurrentOptionsSerializerVersion</c> is untouched: no compile input
    /// changed shape.</para>
    ///
    /// <para>Discriminating revert: bump the semantics stamp and this fails,
    /// naming G-CODEGEN.</para>
    /// </summary>
    [Fact]
    public void BuildStateCacheConstants_FormatBumpedByE5_SemanticsAndOptionsFrozen()
    {
        Assert.Equal("4.0", Calor.Compiler.Incremental.BuildStateCache.CurrentFormatVersion);
        Assert.Equal(
            "calor-compile-semantics-v1",
            Calor.Compiler.Incremental.BuildStateCache.CurrentCompilerSemanticsVersion);
        Assert.Equal(
            "compile-inputs-v3",
            Calor.Compiler.Incremental.BuildStateCache.CurrentOptionsSerializerVersion);
    }

    /// <summary>
    /// Design-doc pin <b>P24</b> — <c>ProjectIndex.CurrentFormatVersion</c> is
    /// <c>"4.0"</c> now that the effects facet is in the index, and the facet is
    /// IN the serialized bytes. Gate 3's instrument compares those bytes between
    /// full and incremental runs; a facet added without the bump would have
    /// moved them silently under a header that still said <c>"3.0"</c>.
    /// Discriminating revert: drop the bump, or serialize the facet under
    /// another name.
    /// </summary>
    [Fact]
    public void ProjectIndexFormatBumped()
    {
        Assert.Equal("4.0", ProjectIndex.CurrentFormatVersion);

        WritePair();
        var options = new ProjectIndexBuilder.Options(
            _tempDir, "p24", ProjectIndexBuilder.DiscoverSources(_tempDir));
        var index = ProjectIndexBuilder.Build(options);
        Assert.Equal(2, index.EffectRows.Count);

        var output = Path.Combine(_tempDir, "index-out");
        index.Save(output);
        var bytes = File.ReadAllText(ProjectIndex.PathFor(output));
        Assert.Contains("\"FormatVersion\": \"4.0\"", bytes);
        Assert.Contains("\"EffectRows\": [", bytes);
        Assert.Contains("\"Verdict\": \"fits\"", bytes);
        Assert.Contains("\"EffectRowsUnavailable\": []", bytes);
    }

    /// <summary>Two independent effectful modules, as <c>IncrementalCliBuildTests.WriteIndependentPair</c> writes them.</summary>
    private (string APath, string BPath) WritePair()
    {
        var a = Path.Combine(_tempDir, "a.calr");
        File.WriteAllText(a, """
            §M{m001:Alpha}
              §F{f001:Greet:pub} () -> void
                §E{cw}
                §P "hello"
            """);
        var b = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(b, """
            §M{m002:Beta}
              §F{f001:Wave:pub} () -> void
                §E{cw}
                §P "wave"
            """);
        return (a, b);
    }

    /// <summary>
    /// Design-doc pin <b>P25</b>, leg 1 — <c>EffectSummaryIsIndexIndependent</c>.
    /// A fresh-clone <c>calor build</c> — the CLI, in a directory with NO
    /// <c>obj/calor</c> and no index anywhere — writes a build state whose
    /// every file entry carries a COMPLETE effect summary: the caller listing
    /// is there, symbol-keyed, with the cross-module call recorded. And no
    /// index appears as a side effect: the summary is a projection of the
    /// compilation's own facts (§8.5), not something read off <c>calor index</c>.
    /// Discriminating revert: derive the summary from the index and this build
    /// either fails or leaves the summary empty.
    /// </summary>
    [Fact]
    public void EffectSummaryIsIndexIndependent()
    {
        var (a, b) = WritePair();
        var c = Path.Combine(_tempDir, "c.calr");
        File.WriteAllText(c, """
            §M{m003:Gamma}
              §F{f001:Call:pub} () -> void
                §E{cw}
                §C{Greet} §/C
            """);
        Assert.Empty(Directory.EnumerateFiles(_tempDir, ".calor-index.json", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "obj")));

        var run = CliTestHarness.RunCli(_tempDir, "--input", a, "--input", b, "--input", c, "--cache");
        Assert.True(run.ExitCode == 0, run.StdOut + run.StdErr);

        var state = BuildStateCache.Load(_tempDir);
        Assert.NotNull(state);
        Assert.Equal(3, state!.Files.Count);
        foreach (var (key, entry) in state.Files)
            Assert.True(entry.EffectSummary != null, $"{key}: no effect summary in the build state");

        var gamma = state.Files.Values.Single(entry => entry.EffectSummary!.ModuleName == "Gamma").EffectSummary!;
        var caller = Assert.Single(gamma.Callers);
        Assert.Equal("f001", caller.CallerId);
        Assert.Equal("Call", caller.DisplayName);
        Assert.Contains(caller.Calls, call => call.Target == "Greet");
        Assert.Contains(gamma.PublicFunctions, function => function.Name == "Call" && function.HasEffectDeclaration);

        Assert.Empty(Directory.EnumerateFiles(_tempDir, ".calor-index.json", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Design-doc pin <b>P25</b>, leg 2 — the structural half: nothing under
    /// <c>Effects/</c> or <c>Incremental/</c> names <c>ProjectIndex</c>, in code
    /// or in a using. Measured before E5 (design §8.5: the index is referenced
    /// from exactly <c>Commands/IndexCommand.cs</c>, <c>Commands/QueryCommand.cs</c>
    /// and <c>Indexing/</c>); frozen here so the dependency cannot grow the
    /// wrong way. Comment lines are not exempt: a doc comment that names the
    /// type is where a <c>cref</c> starts.
    /// </summary>
    [Fact]
    public void EffectsAndIncrementalLayers_DoNotReferenceProjectIndex()
    {
        var root = CliTestHarness.FindRepoRoot();
        var offenders = new List<string>();
        var scanned = 0;
        foreach (var layer in new[] { "Effects", "Incremental" })
        {
            var directory = Path.Combine(root, "src", "Calor.Compiler", layer);
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                scanned++;
                var lines = File.ReadAllLines(file);
                for (var index = 0; index < lines.Length; index++)
                {
                    if (Regex.IsMatch(lines[index], @"\bProjectIndex\w*\b"))
                        offenders.Add($"{layer}/{Path.GetFileName(file)}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        Assert.True(scanned > 10, $"only {scanned} files scanned — wrong root?");
        Assert.True(offenders.Count == 0,
            "Effects/ or Incremental/ references ProjectIndex — the summary must not depend on the index (design §8.5):\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// Design-doc pin <b>P26</b> — <c>NoNameKeyedEffectStoreRemains</c>.
    /// <c>EffectSummaryBuilder</c> used to group a module's call listings under
    /// <c>function.Name</c> and <c>"Class.Method"</c> (its lines 68/75 before
    /// E5): two overloads of one method were ONE caller entry, and the store
    /// was keyed by a string that a rename or a second overload could collide.
    /// Three legs: the POCO has no name key; the builder's source groups by
    /// <c>CallerId</c> and never passes a name as the key; and two overloads
    /// are two entries with two ids and one display name. Discriminating
    /// revert: re-introduce one name key and the third leg collapses to one
    /// entry (and the second leg reads it off the source).
    /// </summary>
    [Fact]
    public void NoNameKeyedEffectStoreRemains()
    {
        Assert.Null(typeof(EffectCallerSummary).GetProperty("CallerName"));
        Assert.NotNull(typeof(EffectCallerSummary).GetProperty("CallerId"));
        Assert.NotNull(typeof(EffectCallerSummary).GetProperty("DisplayName"));
        Assert.NotNull(typeof(RawCall).GetProperty("CallerId"));

        var source = File.ReadAllText(Path.Combine(
            CliTestHarness.FindRepoRoot(), "src", "Calor.Compiler", "Effects", "EffectSummaryBuilder.cs"));
        Assert.Matches(@"callsByCaller\[call\.CallerId\]", source);
        Assert.DoesNotMatch(@"callsByCaller\[[^\]]*\.Name\b", source);
        Assert.DoesNotMatch(@"callerId:\s*(function\.Name|\$""\{cls\.Name\}\.\{method\.Name\}"")", source);

        var diagnostics = new DiagnosticBag();
        var parser = new Parsing.Parser(
            new Parsing.Lexer("""
                §M{m001:Overloads}
                  §CL{c001:Box:pub}
                    §MT{m001:Run:pub} (i32:x) -> void
                      §E{cw}
                      §C{System.Console.WriteLine} §A x §/C
                    §MT{m002:Run:pub} (str:s) -> void
                      §E{cw}
                      §C{System.Console.WriteLine} §A s §/C
                """, diagnostics).TokenizeAllForParser(), diagnostics);
        var module = parser.Parse();
        Assert.NotNull(module);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var summary = EffectSummaryBuilder.Build(module!);
        Assert.Equal(
            new[] { "Box.m001", "Box.m002" },
            summary.Callers.Select(caller => caller.CallerId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.All(summary.Callers, caller => Assert.Equal("Box.Run", caller.DisplayName));
        Assert.All(summary.Callers, caller => Assert.Single(caller.Calls));
    }
}
