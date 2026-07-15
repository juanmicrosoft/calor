using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end CLI tests: invoke calor.dll as a subprocess with multiple --input flags
/// and verify cross-module effect enforcement fires through the real command-line pipeline
/// (System.CommandLine parsing → CompilationDriver → CrossModuleEffectEnforcementPass).
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
        GC.SuppressFinalize(this);
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
        => CliTestHarness.RunCli(_tempDir, args);

    private (string APath, string BPath) WriteCrossModuleViolationPair()
    {
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(aPath, """
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub}
                §O{void}
                §E{db:w}
            """);
        File.WriteAllText(bPath, """
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                §O{void}
                §C{SaveOrder}
                §/C
            """);
        return (aPath, bPath);
    }

    [Fact]
    public void MultiFile_CrossModuleEffect_Violation_Errors()
    {
        var (aPath, bPath) = WriteCrossModuleViolationPair();

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);

        Assert.NotEqual(0, exit);
        var combined = stdOut + stdErr;
        Assert.Contains("Calor0410", combined);
        Assert.Contains("HandleRequest", combined);
        Assert.Contains("db:w", combined);
    }

    [Fact]
    public void MultiFile_CrossModuleEffect_Violation_PermissiveEffects_WarnsAndSucceeds()
    {
        // --permissive-effects must reach the cross-module pass: the violation is
        // demoted to a warning (still visible on stderr) and the compile succeeds.
        var (aPath, bPath) = WriteCrossModuleViolationPair();

        var (exit, stdOut, stdErr) = RunCli(
            "--input", aPath, "--input", bPath, "--permissive-effects");

        Assert.True(exit == 0, $"Expected exit 0 under --permissive-effects. Exit={exit}\nStdOut:\n{stdOut}\nStdErr:\n{stdErr}");
        Assert.Contains("warning Calor0410", stdErr);
        Assert.Contains("HandleRequest", stdErr);
        Assert.DoesNotContain("error Calor0410", stdOut + stdErr);
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
            """);
        File.WriteAllText(bPath, """
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                §O{void}
                §E{db:w}
                §C{SaveOrder}
                §/C
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
            """);
        File.WriteAllText(bPath, """
            §M{m2:B}
              §F{f001:Bar:pub}
                §O{void}
            """);

        var (exit, stdOut, stdErr) = RunCli(
            "--input", aPath, "--input", bPath, "--output", outPath);

        Assert.NotEqual(0, exit);
        Assert.Contains("--output is only supported when compiling a single file", stdOut + stdErr);
    }
}
