using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// Issue #879: postcondition elision is keyed by a mutable cursor
/// (_currentFunctionId, _currentPostconditionIndex) that Visit(MethodNode) never
/// maintained. Consequences pinned here: class-method postconditions never elided even
/// when Proven, and §MT contract violations reported "unknown" as the failing function.
/// The fix enables a NEW elision surface (class methods), so this file also pins that the
/// D14 demotion governs it — an array-carried method proof must stay Assumed, guard kept.
/// </summary>
public class MethodElisionCursorTests
{
    [SkippableFact]
    public void MethodPostcondition_Proven_Elides()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // Same contract shape the §F form has always elided (VerifierTests square
        // pattern); pre-fix the §MT form emitted a runtime guard with id "unknown".
        const string source = @"
§M{m001:Test}
  §CL{c001:Calc:pub}
    §MT{mt001:Square:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §Q (<= x 46340)
      §S (>= result 0)
      §R (* x x)";

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.False(
            result.HasErrors,
            string.Join("; ", result.Diagnostics.Errors.Select(error => error.Message)));
        Assert.Contains("// PROVEN: Postcondition", result.GeneratedCode);
        Assert.DoesNotContain("\"unknown\"", result.GeneratedCode);
    }

    [Fact]
    public void MethodPostconditionGuard_CarriesOwnFunctionId_NotUnknown()
    {
        // No Z3 needed: with no verification result the guard is always kept, and its
        // reported function id comes straight from the cursor — pre-fix, "unknown".
        const string source = @"
§M{m001:Test}
  §CL{c001:Calc:pub}
    §MT{mt001:Half:pub}
      §I{i32:x}
      §O{i32}
      §S (> result 10)
      §R (/ x 2)";

        var result = Program.Compile(source, "test.calr", new CompilationOptions());

        Assert.False(result.HasErrors);
        // Anchored to the exception's function-id argument position, not any occurrence.
        Assert.Contains("\"mt001\", Calor.Runtime.ContractKind", result.GeneratedCode);
        Assert.DoesNotContain("\"unknown\"", result.GeneratedCode);
    }

    [SkippableFact]
    public void OperatorPostcondition_NeverElidesOnForeignMethodProof()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The review-of-the-fix repro: operators emit AFTER methods and are never
        // verified. Pick's §S is Proven but non-lowerable (nested returns), leaving the
        // postcondition index at 0 — without the operator's own cursor reset, the
        // operator's FALSE §S elides on Pick's proof. It must keep its guard.
        const string source = @"
§M{m001:Test}
  §CL{c001:Calc:pub}
    §MT{mt001:Pick:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §S (>= result 0)
      §IF{if1} (>= x 5)
        §R x
      §EL
        §R INT:0
    §OP{op001:+:pub}
      §I{i32:left}
      §I{i32:right}
      §O{i32}
      §S (>= result 100)
      §R INT:0";

        var result = Program.Compile(source, "test.calr", NoCache(unsafeTranspileOnly: true));

        Assert.False(
            result.HasErrors,
            string.Join("; ", result.Diagnostics.Errors.Select(error => error.Message)));
        var generatedLines = result.GeneratedCode.Split('\n');
        Assert.Contains(
            generatedLines,
            line => line.Contains("__calorPostconditionResult", StringComparison.Ordinal)
                && line.Contains(">= 100", StringComparison.Ordinal)
                && line.Contains("throw", StringComparison.Ordinal));
        Assert.DoesNotContain(
            generatedLines,
            line => line.Contains("// PROVEN:", StringComparison.Ordinal)
                && line.Contains(">= 100", StringComparison.Ordinal));
    }

