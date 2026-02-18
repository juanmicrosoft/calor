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
§M{m1:Test}
§F{f1:SafeDivide:pub}
§I{i32:a}
§I{i32:b}
§O{i32}
§TR{t1}
§R (/ a b)
§CA{DivideByZeroException:ex}
§R 0
§/TR{t1}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("try", result);
        Assert.Contains("catch (DivideByZeroException ex)", result);
        Assert.Contains("return (a / b);", result);
        Assert.Contains("return 0;", result);
    }

    [Fact]
    public void CodeGen_TryCatchFinally_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§F{f1:WithFinally:pub}
§O{void}
§E{cw}
§TR{t1}
§P ""try""
§CA{Exception:e}
§P ""catch""
§FI
§P ""finally""
§/TR{t1}
§/F{f1}
§/M{m1}
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
§M{m1:Test}
§F{f1:Nested:pub}
§I{i32:x}
§O{i32}
§TR{t1}
§TR{t2}
§R (/ 100 x)
§CA{DivideByZeroException:inner}
§R -1
§/TR{t2}
§CA{Exception:outer}
§R -2
§/TR{t1}
§/F{f1}
§/M{m1}
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
§M{m1:Test}
§F{f1:MultiCatch:pub}
§I{i32:x}
§O{str}
§TR{t1}
§R (/ 100 x)
§CA{DivideByZeroException:e1}
§R ""divide by zero""
§CA{ArithmeticException:e2}
§R ""arithmetic error""
§CA{Exception:e3}
§R ""general error""
§/TR{t1}
§/F{f1}
§/M{m1}
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
§M{m1:Test}
§F{f1:CatchAll:pub}
§O{void}
§E{cw}
§TR{t1}
§P ""try""
§CA
§P ""caught""
§/TR{t1}
§/F{f1}
§/M{m1}
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
§M{m1:Test}
§F{f1:Filtered:pub}
§I{i32:code}
§O{str}
§TR{t1}
§TH ""Error""
§CA{Exception:ex} §WHEN (== code 42)
§R ""special""
§CA{Exception:ex}
§R ""general""
§/TR{t1}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("when ((code == 42))", result);
    }

    [Fact]
    public void CodeGen_Rethrow_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§F{f1:Rethrow:pub}
§O{void}
§E{cw}
§TR{t1}
§TH ""Error""
§CA{Exception:ex}
§P ""caught""
§RT
§/TR{t1}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("catch (Exception ex)", result);
        // Rethrow should generate "throw;"
        Assert.Contains("throw;", result);
    }

    [Fact]
    public void CodeGen_ThrowStatement_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§F{f1:Throw:pub}
§O{void}
§TH ""error message""
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("throw", result);
        Assert.Contains("error message", result);
    }

    [Fact]
    public void CodeGen_TryOnlyFinally_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§F{f1:TryFinally:pub}
