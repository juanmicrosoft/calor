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
/// §VAR, §PREL patterns, and exhaustiveness checking.
/// </summary>
public class PatternMatchingParserTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K 1 → ""one""
    §K 2 → ""two""
    §K _ → ""other""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K 1
      §R ""one""
    §/K
    §K _
      §R ""other""
    §/K
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K 1 → ""one""
    §K 2
      §P ""two""
      §R ""two""
    §/K
    §K _ → ""other""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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

    #region Parser Tests - §VAR Pattern

    [Fact]
    public void Parse_VarPattern_CreatesVarPatternNode()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K §VAR{n} → ""captured""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K §VAR{n} §WHEN (> n 0) → ""positive""
    §K _ → ""non-positive""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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

    #region Parser Tests - §PREL Relational Pattern

    [Fact]
    public void Parse_RelationalPattern_CreatesRelationalPatternNode()
    {
        var source = @"
§M{m001:Test}
§F{f001:Grade:pub}
  §I{i32:score}
  §O{str}
  §R §W{sw1} score
    §K §PREL{gte} 90 → ""A""
    §K §PREL{gte} 80 → ""B""
    §K _ → ""F""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K §PREL{gte} 100 → ""gte100""
    §K §PREL{gt} 50 → ""gt50""
    §K §PREL{lte} 10 → ""lte10""
    §K §PREL{lt} 0 → ""lt0""
    §K _ → ""other""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
    public void Parse_SomeNonePattern_ParsesCorrectly()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{?i32:opt}
  §O{i32}
  §R §W{sw1} opt
    §K §SM §VAR{v} → v
    §K §NN → 0
  §/W{sw1}
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);

        Assert.IsType<SomePatternNode>(match.Cases[0].Pattern);
        Assert.IsType<NonePatternNode>(match.Cases[1].Pattern);
    }

    [Fact]
    public void Parse_OkErrPattern_ParsesCorrectly()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:res}
  §O{str}
  §R §W{sw1} res
    §K §OK §VAR{v} → ""ok""
    §K §ERR §VAR{e} → ""err""
  §/W{sw1}
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));

        var func = Assert.Single(module.Functions);
        var ret = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var match = Assert.IsType<MatchExpressionNode>(ret.Expression);

        Assert.IsType<OkPatternNode>(match.Cases[0].Pattern);
        Assert.IsType<ErrPatternNode>(match.Cases[1].Pattern);
    }

    #endregion

    #region Code Generation Tests

    [Fact]
    public void Compile_MatchExpression_GeneratesValidCSharp()
    {
        var source = @"
§M{m001:Test}
§F{f001:GetDay:pub}
  §I{i32:day}
  §O{str}
  §R §W{sw1} day
    §K 1 → ""Monday""
    §K 2 → ""Tuesday""
    §K _ → ""Unknown""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Grade:pub}
  §I{i32:score}
  §O{str}
  §R §W{sw1} score
    §K §PREL{gte} 90 → ""A""
    §K §PREL{lt} 60 → ""F""
    §K _ → ""C""
  §/W{sw1}
§/F{f001}
§/M{m001}";

        var csharp = Compile(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        Assert.Contains(">= 90", csharp);
        Assert.Contains("< 60", csharp);
    }

    [Fact]
    public void Compile_GuardClause_GeneratesWhenExpression()
    {
        var source = @"
§M{m001:Test}
§F{f001:Sign:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K §VAR{n} §WHEN (> n 0) → ""positive""
    §K §VAR{n} §WHEN (< n 0) → ""negative""
    §K _ → ""zero""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K 1 → ""one""
    §K _ → ""other""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{bool:flag}
  §O{str}
  §R §W{sw1} flag
    §K true → ""yes""
    §K false → ""no""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{?i32:opt}
  §O{i32}
    §W{m1} opt
      §K §NN
        §R 0
    §/W{m1}
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Errors.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var typeEnv = new TypeEnvironment();
        typeEnv.DefineVariable("opt", new OptionType(PrimitiveType.Int));
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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
    §W{m1} x
      §K _
        §R 0
      §K 1
        §R 1
    §/W{m1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K 1 → ""one""
    §K _ → ""other""
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K 1 → ""one""
  §/W{sw999}
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var diagnostics);
        Assert.Contains(diagnostics, d => d.IsError);
    }

    [Fact]
    public void Parse_EmptyMatch_HasNoCases()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
  §/W{sw1}
§/F{f001}
§/M{m001}";

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
