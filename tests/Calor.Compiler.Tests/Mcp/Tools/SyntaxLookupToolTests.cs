using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp.Tools;

public class SyntaxLookupToolTests
{
    private readonly HelpTool _tool = new();

    private async Task<JsonElement> LookupAsync(string topic, bool feature = false)
    {
        var result = await _tool.ExecuteAsync(feature
            ? JsonSerializer.SerializeToElement(new { feature = topic })
            : JsonSerializer.SerializeToElement(new { query = topic }));
        Assert.False(result.IsError, result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
    }

    [Fact]
    public async Task EveryTopic_ReturnsAtMostTwoByteIdenticalCanonicalPrograms_ThatActuallyCompile()
    {
        using var canonical = JsonDocument.Parse(File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "src", "Calor.Compiler", "Resources", "Teaching", "reference.json")));
        var programs = canonical.RootElement.GetProperty("programs").EnumerateArray()
            .ToDictionary(p => p.GetProperty("id").GetString()!, p => p.GetProperty("calor").GetString()!);
        var compiledPrograms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var construct in canonical.RootElement.GetProperty("constructs").EnumerateArray())
        {
            var topic = construct.GetProperty("csharpConstruct").GetString()!;
            var result = await LookupAsync(topic);
            var feature = await LookupAsync(topic, feature: true);
            Assert.Equal(result.GetRawText(), feature.GetRawText());
            Assert.False(result.TryGetProperty("sections", out _));
            Assert.Equal(topic, result.GetProperty("construct").GetString());
            var expected = construct.GetProperty("programs").EnumerateArray().Select(p => p.GetString()).ToList();
            var examples = result.GetProperty("examples").EnumerateArray().ToList();
            Assert.Equal(expected.Count > 0, result.GetProperty("found").GetBoolean());
            Assert.InRange(examples.Count, expected.Count == 0 ? 0 : 1, Math.Min(2, expected.Count));
            Assert.Equal(examples.Count, examples.Select(e => e.GetProperty("programId").GetString()).Distinct().Count());
            foreach (var example in examples)
            {
                var id = example.GetProperty("programId").GetString()!;
                Assert.Contains(id, expected);
                var source = example.GetProperty("calor").GetString()!;
                Assert.Equal(programs[id], source);
                Assert.StartsWith("§M{", source);
                Assert.NotEqual(result.GetProperty("calorSyntax").GetString(), source);
                if (compiledPrograms.Add(id))
                {
                    var compilation = Program.Compile(source, $"{id}.calr");
                    Assert.False(compilation.HasErrors, $"{topic}/{id}: {string.Join(Environment.NewLine, compilation.Diagnostics)}");
                    Assert.NotEmpty(compilation.GeneratedCode);
                }
            }
        }
        Assert.True(compiledPrograms.Count > 40, "The all-topic check must exercise diverse complete programs, not a common fallback.");
    }

    [Theory]
    [InlineData("foreach", "foreach loop", "task_14")]
    [InlineData("while", "while loop", "task_12")]
    [InlineData("postcondition", "postcondition", "primer_contracts")]
    [InlineData("indexof", "string operations", "primer_strings")]
    [InlineData("dictionary", "collection operations", "task_16")]
    [InlineData("postfix", "Infix expressions and precedence", "complete_postfix")]
    [InlineData("how do I index an array", "array element access", "nested_arrays")]
    [InlineData("how do I use a dictionary", "collection operations", "task_16")]
    [InlineData("ranges", "range expression", "complete_ranges")]
    [InlineData("strings", "string operations", "primer_strings")]
    public async Task PreciseTopics_RankRelevantCompleteProgramFirst_AndAliasesAgree(string query, string construct, string program)
    {
        var result = await LookupAsync(query);
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal(construct, result.GetProperty("construct").GetString());
        Assert.Equal(program, result.GetProperty("examples")[0].GetProperty("programId").GetString());
        Assert.Equal(result.GetRawText(), (await LookupAsync(query, feature: true)).GetRawText());
    }

    [Theory]
    [InlineData("unknown_feature_xyz")]
    [InlineData("quantum entanglement")]
    [InlineData("information")]
    [InlineData("specific")]
    [InlineData("how do I brew coffee")]
    [InlineData("x")]
    public async Task UnrelatedQueries_NeverFallBackToAnIncidentalExample(string query)
    {
        var result = await LookupAsync(query);
        Assert.False(result.GetProperty("found").GetBoolean());
        Assert.Empty(result.GetProperty("examples").EnumerateArray());
        Assert.False(result.TryGetProperty("construct", out _));
        Assert.Contains("action=features", result.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("dictionary", "task_16")]
    [InlineData("indexof", "primer_strings")]
    [InlineData("postfix", "complete_postfix")]
    public async Task PreciseExampleMatches_DoNotPadResponseWithUnrelatedSecondProgram(string query, string program)
    {
        var result = await LookupAsync(query);
        var example = Assert.Single(result.GetProperty("examples").EnumerateArray());
        Assert.Equal(program, example.GetProperty("programId").GetString());
    }

    [Theory]
    [InlineData("record")]
    [InlineData("pragma warning")]
    [InlineData("Staged section call")]
    public async Task TopicsWithoutCanonicalPrograms_PreserveCaveatsWithoutSubstitutingExamples(string query)
    {
        var result = await LookupAsync(query);
        Assert.False(result.GetProperty("found").GetBoolean());
        Assert.Empty(result.GetProperty("examples").EnumerateArray());
        Assert.Contains("no canonical executable example", result.GetProperty("message").GetString());
        Assert.Contains("action=features", result.GetProperty("message").GetString());
        Assert.NotEmpty(result.GetProperty("description").GetString()!);
    }

    [Fact]
    public async Task TiedConstructs_UseOrdinalIds_AndExampleTiesRetainCanonicalOrder()
    {
        var first = await LookupAsync("inheritance");
        Assert.Equal("abstract class", first.GetProperty("construct").GetString());
        for (var i = 0; i < 10; i++)
            Assert.Equal(first.GetRawText(), (await LookupAsync("inheritance")).GetRawText());
        var collections = await LookupAsync("collection operations");
        Assert.Equal(new[] { "task_14", "task_15" }, collections.GetProperty("examples").EnumerateArray()
            .Select(e => e.GetProperty("programId").GetString()).ToArray());
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("reference")]
    public async Task ExplicitDocumentationAccess_IsStillAvailableThroughEitherParameter(string topic)
    {
        var query = await LookupAsync(topic);
        var feature = await LookupAsync(topic, feature: true);
        Assert.Equal(query.GetRawText(), feature.GetRawText());
        Assert.NotEmpty(query.GetProperty("sections").EnumerateArray());
        Assert.False(query.TryGetProperty("examples", out _));
    }

    [Fact]
    public void EmbeddedResource_ExistsInAssembly()
    {
        // Verify the embedded resource can be loaded from the assembly
        var assembly = typeof(HelpTool).Assembly;
        var resourceName = "Calor.Compiler.Resources.calor-syntax-documentation.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        Assert.True(stream.Length > 1000, "Resource should contain substantial content");
    }

    [Fact]
    public void EmbeddedResource_ContainsValidJson()
    {
        var assembly = typeof(HelpTool).Assembly;
        var resourceName = "Calor.Compiler.Resources.calor-syntax-documentation.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        // Verify it's valid JSON with expected structure
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("constructs", out var constructs));
        Assert.True(doc.RootElement.TryGetProperty("tags", out var tags));
        Assert.True(constructs.GetArrayLength() > 20, "Should have many constructs");
        Assert.True(tags.EnumerateObject().Count() > 30, "Should have many tags");
    }

    [Fact]
    public void Name_ReturnsCorrectName()
    {
        Assert.Equal("calor_help", _tool.Name);
    }

    [Fact]
    public void Description_ContainsKeyInfo()
    {
        Assert.Contains("help", _tool.Description.ToLower());
        Assert.Contains("syntax", _tool.Description.ToLower());
    }

    [Fact]
    public void GetInputSchema_ReturnsValidSchema()
    {
        var schema = _tool.GetInputSchema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("query", out _));
    }

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQuery_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"query": ""}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_ObjectInstantiation_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "object instantiation"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        // Content is a list of McpContent items; the first item's Text contains the JSON
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("NEW", json); // §NEW will be escaped
        Assert.Contains("examples", json);
    }

    [Theory]
    [InlineData("method call", "target(arg1, arg2)")]
    [InlineData("null coalescing", "left ?? fallback")]
    [InlineData("postfix", "h.Make()(41)")]
    [InlineData("primitive types", "int, long, short")]
    public async Task ExecuteAsync_CurrentMeasuredSyntax_RemainsQueryable(string query, string expected)
    {
        var args = JsonSerializer.SerializeToElement(new { query });
        var result = await _tool.ExecuteAsync(args);
        Assert.False(result.IsError);
        Assert.Contains(expected, result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_ForLoop_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "for loop"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("loop", json.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_WhileLoop_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "while loop"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("while", json.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_IfStatement_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "if statement"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("IF", json);
    }

    [Fact]
    public async Task ExecuteAsync_TryCatch_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "try catch exception"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("TR", json);
    }

    [Fact]
    public async Task ExecuteAsync_AsyncAwait_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "async await"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("async", json.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_Lambda_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "lambda expression"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("LAM", json);
    }

    [Fact]
    public async Task ExecuteAsync_ClassDefinition_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "class definition"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("CL", json);
    }

    [Fact]
    public async Task ExecuteAsync_Property_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "property getter setter"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("PROP", json);
    }

    [Fact]
    public async Task ExecuteAsync_Constructor_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "constructor"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("CTOR", json);
    }

    [Fact]
    public async Task ExecuteAsync_Contracts_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "precondition postcondition contract"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_Effects_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "side effects"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_NonexistentConstruct_ReturnsNotFound()
    {
        var args = JsonDocument.Parse("""{"query": "xyzzy foobar nonexistent"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":false", json);
        Assert.Contains("availableConstructs", json);
    }

    [Fact]
    public async Task ExecuteAsync_FuzzyMatch_New_ReturnsObjectInstantiation()
    {
        var args = JsonDocument.Parse("""{"query": "new"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_FuzzyMatch_Var_ReturnsVariableDeclaration()
    {
        var args = JsonDocument.Parse("""{"query": "var"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("variable", json.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_ResultIncludesExamples()
    {
        var args = JsonDocument.Parse("""{"query": "return statement"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("examples", json);
        Assert.Contains("csharp", json);
        Assert.Contains("calor", json);
    }

    [Fact]
    public async Task ExecuteAsync_ResultIncludesDescription()
    {
        var args = JsonDocument.Parse("""{"query": "method definition"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("description", json);
    }

    [Fact]
    public async Task ExecuteAsync_PartialKeywordMatch_Works()
    {
        // Should match "foreach loop" even with partial keyword
        var args = JsonDocument.Parse("""{"query": "foreach"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_CaseInsensitive_Works()
    {
        var args = JsonDocument.Parse("""{"query": "OBJECT INSTANTIATION"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_Interface_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "interface definition"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("IFACE", json);
    }

    [Fact]
    public async Task ExecuteAsync_Enum_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "enum enumeration"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("EN", json);
    }

    [Fact]
    public async Task ExecuteAsync_Using_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "using dispose resource"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("USE", json);
    }

    [Fact]
    public async Task ExecuteAsync_Generic_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "generic type parameter"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_Switch_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "switch pattern matching"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_Null_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "null check nullable"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_NullCoalescingAssignment_ReturnsMatch()
    {
        // MCP Gap 3: ??= should be findable via calor_syntax_lookup
        var args = JsonDocument.Parse("""{"query": "??="}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("null", json.ToLower());
        Assert.Contains("IF", json); // workaround uses §IF
    }

    [Fact]
    public async Task ExecuteAsync_NullCoalescingAssignment_ByName_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "null coalescing assignment"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("null-coalescing assignment", json);
        Assert.Contains("examples", json);
    }

    [Fact]
    public async Task ExecuteAsync_IsPatternCombinator_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "is pattern or"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("combinator", json.ToLower());
    }

    [Fact]
    public async Task ExecuteAsync_RangeExpression_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "range slice"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("RANGE", json);
    }

    [Fact]
    public async Task ExecuteAsync_PositionalPattern_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "positional pattern"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("positional", json.ToLower());
        Assert.Contains("IF", json); // workaround uses §IF decomposition
    }

    [Fact]
    public async Task ExecuteAsync_PragmaWarning_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "pragma warning"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":false", json);
        Assert.Contains("no canonical executable example", json);
        Assert.Contains("action=features", json);
        Assert.DoesNotContain("§CSHARP", json);
    }

    #region Fuzzy Matching Edge Cases

    [Fact]
    public async Task ExecuteAsync_SingleCharacterQuery_ReturnsNotFound()
    {
        var args = JsonDocument.Parse("""{"query": "x"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        var output = JsonDocument.Parse(json).RootElement;
        Assert.False(output.GetProperty("found").GetBoolean());
        Assert.Empty(output.GetProperty("examples").EnumerateArray());
    }

    [Fact]
    public async Task ExecuteAsync_TypoInQuery_StillFindsMatch()
    {
        // "objet" instead of "object" - common typo
        var args = JsonDocument.Parse("""{"query": "objet instantiation"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        // Should still find object instantiation due to "instantiation" keyword
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousQuery_Method_ReturnsResult()
    {
        // "method" could match multiple constructs
        var args = JsonDocument.Parse("""{"query": "method"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_VeryLongQuery_HandledGracefully()
    {
        var longQuery = string.Join(" ", Enumerable.Repeat("object instantiation", 50));
        var args = JsonDocument.Parse($"{{\"query\": \"{longQuery}\"}}").RootElement;

        var result = await _tool.ExecuteAsync(args);

        // Should not throw, should handle gracefully
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_SpecialCharacters_HandledGracefully()
    {
        var args = JsonDocument.Parse("""{"query": "=> arrow function lambda"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        // Should match lambda due to "arrow", "function", "lambda" keywords
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_NumbersInQuery_HandledGracefully()
    {
        var args = JsonDocument.Parse("""{"query": "int32 integer 64bit"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        // Should handle numbers gracefully without crashing
    }

    [Fact]
    public async Task ExecuteAsync_VirtualMethod_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "virtual method override"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("vr", json); // virtual modifier
    }

    [Fact]
    public async Task ExecuteAsync_AbstractClass_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "abstract class"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("ab", json); // abstract modifier
    }

    [Fact]
    public async Task ExecuteAsync_SealedClass_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "sealed class final"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_LockStatement_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "lock thread synchronization"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
        Assert.Contains("SYNC", json);
    }

    [Fact]
    public async Task ExecuteAsync_TodoComment_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "todo fixme comment"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_ObsoleteDeprecated_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "obsolete deprecated"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_Complexity_ReturnsMatch()
    {
        var args = JsonDocument.Parse("""{"query": "complexity big o algorithm"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceOnlyQuery_ReturnsError()
    {
        var args = JsonDocument.Parse("""{"query": "   "}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_MixedCaseKeywords_Works()
    {
        var args = JsonDocument.Parse("""{"query": "FOR LOOP ITERATION"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        Assert.Contains("\"found\":true", json);
    }

    #endregion

    #region Load Tests

    [Fact]
    public async Task ExecuteAsync_ManyQueries_CompletesQuickly()
    {
        var queries = new[]
        {
            "object instantiation",
            "for loop",
            "while loop",
            "if statement",
            "try catch",
            "async await",
            "lambda expression",
            "class definition",
            "interface",
            "enum",
            "property",
            "method",
            "function",
            "return",
            "variable declaration",
            "constructor",
            "field",
            "virtual method",
            "abstract class",
            "sealed class"
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Execute 100 queries (each of the 20 queries 5 times)
        for (int i = 0; i < 5; i++)
        {
            foreach (var query in queries)
            {
                var args = JsonDocument.Parse($"{{\"query\": \"{query}\"}}").RootElement;
                var result = await _tool.ExecuteAsync(args);
                Assert.False(result.IsError);
            }
        }

        stopwatch.Stop();

        // 100 queries should complete in under 5 seconds
        Assert.True(stopwatch.ElapsedMilliseconds < 5000,
            $"100 queries took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentQueries_HandlesCorrectly()
    {
        var queries = new[]
        {
            "object instantiation",
            "for loop",
            "async await",
            "class definition",
            "try catch"
        };

        // Run multiple queries concurrently
        var tasks = queries.Select(async query =>
        {
            var args = JsonDocument.Parse($"{{\"query\": \"{query}\"}}").RootElement;
            var result = await _tool.ExecuteAsync(args);
            Assert.False(result.IsError);
            return result;
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.False(r.IsError));
        Assert.Equal(queries.Length, results.Length);
    }

    [Fact]
    public async Task ExecuteAsync_RepeatedSameQuery_ReturnsSameResult()
    {
        var args = JsonDocument.Parse("""{"query": "object instantiation"}""").RootElement;

        // Execute same query multiple times
        var results = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var result = await _tool.ExecuteAsync(args);
            results.Add(result.Content[0].Text ?? "");
        }

        // All results should be identical
        Assert.All(results, r => Assert.Equal(results[0], r));
    }

    [Fact]
    public async Task ExecuteAsync_AllConstructKeywords_FindMatches()
    {
        // Test that common C# keywords find matches
        var keywords = new[]
        {
            "new", "var", "class", "interface", "enum", "struct",
            "for", "foreach", "while", "do", "if", "else", "switch",
            "try", "catch", "finally", "throw",
            "async", "await", "return",
            "public", "private", "protected", "static",
            "virtual", "override", "abstract", "sealed",
            "property", "field", "method", "constructor",
            "lambda", "delegate", "event",
            "using", "lock", "null", "nullable"
        };

        var notFoundKeywords = new List<string>();

        foreach (var keyword in keywords)
        {
            var args = JsonDocument.Parse($"{{\"query\": \"{keyword}\"}}").RootElement;
            var result = await _tool.ExecuteAsync(args);
            var json = result.Content[0].Text ?? "";

            if (!json.Contains("\"found\":true"))
            {
                notFoundKeywords.Add(keyword);
            }
        }

        // All common keywords should find a match
        Assert.Empty(notFoundKeywords);
    }

    #endregion
}
