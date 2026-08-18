using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Compiler.Tests;

// Dedicated regression pin for issue #995 (audit finding F2 / recommendation R1a),
// which flagged that PR #925 shipped without a discoverable regression test.
//
// PR #925 fixed cross-module effect resolution for MODULE-QUALIFIED call
// targets ("Module.Function"). Before the fix, naming the module explicitly
// caused the qualified call to fall through the resolver as an "unknown
// external call" (Calor0411), which then forbade the call inside a pure
// caller (Calor0410) even when the callee itself was declared pure.
// Writing the more explicit thing was penalised.
//
// The fix (src/Calor.Compiler/Effects/EffectEnforcementPass.cs, ~line 1421)
// consults the driver's cross-module function map on the dotted-path branch
// as well as the bare-name branch. Membership there uses the exactly-one-
// match rule already used elsewhere in resolution.
//
// This class complements Q925VerifyTests by (a) surfacing under a name
// future audits will find with `grep -l 925` or "CrossModule.*Regression",
// and (b) asserting behaviour explicitly per case rather than via a
// parameterised theory, so a failure names the specific regression.
public sealed class Issue925CrossModuleEffectRegressionTests
{
    // Callee is pure, caller is pure, caller uses `B.Function()` (qualified).
    // Pre-#925: qualified target fell through to unknown external, so we'd
    // see BOTH Calor0411 (UnknownExternalCall) and Calor0410 (ForbiddenEffect)
    // even though the callee has no effects at all.
    // Post-#925: the qualified target is resolved as a cross-module callee
    // whose effect set is empty, so no diagnostics fire.
    [Fact]
    public void ModuleQualifiedCallToPureCallee_IsAcceptedInPureCaller_Issue925()
    {
        var codes = CompileTwoModules(
            calleeEffects: "",
            callTarget: "OrderService.SaveOrder");

        Assert.DoesNotContain(DiagnosticCode.UnknownExternalCall, codes);
        Assert.DoesNotContain(DiagnosticCode.ForbiddenEffect, codes);
    }

    // Callee declares an effect, caller is pure, caller uses `B.Function()`.
    // Pre-#925: qualified target fell through to unknown external — Calor0411
    // fired, and the "forbidden" diagnostic that DID appear was a false-alarm
    // consequence of that unknown-call chain rather than a genuine effect
    // propagation.
    // Post-#925: the qualified target resolves to the callee's actual effect
    // set, so exactly Calor0410 (ForbiddenEffect) fires — proving the effect
    // truly propagated across the module boundary via the dotted path.
    [Fact]
    public void ModuleQualifiedCallPropagatesDeclaredEffectAcrossModules_Issue925()
    {
        var codes = CompileTwoModules(
            calleeEffects: "cw",
            callTarget: "OrderService.SaveOrder");

        Assert.DoesNotContain(DiagnosticCode.UnknownExternalCall, codes);
        Assert.Contains(DiagnosticCode.ForbiddenEffect, codes);
    }

    // The bare-name path was always correct — this pin exists so a future
    // regression that "fixes" the bare path incorrectly is caught alongside
    // the qualified one. Bare and qualified MUST agree.
    [Fact]
    public void BareAndQualifiedTargetsAgreeOnCrossModuleEffectPropagation_Issue925()
    {
        var bare = CompileTwoModules(calleeEffects: "cw", callTarget: "SaveOrder");
        var qualified = CompileTwoModules(
            calleeEffects: "cw",
            callTarget: "OrderService.SaveOrder");

        Assert.Equal(
            bare.Contains(DiagnosticCode.ForbiddenEffect),
            qualified.Contains(DiagnosticCode.ForbiddenEffect));
        Assert.Equal(
            bare.Contains(DiagnosticCode.UnknownExternalCall),
            qualified.Contains(DiagnosticCode.UnknownExternalCall));
    }

    // Shared harness: compile two modules where module `App` calls
    // `OrderService.SaveOrder` (or its bare form) and return the set of
    // diagnostic codes emitted. Uses the same CompilationDriver entry
    // point the CLI uses, so the test exercises the real cross-module
    // wiring end-to-end.
    private static IReadOnlyCollection<string> CompileTwoModules(
        string calleeEffects,
        string callTarget)
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "issue925-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "callee.calr"),
                "§M{m001:OrderService}\n"
                + "  §F{f001:SaveOrder:pub} () -> void\n"
                + "    §E{" + calleeEffects + "}\n");
            File.WriteAllText(
                Path.Combine(dir, "caller.calr"),
                "§M{m002:App}\n"
                + "  §F{f001:Main:pub} () -> void\n"
                + "    §E{}\n"
                + "    §C{" + callTarget + "} §/C\n");

            var sources = Directory.GetFiles(dir, "*.calr")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new FileInfo(p))
                .ToList();
            var sink = new DiagnosticBag();
            CompilationDriver.CompileAll(
                sources,
                _ => new CompilationOptions
                {
                    EnforceEffects = true,
                    UnknownCallPolicy = UnknownCallPolicy.Strict,
                },
                crossModuleEnforcement: true,
                crossModulePolicy: UnknownCallPolicy.Strict,
                diagnosticSink: sink);

            return sink.Select(d => d.Code).Distinct().ToArray();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
