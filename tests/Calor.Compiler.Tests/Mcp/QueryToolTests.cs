using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Indexing;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// v0.16 E7 — <c>calor_query</c>: argument validation, registration, and the
/// refusals it must voice in the CLI's own words (missing index, unknown or
/// ambiguous symbol, stale format, a row that does not parse). The answers'
/// byte-identity with <c>calor query</c> is the gate leg in
/// <see cref="QueryToolGateTests"/>.
/// </summary>
[Collection("McpSerial")]
public sealed class QueryToolTests : IDisposable
{
    private readonly List<string> _dirs = [];
    private readonly QueryTool _tool = new();

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private string TempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "calor-qtool-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    private string Fixture()
    {
        var dir = TempDir();
        var corpus = Path.Combine(
            CliTestHarness.FindRepoRoot(), "tests", "TestData", "QueryCorpus", "project");
        foreach (var source in Directory.GetFiles(corpus, "*.calr"))
            File.Copy(source, Path.Combine(dir, Path.GetFileName(source)));
        return dir;
    }

    private static string IndexFile(string dir) =>
        ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(dir));

    private static void BuildIndex(string dir, string? output = null)
    {
        ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
            dir, ProjectIndexQueryReader.OptionsToken, ProjectIndexBuilder.DiscoverSources(dir)))
            .Save(output ?? IndexCommand.DefaultOutputDirectory(dir));
    }

    private async Task<McpToolResult> Call(string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return await _tool.ExecuteAsync(document.RootElement.Clone());
    }

    private static string Text(McpToolResult result) => result.Content[0].Text ?? "";

    private static string Args(string dir, string facet, string symbol, string extra = "") =>
        $$"""{ "projectDirectory": {{JsonSerializer.Serialize(dir)}}, "facet": "{{facet}}", "symbol": "{{symbol}}"{{extra}} }""";

    // --- identity ------------------------------------------------------------

    [Fact]
    public void Name_IsCalorQuery()
    {
        Assert.Equal("calor_query", _tool.Name);
        Assert.Contains("callers", _tool.Description);
        Assert.Contains("effects", _tool.Description);
        Assert.Contains("PARTIAL", _tool.Description);
    }

    [Fact]
    public void Annotations_NotReadOnly_BecauseResolvingRebuildsTheIndex()
    {
        var annotations = _tool.Annotations;
        Assert.NotNull(annotations);
        Assert.False(annotations!.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
    }

    [Fact]
    public void Schema_RequiresProjectDirectoryFacetAndSymbol()
    {
        var schema = _tool.GetInputSchema();
        Assert.Equal(
            new[] { "projectDirectory", "facet", "symbol" },
            schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        var properties = schema.GetProperty("properties");
        Assert.Equal(
            new[] { "projectDirectory", "facet", "symbol", "inFile", "effects", "row", "noBuild", "indexPath", "format" },
            properties.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ProjectIndexQueryReader.Facets,
            properties.GetProperty("facet").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task IsRegisteredWithTheServer_AndAnswersThroughToolsCall()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var handler = new McpMessageHandler();

        var list = await handler.HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonDocument.Parse("1").RootElement,
            Method = "tools/list",
        });
        Assert.Contains("\"calor_query\"", JsonSerializer.Serialize(list!.Result, McpJsonOptions.Default));

        var call = await handler.HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonDocument.Parse("2").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse($$"""
                { "name": "calor_query", "arguments": {{Args(dir, "callers", "Scale", ", \"noBuild\": true, \"format\": \"text\"")}} }
                """).RootElement,
        });
        Assert.Null(call!.Error);
        var result = Assert.IsType<McpToolResult>(call.Result);
        Assert.False(result.IsError);
        Assert.Equal(
            "  app.calr:2:11 function Run" + Environment.NewLine
                + "  math.calr:5:11 function ScaleTwice" + Environment.NewLine
                + "query: 2 caller(s) of math.calr:2:11 function Scale" + Environment.NewLine,
            Text(result));
    }

    // --- argument validation ------------------------------------------------

    [Fact]
    public async Task MissingProjectDirectory_IsAnError()
    {
        var result = await Call("""{ "facet": "callers", "symbol": "Scale" }""");
        Assert.True(result.IsError);
        Assert.Equal("Missing required parameter: projectDirectory", Text(result));
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("contracts")]
    [InlineData("assumptions")]
    [InlineData("CALLERS")]
    public async Task FacetOutsideTheFour_IsAnError(string facet)
    {
        var result = await Call(Args(Fixture(), facet, "Scale"));
        Assert.True(result.IsError);
        Assert.Equal("Parameter 'facet' must be one of: callers, callees, impact, effects", Text(result));
    }

    [Fact]
    public async Task MissingFacet_IsAnError()
    {
        var result = await Call($$"""{ "projectDirectory": {{JsonSerializer.Serialize(Fixture())}}, "symbol": "Scale" }""");
        Assert.True(result.IsError);
        Assert.StartsWith("Parameter 'facet' must be one of", Text(result));
    }

    [Fact]
    public async Task MissingSymbol_IsAnError()
    {
        var result = await Call($$"""{ "projectDirectory": {{JsonSerializer.Serialize(Fixture())}}, "facet": "callers" }""");
        Assert.True(result.IsError);
        Assert.Equal("Missing required parameter: symbol", Text(result));
    }

    [Fact]
    public async Task UnknownFormat_IsAnError()
    {
        var result = await Call(Args(Fixture(), "callers", "Scale", ", \"format\": \"yaml\""));
        Assert.True(result.IsError);
        Assert.Equal("Parameter 'format' must be \"json\" or \"text\"", Text(result));
    }

    [Theory]
    [InlineData("callers", ", \"effects\": true")]
    [InlineData("callees", ", \"row\": \"cw\"")]
    [InlineData("effects", ", \"effects\": true, \"row\": \"cw\"")]
    public async Task EffectsAndRow_ApplyToImpactOnly(string facet, string extra)
    {
        var result = await Call(Args(Fixture(), facet, "Scale", extra));
        Assert.True(result.IsError);
        Assert.Equal("Parameters 'effects' and 'row' apply to facet \"impact\" only", Text(result));
    }

    [Fact]
    public async Task RowWithoutEffects_IsAnError()
    {
        var result = await Call(Args(Fixture(), "impact", "Log", ", \"row\": \"cw\""));
        Assert.True(result.IsError);
        Assert.Equal("Parameter 'row' requires effects=true", Text(result));
    }

    // --- resolution refusals, in the CLI's words ----------------------------

    [Fact]
    public async Task DirectoryNotFound_IsTheCliError()
    {
        var missing = Path.Combine(TempDir(), "nope");
        var result = await Call(Args(missing, "callers", "Scale"));
        Assert.True(result.IsError);
        Assert.Equal($"Error: directory not found: {missing}", Text(result));
    }

    [Fact]
    public async Task NoSources_IsTheCliError()
    {
        var dir = TempDir();
        var result = await Call(Args(dir, "callers", "Scale"));
        Assert.True(result.IsError);
        Assert.Equal($"Error: no .calr files under {dir}", Text(result));
    }

    [Fact]
    public async Task MissingIndexWithNoBuild_IsTheCliError_AndWritesNothing()
    {
        var dir = Fixture();
        var result = await Call(Args(dir, "callers", "Scale", ", \"noBuild\": true"));
        Assert.True(result.IsError);
        Assert.Equal(
            "Error: index unusable — no index has been built. Run `calor index build` (or drop --no-build).",
            Text(result));
        Assert.False(File.Exists(IndexFile(dir)));
    }

    [Fact]
    public async Task MissingIndex_IsBuiltAndWritten_ThenAnswered()
    {
        var dir = Fixture();
        var result = await Call(Args(dir, "callers", "Scale"));
        Assert.False(result.IsError);
        Assert.True(File.Exists(IndexFile(dir)));
        using var envelope = JsonDocument.Parse(Text(result));
        Assert.Equal(2, envelope.RootElement.GetProperty("data").GetProperty("declarations").GetArrayLength());
    }

    [Fact]
    public async Task StaleFormatVersionWithNoBuild_IsTheCliError()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var output = IndexCommand.DefaultOutputDirectory(dir);
        var (index, _) = ProjectIndex.Load(output);
        index!.FormatVersion = "3.0";
        index.Save(output);

        var result = await Call(Args(dir, "effects", "Leaky", ", \"noBuild\": true"));
        Assert.True(result.IsError);
        Assert.Equal(
            "Error: index unusable — the index format version changed. Run `calor index build` (or drop --no-build).",
            Text(result));
    }

    [Fact]
    public async Task ChangedSourcesWithNoBuild_IsTheCliError()
    {
        var dir = Fixture();
        BuildIndex(dir);
        File.AppendAllText(Path.Combine(dir, "app.calr"), "\n");

        var result = await Call(Args(dir, "effects", "Leaky", ", \"noBuild\": true"));
        Assert.True(result.IsError);
        Assert.Equal(
            "Error: index unusable — the source files changed. Run `calor index build` (or drop --no-build).",
            Text(result));
    }

    [Fact]
    public async Task IndexPath_PointsAtAnIndexBuiltElsewhere()
    {
        var dir = Fixture();
        var custom = Path.Combine(TempDir(), "idx");
        BuildIndex(dir, custom);
        Assert.False(File.Exists(IndexFile(dir)));

        var byDirectory = await Call(Args(dir, "callees", "ScaleTwice",
            $", \"noBuild\": true, \"indexPath\": {JsonSerializer.Serialize(custom)}"));
        Assert.False(byDirectory.IsError);

        var byFile = await Call(Args(dir, "callees", "ScaleTwice",
            $", \"noBuild\": true, \"indexPath\": {JsonSerializer.Serialize(ProjectIndex.PathFor(custom))}"));
        Assert.False(byFile.IsError);
        Assert.Equal(Text(byDirectory), Text(byFile));

        var withoutIt = await Call(Args(dir, "callees", "ScaleTwice", ", \"noBuild\": true"));
        Assert.True(withoutIt.IsError);
    }

    // --- subject refusals ---------------------------------------------------

    [Fact]
    public async Task UnknownSymbol_IsTheCliText()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "callers", "NoSuchName"));
        Assert.True(result.IsError);
        Assert.Equal("query: no declaration named 'NoSuchName'", Text(result));
    }

    [Fact]
    public async Task UnknownSymbol_ForImpact_IsTheCliError()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "impact", "NoSuchName"));
        Assert.True(result.IsError);
        Assert.Equal("Error: no declaration named 'NoSuchName'. Use --file to ask about a file.", Text(result));
    }

    [Fact]
    public async Task AmbiguousSymbol_IsRefusedWithTheCandidates()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "callers", "Shared"));
        Assert.True(result.IsError);
        Assert.Equal(
            "Error: 'Shared' is declared in 2 places; narrow it with --in-file:" + Environment.NewLine
                + "  ambiguous.calr:2:11 function Shared" + Environment.NewLine
                + "  ambiguous2.calr:2:11 function Shared",
            Text(result));

        var narrowed = await Call(Args(dir, "callers", "Shared", ", \"inFile\": \"ambiguous2.calr\", \"format\": \"text\""));
        Assert.False(narrowed.IsError);
        Assert.StartsWith("  ambiguous2.calr:5:11 function AsksShared", Text(narrowed));
        Assert.Contains("query: PARTIAL", Text(narrowed));
    }

    [Fact]
    public async Task RowThatDoesNotParse_IsTheCliError()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "impact", "Log", ", \"effects\": true, \"row\": \"not-a-code\""));
        Assert.True(result.IsError);
        Assert.StartsWith("Error: --row 'not-a-code' is not a row of effect codes: ", Text(result));
    }

    [Fact]
    public async Task EffectsWithNoRecordedRow_SaysSo_AsAnError()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "broken.calr"), """
            §M{m002:Broken}
              §F{f001:Uses:pub} () -> i32
                §E{}
                §R (+ undefinedName INT:1)
            """);
        var text = await Call(Args(dir, "effects", "Uses", ", \"format\": \"text\""));
        Assert.True(text.IsError);
        Assert.StartsWith("query: no effect row for broken.calr:2:11 function Uses — ", Text(text));

        // The JSON form is the CLI's: an envelope with empty rows, not an error.
        var json = await Call(Args(dir, "effects", "Uses"));
        Assert.False(json.IsError);
        using var envelope = JsonDocument.Parse(Text(json));
        Assert.Equal(0, envelope.RootElement.GetProperty("data").GetProperty("rows").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(envelope.RootElement.GetProperty("data").GetProperty("unavailable").GetString()));
    }

    // --- answers -------------------------------------------------------------

    [Fact]
    public async Task JsonIsTheDefault_AndIsTheCliEnvelope()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "effects", "Leaky"));
        Assert.False(result.IsError);
        using var envelope = JsonDocument.Parse(Text(result));
        Assert.Equal("query", envelope.RootElement.GetProperty("command").GetString());
        var row = Assert.Single(envelope.RootElement.GetProperty("data").GetProperty("rows").EnumerateArray());
        Assert.Equal("Calor0410", row.GetProperty("diagnosticCode").GetString());
    }

    [Fact]
    public async Task ImpactWithEffects_DefaultsToTheCurrentDeclaredRow()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "impact", "Log", ", \"effects\": true, \"format\": \"text\""));
        Assert.False(result.IsError);
        Assert.Contains(
            "impact: 1 of 3 affected declaration(s) would stop fitting a row of cw (its current declared row)",
            Text(result));
    }

    [Fact]
    public async Task AFreshIndex_IsNeverRewritten()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var path = IndexFile(dir);
        var before = File.ReadAllBytes(path);
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await Call(Args(dir, "impact", "Scale"));

        Assert.False(result.IsError);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
    }
}
