using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ExtensionMethodTests
{
    private readonly CSharpToCalorConverter _converter = new();

    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    #region Converter Tests

    [Fact]
    public void Converter_ExtensionMethod_DetectsThisModifier()
    {
        var csharp = """
            public static class StringExtensions
            {
                public static string Reverse(this string input)
                {
                    return new string(input.ToCharArray().Reverse().ToArray());
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.True(method.Parameters.Count > 0);
        Assert.True(method.Parameters[0].Modifier.HasFlag(ParameterModifier.This),
            "First parameter should have 'this' modifier for extension method");
    }

    [Fact]
    public void Converter_ExtensionMethodWithMultipleParams_OnlyFirstHasThis()
    {
        var csharp = """
            public static class EnumerableExtensions
            {
                public static T FirstOrDefault<T>(this IEnumerable<T> source, T defaultValue)
                {
                    return default;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.Equal(2, method.Parameters.Count);
        Assert.True(method.Parameters[0].Modifier.HasFlag(ParameterModifier.This));
        Assert.False(method.Parameters[1].Modifier.HasFlag(ParameterModifier.This));
    }

    [Fact]
    public void Converter_RegularMethod_NoThisModifier()
    {
        var csharp = """
            public class MyClass
            {
                public void DoSomething(string input)
                {
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.Single(method.Parameters);
        Assert.Equal(ParameterModifier.None, method.Parameters[0].Modifier);
    }

    #endregion

    #region CSharpEmitter Tests

    [Fact]
    public void CSharpEmitter_ExtensionMethod_EmitsThisKeyword()
    {
        var csharp = """
            public static class StringExtensions
            {
                public static string Reverse(this string input)
                {
                    return input;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("this string", output);
    }

    #endregion

    #region CalorEmitter Tests

    [Fact]
    public void CalorEmitter_ExtensionMethod_EmitsThisModifier()
    {
        var csharp = """
            public static class StringExtensions
            {
                public static string Reverse(this string input)
                {
                    return input;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("this", output);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_ParameterWithThisModifier_ParsesModifier()
    {
        // §I{string:input:this} — third position is semantic/modifier
        var source = """
            §M{m1:TestModule}
              §F{f1:Reverse:pub}
                §I{string:input:this}
                §O{string}
                §R input
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = Assert.Single(module.Functions);
        var param = Assert.Single(func.Parameters);
        Assert.True(param.Modifier.HasFlag(ParameterModifier.This));
    }

    #endregion

    #region E2E Roundtrip

    [Fact]
    public void E2E_ExtensionMethod_RoundtripsCorrectly()
    {
        var csharp = """
            public static class IntExtensions
            {
                public static bool IsEven(this int value)
                {
                    return value % 2 == 0;
                }
            }
            """;

        // Convert C# → Calor AST
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Emit back to C#
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        // Should contain the this keyword
        Assert.Contains("this int", output);
        Assert.Contains("IsEven", output);
    }

    #endregion

    #region FeatureSupport

    [Fact]
    public void FeatureSupport_ExtensionMethod_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("extension-method"));
    }

    #endregion
}
