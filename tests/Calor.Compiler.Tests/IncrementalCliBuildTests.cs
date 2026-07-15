using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
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

    private RunOutcome Run(string[] files, string optionsToken = "opts", bool clearFirst = false)
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
    public void CrossModuleViolation_SurfacesFromCachedSummaries_OnFullySkippedWarmBuild()
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

        // Warm build: both files are skipped, yet the violation must still be
        // reported — cross-module enforcement runs over the cached summaries.
        var warm = Run([callee, caller]);
        Assert.Empty(warm.CompiledFiles);
        Assert.Equal(2, warm.Result.Skipped.Count);
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
/// End-to-end incrementality through the real CLI (subprocess): the second
/// identical invocation reports cache hits, and --no-cache suppresses them.
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
    public void SecondRun_ReportsUpToDate_AndNoCacheDisablesIt()
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

        var cold = CliTestHarness.RunCli(_tempDir, "--input", a, "--input", b);
        Assert.Equal(0, cold.ExitCode);
        Assert.DoesNotContain("Up-to-date (cached)", cold.StdOut);

        var warm = CliTestHarness.RunCli(_tempDir, "--input", a, "--input", b);
        Assert.Equal(0, warm.ExitCode);
        Assert.Contains("Up-to-date (cached)", warm.StdOut);
        Assert.DoesNotContain("Compilation successful", warm.StdOut);

        var uncached = CliTestHarness.RunCli(_tempDir, "--input", a, "--input", b, "--no-cache");
        Assert.Equal(0, uncached.ExitCode);
        Assert.DoesNotContain("Up-to-date (cached)", uncached.StdOut);
        Assert.Contains("Compilation successful", uncached.StdOut);
    }
}
