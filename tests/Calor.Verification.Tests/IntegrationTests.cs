using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
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

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.False(result.HasErrors);

        // The check must be EMITTED, not elided — this is the whole point.
        Assert.Contains("ContractKind.Ensures", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);

        // Control, so the assertion above is known to discriminate rather than pass vacuously.
        // NOTE the control changed shape when D3/D12 landed: no string proof elides any more, so
        // "the ordinal form IS elided" is no longer available as the contrast. The contrast that
        // remains is the one that matters — REFUSED (outside the modeled surface, Calor0718) vs
        // DEMOTED (modeled, discharged, then made conditional on the string model, Calor0720).
        // Both keep the runtime check; only the first means the solver never engaged.
        var ordinal = Program.Compile(
            source.Replace(@" :ignore-case", string.Empty), "test.calr", NoCache());

        Assert.False(ordinal.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ContractVerificationUnsupported);
        Assert.DoesNotContain(ordinal.Diagnostics, d => d.Code == DiagnosticCode.ContractVerificationUnsupported);
        Assert.Contains(ordinal.Diagnostics, d => d.Code == DiagnosticCode.ContractVerificationAssumed);
    }

    /// <summary>
    /// Divergence D4's SECOND half, found by adversarial review of the first fix. `.NET` resolves
    /// <c>String.StartsWith(String)</c>, <c>EndsWith(String)</c> and <c>IndexOf(String)</c> to the
    /// <b>CurrentCulture</b> overload, while the solver models them ordinally — so omitting the
    /// mode carried the identical false-<c>Proven</c>-elides vector on the far more common
    /// spelling. Reproduced end-to-end before the fix with exactly this program: it threw
    /// <c>ContractViolationException</c> under <c>calor run</c> and printed its value under
    /// <c>calor run --verify</c>.
    ///
    /// <para>A zero-width joiner is the witness: it has no collation weight, so
    /// <c>"abc".StartsWith("\u200dabc")</c> is <b>true</b> culturally and <b>false</b> ordinally.</para>
    /// </summary>
    [SkippableFact]
    public void BareCultureSensitiveStringOp_NeverElidesTheRuntimeCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        const string source = @"
§M{m001:Test}
  §F{f001:Chk:pub} (str:s) -> str
    §E{}
    §Q (== s STR:""abc"")
    §S (! (starts result STR:""\u200dabc""))
    §R s";

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.False(result.HasErrors);
        Assert.Contains("ContractKind.Ensures", result.GeneratedCode);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);

        // Control: with ':ordinal' stated the model matches the emitted overload, so the solver
        // genuinely engages — the form is DEMOTED (Calor0720, conditional on the string model per
        // D3/D12) rather than REFUSED (Calor0718, outside the modeled surface). Both keep the
        // check, so the emitted-overload assertion is what pins that the fix is precise: a blanket
        // refusal of these three operations would fail here.
        var ordinal = Program.Compile(
            source.Replace(@"""))", @""" :ordinal))"), "test.calr", NoCache());

        Assert.False(ordinal.HasErrors);
        Assert.Contains("StringComparison.Ordinal", ordinal.GeneratedCode);
        Assert.DoesNotContain(ordinal.Diagnostics, d => d.Code == DiagnosticCode.ContractVerificationUnsupported);
        Assert.Contains(ordinal.Diagnostics, d => d.Code == DiagnosticCode.ContractVerificationAssumed);
    }

    /// <summary>Verification must be exercised, not replayed from a warm cache.</summary>
    private static CompilationOptions NoCache() => new()
    {
        VerifyContracts = true,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };

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
