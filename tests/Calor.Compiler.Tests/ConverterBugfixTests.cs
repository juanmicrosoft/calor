using Calor.Compiler.Migration;
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
        // A typed variable that is reassigned should produce §B{type:~name}
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

    #region Bug 2: Chained call with built-in should fall back to C# syntax

    [Fact]
    public void Converter_ChainedCallWithBuiltin_FallsBackToCSharpSyntax()
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

        // Should contain original C# method chain, not embedded Calor built-in syntax
        Assert.Contains("obj.ToLower().GetHashCode", calor);
        // Should NOT contain the Calor built-in form embedded in a call target
        Assert.DoesNotContain("(lower obj).GetHashCode", calor);
    }

    [Fact]
    public void Converter_DeeperChainWithMultipleBuiltins_FallsBackToCSharpSyntax()
    {
        // When multiple built-in operations are chained (e.g., ToLower().Trim()),
        // the entire chain should fall back to raw C# rather than partially converting
        var csharp = @"
public class Test
{
    public int M(string obj)
    {
        return obj.ToLower().Trim().GetHashCode();
    }
}";
        var calor = ConvertToCalor(csharp);

        // The entire chain should be preserved as raw C#
        Assert.Contains("obj.ToLower().Trim().GetHashCode", calor);
        // Should NOT contain Calor built-in forms embedded in call targets
        Assert.DoesNotContain("(lower ", calor);
        Assert.DoesNotContain("(trim ", calor);
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
        var compilationResult = Program.Compile(conversionResult.CalorSource!);

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
        var compilationResult = Program.Compile(conversionResult.CalorSource!);

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

        var compilationResult = Program.Compile(conversionResult.CalorSource!);

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

        var compilationResult = Program.Compile(conversionResult.CalorSource!);

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        // Generated C# should contain valid chained method calls
        Assert.Contains("GetHashCode()", compilationResult.GeneratedCode);
    }

    #endregion
}
