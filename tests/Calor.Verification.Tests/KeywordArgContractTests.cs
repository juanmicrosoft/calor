using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// Issue #874: a call expression carrying a keyword argument inside a contract must degrade
/// to a diagnostic, never crash the compiler. The capitalized C# spellings (StartsWith vs the
/// documented lowercase forms) parse as CallExpressionNode, so this is reachable from the
/// plausible mistake of porting a contract from C#.
/// </summary>
public class KeywordArgContractTests
{
    [SkippableFact]
    public void KeywordArgInPrecondition_ProducesDiagnostic_NotCrash()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        const string source = @"
§M{m001:Repro}
  §F{f001:Check:pub} (str:s) -> bool
    §Q (StartsWith s STR:""hello"" :ignore-case)
    §R true";

        // The defect was an unhandled NullReferenceException with no location — the one
        // outcome that carries no information. Compile must complete and speak.
        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.CliInternalError);
        // The diagnostic must name the keyword argument so the user can act on it.
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("ignore-case"));
    }

    [SkippableFact]
    public void KeywordArgInPostcondition_ProducesDiagnostic_NotCrash()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        const string source = @"
§M{m001:Repro}
  §F{f001:Check:pub} (str:s) -> bool
    §S (Contains s STR:""x"" :ignore-case)
    §R true";

        var result = Program.Compile(source, "test.calr", NoCache());

        Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.CliInternalError);
    }

    private static CompilationOptions NoCache() => new()
    {
        VerifyContracts = true,
        VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
    };
}
