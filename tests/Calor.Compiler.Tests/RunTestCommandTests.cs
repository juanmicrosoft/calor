using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end CLI tests for `calor run` and `calor test`: invoke calor.dll as a
/// subprocess so System.CommandLine parsing, workspace materialization, the
/// dotnet build/run/test pipeline, and exit-code propagation are all exercised
/// through the real command-line surface.
/// </summary>
public class RunTestCommandTests : IDisposable
{
    private readonly string _tempDir;

    public RunTestCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-runtest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
        => CliTestHarness.RunCli(_tempDir, args);

    // ------------------------------------------------------------------
    // calor run
    // ------------------------------------------------------------------

    [Fact]
    public void Run_FizzBuzzSample_PrintsFizzBuzzAndExitsZero()
    {
        var sample = Path.Combine(CliTestHarness.FindRepoRoot(), "samples", "FizzBuzz", "fizzbuzz.calr");

        var (exitCode, stdOut, stdErr) = RunCli("run", sample);

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.Contains("FizzBuzz", stdOut);
        Assert.Contains("Fizz", stdOut);
        Assert.Contains("Buzz", stdOut);
        Assert.Contains("14", stdOut); // plain numbers printed too
    }

    [Fact]
    public void Run_ProgramWithIntMain_PropagatesExitCode()
    {
        var file = Path.Combine(_tempDir, "exitcoder.calr");
        File.WriteAllText(file, """
            §M{m001:ExitCoder}
              §F{f001:Main:pub} () -> i32
                §R INT:3
            """);

        var (exitCode, stdOut, stdErr) = RunCli("run", file);

        Assert.True(exitCode == 3, $"expected exit 3, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
    }

    [Fact]
    public void Run_CompileError_ExitsOneWithDiagnostics()
    {
        var file = Path.Combine(_tempDir, "broken.calr");
        File.WriteAllText(file, """
            §M{m001:Broken}
              §F{f001:Main:pub} () -> void
                §P undefined_variable_xyz
            """);

        var (exitCode, _, stdErr) = RunCli("run", file);

        Assert.Equal(1, exitCode);
        Assert.Contains("Calor", stdErr); // a Calor diagnostic code was printed
    }

    [Fact]
    public void Run_MissingFile_ExitsTwo()
    {
        var (exitCode, _, stdErr) = RunCli("run", Path.Combine(_tempDir, "does-not-exist.calr"));

        Assert.Equal(2, exitCode);
        Assert.Contains("not found", stdErr);
    }

    [Fact]
    public void Run_KeepTemp_PrintsWorkspacePath()
    {
        var file = Path.Combine(_tempDir, "hello.calr");
        File.WriteAllText(file, """
            §M{m001:Hello}
              §F{f001:Main:pub} () -> void
                §E{cw}
                §P "hello from calor run"
            """);

        var (exitCode, stdOut, stdErr) = RunCli("run", file, "--keep-temp");

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.Contains("hello from calor run", stdOut);
        Assert.Contains("Workspace kept at:", stdOut);

        var line = stdOut.Split('\n').First(l => l.Contains("Workspace kept at:"));
        var workspace = line.Split("Workspace kept at:")[1].Trim();
        Assert.True(Directory.Exists(workspace), $"kept workspace should exist: {workspace}");
        try { Directory.Delete(workspace, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    // effect enforcement on calor run
    // ------------------------------------------------------------------

    private string WritePureViolator()
    {
        // Declares §E{} (pure) but prints — a forbidden 'cw' effect.
        var file = Path.Combine(_tempDir, "pureviolator.calr");
        File.WriteAllText(file, """
            §M{m001:PureViolator}
              §F{f001:Main:pub} () -> void
                §E{}
                §P "side effect!"
            """);
        return file;
    }

    [Fact]
    public void Run_EffectViolation_Strict_ExitsOneWithError()
    {
        var (exitCode, _, stdErr) = RunCli("run", WritePureViolator());

        Assert.Equal(1, exitCode);
        Assert.Contains("error Calor04", stdErr);
    }

    [Fact]
    public void Run_EffectViolation_Permissive_RunsAndPrintsDemotedWarning()
    {
        var (exitCode, stdOut, stdErr) = RunCli("run", WritePureViolator(), "--permissive");

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.Contains("side effect!", stdOut);
        // The demoted warning must be visible even though compilation succeeded.
        Assert.Contains("warning Calor04", stdErr);
    }

    [Fact]
    public void Run_EffectViolation_EnforceEffectsFalse_RunsWithoutDiagnostics()
    {
        var (exitCode, stdOut, stdErr) = RunCli("run", WritePureViolator(), "--enforce-effects", "false");

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.DoesNotContain("Calor04", stdErr);
    }

    [Fact]
    public void TopLevelCompile_PermissiveEffects_WarningVisibleOnSuccess()
    {
        // Same visibility guarantee on the top-level compile command: demoted
        // warnings print even though the compilation succeeds.
        var file = WritePureViolator();

        var (exitCode, stdOut, stdErr) = RunCli(
            "--input", file, "--enforce-effects", "--permissive-effects");

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.Contains("warning Calor04", stdErr);
        Assert.Contains("Compilation successful", stdOut);
    }

    // ------------------------------------------------------------------
    // calor test
    // ------------------------------------------------------------------

    private void WriteMathFixture(string dir, bool withBug)
    {
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "tests"));

        File.WriteAllText(Path.Combine(dir, "mathutil.calr"), $$"""
            §M{m001:MathUtil}
              §F{f001:Add:pub} (i32:a, i32:b) -> i32
                §R (+ a {{(withBug ? "(+ b INT:1)" : "b")}})
            """);

        File.WriteAllText(Path.Combine(dir, "tests", "MathUtilTests.cs"), """
            using Xunit;

            namespace MathUtil.Tests;

            public class MathUtilTests
            {
                [Fact]
                public void Add_ReturnsSum() => Assert.Equal(5, global::MathUtil.MathUtilModule.Add(2, 3));

                [Fact]
                public void Add_Zero() => Assert.Equal(7, global::MathUtil.MathUtilModule.Add(7, 0));
            }
            """);
    }

    [Fact]
    public void Test_PairLayoutFixture_PassesAndExitsZero()
    {
        var fixtureDir = Path.Combine(_tempDir, "math-pass");
        WriteMathFixture(fixtureDir, withBug: false);

        var (exitCode, stdOut, stdErr) = RunCli("test", fixtureDir);

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
    }

    [Fact]
    public void Test_FailingTests_ExitNonZero()
    {
        var fixtureDir = Path.Combine(_tempDir, "math-fail");
        WriteMathFixture(fixtureDir, withBug: true);

        var (exitCode, _, _) = RunCli("test", fixtureDir);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void Test_SingleFileWithoutTests_BuildsAndExitsThree()
    {
        var file = Path.Combine(_tempDir, "lib.calr");
        File.WriteAllText(file, """
            §M{m001:LibOnly}
              §F{f001:Double:pub} (i32:x) -> i32
                §R (* x INT:2)
            """);

        var (exitCode, stdOut, stdErr) = RunCli("test", file);

        Assert.True(exitCode == 3, $"expected exit 3 (no tests found), got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.Contains("No tests found", stdErr);
    }

    [Fact]
    public void Test_BenchmarkPairDirectory_N1003CsvRow_PassesAndExitsZero()
    {
        // The real benchmark-pair layout: calor/ sources, a same-module copy under
        // reference/calor/, and tests/ with a Calor-arm shim. reference/ must be
        // excluded (with a warning) or the materialized project fails with CS0101.
        // The checked-in calor/ fixture is the unsolved scaffold by design, so the
        // pair is copied and solved (reference implementation applied) to get a
        // green run — reference/ stays in place to exercise the exclusion.
        var pairDir = Path.Combine(CliTestHarness.FindRepoRoot(),
            "bench", "phase0-agent-native", "pairs", "N1-003-csv-row");
        Assert.True(Directory.Exists(pairDir), $"benchmark pair not found: {pairDir}");

        var pairCopy = Path.Combine(_tempDir, "N1-003-csv-row");
        CopyDirectory(pairDir, pairCopy);
        File.Copy(
            Path.Combine(pairCopy, "reference", "calor", "CsvRow.calr"),
            Path.Combine(pairCopy, "calor", "CsvRow.calr"),
            overwrite: true);

        var (exitCode, stdOut, stdErr) = RunCli("test", pairCopy);

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.Contains("skipping", stdErr);
        Assert.Contains("reference", stdErr);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }
}
