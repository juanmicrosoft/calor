using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Mcp;
using Calor.Compiler.Tests.EvidenceContract;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1429 (0.25 W1): the CLI and MCP transcripts behind the interoperability guide. Each fixture in
/// <c>docs/plans/evidence/v0.25-w1-1429/fixtures/</c> is converted with <c>calor convert</c> (text and
/// JSON), the CLI output is compiled with default options, the fixture is migrated with
/// <c>calor migrate</c>, and it is converted with MCP <c>calor_convert</c> with and without
/// <c>passthroughOnError</c>. The observation must equal the committed transcript in
/// <c>transcripts/</c>; regenerate with <c>CALOR_REGENERATE_W1_TRANSCRIPTS=1</c> (a changed
/// transcript is a review decision, and the pages that quote it must change with it). A website
/// fence preceded by <c>{/* w1-transcript: ID */}</c> must equal that fixture's CLI output.
/// </summary>
public sealed class W1GuideTranscriptTests : IDisposable
{
    public const string Dir = "docs/plans/evidence/v0.25-w1-1429";

    /// <summary>Fixture id and the C# file name it is converted as (the CLI derives the module name from it).</summary>
    public static readonly (string Id, string File)[] Fixtures =
    [
        ("W1-F1-refout", "Inventory.cs"),
        ("W1-F1-outvar-statement", "Parser.cs"),
        ("W1-F1-ref-return-expression", "Cells.cs"),
        ("W1-F1-notnullwhen", "Names.cs"),
        ("W1-F2-interpolation", "Report.cs"),
        ("W1-F3-local-functions", "Shapes.cs"),
        ("W1-F4-iterator-accessor", "Countdown.cs"),
        ("W1-F5-arrays", "Grid.cs"),
        ("W1-F6-rescue", "Counter.cs"),
    ];

    private static readonly bool Regenerate = Environment.GetEnvironmentVariable("CALOR_REGENERATE_W1_TRANSCRIPTS") == "1";
    /// <summary><c>w1-transcript</c>: the fence is the whole CLI output; <c>w1-transcript-lines</c>: the fence's lines,
    /// with their indentation, occur in the CLI output in the same order.</summary>
    private static readonly Regex Marker = new(@"\{/\* w1-transcript(?<lines>-lines)?: (?<id>[A-Za-z0-9-]+) \*/\}\s*\n```calor[^\n]*\n(?<body>.*?)\n```", RegexOptions.Singleline);

