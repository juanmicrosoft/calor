using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class ConvertToolTests
{
    private readonly ConvertTool _tool = new();

    [Fact]
    public void Name_ReturnsCalorConvert()
    {
        Assert.Equal("calor_convert", _tool.Name);
    }

    [Fact]
    public void Description_ContainsConvertInfo()
    {
        Assert.Contains("Convert", _tool.Description);
        Assert.Contains("C#", _tool.Description);
        Assert.Contains("Calor", _tool.Description);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("moduleName", out _));
    }

    [Fact]
    public async Task ExecuteAsync_WithSimpleClass_ReturnsCalorCode()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calculator { public int Add(int a, int b) => a + b; }",
                "moduleName": "TestModule"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
        Assert.Contains("calorSource", text);
        Assert.Contains("TestModule", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutModuleName_DerivesFromSource()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "namespace MyNamespace { public class Test { } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidCSharp_ReturnsErrors()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class { invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("issues", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingSource_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("source", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsStats()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Test { public int Value { get; set; } public void DoSomething() { } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        Assert.Contains("stats", text);
        Assert.Contains("classesConverted", text);
        Assert.Contains("methodsConverted", text);
        Assert.Contains("propertiesConverted", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithLocalFunction_HoistsToModuleLevel()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Example { public int Calculate(int x) { int Square(int n) => n * n; return Square(x); } }",
                "moduleName": "LocalFuncTest"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, $"Local function conversion should succeed");
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var calorSource = json.RootElement.GetProperty("calorSource").GetString()!;
        Assert.Contains("\u00A7F{", calorSource);  // Hoisted to module-level §F function
        Assert.Contains("Square", calorSource);
        Assert.DoesNotContain("localfunction", calorSource);
    }

    [Fact]
    public async Task ExecuteAsync_WithLocalFunction_RoundTripCompiles()
    {
        // Round-trip test: C# → Calor → C# should produce valid C# output.
        var convertArgs = JsonDocument.Parse("""
            {
                "source": "public class Math { public int Calculate(int x) { int Double(int n) { return n * 2; } return Double(x); } }",
                "moduleName": "RoundTrip"
            }
            """).RootElement;

        var convertResult = await _tool.ExecuteAsync(convertArgs);
        Assert.False(convertResult.IsError, "Conversion should succeed");

        var convertJson = JsonDocument.Parse(convertResult.Content[0].Text!);
        var calorSource = convertJson.RootElement.GetProperty("calorSource").GetString()!;

        // Now compile the Calor source back to C#
        var compileTool = new CompileTool();
        var compileArgs = JsonDocument.Parse($$"""
            {
                "source": {{JsonSerializer.Serialize(calorSource)}}
            }
            """).RootElement;

        var compileResult = await compileTool.ExecuteAsync(compileArgs);
        var compileText = compileResult.Content[0].Text!;
        var compileJson = JsonDocument.Parse(compileText);

        // The compiled C# should contain the hoisted function
        Assert.True(compileJson.RootElement.TryGetProperty("generatedCode", out var csharpProp),
            $"Round-trip compile should produce C# output. Result: {compileText}");
        var csharp = csharpProp.GetString()!;
        Assert.Contains("Double", csharp);
    }

    [Fact]
    public async Task ExecuteAsync_WithLocalFunction_ClosureNotCaptured()
    {
        // Known limitation: local functions that capture outer variables are hoisted
        // to module level, which means the captured variable is out of scope.
        // The converter still hoists the function but the round-trip compile may fail
        // because the variable reference cannot be resolved.
        var args = JsonDocument.Parse("""
            {
                "source": "public class Example { public int Compute(int x) { int multiplier = 3; int Multiply(int n) { return n * multiplier; } return Multiply(x); } }",
                "moduleName": "ClosureTest"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        // Conversion itself succeeds (the local function is hoisted)
        Assert.False(result.IsError, "Conversion should succeed even with closure");
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var calorSource = json.RootElement.GetProperty("calorSource").GetString()!;
        Assert.Contains("Multiply", calorSource);
        // Note: The hoisted function references 'multiplier' which is not in scope.
        // This is a known limitation documented in Issue #315.
    }

    [Fact]
    public async Task ExecuteAsync_WithInterface_SucceedsWithMTTags()
    {
        // Interface conversion now generates §MT tags (not §SIG) which the parser recognizes.
        var args = JsonDocument.Parse("""
            {
                "source": "public interface IService { void Process(); string GetValue(); }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
        Assert.Contains("interfacesConverted", text);
    }
}
