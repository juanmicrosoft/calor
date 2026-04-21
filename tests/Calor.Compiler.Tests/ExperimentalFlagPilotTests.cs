using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end tests for the Phase 0a experimental-flag plumbing. The pilot flag
/// <c>pilot-hello-world</c> emits an info diagnostic (<see cref="DiagnosticCode.ExperimentalFlagPilot"/>)
/// when enabled; these tests verify the full path from <see cref="CompilationOptions"/>
/// through <see cref="Program.Compile"/>.
/// </summary>
public class ExperimentalFlagPilotTests
{
    private const string MinimalSource = @"§M{m001:Test}
§F{f001:Noop:pub}
  §O{void}
§/F{f001}
§/M{m001}
";

    [Fact]
    public void NoFlags_PilotDiagnosticNotEmitted()
    {
        var result = Program.Compile(MinimalSource, "test.calr", new CompilationOptions());
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ExperimentalFlagPilot);
    }

    [Fact]
    public void UnrelatedFlag_PilotDiagnosticNotEmitted()
    {
        var options = new CompilationOptions
        {
            ExperimentalFlags = new ExperimentalFlags(new[] { "some-other-flag" })
        };
        var result = Program.Compile(MinimalSource, "test.calr", options);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ExperimentalFlagPilot);
    }

    [Fact]
    public void PilotFlagEnabled_DiagnosticEmittedExactlyOnce()
    {
        var options = new CompilationOptions
        {
            ExperimentalFlags = new ExperimentalFlags(new[] { "pilot-hello-world" })
        };
        var result = Program.Compile(MinimalSource, "test.calr", options);

        var pilotDiags = result.Diagnostics
            .Where(d => d.Code == DiagnosticCode.ExperimentalFlagPilot)
            .ToList();
        Assert.Single(pilotDiags);
        Assert.Equal(DiagnosticSeverity.Info, pilotDiags[0].Severity);
    }

    [Fact]
    public void PilotFlagEnabled_DiagnosticDoesNotCauseCompileFailure()
    {
        var options = new CompilationOptions
        {
            ExperimentalFlags = new ExperimentalFlags(new[] { "pilot-hello-world" })
        };
        var result = Program.Compile(MinimalSource, "test.calr", options);

        // Info diagnostic must not flip HasErrors. The pilot is a probe, not a failure.
        Assert.False(result.HasErrors);
        Assert.NotNull(result.GeneratedCode);
        Assert.NotEmpty(result.GeneratedCode);
    }

    [Fact]
    public void PilotFlag_DiagnosticReportsTotalFlagCount()
    {
        var options = new CompilationOptions
        {
            ExperimentalFlags = new ExperimentalFlags(new[] { "pilot-hello-world", "other-1", "other-2" })
        };
        var result = Program.Compile(MinimalSource, "test.calr", options);

        var pilot = Assert.Single(result.Diagnostics.Where(d => d.Code == DiagnosticCode.ExperimentalFlagPilot));
        Assert.Contains("3 flag(s)", pilot.Message);
    }

    [Fact]
    public void FlagName_CaseInsensitive_AtPilotCheck()
    {
        var options = new CompilationOptions
        {
            ExperimentalFlags = new ExperimentalFlags(new[] { "PILOT-HELLO-WORLD" })
        };
        var result = Program.Compile(MinimalSource, "test.calr", options);
        Assert.Single(result.Diagnostics.Where(d => d.Code == DiagnosticCode.ExperimentalFlagPilot));
    }

    [Fact]
    public void OptionsWithoutExperimentalFlags_DefaultsToNone_NoDiagnostic()
    {
        // Default-constructed CompilationOptions must have ExperimentalFlags = None,
        // not null — verifies the init default works.
        var options = new CompilationOptions();
        Assert.NotNull(options.ExperimentalFlags);
        Assert.Equal(0, options.ExperimentalFlags.Count);

        var result = Program.Compile(MinimalSource, "test.calr", options);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ExperimentalFlagPilot);
    }
}
