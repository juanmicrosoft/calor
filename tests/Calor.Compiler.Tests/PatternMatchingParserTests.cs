using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for pattern matching syntax support including arrow syntax,
/// variable and relational patterns, and exhaustiveness checking.
/// </summary>
public class PatternMatchingParserTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string Compile(string source, out DiagnosticBag diagnostics)
    {
        var module = Parse(source, out diagnostics);
        if (diagnostics.HasErrors) return "";

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    #region Parser Tests - Arrow Syntax

    [Fact]
    public void Parse_MatchWithArrowSyntax_CreatesMatchExpression()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case 1 → ""one""
      case 2 → ""two""
      case _ → ""other""";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);
        Assert.Equal(3, match.Cases.Count);
        Assert.IsType<LiteralPatternNode>(match.Cases[0].Pattern);
        Assert.IsType<LiteralPatternNode>(match.Cases[1].Pattern);
        Assert.IsType<WildcardPatternNode>(match.Cases[2].Pattern);
    }

    [Fact]
    public void Parse_MatchWithBlockSyntax_CreatesMatchExpression()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case 1
        return ""one""
      case _
        return ""other""";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);
        Assert.Equal(2, match.Cases.Count);
    }

    [Fact]
    public void Parse_MatchMixedSyntax_AllowsBothStyles()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case 1 → ""one""
      case 2
        System.Console.WriteLine(""two"")
        return ""two""
      case _ → ""other""";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);
        Assert.Equal(3, match.Cases.Count);

        // First and third cases have 1 statement (arrow syntax creates return)
        Assert.Single(match.Cases[0].Body);
        Assert.Single(match.Cases[2].Body);

        // Second case has 2 statements (print + return)
        Assert.Equal(2, match.Cases[1].Body.Count);
    }

    #endregion

    #region Parser Tests - Variable Pattern

    [Fact]
    public void Parse_VarPattern_CreatesVarPatternNode()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case var n → ""captured""";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);
        var varPattern = Assert.IsType<VarPatternNode>(match.Cases[0].Pattern);
        Assert.Equal("n", varPattern.Name);
    }

    [Fact]
    public void Parse_VarPatternWithGuard_CreatesVarPatternAndGuard()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case var n when n > 0 → ""positive""
      case _ → ""non-positive""";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);
        Assert.NotNull(match.Cases[0].Guard);
        var varPattern = Assert.IsType<VarPatternNode>(match.Cases[0].Pattern);
        Assert.Equal("n", varPattern.Name);
    }

    #endregion

    #region Parser Tests - Relational Pattern

    [Fact]
    public void Parse_RelationalPattern_CreatesRelationalPatternNode()
    {
        var source = @"
module Test
  func Grade public (int:score) → string
    return match score
      case >= 90 → ""A""
      case >= 80 → ""B""
      case _ → ""F""";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);
        var relPattern = Assert.IsType<RelationalPatternNode>(match.Cases[0].Pattern);
        Assert.Equal(">=", relPattern.Operator);
    }

    [Fact]
    public void Parse_AllRelationalOperators_ParsesCorrectly()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case >= 100 → ""gte100""
      case > 50 → ""gt50""
      case <= 10 → ""lte10""
      case < 0 → ""lt0""
      case _ → ""other""";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);

        var ops = match.Cases.Take(4).Select(c => ((RelationalPatternNode)c.Pattern).Operator).ToList();
        Assert.Equal(new[] { ">=", ">", "<=", "<" }, ops);
    }

    #endregion

    #region Parser Tests - Option/Result Patterns

    [Fact]
    public void Parse_SomeNonePattern_IsRejected()
    {
        var source = @"
module Test
  func Test public (bool:opt) → int
    return match opt
      case §SM §VAR{v} → v
      case §NN → 0";

        Parse(source, out var diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Parse_OkErrPattern_IsRejected()
    {
        var source = @"
module Test
  func Test public (int:res) → string
    return match res
      case §OK §VAR{v} → ""ok""
      case §ERR §VAR{e} → ""err""";

        Parse(source, out var diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    #endregion

    #region Code Generation Tests

    [Fact]
    public void Compile_MatchExpression_GeneratesValidCSharp()
    {
        var source = @"
module Test
  func GetDay public (int:day) → string
    return match day
      case 1 → ""Monday""
      case 2 → ""Tuesday""
      case _ → ""Unknown""";

        var csharp = Compile(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        Assert.Contains("switch", csharp);
        Assert.Contains("1 =>", csharp);
        Assert.Contains("\"Monday\"", csharp);
        Assert.Contains("_ =>", csharp);
    }

    [Fact]
    public void Compile_RelationalPatterns_GeneratesCSharpOperators()
    {
        var source = @"
module Test
  func Grade public (int:score) → string
    return match score
      case >= 90 → ""A""
      case < 60 → ""F""
      case _ → ""C""";

        var csharp = Compile(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        Assert.Contains(">= 90", csharp);
        Assert.Contains("< 60", csharp);
    }

    [Fact]
    public void Compile_GuardClause_GeneratesWhenExpression()
    {
        var source = @"
module Test
  func Sign public (int:x) → string
    return match x
      case var n when n > 0 → ""positive""
      case var n when n < 0 → ""negative""
      case _ → ""zero""";

        var csharp = Compile(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        Assert.Contains("when", csharp);
    }

    #endregion

    #region Exhaustiveness Tests

    [Fact]
    public void Check_WithWildcard_NoExhaustivenessWarning()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case 1 → ""one""
      case _ → ""other""";

        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors);

        var checkDiagnostics = new DiagnosticBag();
        var checker = new PatternChecker(checkDiagnostics, new TypeEnvironment());
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics,
            d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_BoolWithoutWildcard_BothCases_NoWarning()
    {
        var source = @"
module Test
  func Test public (bool:flag) → string
    return match flag
      case true → ""yes""
      case false → ""no""";

        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Errors.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("flag", PrimitiveType.Bool);
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics,
            d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_OptionMissingSome_ReportsNonExhaustive()
    {
        // Use block syntax to create MatchStatementNode (not wrapped in return)
        var source = @"
module Test
  func Test public (int?:opt) → int
    match opt
      case true
        return 0";

        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Errors.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("opt", PrimitiveType.Bool);
        var checker = new PatternChecker(checkDiagnostics, typeEnv);
        checker.Check(module);

        Assert.Contains(checkDiagnostics,
            d => d.Code == DiagnosticCode.NonExhaustiveMatch);
    }

    [Fact]
    public void Check_UnreachablePatternAfterWildcard_ReportsWarning()
    {
        // Use block syntax to create MatchStatementNode (not wrapped in return)
        var source = @"
module Test
  func Test public (int:x) → int
    match x
      case _
        return 0
      case 1
        return 1";

        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Errors.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var checker = new PatternChecker(checkDiagnostics, new TypeEnvironment());
        checker.Check(module);

        Assert.Contains(checkDiagnostics,
            d => d.Code == DiagnosticCode.UnreachablePattern);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_MatchExpression_PreservesSemantics()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x
      case 1 → ""one""
      case _ → ""other""";

        // Calor → C#
        var csharp = Compile(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        // Verify C# output is valid (contains expected constructs)
        Assert.Contains("switch", csharp);
        Assert.Contains("1 =>", csharp);
        Assert.Contains("_ =>", csharp);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void Parse_MismatchedMatchId_ReportsError()
    {
        var source = @"
§M{Test}
§F{Test:pub}
  §I{i32:x}
  §O{str}
  §R §W x
    §K 1 → ""one""
  §/W
§/F
§/M";

        var module = Parse(source, out var diagnostics);
        Assert.Contains(diagnostics, d => d.IsError);
    }

    [Fact]
    public void Parse_EmptyMatch_HasNoCases()
    {
        var source = @"
module Test
  func Test public (int:x) → string
    return match x";

        var module = Parse(source, out var diagnostics);
        // Parse should succeed but match should have no cases
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);
        Assert.Empty(match.Cases);
    }

    #endregion
}
