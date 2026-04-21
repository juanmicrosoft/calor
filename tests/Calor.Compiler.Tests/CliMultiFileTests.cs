using System.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end CLI tests: invoke calor.dll as a subprocess with multiple --input flags
/// and verify cross-module effect enforcement fires through the real command-line pipeline
/// (System.CommandLine parsing → CompileAsync → CrossModuleEffectEnforcementPass).
/// </summary>
public class CliMultiFileTests : IDisposable
{
    private readonly string _tempDir;

    public CliMultiFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-cli-mf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static string FindCalorDll()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "Calor.Compiler", "bin", "Debug", "net10.0", "calor.dll");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir, "src", "Calor.Compiler", "bin", "Release", "net10.0", "calor.dll");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new InvalidOperationException("calor.dll not found — build the compiler first.");
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var dll = FindCalorDll();
        var argLine = "\"" + dll + "\" --no-telemetry " + string.Join(" ", args);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = argLine,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _tempDir
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start calor CLI process.");

        var stdOut = proc.StandardOutput.ReadToEnd();
        var stdErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);

        return (proc.ExitCode, stdOut, stdErr);
    }

    [Fact]
    public void MultiFile_CrossModuleEffect_Violation_Errors()
    {
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(aPath, """
            §M{m001:OrderService}
            §F{f001:SaveOrder:pub}
              §O{void}
              §E{db:w}
            §/F{f001}
            §/M{m001}
            """);
        File.WriteAllText(bPath, """
            §M{m002:Handler}
            §F{f001:HandleRequest:pub}
              §O{void}
              §C{SaveOrder}
              §/C
            §/F{f001}
            §/M{m002}
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);

        Assert.NotEqual(0, exit);
        var combined = stdOut + stdErr;
        Assert.Contains("Calor0410", combined);
        Assert.Contains("HandleRequest", combined);
        Assert.Contains("db:w", combined);
    }

    [Fact]
    public void MultiFile_CrossModuleEffect_Declared_Succeeds()
    {
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(aPath, """
            §M{m001:OrderService}
            §F{f001:SaveOrder:pub}
              §O{void}
              §E{db:w}
            §/F{f001}
            §/M{m001}
            """);
        File.WriteAllText(bPath, """
            §M{m002:Handler}
            §F{f001:HandleRequest:pub}
              §O{void}
              §E{db:w}
              §C{SaveOrder}
              §/C
            §/F{f001}
            §/M{m002}
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);

        var combined = stdOut + stdErr;
        Assert.True(exit == 0, $"Expected clean compile. Exit={exit}\nStdOut:\n{stdOut}\nStdErr:\n{stdErr}");
        Assert.DoesNotContain("Calor0410", combined);
    }

    [Fact]
    public void MultiFile_OutputFlag_RejectedForMultipleInputs()
    {
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        var outPath = Path.Combine(_tempDir, "out.cs");
        File.WriteAllText(aPath, """
            §M{m1:A}
            §F{f001:Foo:pub}
              §O{void}
            §/F{f001}
            §/M{m1}
            """);
        File.WriteAllText(bPath, """
            §M{m2:B}
            §F{f001:Bar:pub}
              §O{void}
            §/F{f001}
            §/M{m2}
            """);

        var (exit, stdOut, stdErr) = RunCli(
            "--input", aPath, "--input", bPath, "--output", outPath);

        Assert.NotEqual(0, exit);
        Assert.Contains("--output is only supported when compiling a single file", stdOut + stdErr);
    }
}
