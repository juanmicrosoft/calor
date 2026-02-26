using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for P0 converter bugs found during equality sample conversion.
/// Bug 1: §B{name:const} emits invalid C# `const` for arrays.
/// Bug 2: Built-in operations (e.g., ToLower→(lower obj)) inside chained calls produce invalid C#.
/// </summary>
public class ConverterBugfixTests
{
    private static string ConvertToCalor(string csharpSource)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        var emitter = new CalorEmitter();
        return emitter.Emit(result.Ast!);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"[{i.Severity}] {i.Message}"));
    }

    #region Bug 1: Array binding should not emit :const

    [Fact]
    public void Converter_ArrayBinding_DoesNotEmitConst()
    {
        var csharp = @"
public class Test
{
    public void M()
    {
        var arr = new string[] { ""a"", ""b"" };
    }
}";
        var calor = ConvertToCalor(csharp);

        // Should not contain :const anywhere
        Assert.DoesNotContain(":const", calor);
    }

    [Fact]
    public void Converter_MutableBinding_EmitsTildePrefix()
    {
        // A variable that is reassigned should be mutable (~ prefix)
        var csharp = @"
public class Test
{
    public int M()
    {
        var name = 1;
        name = 2;
        return name;
    }
}";
        var calor = ConvertToCalor(csharp);

        // Mutable binding should use ~ prefix
        Assert.Contains("~name", calor);
        // Should not contain :const
        Assert.DoesNotContain(":const", calor);
    }

    [Fact]
    public void Converter_TypedMutableBinding_EmitsTildePrefixWithType()
    {
        // A typed variable that is reassigned should produce §B{~name:type}
        var csharp = @"
public class Test
{
    public string M()
    {
        string name = ""a"";
        name = ""b"";
        return name;
    }
}";
        var calor = ConvertToCalor(csharp);

        // Should have tilde prefix for mutable, with type
        Assert.Contains("~name", calor);
        Assert.DoesNotContain(":const", calor);
    }

    #endregion

    #region Bug 2: Chained call with built-in hoists to temp bind

    [Fact]
    public void Converter_ChainedCallWithBuiltin_HoistsToTempBind()
    {
        var csharp = @"
public class Test
{
    public int M(string obj)
    {
        return obj.ToLower().GetHashCode();
    }
}";
        var calor = ConvertToCalor(csharp);

        // The built-in ToLower() is hoisted to a temp bind, GetHashCode called on temp
        Assert.Contains("_chain", calor);
        Assert.Contains("GetHashCode", calor);
        // Should NOT contain the broken CalorEmitter serialization pattern
        Assert.DoesNotContain("§C{(§C{", calor);
    }

    [Fact]
    public void Converter_DeeperChainWithMultipleBuiltins_HoistsToTempBinds()
    {
        // When multiple built-in operations are chained (e.g., ToLower().Trim()),
        // each built-in is hoisted to a temp bind
        var csharp = @"
public class Test
{
    public int M(string obj)
    {
        return obj.ToLower().Trim().GetHashCode();
    }
}";
        var calor = ConvertToCalor(csharp);

        // Chain is decomposed via temp binds
        Assert.Contains("_chain", calor);
        Assert.Contains("GetHashCode", calor);
        // Should NOT contain the broken CalorEmitter serialization pattern
        Assert.DoesNotContain("§C{(§C{", calor);
    }

    #endregion

    #region Round-trip tests

    [Fact]
    public void RoundTrip_ArrayBinding_ProducesValidCSharp()
    {
        var csharp = @"
public class Test
{
    public void M()
    {
        var arr = new string[] { ""a"", ""b"" };
    }
}";
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Compile Calor back to C#
        // Disable effect enforcement: the converter doesn't generate §E{} annotations
        var compilationResult = Program.Compile(conversionResult.CalorSource!, "roundtrip.calr",
            new CompilationOptions { EnforceEffects = false });

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        // Generated C# should not contain invalid 'const' for array
        Assert.DoesNotContain("const ", compilationResult.GeneratedCode);
    }

    [Fact]
    public void RoundTrip_ChainedBuiltinCall_ProducesValidCSharp()
    {
        var csharp = @"
public class Test
{
    public int M(string obj)
    {
        return obj.ToLower().GetHashCode();
    }
}";
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Compile Calor back to C#
        // Disable effect enforcement: the converter doesn't generate §E{} annotations
        var compilationResult = Program.Compile(conversionResult.CalorSource!, "roundtrip.calr",
            new CompilationOptions { EnforceEffects = false });

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        // Generated C# should contain valid method chain
        Assert.Contains("GetHashCode()", compilationResult.GeneratedCode);
    }

    [Fact]
    public void RoundTrip_TypedMutableBinding_ProducesValidCSharp()
    {
        var csharp = @"
public class Test
{
    public string M()
    {
        string name = ""a"";
        name = ""b"";
        return name;
    }
}";
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Disable effect enforcement: the converter doesn't generate §E{} annotations
        var compilationResult = Program.Compile(conversionResult.CalorSource!, "roundtrip.calr",
            new CompilationOptions { EnforceEffects = false });

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        Assert.DoesNotContain("const ", compilationResult.GeneratedCode);
    }

    [Fact]
    public void RoundTrip_DeeperChainWithMultipleBuiltins_ProducesValidCSharp()
    {
        var csharp = @"
public class Test
{
    public int M(string obj)
    {
        return obj.ToLower().Trim().GetHashCode();
    }
}";
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Disable effect enforcement: the converter doesn't generate §E{} annotations
        var compilationResult = Program.Compile(conversionResult.CalorSource!, "roundtrip.calr",
            new CompilationOptions { EnforceEffects = false });

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        // Generated C# should contain valid chained method calls
        Assert.Contains("GetHashCode()", compilationResult.GeneratedCode);
    }

    #endregion

    #region Issue 3: st modifier alias for static

    [Fact]
    public void Parser_StModifier_OnClass_SetsIsStatic()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Helper:st}
