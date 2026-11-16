using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

public sealed class InlineRefinementConvergenceTests
{

    [Fact]
    public void LaterParameterRelationship_IsAssumedWithBothParameterNames()
    {
        var module = Parse("""
            module Test Test block
              func Upper (int:lo, int:hi | (# > lo)) → int
                !
                => (result > lo)
                return hi
            """, out var diagnostics);
        var result = Assert.Single(Verify(module, diagnostics).Functions);
        Assert.Equal(ContractVerificationStatus.Proven, Assert.Single(result.PostconditionResults).Status);
        Assert.Equal(ContractVerificationStatus.Proven, Assert.Single(result.PreconditionResults).Status);
    }

    [Fact]
    public void UninhabitedRefinement_ReportsAtPredicateSpan()
    {
        const string source = """
            module Test Test block
              func Impossible (int:x | (# < -2147483648)) → int
                !
                return x
            """;
        var module = Parse(source, out var diagnostics);
        var precondition = Assert.Single(Assert.Single(Verify(module, diagnostics).Functions).PreconditionResults);
        Assert.Equal(ContractVerificationStatus.Disproven, precondition.Status);
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.PreconditionMayBeViolated));
        Assert.Equal(module.Functions[0].Parameters[0].InlineRefinement!.Predicate.Span, diagnostic.Span);
    }

    [Fact]
    public void CapturedSelfSubstitution_DoesNotManufactureVacuity()
    {
        var module = Parse("""
            module Test Test block
              func Captured (int:x | (exists int:x where true : (# == x))) → int
                !
                => (result == x)
                return x
            """, out var diagnostics);
        var result = Assert.Single(Verify(module, diagnostics).Functions);
        Assert.Equal(ContractVerificationStatus.Unsupported, Assert.Single(result.PostconditionResults).Status);
        Assert.False(result.PostconditionResults[0].EffectiveOutcome.IsVacuous);
    }

    private static ModuleVerificationResult Verify(ModuleNode module, DiagnosticBag diagnostics)
        => new ContractVerificationPass(diagnostics, new VerificationOptions
        {
            CacheOptions = new VerificationCacheOptions { Enabled = false }
        }).Verify(module);

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return module;
    }
}