    [Fact]
    public void ConstructorPrecondition_ReportsOwnId_NotForeignMethodId()
    {
        // Cross-class misattribution: pre-fix the cursor carried First.Inc's id into
        // Second's constructor guard (pre-#879 it was "unknown"; the first fix upgraded
        // it to a confidently WRONG id). The constructor's own id is the honest key.
        const string source = @"
§M{m001:Test}
  §CL{c001:First:pub}
    §MT{mt001:Inc:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §S (>= result 1)
      §R (+ x 1)
  §CL{c002:Second:pub}
    §CTOR{ct001:pub}
      §I{i32:size}
      §Q (> size 0)";

        var result = Program.Compile(source, "test.calr", new CompilationOptions());

        Assert.False(result.HasErrors);
        Assert.Contains("\"ct001\", Calor.Runtime.ContractKind", result.GeneratedCode);
        Assert.DoesNotContain("size > 0\", \"mt001\"", result.GeneratedCode);
    }

    [SkippableFact]
    public void MethodPostcondition_ArrayTakingMethod_NeverElides_GuardKept()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The #879 landing requirement: the fix opens elision to class methods, so the
        // demotion/refusal machinery must govern the new surface. This array shape is
        // REFUSED at translation (Calor0718 — `(len x)` models string length, not array
        // length), which keeps the guard: the new surface must not over-elide it.
        const string source = @"
§M{m001:Test}
  §CL{c001:Calc:pub}
    §MT{mt001:Count:pub}
      §I{i32[]:arr}
      §O{i32}
      §S (>= result 0)
      §R (len arr)";

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.False(result.HasErrors);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);
        // The guard must be PRESENT even though verification could not model the body.
        Assert.Contains("ContractViolationException", result.GeneratedCode);
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.ContractVerificationUnsupported);
    }

    [SkippableFact]
    public void MethodPostcondition_StringModelCarried_StaysAssumed_GuardKept()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The D3/D12 demotion on the new surface: the same string-ordinal shape that is
        // Assumed (never Proven-elided) on the §F form (IntegrationTests) must be
        // Assumed on the §MT form too — the cursor fix must not turn a demoted proof
        // into an elision on class methods.
        const string source = @"
§M{m001:Test}
  §CL{c001:Fmt:pub}
    §MT{mt001:Chk:pub}
      §I{str:s}
      §O{str}
      §Q (== s STR:""abc"")
      §S (! (starts result STR:""zzz"" :ordinal))
      §R s";

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.False(result.HasErrors);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);
        // Guard presence pinned for the same reason as the array test above.
        Assert.Contains("ContractViolationException", result.GeneratedCode);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ContractVerificationAssumed);
    }

    // Same Proven source for both default/opt-out pins below: a genuine ∀-proof
    // (x in [0, 46340] ⇒ x*x >= 0), not vacuous.
    private const string ProvenSquareSource = @"
§M{m001:Test}
  §CL{c001:Calc:pub}
    §MT{mt001:Square:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §Q (<= x 46340)
      §S (>= result 0)
      §R (* x x)";

    [SkippableFact]
    public void ProvenPostcondition_Default_ElidesGuard()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The v0.15 default (roadmap §4.5, re-enable condition met: differential at
        // 0 mismatches, coverage 40/65): a clean Proven verdict drops its guard
        // WITHOUT any opt-in. ElideProvenGuards is deliberately not set here.
        var result = Program.Compile(ProvenSquareSource, "test.calr", new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            // Verbose surfaces the Calor0713 Proven diagnostic the vacuity check needs.
            Verbose = true,
            StatusWriter = TextWriter.Null
        });

        Assert.False(result.HasErrors);
        Assert.Contains("// PROVEN: Postcondition", result.GeneratedCode);
        // Only the postcondition guard (ContractKind.Ensures) goes; the two §Q
        // precondition guards stay by design, so the exception type is still present.
        Assert.DoesNotContain("ContractKind.Ensures", result.GeneratedCode);
        Assert.Contains("ContractKind.Requires", result.GeneratedCode);
        // The verdict must actually be Proven — otherwise this test passes vacuously
        // for a source whose verification degraded to Assumed/Timeout. The message must
        // also say the check was ELIDED, matching the emitted code.
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.PostconditionProven && d.Message.Contains("elided"));
    }

    [SkippableFact]
    public void ProvenPostcondition_WithOptOut_KeepsGuard()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // The opt-out (CLI --keep-proven-guards / ElideProvenGuards = false): the
        // verdict is diagnostic and the guard stays.
        var result = Program.Compile(ProvenSquareSource, "test.calr", new CompilationOptions
        {
            VerifyContracts = true,
            ElideProvenGuards = false,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            Verbose = true,
            StatusWriter = TextWriter.Null
        });

        Assert.False(result.HasErrors);
        Assert.DoesNotContain("// PROVEN: Postcondition", result.GeneratedCode);
        Assert.Contains("ContractKind.Ensures", result.GeneratedCode);
        // The message must say the check was KEPT: claiming "elided" when the guard
        // stays would misreport the emitted code.
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.PostconditionProven && d.Message.Contains("kept"));
    }

    private static CompilationOptions NoCache(bool unsafeTranspileOnly = false) => new()
    {
        VerifyContracts = true,
        ElideProvenGuards = true,
        UnsafeTranspileOnly = unsafeTranspileOnly,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };
}
