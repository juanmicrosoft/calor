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
            module Test
              func Test public (bool:x) → int
                match x
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
        var diagnostics = Check("      case true\n        return 1\n      case false\n        return 0");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Theory]
    [InlineData("false", "true")]
    [InlineData("true", "false")]
    public void Check_BoolMissingBranch_ReportsNonExhaustiveError(string covered, string missing)
    {
        var diagnostics = Check($"      case {covered}\n        return 0");
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.NonExhaustiveMatch));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(missing, diagnostic.Message);
    }

    [Theory]
    [InlineData("_")]
    [InlineData("y")]
    public void Check_CatchAllMakesExhaustive(string pattern)
    {
        var diagnostics = Check($"      case {pattern}\n        return 0");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_PatternAfterWildcard_ReportsUnreachable()
    {
        var diagnostics = Check("      case _\n        return 0\n      case true\n        return 1");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UnreachablePattern);
    }

    [Fact]
    public void Check_DuplicateLiteralPattern_ReportsDuplicate()
    {
        var diagnostics = Check("      case true\n        return 1\n      case true\n        return 2\n      case _\n        return 0");
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
            module RemovedPatterns
              func Probe public (object:value) → int
                return match value
                  case {{pattern}} → 1
                  case _ → 0
            """;
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }
}
