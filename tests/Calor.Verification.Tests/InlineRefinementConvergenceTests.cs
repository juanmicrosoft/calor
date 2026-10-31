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
    [Theory]
    [InlineData("i32", "(>= x 0)", "(>= # 0)", "(>= result 0)")]
    [InlineData("i32", "(&& (> x 10) (< x 5))", "(&& (> # 10) (< # 5))", "(== result 1)")]
    [InlineData("i32", "(> (** x 2) 0)", "(> (** # 2) 0)", "(>= result 0)")]
    [InlineData("bool", "x", "#", "(== result true)")]
    [InlineData("str", "(contains x \"a\")", "(contains # \"a\")", "(contains result \"a\")")]
    [InlineData("str", "§C{string.IsNullOrEmpty} §A x §/C", "§C{string.IsNullOrEmpty} §A # §/C", "(== result x)")]
    [InlineData("i32", "(forall ((i i32)) (-> (&& (>= i 0) (< i 10)) (>= x i)))",
        "(forall ((i i32)) (-> (&& (>= i 0) (< i 10)) (>= # i)))", "(>= result 0)")]
    public void LegacyAndInlineContracts_HaveIdenticalProofOutcomes(
        string type, string legacy, string inline, string postcondition)
    {
        ModuleVerificationResult Run(bool refined)
        {
            var source = $$"""
                §M{Test}
                  §F{Identity:pub} ({{type}}:x{{(refined ? " | " + inline : "")}}) -> {{type}}
                    {{(refined ? "" : "§Q " + legacy)}}
                    §S {{postcondition}}
                    §R x
                """;
            var module = Parse(source, out var diagnostics);
            new ContractVerifier(diagnostics).Verify(module);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
            return Verify(module, diagnostics);
        }

        var oldResult = Assert.Single(Run(false).Functions);
        var newResult = Assert.Single(Run(true).Functions);
        Assert.Equal(oldResult.PreconditionResults[0].EffectiveOutcome.Status,
            newResult.PreconditionResults[0].EffectiveOutcome.Status);
        Assert.Equal(oldResult.PostconditionResults[0].EffectiveOutcome.Status,
            newResult.PostconditionResults[0].EffectiveOutcome.Status);
        Assert.Equal(oldResult.PostconditionResults[0].EffectiveOutcome.IsVacuous,
            newResult.PostconditionResults[0].EffectiveOutcome.IsVacuous);
    }

    [Theory]
    [InlineData("(>= # 0)", "(>= result 0)", ContractVerificationStatus.Proven)]
    [InlineData("(>= # 0)", "(< result 0)", ContractVerificationStatus.Disproven)]
    [InlineData("(&& (> # 10) (< # 5))", "(== result 1)", ContractVerificationStatus.Proven)]
    [InlineData("(> (** # 2) 0)", "(>= result 0)", ContractVerificationStatus.Unsupported)]
    public void SignatureRefinements_UseTheContractOutcomePath(
        string predicate, string postcondition, ContractVerificationStatus expected)
    {
        var source = $$"""
            §M{Test}
              §F{Identity:pub} (i32:x | {{predicate}}) -> i32
                §S {{postcondition}}
                §R x
            """;
        var module = Parse(source, out var diagnostics);
        var function = Assert.Single(module.Functions);
        Assert.NotNull(Assert.Single(function.Parameters).InlineRefinement);
        var result = Assert.Single(Verify(module, diagnostics).Functions);
        Assert.Single(result.PreconditionResults);
        var outcome = Assert.Single(result.PostconditionResults);
        Assert.Equal(expected, outcome.Status);
        Assert.Equal(predicate.Contains("> # 10"), outcome.EffectiveOutcome.IsVacuous);
        if (outcome.EffectiveOutcome.IsVacuous)
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.VacuousPrecondition);
    }

    [Fact]
    public void LaterParameterRelationship_IsAssumedWithBothParameterNames()
    {
        var module = Parse("""
            §M{Test}
              §F{Upper:pub} (i32:lo, i32:hi | (> # lo)) -> i32
                §S (> result lo)
                §R hi
            """, out var diagnostics);
        var result = Assert.Single(Verify(module, diagnostics).Functions);
        Assert.Equal(ContractVerificationStatus.Proven, Assert.Single(result.PostconditionResults).Status);
        Assert.Equal(ContractVerificationStatus.Proven, Assert.Single(result.PreconditionResults).Status);
    }

    [Theory]
    [InlineData("(> # (+ a 10))", ContractInheritanceStatus.Violation)]
    [InlineData("(>= # a)", ContractInheritanceStatus.Valid)]
    [InlineData("(> # a)", ContractInheritanceStatus.Valid)]
    public void OverrideRefinements_EnforceWeakeningAfterParameterRenaming(
        string predicate, ContractInheritanceStatus expected)
    {
        var module = Parse($$"""
            §M{Test}
              §IFACE{IRange}
                §MT{Upper} (i32:lo, i32:hi | (> # lo)) -> i32
              §CL{Range:pub}
                §IMPL{IRange}
                §MT{Upper:pub} (i32:a, i32:b | {{predicate}}) -> i32
                  §E{}
                  §R b
            """, out var diagnostics);
        using var checker = new ContractInheritanceChecker(diagnostics);
        var result = checker.Check(module);
        Assert.Equal(expected, Assert.Single(Assert.Single(result.Classes).Methods).Status);
        if (expected == ContractInheritanceStatus.Violation)
        {
            var diagnostic = Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.StrongerPrecondition));
            Assert.Equal(module.Classes[0].Methods[0].Parameters[1].InlineRefinement!.Predicate.Span, diagnostic.Span);
        }
    }

    [Fact]
    public void UnsupportedOverrideImplication_DoesNotClaimValidInheritance()
    {
        var module = Parse("""
            §M{Test}
              §IFACE{IValue}
                §MT{Get} (str:s | (contains # "a")) -> str
              §CL{Value:pub}
                §IMPL{IValue}
                §MT{Get:pub} (str:value | (contains # "")) -> str
                  §R value
            """, out var diagnostics);
        using var checker = new ContractInheritanceChecker(diagnostics);
        var result = checker.Check(module);
        Assert.Equal(ContractInheritanceStatus.Unestablished,
            Assert.Single(Assert.Single(result.Classes).Methods).Status);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ImplicationAssumed
            || diagnostic.Code == DiagnosticCode.ImplicationUnknown);
    }

    [Fact]
    public void OverrideWithSwappedParameterNames_UsesSimultaneousRebinding()
    {
        var module = Parse("""
            §M{Test}
              §IFACE{IRange}
                §MT{Upper} (i32:lo, i32:hi | (> # lo)) -> i32
              §CL{Range:pub}
                §IMPL{IRange}
                §MT{Upper:pub} (i32:hi, i32:lo | (> # hi)) -> i32
                  §R lo
            """, out var diagnostics);
        using var checker = new ContractInheritanceChecker(diagnostics);
        Assert.Equal(ContractInheritanceStatus.Valid,
            Assert.Single(Assert.Single(checker.Check(module).Classes).Methods).Status);
    }

    [Fact]
    public void UninhabitedRefinement_ReportsAtPredicateSpan()
    {
        const string source = """
            §M{Test}
              §F{Impossible:pub} (i32:x | (< # -2147483648)) -> i32
                §R x
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
            §M{Test}
              §F{Captured:pub} (i32:x | (exists ((x i32)) (== # x))) -> i32
                §S (== result x)
                §R x
            """, out var diagnostics);
        var result = Assert.Single(Verify(module, diagnostics).Functions);
        Assert.Equal(ContractVerificationStatus.Unsupported, Assert.Single(result.PostconditionResults).Status);
        Assert.False(result.PostconditionResults[0].EffectiveOutcome.IsVacuous);
    }

    [Fact]
    public void OutRefinement_IsNotAssumedOnEntry()
    {
        var module = Parse("""
            §M{Test}
              §F{Out:pub} (i32:x:out | (> # 0)) -> i32
                §S (> result 0)
                §ASSIGN x -1
                §R x
            """, out var diagnostics);
        var result = Assert.Single(Verify(module, diagnostics).Functions);
        Assert.Empty(result.PreconditionResults);
        Assert.NotEqual(ContractVerificationStatus.Proven, Assert.Single(result.PostconditionResults).Status);
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
