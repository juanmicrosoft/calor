using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Indexing;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// v0.16 gate 7, E7 leg (roadmap-v0.16 §5 item 7, unconditional): the
/// index/query goldens — every row an agent can ask <c>calor_query</c> for,
/// including the ten-plus effects rows E5 registered — answered through the
/// MCP tool, byte-for-byte with <c>calor query</c>, FROM THE INDEX FILE THE
/// CLI WROTE. The discriminating pin (§3.1 E7): a tool that answered from an
/// in-memory graph instead of the index would not see what the file says,
/// and the cross-module fold (<c>Whisper</c>) is where that shows.
///
/// Corpus: tests/TestData/QueryCorpus/ (authored ground truth, see
/// <see cref="QueryGoldenTests"/>).
/// </summary>
[Collection("McpSerial")]
public sealed class QueryToolGateTests : IClassFixture<QueryToolGateTests.IndexedCorpus>
{
    /// <summary>
    /// The corpus copied to a workspace and indexed ONCE by the CLI process
    /// (<c>calor index build</c>) — the file every test here reads through the
    /// tool. Nothing in this class rebuilds it: every query passes
    /// <c>noBuild=true</c>, so a stale or missing file is a failure, not a
    /// silent rebuild.
    /// </summary>
    public sealed class IndexedCorpus : IDisposable
    {
        public string Directory { get; }
        public string IndexPath { get; }
        public byte[] IndexBytes { get; }

        public IndexedCorpus()
        {
            Directory = Path.Combine(
                Path.GetTempPath(), "calor-e7gate-" + Guid.NewGuid().ToString("N")[..12]);
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var source in System.IO.Directory.GetFiles(FixtureRoot, "*.calr"))
                File.Copy(source, Path.Combine(Directory, Path.GetFileName(source)));

            var build = CliTestHarness.RunCli(Directory, "index", "build", Directory);
            if (build.ExitCode != 0)
                throw new InvalidOperationException("calor index build failed: " + build.StdOut + build.StdErr);

