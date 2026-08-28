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

    /// <summary>
    /// The tool as the server registers it, confined to <paramref name="root"/>.
    /// Every fixture here lives under the system temp directory, so the root is
    /// the workspace itself unless a test is probing confinement.
    /// </summary>
    private static QueryTool ToolFor(string root) => new(root);

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

    private static string FixtureCorpus => Path.Combine(
        CliTestHarness.FindRepoRoot(), "tests", "TestData", "QueryCorpus", "project");

    private string Fixture()
    {
        var dir = TempDir();
        foreach (var source in Directory.GetFiles(FixtureCorpus, "*.calr"))
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

    /// <summary>Calls the tool rooted at its own projectDirectory (the ordinary case).</summary>
    private async Task<McpToolResult> Call(string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        var arguments = document.RootElement.Clone();
        var root = arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("projectDirectory", out var directory)
            && directory.ValueKind == JsonValueKind.String
                ? directory.GetString()!
                : Path.GetTempPath();
        if (!Directory.Exists(root))
            root = Path.GetTempPath();
        return await ToolFor(root).ExecuteAsync(arguments);
    }

    /// <summary>Calls a tool pinned to <paramref name="serverRoot"/>, whatever the arguments ask for.</summary>
    private static async Task<McpToolResult> CallRooted(string serverRoot, string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return await ToolFor(serverRoot).ExecuteAsync(document.RootElement.Clone());
    }

    private static string Text(McpToolResult result) => result.Content[0].Text ?? "";

    private static string Args(string dir, string facet, string symbol, string extra = "") =>
        $$"""{ "projectDirectory": {{JsonSerializer.Serialize(dir)}}, "facet": "{{facet}}", "symbol": "{{symbol}}"{{extra}} }""";

    // --- identity ------------------------------------------------------------

    [Fact]
    public void Name_IsCalorQuery()
    {
        var tool = ToolFor(Path.GetTempPath());
        Assert.Equal("calor_query", tool.Name);
        Assert.Contains("callers", tool.Description);
        Assert.Contains("effects", tool.Description);
        Assert.Contains("PARTIAL", tool.Description);
    }

    [Fact]
    public void Annotations_NotReadOnly_BecauseResolvingRebuildsTheIndex()
    {
        var annotations = ToolFor(Path.GetTempPath()).Annotations;
        Assert.NotNull(annotations);
        Assert.False(annotations!.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
    }

    [Fact]
    public void Schema_RequiresProjectDirectoryFacetAndSymbol()
    {
        var schema = ToolFor(Path.GetTempPath()).GetInputSchema();
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
        var handler = new McpMessageHandler(rootDirectory: dir);

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
        Assert.Equal($"Error: directory not found: {missing}" + Environment.NewLine, Text(result));
    }

    [Fact]
    public async Task NoSources_IsTheCliError()
    {
        var dir = TempDir();
        var result = await Call(Args(dir, "callers", "Scale"));
        Assert.True(result.IsError);
        Assert.Equal($"Error: no .calr files under {dir}" + Environment.NewLine, Text(result));
    }

    [Fact]
    public async Task MissingIndexWithNoBuild_IsTheCliError_AndWritesNothing()
    {
        var dir = Fixture();
        var result = await Call(Args(dir, "callers", "Scale", ", \"noBuild\": true"));
        Assert.True(result.IsError);
        Assert.Equal(
            "Error: index unusable — no index has been built. Run `calor index build` (or drop --no-build)."
                + Environment.NewLine,
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
            "Error: index unusable — the index format version changed. Run `calor index build` (or drop --no-build)."
                + Environment.NewLine,
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
            "Error: index unusable — the source files changed. Run `calor index build` (or drop --no-build)."
                + Environment.NewLine,
            Text(result));
    }

    [Fact]
    public async Task IndexPath_PointsAtAnIndexBuiltElsewhereInsideTheProject()
    {
        var dir = Fixture();
        var custom = Path.Combine(dir, "idx");
        BuildIndex(dir, custom);
        Assert.False(File.Exists(IndexFile(dir)));

        var byDirectory = await Call(Args(dir, "callees", "ScaleTwice",
            $", \"indexPath\": {JsonSerializer.Serialize(custom)}"));
        Assert.False(byDirectory.IsError, Text(byDirectory));

        var byFile = await Call(Args(dir, "callees", "ScaleTwice",
            $", \"indexPath\": {JsonSerializer.Serialize(ProjectIndex.PathFor(custom))}"));
        Assert.False(byFile.IsError);
        Assert.Equal(Text(byDirectory), Text(byFile));

        var withoutIt = await Call(Args(dir, "callees", "ScaleTwice", ", \"noBuild\": true"));
        Assert.True(withoutIt.IsError);
    }

    /// <summary>
    /// An explicit index path is READ-ONLY: a stale index there is refused even
    /// without noBuild, and nothing is written to it. Rebuilding through this
    /// argument is what would let it create a tree anywhere, or overwrite
    /// another project's index in place.
    /// </summary>
    [Fact]
    public async Task IndexPath_IsReadOnly_AStaleOneIsRefusedRatherThanRebuilt()
    {
        var dir = Fixture();
        var custom = Path.Combine(dir, "idx");
        BuildIndex(dir, custom);
        File.AppendAllText(Path.Combine(dir, "app.calr"), "\n");
        var before = File.ReadAllBytes(ProjectIndex.PathFor(custom));

        var result = await Call(Args(dir, "callees", "ScaleTwice",
            $", \"indexPath\": {JsonSerializer.Serialize(custom)}"));

        Assert.True(result.IsError);
        Assert.StartsWith("Error: index unusable — the source files changed.", Text(result));
        Assert.Equal(before, File.ReadAllBytes(ProjectIndex.PathFor(custom)));
        Assert.False(File.Exists(IndexFile(dir)));
    }

    // --- write confinement (review #1 M1) -----------------------------------

    /// <summary>
    /// Resolving a stale or missing index REBUILDS it, so this tool writes —
    /// and is confined to the server's root exactly as calor_file_write is.
    /// An absolute project directory outside the root is refused, and nothing
    /// is written there.
    /// </summary>
    [Fact]
    public async Task ProjectDirectoryOutsideTheServerRoot_IsRefused_AndWritesNothing()
    {
        var outside = Fixture();
        var serverRoot = TempDir();

        var result = await CallRooted(serverRoot, Args(outside, "callers", "Scale"));

        Assert.True(result.IsError);
        Assert.StartsWith("Parameter 'projectDirectory' is outside the server's root ", Text(result));
        Assert.False(File.Exists(IndexFile(outside)));
        Assert.False(Directory.Exists(IndexCommand.DefaultOutputDirectory(outside)));
    }

    [Fact]
    public async Task DotDotTraversalOutOfTheRoot_IsRefused()
    {
        // The escape is canonicalised before the check, so "<root>/../sibling"
        // is rejected on where it LANDS, not on how it is spelled.
        var parent = TempDir();
        var serverRoot = Path.Combine(parent, "root");
        Directory.CreateDirectory(serverRoot);
        var sibling = Path.Combine(parent, "sibling");
        Directory.CreateDirectory(sibling);
        File.Copy(
            Path.Combine(FixtureCorpus, "math.calr"),
            Path.Combine(sibling, "math.calr"));

        var traversal = Path.Combine(serverRoot, "..", "sibling");
        var result = await CallRooted(serverRoot, Args(traversal, "callers", "Scale"));

        Assert.True(result.IsError);
        Assert.StartsWith("Parameter 'projectDirectory' is outside the server's root ", Text(result));
        Assert.False(Directory.Exists(Path.Combine(sibling, "obj")));
    }

    [Fact]
    public async Task IndexPathOutsideTheRoot_IsRefused_AndCreatesNothing()
    {
        var dir = Fixture();
        var elsewhere = Path.Combine(TempDir(), "a", "b", "c", "not-a-calor-dir");

        var result = await CallRooted(dir, Args(dir, "callers", "Scale",
            $", \"indexPath\": {JsonSerializer.Serialize(elsewhere)}"));

        Assert.True(result.IsError);
        Assert.StartsWith("Parameter 'indexPath' is outside the server's root ", Text(result));
        Assert.False(Directory.Exists(elsewhere));
    }

    /// <summary>
    /// The overwrite probe: an index belonging to ANOTHER project, handed in
    /// through indexPath, is neither answered from (its header does not match
    /// this project) nor rewritten with this project's contents.
    /// </summary>
    [Fact]
    public async Task AForeignIndex_IsNeitherAnsweredFromNorOverwritten()
    {
        var parent = TempDir();
        var project = Path.Combine(parent, "project");
        Directory.CreateDirectory(project);
        foreach (var source in Directory.GetFiles(FixtureCorpus, "*.calr"))
            File.Copy(source, Path.Combine(project, Path.GetFileName(source)));

        var foreign = Path.Combine(parent, "foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "other.calr"), """
            §M{m001:Other}
              §F{f001:OtherOnly:pub} () -> i32
                §E{}
                §R INT:7
            """);
        var foreignIndexDirectory = Path.Combine(foreign, "obj", "calor");
        BuildIndex(foreign, foreignIndexDirectory);
        var foreignBytes = File.ReadAllBytes(ProjectIndex.PathFor(foreignIndexDirectory));

        var result = await CallRooted(parent, Args(project, "callers", "Scale",
            $", \"indexPath\": {JsonSerializer.Serialize(foreignIndexDirectory)}"));

        Assert.True(result.IsError);
        Assert.StartsWith("Error: index unusable — ", Text(result));
        Assert.Equal(foreignBytes, File.ReadAllBytes(ProjectIndex.PathFor(foreignIndexDirectory)));
        var (survived, _) = ProjectIndex.Load(foreignIndexDirectory);
        Assert.NotEmpty(survived!.FindDeclarations("OtherOnly"));
        Assert.Empty(survived.FindDeclarations("Scale"));
    }

    // --- argument kinds (review #1 M2 / M3) ---------------------------------

    [Theory]
    [InlineData("noBuild", "\"true\"")]
    [InlineData("noBuild", "1")]
    [InlineData("effects", "\"yes\"")]
    public async Task NonBooleanFlag_IsRefused_RatherThanSilentlyIgnored(string name, string literal)
    {
        // GetBool returns the default for any non-boolean kind, which would turn
        // "noBuild": "true" into "rebuild the index" — the opposite of what the
        // caller asked, on the flag that decides whether this tool writes.
        var dir = Fixture();
        var facet = name == "effects" ? "impact" : "callers";
        var result = await Call(Args(dir, facet, "Scale", $", \"{name}\": {literal}"));

        Assert.True(result.IsError);
        Assert.StartsWith($"Parameter '{name}' must be a boolean (true or false), not ", Text(result));
        Assert.False(File.Exists(IndexFile(dir)));
    }

    [Theory]
    [InlineData("noBuild")]
    [InlineData("effects")]
    public async Task NullFlag_IsTheDefault(string name)
    {
        var dir = Fixture();
        BuildIndex(dir);
        var facet = name == "effects" ? "impact" : "callers";
        var result = await Call(Args(dir, facet, "Scale", $", \"{name}\": null"));
        Assert.False(result.IsError, Text(result));
    }

    [Fact]
    public async Task BlankIndexPath_IsNoOverride_NotAnInternalError()
    {
        // Path.GetFullPath("") throws; an exception here would leave the client
        // with JSON-RPC -32603 instead of the refusal this tool promises.
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "callers", "Scale", ", \"indexPath\": \"\", \"noBuild\": true"));
        Assert.False(result.IsError, Text(result));
        using var envelope = JsonDocument.Parse(Text(result));
        Assert.Equal(2, envelope.RootElement.GetProperty("data").GetProperty("declarations").GetArrayLength());
    }

    [Fact]
    public async Task BlankIndexPath_ThroughTheServer_IsNotAProtocolError()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var handler = new McpMessageHandler(rootDirectory: dir);
        var response = await handler.HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonDocument.Parse("7").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse($$"""
                { "name": "calor_query", "arguments": {{Args(dir, "callers", "Scale", ", \"indexPath\": \"\", \"noBuild\": true, \"format\": \"text\"")}} }
                """).RootElement,
        });

        Assert.NotNull(response);
        Assert.Null(response!.Error);
        var result = Assert.IsType<McpToolResult>(response.Result);
        Assert.False(result.IsError, Text(result));
    }

    [Fact]
    public async Task UnknownParameter_IsRefused()
    {
        // The schema declares additionalProperties:false; enforce it rather
        // than trusting every client to validate.
        var dir = Fixture();
        var result = await Call(Args(dir, "callers", "Scale", ", \"noBuidl\": true"));
        Assert.True(result.IsError);
        Assert.StartsWith("Unknown parameter(s): noBuidl. Accepted: projectDirectory, facet, symbol", Text(result));
        Assert.False(File.Exists(IndexFile(dir)));
    }

    // --- subject refusals ---------------------------------------------------

    [Fact]
    public async Task UnknownSymbol_IsTheCliText()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "callers", "NoSuchName"));
        Assert.True(result.IsError);
        Assert.Equal("query: no declaration named 'NoSuchName'" + Environment.NewLine, Text(result));
    }

    [Fact]
    public async Task UnknownSymbol_ForImpact_IsTheCliError()
    {
        var dir = Fixture();
        BuildIndex(dir);
        var result = await Call(Args(dir, "impact", "NoSuchName"));
        Assert.True(result.IsError);
        // The tool has no --file counterpart, so it names the CLI-only route
        // instead of pointing at a flag it does not accept.
        Assert.Equal(
            "Error: no declaration named 'NoSuchName'. Whole-file impact is CLI-only: "
                + "`calor query impact <file> --file`." + Environment.NewLine,
            Text(result));
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
                + "  ambiguous2.calr:2:11 function Shared" + Environment.NewLine,
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
        Assert.EndsWith(Environment.NewLine, Text(result));
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
        // The envelope's command is "query" for every facet, so the payload's
        // own `facet` is what tells a client which answer it is holding.
        Assert.Equal("effects", envelope.RootElement.GetProperty("data").GetProperty("facet").GetString());
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
