using Xunit;
using System.CommandLine;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;

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

    [Fact]
    public void SdkDefaults_MatchCliStrictEnforcementDefaults()
    {
        var options = new CompilationOptions();

        Assert.True(options.EnforceEffects);
        Assert.Equal(UnknownCallPolicy.Strict, options.UnknownCallPolicy);
        Assert.False(options.StrictEffects);

        var result = Program.Compile(
            """
            §M{m001:Test}
              §F{f001:Main:pub}
                §O{void}
                §P "undeclared console write"
            """,
            "sdk-default.calr",
            options);
        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void WatchDefaults_MatchCliEnforcementDefault()
    {
        var command = WatchCommand.Create();
        var option = Assert.IsType<System.CommandLine.Option<bool>>(Assert.Single(command.Options,
            candidate => candidate.HasAlias("--enforce-effects")));
        var parseResult = command.Parse(["."]);

        Assert.True(parseResult.GetValueForOption(option));
    }
}
