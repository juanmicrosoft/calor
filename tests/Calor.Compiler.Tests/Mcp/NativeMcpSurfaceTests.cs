using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public sealed class NativeMcpSurfaceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"calor-native-mcp-{Guid.NewGuid():N}");
    private const string Native = """
        module Native
          class Box
            ctor ()
          func Make () → Box
            ! alloc
            return new Box()
          func Use () → Box
            ! alloc
            let Box:first = Make()
            return Make()
        """;
    private const string Wide = "module Calc\n    func Add (int:x, int:y) → int\n        return x + y\n";

    public NativeMcpSurfaceTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static async Task<McpToolResult> Call(string name, object arguments, string? root = null)
    {
        var response = await new McpMessageHandler(rootDirectory: root).HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonSerializer.SerializeToElement(1),
            Method = "tools/call",
            Params = JsonSerializer.SerializeToElement(new { name, arguments })
        });
        Assert.NotNull(response);
        Assert.Null(response.Error);
        return Assert.IsType<McpToolResult>(response.Result);
    }

    private static JsonElement Output(McpToolResult result) =>
        JsonDocument.Parse(result.Content[0].Text!).RootElement;

    [Fact]
    public async Task NativeStdioRequest_UsesDocumentedNewlineDelimitedJsonTransport()
    {
        var text = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "calor_fix", arguments = new { source = Wide } }
        });
        using var reader = new StringReader(text + "\n");
        using var output = new MemoryStream();
        await new McpServer(reader, output).RunAsync();
        using var response = JsonDocument.Parse(output.ToArray());
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        using var fixedOutput = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.True(fixedOutput.RootElement.GetProperty("success").GetBoolean());
        Assert.True(fixedOutput.RootElement.GetProperty("wasModified").GetBoolean());
        Assert.Contains("newline-delimited JSON",
            File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), "docs/cli/mcp.md")));
    }

    [Theory]
    [InlineData("calor_compile")]
    [InlineData("calor_check")]
    [InlineData("calor_fix")]
    public async Task NativeRequests_PreserveConstructorsNamesAndCallTargets(string name)
    {
        var result = await Call(name, new { source = Native, filePath = Path.Combine(_directory, "Native.calr") });
        Assert.False(result.IsError, result.Content[0].Text);
        var output = Output(result);
        Assert.True(output.GetProperty("success").GetBoolean());
        if (name == "calor_fix")
        {
            Assert.Equal(Native, output.GetProperty("fixedSource").GetString());
            Assert.Equal(0, output.GetProperty("fixCount").GetInt32());
        }
    }

    [Fact]
    public async Task SafeFix_AgreesWithDiagnoseAndPreservesGeneratedCode()
    {
        var original = Program.Compile(Wide);
        Assert.False(original.HasErrors);
        var fixedResult = await Call("calor_fix", new { source = Wide, errors = new[] { "safe_diagnostics" } });
        Assert.False(fixedResult.IsError, fixedResult.Content[0].Text);
        var fixedOutput = Output(fixedResult);
        Assert.True(fixedOutput.GetProperty("wasModified").GetBoolean());
        Assert.True(fixedOutput.GetProperty("fixCount").GetInt32() > 0);
        var fixedSource = fixedOutput.GetProperty("fixedSource").GetString()!;
        Assert.Equal(original.GeneratedCode, Program.Compile(fixedSource).GeneratedCode);
        var diagnosed = Output(await Call("calor_check", new { action = "diagnose", source = Wide, apply = true }));
        Assert.Equal(diagnosed.GetProperty("fixedSource").GetString(), fixedSource);
        Assert.Equal(diagnosed.GetProperty("fixesApplied").GetInt32(), fixedOutput.GetProperty("fixCount").GetInt32());
        var again = Output(await Call("calor_fix", new { source = fixedSource }));
        Assert.False(again.GetProperty("wasModified").GetBoolean());
    }

    [Fact]
    public async Task FixFile_ReturnsRepairsWithoutWritingOrChangingSidecar()
    {
        var path = Path.Combine(_directory, "Calc.calr");
        await File.WriteAllTextAsync(path, Wide);
        var sidecarDirectory = Path.Combine(_directory, ".calor");
        Directory.CreateDirectory(sidecarDirectory);
        var sidecar = Path.Combine(sidecarDirectory, "ids.json");
        new Ids.DeclarationIdentityStore(_directory).Sync([path]);
        var sentinel = await File.ReadAllTextAsync(sidecar);
        var result = await Call("calor_fix", new { filePath = path });
        Assert.False(result.IsError, result.Content[0].Text);
        Assert.True(Output(result).GetProperty("wasModified").GetBoolean());
        Assert.Equal(Wide, await File.ReadAllTextAsync(path));
        Assert.Equal(sentinel, await File.ReadAllTextAsync(sidecar));
    }

    [Fact]
    public async Task Fix_PreservesStringsCommentsAndConverterOwnedPayload()
    {
        const string csharp = """
            public class Kept {
                ~Kept() { System.Console.WriteLine("§TH §NEW{object} §/NEW"); }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(converted.Success);
        Assert.Contains("raw ", converted.CalorSource);
        var source = converted.CalorSource + """

              // §FLD{StringBuilder:x:priv} §TH §NEW{object}
              func Text () → string
                return "§B{x:Exception} §NEW{object} §/NEW"
            """;
        var result = await Call("calor_fix", new { source });
        var output = Output(result);
        Assert.Equal(source, output.GetProperty("fixedSource").GetString());
        Assert.False(output.GetProperty("wasModified").GetBoolean());
        Assert.Equal(0, output.GetProperty("fixCount").GetInt32());
        Assert.Contains(csharp.Split('\n')[1].Trim(), output.GetProperty("fixedSource").GetString());
    }

    [Fact]
    public async Task Fix_DoesNotGuessTypesOrApplyFuzzyNameSuggestions()
    {
        const string source = "module Guess\n  func Use (int:value) → int\n    return vlaue\n";
        var result = await Call("calor_fix", new { source });
        Assert.True(result.IsError);
        var output = Output(result);
        Assert.False(output.GetProperty("success").GetBoolean());
        Assert.Equal(source, output.GetProperty("fixedSource").GetString());
        Assert.Equal(0, output.GetProperty("fixCount").GetInt32());
        Assert.NotEmpty(output.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task SafeIndentationPatch_PreservesCountedCSharpPayloadBytes()
    {
        const string csharp = """
            public class Kept {
                ~Kept() { System.Console.WriteLine("§TH §NEW{object} §/NEW"); }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(converted.Success);
        var source = RawCSharpFrame.RewriteOutsideFrames(converted.CalorSource!,
            text => Regex.Replace(text, @"(?m)^ +", match => new string(' ', match.Length * 2)));
        var before = RawCSharpFrame.ReadTokens(source).Where(t => t.Value is RawCSharpFrame)
            .Select(t => ((RawCSharpFrame)t.Value!).Code).ToArray();
        Assert.NotEmpty(before);
        var result = await Call("calor_fix", new { source });
        Assert.False(result.IsError, result.Content[0].Text);
        var output = Output(result);
        Assert.True(output.GetProperty("wasModified").GetBoolean());
        var after = RawCSharpFrame.ReadTokens(output.GetProperty("fixedSource").GetString()!)
            .Where(t => t.Value is RawCSharpFrame).Select(t => ((RawCSharpFrame)t.Value!).Code).ToArray();
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("new_object")]
    [InlineData("NEW_OBJECT")]
    [InlineData("id_conflicts")]
    public async Task RetiredRules_AreRejectedForNativeSource(string rule)
    {
        var result = await Call("calor_fix", new { source = Native, errors = new[] { rule } });
        Assert.True(result.IsError);
        Assert.DoesNotContain("fixedSource", result.Content[0].Text);
    }

    [Theory]
    [InlineData("fix")]
    [InlineData("full")]
    public async Task Migration_PreservesNativeConstructorSemantics(string phase)
    {
        var csPath = Path.Combine(_directory, "Native.cs");
        await File.WriteAllTextAsync(csPath,
            "public class Box { public Box() { } } public static class Native { public static Box Make() => new Box(); }");
        var path = Path.Combine(_directory, "Native.calr");
        if (phase == "fix")
            await File.WriteAllTextAsync(path, Native);
        var result = await Call("calor_migrate", new { projectPath = _directory, phase, autoFix = true }, _directory);
        Assert.False(result.IsError, result.Content[0].Text);
        var actual = await File.ReadAllTextAsync(path);
        Assert.Contains("new Box()", actual);
        Assert.DoesNotContain("§", actual);
        var compilation = Program.Compile(actual, path);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        if (phase == "fix")
            Assert.Equal(Native, actual);
    }

    [Fact]
    public async Task Fix_UnknownConstructorEffectsRemainErrorsWithoutChangingObjectSemantics()
    {
        const string source = "module Native\n  func Make () → object\n    ! alloc\n    return new object()\n";
        var result = await Call("calor_fix", new { source });
        Assert.True(result.IsError);
        var output = Output(result);
        Assert.Equal(source, output.GetProperty("fixedSource").GetString());
        Assert.Equal(0, output.GetProperty("fixCount").GetInt32());
        Assert.Contains(output.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "Calor0410");
    }

    [Fact]
    public async Task MigrationFix_WritesSafePatchButLeavesUnfixableFilesUnchanged()
    {
        var path = Path.Combine(_directory, "Calc.calr");
        var badPath = Path.Combine(_directory, "Bad.calr");
        await File.WriteAllTextAsync(path, Wide);
        await File.WriteAllTextAsync(badPath, "not Calor §NEW{object}");
        var result = await Call("calor_migrate", new { projectPath = _directory, phase = "fix" }, _directory);
        Assert.True(result.IsError);
        var files = Output(result).GetProperty("perFile").EnumerateArray().ToArray();
        var fixedFile = Assert.Single(files, f => f.GetProperty("path").GetString() == "Calc.calr");
        Assert.Equal("fixed", fixedFile.GetProperty("status").GetString());
        Assert.True(fixedFile.GetProperty("fixesApplied").GetInt32() > 0);
        Assert.Equal(Program.Compile(Wide).GeneratedCode, Program.Compile(await File.ReadAllTextAsync(path)).GeneratedCode);
        var failed = Assert.Single(files, f => f.GetProperty("path").GetString() == "Bad.calr");
        Assert.Equal("fix_incomplete", failed.GetProperty("status").GetString());
        Assert.Equal(0, failed.GetProperty("fixesApplied").GetInt32());
        Assert.Equal("not Calor §NEW{object}", await File.ReadAllTextAsync(badPath));
    }

    [Fact]
    public async Task Minimize_UsesCurrentTeachingTemplatesAndCompilableHelpExamples()
    {
        const string csharp = """
            public enum State { Ready }
            public class Example {
                private int value;
                public Example() { }
                public int Value { get; set; }
                public int Get() => value;
                public async System.Threading.Tasks.Task<int> GetAsync() => await System.Threading.Tasks.Task.FromResult(1);
                public event System.EventHandler Changed;
                public static Example operator +(Example a, Example b) => a;
            }
            """;
        var result = await Call("calor_analyze", new { action = "minimize", csharpCode = csharp });
        Assert.False(result.IsError, result.Content[0].Text);
        var output = Output(result);
        Assert.Contains("not replacement patches", output.GetProperty("guidance").GetString());
        var suggestions = output.GetProperty("suggestions").EnumerateArray().ToArray();
        var topics = new Dictionary<string, string>
        {
            ["enum"] = "enum_definition", ["method"] = "method_definition",
            ["async method"] = "async_method", ["field"] = "field_definition",
            ["constructor"] = "constructor", ["property"] = "property_definition",
            ["operator overload"] = "operator_overload", ["event"] = "event_definition"
        };
        Assert.Equal(topics.Keys.OrderBy(x => x), suggestions.Select(s => s.GetProperty("feature").GetString()!).OrderBy(x => x));
        foreach (var suggestion in suggestions)
        {
            var topic = suggestion.GetProperty("feature").GetString()!;
            var template = suggestion.GetProperty("calorEquivalent").GetString()!;
            Assert.Equal(HelpTool.GetSyntaxTemplate(topics[topic]), template);
            Assert.DoesNotContain("§", template);
            var help = Output(await Call("calor_help", new { query = topic }));
            var examples = help.GetProperty("examples").EnumerateArray().ToArray();
            Assert.NotEmpty(examples);
            foreach (var example in examples)
            {
                var compilation = Program.Compile(example.GetProperty("calor").GetString()!);
                Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
            }
        }
    }

    [Fact]
    public async Task Minimize_InvalidCSharpFailsRatherThanReturningHighConfidenceHints()
    {
        var result = await Call("calor_analyze", new { action = "minimize", csharpCode = "public enum {" });
        Assert.True(result.IsError);
        Assert.Contains("C# syntax errors", result.Content[0].Text);
    }

    [Fact]
    public async Task Minimize_InvalidFrameInDirectoryIsNotSilentlySkipped()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "Broken.calr"),
            "module Broken\n  raw member:destructor 99\nnot a complete payload");
        var result = await Call("calor_analyze", new { action = "minimize", directoryPath = _directory });
        Assert.True(result.IsError);
        Assert.Contains("Broken.calr", result.Content[0].Text);
    }

    [Fact]
    public async Task DocumentationNativeSourceRequests_RunThroughRegisteredTools()
    {
        foreach (var relativePath in new[] { "docs/cli/mcp.md", "website/content/cli/mcp.mdx" })
        {
            var text = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), relativePath));
            var requests = 0;
            foreach (Match fence in Regex.Matches(text, @"(?s)```json\s*\n(.*?)\n```"))
            {
                using var document = JsonDocument.Parse(fence.Groups[1].Value);
                var request = document.RootElement;
                if (!request.TryGetProperty("name", out var name)
                    || !request.TryGetProperty("arguments", out var arguments)
                    || !arguments.TryGetProperty("source", out var source)
                    || !source.GetString()!.StartsWith("module ", StringComparison.Ordinal))
                    continue;
                var result = await Call(name.GetString()!, arguments);
                Assert.False(result.IsError, result.Content[0].Text);
                Assert.True(Output(result).GetProperty("success").GetBoolean());
                requests++;
            }
            Assert.True(requests > 0, $"No native source requests checked in {relativePath}");
        }
    }

    [Fact]
    public async Task DocumentationCatalogs_MatchRegisteredToolsAndSelectors()
    {
        var response = await new McpMessageHandler().HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonSerializer.SerializeToElement(1), Method = "tools/list"
        });
        Assert.NotNull(response);
        Assert.Null(response.Error);
        var tools = JsonSerializer.SerializeToElement(response.Result, McpJsonOptions.Default)
            .GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(19, tools.Length);
        foreach (var relativePath in new[] { "docs/cli/mcp.md", "website/content/cli/mcp.mdx" })
        {
            var text = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), relativePath));
            var catalog = Regex.Matches(text, @"(?m)^\| `(calor_\w+)` \|")
                .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();
            Assert.Equal(tools.Select(t => t.GetProperty("name").GetString()!).OrderBy(x => x), catalog);
            foreach (var tool in tools)
            {
                var name = tool.GetProperty("name").GetString()!;
                var props = tool.GetProperty("inputSchema").GetProperty("properties");
                foreach (var selector in new[] { "action", "phase", "mode", "facet" })
                {
                    if (!props.TryGetProperty(selector, out var property) || !property.TryGetProperty("enum", out var values))
                        continue;
                    var rows = string.Join("\n", text.Split('\n').Where(line => line.StartsWith($"| `{name}` |", StringComparison.Ordinal)));
                    foreach (var value in values.EnumerateArray())
                        Assert.Contains($"`{value.GetString()}`", rows);
                }
            }
            Assert.Contains("safe_diagnostics", text);
            Assert.Contains("safe: true", text);
            Assert.DoesNotContain("check/assign declaration IDs", text);
            Assert.DoesNotContain("Return type-context fixes", text);
        }
    }
}
