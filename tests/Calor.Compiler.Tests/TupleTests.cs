using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for tuple type parsing, tuple expression parsing, and round-trip compilation.
/// </summary>
public class TupleTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Tuple Type Parsing

    [Fact]
    public void Parse_TupleReturnType_EmitsCorrectCSharp()
    {
        var calor = """
            §M{m001:TupleType}
            §CL{c001:Tuples:pub:stat}
            §MT{m002:GetMinMax:pub:stat} (i32:a, i32:b) -> (i32, i32)
            §IF{if003} (< a b)
            §R (a, b)
            §/I{if003}
            §R (b, a)
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "tuple-test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("(int, int)", result.GeneratedCode);
        Assert.Contains("(a, b)", result.GeneratedCode);
        Assert.Contains("(b, a)", result.GeneratedCode);
    }

    [Fact]
    public void Parse_NamedTupleReturnType_EmitsCorrectCSharp()
    {
        var calor = """
            §M{m001:TupleType}
            §CL{c001:Tuples:pub:stat}
            §MT{m002:GetPerson:pub:stat} () -> (str Name, i32 Age)
            §R ("Alice", 30)
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "tuple-test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("(string Name, int Age)", result.GeneratedCode);
    }

    [Fact]
    public void Parse_TupleParameterType_EmitsCorrectCSharp()
    {
        var calor = """
            §M{m001:TupleType}
            §CL{c001:Tuples:pub:stat}
            §MT{m002:GetFirst:pub:stat} ((i32, i32):tuple) -> i32
            §R tuple.Item1
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "tuple-test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("(int, int) tuple", result.GeneratedCode);
        Assert.Contains("tuple.Item1", result.GeneratedCode);
    }

    #endregion

    #region Tuple Expression Parsing

    [Fact]
    public void Parse_TupleReturnExpression_ProducesTupleLiteralNode()
    {
        var calor = """
            §M{m001:TupleType}
            §CL{c001:Tuples:pub:stat}
            §MT{m002:MakeTuple:pub:stat} (i32:a, i32:b) -> (i32, i32)
            §R (a, b)
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calor, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var method = Assert.Single(Assert.Single(module.Classes).Methods);
        // Find the return statement with a tuple
        var returnStmt = method.Body.OfType<ReturnStatementNode>().First();
        Assert.IsType<TupleLiteralNode>(returnStmt.Expression);
        var tuple = (TupleLiteralNode)returnStmt.Expression!;
        Assert.Equal(2, tuple.Elements.Count);
    }

    [Fact]
    public void Parse_TupleWithLiterals_EmitsCorrectCSharp()
    {
        var calor = """
            §M{m001:TupleType}
            §CL{c001:Tuples:pub:stat}
            §MT{m002:GetPair:pub:stat} () -> (i32, i32)
            §R (10, 20)
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "tuple-test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("(10, 20)", result.GeneratedCode);
    }

    [Fact]
    public void Parse_TupleWithStringAndInt_EmitsCorrectCSharp()
    {
        var calor = """
            §M{m001:TupleType}
            §CL{c001:Tuples:pub:stat}
            §MT{m002:GetPerson:pub:stat} () -> (str, i32)
            §R ("Alice", 30)
            §/MT{m002}
            §/CL{c001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "tuple-test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("(\"Alice\", 30)", result.GeneratedCode);
    }

    #endregion

    #region Round-Trip: C# → Calor → Parse → Emit C#

    [Fact]
    public void RoundTrip_TupleReturnType_Preserved()
    {
        var csharp = """
            namespace TupleType
            {
                public static class Tuples
                {
                    public static (int, int) GetMinMax(int a, int b)
                    {
                        if (a < b) return (a, b);
                        return (b, a);
                    }
                    public static (string Name, int Age) GetPerson()
                    {
                        return ("Alice", 30);
                    }
                    public static int GetFirst((int, int) tuple)
                    {
                        return tuple.Item1;
                    }
                }
            }
            """;

        // C# → Calor AST
        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        // Calor AST → Calor text
        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);

        // Verify the Calor text contains tuple syntax
        Assert.Contains("(i32, i32)", calrText);
        Assert.Contains("(str Name, i32 Age)", calrText);

        // Calor text → Parse → Compile → C#
        var compileResult = Program.Compile(calrText, "round-trip.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors,
            $"Compilation errors:\n{FormatDiagnostics(compileResult)}\n\nCalor text:\n{calrText}");

        // Verify the generated C# contains correct tuple types
        Assert.Contains("(int, int)", compileResult.GeneratedCode);
        Assert.Contains("(string Name, int Age)", compileResult.GeneratedCode);
        Assert.Contains("(int, int) tuple", compileResult.GeneratedCode);
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }

    private static string FormatDiagnostics(CompilationResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"[{d.Code}] {d.Message}");
        return string.Join("\n", errors);
    }

    #endregion
}
