using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// End-to-end integration tests for the static contract verification feature.
/// </summary>
public class IntegrationTests
{
    [SkippableFact]
    public void ProvenContract_EmitsComment_NotRuntimeCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // Bounded Square: with result bound to the body (D-G1.1) and the §Q
        // bound excluding overflow, the postcondition is a genuine ∀-proof.
        // (The unbounded form is genuinely refutable — x*x wraps negative —
        // and lives in the outcome corpus as refuted-overflow.calr.)
        var source = @"
§M{m001:Test}
  §F{f001:Square:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §Q (<= x 46340)
      §S (>= result 0)
      §R (* x x)";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);

        // The proven postcondition elides its runtime check (comment instead)
        Assert.Contains("// PROVEN: Postcondition", result.GeneratedCode);
        Assert.DoesNotContain("ContractKind.Ensures", result.GeneratedCode);
    }

    [SkippableFact]
    public void PreconditionGuards_NeverElidedOnSatisfiability()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // #755 / D-G1.2 elision-soundness pin: the verifier's precondition
        // "Proven" is a satisfiability result (∃), not validity (∀) — a caller
        // can still violate it, so the emitted C# must always carry the guard.
        var source = @"
§M{m001:Test}
  §F{f001:Half:pub}
      §I{i32:x}
      §O{i32}
      §Q (> x 0)
      §R (/ x 2)";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);
        Assert.Contains("ContractKind.Requires", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN: Precondition", result.GeneratedCode);
    }

    [SkippableFact]
    public void VacuousProvenPostcondition_KeepsRuntimeCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // D-G1.3 elision-soundness pin: Proven(vacuous) — unsatisfiable §Q set —
        // must not elide the postcondition check and must warn loudly.
        var source = @"
§M{m001:Test}
  §F{f001:Impossible:pub}
      §I{i32:x}
      §O{i32}
      §Q (> x 10)
      §Q (< x 5)
      §S (== result x)
      §R x";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);
        Assert.Contains("ContractKind.Ensures", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.VacuousPrecondition);
    }

    [SkippableFact]
    public void DisprovenContract_EmitsWarning_AndRuntimeCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
  §F{f001:Bad:pub}
      §I{i32:x}
      §O{i32}
      §S (> result x)
      §R x";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        // Check for warning about violation
        var warnings = result.Diagnostics.Warnings.ToList();
        Assert.Contains(warnings, w => w.Message.Contains("Counterexample"));

        // Runtime check should still be present
        Assert.Contains("ContractViolationException", result.GeneratedCode);
    }

    [Fact]
    public void WithoutVerifyFlag_BehaviorIdentical()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Square:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §S (>= result 0)
      §R (* x x)";

        var withoutVerify = Program.Compile(source, "test.calr", new CompilationOptions
        {
            VerifyContracts = false
        });

        // Both should compile successfully
        Assert.False(withoutVerify.HasErrors);

        // Without verify should have runtime checks
        Assert.Contains("ContractViolationException", withoutVerify.GeneratedCode);
    }

    [Fact]
    public void Z3Unavailable_GracefulFallback()
    {
        // This test runs regardless of Z3 availability
        // If Z3 is unavailable, it should still compile successfully

        var source = @"
§M{m001:Test}
  §F{f001:Square:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §S (>= result 0)
      §R (* x x)";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        // Should succeed regardless of Z3 availability
        Assert.False(result.HasErrors);

        // If Z3 wasn't available, should have info message
        if (!Z3ContextFactory.IsAvailable)
        {
            var infos = result.Diagnostics.Where(d => d.Code == DiagnosticCode.Z3Unavailable).ToList();
            Assert.NotEmpty(infos);
        }
    }

    [Fact]
    public void ExistingTestsShouldStillPass_ContractCompilation()
    {
        // Basic contract compilation without --verify should work as before
        var source = @"
§M{m001:Test}
  §F{f001:Add:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (+ a b)";

        var result = Program.Compile(source, "test.calr");

        Assert.False(result.HasErrors);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
    }

    [Fact]
    public void ContractModeOff_NoChecks()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Square:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §S (>= result 0)
      §R (* x x)";

        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Off,
            VerifyContracts = false
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);
        Assert.DoesNotContain("ContractViolationException", result.GeneratedCode);
    }
}
