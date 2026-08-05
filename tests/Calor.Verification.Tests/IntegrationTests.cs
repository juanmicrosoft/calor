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

    /// <summary>
    /// Divergence D4, end-to-end. This is the shape that shipped before the fix: the postcondition
    /// is <b>false at runtime</b> (<c>"abc".Equals("ABC", OrdinalIgnoreCase)</c> is true, so the
    /// negation fails), yet the verifier translated <c>:ignore-case</c> as ORDINAL, returned
    /// <c>Proven</c>, and the emitter deleted the check — so <c>calor run</c> threw
    /// <c>ContractViolationException</c> while <c>calor run --verify</c> printed <c>abc</c>.
    ///
    /// <para>The unit-level pins live in <c>TranslatorTests</c>/<c>VerifierTests</c>; this one is
    /// here because only the full pipeline demonstrates the consequence that makes D4 a soundness
    /// defect rather than a precision one — <b>a check that would have failed is gone</b>.</para>
    /// </summary>
    [SkippableFact]
    public void NonOrdinalComparisonMode_NeverElidesTheRuntimeCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
  §F{f001:Echo:pub} (str:s) -> str
    §E{}
    §Q (== s STR:""abc"")
    §S (! (Equals result STR:""ABC"" :ignore-case))
    §R s";

        var result = Program.Compile(source, "test.calr", new CompilationOptions { VerifyContracts = true });

        Assert.False(result.HasErrors);

        // The check must be EMITTED, not elided — this is the whole point.
        Assert.Contains("ContractKind.Ensures", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);

        // Control, so the assertion above is known to discriminate rather than pass vacuously:
        // the SAME contract stated ordinally is genuinely provable and IS elided. Pre-fix both
        // programs produced this second output — which is exactly the defect.
        var ordinal = Program.Compile(
            source.Replace(@" :ignore-case", string.Empty), "test.calr",
            new CompilationOptions { VerifyContracts = true });

        Assert.False(ordinal.HasErrors);
        Assert.Contains("// PROVEN: Postcondition", ordinal.GeneratedCode);
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

    // ------------------------------------------------------------------
    // #774: the width-carrying typed literals (LONG:/UINT:/ULONG:/SINGLE:)
    // must flow through the verification pipeline (ExpressionSimplifier + Z3)
    // without breaking contract translation — the width marker is metadata on
    // top of the same numeric value the translator already consumes.
    // ------------------------------------------------------------------

    [SkippableFact]
    public void WidthTypedIntLiterals_InContracts_VerifyWithoutError()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // LONG:/UINT: literals appear in the pre/postconditions and the body.
        var source = @"
§M{m001:Test}
  §F{f001:Clamp:pub}
      §I{i64:x}
      §O{i64}
      §Q (>= x LONG:0)
      §Q (<= x LONG:1000000000000)
      §S (>= result LONG:0)
      §R x";

        var options = new CompilationOptions { VerifyContracts = true };
        var result = Program.Compile(source, "test.calr", options);

        // Verification runs to completion — the width markers neither crash the
        // translator nor introduce a compile error.
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void WidthTypedLiterals_CompileToCorrectlySuffixedCSharp()
    {
        // End-to-end Calor-source → C#: the typed literals emit the right suffix.
        var source = @"
§M{m001:Test}
  §F{f001:Widths:pub}
      §O{i64}
      §B{a:i64} LONG:5
      §B{b:u32} UINT:7
      §B{c:f32} SINGLE:3.14
      §R a";

        var result = Program.Compile(source, "test.calr");

        Assert.False(result.HasErrors);
        Assert.Contains("5L", result.GeneratedCode);
        Assert.Contains("7U", result.GeneratedCode);
        Assert.Contains("3.14f", result.GeneratedCode);
    }
}