            IndexPath = ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(Directory));
            IndexBytes = File.ReadAllBytes(IndexPath);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); } catch { }
        }
    }

    private readonly IndexedCorpus _corpus;

    public QueryToolGateTests(IndexedCorpus corpus)
    {
        _corpus = corpus;
    }

    private sealed record GoldenQuery(
        string Facet,
        string Name,
        string? InFile,
        string[] Expect,
        bool Partial,
        string? Row);

    /// <summary>The facets the tool answers; <c>symbol</c>, <c>contracts</c>, <c>assumptions</c> and <c>impact-file</c> stay CLI-only.</summary>
    private static readonly string[] ToolFacets = ["callers", "callees", "impact", "effects", "impact-effects"];

    public static TheoryData<int, string> ToolGoldens()
    {
        var data = new TheoryData<int, string>();
        var queries = LoadGoldens();
        for (var index = 0; index < queries.Count; index++)
        {
            if (ToolFacets.Contains(queries[index].Facet, StringComparer.Ordinal))
                data.Add(index, $"{queries[index].Facet}:{queries[index].Name}");
        }
        return data;
    }

    private static async Task<McpToolResult> CallTool(string argumentsJson)
    {
        var handler = new McpMessageHandler();
        var response = await handler.HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonDocument.Parse("1").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse($$"""{ "name": "calor_query", "arguments": {{argumentsJson}} }""").RootElement,
        });
        Assert.NotNull(response);
        Assert.Null(response!.Error);
        return Assert.IsType<McpToolResult>(response.Result);
    }

    private string ToolArguments(GoldenQuery golden, string format)
    {
        var facet = golden.Facet == "impact-effects" ? "impact" : golden.Facet;
        var arguments = new Dictionary<string, object?>
        {
            ["projectDirectory"] = _corpus.Directory,
            ["facet"] = facet,
            ["symbol"] = golden.Name,
            ["noBuild"] = true,
            ["format"] = format,
        };
        if (golden.InFile != null)
            arguments["inFile"] = golden.InFile;
        if (golden.Facet == "impact-effects")
        {
            arguments["effects"] = true;
            arguments["row"] = golden.Row;
        }
        return JsonSerializer.Serialize(arguments);
    }

    private string[] CliArguments(GoldenQuery golden, bool json)
    {
        var arguments = new List<string>
        {
            "query",
            golden.Facet == "impact-effects" ? "impact" : golden.Facet,
            golden.Name,
            "--project", _corpus.Directory,
            "--no-build",
        };
        if (golden.InFile != null)
            arguments.AddRange(["--in-file", golden.InFile]);
        if (golden.Facet == "impact-effects")
            arguments.AddRange(["--effects", "--row", golden.Row!]);
        if (json)
            arguments.Add("--json");
        return arguments.ToArray();
    }

    /// <summary>
    /// The gate proper: each golden's answer, read off the tool's JSON payload
    /// and rendered in the golden's own vocabulary, equals the authored ground
    /// truth — including whether the answer is partial.
    /// </summary>
    [Theory]
    [MemberData(nameof(ToolGoldens))]
    public async Task GoldenIsAnsweredThroughCalorQuery(int position, string label)
    {
        var golden = LoadGoldens()[position];
        var result = await CallTool(ToolArguments(golden, "json"));
        Assert.False(result.IsError, $"{label}: {result.Content[0].Text}");

        using var envelope = JsonDocument.Parse(result.Content[0].Text!);
        var data = envelope.RootElement.GetProperty("data");

        IEnumerable<string> rendered = golden.Facet switch
        {
            "callers" or "callees" => data.GetProperty("declarations").EnumerateArray().Select(Position),
            "impact" => data.GetProperty("affected").EnumerateArray().Select(Position),
            "impact-effects" => data.GetProperty("impacts").EnumerateArray().Select(impact =>
                $"{Position(impact.GetProperty("declaration"))}:{impact.GetProperty("verdict").GetString()}"),
            "effects" => data.GetProperty("rows").EnumerateArray().Select(RenderEffectRow),
            _ => throw new InvalidOperationException(golden.Facet),
        };

        Assert.Equal(
            golden.Expect.OrderBy(entry => entry, StringComparer.Ordinal).ToArray(),
            rendered.OrderBy(entry => entry, StringComparer.Ordinal).ToArray());
        Assert.Equal(golden.Partial, data.GetProperty("partial").GetBoolean());
    }

    /// <summary>
    /// Byte-for-byte: the tool's text is the CLI's stdout, and the tool's JSON
    /// is the CLI's <c>--json</c> stdout, for every golden — both read the
    /// index the CLI built, neither rebuilds it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ToolGoldens))]
    public async Task ToolOutputIsByteIdenticalToTheCli(int position, string label)
    {
        var golden = LoadGoldens()[position];

        var cliText = CliTestHarness.RunCli(_corpus.Directory, CliArguments(golden, json: false));
        Assert.True(cliText.ExitCode == 0, $"{label}: {cliText.StdOut}{cliText.StdErr}");
        var toolText = await CallTool(ToolArguments(golden, "text"));
        Assert.False(toolText.IsError, label);
        Assert.Equal(cliText.StdOut, toolText.Content[0].Text);

        var cliJson = CliTestHarness.RunCli(_corpus.Directory, CliArguments(golden, json: true));
        Assert.True(cliJson.ExitCode == 0, $"{label}: {cliJson.StdOut}{cliJson.StdErr}");
        var toolJson = await CallTool(ToolArguments(golden, "json"));
        Assert.False(toolJson.IsError, label);
        Assert.Equal(cliJson.StdOut, toolJson.Content[0].Text);

        Assert.Equal(_corpus.IndexBytes, File.ReadAllBytes(_corpus.IndexPath));
    }

    /// <summary>
    /// The discriminating pin (§3.1 E7): the tool answers from the index FILE.
    /// The file's record for <c>Whisper</c> — the cross-module fold — is
    /// rewritten with a sentinel the source could never produce; the header is
    /// untouched, so the index is still fresh and a reader honouring the
    /// file returns the sentinel. A tool that rebuilt an in-memory graph
    /// from the sources would return the real row and fail here, with or
    /// without <c>noBuild</c>.
    /// </summary>
    [Fact]
    public async Task AnswersComeFromTheIndexFile_NotFromAnInMemoryGraph()
    {
        var output = IndexCommand.DefaultOutputDirectory(_corpus.Directory);
        try
        {
            var (index, status) = ProjectIndex.Load(output);
            Assert.Equal(ProjectIndex.Freshness.Fresh, status);
            var whisper = Assert.Single(index!.FindDeclarations("Whisper"));
            var own = index.FindEffectRow(whisper.SymbolId);
            Assert.NotNull(own);
            Assert.Equal("cw", own!.InferredRow!.Display);
            own.InferredRow.Display = "SENTINEL-FROM-THE-FILE";
            index.Save(output);
            var tampered = File.ReadAllBytes(_corpus.IndexPath);

            foreach (var noBuild in new[] { true, false })
            {
                var arguments = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["projectDirectory"] = _corpus.Directory,
                    ["facet"] = "effects",
                    ["symbol"] = "Whisper",
                    ["noBuild"] = noBuild,
                    ["format"] = "text",
                });
                var result = await CallTool(arguments);
                Assert.False(result.IsError, result.Content[0].Text);
                Assert.Contains("    inferred: SENTINEL-FROM-THE-FILE", result.Content[0].Text);
                Assert.Equal(tampered, File.ReadAllBytes(_corpus.IndexPath));
            }
        }
        finally
        {
            File.WriteAllBytes(_corpus.IndexPath, _corpus.IndexBytes);
        }
    }

    /// <summary>
    /// The leg's denominator cannot shrink quietly: E5 registered eight
    /// <c>effects</c> rows and three <c>impact-effects</c> rows (the "ten
    /// effects goldens" of roadmap §5 item 7, as they stand in the corpus),
    /// and the cross-module fold is among them.
    /// </summary>
    [Fact]
    public void TheLegCoversEveryEffectsGolden()
    {
        var goldens = LoadGoldens();
        var effects = goldens.Where(golden => golden.Facet == "effects").ToArray();
        var blast = goldens.Where(golden => golden.Facet == "impact-effects").ToArray();
        Assert.Equal(8, effects.Length);
        Assert.Equal(3, blast.Length);
        Assert.Contains(effects, golden => golden.Name == "Whisper");
        Assert.Contains(effects, golden => golden.Name == "Map");
        Assert.Contains(effects, golden => golden.Name == "Twice");
        Assert.Equal(21, ToolGoldens().Count());

        // And the facets the tool does not answer are exactly the ones the
        // roadmap left to the CLI — none of them carries an effects row.
        var others = goldens.Where(golden => !ToolFacets.Contains(golden.Facet, StringComparer.Ordinal))
            .Select(golden => golden.Facet).Distinct().OrderBy(facet => facet, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "assumptions", "contracts", "impact-file", "symbol" }, others);
    }

    // --- rendering in the golden's vocabulary --------------------------------

    private static string Position(JsonElement declaration) =>
        $"{declaration.GetProperty("file").GetString()}:{declaration.GetProperty("line").GetInt32()}:{declaration.GetProperty("name").GetString()}";

    private static string RenderEffectRow(JsonElement row)
    {
        var file = row.GetProperty("file").GetString();
        var line = row.GetProperty("line").GetInt32();
        var name = row.GetProperty("name").GetString();
        var kind = row.GetProperty("kind").GetString();
        var declared = row.GetProperty("declaredRow").GetProperty("display").GetString();
        var isOwn = row.GetProperty("ownerSymbolId").GetString()!.Length == 0
            && kind is not ("parameter" or "return");
        if (isOwn)
        {
            var inferred = row.TryGetProperty("inferredRow", out var inferredRow) && inferredRow.ValueKind == JsonValueKind.Object
                ? inferredRow.GetProperty("display").GetString()
                : "none";
            var code = row.TryGetProperty("diagnosticCode", out var diagnosticCode) && diagnosticCode.ValueKind == JsonValueKind.String
                ? diagnosticCode.GetString()
                : "none";
            var forbidden = string.Join(",", row.GetProperty("forbidden").EnumerateArray().Select(e => e.GetString()));
            return $"{file}:{line}:{name}:written={(row.GetProperty("declared").GetBoolean() ? "true" : "false")};"
                + $"declared={declared};inferred={inferred};verdict={row.GetProperty("verdict").GetString()};"
                + $"code={code};undeclared={forbidden}";
        }

        var bound = row.TryGetProperty("boundRow", out var boundRow) && boundRow.ValueKind == JsonValueKind.String
            ? boundRow.GetString()
            : "none";
        return $"{file}:{line}:{name}:position={kind};declared={declared};bound={bound}";
    }

    private static IReadOnlyList<GoldenQuery> LoadGoldens()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(CorpusRoot, "expected.json")));
        return document.RootElement.GetProperty("queries")
            .EnumerateArray()
            .Select(entry => new GoldenQuery(
                entry.GetProperty("facet").GetString()!,
                entry.GetProperty("name").GetString()!,
                entry.TryGetProperty("inFile", out var file) && file.ValueKind != JsonValueKind.Null
                    ? file.GetString()
                    : null,
                entry.GetProperty("expect").EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToArray(),
                entry.GetProperty("partial").GetBoolean(),
                entry.TryGetProperty("row", out var row) && row.ValueKind == JsonValueKind.String
                    ? row.GetString()
                    : null))
            .ToArray();
    }

    private static string CorpusRoot =>
        Path.Combine(CliTestHarness.FindRepoRoot(), "tests", "TestData", "QueryCorpus");

    private static string FixtureRoot => Path.Combine(CorpusRoot, "project");
}