§O{void}
§E{cw}
§TR{t1}
§P ""try""
§FI
§P ""finally""
§/TR{t1}
§/F{f1}
§/M{m1}
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
    public void Parse_MismatchedTryId_ReportsError()
    {
        var source = @"
§M{m1:Test}
§F{f1:Bad:pub}
§O{void}
§TR{t1}
§P ""try""
§/TR{t2}
§/F{f1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        var errorMessages = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        Assert.Contains("does not match", errorMessages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingTryId_ReportsError()
    {
        var source = @"
§M{m1:Test}
§F{f1:Bad:pub}
§O{void}
§TR
§P ""try""
§/TR
§/F{f1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        var errorMessages = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        Assert.Contains("id", errorMessages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EmptyTryBlock_ParsesSuccessfully()
    {
        // Empty try blocks are syntactically valid (though semantically questionable)
        var source = @"
§M{m1:Test}
§F{f1:Empty:pub}
§O{void}
§TR{t1}
§CA{Exception:e}
§P ""catch""
§/TR{t1}
§/F{f1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_TryWithoutCatchOrFinally_ParsesButMayWarn()
    {
        // Try without catch or finally - syntactically allowed
        var source = @"
§M{m1:Test}
§F{f1:NoHandler:pub}
§O{void}
§E{cw}
§TR{t1}
§P ""try only""
§/TR{t1}
§/F{f1}
§/M{m1}
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
§M{m1:Test}
§F{f1:Bad:pub}
§O{void}
§TR{t1}
§P ""try""
§CA{NonExistentException:e}
§P ""catch""
§/TR{t1}
§/F{f1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        // Parser should accept it (semantic analysis would catch type errors)
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parse_WhenFilterWithInvalidExpression_ReportsError()
    {
        var source = @"
§M{m1:Test}
§F{f1:Bad:pub}
§I{i32:x}
§O{void}
§TR{t1}
§P ""try""
§CA{Exception:e} §WHEN (invalid.operator x)
§P ""catch""
§/TR{t1}
§/F{f1}
§/M{m1}
";

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Parse_RethrowOutsideCatch_ParsesButSemanticallyInvalid()
    {
        // Parser allows rethrow anywhere, but it's semantically invalid outside catch
        var source = @"
§M{m1:Test}
§F{f1:Bad:pub}
§O{void}
§RT
§/F{f1}
§/M{m1}
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
§M{m1:Test}
§F{f1:RoundTrip:pub}
§O{void}
§E{cw}
§TR{t1}
§P ""try""
§CA{Exception:e}
§P ""catch""
§FI
§P ""finally""
§/TR{t1}
§/F{f1}
§/M{m1}
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
§M{m1:Test}
§F{f1:WhenRoundTrip:pub}
§I{i32:code}
§O{str}
§TR{t1}
§TH ""Error""
§CA{Exception:ex} §WHEN (== code 42)
§R ""special""
§CA{Exception:ex}
§R ""general""
§/TR{t1}
§/F{f1}
§/M{m1}
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify when filter is preserved
        Assert.Contains("§WHEN", formatted);

        // Re-parse formatted output
        var module2 = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors, $"Formatted output should parse:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");
    }

    [Fact]
    public void RoundTrip_Rethrow_FormatsAndReparses()
    {
        var source = @"
§M{m1:Test}
§F{f1:RethrowRoundTrip:pub}
§O{void}
§E{cw}
§TR{t1}
§TH ""Error""
§CA
§P ""caught""
§RT
§/TR{t1}
§/F{f1}
§/M{m1}
";

        // Parse original
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        // Format
        var formatter = new Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // Verify rethrow is preserved
        Assert.Contains("§RT", formatted);

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
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

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
§M{m1:Test}
§F{f1:ReadFile:pub}
§I{str:path}
§O{void}
§E{cw}
§USE{u1:reader:StreamReader} §NEW{StreamReader} §A path
  §P ""inside using""
§/USE{u1}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("using (StreamReader reader =", result);
        Assert.Contains("new StreamReader(path)", result);
    }

    [Fact]
    public void CodeGen_UsingStatementNoType_UsesVar()
    {
        var source = @"
§M{m1:Test}
§F{f1:ReadFile:pub}
§I{str:path}
§O{void}
§E{cw}
§USE{u1:reader} §NEW{StreamReader} §A path
  §P ""inside using""
§/USE{u1}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("using (var reader =", result);
    }

    [Fact]
    public void CodeGen_UsingStatementWithMethodCallResource_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§F{f1:Process:pub}
§I{str:path}
§O{void}
§E{cw}
§USE{u1:stream:FileStream} §C{File.OpenRead} §A path §/C
  §P ""reading""
§/USE{u1}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("using (FileStream stream =", result);
        Assert.Contains("File.OpenRead(path)", result);
    }

    [Fact]
    public void Parse_UsingStatementMismatchedId_ReportsError()
    {
        var source = @"
§M{m1:Test}
§F{f1:ReadFile:pub}
§O{void}
§USE{u1:reader:StreamReader} §NEW{StreamReader} §A ""test""
  §P ""inside""
§/USE{u2}
§/F{f1}
§/M{m1}
";

        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        parser.Parse();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MismatchedId);
    }

    #endregion
}
