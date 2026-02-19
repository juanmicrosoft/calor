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
        var x = stackalloc int[10];
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
}
