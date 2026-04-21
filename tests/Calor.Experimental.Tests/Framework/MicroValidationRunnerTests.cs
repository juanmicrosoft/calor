using Xunit;

namespace Calor.Experimental.Tests.Framework;

/// <summary>
/// Smoke tests for <see cref="MicroValidationRunner"/> using the pilot-hello-world
/// experimental flag (shipped in Phase 0a). The pilot flag is a deliberately trivial
/// probe — it emits Calor1200 whenever the flag is enabled, regardless of program content.
///
/// This test class demonstrates the convention future TIERxY authors will follow:
/// write a manifest pointing at your flag and diagnostic code, then invoke the runner
/// per program.
/// </summary>
public class MicroValidationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public MicroValidationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "micro-runner-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static readonly MicroValidationManifest PilotManifest = new()
    {
        HypothesisId = "TIER0-PILOT",
        ExperimentalFlag = "pilot-hello-world",
        ExpectedDiagnosticCode = "Calor1200"
    };

    private string WriteProgram(string content)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.calr");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Run_PilotFlag_EmitsExpectedDiagnosticWhenFlagOn()
    {
        var program = WriteProgram(@"§M{m1:Test}
§F{f001:Noop:pub}
  §O{void}
§/F{f001}
§/M{m1}
");

        var outcome = MicroValidationRunner.Run(PilotManifest, program);

        Assert.True(outcome.DiagnosticEmittedWithFlag, "Pilot flag should emit Calor1200 when enabled.");
        Assert.False(outcome.DiagnosticEmittedWithoutFlag, "Pilot flag should be silent when disabled.");
        Assert.False(outcome.CompileErrorsWithFlag);
        Assert.False(outcome.CompileErrorsWithoutFlag);
    }

    [Fact]
    public void Run_InvalidProgramPath_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            MicroValidationRunner.Run(PilotManifest, Path.Combine(_tempDir, "does-not-exist.calr")));
    }

    [Fact]
    public void Run_NullManifest_Throws()
    {
        var program = WriteProgram("§M{m1:Test} §/M{m1}");
        Assert.Throws<ArgumentNullException>(() =>
            MicroValidationRunner.Run(null!, program));
    }
}
