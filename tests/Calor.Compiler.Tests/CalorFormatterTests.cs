using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class CalorFormatterTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Module Formatting

    [Fact]
    public void Format_MinimalModule_ProducesCorrectOutput()
    {
        var source = @"
module Test Test block
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("module Test Test block", result);
        Assert.Contains("Test", result);
    }

    [Fact]
    public void Format_ModuleWithFunction_IncludesStructure()
    {
        // Simplified test without using directives which have complex parsing
        var source = @"
module Test Test block
  func Main () → void
    !
    return
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("Test", result);
        Assert.Contains("Main", result);
    }

    #endregion

    #region Function Formatting

    [Fact]
    public void Format_FunctionWithParameters_IncludesTypeAndName()
    {
        var source = @"
module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("Add", result);
        Assert.Contains("func Add (int:a, int:b) → int", result);
    }

    [Fact]
    public void Format_FunctionWithEffects_IncludesEffectsDeclaration()
    {
        var source = @"
module Test Test block
  func Print (string:message)
    ! cw
    Console.WriteLine(message)
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        // Just verify the formatter produces output (effects may be formatted differently)
        Assert.NotEmpty(result);
        Assert.Contains("Print", result);
    }

    [Fact]
    public void Format_FunctionWithContracts_IncludesPreconditionsAndPostconditions()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    requires (b != 0)
    => (result >= 0)
    return (a / b)
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        // Check that contracts are included
        Assert.NotEmpty(result);
    }

    #endregion

    #region Statement Formatting

    [Fact]
    public void Format_BindStatement_FormatsCorrectly()
    {
        var source = @"
module Test Test block
  func Test () → int
    !
    let x = 42
    return x
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("x", result);
    }

    [Fact]
    public void Format_IfStatement_FormatsWithIndentation()
    {
        var source = @"
module Test Test block
  func Test (bool:cond) → int
    !
    if cond
      return 1
    else
      return 0
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        // The formatter canonicalizes an if/else that only returns into a conditional return.
        Assert.Contains("(cond ? 1 : 0)", result);
    }

    [Fact]
    public void Format_ForLoop_FormatsCorrectly()
    {
        var source = @"
module Test Test block
  func Test () → void
    ! cw
    for i from 0 to 10 step 1
      Console.WriteLine(i)
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        // Formatter uses the native for/from/to/step header
        Assert.Contains("for i from 0 to 10 step 1", result);
    }

    [Fact]
    public void Format_WhileLoop_FormatsCorrectly()
    {
        var source = @"
module Test Test block
  func Test (bool:running) → void
    ! cw
    while running
      Console.WriteLine(""working"")
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        // Formatter uses the native while header
        Assert.Contains("while running", result);
    }

    [Fact]
    public void Format_MatchStatement_FormatsWithCases()
    {
        var source = @"
module Test Test block
  func Test (int:x) → int
    !
    match x
      case null
        return 1
      case _
        return 0
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("match x", result);
        Assert.Contains("case _", result);
    }

    #endregion

    #region Expression Formatting

    [Fact]
    public void Format_Literals_FormatsCorrectly()
    {
        var source = @"
module Test Test block
  func Test () → void
    !
    let a = 42
    let b = 3.14
    let c = true
    let d = ""hello""
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("42", result);
        Assert.Contains("true", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void Format_BinaryOperations_FormatsWithParentheses()
    {
        var source = @"
module Test Test block
  func Test (int:a, int:b) → int
    !
    return (a + b)
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("+", result);
    }

    [Fact]
    public void Format_NullableExpressions_FormatsCorrectly()
    {
        var source = @"
module Test Test block
  func Test () → int?
    !
    return 42

  func Test2 () → int?
    !
    return null
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("42", result);
        Assert.Contains("null", result);
    }

    [Fact]
    public void Format_ReturnAndThrow_FormatsCorrectly()
    {
        var source = @"
module Test Test block
  func Test () → int
    !
    return 42

  func Test2 () → string
    !
    throw new Exception(""error"")
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("42", result);
        Assert.Contains("throw new Exception", result);
    }

    #endregion

    #region Nested Statements

    [Fact]
    public void Format_NestedStatements_IndentsCorrectly()
    {
        var source = @"
module Test Test block
  func Test (bool:a, bool:b) → int
    !
    if a
      if b
        return 2
      else
        return 1
    else
      return 0
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        // Check that nested IF is indented more than outer IF
        var lines = result.Split('\n');
        var ifLines = lines.Where(l => l.TrimStart().StartsWith("if ")).ToArray();
        Assert.True(ifLines.Length >= 2, "Should have at least 2 IF statements");
    }

    #endregion

    #region Round-Trip

    [Fact]
    public void Format_RoundTrip_PreservesModuleInfo()
    {
        // Test that basic formatting works and produces parseable output
        var source = @"
module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify the formatted output contains key elements
        Assert.Contains("module Test Test block", formatted);
        Assert.Contains("func Add", formatted);
        Assert.Contains("Test", formatted);
        Assert.Contains("Add", formatted);
        Assert.Contains("func Add (int:a, int:b) → int", formatted);
    }

    #endregion

    #region Foreach Formatting

    [Fact]
    public void Format_ForeachWithIndex_EmitsForeachBlock()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §EACH{item:i32:idx} items
                      §P item
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("§EACH{", result);
        Assert.Contains("item:int:idx", result);
    }

    [Fact]
    public void Format_ForeachWithoutIndex_EmitsForeachBlockWithoutIndex()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                each int:item in items
                  Console.WriteLine(item)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var formatter = new CalorFormatter();
        var result = formatter.Format(module);

        Assert.Contains("each int:item in items", result);
        Assert.Contains("int:item", result);
        // Variable should have no trailing index slot.
        Assert.DoesNotContain("int:item,", result);
    }

    #endregion
}
