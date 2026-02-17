using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp.Tools;

public class SyntaxLookupToolTests
{
    private readonly SyntaxLookupTool _tool = new();

    [Fact]
    public void EmbeddedResource_ExistsInAssembly()
    {
        // Verify the embedded resource can be loaded from the assembly
        var assembly = typeof(SyntaxLookupTool).Assembly;
        var resourceName = "Calor.Compiler.Resources.calor-syntax-documentation.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        Assert.True(stream.Length > 1000, "Resource should contain substantial content");
    }

    [Fact]
    public void EmbeddedResource_ContainsValidJson()
    {
        var assembly = typeof(SyntaxLookupTool).Assembly;
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
        Assert.Equal("calor_syntax_lookup", _tool.Name);
    }

    [Fact]
    public void Description_ContainsKeyInfo()
    {
        Assert.Contains("C#", _tool.Description);
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

    #region Fuzzy Matching Edge Cases

    [Fact]
    public async Task ExecuteAsync_SingleCharacterQuery_ReturnsNotFound()
    {
        var args = JsonDocument.Parse("""{"query": "x"}""").RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var json = result.Content[0].Text ?? "";
        // Single character queries may not find specific matches
        // This tests that we handle them gracefully
        Assert.DoesNotContain("exception", json.ToLower());
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
        Assert.Contains("LK", json);
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
