using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Indexing;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

/// <summary>
/// v0.16 gate 3, MCP leg (roadmap-v0.16 §5 item 3; R:889-905): the edit-script
/// corpus (<c>tests/TestData/EditScripts/ES-01…ES-07</c>) compiled through
/// MCP yields the same canonical diagnostics and the same index bytes as the
/// CLI path. Two instruments, each pinning exactly one claim:
///
/// <list type="bullet">
/// <item><b>Diagnostics:</b> <c>calor_compile</c> with <c>options.crossModule</c>
/// (the file set as one project) vs the <c>calor</c> PROCESS run as
/// <c>calor -i … --format json</c> with the step's option profile —
/// canonical <c>file|code|severity|line|column|message</c>, sorted.</item>
/// <item><b>Index bytes:</b> the index <c>calor_query</c> writes when it resolves a
/// missing index vs the file the <c>calor index build</c> PROCESS writes —
/// byte-for-byte, header included, on the same workspace.</item>
/// </list>
///
/// What this does NOT claim: that <c>calor_compile</c> writes an index (it
/// does not, and neither does <c>calor -i</c>); the index leg goes through
/// the surface that does build one on both sides.
/// </summary>
[Collection("McpSerial")]
public sealed class McpCompileGateTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    public static TheoryData<string> RegisteredScripts()
    {
        var data = new TheoryData<string>();
        foreach (var directory in EnumerateScriptDirectories())
            data.Add(Path.GetFileName(directory));
        return data;
    }

    [Fact]
    public void TheDenominatorIsTheWholeCorpus()
    {
        // Seven scripts, the same seven EditScriptIdentityTests pins; a script
        // dropped here would have to edit this list in the diff.
        Assert.Equal(
            new[]
            {
                "ES-01-local-edit",
                "ES-02-add-file",
                "ES-03-delete-file",
                "ES-04-cross-module-effect",
                "ES-05-options-flip",
                "ES-06-touch-noop",
                "ES-07-persistent-finding",
            },
            EnumerateScriptDirectories().Select(Path.GetFileName).ToArray());
    }

    [Theory]
    [MemberData(nameof(RegisteredScripts))]
    public async Task DiagnosticsThroughCalorCompileMatchTheCliProcess(string scriptName)
    {
        var script = LoadScript(scriptName);
        var anyDiagnostics = false;
        foreach (var step in script.Steps)
        {
            var workspace = CreateTempDir();
            SyncWorkspace(workspace, step.SourceDirectory);
            var sources = Directory.GetFiles(workspace, "*.calr")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            // MCP first: the CLI writes .g.cs beside the sources, and the MCP
            // path must be judged on the pristine step, not on leftovers.
            var mcp = await CompileThroughMcp(workspace, step.Options);
            var cli = CompileThroughCliProcess(workspace, sources, step.Options);

            Assert.Equal(cli, mcp);
            anyDiagnostics |= cli.Count > 0;
        }

        // Anti-vacuity per script: ES-06 is the deliberate exception (nothing
        // ever changes and nothing is ever wrong there is not what it pins).
        if (scriptName != "ES-06-touch-noop")
            Assert.True(anyDiagnostics, $"{scriptName}: no step produced a diagnostic on either path; the comparison is vacuous");
    }

    [Theory]
    [MemberData(nameof(RegisteredScripts))]
    public async Task IndexBytesThroughCalorQueryMatchCalorIndexBuild(string scriptName)
    {
        var script = LoadScript(scriptName);
        foreach (var step in script.Steps)
        {
            var workspace = CreateTempDir();
            SyncWorkspace(workspace, step.SourceDirectory);
            var indexPath = ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(workspace));

            var build = CliTestHarness.RunCli(workspace, "index", "build", workspace);
            Assert.True(build.ExitCode == 0, build.StdOut + build.StdErr);
            var cliBytes = File.ReadAllBytes(indexPath);
            var (built, _) = ProjectIndex.Load(IndexCommand.DefaultOutputDirectory(workspace));
            Assert.NotNull(built);
            var first = built!.Declarations.First();
            File.Delete(indexPath);

            var result = await CallTool("calor_query", JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["projectDirectory"] = workspace,
                ["facet"] = "callers",
                ["symbol"] = first.Name,
                ["inFile"] = first.File,
            }));
            Assert.False(result.IsError, result.Content[0].Text);
            Assert.True(File.Exists(indexPath), "calor_query did not rebuild the missing index");

            Assert.Equal(cliBytes, File.ReadAllBytes(indexPath));
        }
    }

    // --- calor_compile's project mode, on its own ---------------------------

    /// <summary>
    /// The option's discriminating pin: ES-04's violating step has a caller
    /// whose declared row the callee's new effects do not fit. Per-file batch
    /// mode (the pre-E7 behaviour, kept as the default) cannot see it; project
    /// mode reports the CLI's Calor0410.
    /// </summary>
    [Fact]
    public async Task CrossModule_ReportsWhatPerFileBatchModeCannot()
    {
        var workspace = CreateTempDir();
        SyncWorkspace(workspace, Path.Combine(CorpusRoot, "ES-04-cross-module-effect", "step-01-callee-gains-effect"));

        var perFile = await CallTool("calor_compile", JsonSerializer.Serialize(new { projectPath = workspace }));
        using var lenient = JsonDocument.Parse(perFile.Content[0].Text!);
        Assert.False(lenient.RootElement.TryGetProperty("crossModule", out _));
        // Alone, caller.calr cannot resolve SaveOrder at all — a binder error,
        // never the effect verdict the project as a whole carries.
        Assert.DoesNotContain("Calor0410", perFile.Content[0].Text);

        var project = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = workspace,
            options = new { crossModule = true },
        }));
        using var strict = JsonDocument.Parse(project.Content[0].Text!);
        Assert.True(strict.RootElement.GetProperty("crossModule").GetBoolean());
        Assert.False(strict.RootElement.GetProperty("success").GetBoolean());
        Assert.True(project.IsError);
        var caller = strict.RootElement.GetProperty("files").EnumerateArray()
            .Single(file => file.GetProperty("filePath").GetString()!.EndsWith("caller.calr", StringComparison.Ordinal));
        Assert.False(caller.GetProperty("success").GetBoolean());
        Assert.Contains(
            caller.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "Calor0410");
        Assert.Equal(1, strict.RootElement.GetProperty("errorCategories").GetProperty("Calor0410").GetInt32());
    }

    [Fact]
    public async Task PerFileBatchMode_NowCarriesEnvelopeDiagnostics()
    {
        var workspace = CreateTempDir();
        SyncWorkspace(workspace, Path.Combine(CorpusRoot, "ES-07-persistent-finding", "step-00-finding-present"));

        var result = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = workspace,
            options = new { crossModule = false, requireDocs = true },
        }));
        using var output = JsonDocument.Parse(result.Content[0].Text!);
        foreach (var file in output.RootElement.GetProperty("files").EnumerateArray())
            Assert.Equal(JsonValueKind.Array, file.GetProperty("diagnostics").ValueKind);
    }

    [Fact]
    public async Task CrossModule_HonoursRequireDocsAndEnforceEffects()
    {
        var workspace = CreateTempDir();
        SyncWorkspace(workspace, Path.Combine(CorpusRoot, "ES-07-persistent-finding", "step-00-finding-present"));

        var documented = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = workspace,
            options = new { crossModule = true, requireDocs = true },
        }));
        Assert.Contains("Calor0601", documented.Content[0].Text);

        var relaxed = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = workspace,
            options = new { crossModule = true, requireDocs = false },
        }));
        Assert.DoesNotContain("Calor0601", relaxed.Content[0].Text);

        // enforceEffects governs the per-file pass. Cross-module enforcement
        // still runs with it off — exactly as `calor --no-enforce-effects` does
        // (the gate theory above pins that agreement on ES-05's effects-off step).
        var violating = CreateTempDir();
        File.WriteAllText(Path.Combine(violating, "leaky.calr"), """
            §M{m001:Leaky}
              §F{f001:Speak:pub} () -> void
                §E{}
                §P "cw without declaring it"
            """);
        var effectsOn = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = violating,
            options = new { crossModule = true },
        }));
        Assert.Contains("Calor0410", effectsOn.Content[0].Text);
        var effectsOff = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = violating,
            options = new { crossModule = true, enforceEffects = false },
        }));
        Assert.DoesNotContain("Calor0410", effectsOff.Content[0].Text);

        var singleOff = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            source = File.ReadAllText(Path.Combine(violating, "leaky.calr")),
            options = new { enforceEffects = false, autoFix = false },
        }));
        Assert.DoesNotContain("Calor0410", singleOff.Content[0].Text);
    }

    [Fact]
    public async Task SingleFile_HonoursRequireDocs()
    {
        var source = File.ReadAllText(Path.Combine(
            CorpusRoot, "ES-07-persistent-finding", "step-00-finding-present", "violating.calr"));

        var strict = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            source,
            options = new { requireDocs = true, autoFix = false },
        }));
        Assert.Contains("Calor0601", strict.Content[0].Text);

        var lenient = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            source,
            options = new { autoFix = false },
        }));
        Assert.DoesNotContain("Calor0601", lenient.Content[0].Text);
    }

    [Fact]
    public async Task CrossModule_MissingFile_IsAnError()
    {
        var result = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            files = new[] { Path.Combine(CreateTempDir(), "absent.calr") },
            options = new { crossModule = true },
        }));
        Assert.True(result.IsError);
        Assert.StartsWith("File not found: ", result.Content[0].Text);
    }

    // --- harness -----------------------------------------------------------

    private static async Task<McpToolResult> CallTool(string name, string argumentsJson)
    {
        var handler = new McpMessageHandler();
        var response = await handler.HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonDocument.Parse("1").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse($$"""{ "name": "{{name}}", "arguments": {{argumentsJson}} }""").RootElement,
        });
        Assert.NotNull(response);
        Assert.Null(response!.Error);
        return Assert.IsType<McpToolResult>(response.Result);
    }

    private static async Task<IReadOnlyList<string>> CompileThroughMcp(string workspace, string profile)
    {
        var result = await CallTool("calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = workspace,
            options = new
            {
                crossModule = true,
                enforceEffects = profile != "effects-off",
                requireDocs = profile == "docs-required",
            },
        }));
        using var output = JsonDocument.Parse(result.Content[0].Text!);
        var root = output.RootElement;
        Assert.True(root.GetProperty("crossModule").GetBoolean());

        var entries = new List<JsonElement>();
        foreach (var file in root.GetProperty("files").EnumerateArray())
            entries.AddRange(file.GetProperty("diagnostics").EnumerateArray());
        if (root.TryGetProperty("projectDiagnostics", out var unattributed))
            entries.AddRange(unattributed.EnumerateArray());
        return Canonicalize(entries, workspace);
    }

    private static IReadOnlyList<string> CompileThroughCliProcess(string workspace, string[] sources, string profile)
    {
        var arguments = new List<string>();
        foreach (var source in sources)
            arguments.AddRange(["-i", source]);
        arguments.AddRange(["--format", "json"]);
        if (profile == "effects-off")
            arguments.Add("--no-enforce-effects");
        if (profile == "docs-required")
            arguments.Add("--require-docs");

        var run = CliTestHarness.RunCli(workspace, arguments.ToArray());
        using var output = JsonDocument.Parse(run.StdOut);
        return Canonicalize(output.RootElement.GetProperty("diagnostics").EnumerateArray().ToList(), workspace);
    }

    /// <summary>
    /// Envelope entries as comparable text: workspace-relative paths and a
    /// fixed ordering, the form <see cref="EditScriptIdentityTests"/> uses.
    /// </summary>
    private static IReadOnlyList<string> Canonicalize(IReadOnlyList<JsonElement> entries, string workspace)
    {
        return entries
            .Select(entry =>
            {
                var location = entry.GetProperty("location");
                var file = location.TryGetProperty("file", out var fileElement) && fileElement.ValueKind == JsonValueKind.String
                    ? Path.GetRelativePath(workspace, fileElement.GetString()!).Replace('\\', '/')
                    : "";
                return string.Join(
                    "|",
                    file,
                    entry.GetProperty("code").GetString(),
                    entry.GetProperty("severity").GetString(),
                    location.GetProperty("line").GetInt32(),
                    location.GetProperty("column").GetInt32(),
                    entry.GetProperty("message").GetString());
            })
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record ScriptStep(string SourceDirectory, string Options);

    private sealed record EditScript(string Id, IReadOnlyList<ScriptStep> Steps);

    private static void SyncWorkspace(string workspace, string stepDirectory)
    {
        foreach (var source in Directory.GetFiles(stepDirectory, "*.calr"))
            File.Copy(source, Path.Combine(workspace, Path.GetFileName(source)), overwrite: true);
    }

    private static EditScript LoadScript(string scriptName)
    {
        var directory = Path.Combine(CorpusRoot, scriptName);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "script.json")));
        var root = json.RootElement;
        var steps = root.GetProperty("steps")
            .EnumerateArray()
            .Select(step => new ScriptStep(
                Path.Combine(directory, step.GetProperty("dir").GetString()!),
                step.GetProperty("options").GetString()!))
            .ToArray();
        Assert.NotEmpty(steps);
        foreach (var step in steps)
            Assert.Contains(step.Options, new[] { "effects-on", "effects-off", "docs-required" });
        return new EditScript(root.GetProperty("id").GetString()!, steps);
    }

    private static string[] EnumerateScriptDirectories() =>
        Directory.GetDirectories(CorpusRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string CorpusRoot =>
        Path.Combine(CliTestHarness.FindRepoRoot(), "tests", "TestData", "EditScripts");

    private string CreateTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "calor-mcpgate-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
