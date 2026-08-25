using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// CLI-surface pins for proof-based guard elision being default-on (roadmap
/// v0.13–v0.15 §4.5, row "3.5.6 elision re-enable"): through the real
/// System.CommandLine pipeline, <c>--verify</c> alone elides a clean Proven
/// postcondition, <c>--keep-proven-guards</c> opts out, and the v0.13/v0.14
/// <c>--elide-proven-guards</c> spelling is still accepted as a no-op.
/// </summary>
public class CliElisionDefaultTests : IDisposable
{
    private readonly string _tempDir;

    public CliElisionDefaultTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-cli-elide-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // A genuine ∀-proof (x in [0, 46340] ⇒ x*x >= 0); not vacuous, so it is eligible.
    private string WriteProvenSource()
    {
        var path = Path.Combine(_tempDir, "square.calr");
        File.WriteAllText(path, """
            §M{m001:Test}
              §CL{c001:Calc:pub}
                §MT{mt001:Square:pub}
                  §I{i32:x}
                  §O{i32}
                  §Q (>= x 0)
                  §Q (<= x 46340)
                  §S (>= result 0)
                  §R (* x x)
            """);
        return path;
    }

    private (int ExitCode, string Generated, string StdErr) CompileVerified(params string[] extraArgs)
    {
        var input = WriteProvenSource();
        var output = Path.Combine(_tempDir, "square.cs");
        var args = new List<string> { "--input", input, "--output", output, "--verify", "--no-cache" };
        args.AddRange(extraArgs);
        var (exit, _, stdErr) = CliTestHarness.RunCli(_tempDir, args.ToArray());
        var generated = File.Exists(output) ? File.ReadAllText(output) : string.Empty;
        return (exit, generated, stdErr);
    }

    [SkippableFact]
    public void Verify_Default_ElidesProvenPostcondition()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var (exit, generated, stdErr) = CompileVerified();

        Assert.Equal(0, exit);
        Assert.Contains("// PROVEN: Postcondition", generated);
        // Only the postcondition guard (ContractKind.Ensures) goes; §Q guards stay by design.
        Assert.DoesNotContain("ContractKind.Ensures", generated);
        Assert.Contains("ContractKind.Requires", generated);
        // No flag was written, so no "has no effect" warning may fire either.
        Assert.DoesNotContain("has no effect without --verify", stdErr);
    }

    [SkippableFact]
    public void Verify_KeepProvenGuards_KeepsGuard()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var (exit, generated, _) = CompileVerified("--keep-proven-guards");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("// PROVEN: Postcondition", generated);
        Assert.Contains("ContractKind.Ensures", generated);
    }

    [SkippableFact]
    public void Verify_LegacyElideFlag_StillAcceptedAndElides()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var (exit, generated, _) = CompileVerified("--elide-proven-guards");

        Assert.Equal(0, exit);
        Assert.Contains("// PROVEN: Postcondition", generated);
        Assert.DoesNotContain("ContractKind.Ensures", generated);
    }

    [Fact]
    public void KeepProvenGuards_WithoutVerify_WarnsButCompiles()
    {
        var input = WriteProvenSource();
        var output = Path.Combine(_tempDir, "square.cs");

        var (exit, _, stdErr) = CliTestHarness.RunCli(
            _tempDir, "--input", input, "--output", output, "--keep-proven-guards");

        Assert.Equal(0, exit);
        Assert.Contains("--keep-proven-guards has no effect without --verify", stdErr);
    }

    [Fact]
    public void NoVerify_KeepsPostconditionGuard()
    {
        // Without --verify there is no verdict to act on: the default-on elision must
        // leave the postcondition guard exactly where 0.14 left it.
        var input = WriteProvenSource();
        var output = Path.Combine(_tempDir, "square.cs");

        var (exit, _, _) = CliTestHarness.RunCli(_tempDir, "--input", input, "--output", output);

        Assert.Equal(0, exit);
        var generated = File.ReadAllText(output);
        Assert.DoesNotContain("// PROVEN: Postcondition", generated);
        Assert.Contains("ContractKind.Ensures", generated);
    }

    [Fact]
    public void ElideProvenGuards_WithoutVerify_WarnsButCompiles()
    {
        var input = WriteProvenSource();
        var output = Path.Combine(_tempDir, "square.cs");

        var (exit, _, stdErr) = CliTestHarness.RunCli(
            _tempDir, "--input", input, "--output", output, "--elide-proven-guards");

        Assert.Equal(0, exit);
        Assert.Contains("--elide-proven-guards has no effect without --verify", stdErr);
    }

    [Fact]
    public void NoFlag_WithoutVerify_DoesNotWarn()
    {
        var input = WriteProvenSource();
        var output = Path.Combine(_tempDir, "square.cs");

        var (exit, _, stdErr) = CliTestHarness.RunCli(_tempDir, "--input", input, "--output", output);

        Assert.Equal(0, exit);
        // Elision is the default now; a bare compile must not nag about a flag nobody wrote.
        Assert.DoesNotContain("has no effect without --verify", stdErr);
    }
}
