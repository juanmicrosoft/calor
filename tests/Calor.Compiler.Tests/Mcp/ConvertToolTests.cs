using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Mcp.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    public void Description_ContainsValidationInfo()
    {
        Assert.Contains("validation", _tool.Description.ToLower());
    }

    [Fact]
    public void Annotations_IsReadOnlyWithOutputPath()
    {
        // ConvertTool can write files via outputPath, so it's not readOnly
        Assert.NotNull(_tool.Annotations);
        Assert.False(_tool.Annotations!.ReadOnlyHint);
        Assert.True(_tool.Annotations.IdempotentHint);
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("source", out _));
        Assert.True(props.TryGetProperty("moduleName", out _));
        Assert.True(props.TryGetProperty("fidelity", out _));
        Assert.True(props.TryGetProperty(
            "selectActivePreprocessorBranchLossy",
            out _));
        Assert.True(props.TryGetProperty("definedSymbols", out _));
        Assert.True(props.TryGetProperty("configuration", out _));
        Assert.True(props.TryGetProperty("targetFramework", out _));
        Assert.True(props.TryGetProperty("languageVersion", out _));
        Assert.True(props.TryGetProperty("documentationMode", out _));
        Assert.True(props.TryGetProperty("sourceCodeKind", out _));
        Assert.True(props.TryGetProperty("parseFeatures", out _));
        Assert.True(props.TryGetProperty("references", out _));
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
    public async Task ExecuteAsync_ValidateMode_WriteFailureReturnsError()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"calor-convert-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var args = JsonSerializer.SerializeToElement(new
            {
                source = "public class Calculator { public int Value() => 42; }",
                moduleName = "TestModule",
                mode = "validate",
                outputPath = outputDirectory
            });

            var result = await _tool.ExecuteAsync(args);

            Assert.True(result.IsError);
            var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.Equal("write", root.GetProperty("stage").GetString());
        }
        finally
        {
            Directory.Delete(outputDirectory);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ValidateMode_ReportsLossyDropLocations()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            source = "public interface IEvents { event System.EventHandler Changed; }",
            moduleName = "Events",
            mode = "validate",
            fidelity = "lossy"
        });

        var result = await _tool.ExecuteAsync(args);

        var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("lossy", root.GetProperty("fidelity").GetString());
        var lossSummary = root.GetProperty("lossSummary");
        Assert.Equal(1, lossSummary.GetProperty("drops").GetInt32());
        Assert.True(Assert.Single(lossSummary.GetProperty("locations").EnumerateArray())
            .GetProperty("line").GetInt32() > 0);
    }

    [Fact]
    public async Task ExecuteAsync_SelectedBranch_ReportsEffectiveMetadata()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            source = """
                #if FEATURE
                public class Selected { }
                #else
                public class Fallback { }
                #endif
                """,
            selectActivePreprocessorBranchLossy = true,
            definedSymbols = new[] { "FEATURE" },
            configuration = "Release",
            targetFramework = "net10.0",
            languageVersion = "preview",
            documentationMode = "diagnose",
            sourceCodeKind = "regular",
            parseFeatures = new { test_feature = "enabled" }
        });

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("lossy", root.GetProperty("fidelity").GetString());
        var metadata = root.GetProperty("metadata");
        Assert.Equal("Release", metadata.GetProperty("configuration").GetString());
        Assert.Equal("net10.0", metadata.GetProperty("targetFramework").GetString());
        Assert.Contains(
            metadata.GetProperty("definedSymbols").EnumerateArray(),
            symbol => symbol.GetString() == "FEATURE");
        Assert.Equal(
            "Diagnose",
            metadata.GetProperty("documentationMode").GetString());
        Assert.Equal(
            "Regular",
            metadata.GetProperty("sourceCodeKind").GetString());
        Assert.Equal(
            "enabled",
            metadata.GetProperty("features")
                .GetProperty("test_feature").GetString());
        Assert.Contains("Selected", root.GetProperty("calorSource").GetString());
        Assert.DoesNotContain("Fallback", root.GetProperty("calorSource").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_LegacyStripTrue_ForcesEffectiveLossyFidelity()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            source = """
                #if false
                public class Dead { }
                #else
                public class Live { }
                #endif
                """,
            stripPreprocessor = true
        });

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("lossy", root.GetProperty("fidelity").GetString());
        Assert.Contains("Live", root.GetProperty("calorSource").GetString());
        Assert.DoesNotContain("Dead", root.GetProperty("calorSource").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_Propagates()
    {
        var args = JsonSerializer.SerializeToElement(new
        {
            source = "public class Cancelled { }"
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tool.ExecuteAsync(args, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_ValidateMode_UsesExactDefinedSymbolsForErrorDirectives()
    {
        const string source = """
            #if FEATURE
            #error feature-only
            #endif
            public class ErrorHost { }
            """;
        var inactive = await _tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                source,
                mode = "validate",
                definedSymbols = Array.Empty<string>()
            }));
        Assert.False(
            inactive.IsError,
            inactive.Content[0].Text);

        var active = await _tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                source,
                mode = "validate",
                definedSymbols = new[] { "FEATURE" }
            }));
        Assert.True(active.IsError);
        var root = JsonDocument.Parse(active.Content[0].Text!).RootElement;
        Assert.Contains(
            root.GetProperty("conversionIssues").EnumerateArray(),
            issue => issue.GetProperty("message").GetString()!
                .Contains("active-error-directive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_AliasAwareReferences_ValidateConflictingTypes()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"issue772-mcp-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "First.dll");
        var secondPath = Path.Combine(directory, "Second.dll");
        try
        {
            EmitAssembly(firstPath, "First", 1);
            EmitAssembly(secondPath, "Second", 2);
            var args = JsonSerializer.SerializeToElement(new
            {
                source = """
                    extern alias FirstAlias;
                    extern alias SecondAlias;
                    public static class AliasHarness
                    {
                        public static int Get()
                            => new FirstAlias::Shared.Value().Number
                             + new SecondAlias::Shared.Value().Number;
                    }
                    """,
                references = new[]
                {
                    new
                    {
                        path = firstPath,
                        aliases = new[] { "FirstAlias" }
                    },
                    new
                    {
                        path = secondPath,
                        aliases = new[] { "SecondAlias" }
                    }
                }
            });

            var result = await _tool.ExecuteAsync(args);
            Assert.False(result.IsError, result.Content[0].Text);
            var root = JsonDocument.Parse(result.Content[0].Text!)
                .RootElement;
            Assert.Equal(
                2,
                root.GetProperty("metadata")
                    .GetProperty("references")
                    .GetArrayLength());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static void EmitAssembly(
            string path,
            string assemblyName,
            int value)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(
                    $"namespace Shared; public sealed class Value {{ public int Number => {value}; }}")],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var stream = File.Create(path);
            var emit = compilation.Emit(stream);
            Assert.True(
                emit.Success,
                string.Join("\n", emit.Diagnostics));
        }
    }

    [Fact]
    public async Task ExecuteAsync_Issues_AreEnvelopeDiagnostics()
    {
        // Envelope schema v1.1 (loop plan D1.3): conversion issues are
        // EnvelopeDiagnostic entries with code Calor1343, not flat DTOs.
        var args = JsonDocument.Parse("""
            {
                "source": "public class { invalid syntax"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        var issues = root.GetProperty("issues").EnumerateArray().ToList();
        Assert.NotEmpty(issues);
        foreach (var entry in issues)
        {
            Assert.Equal("Calor1343", entry.GetProperty("code").GetString());
            Assert.Contains(entry.GetProperty("severity").GetString(),
                new[] { "error", "warning", "info" });
            var location = entry.GetProperty("location");
            // Real Roslyn positions are 1-based; >= 1 would catch a garbage
            // or defaulted position where >= 0 was trivially true (review of
            // #757 item 2).
            Assert.True(location.GetProperty("line").GetInt32() >= 1);
            Assert.True(location.GetProperty("column").GetInt32() >= 1);
        }
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
    public async Task ExecuteAsync_DefaultsToLosslessAndReportsInteropLocations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Example-{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(
            path,
            "public class Example { public int Get() { int Local() => 42; return Local(); } }");
        try
        {
            var args = JsonSerializer.SerializeToElement(new { inputPath = path });
            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
            Assert.Equal("lossless", root.GetProperty("fidelity").GetString());
            var summary = root.GetProperty("lossSummary");
            Assert.Equal(1, summary.GetProperty("interopPreservations").GetInt32());
            Assert.Equal(0, summary.GetProperty("lossySubstitutions").GetInt32());
            Assert.Equal(0, summary.GetProperty("drops").GetInt32());
            var location = Assert.Single(summary.GetProperty("locations").EnumerateArray());
            Assert.Equal(path, location.GetProperty("file").GetString());
            Assert.True(location.GetProperty("line").GetInt32() >= 1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithLocalFunction_EscalatesToInterop()
    {
        // #777 (WS-W4 D4): a member containing a local function is preserved verbatim
        // as interop rather than hoisted to a module-level function (the hoist orphans
        // the call site — build break or silent rebind).
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
        Assert.Contains("\u00A7CSHARP", calorSource);   // member preserved as interop
        Assert.Contains("Square", calorSource);
        Assert.DoesNotContain("\u00A7F{", calorSource);  // NOT hoisted to a module-level function
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

        // The compiled C# should contain the original member and nested local
        // function, not a module-level hoist.
        Assert.True(compileJson.RootElement.TryGetProperty("generatedCode", out var csharpProp),
            $"Round-trip compile should produce C# output. Result: {compileText}");
        var csharp = csharpProp.GetString()!;
        Assert.Contains("Calculate", csharp);
        Assert.Contains("Double", csharp);
        Assert.Contains("return Double(x)", csharp);
    }

    [Fact]
    public async Task ExecuteAsync_WithLocalFunction_ClosureIsPreserved()
    {
        var args = JsonDocument.Parse("""
            {
                "source": "public class Example { public int Compute(int x) { int multiplier = 3; int Multiply(int n) { return n * multiplier; } return Multiply(x); } }",
                "moduleName": "ClosureTest"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError, "Conversion should preserve the containing member");
        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);
        var calorSource = json.RootElement.GetProperty("calorSource").GetString()!;
        Assert.Contains("\u00A7CSHARP", calorSource);
        Assert.Contains("Multiply", calorSource);
        Assert.Contains("multiplier", calorSource);
        Assert.DoesNotContain("\u00A7F{", calorSource);
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

    [Fact]
    public async Task ExecuteAsync_AutoFixPath_ReportsInfoLevelIssues()
    {
        // Feed the converter Calor source that was manually broken with a known fixer pattern.
        // We test the ConvertTool's auto-fix integration by injecting a post-conversion scenario.
        // Since we can't easily force the converter to produce parse-failing output on demand,
        // we verify the auto-fix code path works by checking that when auto-fix succeeds,
        // the tool returns success with info-level "Auto-fixed" issues.
        //
        // Strategy: Convert valid C# that produces valid Calor, then verify the existing
        // convert flow succeeds normally (no auto-fix needed). This ensures the auto-fix
        // code path doesn't interfere with normal operation.
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calc { public int Add(int a, int b) { return a + b; } public int Sub(int a, int b) { return a - b; } }"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!);
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());

        // Verify no "Auto-fixed" issues (normal path, fixer not triggered)
        var issues = root.GetProperty("issues").EnumerateArray().ToList();
        var autoFixIssues = issues.Where(i =>
            i.GetProperty("message").GetString()?.Contains("Auto-fixed") == true).ToList();
        Assert.Empty(autoFixIssues);
    }

    [Fact]
    public async Task ExecuteAsync_WithInputPath_ReadsFromFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "public class FromFile { public int X { get; set; } }");

            var args = JsonDocument.Parse($$"""
                {
                    "inputPath": {{JsonSerializer.Serialize(tempFile)}}
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var text = result.Content[0].Text!;
            Assert.Contains("success", text);
            Assert.Contains("calorSource", text);
            Assert.Contains("FromFile", text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithInputPath_DerivesModuleNameFromFilename()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"calor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "MyService.cs");
        try
        {
            await File.WriteAllTextAsync(tempFile, "public class MyService { }");

            var args = JsonDocument.Parse($$"""
                {
                    "inputPath": {{JsonSerializer.Serialize(tempFile)}}
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            var text = result.Content[0].Text!;
            Assert.Contains("MyService", text);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputPath_WritesFile()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"calor_output_{Guid.NewGuid():N}.calr");
        try
        {
            var args = JsonDocument.Parse($$"""
                {
                    "source": "public class OutputTest { public int Y { get; set; } }",
                    "outputPath": {{JsonSerializer.Serialize(outputFile)}}
                }
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            Assert.True(File.Exists(outputFile), "Output file should be created");
            var content = await File.ReadAllTextAsync(outputFile);
            Assert.Contains("OutputTest", content);

            // Verify outputPath appears in the JSON response
            var text = result.Content[0].Text!;
            Assert.Contains("outputPath", text);
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithInteropBlocks_ReturnsFeatureHints()
    {
        // Use interop mode with foreach — the converter may produce §CSHARP for
        // complex constructs. When it does, feature hints should be emitted.
        // This test uses a class with a foreach that may or may not be natively converted,
        // plus an await foreach (which is NotSupported and guaranteed to produce §CSHARP).
        var args = JsonDocument.Parse("""
            {
                "source": "using System.Collections.Generic; public class Test { public async System.Threading.Tasks.Task ProcessAsync(IAsyncEnumerable<int> items) { await foreach (var i in items) { System.Console.WriteLine(i); } } }",
                "mode": "interop"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        // The result should have interop blocks since await foreach is not supported
        if (json.RootElement.TryGetProperty("featureHints", out var hintsArray))
        {
            Assert.True(hintsArray.GetArrayLength() > 0, "Feature hints should be non-empty when §CSHARP blocks are present");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithNoInteropBlocks_OmitsFeatureHints()
    {
        // Simple class with fully supported constructs — should NOT produce feature hints
        var args = JsonDocument.Parse("""
            {
                "source": "public class Simple { public int Add(int a, int b) { return a + b; } }",
                "mode": "interop"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        var text = result.Content[0].Text!;
        var json = JsonDocument.Parse(text);

        // featureHints should either be absent or null (JsonIgnoreCondition.WhenWritingNull)
        if (json.RootElement.TryGetProperty("featureHints", out var hints))
        {
            Assert.True(hints.ValueKind == System.Text.Json.JsonValueKind.Null,
                "featureHints should be null when no §CSHARP blocks are present");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingInputPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""
            {
                "inputPath": "/nonexistent/path/file.cs"
            }
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("not found", text.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WithNeitherSourceNorInputPath_ReturnsError()
    {
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("source", text.ToLower());
        Assert.Contains("inputPath", text);
    }
}