§/CL{c1}
§/M{m1}";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.True(cls.IsStatic);
    }

    [Fact]
    public void Parser_StModifier_OnMethod_SetsIsStatic()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Helper}
§MT{m1:Greet:pub:st}
§/MT{m1}
§/CL{c1}
§/M{m1}";
        var module = ParseModule(source);
        var method = module.Classes[0].Methods[0];
        Assert.True(method.Modifiers.HasFlag(Calor.Compiler.Ast.MethodModifiers.Static));
    }

    [Fact]
    public void Parser_StModifier_OnClass_EmitsStaticInCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Helper:st}
§/CL{c1}
§/M{m1}";
        var csharp = ParseAndEmit(source);
        Assert.Contains("static class Helper", csharp);
    }

    #endregion

    #region Issue 11: Interpolated string format specifiers

    [Fact]
    public void Converter_InterpolatedString_PreservesFormatSpecifier()
    {
        var csharp = @"
public class Test
{
    public string M(decimal price)
    {
        return $""{price:C}"";
    }
}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // The Calor output should contain the format specifier
        var emitter = new CalorEmitter();
        var calor = emitter.Emit(result.Ast!);
        Assert.Contains(":C}", calor);
    }

    [Fact]
    public void Converter_InterpolatedString_FormatSpecifierRoundtrips()
    {
        var csharp = @"
public class Test
{
    public string M(double value)
    {
        return $""{value:F2}"";
    }
}";
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Round-trip: compile Calor → C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!, "roundtrip.calr",
            new CompilationOptions { EnforceEffects = false });

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        // Generated C# should contain the format specifier
        Assert.Contains(":F2}", compilationResult.GeneratedCode);
    }

    #endregion

    #region Issue 6: Fallback nodes populate issues list

    [Fact]
    public void Converter_FallbackNode_PopulatesIssuesList_WhenGracefulFallbackEnabled()
    {
        var csharp = @"
public class Test
{
    void M()
    {
        var x = checked(1 + 2);
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success);
        // Issues should contain a warning about the fallback
        Assert.True(result.Issues.Count > 0, "Expected at least one issue for fallback nodes");
        Assert.Contains(result.Issues, i =>
            i.Severity == ConversionIssueSeverity.Warning &&
            i.Message.Contains("fallback"));
    }

    #endregion

    #region Issue 10: dec type alias for decimal

    [Fact]
    public void TypeMapper_DecimalMapsToDec()
    {
        var csharp = @"
public class Test
{
    public decimal GetPrice() { return 0m; }
}";
        var calor = ConvertToCalor(csharp);
        // Calor should use 'dec' alias for decimal
        Assert.Contains("dec", calor);
    }

    [Fact]
    public void TypeMapper_DecRoundtripsToDecimal()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Calc}