    private readonly string _temp = Directory.CreateTempSubdirectory("calor-w1-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string> Ids()
    {
        var data = new TheoryData<string>();
        foreach (var (id, _) in Fixtures)
            data.Add(id);
        return data;
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task Transcript_MatchesTheCommittedFixture(string id) => Compare(id, await ObserveAsync(id));

    /// <summary>
    /// W1-F6-project: a two-file project (W1-F1-refout, native; W1-F6-rescue, needs a round-trip
    /// rescue). Project surfaces never rescue a round-trip failure, so the lossless project is refused.
    /// </summary>
    [Fact]
    public async Task ProjectTranscript_MatchesTheCommittedFixture()
    {
        var root = EvidenceContractTests.RepoRoot();
        string Write(string dir)
        {
            File.WriteAllText(Path.Combine(dir, "Inventory.cs"), File.ReadAllText(Path.Combine(root, Dir, "fixtures", "W1-F1-refout.cs.txt")));
            File.WriteAllText(Path.Combine(dir, "Counter.cs"), File.ReadAllText(Path.Combine(root, Dir, "fixtures", "W1-F6-rescue.cs.txt")));
            return dir;
        }
        // Issue entries are reduced to severity, code and feature (messages carry temp paths).
        JsonArray Issues(params JsonNode?[] lists) => new(lists.SelectMany(l => l?.AsArray() ?? [])
            .Select(i => $"{i!["severity"]?.GetValue<string>()}:{i["code"]?.GetValue<string>()}:{Feature(i["message"]?.GetValue<string>())}")
            .Distinct().Order(StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray());
        JsonArray PerFile(JsonNode? files, string pathKey, string nameKey, params string[] issueKeys) => new((files?.AsArray() ?? [])
            .OrderBy(f => Path.GetFileName(f![nameKey]?.GetValue<string>() ?? ""), StringComparer.Ordinal)
            .Select(f => (JsonNode)new JsonObject
            {
                ["file"] = Path.GetFileName(f![nameKey]?.GetValue<string>()),
                ["conversionPaths"] = f[pathKey]?.DeepClone(),
                ["issues"] = Issues(issueKeys.Select(k => f[k]).ToArray()),
            }).ToArray());

        var result = new JsonObject();
        var migrateDir = Write(NewDir("migrate"));
        var report = Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".json");
        var (exit, _, _) = CliTestHarness.RunCli(migrateDir, IsolatedHome(), "migrate", migrateDir, "--skip-verify", "--skip-analyze", "--report", report);
        var written = Directory.EnumerateFiles(migrateDir, "*.calr", SearchOption.AllDirectories).Count();
        result["cli-migrate"] = new JsonObject
        {
            ["command"] = "calor migrate <dir> --skip-verify --skip-analyze --report <report.json>",
            ["exit"] = exit,
            ["calrFilesWritten"] = written,
            ["files"] = PerFile(JsonNode.Parse(File.ReadAllText(report))?["data"]?["fileResults"], "conversionPaths", "sourcePath"),
        };
        var (migrateError, migrate) = await Mcp("calor_migrate", new { projectPath = Write(NewDir("mcp-migrate")), phase = "convert" });
        result["mcp-migrate"] = new JsonObject
        {
            ["arguments"] = "{ projectPath, phase: \"convert\" }",
            ["isError"] = migrateError,
            ["files"] = PerFile(migrate["perFile"], "conversionPaths", "path", "errors", "warnings"),
        };
        var (batchError, batch) = await Mcp("calor_batch", new { action = "convert", projectPath = Write(NewDir("mcp-batch")) });
        result["mcp-batch"] = new JsonObject
        {
            ["arguments"] = "{ action: \"convert\", projectPath }",
            ["isError"] = batchError,
            ["failedFiles"] = batch["summary"]?["failedFiles"]?.DeepClone(),
            ["files"] = PerFile(batch["files"], "conversionPaths", "sourcePath", "issues"),
        };
        Compare("W1-F6-project", result);
    }

    /// <summary>
    /// The per-family W1 records: the six families in order, status equal to the scope's readiness
    /// (W1 never records READY), pages and anchors that exist, transcripts that exist, and a non-empty
    /// list of what READY still needs.
    /// </summary>
    [Fact]
    public void FamilyRecords_AreConsistentWithTheScopeAndThePages()
    {
        var root = EvidenceContractTests.RepoRoot();
        var records = JsonNode.Parse(File.ReadAllText(Path.Combine(root, Dir, "records.json")))!;
        var scope = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/plans/evidence/v0.25-r0-1426/scope.json")))!;
        Assert.Equal(scope["scopeVersion"]!.GetValue<string>(), records["scopeVersion"]!.GetValue<string>());
        Assert.Equal("reduced", records["independence"]!.GetValue<string>());
        var families = records["families"]!.AsArray();
        Assert.Equal(["F1", "F2", "F3", "F4", "F5", "F6"], families.Select(f => f!["family"]!.GetValue<string>()));
        foreach (var family in families)
        {
            var id = family!["family"]!.GetValue<string>();
            var readiness = scope["families"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == id)!["readiness"]!["status"]!.GetValue<string>();
            var status = family["status"]!.GetValue<string>();
            Assert.NotEqual("READY", status);
            Assert.Equal(readiness, status);
            Assert.NotEmpty(family["readyNeeds"]!.AsArray());
            foreach (var transcript in family["transcripts"]!.AsArray())
                Assert.True(File.Exists(Path.Combine(root, Dir, "transcripts", transcript!.GetValue<string>() + ".json")), $"{id}: {transcript}");
            foreach (var page in family["pages"]!.AsArray().Select(p => p!.GetValue<string>()))
            {
                var parts = page.Split('#');
                var path = Path.Combine(root, parts[0]);
                Assert.True(File.Exists(path), $"{id}: {page}");
                if (parts.Length == 2)
                {
                    // The site's heading ids (website/src/lib/headings.ts): lower-case, non-alphanumeric runs to '-'.
                    var slugs = Regex.Matches(File.ReadAllText(path), @"^#{2,4} (.+)$", RegexOptions.Multiline)
                        .Select(m => Regex.Replace(Regex.Replace(m.Groups[1].Value.Replace("`", "").ToLowerInvariant(), "[^a-z0-9]+", "-"), "(^-|-$)", ""));
                    Assert.Contains(parts[1], slugs);
                }
            }
        }
        foreach (var page in records["sharedPages"]!.AsArray())
            Assert.True(File.Exists(Path.Combine(root, page!.GetValue<string>())), page!.GetValue<string>());
    }

    private static void Compare(string id, JsonObject observed)
    {
        var path = Path.Combine(EvidenceContractTests.RepoRoot(), Dir, "transcripts", id + ".json");
        if (Regenerate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, observed.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"{id} has no committed transcript; regenerate with CALOR_REGENERATE_W1_TRANSCRIPTS=1");
        var committed = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var (surface, cell) in observed.AsObject())
            Assert.True(JsonNode.DeepEquals(committed[surface], cell),
                $"{id} {surface} changed: committed {committed[surface]?.ToJsonString()}, observed {cell?.ToJsonString()}");
        Assert.Equal(observed.AsObject().Count, committed.AsObject().Count);
    }

    private static readonly Regex SourceMarker = new(@"\{/\* w1-source: (?<id>[A-Za-z0-9-]+) \*/\}\s*\n```csharp[^\n]*\n(?<body>.*?)\n```", RegexOptions.Singleline);

    /// <summary>
    /// Problems with the C# a page shows as a fixture's input (EXC-W1-VERBATIM). A marked <c>csharp</c> fence
    /// must occur verbatim, as one contiguous substring, in the fixture. The only normalization is CRLF to LF and
    /// trimming newlines at the fence's two ends: no comment stripping, no blank-line splitting, no whitespace
    /// changes, and no elision: a shown block is never allowed to skip fixture text.
    /// </summary>
    private static List<string> SourceExcerptProblems(string page, string text, Func<string, string?> fixture, HashSet<string> seen)
    {
        var problems = new List<string>();
        foreach (Match m in SourceMarker.Matches(text.Replace("\r\n", "\n")))
        {
            var id = m.Groups["id"].Value;
            if (fixture(id) is not { } source)
            {
                problems.Add($"{page}: unknown fixture {id}");
                continue;
            }
            seen.Add(id);
            var haystack = source.Replace("\r\n", "\n");
            var block = m.Groups["body"].Value.Trim('\n');
            if (block.Length == 0 || !haystack.Contains(block, StringComparison.Ordinal))
                problems.Add($"{page}: a {id} block is not a verbatim substring of the fixture: '{block.Split('\n')[0]}'");
        }
        return problems;
    }

    /// <summary>The C# a page presents as the input of a quoted conversion is the fixture that produced it.</summary>
    [Fact]
    public void WebsiteSources_AreExcerptsOfTheFixtures()
    {
        var root = EvidenceContractTests.RepoRoot();
        string? Fixture(string id) => Path.Combine(root, Dir, "fixtures", id + ".cs.txt") is var f && File.Exists(f) ? File.ReadAllText(f) : null;
        var seen = new HashSet<string>();
        var problems = new List<string>();
        foreach (var page in Directory.EnumerateFiles(Path.Combine(root, "website", "content"), "*.mdx", SearchOption.AllDirectories))
            problems.AddRange(SourceExcerptProblems(Path.GetFileName(page), File.ReadAllText(page), Fixture, seen));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        foreach (var required in new[] { "W1-F1-refout", "W1-F2-interpolation", "W1-F3-local-functions", "W1-F5-arrays" })
            Assert.True(seen.Contains(required), $"the guide no longer marks the {required} C# input");

        // Negative controls (EXC-W1-VERBATIM): each edit to the shown C# alone must be caught.
        var guide = File.ReadAllText(Path.Combine(root, "website", "content", "guides", "csharp-interoperability.mdx"));
        var normalized = guide.Replace("\r\n", "\n");
        const string bump = "    public static void Bump(ref int x)\n    {\n        x = x + 1;\n    }\n";
        Assert.Contains(bump, normalized);
        Assert.Contains("        int total = 1;\n", normalized);
        Assert.Contains("    public static int Twice(in int x) => x * 2;\n", normalized);
        var edits = new Dictionary<string, string>
        {
            ["deleted statement"] = normalized.Replace(bump, "    public static void Bump(ref int x)\n    {\n    }\n"),
            ["commented-out line"] = normalized.Replace(bump, "    public static void Bump(ref int x)\n    {\n        // x = x + 1;\n    }\n"),
            ["changed line"] = normalized.Replace("    public static int Twice(in int x) => x * 2;\n", "    public static int Twice(in int x) => x * 3;\n"),
            ["added line"] = normalized.Replace("        int total = 1;\n", "        int total = 1;\n        total = 0;\n"),
            ["indented elision"] = normalized.Replace(bump, "    public static void Bump(ref int x)\n    {\n        // ...\n    }\n"),
            ["exact elision"] = normalized.Replace(bump, "    public static void Bump(ref int x)\n    {\n// ...\n    }\n"),
        };
        foreach (var (kind, mutated) in edits)
        {
            Assert.NotEqual(normalized, mutated);
            Assert.True(SourceExcerptProblems(kind, mutated, Fixture, []).Count > 0, $"a {kind} in the shown C# was not caught");
        }
    }

    /// <summary>Converted output shown on a website page is the CLI's actual output for the named fixture.</summary>
    [Fact]
    public void WebsiteFences_QuoteTheCliOutput()
    {
        var root = EvidenceContractTests.RepoRoot();
        var seen = new HashSet<string>();
        foreach (var page in Directory.EnumerateFiles(Path.Combine(root, "website", "content"), "*.mdx", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(page).Replace("\r\n", "\n");
            foreach (Match m in Marker.Matches(text))
            {
                var id = m.Groups["id"].Value;
                var transcript = Path.Combine(root, Dir, "transcripts", id + ".json");
                Assert.True(File.Exists(transcript), $"{Path.GetFileName(page)} names unknown transcript {id}");
                var calor = JsonNode.Parse(File.ReadAllText(transcript))!["cli-convert"]!["calor"]!.GetValue<string>();
                if (m.Groups["lines"].Success)
                {
                    var output = calor.Split('\n').Select(l => l.TrimEnd()).ToList();
                    var at = 0;
                    foreach (var line in m.Groups["body"].Value.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0))
                    {
                        var next = output.FindIndex(at, l => l == line);
                        Assert.True(next >= 0, $"{Path.GetFileName(page)}: '{line}' is not a later line of the {id} CLI output");
                        at = next + 1;
                    }
                }
                else
                {
                    Assert.Equal(calor.TrimEnd(), m.Groups["body"].Value.TrimEnd());
                }
                seen.Add(id);
            }
        }
        // The guide quotes these converted outputs; dropping a marker silently would leave an unchecked fence.
        foreach (var required in new[] { "W1-F1-refout", "W1-F2-interpolation", "W1-F3-local-functions", "W1-F4-iterator-accessor", "W1-F5-arrays" })
            Assert.True(seen.Contains(required), $"the guide no longer marks the {required} fence");
    }

    private async Task<JsonObject> ObserveAsync(string id)
    {
        var file = Fixtures.Single(f => f.Id == id).File;
        var source = File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), Dir, "fixtures", id + ".cs.txt"));
        var name = Path.GetFileNameWithoutExtension(file);
        var result = new JsonObject();

        // calor convert, text: the summary lines a reader sees (paths in loss lines are reduced to the file name,
        // because the CLI shortens a long line and the temp path length varies).
        var dir = NewDir("cli");
        File.WriteAllText(Path.Combine(dir, file), source);
        var (exit, stdout, stderr) = CliTestHarness.RunCli(dir, IsolatedHome(), "convert", file, "-o", name + ".calr");
        var calrPath = Path.Combine(dir, name + ".calr");
        var calor = File.Exists(calrPath) ? File.ReadAllText(calrPath).Replace("\r\n", "\n") : null;
        result["cli-convert"] = new JsonObject
        {
            ["command"] = $"calor convert {file} -o {name}.calr",
            ["exit"] = exit,
            ["stdout"] = new JsonArray(TextLines(stdout, dir).Select(l => (JsonNode)l).ToArray()),
            ["stderr"] = new JsonArray(TextLines(stderr, dir).Select(l => (JsonNode)l).ToArray()),
            ["calor"] = calor,
        };

        // calor convert --format json: the envelope fields the pages describe.
        var jsonDir = NewDir("json");
        File.WriteAllText(Path.Combine(jsonDir, file), source);
        var (jsonExit, json, _) = CliTestHarness.RunCli(jsonDir, IsolatedHome(), "convert", file, "-o", name + ".calr", "--format", "json");
        var envelope = JsonNode.Parse(json)!;
        var data = envelope["data"]!;
        result["cli-convert-json"] = new JsonObject
        {
            ["command"] = $"calor convert {file} -o {name}.calr --format json",
            ["exit"] = jsonExit,
            ["success"] = data["success"]?.DeepClone(),
            ["nativeConversionCount"] = data["nativeConversionCount"]?.DeepClone(),
            ["interopPreservationCount"] = data["interopPreservationCount"]?.DeepClone(),
            ["lossCount"] = data["lossCount"]?.DeepClone(),
            ["losses"] = new JsonArray((data["losses"]?.AsArray() ?? []).Select(l => (JsonNode)new JsonObject
            {
                ["feature"] = l!["feature"]?.DeepClone(),
                ["path"] = l["path"]?.DeepClone(),
                ["trigger"] = l["trigger"]?.DeepClone(),
                ["enabledBy"] = l["enabledBy"]?.DeepClone(),
            }).ToArray()),
            ["conversionPaths"] = data["conversionPaths"]?.DeepClone(),
            ["diagnosticCodes"] = Codes(envelope["diagnostics"]),
        };
        if (id == "W1-F6-rescue")
        {
            // --verbose prints converter progress to stdout, so stdout is not one JSON document (documented on cli/convert).
            var verboseDir = NewDir("verbose");
            File.WriteAllText(Path.Combine(verboseDir, file), source);
            var (_, verboseOut, _) = CliTestHarness.RunCli(verboseDir, IsolatedHome(), "convert", file, "-o", name + ".calr", "--format", "json", "--verbose");
            bool parses;
            try { parses = JsonNode.Parse(verboseOut) is not null; }
            catch (JsonException) { parses = false; }
            result["cli-convert-json-verbose"] = new JsonObject
            {
                ["command"] = $"calor convert {file} -o {name}.calr --format json --verbose",
                ["stdoutIsOneJsonDocument"] = parses,
                ["firstStdoutLine"] = verboseOut.Replace("\r\n", "\n").Split('\n')[0],
            };
        }

        // The CLI output, compiled with default options.
        if (calor is not null)
        {
            var (compileExit, compileOut, _) = CliTestHarness.RunCli(dir, IsolatedHome(), "--input", name + ".calr", "--output", name + ".g.cs", "--format", "json");
            var diagnostics = JsonNode.Parse(compileOut)?["diagnostics"];
            result["cli-compile"] = new JsonObject
            {
                ["command"] = $"calor --input {name}.calr --output {name}.g.cs",
                ["exit"] = compileExit,
                ["errorCodes"] = Codes(diagnostics, "error"),
                ["warningCodes"] = Codes(diagnostics, "warning"),
            };
        }

        // calor migrate on a one-file project.
        var migrateDir = NewDir("migrate");
        File.WriteAllText(Path.Combine(migrateDir, file), source);
        var report = Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".json");
        var (migrateExit, _, _) = CliTestHarness.RunCli(migrateDir, IsolatedHome(), "migrate", migrateDir, "--skip-verify", "--skip-analyze", "--report", report);
        var fileResult = File.Exists(report) ? JsonNode.Parse(File.ReadAllText(report))?["data"]?["fileResults"]?[0] : null;
        result["cli-migrate"] = new JsonObject
        {
            ["command"] = "calor migrate <dir> --skip-verify --skip-analyze --report <report.json>",
            ["exit"] = migrateExit,
            ["conversionPaths"] = fileResult?["conversionPaths"]?.DeepClone(),
        };

        // MCP calor_convert, default arguments and passthroughOnError.
        foreach (var passthrough in new[] { false, true })
        {
            var (isError, payload) = await Mcp("calor_convert", passthrough ? new { source, passthroughOnError = true } : new { source });
            var summary = payload["lossSummary"];
            result[passthrough ? "mcp-convert-passthroughOnError" : "mcp-convert"] = new JsonObject
            {
                ["arguments"] = passthrough ? "{ source, passthroughOnError: true }" : "{ source }",
                ["isError"] = isError,
                ["success"] = payload["success"]?.DeepClone(),
                ["nativeConversions"] = summary?["nativeConversions"]?.DeepClone(),
                ["interopPreservations"] = summary?["interopPreservations"]?.DeepClone(),
                ["locations"] = new JsonArray((summary?["locations"]?.AsArray() ?? []).Select(l => (JsonNode)new JsonObject
                {
                    ["feature"] = l!["feature"]?.DeepClone(),
                    ["path"] = l["path"]?.DeepClone(),
                    ["trigger"] = l["trigger"]?.DeepClone(),
                    ["enabledBy"] = l["enabledBy"]?.DeepClone(),
                }).ToArray()),
                ["paths"] = summary?["paths"]?.DeepClone(),
                ["issueCodes"] = new JsonArray((payload["issues"]?.AsArray() ?? [])
                    .Select(i => $"{i!["severity"]?.GetValue<string>()}:{i["code"]?.GetValue<string>()}:{Feature(i["message"]?.GetValue<string>())}")
                    .Distinct().Order(StringComparer.Ordinal).Select(s => (JsonNode)s).ToArray()),
                ["calorSource"] = payload["calorSource"]?.DeepClone(),
            };
        }
        return result;
    }

    private string NewDir(string kind)
    {
        var dir = Path.Combine(_temp, kind + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Non-empty lines; a line naming the temp directory keeps only the file name and the bracketed tags.</summary>
    private static IEnumerable<string> TextLines(string text, string dir)
    {
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
                continue;
            // macOS reports /var temp directories as /private/var.
            var at = line.IndexOf("/private" + dir, StringComparison.Ordinal) is var p and >= 0 ? p : line.IndexOf(dir, StringComparison.Ordinal);
            if (at < 0)
            {
                yield return line;
                continue;
            }
            var end = line.IndexOf(dir, at, StringComparison.Ordinal) + dir.Length;
            var rest = line[end..].TrimStart('/', '\\');
            var tags = Regex.Match(rest, @"^(?<file>[^ ]+)(?<tags>( \[[^\]]+\])*)");
            yield return line[..at] + "<dir>/" + tags.Groups["file"].Value + tags.Groups["tags"].Value;
        }
    }

    private static string? Feature(string? message) =>
        message is not null && Regex.Match(message, @"^\[(?<f>[^\]]+)\]") is { Success: true } m ? m.Groups["f"].Value : null;

    /// <summary>Each code with its count (<c>Calor0419 x1</c>), so a quoted count is pinned too.</summary>
    private static JsonArray Codes(JsonNode? diagnostics, string? severity = null) =>
        new((diagnostics?.AsArray() ?? [])
            .Where(d => severity is null || d!["severity"]?.GetValue<string>() == severity)
            .GroupBy(d => d!["code"]!.GetValue<string>()).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (JsonNode)$"{g.Key} x{g.Count()}").ToArray());

    /// <summary>CLI children get their own home, so no user manifest or cache under ~/.calor applies.</summary>
    private IReadOnlyDictionary<string, string> IsolatedHome()
    {
        var home = Path.Combine(_temp, "home");
        Directory.CreateDirectory(home);
        return new Dictionary<string, string> { ["HOME"] = home, ["USERPROFILE"] = home };
    }

    private static async Task<(bool IsError, JsonNode Payload)> Mcp(string tool, object arguments)
    {
        // In-process conversion reads ~/.calor/manifests; fail closed rather than depend on a user's overrides.
        var userManifests = Effects.Manifests.ManifestLoader.GetUserManifestsDirectory();
        Assert.False(userManifests is not null && Directory.Exists(userManifests)
            && Directory.EnumerateFiles(userManifests, "*.calor-effects.json").Any(),
            $"user effect manifests under {userManifests} would change in-process conversion; run with an isolated HOME");
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("1").RootElement,
            Method = "tools/call",
            Params = JsonSerializer.SerializeToElement(new { name = tool, arguments }),
        };
        var response = await new McpMessageHandler().HandleRequestAsync(request);
        var result = JsonSerializer.SerializeToNode(response!.Result, McpJsonOptions.Default)!;
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        JsonNode payload;
        try { payload = JsonNode.Parse(text) ?? new JsonObject(); }
        catch (JsonException) { payload = new JsonObject { ["text"] = text }; }
        return (result["isError"]?.GetValue<bool>() == true, payload);
    }
}
