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

        Assert.False(result.HasErrors);
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
        Assert.Contains("mt001", result.GeneratedCode);
        Assert.DoesNotContain("\"unknown\"", result.GeneratedCode);
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
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ContractVerificationAssumed);
    }

    private static CompilationOptions NoCache() => new()
    {
        VerifyContracts = true,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };
}
