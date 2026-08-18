using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Compiler.Tests;

// Regression pin for #925 (cross-module effect resolution).
//
// #925 fixed the case where a call written as `Module.Function` (dotted-path)
// did NOT resolve the callee's declared effects through cross-module lookup,
// so an effectful callee could be invoked from a pure caller without the
// expected Calor0410 firing. The fix registered both the bare and qualified
// forms of every cross-module function name; see
// `src/Calor.Compiler/Effects/EffectEnforcementPass.cs` and search for
// `CrossModuleFunctionNames.Contains(target)` — the check appears in both the
// qualified-name and the bare-name resolution paths, and this theory
// exercises both.
//
// This file was previously named `Q925VerifyTests.cs`; the 2026-08-18
// test-suite audit (finding F2, recommendation R1a) missed the pin because
// "Q925" is opaque and not grep-friendly. Renamed to
// `Issue925CrossModuleEffectRegressionTests` so future audits find it via
// `grep -irn "925"` or `grep -irn regression tests/`.
//
// The theory covers four rows: {bare, qualified} × {pure callee, effectful
// callee}. The bare and qualified rows must agree — that is the invariant
// #925 restored.
public sealed class Issue925CrossModuleEffectRegressionTests
{
    [Theory]
    [InlineData("", "OrderService.SaveOrder", false)]
    [InlineData("db:w", "OrderService.SaveOrder", true)]
    [InlineData("", "SaveOrder", false)]
    [InlineData("db:w", "SaveOrder", true)]
    public void QualifiedAndBareAgree(string calleeEffects, string callTarget, bool expectViolation)
    {
        var dir = Path.Combine(Path.GetTempPath(), "q925v-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "callee.calr"),
                "§M{m001:OrderService}\n  §F{f001:SaveOrder:pub} () -> void\n    §E{" + calleeEffects + "}\n");
            File.WriteAllText(Path.Combine(dir, "caller.calr"),
                "§M{m002:App}\n  §F{f001:Main:pub} () -> void\n    §E{}\n    §C{" + callTarget + "} §/C\n");

            var sources = Directory.GetFiles(dir, "*.calr")
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => new FileInfo(p)).ToList();
            var sink = new DiagnosticBag();
            CompilationDriver.CompileAll(
                sources,
                _ => new CompilationOptions { EnforceEffects = true, UnknownCallPolicy = UnknownCallPolicy.Strict },
                crossModuleEnforcement: true,
                crossModulePolicy: UnknownCallPolicy.Strict,
                onCompiled: (f, r) => File.WriteAllText(Path.ChangeExtension(f.FullName, ".g.cs"), r.GeneratedCode),
                diagnosticSink: sink);

            var codes = sink.Select(d => d.Code).Distinct().OrderBy(c => c).ToArray();

            Assert.DoesNotContain(DiagnosticCode.UnknownExternalCall, codes);
            Assert.Equal(expectViolation, codes.Contains(DiagnosticCode.ForbiddenEffect));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
