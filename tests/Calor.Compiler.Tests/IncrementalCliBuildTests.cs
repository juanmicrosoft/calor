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

    [Fact]
    public void RootAndWatchEffectiveTypingAndElision_ChangeTheSharedOptionsIdentity()
    {
        static string Token(bool typing, bool elide) => Program.BuildOptionsToken(
            strictApi: false, requireDocs: false, enforceEffects: false,
            enableTypeChecking: typing, strictEffects: false, permissiveEffects: false,
            contractMode: "debug", verify: false, verificationTimeout: 5000,
            analyze: false, allFindings: false, strictBindInference: false,
            experimentalFlags: null, elideProvenGuards: elide);

        var previous = Environment.GetEnvironmentVariable("CALOR_NO_TYPE_CHECK");
        try
        {
            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", "0");
            var enabled = Program.IsTypeCheckingEffectivelyEnabled(noTypeCheck: false, transpileOnly: false);
            Assert.True(enabled);
            Assert.False(Program.IsTypeCheckingEffectivelyEnabled(noTypeCheck: true, transpileOnly: false));
            Assert.False(Program.IsTypeCheckingEffectivelyEnabled(noTypeCheck: false, transpileOnly: true));
            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", "1");
            var disabled = Program.IsTypeCheckingEffectivelyEnabled(noTypeCheck: false, transpileOnly: false);
            Assert.False(disabled);
            Assert.NotEqual(Token(enabled, true), Token(disabled, true));
            Assert.NotEqual(Token(enabled, true), Token(enabled, false));
            Assert.NotEqual(BuildStateCache.ComputeOptionsHash(Token(enabled, true)),
                BuildStateCache.ComputeOptionsHash(Token(disabled, true)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", previous);
        }
    }

    private (string APath, string BPath) WriteIndependentPair()
    {
        var a = WriteSource("a.calr", """
            module Alpha Alpha block
              func Greet () → void
                ! cw
                Console.WriteLine("hello")
            """);
        var b = WriteSource("b.calr", """
            module Beta Beta block
              func Wave () → void
                ! cw
                Console.WriteLine("wave")
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
            module Alpha Alpha block
              func Greet () → void
                ! cw
                Console.WriteLine("changed")
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
            module Alpha Alpha block
              func Greet () → void
                ! cw
                Console.WriteLine("edited mid-compile")
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
    public void CrossModuleViolation_IsNeverPublishedOrCached()
    {
        var callee = WriteSource("callee.calr", """
            module OrderService OrderService block
              func SaveOrder () → void
                ! db:w
                return
            """);
        var caller = WriteSource("caller.calr", """
            module App App block
              func Main () → void
                !
                OrderService.SaveOrder()
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
            module OrderService OrderService block
              func SaveOrder () → void
                ! db:w
                return
            """);
        var caller = WriteSource("caller2.calr", """
            module App App block
              func Main () → void
                !
                OrderService.SaveOrder()
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
            module Alpha Alpha block
              func Greet () → void
                ! cw
                Console.WriteLine("hello")
            """);
        File.WriteAllText(b, """
            module Beta Beta block
              func Wave () → void
                ! cw
                Console.WriteLine("wave")
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
            module Alpha Alpha block
              func Greet () → void
                ! cw
                Console.WriteLine("hello")
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

    [Fact]
    public void CacheFlag_WithExplicitOutput_StillDoesNotUseIncrementalBuildCache()
    {
        var source = Path.Combine(_tempDir, "explicit-output.calr");
        var output = Path.Combine(_tempDir, "explicit-output.cs");
        File.WriteAllText(source, """
            module ExplicitOutput ExplicitOutput block
              func Main () → void
                ! cw
                Console.WriteLine("hello")
            """);

        for (var run = 0; run < 2; run++)
        {
            var result = CliTestHarness.RunCli(
                _tempDir, "--input", source, "--output", output, "--cache");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Compilation successful", result.StdOut);
            Assert.DoesNotContain("Up-to-date (cached)", result.StdOut);
        }

        Assert.False(File.Exists(Path.Combine(_tempDir, ".calor-build-state.json")),
            "explicit -o uses a redirected output and must stay outside the default-layout cache");
    }

    [Fact]
    public void Cache_NoTypeCheckCannotBypassActiveScalarBinding()
    {
        var source = WriteTransitionalNullableAssignmentViolation();

        var firstDefault = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = "0" },
            "--input", source, "--cache");
        Assert.NotEqual(0, firstDefault.ExitCode);
        Assert.Contains("Calor0272", firstDefault.StdOut + firstDefault.StdErr);
        var stateAfterFailure = BuildStateCache.Load(_tempDir);
        if (stateAfterFailure != null)
            Assert.Empty(stateAfterFailure.Files);

        var optedOut = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = "0" },
            "--input", source, "--cache", "--no-type-check");
        Assert.NotEqual(0, optedOut.ExitCode);
        Assert.Contains("Calor0272", optedOut.StdOut + optedOut.StdErr);
        var stateAfterOptOut = BuildStateCache.Load(_tempDir);
        if (stateAfterOptOut != null)
            Assert.Empty(stateAfterOptOut.Files);

        var defaultCompile = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = "0" },
            "--input", source, "--cache");
        Assert.NotEqual(0, defaultCompile.ExitCode);
        Assert.Contains("Calor0272", defaultCompile.StdOut + defaultCompile.StdErr);
        Assert.DoesNotContain("Up-to-date (cached)", defaultCompile.StdOut);
    }

    [Fact]
    public void Cache_EnvironmentOptOutCannotBypassActiveScalarBinding()
    {
        var source = WriteTransitionalNullableAssignmentViolation();

        var envOptOut = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = "1" },
            "--input", source, "--cache");
        Assert.NotEqual(0, envOptOut.ExitCode);
        Assert.Contains("Calor0272", envOptOut.StdOut + envOptOut.StdErr);
        var stateAfterOptOut = BuildStateCache.Load(_tempDir);
        if (stateAfterOptOut != null)
            Assert.Empty(stateAfterOptOut.Files);

        var defaultCompile = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = "0" },
            "--input", source, "--cache");
        Assert.NotEqual(0, defaultCompile.ExitCode);
        Assert.Contains("Calor0272", defaultCompile.StdOut + defaultCompile.StdErr);
        Assert.DoesNotContain("Up-to-date (cached)", defaultCompile.StdOut);
    }

    private string WriteTransitionalNullableAssignmentViolation()
    {
        var path = Path.Combine(_tempDir, "nullable-assignment.calr");
        File.WriteAllText(path, """
            module N0 N0 block
              func Probe () → void
                !
                let string?:x = null
                let string:y = x
            """);
        return path;
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
    /// <c>CurrentOptionsSerializerVersion</c> moved to <c>"compile-inputs-v4"</c>
    /// because #1396 added the effective type-checking and guard-elision policy
    /// inputs to the CLI/watch cache token.</para>
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
            "compile-inputs-v4",
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
            module Alpha Alpha block
              func Greet () → void
                ! cw
                Console.WriteLine("hello")
            """);
        var b = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(b, """
            module Beta Beta block
              func Wave () → void
                ! cw
                Console.WriteLine("wave")
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
            module Gamma Gamma block
              func Call () → void
                ! cw
                Greet()
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
        var identity = new Calor.Compiler.Ids.DeclarationIdentityStore(_tempDir).Preview([])
            .Declarations.Single(declaration => declaration.File == "c.calr" && declaration.Name == "Call").Id;
        Assert.Equal(identity, caller.CallerId);
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
                module Overloads Overloads block
                  class Box
                    func Run (int:x) → void
                      ! cw
                      System.Console.WriteLine(x)

                    func Run (string:s) → void
                      ! cw
                      System.Console.WriteLine(s)
                """, diagnostics).TokenizeAllForParser(), diagnostics);
        var module = parser.Parse();
        Assert.NotNull(module);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var summary = EffectSummaryBuilder.Build(module!);
        Assert.Equal(
            Assert.Single(module.Classes).Methods.Select(method => $"Box.{method.Id}")
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            summary.Callers.Select(caller => caller.CallerId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.All(summary.Callers, caller => Assert.Equal("Box.Run", caller.DisplayName));
        Assert.All(summary.Callers, caller => Assert.Single(caller.Calls));
    }
}

public sealed class NativeProjectCacheTests : IDisposable
{
    private string _root = Path.Combine(Path.GetTempPath(), "calor-native-cache-" + Guid.NewGuid().ToString("N"));
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public NativeProjectCacheTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    private const string Library = """
        module Library Contracts block
          module Arithmetic public
            func Pick public (int:value) → int
              !
              return (value + 1)
            func Name public () → string
              !
              return "Ada"
        """;

    private const string Caller = """
        module Caller Consumer block
          module Entry public
            func Probe public () → int
              !
              return Contracts.Arithmetic.Pick(41)
            func Read public () → string
              !
              return Contracts.Arithmetic.Name()
        """;

    private static string Independent(string name, int value) =>
        $"module {name}\n  module {name}\n    func Answer public () → int\n      !\n      return {value}\n";

    private void Write(string name, string source) => File.WriteAllText(Path.Combine(_root, name + ".calr"), source);

    private (CompilationDriver.DriverResult Result, DiagnosticBag Diagnostics) Run(
        bool clear = false, string token = "native-cache", bool enforceEffects = true)
    {
        var diagnostics = new DiagnosticBag();
        var attempted = new List<string>();
        var result = CompilationDriver.CompileAll(
            Directory.GetFiles(_root, "*.calr").Order(StringComparer.Ordinal).Select(path => new FileInfo(path)).ToArray(),
            _ => new CompilationOptions
            {
                EnforceEffects = enforceEffects,
                ContractMode = ContractMode.Off,
                StatusWriter = TextWriter.Null
            },
            crossModuleEnforcement: true, crossModulePolicy: UnknownCallPolicy.Strict,
            onCompiled: (file, compiled) =>
                File.WriteAllText(Path.ChangeExtension(file.FullName, ".g.cs"), compiled.GeneratedCode),
            diagnosticSink: diagnostics,
            cache: new(_root, token + ":effects=" + enforceEffects, clear,
                file => Path.ChangeExtension(file.FullName, ".g.cs")),
            onFileResult: (file, _) => attempted.Add(file.Name));
        var statePath = BuildStateCache.GetCachePath(_root);
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            Recompiled = attempted,
            Compiled = result.Compiled.Select(item => item.File.Name),
            Skipped = result.Skipped.Select(file => file.Name),
            result.AnyErrors,
            Diagnostics = Snapshot(diagnostics),
            CacheStateExists = File.Exists(statePath),
            State = File.Exists(statePath) ? File.ReadAllText(statePath) : null
        }));
        return (result, diagnostics);
    }

    private static string[] Snapshot(DiagnosticBag diagnostics) => diagnostics.Select(diagnostic =>
        $"{diagnostic.FilePath}|{diagnostic.Code}|{diagnostic.Severity}|{diagnostic.Span}|{diagnostic.Message}")
        .Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void IndependentBodiesKeepRealHitsForSingleAndBatchedEdits()
    {
        Write("a", Independent("Alpha", 1));
        Write("b", Independent("Beta", 2));
        Write("c", Independent("Gamma", 3));
        Assert.Equal(3, Run().Result.Compiled.Count);
        Assert.Equal(3, Run().Result.Skipped.Count);
        var unchanged = File.ReadAllText(Path.Combine(_root, "c.g.cs"));
        Write("a", Independent("Alpha", 11));
        var one = Run();
        Assert.Equal("a.calr", Assert.Single(one.Result.Compiled).File.Name);
        Assert.Equal(2, one.Result.Skipped.Count);
        Write("a", Independent("Alpha", 12));
        Write("b", Independent("Beta", 22));
        var two = Run();
        Assert.Equal(2, two.Result.Compiled.Count);
        Assert.Equal("c.calr", Assert.Single(two.Result.Skipped).Name);
        Assert.Equal(unchanged, File.ReadAllText(Path.Combine(_root, "c.g.cs")));
        Assert.False(two.Result.AnyErrors, string.Join("\n", two.Diagnostics));
    }

    [Fact]
    public void RelocatedProjectsDoNotReuseOldSourceLocations()
    {
        Write("a", Independent("Alpha", 1));
        Write("b", Independent("Beta", 2));
        Assert.Equal(2, Run().Result.Compiled.Count);
        Assert.Equal(2, Run().Result.Skipped.Count);
        var oldRoot = _root;
        var movedRoot = _root + "-moved";
        Directory.Move(_root, movedRoot);
        _root = movedRoot;
        var moved = Run();
        Assert.False(moved.Result.AnyErrors, string.Join("\n", moved.Diagnostics));
        Assert.Equal(2, moved.Result.Compiled.Count);
        Assert.Empty(moved.Result.Skipped);
        Assert.DoesNotContain(oldRoot + Path.DirectorySeparatorChar,
            File.ReadAllText(Path.Combine(_root, "a.g.cs")));
    }

    [Theory]
    [InlineData("body")]
    [InlineData("return")]
    [InlineData("nullable")]
    [InlineData("row")]
    [InlineData("unsafe-body")]
    [InlineData("owner")]
    [InlineData("namespace")]
    [InlineData("parameter")]
    [InlineData("alias")]
    public void SourceEditsInvalidateRealConsumersAndMatchColdDiagnostics(string edit)
    {
        Write("library", Library);
        Write("caller", Caller);
        var cold = Run();
        Assert.False(cold.Result.AnyErrors, string.Join("\n", cold.Diagnostics));
        Assert.Equal(2, cold.Result.Compiled.Count);
        Assert.Equal(2, Run().Result.Skipped.Count);
        var changed = edit switch
        {
            "body" => Library.Replace("(value + 1)", "(value + 2)", StringComparison.Ordinal),
            "return" => Library.Replace("→ int", "→ string", StringComparison.Ordinal)
                .Replace("(value + 1)", "\"answer\"", StringComparison.Ordinal),
            "nullable" => Library.Replace("→ string", "→ string?", StringComparison.Ordinal)
                .Replace("\"Ada\"", "null", StringComparison.Ordinal),
            "row" => Library.Replace("(int:value) → int\n      !", "(int:value) → int\n      ! cw", StringComparison.Ordinal),
            "unsafe-body" => Library.Replace("return (value + 1)",
                "System.Console.WriteLine(value)\n      return (value + 1)", StringComparison.Ordinal),
            "owner" => Library.Replace("module Arithmetic public", "module Renamed public", StringComparison.Ordinal),
            "namespace" => Library.Replace("Contracts block", "Moved block", StringComparison.Ordinal),
            "parameter" => Library.Replace("int:value", "bool:value", StringComparison.Ordinal)
                .Replace("(value + 1)", "42", StringComparison.Ordinal),
            "alias" => Library.Replace("module Library Contracts block",
                    "module Library Contracts block\n  namespace using Input = System.Boolean", StringComparison.Ordinal)
                .Replace("int:value", "Input:value", StringComparison.Ordinal)
                .Replace("(value + 1)", "42", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(edit))
        };
        Write("library", changed);
        var warm = Run();
        Assert.Empty(warm.Result.Skipped);
        Assert.Equal(edit != "body", warm.Result.AnyErrors);
        if (edit == "body")
            Assert.Equal(2, warm.Result.Compiled.Count);
        var fresh = Run(clear: true);
        Assert.Equal(Snapshot(fresh.Diagnostics), Snapshot(warm.Diagnostics));
        Assert.Equal(fresh.Result.AnyErrors, warm.Result.AnyErrors);
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("partial")]
    [InlineData("field")]
    [InlineData("extension")]
    [InlineData("generic")]
    [InlineData("global-alias")]
    [InlineData("constant")]
    public void UnprovenSourceContextsRetainFullSnapshotInvalidation(string kind)
    {
        var source = kind switch
        {
            "instance" => Library.Replace("module Arithmetic public", "class Arithmetic public", StringComparison.Ordinal),
            "partial" => Library.Replace("module Arithmetic public", "module Arithmetic public partial", StringComparison.Ordinal),
            "field" => Library.Replace("module Arithmetic public",
                "module Arithmetic public\n    field int:Seed public static = 1", StringComparison.Ordinal),
            "extension" => Library.Replace("int:value", "this int:value", StringComparison.Ordinal),
            "generic" => Library.Replace("module Arithmetic public", "class Arithmetic<T> public", StringComparison.Ordinal),
            "global-alias" => Library.Replace("module Library Contracts block",
                "module Library Contracts block\n  global using Text = System.String", StringComparison.Ordinal),
            "constant" => Library.Replace("module Arithmetic public",
                "module Arithmetic public\n    field int:Seed public const = 1", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Write("library", source);
        Write("peer", Independent("Peer", 7));
        var cold = Run();
        Assert.False(cold.Result.AnyErrors, string.Join("\n", cold.Diagnostics));
        Assert.Equal(2, Run().Result.Skipped.Count);
        Write("library", kind == "constant"
            ? source.Replace("Seed public const = 1", "Seed public const = 2", StringComparison.Ordinal)
            : source.Replace("(value + 1)", "(value + 2)", StringComparison.Ordinal));
        var changed = Run();
        Assert.False(changed.Result.AnyErrors, string.Join("\n", changed.Diagnostics));
        Assert.Equal(2, changed.Result.Compiled.Count);
        Assert.Empty(changed.Result.Skipped);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("missing-output")]
    [InlineData("summary")]
    [InlineData("manifest")]
    [InlineData("options")]
    public void GenuineCacheHitsStillRequireAllExistingIntegrityEvidence(string change)
    {
        Write("a", Independent("Alpha", 1));
        Write("b", Independent("Beta", 2));
        Assert.Equal(2, Run().Result.Compiled.Count);
        if (change == "output")
            File.AppendAllText(Path.Combine(_root, "a.g.cs"), "\n// external modification\n");
        else if (change == "missing-output")
            File.Delete(Path.Combine(_root, "a.g.cs"));
        else if (change == "summary")
        {
            var state = BuildStateCache.Load(_root)!;
            state.Files["a.calr"].EffectSummary = null;
            BuildStateCache.Save(state, _root);
        }
        else if (change == "manifest")
            File.WriteAllText(Path.Combine(_root, "local.calor-effects.json"), "{}");
        var result = Run(token: change == "options" ? "changed-options" : "native-cache");
        Assert.False(result.Result.AnyErrors, string.Join("\n", result.Diagnostics));
        var global = change is "manifest" or "options";
        Assert.Equal(global ? 2 : 1, result.Result.Compiled.Count);
        Assert.Equal(global ? 0 : 1, result.Result.Skipped.Count);
    }

    [Fact]
    public void CallerBodyEditsKeepTheUnchangedLibraryAsARealHit()
    {
        Write("library", Library);
        Write("caller", Caller);
        Assert.False(Run().Result.AnyErrors);
        Assert.Equal(2, Run().Result.Skipped.Count);
        Write("caller", Caller.Replace("Pick(41)", "Pick(42)", StringComparison.Ordinal));
        var changed = Run();
        Assert.False(changed.Result.AnyErrors, string.Join("\n", changed.Diagnostics));
        Assert.Equal("caller.calr", Assert.Single(changed.Result.Compiled).File.Name);
        Assert.Equal("library.calr", Assert.Single(changed.Result.Skipped).Name);
    }

    [Fact]
    public void FullySkippedOutputsStillEnforceTheirCachedDeclaredEffectRows()
    {
        // 0.30: a public function must declare a row (Calor0124); an internal one may omit
        // it, which is what the cross-module pass reports as Calor0417 on every build.
        Write("library", """
            module Store Storage
              func Write internal () → void
                return
            """);
        Write("caller", """
            module Caller Client
              func Main public () → void
                !
                Store.Write()
            """);
        var cold = Run(enforceEffects: false);
        Assert.False(cold.Result.AnyErrors, string.Join("\n", cold.Diagnostics));
        Assert.Equal(2, cold.Result.Compiled.Count);
        Assert.Contains(cold.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndeclaredPublicFunction);
        var warm = Run(enforceEffects: false);
        Assert.False(warm.Result.AnyErrors, string.Join("\n", warm.Diagnostics));
        Assert.Empty(warm.Result.Compiled);
        Assert.Equal(2, warm.Result.Skipped.Count);
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(warm.Diagnostics));
    }

    [Fact]
    public void CrossModuleViolationNeverPublishesOrCreatesAReusableState()
    {
        Write("library", """
            module Store Storage
              func Write public () → void
                ! db:w
            """);
        Write("caller", """
            module Caller Client
              func Main public () → void
                !
                Store.Write()
            """);
        var cold = Run(enforceEffects: false);
        Assert.True(cold.Result.AnyErrors);
        Assert.Contains(cold.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Empty(cold.Result.Compiled);
        Assert.Null(BuildStateCache.Load(_root));
        var warm = Run(enforceEffects: false);
        Assert.True(warm.Result.AnyErrors);
        Assert.Empty(warm.Result.Skipped);
        Assert.Empty(warm.Result.Compiled);
        Assert.Null(BuildStateCache.Load(_root));
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(warm.Diagnostics));
    }

    [Fact]
    public void ConditionalCallerRebindsAliasChangesAndMatchesColdRejection()
    {
        const string library = """
            module Library Contracts block
              namespace using Result = System.Int32
              module Arithmetic public
                func Pick public (int:value) → Result
                  !
                  return 0
            """ + "\n";
        Write("library", library);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Value public () → int
                  !
                  pp NEVER
                    return Contracts.Arithmetic.Pick(41)
                  return 7
            """ + "\n");
        var initial = Run();
        Assert.False(initial.Result.AnyErrors, string.Join("\n", initial.Diagnostics));
        Assert.Equal(2, initial.Result.Compiled.Count);
        Assert.Equal(2, Run().Result.Skipped.Count);
        var statePath = BuildStateCache.GetCachePath(_root);
        var callerOutput = File.ReadAllText(Path.Combine(_root, "caller.g.cs"));

        Write("library", library.Replace("System.Int32", "System.Int64", StringComparison.Ordinal));
        var warm = Run();
        Assert.True(warm.Result.AnyErrors, string.Join("\n", warm.Diagnostics));
        Assert.Equal("library.calr", Assert.Single(warm.Result.Compiled).File.Name);
        Assert.Empty(warm.Result.Skipped);
        Assert.Equal(new[] { "Calor0410", "Calor0411", "Calor0411" },
            warm.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal));
        Assert.Equal("library.calr", Assert.Single(BuildStateCache.Load(_root)!.Files).Key);
        Assert.Equal(callerOutput, File.ReadAllText(Path.Combine(_root, "caller.g.cs")));
        var state = File.ReadAllText(statePath);
        var outputs = Directory.GetFiles(_root, "*.g.cs").ToDictionary(path => path, File.ReadAllText);

        var repeated = Run();
        Assert.True(repeated.Result.AnyErrors);
        Assert.Empty(repeated.Result.Compiled);
        Assert.Equal("library.calr", Assert.Single(repeated.Result.Skipped).Name);

        var cold = Run(clear: true);
        Assert.True(cold.Result.AnyErrors);
        Assert.Equal("library.calr", Assert.Single(cold.Result.Compiled).File.Name);
        Assert.Empty(cold.Result.Skipped);
        Assert.Equal(state, File.ReadAllText(statePath));
        Assert.All(outputs, output => Assert.Equal(output.Value, File.ReadAllText(output.Key)));
        var warmFields = System.Text.Json.JsonSerializer.Serialize(warm.Diagnostics.ToArray());
        var coldFields = System.Text.Json.JsonSerializer.Serialize(cold.Diagnostics.ToArray());
        _output.WriteLine("ConditionalWarmDiagnostics:" + warmFields);
        _output.WriteLine("ConditionalColdDiagnostics:" + coldFields);
        Assert.Equal(coldFields, warmFields);
        Assert.Equal(coldFields, System.Text.Json.JsonSerializer.Serialize(repeated.Diagnostics.ToArray()));
    }

    [Fact]
    public void NativeGlobalFunctionFragmentsKeepRealHitsAndExactColdArtifacts()
    {
        const string left = "module Left\n  func LeftValue public () → int\n    !\n    return 1\n";
        const string right = "module Right\n  func RightValue public () → int\n    !\n    return 42\n";
        Write("left", left);
        Write("right", right);
        var initial = Run();
        Assert.False(initial.Result.AnyErrors, string.Join("\n", initial.Diagnostics));
        Assert.Equal(2, initial.Result.Compiled.Count);
        Assert.Equal(2, Run().Result.Skipped.Count);
        var rightPath = Path.Combine(_root, "right.g.cs");
        var originalRight = File.ReadAllText(rightPath);

        Write("left", left.Replace("return 1", "return 11", StringComparison.Ordinal));
        var changed = Run();
        Assert.False(changed.Result.AnyErrors, string.Join("\n", changed.Diagnostics));
        Assert.Equal("left.calr", Assert.Single(changed.Result.Compiled).File.Name);
        Assert.Equal("right.calr", Assert.Single(changed.Result.Skipped).Name);
        Assert.Equal(originalRight, File.ReadAllText(rightPath));
        var generated = Directory.GetFiles(_root, "*.g.cs").ToDictionary(path => path, File.ReadAllText);
        var runtime = Execute();
        Assert.Equal("System.Int32 LeftValue()=11|System.Int32 RightValue()=42", runtime);

        var cold = Run(clear: true);
        Assert.False(cold.Result.AnyErrors, string.Join("\n", cold.Diagnostics));
        Assert.Empty(cold.Result.Skipped);
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(changed.Diagnostics));
        Assert.All(generated, output => Assert.Equal(output.Value, File.ReadAllText(output.Key)));
        Assert.Equal(runtime, Execute());

        string Execute()
        {
            var trees = Directory.GetFiles(_root, "*.g.cs")
                .Select(path => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path)));
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "SharedNativeCache_" + Guid.NewGuid().ToString("N"), trees,
                CodeGen.GeneratedCSharpCompiler.References,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var context = new System.Runtime.Loader.AssemblyLoadContext(null, isCollectible: true);
            try
            {
                stream.Position = 0;
                var assembly = context.LoadFromStream(stream);
                var owner = assembly.GetType("GlobalModule", throwOnError: true)!;
                return string.Join("|", owner.GetMethods(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                    .OrderBy(method => method.Name, StringComparer.Ordinal)
                    .Select(method => $"{method.ReturnType.FullName} {method.Name}()={method.Invoke(null, null)}"));
            }
            finally
            {
                context.Unload();
            }
        }
    }

    private static string Namespaced(string module, string ns, string function, int value) =>
        $"module {module} {ns} block\n  func {function} public () → int\n    !\n    return {value}\n";

    [Fact]
    public void NamespacedModuleWrappersKeepRealHitsAndExactColdArtifacts()
    {
        // #1732: `module Alpha Alpha block` emits
        // `namespace Alpha { public static partial class AlphaModule }`. The
        // independence proof accepts that wrapper, so an edit to one namespaced
        // module spares an unrelated one, byte for byte.
        Write("alpha", Namespaced("Alpha", "Alpha", "Greet", 1));
        Write("beta", Namespaced("Beta", "Beta", "Wave", 2));
        Assert.Equal(2, Run().Result.Compiled.Count);
        Assert.Equal(2, Run().Result.Skipped.Count);
        var betaPath = Path.Combine(_root, "beta.g.cs");
        var beta = File.ReadAllText(betaPath);
        Assert.Contains("public static partial class BetaModule", beta);

        Write("alpha", Namespaced("Alpha", "Alpha", "Greet", 11));
        var changed = Run();
        Assert.False(changed.Result.AnyErrors, string.Join("\n", changed.Diagnostics));
        Assert.Equal("alpha.calr", Assert.Single(changed.Result.Compiled).File.Name);
        Assert.Equal("beta.calr", Assert.Single(changed.Result.Skipped).Name);
        Assert.Equal(beta, File.ReadAllText(betaPath));
        var generated = Directory.GetFiles(_root, "*.g.cs").ToDictionary(path => path, File.ReadAllText);
        var state = CanonicalState();

        var cold = Run(clear: true);
        Assert.Empty(cold.Result.Skipped);
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(changed.Diagnostics));
        Assert.All(generated, output => Assert.Equal(output.Value, File.ReadAllText(output.Key)));
        Assert.Equal(state, CanonicalState());

        string CanonicalState()
        {
            var loaded = BuildStateCache.Load(_root)!;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                loaded.CrossModuleMapHash,
                loaded.CrossModuleFunctionMapHash,
                Files = loaded.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new { pair.Key, pair.Value.ContentHash, pair.Value.OutputContentHash }),
                Evidence = loaded.ValidatedOutputs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new { pair.Key, pair.Value.OutputContentHash, pair.Value.IsNativeGlobalModule })
            });
        }
    }

    [Theory]
    [InlineData("same-namespace")]
    [InlineData("nested-namespace")]
    public void SharedNamespacedWrapperIsNeverSpared(string shape)
    {
        // Two modules with the same namespace emit the SAME partial class
        // (Alpha.AlphaModule): an edit to one can change member lookup in the
        // other, so the other is recompiled. A nested namespace ending in the same
        // segment emits a different type (Outer.Alpha.AlphaModule) but the proof
        // compares simple names, so it is not spared either.
        Write("alpha", Namespaced("Alpha", "Alpha", "Greet", 1));
        Write("gamma", Namespaced("Gamma", shape == "same-namespace" ? "Alpha" : "Outer.Alpha", "Other", 3));
        Write("beta", Namespaced("Beta", "Beta", "Wave", 2));
        Assert.Equal(3, Run().Result.Compiled.Count);
        Assert.Equal(3, Run().Result.Skipped.Count);

        Write("gamma", Namespaced("Gamma", shape == "same-namespace" ? "Alpha" : "Outer.Alpha", "Other", 4));
        var changed = Run();
        Assert.False(changed.Result.AnyErrors, string.Join("\n", changed.Diagnostics));
        Assert.Equal(new[] { "alpha.calr", "gamma.calr" },
            changed.Result.Compiled.Select(item => item.File.Name).Order(StringComparer.Ordinal));
        Assert.Equal("beta.calr", Assert.Single(changed.Result.Skipped).Name);
        var cold = Run(clear: true);
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(changed.Diagnostics));
    }

    [Theory]
    [InlineData("row")]
    [InlineData("alias")]
    [InlineData("conditional-alias")]
    public void SharedGlobalOwnerStillRevalidatesSourceContractsAndDisabledBranches(string change)
    {
        const string library = """
            module Library
              global using Result = System.Int32
              func Pick public (int:value) → Result
                !
                return value
            """ + "\n";
        var caller = change == "conditional-alias"
            ? """
              module Caller
                func Value public () → int
                  !
                  pp NEVER
                    return Pick(41)
                  return 7
              """ + "\n"
            : """
              module Caller
                func Value public () → int
                  !
                  return Pick(41)
              """ + "\n";
        Write("library", library);
        Write("caller", caller);
        var initial = Run();
        Assert.False(initial.Result.AnyErrors, string.Join("\n", initial.Diagnostics));
        Assert.Equal(2, initial.Result.Compiled.Count);
        Assert.Equal(2, Run().Result.Skipped.Count);

        Write("library", change == "row"
            ? library.Replace("    !\n", "    ! cw\n", StringComparison.Ordinal)
            : library.Replace("System.Int32", "System.Int64", StringComparison.Ordinal));
        var changed = Run();
        Assert.Equal(change != "conditional-alias", changed.Result.AnyErrors);
        Assert.Empty(changed.Result.Skipped);
        var cold = Run(clear: true);
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(changed.Diagnostics));
        Assert.Equal(cold.Result.AnyErrors, changed.Result.AnyErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedGlobalOwnerRequiresNativeProvenanceInBothSnapshots(bool rawFirst)
    {
        const string native = "module Left\n  func LeftValue public () → int\n    !\n    return 1\n";
        const string payload = "return 2;";
        var raw = "module Left\n  func LeftValue public () → int\n    !\n"
            + $"    raw member:post-validation-fallback:Other:opaque%20body {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
        Write("left", rawFirst ? raw : native);
        Write("right", "module Right\n  func RightValue public () → int\n    !\n    return 42\n");
        var initial = Run(enforceEffects: false);
        Assert.False(initial.Result.AnyErrors, string.Join("\n", initial.Diagnostics));
        Assert.Equal(2, initial.Result.Compiled.Count);
        Write("left", rawFirst ? native : raw);
        var changed = Run(enforceEffects: false);
        Assert.False(changed.Result.AnyErrors, string.Join("\n", changed.Diagnostics));
        Assert.Empty(changed.Result.Skipped);
        Assert.Equal(2, changed.Result.Compiled.Count);
        var cold = Run(clear: true, enforceEffects: false);
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(changed.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedGlobalOwnerInitializationCannotUseMemberNameDisjointness(bool explicitConstructor)
    {
        var stateful = explicitConstructor
            ? """
              module Library
                module GlobalModule public partial
                  field int:Seed public static
                  ctor static ()
                    set Seed 1
              """ + "\n"
            : """
              module Library
                module GlobalModule public partial
                  field int:Seed public static = 1
              """ + "\n";
        Write("state", stateful);
        Write("caller", "module Caller\n  func Answer public () → int\n    !\n    return 42\n");
        var initial = Run(enforceEffects: false);
        Assert.False(initial.Result.AnyErrors, string.Join("\n", initial.Diagnostics));
        Assert.Equal(2, initial.Result.Compiled.Count);
        Assert.Equal(2, Run(enforceEffects: false).Result.Skipped.Count);
        var before = BuildStateCache.Load(_root)!;
        var callerOutput = File.ReadAllText(Path.Combine(_root, "caller.g.cs"));
        Assert.DoesNotContain("Seed", callerOutput);

        Write("state", stateful.Replace(explicitConstructor ? "Seed 1" : "= 1",
            explicitConstructor ? "Seed 2" : "= 2", StringComparison.Ordinal));
        var changed = Run(enforceEffects: false);
        Assert.False(changed.Result.AnyErrors, string.Join("\n", changed.Diagnostics));
        Assert.Equal(before.CrossModuleFunctionMapHash, BuildStateCache.Load(_root)!.CrossModuleFunctionMapHash);
        Assert.Empty(changed.Result.Skipped);
        Assert.Equal(2, changed.Result.Compiled.Count);
        Assert.Equal(callerOutput, File.ReadAllText(Path.Combine(_root, "caller.g.cs")));
        var cold = Run(clear: true, enforceEffects: false);
        Assert.Equal(Snapshot(cold.Diagnostics), Snapshot(changed.Diagnostics));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
