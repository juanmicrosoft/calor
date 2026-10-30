using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Xunit;

namespace Calor.Compiler.Tests;

public class PatternCheckerTests
{
    private static DiagnosticBag Check(string cases)
    {
        var source = """
            §M{m001:Test}
              §F{f001:Test:pub} (bool:x) -> i32
                §W{m1} x
            """ + "\n" + cases;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", PrimitiveType.Bool);
        new PatternChecker(diagnostics, typeEnv).Check(module);
        return diagnostics;
    }

    [Fact]
    public void Check_BoolExhaustive_NoWarning()
    {
        var diagnostics = Check("      §K true\n        §R 1\n      §K false\n        §R 0");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Theory]
    [InlineData("false", "true")]
    [InlineData("true", "false")]
    public void Check_BoolMissingBranch_ReportsNonExhaustiveError(string covered, string missing)
    {
        var diagnostics = Check($"      §K {covered}\n        §R 0");
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.NonExhaustiveMatch));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(missing, diagnostic.Message);
    }

    [Theory]
    [InlineData("_")]
    [InlineData("y")]
    public void Check_CatchAllMakesExhaustive(string pattern)
    {
        var diagnostics = Check($"      §K {pattern}\n        §R 0");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_PatternAfterWildcard_ReportsUnreachable()
    {
        var diagnostics = Check("      §K _\n        §R 0\n      §K true\n        §R 1");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnreachablePattern);
    }

    [Fact]
    public void Check_DuplicateLiteralPattern_ReportsDuplicate()
    {
        var diagnostics = Check("      §K true\n        §R 1\n      §K true\n        §R 2\n      §K _\n        §R 0");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.DuplicatePattern);
    }

    [Theory]
    [InlineData("§SM _")]
    [InlineData("§NN")]
    [InlineData("§OK _")]
    [InlineData("§ERR _")]
    public void RemovedPatterns_AreRejectedBeforeExhaustivenessAnalysis(string pattern)
    {
        var source = $$"""
            §M{m1:RemovedPatterns}
              §F{f1:Probe:pub} (object:value) -> i32
                §R §W{w1} value
                  §K {{pattern}} → 1
                  §K _ → 0
            """;
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }
}
