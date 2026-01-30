using Opal.Compiler.Analysis;
using Opal.Compiler.Ast;
using Opal.Compiler.Diagnostics;
using Opal.Compiler.Parsing;
using Opal.Compiler.TypeChecking;
using Xunit;

namespace Opal.Compiler.Tests;

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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE §SOME _
        §RETURN INT:1
      §CASE §NONE
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE §NONE
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
    public void Check_OptionMissingNone_ReportsNonExhaustive()
    {
        var source = @"
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE §SOME _
        §RETURN INT:1
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE §OK _
        §RETURN INT:1
      §CASE §ERR _
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE §ERR _
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE §OK _
        §RETURN INT:1
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=BOOL]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE BOOL:true
        §RETURN INT:1
      §CASE BOOL:false
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=BOOL]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE BOOL:false
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=BOOL]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE BOOL:true
        §RETURN INT:1
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE _
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE y
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE _
        §RETURN INT:0
      §CASE §SOME _
        §RETURN INT:1
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
§MODULE[id=m001][name=Test]
§FUNC[id=f001][name=Test][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §BODY
    §MATCH[id=m1] x
      §CASE INT:1
        §RETURN INT:1
      §CASE INT:1
        §RETURN INT:2
      §CASE _
        §RETURN INT:0
    §END_MATCH[id=m1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
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
