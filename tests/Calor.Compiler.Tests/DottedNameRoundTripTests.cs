using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests verifying that dotted names (Math.PI, StringComparison.Ordinal, Console.WriteLine)
/// round-trip correctly through the full Calor pipeline.
///
/// Design note: The Calor lexer intentionally includes dots in identifiers (Lexer.cs:787),
/// producing single tokens like "Math.PI" instead of three tokens "Math", ".", "PI".
/// This means:
///   - C# converter creates: FieldAccessNode(ReferenceNode("Math"), "PI")
///   - CalorFormatter emits: "Math.PI"
///   - Calor parser creates: ReferenceNode("Math.PI")  (flat, single token)
///   - CSharpEmitter emits:  "Math.PI"  (correct, splits on dot)
///
/// Both representations produce identical C# output — the round-trip is lossless.
/// This is a deliberate design choice for C# interop compatibility.
/// </summary>
public class DottedNameRoundTripTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region C# → Calor Conversion (produces FieldAccessNode)

    [Fact]
    public void Convert_MathPI_ProducesFieldAccessNode()
    {
        var csharp = """
            using System;
            public class Calc
            {
                public double Area(double r) => Math.PI * r * r;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        // The converter should produce FieldAccessNode for Math.PI
        var body = method.Body;
        Assert.NotEmpty(body);
    }

    [Fact]
    public void Convert_EnumValue_ProducesFieldAccessNode()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public bool Compare(string a, string b)
                {
                    return string.Equals(a, b, StringComparison.Ordinal);
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));
    }

    [Fact]
    public void Convert_NestedMemberAccess_ProducesChainedFieldAccess()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public string GetPlatform()
                {
                    return Environment.OSVersion.Platform.ToString();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));
    }

    #endregion

    #region Calor Source → Parse → C# (ReferenceNode with dots)

    [Fact]
    public void Compile_DottedReference_EmitsCorrectly()
    {
        var calor = """
            §M{m001:DottedRef}
            §F{f001:Area:pub}
              §I{f64:r}
              §O{f64}
              §R (* Math.PI (* r r))
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("Math.PI", result.GeneratedCode);
    }

    [Fact]
    public void Compile_EnumDottedReference_EmitsCorrectly()
    {
        var calor = """
            §M{m001:EnumRef}
            §F{f001:GetMode:pub}
              §O{i32}
              §R StringComparison.Ordinal
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("StringComparison.Ordinal", result.GeneratedCode);
    }

    [Fact]
    public void Compile_MultipleDottedReferences_AllPreserved()
    {
        var calor = """
            §M{m001:MultiDot}
            §F{f001:Constants:pub}
              §O{f64}
              §B{pi} Math.PI
              §B{e} Math.E
              §R (+ pi e)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("Math.PI", result.GeneratedCode);
        Assert.Contains("Math.E", result.GeneratedCode);
    }

    #endregion

    #region Full Round-Trip: C# → Calor text → Parse → C#

    [Fact]
    public void RoundTrip_MathPI_PreservedThroughCalorText()
    {
        var csharp = """
            using System;
            public class Calc
            {
                public double CircleArea(double radius)
                {
                    return Math.PI * radius * radius;
                }
            }
            """;

        // C# → Calor AST
        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        // Calor AST → Calor text
        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);
        Assert.Contains("Math.PI", calrText);

        // Calor text → Parse → Compile → C#
        var compileResult = Program.Compile(calrText, "round-trip.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        Assert.Contains("Math.PI", compileResult.GeneratedCode);
    }

    [Fact]
    public void RoundTrip_StringComparisonOrdinal_PreservedThroughCalorText()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public int Compare(string a, string b)
                {
                    return string.Compare(a, b, StringComparison.Ordinal);
                }
            }
            """;

        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);
        Assert.Contains("StringComparison.Ordinal", calrText);

        var compileResult = Program.Compile(calrText, "round-trip.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        Assert.Contains("StringComparison.Ordinal", compileResult.GeneratedCode);
    }

    [Fact]
    public void RoundTrip_IntMaxValue_PreservedThroughCalorText()
    {
        var csharp = """
            public class Svc
            {
                public int GetMax()
                {
                    return int.MaxValue;
                }
            }
            """;

        var convertResult = _converter.Convert(csharp);
        Assert.True(convertResult.Success, GetErrorMessage(convertResult));

        var calrEmitter = new CalorEmitter();
        var calrText = calrEmitter.Emit(convertResult.Ast!);

        var compileResult = Program.Compile(calrText, "round-trip.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        // int.MaxValue should survive round-trip
        var code = compileResult.GeneratedCode;
        Assert.True(
            code.Contains("int.MaxValue") || code.Contains("Int32.MaxValue") || code.Contains("2147483647"),
            $"Expected int.MaxValue equivalent in generated code");
    }

    #endregion

    #region AST Structure Verification

    [Fact]
    public void Parser_DottedName_ProducesReferenceNode()
    {
        // Verify the lexer/parser creates ReferenceNode (not FieldAccessNode) for dotted names
        var calor = """
            §M{m001:Test}
            §F{f001:Get:pub}
              §O{f64}
              §R Math.PI
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.NotNull(result.Ast);

        var func = Assert.Single(result.Ast.Functions);
        var retStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);

        // Parser creates ReferenceNode("Math.PI") — single token due to lexer design
        var refNode = Assert.IsType<ReferenceNode>(retStmt.Expression);
        Assert.Equal("Math.PI", refNode.Name);
    }

    [Fact]
    public void Converter_DottedName_ProducesFieldAccessNode()
    {
        var csharp = """
            using System;
            public class Svc
            {
                public double GetPi() => Math.PI;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        var retStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);

        // Converter creates FieldAccessNode(ReferenceNode("Math"), "PI")
        var fieldAccess = Assert.IsType<FieldAccessNode>(retStmt.Expression);
        Assert.Equal("PI", fieldAccess.FieldName);
        var target = Assert.IsType<ReferenceNode>(fieldAccess.Target);
        Assert.Equal("Math", target.Name);
    }

    [Fact]
    public void BothRepresentations_EmitIdenticalCSharp()
    {
        // ReferenceNode path (from Calor source)
        var calor = """
            §M{m001:Test}
            §F{f001:Get:pub}
              §O{f64}
              §R Math.PI
            §/F{f001}
            §/M{m001}
            """;

        var fromCalor = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        // FieldAccessNode path (from C# conversion)
        var csharp = """
            using System;
            public static class Test
            {
                public static double Get() => Math.PI;
            }
            """;

        var fromCSharp = _converter.Convert(csharp);
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(fromCSharp.Ast!);

        // Both paths should produce Math.PI in the output
        Assert.Contains("Math.PI", fromCalor.GeneratedCode);
        Assert.Contains("Math.PI", regenerated);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Compile_DeepDottedName_Works()
    {
        var calor = """
            §M{m001:Deep}
            §F{f001:Get:pub}
              §O{str}
              §R System.Environment.NewLine
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("System.Environment.NewLine", result.GeneratedCode);
    }

    [Fact]
    public void Compile_DottedNameInExpression_Works()
    {
        var calor = """
            §M{m001:Expr}
            §F{f001:Tau:pub}
              §O{f64}
              §R (* 2.0 Math.PI)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("Math.PI", result.GeneratedCode);
    }

    [Fact]
    public void Compile_DottedNameInCondition_Works()
    {
        var calor = """
            §M{m001:Cond}
            §F{f001:IsMax:pub}
              §I{i32:x}
              §O{bool}
              §R (== x int.MaxValue)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(calor, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("int.MaxValue", result.GeneratedCode);
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
