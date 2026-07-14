using System.Diagnostics;
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

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "samples", "FizzBuzz", "fizzbuzz.calr")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        throw new InvalidOperationException("Repository root not found from " + Directory.GetCurrentDirectory());
    }

    private static string FindCalorDll()
    {
        var root = FindRepoRoot();
        foreach (var config in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(root, "src", "Calor.Compiler", "bin", config, "net10.0", "calor.dll");
            if (File.Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException("calor.dll not found — build the compiler first.");
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _tempDir
        };
        psi.ArgumentList.Add(FindCalorDll());
        psi.ArgumentList.Add("--no-telemetry");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start calor CLI process.");

        // Read both streams concurrently to avoid pipe-buffer deadlocks.
        var stdOutTask = proc.StandardOutput.ReadToEndAsync();
        var stdErrTask = proc.StandardError.ReadToEndAsync();

        // Generous timeout: these tests restore/build/run real dotnet projects.
        if (!proc.WaitForExit(300_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("calor CLI did not exit within 5 minutes: " + string.Join(" ", args));
        }

        return (proc.ExitCode, stdOutTask.Result, stdErrTask.Result);
    }

    // ------------------------------------------------------------------
    // calor run
    // ------------------------------------------------------------------

    [Fact]
    public void Run_FizzBuzzSample_PrintsFizzBuzzAndExitsZero()
    {
        var sample = Path.Combine(FindRepoRoot(), "samples", "FizzBuzz", "fizzbuzz.calr");

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
    public void Test_SingleFileWithoutTests_BuildsAndExitsZero()
    {
        var file = Path.Combine(_tempDir, "lib.calr");
        File.WriteAllText(file, """
            §M{m001:LibOnly}
              §F{f001:Double:pub} (i32:x) -> i32
                §R (* x INT:2)
            """);

        var (exitCode, stdOut, stdErr) = RunCli("test", file);

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}\nstdout: {stdOut}");
        Assert.Contains("No tests/ directory", stdOut);
    }
}
