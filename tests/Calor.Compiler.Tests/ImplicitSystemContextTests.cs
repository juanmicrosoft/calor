using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests;

public sealed class ImplicitSystemContextTests(ITestOutputHelper output)
{
    public static string Case081 => File.ReadAllText(Path.Combine(
        CliTestHarness.FindRepoRoot(),
        "tests/TestData/CSharpImport/Level5_Advanced/081_AsyncMethod.cs"));

    [Theory]
    [InlineData("verify")]
    [InlineData("fix")]
    public void CliVerificationAndRepair_HonorTheStandaloneOptOut(string command)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-system-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "081.calr");
            File.WriteAllText(input, new CSharpToCalorConverter().Convert(Case081, "081.cs").CalorSource);
            var extra = command == "fix" ? new[] { "--apply" } : Array.Empty<string>();
            var project = CliTestHarness.RunCli(directory, [command, input, "--format", "json", .. extra]);
            Assert.Equal(0, project.ExitCode);
            var standalone = CliTestHarness.RunCli(directory,
                [command, input, "--format", "json", "--no-implicit-system", .. extra]);
            Assert.NotEqual(0, standalone.ExitCode);
            Assert.Contains("CS0103", standalone.StdOut);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Original081_AndExactPreservedOutput_UseTheSameProfile(bool implicitSystem)
    {
        var source = Case081;
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            IncludeImplicitSystemUsing = implicitSystem
        }).Convert(source, "081_AsyncMethod.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(implicitSystem, conversion.Metadata.IncludeImplicitSystemUsing);
        Assert.Equal(1, conversion.InteropPreservationCount);
        var compiled = Program.Compile(conversion.CalorSource!, null, new CompilationOptions
        {
            IncludeImplicitSystemUsing = implicitSystem
        });
        Assert.Equal(source, compiled.GeneratedCode);
        var context = new GeneratedCSharpCompilationContext
        {
            IncludeImplicitSystemUsing = implicitSystem
        };
        var original = GeneratedCSharpCompiler.Validate([new(source, "original081.cs")], context);
        var generated = GeneratedCSharpCompiler.Validate([new(compiled.GeneratedCode, "generated081.cs")], context);
        Assert.Equal(implicitSystem, original.CompilationSuccess);
        Assert.Equal(implicitSystem, generated.CompilationSuccess);
        Assert.Equal(!implicitSystem, compiled.HasErrors);
        Assert.Equal(original.CompilationErrors.Select(error => error.Id),
            generated.CompilationErrors.Select(error => error.Id));
        if (!implicitSystem)
            Assert.Equal("CS0103", Assert.Single(original.CompilationErrors).Id);
        output.WriteLine($"081 {(implicitSystem ? "System-project" : "standalone")}: " +
            $"original={original.CompilationSuccess}, generated={generated.CompilationSuccess}, " +
            $"preserved={conversion.InteropPreservationCount}, errors={string.Join(",", original.CompilationErrors.Select(error => error.Id))}");
    }

    [Fact]
    public void LowLevelDefaults_RemainStandalone_AndCliOptionsSelectProjectContext()
    {
        Assert.False(new GeneratedCSharpCompilationContext().IncludeImplicitSystemUsing);
        Assert.False(new CompilationOptions().IncludeImplicitSystemUsing);
        Assert.False(new ConversionOptions().IncludeImplicitSystemUsing);
        Assert.True(ConvertCommand.BuildCSharpToCalorOptions(false, false, false, false, false)
            .IncludeImplicitSystemUsing);
        Assert.False(ConvertCommand.BuildCSharpToCalorOptions(false, false, false, false, false,
            includeImplicitSystemUsing: false).IncludeImplicitSystemUsing);
    }

    [Fact]
    public void SystemOnly_DoesNotImportSdkNamespaces()
    {
        const string source = "public class Host { public Task Run() => Task.CompletedTask; }";
        Assert.False(GeneratedCSharpCompiler.Validate([new(source, "host.cs")],
            new GeneratedCSharpCompilationContext { IncludeImplicitSystemUsing = true }).CompilationSuccess);
        Assert.True(GeneratedCSharpCompiler.Validate([new(source, "host.cs")],
            new GeneratedCSharpCompilationContext { IncludeImplicitGlobalUsings = true }).CompilationSuccess);
    }

    [Theory]
    [InlineData("using Rival;", false)]
    [InlineData("using Rival;\nusing Action = System.Action;", true)]
    [InlineData("using Rival;", true, "global::System.Action")]
    public void AmbiguityIsReported_AndAliasesOrQualificationResolveIt(
        string imports, bool accepted, string type = "Action")
    {
        var source = $"{imports}\nnamespace Rival {{ public class Action {{}} }}\n" +
            $"public class Host {{ public {type} Value {{ get; set; }} }}";
        var standalone = GeneratedCSharpCompiler.Validate(source);
        Assert.True(standalone.CompilationSuccess, string.Join("\n", standalone.FormattedCompilationErrors));
        var project = GeneratedCSharpCompiler.Validate([new(source, "collision.cs")],
            new GeneratedCSharpCompilationContext { IncludeImplicitSystemUsing = true });
        Assert.Equal(accepted, project.CompilationSuccess);
        if (!accepted)
            Assert.Contains(project.CompilationErrors, error => error.Id == "CS0104");
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            IncludeImplicitSystemUsing = true
        }).Convert(source, "collision.cs");
        Assert.Equal(accepted, conversion.Success);
        if (!accepted)
            Assert.Contains(conversion.Issues, issue => issue.Message.Contains("CS0104"));
    }

    [Fact]
    public void OriginalSemanticModel_UsesSystemForAuthoritativeLocalTypeInference()
    {
        const string source = """
            public static class Host
            {
                public static int Read()
                {
                    var value = Math.Abs(-1);
                    return value;
                }
            }
            """;
        var project = new CSharpToCalorConverter(new ConversionOptions
        {
            IncludeImplicitSystemUsing = true
        }).Convert(source, "host.cs");
        Assert.True(project.Success, string.Join("\n", project.Issues));
        Assert.Equal(0, project.InteropPreservationCount);
        Assert.Contains("int:value", project.CalorSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptValidation_UsesTheSameSystemProfile(bool implicitSystem)
    {
        var result = GeneratedCSharpCompiler.Validate(
            [new("Console.WriteLine(\"hello\");", "input.csx")],
            new GeneratedCSharpCompilationContext
            {
                IncludeImplicitSystemUsing = implicitSystem,
                SourceCodeKind = SourceCodeKind.Script
            });
        Assert.Equal(implicitSystem, result.CompilationSuccess);
        if (!implicitSystem)
            Assert.Contains(result.CompilationErrors, error => error.Id == "CS0103");
    }

    [Fact]
    public void ConvertibilityAnalysis_UsesOneProfileForOriginalAndGeneratedSource()
    {
        var analyzer = new Calor.Compiler.Analysis.ConvertibilityAnalyzer();
        Assert.False(analyzer.Analyze(Case081, "081.cs").CompilationSucceeded);
        Assert.True(analyzer.Analyze(Case081, "081.cs", includeImplicitSystemUsing: true).CompilationSucceeded);
    }

    [Fact]
    public void NativeSource_UsesSystemContextWithoutChangingEmission()
    {
        const string source = "module Demo\n  func Identity (Exception:value) → Exception\n    !\n    return value\n";
        var standalone = Program.Compile(source);
        var project = Program.Compile(source, null, new CompilationOptions
        {
            IncludeImplicitSystemUsing = true
        });
        Assert.False(project.HasErrors, string.Join("\n", project.Diagnostics));
        Assert.False(standalone.HasErrors, string.Join("\n", standalone.Diagnostics));
        Assert.Empty(project.Ast!.InteropBlocks);
        Assert.Equal(standalone.GeneratedCode, project.GeneratedCode);
        Assert.DoesNotContain("raw ", source);
    }

    [Fact]
    public void ContextRetainsNullableSymbolsAndAdditionalSources()
    {
        const string source = """
            #if FEATURE
            public class Host { public Action Value => Other.Value; }
            #else
            #error missing compilation symbols
            #endif
            """;
        var context = new GeneratedCSharpCompilationContext
        {
            IncludeImplicitSystemUsing = true,
            PreprocessorSymbols = ["FEATURE"],
            NullableContextOptions = NullableContextOptions.Enable,
            TreatWarningsAsErrors = true,
            AdditionalSources = [new("public static class Other { public static Action Value => () => {}; }", "other.cs")]
        };
        Assert.True(GeneratedCSharpCompiler.Validate([new(source, "host.cs")], context).CompilationSuccess);
        var invalid = GeneratedCSharpCompiler.Validate(
            [new("public class Host { public Action Value => null; }", "nullable.cs")], context);
        Assert.Contains(invalid.CompilationErrors, error => error.Id == "CS8603");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpConvertAndCompile_UseTheSameSelectedContext(bool implicitSystem)
    {
        var converted = await new ConvertTool().ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            source = Case081, implicitSystem, mode = "validate"
        }));
        using var document = JsonDocument.Parse(converted.Content[0].Text!);
        Assert.Equal(implicitSystem, document.RootElement.GetProperty("success").GetBoolean());
        var conversion = new CSharpToCalorConverter().Convert(Case081, "081.cs");
        var compiled = await new CompileTool().ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            source = conversion.CalorSource, options = new { implicitSystem, autoFix = false }
        }));
        using var result = JsonDocument.Parse(compiled.Content[0].Text!);
        Assert.Equal(implicitSystem, result.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void McpCompileSchema_AdvertisesTheNestedRuntimeContextOption()
    {
        var properties = new CompileTool().GetInputSchema().GetProperty("properties");
        Assert.False(properties.TryGetProperty("implicitSystem", out _));
        var option = properties.GetProperty("options").GetProperty("properties").GetProperty("implicitSystem");
        Assert.Equal("boolean", option.GetProperty("type").GetString());
        Assert.True(option.GetProperty("default").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CliConversionCompilationAndWarmCache_UseTheSelectedContext(bool implicitSystem)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-system-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "081.cs");
            var converted = Path.Combine(directory, "081.calr");
            File.WriteAllText(input, Case081);
            var options = implicitSystem ? Array.Empty<string>() : new[] { "--no-implicit-system" };
            var conversion = CliTestHarness.RunCli(directory,
                ["convert", input, "-o", converted, "--format", "json", .. options]);
            Assert.Equal(implicitSystem ? 0 : 2, conversion.ExitCode);
            using var json = JsonDocument.Parse(conversion.StdOut);
            Assert.Equal(implicitSystem, json.RootElement.GetProperty("data").GetProperty("implicitSystem").GetBoolean());
            if (implicitSystem)
            {
                // 0.30: a file that is exactly one named namespace with local usings converts
                // through the native scoped-import projection by default, so nothing is preserved.
                Assert.Equal(0, json.RootElement.GetProperty("data").GetProperty("interopPreservationCount").GetInt32());
                Assert.Equal(0, json.RootElement.GetProperty("data").GetProperty("lossCount").GetInt32());
            }
            var reverse = CliTestHarness.RunCli(directory,
                ["convert", converted, "-o", Path.Combine(directory, "reverse.cs"), "--format", "json", .. options]);
            Assert.Equal(implicitSystem ? 0 : 1, reverse.ExitCode);
            using var reverseJson = JsonDocument.Parse(reverse.StdOut);
            Assert.Equal(implicitSystem, reverseJson.RootElement.GetProperty("data").GetProperty("implicitSystem").GetBoolean());
            for (var run = 0; run < 2; run++)
            {
                var compilation = CliTestHarness.RunCli(directory,
                    ["-i", converted, "--cache", "--format", "json", .. options]);
                Assert.Equal(implicitSystem ? 0 : 1, compilation.ExitCode);
                if (!implicitSystem)
                    Assert.Contains("CS0103", compilation.StdOut);
            }
            if (implicitSystem)
            {
                // The native output names System.Console explicitly, so it no longer depends on
                // the implicit System import: the standalone profile recompiles it (no cached
                // hit from the other profile) and succeeds instead of failing with CS0103.
                var generated = File.ReadAllText(Path.ChangeExtension(converted, ".g.cs"));
                Assert.NotEqual(Case081, generated);
                Assert.Contains("System.Console.WriteLine(\"Work done\")", generated);
                var changedProfile = CliTestHarness.RunCli(directory,
                    "-i", converted, "--cache", "--no-implicit-system", "--format", "json");
                Assert.Equal(0, changedProfile.ExitCode);
                Assert.DoesNotContain("CS0103", changedProfile.StdOut);
                Assert.Contains("1 compiled, 0 cached", changedProfile.StdOut + changedProfile.StdErr);
                Assert.True(File.Exists(Path.ChangeExtension(converted, ".g.cs")));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
