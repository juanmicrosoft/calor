using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class Q925VerifyTests
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
        Directory.Delete(dir, true);

        Assert.DoesNotContain(DiagnosticCode.UnknownExternalCall, codes);
        Assert.Equal(expectViolation, codes.Contains(DiagnosticCode.ForbiddenEffect));
    }
}