§MT{m1:GetPrice:pub}
  §O{dec}
  §R 0
§/MT{m1}
§/CL{c1}
§/M{m1}";
        var csharp = ParseAndEmit(source);
        Assert.Contains("decimal", csharp);
    }

    #endregion

    #region Issue 8: §NEW{X}() with empty parens

    [Fact]
    public void Parser_NewExpression_EmptyParens_ParsesWithoutError()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyClass}
§MT{m1:Create:pub}
  §R §NEW{MyClass}()§/NEW
§/MT{m1}
§/CL{c1}
§/M{m1}";
        var module = ParseModule(source);
        Assert.NotNull(module);
    }

    [Fact]
    public void Parser_NewExpression_EmptyParens_EquivalentToWithout()
    {
        var sourceWithParens = @"
§M{m1:Test}
§CL{c1:MyClass}
§MT{m1:Create:pub}
  §R §NEW{MyClass}()§/NEW
§/MT{m1}
§/CL{c1}
§/M{m1}";
        var sourceWithout = @"
§M{m1:Test}
§CL{c1:MyClass}
§MT{m1:Create:pub}
  §R §NEW{MyClass}§/NEW
§/MT{m1}
§/CL{c1}
§/M{m1}";
        var csharp1 = ParseAndEmit(sourceWithParens);
        var csharp2 = ParseAndEmit(sourceWithout);

        Assert.Equal(csharp1, csharp2);
    }

    #endregion

    #region Edge cases: st + struct interaction

    [Fact]
    public void Parser_StStruct_DoesNotProduceStaticStruct()
    {
        // "st struct" should parse as static struct, not double-stat
        var source = @"
§M{m1:Test}
§CL{c1:Point:st struct}
§/CL{c1}
§/M{m1}";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.True(cls.IsStatic, "Should be static");
        Assert.True(cls.IsStruct, "Should be struct");
    }

    [Fact]
    public void Parser_StructAlone_IsNotStatic()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Point:struct}
§/CL{c1}
§/M{m1}";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.False(cls.IsStatic, "struct alone should not be static");
        Assert.True(cls.IsStruct, "Should be struct");
    }

    #endregion

    #region Edge cases: §NEW{X}() with trailing member access

    [Fact]
    public void Parser_NewExpression_EmptyParens_WithTrailingMemberAccess()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyClass}
§MT{m1:GetName:pub}
  §R §NEW{MyClass}()§/NEW.ToString
