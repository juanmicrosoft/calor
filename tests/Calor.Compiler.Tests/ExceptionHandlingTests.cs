using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for exception handling (try/catch/finally) parsing, code generation, and error cases.
/// </summary>
public class ExceptionHandlingTests
{

    #region Code Generation Tests

    [Fact]
    public void CodeGen_BasicTryCatch_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func SafeDivide (int:a, int:b) → int
    !
    try
      return (a / b)
    catch DivideByZeroException:ex
      return 0
";

        var result = ParseAndEmit(source);

        Assert.Contains("try", result);
        Assert.Contains("catch (DivideByZeroException ex)", result);
        Assert.Contains("return a / b;", result);
        Assert.Contains("return 0;", result);
    }

    [Fact]
    public void CodeGen_TryCatchFinally_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func WithFinally () → void
    ! cw
    try
      Console.WriteLine(""try"")
    catch Exception:e
      Console.WriteLine(""catch"")
    finally
      Console.WriteLine(""finally"")
";

        var result = ParseAndEmit(source);

        Assert.Contains("try", result);
        Assert.Contains("catch (Exception e)", result);
        Assert.Contains("finally", result);
    }

    [Fact]
    public void CodeGen_NestedTryCatch_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func Nested (int:x) → int
    !
    try
      try
        return (100 / x)
      catch DivideByZeroException:inner
        return -1
    catch Exception:outer
      return -2
";

        var result = ParseAndEmit(source);

        // Should have two try blocks
        var tryCount = CountOccurrences(result, "try");
        Assert.Equal(2, tryCount);

        // Should have two catch blocks
        Assert.Contains("catch (DivideByZeroException inner)", result);
        Assert.Contains("catch (Exception outer)", result);
    }

    [Fact]
    public void CodeGen_MultipleCatchClauses_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func MultiCatch (int:x) → string
    !
    try
      return (100 / x)
    catch DivideByZeroException:e1
      return ""divide by zero""
    catch ArithmeticException:e2
      return ""arithmetic error""
    catch Exception:e3
      return ""general error""
";

        var result = ParseAndEmit(source);

        Assert.Contains("catch (DivideByZeroException e1)", result);
        Assert.Contains("catch (ArithmeticException e2)", result);
        Assert.Contains("catch (Exception e3)", result);
    }

    [Fact]
    public void CodeGen_CatchAll_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func CatchAll () → void
    ! cw
    try
      Console.WriteLine(""try"")
    catch
      Console.WriteLine(""caught"")
";

        var result = ParseAndEmit(source);

        Assert.Contains("try", result);
        // Catch-all without type should just be "catch"
        Assert.Contains("catch", result);
    }

    [Fact]
    public void CodeGen_WhenFilter_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func Filtered (int:code) → string
    !
    try
      throw ""Error""
    catch Exception:ex when (code == 42)
      return ""special""
    catch Exception:ex
      return ""general""
";

        var result = ParseAndEmit(source);

        Assert.Contains("when (code == 42)", result);
    }

    [Fact]
    public void CodeGen_Rethrow_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func Rethrow () → void
    ! cw
    try
      throw ""Error""
    catch Exception:ex
      Console.WriteLine(""caught"")
      throw
