using Calor.Compiler.Ast;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class EffectInferenceTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Converter effect inference tests

    [Fact]
    public void Converter_ConsoleWriteLine_InfersCwEffect()
    {
        var csharp = """
            public class Service
            {
                public void Greet(string name)
                {
                    Console.WriteLine("Hello, " + name);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.NotNull(method.Effects);
        Assert.True(method.Effects!.Effects.ContainsKey("io"));
        Assert.Equal("console_write", method.Effects.Effects["io"]);
    }

    [Fact]
    public void Converter_ThrowStatement_InfersThrowEffect()
    {
        var csharp = """
            public class Service
            {
                public void Validate(string input)
                {
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.NotNull(method.Effects);
        Assert.True(method.Effects!.Effects.ContainsKey("exception"));
        Assert.Equal("intentional", method.Effects.Effects["exception"]);
    }

    [Fact]
    public void Converter_PureMethod_NoEffects()
    {
        var csharp = """
            public class MathUtil
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.Null(method.Effects);
    }

    [Fact]
    public void Converter_MultipleEffects_InfersAll()
    {
        var csharp = """
            using System;
            public class Service
            {
                public void Process(string input)
                {
                    Console.WriteLine("Processing: " + input);
                    if (input == null)
                        throw new ArgumentNullException(nameof(input));
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.NotNull(method.Effects);
        Assert.True(method.Effects!.Effects.ContainsKey("io"));
        Assert.True(method.Effects.Effects.ContainsKey("exception"));
    }

    [Fact]
    public void Converter_MultipleIOEffects_InfersAllValues()
    {
        var csharp = """
            using System.IO;
            public class FileLogger
            {
                public void Log(string message)
                {
                    Console.WriteLine(message);
                    File.WriteAllText("log.txt", message);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.NotNull(method.Effects);
        Assert.True(method.Effects!.Effects.ContainsKey("io"));
        // Both console_write and filesystem_write should be present (comma-separated)
        var ioValue = method.Effects.Effects["io"];
        Assert.Contains("console_write", ioValue);
        Assert.Contains("filesystem_write", ioValue);
    }

    [Fact]
    public void CalorEmitter_MultiIOEffects_EmitsAllCodes()
    {
        var effects = new EffectsNode(
            new TextSpan(0, 0, 0, 0),
            new Dictionary<string, string> { ["io"] = "console_write,filesystem_write" });

        var func = new FunctionNode(
            new TextSpan(0, 0, 0, 0),
            "f1", "Log",
            Visibility.Public,
            Array.Empty<ParameterNode>(),
            output: null,
            effects: effects,
            body: Array.Empty<StatementNode>(),
            attributes: new AttributeCollection());

        var module = new ModuleNode(
            new TextSpan(0, 0, 0, 0), "m1", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            Array.Empty<ClassDefinitionNode>(),
            Array.Empty<EnumDefinitionNode>(),
            Array.Empty<DelegateDefinitionNode>(),
            new[] { func },
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        // Should emit both compact codes
        Assert.Contains("cw", output);
        Assert.Contains("fs:w", output);
    }

    #endregion

    #region CalorEmitter effect emission tests

    [Fact]
    public void CalorEmitter_Effects_EmitsETag()
    {
        var effects = new EffectsNode(
            new TextSpan(0, 0, 0, 0),
            new Dictionary<string, string> { ["io"] = "console_write" });

        var func = new FunctionNode(
            new TextSpan(0, 0, 0, 0),
            "f1", "Main",
            Visibility.Public,
            Array.Empty<ParameterNode>(),
            output: null,
            effects: effects,
            body: Array.Empty<StatementNode>(),
            attributes: new AttributeCollection());

        var module = new ModuleNode(
            new TextSpan(0, 0, 0, 0), "m1", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            Array.Empty<ClassDefinitionNode>(),
            Array.Empty<EnumDefinitionNode>(),
            Array.Empty<DelegateDefinitionNode>(),
            new[] { func },
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("§E{cw}", output);
    }

    [Fact]
    public void CalorEmitter_NoEffects_NoETag()
    {
        var func = new FunctionNode(
            new TextSpan(0, 0, 0, 0),
            "f1", "Main",
            Visibility.Public,
            Array.Empty<ParameterNode>(),
            output: null,
            effects: null,
            body: Array.Empty<StatementNode>(),
            attributes: new AttributeCollection());

        var module = new ModuleNode(
            new TextSpan(0, 0, 0, 0), "m1", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            Array.Empty<ClassDefinitionNode>(),
            Array.Empty<EnumDefinitionNode>(),
            Array.Empty<DelegateDefinitionNode>(),
            new[] { func },
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null);

        var emitter = new CalorEmitter();
        var output = emitter.Emit(module);

        Assert.DoesNotContain("§E{", output);
    }

    #endregion

    #region Roundtrip test

    [Fact]
    public void Roundtrip_EffectAnnotation_Preserved()
    {
        var csharp = """
            public class App
            {
                public void Run()
                {
                    Console.WriteLine("hello");
                }
            }
            """;

        // C# → Calor
        var conversionResult = _converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.Contains("§E{cw}", conversionResult.CalorSource!);

        // Calor → parse → check AST
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    #endregion
}
