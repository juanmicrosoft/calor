using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// D-W2.5 regression pins (WS-W2 strictness batch): the CLI's --enforce-effects
/// defaults ON as of v0.11, ending the CLI-false/SDK-true split-brain, with
/// --no-enforce-effects as the explicit opt-out.
/// </summary>
public class CliEnforceEffectsDefaultTests : IDisposable
{
    private readonly string _tempDir;

    public CliEnforceEffectsDefaultTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-cli-ee-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteEffectViolation()
    {
        var path = Path.Combine(_tempDir, "violation.calr");
        File.WriteAllText(path, """
            §M{m001:Test}
              §F{f001:Main:pub}
                §O{void}
                §P "undeclared console write"
            """);
        return path;
    }

    [Fact]
    public void Cli_Default_EnforcesEffects()
    {
        var path = WriteEffectViolation();

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir, "--input", path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Calor0410", stdOut + stdErr);
    }

    [Fact]
    public void Cli_NoEnforceEffects_OptsOut()
    {
        var path = WriteEffectViolation();

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir, "--input", path, "--no-enforce-effects");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Calor0410", stdOut + stdErr);
    }
}