";

        var result = ParseAndEmit(source);

        Assert.Contains("catch (Exception ex)", result);
        // Rethrow should generate "throw;"
        Assert.Contains("throw;", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_StringWrapsInException()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    throw ""error message""
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new System.Exception(\"error message\")", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_IntLiteralWrapsInException()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    throw 42
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new System.Exception(42.ToString())", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_BoolLiteralWrapsInException()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    throw true
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new System.Exception(true.ToString())", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_NewExceptionPassesThrough()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    throw new ArgumentException(""bad arg"")
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new ArgumentException(\"bad arg\")", result);
        Assert.DoesNotContain("System.Exception", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_VariablePassesThrough()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    var ex = new InvalidOperationException(""oops"")
    throw ex
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw ex;", result);
        Assert.DoesNotContain("System.Exception", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_FloatLiteralWrapsInException()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    throw 3.14
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new System.Exception(3.14.ToString())", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_DecimalLiteralWrapsInException()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    throw 18.00m
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new System.Exception(18.00m.ToString())", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_CharLiteralWrapsInException()
    {
        var source = @"
module Test Test block
  func Throw () → void
    !
    throw 'x'
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new System.Exception('x'.ToString())", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_InterpolatedStringWrapsInException()
    {
        var source = @"
module Test Test block
  func Throw (string:name) → void
    !
    throw ""Hello, ${name}!""
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw new System.Exception($\"Hello, {name}!\")", result);
    }

    [Fact]
    public void CodeGen_TryOnlyFinally_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func TryFinally () → void
    ! cw
    try
      Console.WriteLine(""try"")
    finally
      Console.WriteLine(""finally"")
";

        var result = ParseAndEmit(source);

        Assert.Contains("try", result);
        Assert.Contains("finally", result);
        // Should not have catch when there's no catch clause
        var catchCount = CountOccurrences(result, "catch");
        Assert.Equal(0, catchCount);
    }
    #endregion
    #region Negative/Error Case Tests

    [Fact]
    public void Parse_TryWithoutSourceId_ParsesSuccessfully()
    {
        var source = @"
module Test Test block
  func Bad () → void
    !
    try
      Console.WriteLine(""try"")
    catch Exception:e
      Console.WriteLine(""catch"")
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(diagnostic => diagnostic.Message)));
        var statement = Assert.IsType<Ast.TryStatementNode>(Assert.Single(module.Functions[0].Body));
        Assert.Single(statement.TryBody);
        Assert.Single(Assert.Single(statement.CatchClauses).Body);
    }

    [Fact]
    public void Parse_EmptyTryBlock_ParsesSuccessfully()
    {
        // Empty try blocks are syntactically valid (though semantically questionable)
        var source = @"
module Test Test block
  func Empty () → void
    !
    try
    catch Exception:e
      Console.WriteLine(""catch"")
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_TryWithoutCatchOrFinally_ParsesButMayWarn()
    {
        // Try without catch or finally - syntactically allowed
        var source = @"
module Test Test block
  func NoHandler () → void
    ! cw
    try
      Console.WriteLine(""try only"")
";

        var module = Parse(source, out var diagnostics);

        // Should parse, but C# requires at least catch or finally
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_CatchWithInvalidExceptionType_ParsesSuccessfully()
    {
        // Parser accepts any identifier as exception type (type checking is separate)
        var source = @"
module Test Test block
  func Bad () → void
    !
    try
      Console.WriteLine(""try"")
    catch NonExistentException:e
      Console.WriteLine(""catch"")
";

        var module = Parse(source, out var diagnostics);

        // Parser should accept it (semantic analysis would catch type errors)
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_WhenFilterWithInvalidExpression_ParsesWithFallback()
    {
        // The parser now has a fallback for unknown Lisp operators (treats them as calls),
        // so (invalid.operator x) parses without errors.
        var source = @"
module Test Test block
  func Bad (int:x) → void
    !
    try
      Console.WriteLine(""try"")
    catch Exception:e when invalid.@operator(x)
      Console.WriteLine(""catch"")
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_RethrowOutsideCatch_ParsesButSemanticallyInvalid()
    {
        // Parser allows rethrow anywhere, but it's semantically invalid outside catch
        var source = @"
module Test Test block
  func Bad () → void
    !
    throw
";

        var module = Parse(source, out var diagnostics);

        // Parser should accept it (semantic analysis would catch the error)
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_TryCatchFinally_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func RoundTrip () → void
    ! cw
    try
      Console.WriteLine(""try"")
    catch Exception:e
      Console.WriteLine(""catch"")
    finally
      Console.WriteLine(""finally"")
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Re-parse formatted output
        var module2 = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, $"Formatted output should parse:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");
    }

    [Fact]
    public void RoundTrip_WhenFilter_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func WhenRoundTrip (int:code) → string
    !
    try
      throw ""Error""
    catch Exception:ex when (code == 42)
      return ""special""
    catch Exception:ex
      return ""general""
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify when filter is preserved
        Assert.Contains("when (code == 42)", formatted);

        // Re-parse formatted output
        var module2 = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, $"Formatted output should parse:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");
    }

    [Fact]
    public void RoundTrip_Rethrow_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func RethrowRoundTrip () → void
    ! cw
    try
      throw ""Error""
    catch
      Console.WriteLine(""caught"")
      throw
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify rethrow is preserved
        Assert.Contains("throw", formatted.Split('\n').Select(line => line.Trim()));

        // Re-parse formatted output
        var module2 = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, $"Formatted output should parse:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");
    }

    #endregion

    #region Helper Methods

    private static Ast.ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    #endregion

    #region Using Statement (§USE) Tests

    [Fact]
    public void CodeGen_UsingStatement_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func ReadFile (string:path) → void
    ! cw
    using StreamReader:reader = new StreamReader(path)
      Console.WriteLine(""inside using"")
";

        var result = ParseAndEmit(source);

        Assert.Contains("using (StreamReader reader =", result);
        Assert.Contains("new StreamReader(path)", result);
    }

    [Fact]
    public void CodeGen_UsingStatementNoType_UsesVar()
    {
        var source = @"
module Test Test block
  func ReadFile (string:path) → void
    ! cw
    using reader = new StreamReader(path)
      Console.WriteLine(""inside using"")
";

        var result = ParseAndEmit(source);

        Assert.Contains("using (var reader =", result);
    }

    [Fact]
    public void CodeGen_UsingStatementWithMethodCallResource_GeneratesValidCSharp()
    {
        var source = @"
module Test Test block
  func Process (string:path) → void
    ! cw
    using FileStream:stream = File.OpenRead(path)
      Console.WriteLine(""reading"")
";

        var result = ParseAndEmit(source);

        Assert.Contains("using (FileStream stream =", result);
        Assert.Contains("File.OpenRead(path)", result);
    }

    #endregion
}