§/MT{m1}
§/CL{c1}
§/M{m1}";
        var csharp = ParseAndEmit(source);
        // Should produce new MyClass().ToString() in the output
        Assert.Contains("new MyClass()", csharp);
        Assert.Contains("ToString", csharp);
    }

    #endregion

    #region Edge cases: alignment clause in interpolated strings

    [Fact]
    public void Converter_InterpolatedString_PreservesAlignmentAndFormat()
    {
        var csharp = @"
public class Test
{
    public string M(double value)
    {
        return $""{value,10:F2}"";
    }
}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Calor output should contain alignment and format
        var emitter = new CalorEmitter();
        var calor = emitter.Emit(result.Ast!);
        Assert.Contains(",10:F2}", calor);
    }

    [Fact]
    public void Converter_InterpolatedString_AlignmentOnlyNoFormat()
    {
        var csharp = @"
public class Test
{
    public string M(string name)
    {
        return $""{name,-20}"";
    }
}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Calor output should contain alignment
        var emitter = new CalorEmitter();
        var calor = emitter.Emit(result.Ast!);
        Assert.Contains(",-20}", calor);
    }

    [Fact]
    public void Converter_InterpolatedString_AlignmentAndFormatRoundtrip()
    {
        var csharp = @"
public class Test
{
    public string M(double value)
    {
        return $""{value,10:F2}"";
    }
}";
        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Round-trip: compile Calor → C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!, "roundtrip.calr",
            new CompilationOptions { EnforceEffects = false });

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        // Generated C# should contain both alignment and format
        Assert.Contains(",10:F2}", compilationResult.GeneratedCode);
    }

    #endregion

    #region Helpers

    private static Ast.ModuleNode ParseModule(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return module;
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

    #endregion

    #region Challenge 10: String interpolation in base() constructor calls

    [Fact]
    public void Converter_BaseCallWithInterpolation_NativeCalorNotInterop()
    {
        var csharp = """
            using System;
            public class ContractException : Exception
            {
                public ContractException(string kind, string functionId)
                    : base($"{kind} contract violation in {functionId}")
                {
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var calor = emitter.Emit(result.Ast!);

        // Must NOT fall back to interop
        Assert.DoesNotContain("§CSHARP", calor);
        // Must contain native Calor base call with interpolation
        Assert.Contains("§BASE", calor);
        Assert.Contains("${kind}", calor);
        Assert.Contains("${functionId}", calor);
    }

    [Fact]
    public void RoundTrip_BaseCallWithInterpolation_Preserved()
    {
        var csharp = """
            using System;
            public class ContractException : Exception
            {
                public ContractException(string kind, string functionId)
                    : base($"{kind} contract violation in {functionId}")
                {
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);

        Assert.Contains(": base($\"{kind} contract violation in {functionId}\")", output);
    }

    [Fact]
    public void RoundTrip_BaseCallWithFormatSpecifier_Preserved()
    {
        var csharp = """
            using System;
            public class PriceException : Exception
            {
                public PriceException(decimal price)
                    : base($"Invalid price: {price:C2}")
                {
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);

        Assert.Contains("$\"Invalid price: {price:C2}\"", output);
    }

    [Fact]
    public void RoundTrip_BaseCallWithMethodCallInInterpolation_Preserved()
    {
        var csharp = """
            using System;
            public class DetailException : Exception
            {
                public DetailException(object obj)
                    : base($"Object: {obj.ToString()}")
                {
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);

        // Converter simplifies obj.ToString() to (str obj) cast
        Assert.Contains("base($\"Object: {", output);
        Assert.Contains("obj", output);
    }

    [Fact]
    public void RoundTrip_BaseCallWithMultipleInterpolationParts_Preserved()
    {
        var csharp = """
            using System;
            public class ValidationException : Exception
            {
                public ValidationException(string field, object value, string rule)
                    : base($"Field '{field}' with value '{value}' failed rule '{rule}'")
                {
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);

        Assert.Contains("$\"Field '{field}' with value '{value}' failed rule '{rule}'\"", output);
    }

    [Fact]
    public void RoundTrip_ThisCallWithInterpolation_Preserved()
    {
        var csharp = """
            using System;
            public class AppException : Exception
            {
                public AppException(string msg) : base(msg) { }
                public AppException(string code, string detail)
                    : this($"{code}: {detail}")
                {
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);

        Assert.Contains("$\"{code}: {detail}\"", output);
    }

    [Fact]
    public void InteropMode_BaseCallWithInterpolation_NativeNotInterop()
    {
        var csharp = """
            using System;
            public class ContractException : Exception
            {
                public ContractException(string kind, string functionId)
                    : base($"{kind} contract violation in {functionId}")
                {
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { Mode = ConversionMode.Interop });
        var result = converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Must not produce any interop blocks
        Assert.All(result.Ast!.Classes, c => Assert.Empty(c.InteropBlocks));
    }

    private string ConvertAndRoundTrip(string csharp)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calorEmitter = new CalorEmitter();
        var calorCode = calorEmitter.Emit(result.Ast!);

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorCode, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Re-parse failed:\n{string.Join("\n", diagnostics.Select(d => d.Message))}\nCalor:\n{calorCode}");

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    #endregion

    #region Bug: Enum pattern var prefix (Humanizer gap)

    [Fact]
    public void EmitPattern_DottedIdentifier_NoVarPrefix()
    {
        // VariablePatternNode("Status.OK") should emit "Status.OK", not "var Status.OK"
        var node = new Ast.VariablePatternNode(Parsing.TextSpan.Empty, "Status.OK");
        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("Status.OK", result);
        Assert.DoesNotContain("var", result);
    }

    [Fact]
    public void EmitPattern_SimpleVariable_StillGetsVarPrefix()
    {
        // VariablePatternNode("x") should still emit "var x"
        var node = new Ast.VariablePatternNode(Parsing.TextSpan.Empty, "x");
        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("var x", result);
    }

    [Fact]
    public void EmitPattern_DottedEnumInMatchExpression_ValidCSharp()
    {
        // Full match expression with dotted enum case and wildcard
        var span = Parsing.TextSpan.Empty;
        var matchExpr = new Ast.MatchExpressionNode(
            span, "m1",
            new Ast.ReferenceNode(span, "status"),
            new List<Ast.MatchCaseNode>
            {
                new(span,
                    new Ast.VariablePatternNode(span, "Status.OK"),
                    null,
                    new List<Ast.StatementNode>
                    {
                        new Ast.ReturnStatementNode(span, new Ast.StringLiteralNode(span, "ok"))
                    }),
                new(span,
                    new Ast.WildcardPatternNode(span),
                    null,
                    new List<Ast.StatementNode>
                    {
                        new Ast.ReturnStatementNode(span, new Ast.StringLiteralNode(span, "other"))
                    })
            },
            new Ast.AttributeCollection());

        var emitter = new CSharpEmitter();
        var result = matchExpr.Accept(emitter);

        Assert.Contains("Status.OK", result);
        Assert.DoesNotContain("var Status.OK", result);
    }

    [Fact]
    public void Parser_DottedIdentifierInPattern_ParsesAsConstant()
    {
        // §K Status.OK in Calor source should parse correctly and emit Status.OK in C#
        var source = @"
§M{m1:Test}
§F{f001:Describe:pub}
  §I{i32:status}
  §O{str}
  §R §W{m1} status
    §K Status.OK → ""ok""
    §K Status.Error → ""error""
    §K _ → ""unknown""
  §/W{m1}
§/F{f001}
§/M{m1}
";
        var result = ParseAndEmit(source);

        Assert.Contains("Status.OK", result);
        Assert.Contains("Status.Error", result);
        Assert.DoesNotContain("var Status.OK", result);
        Assert.DoesNotContain("var Status.Error", result);
    }

    [Fact]
    public void RoundTrip_EnumSwitchExpression_ProducesValidCSharp()
    {
        // Full C# → Calor → C# round-trip with enum switch expression
        var csharp = @"
public enum Status { OK, Error, Pending }
public class Test
{
    public string Describe(Status s)
    {
        return s switch
        {
            Status.OK => ""ok"",
            Status.Error => ""error"",
            _ => ""unknown""
        };
    }
}";
        var output = ConvertAndRoundTrip(csharp);

        Assert.Contains("Status.OK", output);
        Assert.Contains("Status.Error", output);
        Assert.DoesNotContain("var Status", output);
    }

    #endregion

    #region Bug: §IDX brace syntax round-trip (Humanizer gap)

    [Fact]
    public void IDX_SimpleRef_ParsesAndEmitsValidCSharp()
    {
        var source = @"
§M{m1:Test}
§F{f001:Main:pub}
  §O{void}
  §B{~result:str} §IDX{words} 0
§/F{f001}
§/M{m1}
";
        var result = ParseAndEmit(source);
        Assert.Contains("words[0]", result);
    }

    [Fact]
    public void IDX_CalorEmitter_EmitsBraceSyntaxForSimpleRef()
    {
        var span = Parsing.TextSpan.Empty;
        var arrayAccess = new Ast.ArrayAccessNode(
            span,
            new Ast.ReferenceNode(span, "words"),
            new Ast.IntLiteralNode(span, 0));

        var calorEmitter = new CalorEmitter();
        var calorOutput = arrayAccess.Accept(calorEmitter);

        Assert.Equal("§IDX{words} 0", calorOutput);
    }

    [Fact]
    public void IDX_CSharpIndexer_RoundTrips()
    {
        var csharp = @"
public class Test
{
    public string GetFirst(string[] words)
    {
        return words[0];
    }
}";
        var output = ConvertAndRoundTrip(csharp);
        Assert.Contains("words[0]", output);
    }

    #endregion

    #region Bug: Lambda hoisting in converter (Humanizer gap)

    [Fact]
    public void Lambda_InMethodCall_ConvertsWithoutCrash()
    {
        var csharp = @"
using System.Linq;
public class Test
{
    public int[] Double(int[] numbers)
    {
        return numbers.Select(x => x * 2).ToArray();
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.NotNull(calor);

        // Lambda should be hoisted to a temp binding
        Assert.Contains("_lam", calor);
    }

    [Fact]
    public void Lambda_LinqChain_ConvertsWithoutCrash()
    {
        var csharp = @"
using System.Linq;
public class Test
{
    public int[] FilterAndDouble(int[] numbers)
    {
        return numbers.Where(x => x > 0).Select(x => x * 2).ToArray();
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.NotNull(calor);

        // Both lambdas should be hoisted
        Assert.Contains("_lam", calor);
    }

    [Fact]
    public void RoundTrip_Lambda_InMethodCall_ProducesValidCSharp()
    {
        var csharp = @"
using System.Linq;
public class Test
{
    public int[] Double(int[] numbers)
    {
        return numbers.Select(x => x * 2).ToArray();
    }
}";
        var output = ConvertAndRoundTrip(csharp);

        // Should produce a valid Select call with a lambda
        Assert.Contains("Select(", output);
        Assert.Contains("ToArray()", output);
    }

    [Fact]
    public void RoundTrip_Lambda_LinqChain_ProducesValidCSharp()
    {
        var csharp = @"
using System.Linq;
public class Test
{
    public int[] FilterAndDouble(int[] numbers)
    {
        return numbers.Where(x => x > 0).Select(x => x * 2).ToArray();
    }
}";
        var output = ConvertAndRoundTrip(csharp);

        // Should produce valid chained calls
        Assert.Contains("Where(", output);
        Assert.Contains("Select(", output);
        Assert.Contains("ToArray()", output);
    }

    #endregion

    #region Ternary Expression Round-Trip Tests

    [Fact]
    public void RoundTrip_SimpleTernary_Preserved()
    {
        var csharp = """
            public class Test
            {
                public int Max(int a, int b) { return a > b ? a : b; }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);
        Assert.Contains("?", output);
        Assert.Contains(":", output);
        Assert.Contains("a", output);
        Assert.Contains("b", output);
    }

    [Fact]
    public void RoundTrip_TernaryChain_Preserved()
    {
        var csharp = """
            public class Test
            {
                public int Classify(int score)
                {
                    return score >= 90 ? 4 : score >= 80 ? 3 : score >= 70 ? 2 : 1;
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);
        Assert.Contains("score >= 90", output);
        Assert.Contains("? 4 :", output);
        Assert.Contains("? 3 :", output);
    }

    [Fact]
    public void RoundTrip_TernaryInAssignment_Preserved()
    {
        var csharp = """
            public class Test
            {
                public void M(bool flag)
                {
                    var x = flag ? "yes" : "no";
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);
        Assert.Contains("flag ? \"yes\" : \"no\"", output);
    }

    [Fact]
    public void RoundTrip_TernaryInBaseCall_Preserved()
    {
        var csharp = """
            using System;
            public class MyException : Exception
            {
                public MyException(bool critical, string msg)
                    : base(critical ? "CRITICAL: " + msg : msg) { }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);
        Assert.Contains("base(", output);
        Assert.Contains("critical ?", output);
    }

    [Fact]
    public void RoundTrip_TernaryInMethodArgument_Preserved()
    {
        var csharp = """
            using System;
            public class Test
            {
                public void M(int x)
                {
                    Console.WriteLine(x > 0 ? "positive" : "non-positive");
                }
            }
            """;

        var output = ConvertAndRoundTrip(csharp);
        Assert.Contains("? \"positive\" : \"non-positive\"", output);
    }

    [Fact]
    public void CalorParser_TernaryLispSyntax_ParsesCorrectly()
    {
        var calor = """
            §M{m001:TestModule}
              §CL{c001:Test}
                §MT{m001:Max:pub}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §R (? (> a b) a b)
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calor, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var csharpEmitter = new CSharpEmitter();
        var output = csharpEmitter.Emit(module);
        Assert.Contains("? a : b", output);
    }

    #endregion

    #region Fix: Lock/Checked/For body ordering

    [Fact]
    public void Converter_LockStatement_CommentBeforeBody()
    {
        var csharp = @"
public class Test
{
    private readonly object _sync = new object();
    public void M()
    {
        lock (_sync)
        {
            var x = 1;
        }
    }
}";
        var calor = ConvertToCalor(csharp);

        // The lock comment should appear before the body statement
        var commentIdx = calor.IndexOf("lock(");
        var bindIdx = calor.IndexOf("§B{", commentIdx > 0 ? commentIdx : 0);
        Assert.True(commentIdx >= 0, "Lock comment not found");
        Assert.True(bindIdx > commentIdx, "Lock comment should appear before body statements");
    }

    [Fact]
    public void Converter_CheckedStatement_CommentBeforeBody()
    {
        var csharp = @"
public class Test
{
    public int M(int a, int b)
    {
        checked
        {
            var result = a + b;
            return result;
        }
    }
}";
        var calor = ConvertToCalor(csharp);

        // The checked comment should appear before the body
        var commentIdx = calor.IndexOf("checked");
        var bindIdx = calor.IndexOf("§B{", commentIdx > 0 ? commentIdx : 0);
        Assert.True(commentIdx >= 0, "Checked comment not found");
        Assert.True(bindIdx > commentIdx, "Checked comment should appear before body statements");
    }

    [Fact]
    public void Converter_NonStandardForLoop_InitializersBeforeWhile()
    {
        // Non-standard for loop with multiple variables
        var csharp = @"
public class Test
{
    public void M()
    {
        for (int i = 0, j = 10; i < j; i++)
        {
            var x = i;
        }
    }
}";
        var calor = ConvertToCalor(csharp);

        // The initializer binds should appear before the while loop
        var bindIdx = calor.IndexOf("§B{");
        var whileIdx = calor.IndexOf("§WH{");
        Assert.True(bindIdx >= 0, "Initializer bind not found");
        Assert.True(whileIdx > bindIdx, "Initializer binds should appear before while loop");
    }

    [Fact]
    public void Converter_ForLoopWithExpressionInit_InitializerBeforeWhile()
    {
        // For loop with expression initializer (no declaration)
        var csharp = @"
public class Test
{
    private int x;
    public void M()
    {
        for (x = 0; x < 10; x++)
        {
            var y = x;
        }
    }
}";
        var calor = ConvertToCalor(csharp);

        // The assignment should appear before the while loop
        var assignIdx = calor.IndexOf("§ASSIGN");
        // For non-standard for loops it falls back to while
        var whileIdx = calor.IndexOf("§WH{");
        Assert.True(whileIdx >= 0, "While loop not found (expected for non-standard for-loop fallback)");
        Assert.True(assignIdx >= 0, "Initializer assignment not found");
        Assert.True(assignIdx < whileIdx, "Initializer assignment should appear before while loop");
    }

    #endregion
}
