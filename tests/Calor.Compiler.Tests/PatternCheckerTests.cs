using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Xunit;

namespace Calor.Compiler.Tests;

public class PatternCheckerTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Option Exhaustiveness

    [Fact]
    public void Check_OptionExhaustive_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K §SM _
        §R 1
      §K §NN
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new OptionType(PrimitiveType.Int));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_OptionMissingSome_ReportsNonExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K §NN
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new OptionType(PrimitiveType.Int));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
        Assert.Contains(checkDiagnostics, d => d.Message.Contains("Some(_)"));
    }

    [Fact]
    public void Check_OptionNonExhaustive_IsError_NotWarning()
    {
        // TIER1C: non-exhaustive match over Option is an error, regardless of
        // whether the match is in statement or expression position. See
        // docs/design/calor-direction.md — exhaustive match on known sum types
        // is mandatory syntax.
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K §NN
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new OptionType(PrimitiveType.Int));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        var diag = checkDiagnostics.FirstOrDefault(d => d.Code == DiagnosticCode.NonExhaustiveMatch);
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void Check_OptionMissingNone_ReportsNonExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K §SM _
        §R 1
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new OptionType(PrimitiveType.Int));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
        Assert.Contains(checkDiagnostics, d => d.Message.Contains("None"));
    }

    #endregion

    #region Result Exhaustiveness

    [Fact]
    public void Check_ResultExhaustive_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K §OK _
        §R 1
      §K §ERR _
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new ResultType(PrimitiveType.Int, PrimitiveType.String));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_ResultMissingOk_ReportsNonExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K §ERR _
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new ResultType(PrimitiveType.Int, PrimitiveType.String));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
        Assert.Contains(checkDiagnostics, d => d.Message.Contains("Ok(_)"));
    }

    [Fact]
    public void Check_ResultMissingErr_ReportsNonExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K §OK _
        §R 1
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new ResultType(PrimitiveType.Int, PrimitiveType.String));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
        Assert.Contains(checkDiagnostics, d => d.Message.Contains("Err(_)"));
    }

    #endregion

    #region Bool Exhaustiveness

    [Fact]
    public void Check_BoolExhaustive_NoWarning()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{bool:x}
  §O{i32}
    §W{m1} x
      §K BOOL:true
        §R 1
      §K BOOL:false
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", PrimitiveType.Bool);
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_BoolMissingTrue_ReportsNonExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{bool:x}
  §O{i32}
    §W{m1} x
      §K BOOL:false
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", PrimitiveType.Bool);
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
        Assert.Contains(checkDiagnostics, d => d.Message.Contains("true"));
    }

    [Fact]
    public void Check_BoolMissingFalse_ReportsNonExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{bool:x}
  §O{i32}
    §W{m1} x
      §K BOOL:true
        §R 1
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", PrimitiveType.Bool);
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
        Assert.Contains(checkDiagnostics, d => d.Message.Contains("false"));
    }

    #endregion

    #region Catch-All Patterns

    [Fact]
    public void Check_WildcardMakesExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K _
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new OptionType(PrimitiveType.Int));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_VariablePatternMakesExhaustive()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K y
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("x", new OptionType(PrimitiveType.Int));
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics, d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    #endregion

    #region Unreachable Patterns

    [Fact]
    public void Check_PatternAfterWildcard_ReportsUnreachable()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K _
        §R 0
      §K §SM _
        §R 1
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var checker = new PatternChecker(checkDiagnostics);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.UnreachablePattern);
    }

    [Fact]
    public void Check_DuplicateLiteralPattern_ReportsDuplicate()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K INT:1
        §R 1
      §K INT:1
        §R 2
      §K _
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var checker = new PatternChecker(checkDiagnostics);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.DuplicatePattern);
    }

    #endregion
}
