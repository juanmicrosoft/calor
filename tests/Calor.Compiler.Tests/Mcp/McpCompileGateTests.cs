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
        // Eight scripts, the same eight EditScriptIdentityTests pins; a script
        // dropped here would have to edit this list in the diff. ES-08 (the
        // effect-row edit script) is registered by this PR.
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
                "ES-08-effect-row-edit",
            },
            EnumerateScriptDirectories().Select(Path.GetFileName).ToArray());
    }

    /// <summary>
    /// Steps whose two paths legitimately agree on an EMPTY finding set, with
    /// the reason. Everywhere else, agreement on nothing would be agreement
    /// about nothing, so the per-step check below demands a diagnostic.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CleanSteps =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ES-01-local-edit/0"] = "baseline: the violation is introduced in step 2",
            ["ES-01-local-edit/1"] = "the edit is inside a body; the violation is introduced in step 2",
            ["ES-02-add-file/0"] = "baseline before any file is added",
            ["ES-02-add-file/1"] = "the added file is the CLEAN one; the violating one arrives in step 2",
            ["ES-03-delete-file/1"] = "the violating file has just been deleted; its finding must be gone",
            ["ES-03-delete-file/2"] = "only the clean file remains",
            ["ES-04-cross-module-effect/0"] = "clean state before the callee gains an effect",
            ["ES-04-cross-module-effect/2"] = "the callee lost the effect again; the finding must disappear",
            ["ES-05-options-flip/1"] = "effects enforcement is OFF for this step, which is the point of the flip",
            ["ES-06-touch-noop/0"] = "nothing is wrong in this corpus; the script pins that nothing moves",
            ["ES-06-touch-noop/1"] = "identical rewrite",
            ["ES-06-touch-noop/2"] = "identical rewrite",
            ["ES-08-effect-row-edit/0"] = "the pre-edit baseline: the callee's row is correct, so nothing is reported; the row widens in step 1 and is erased in step 2",
        };

    [Theory]
    [MemberData(nameof(RegisteredScripts))]
    public async Task DiagnosticsThroughCalorCompileMatchTheCliProcess(string scriptName)
    {
        var script = LoadScript(scriptName);
        for (var index = 0; index < script.Steps.Count; index++)
        {
            var step = script.Steps[index];
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

            // Per-step anti-vacuity: two paths agreeing on an empty list agree
            // about nothing. A step allowed to be clean must be registered as
            // clean, with its reason — so a step that STOPS producing findings
            // fails here instead of passing silently.
            var key = $"{scriptName}/{index}";
            if (CleanSteps.ContainsKey(key))
            {
                Assert.True(cli.Count == 0,
                    $"{key} is registered as clean ({CleanSteps[key]}) but produced: {string.Join(" | ", cli)}");
            }
            else
            {
                Assert.True(cli.Count > 0,
                    $"{key} produced no diagnostic on either path, so the comparison is vacuous. "
                        + "Either the step regressed, or it belongs in CleanSteps with a reason.");
            }
        }
    }

    [Fact]
    public void EveryRegisteredCleanStepExists()
    {
        // The clean-step registry cannot outlive the corpus: a key naming a
        // script or a step that no longer exists is a stale exemption.
        foreach (var key in CleanSteps.Keys)
        {
            var separator = key.LastIndexOf('/');
            var scriptName = key[..separator];
            var index = int.Parse(key[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            var script = LoadScript(scriptName);
            Assert.True(index >= 0 && index < script.Steps.Count, $"{key}: no such step");
        }

        // And it may not swallow the corpus. The honest numbers, stated rather
        // than implied: 24 steps, of which 13 are clean by construction (a
        // baseline, an addition of a clean file, a deletion, a reversal, the
        // effects-off step, all three of ES-06's identical rewrites, and
        // ES-08's pre-edit baseline) and 11 carry findings. A leg comparing two paths on an empty list proves
        // nothing, so the 9 are what makes it load-bearing — and if that number
        // drops, this test says so before the theory quietly passes.
        var totalSteps = EnumerateScriptDirectories()
            .Select(directory => LoadScript(Path.GetFileName(directory)).Steps.Count)
            .Sum();
        Assert.Equal(24, totalSteps);
        Assert.Equal(13, CleanSteps.Count);
        Assert.Equal(11, totalSteps - CleanSteps.Count);

        // Every script except ES-06 (whose whole point is that nothing moves)
        // must observe at least one step with findings.
        foreach (var directory in EnumerateScriptDirectories())
        {
            var scriptName = Path.GetFileName(directory);
            if (scriptName == "ES-06-touch-noop")
                continue;
            var steps = LoadScript(scriptName).Steps.Count;
            var clean = Enumerable.Range(0, steps).Count(index => CleanSteps.ContainsKey($"{scriptName}/{index}"));
            Assert.True(clean < steps, $"{scriptName}: every step is registered as clean, so the script observes nothing");
        }
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
            var cliText = File.ReadAllText(indexPath);
            var (built, _) = ProjectIndex.Load(IndexCommand.DefaultOutputDirectory(workspace));
            Assert.NotNull(built);
            var first = built!.Declarations.First();
            File.Delete(indexPath);

            var result = await CallTool(workspace, "calor_query", JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["projectDirectory"] = workspace,
                ["facet"] = "callers",
                ["symbol"] = first.Name,
                ["inFile"] = first.File,
            }));
            Assert.False(result.IsError, result.Content[0].Text);
            Assert.True(File.Exists(indexPath), "calor_query did not rebuild the missing index");

            var mcpText = File.ReadAllText(indexPath);
            if (CliTestHarness.CliCompilerIsThisCompiler)
            {
                Assert.Equal(cliText, mcpText);
                continue;
            }

            // Coverage lane: coverlet rewrites the assemblies this process
            // loaded, so the in-process compiler hash cannot equal the CLI
            // child's. Normalise that ONE header field and require every other
            // byte to match — the index CONTENTS are what the gate claims.
            var (fromMcp, _) = ProjectIndex.Load(IndexCommand.DefaultOutputDirectory(workspace));
            Assert.NotEqual(built.CompilerHash, fromMcp!.CompilerHash);
            Assert.Equal(
                cliText.Replace(built.CompilerHash, "<compiler>", StringComparison.Ordinal),
                mcpText.Replace(fromMcp.CompilerHash, "<compiler>", StringComparison.Ordinal));
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

        var perFile = await CallTool(workspace, "calor_compile", JsonSerializer.Serialize(new { projectPath = workspace }));
        using var lenient = JsonDocument.Parse(perFile.Content[0].Text!);
        Assert.False(lenient.RootElement.TryGetProperty("crossModule", out _));
        // Alone, caller.calr cannot resolve SaveOrder at all — a binder error,
        // never the effect verdict the project as a whole carries.
        Assert.DoesNotContain("Calor0410", perFile.Content[0].Text);

        var project = await CallTool(workspace, "calor_compile", JsonSerializer.Serialize(new
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

    /// <summary>
    /// `--no-enforce-effects` turns off the per-file effect pass; the driver
    /// still runs cross-module enforcement, so ES-04's violating step reports
    /// Calor0410 with effects off — and the MCP path reports exactly what the
    /// CLI process does. (ES-05's effects-off step does NOT pin this: its
    /// violation is per-file, so both paths agree on an empty list there,
    /// which is registered in <c>CleanSteps</c>.)
    /// </summary>
    [Fact]
    public async Task CrossModuleEnforcement_StillRunsWithEffectsOff_AndAgreesWithTheCli()
    {
        var workspace = CreateTempDir();
        SyncWorkspace(workspace, Path.Combine(CorpusRoot, "ES-04-cross-module-effect", "step-01-callee-gains-effect"));
        var sources = Directory.GetFiles(workspace, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var mcp = await CompileThroughMcp(workspace, "effects-off");
        var cli = CompileThroughCliProcess(workspace, sources, "effects-off");

        Assert.Equal(cli, mcp);
        Assert.Contains(cli, line => line.Contains("|Calor0410|error|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PerFileBatchMode_NowCarriesEnvelopeDiagnostics()
    {
        var workspace = CreateTempDir();
        SyncWorkspace(workspace, Path.Combine(CorpusRoot, "ES-07-persistent-finding", "step-00-finding-present"));

        var result = await CallTool(workspace, "calor_compile", JsonSerializer.Serialize(new
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

        var documented = await CallTool(workspace, "calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = workspace,
            options = new { crossModule = true, requireDocs = true },
        }));
        Assert.Contains("Calor0601", documented.Content[0].Text);

        var relaxed = await CallTool(workspace, "calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = workspace,
            options = new { crossModule = true, requireDocs = false },
        }));
        Assert.DoesNotContain("Calor0601", relaxed.Content[0].Text);

        // enforceEffects governs the PER-FILE pass only, which is what this
        // single-module case shows. Cross-module enforcement is separate and
        // keeps running with it off — pinned against the CLI in
        // CrossModuleEnforcement_StillRunsWithEffectsOff_AndAgreesWithTheCli.
        var violating = CreateTempDir();
        File.WriteAllText(Path.Combine(violating, "leaky.calr"), """
            §M{m001:Leaky}
              §F{f001:Speak:pub} () -> void
                §E{}
                §P "cw without declaring it"
            """);
        var effectsOn = await CallTool(violating, "calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = violating,
            options = new { crossModule = true },
        }));
        Assert.Contains("Calor0410", effectsOn.Content[0].Text);
        var effectsOff = await CallTool(violating, "calor_compile", JsonSerializer.Serialize(new
        {
            projectPath = violating,
            options = new { crossModule = true, enforceEffects = false },
        }));
        Assert.DoesNotContain("Calor0410", effectsOff.Content[0].Text);

        var singleOff = await CallTool(violating, "calor_compile", JsonSerializer.Serialize(new
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

        var strict = await CallTool(Path.GetTempPath(), "calor_compile", JsonSerializer.Serialize(new
        {
            source,
            options = new { requireDocs = true, autoFix = false },
        }));
        Assert.Contains("Calor0601", strict.Content[0].Text);

        var lenient = await CallTool(Path.GetTempPath(), "calor_compile", JsonSerializer.Serialize(new
        {
            source,
            options = new { autoFix = false },
        }));
        Assert.DoesNotContain("Calor0601", lenient.Content[0].Text);
    }

    [Fact]
    public async Task CrossModule_MissingFile_IsAnError()
    {
        var workspace = CreateTempDir();
        var result = await CallTool(workspace, "calor_compile", JsonSerializer.Serialize(new
        {
            files = new[] { Path.Combine(workspace, "absent.calr") },
            options = new { crossModule = true },
        }));
        Assert.True(result.IsError);
        Assert.StartsWith("File not found: ", result.Content[0].Text);
    }

    // --- harness -----------------------------------------------------------

    private static async Task<McpToolResult> CallTool(string rootDirectory, string name, string argumentsJson)
    {
        var handler = new McpMessageHandler(rootDirectory: rootDirectory);
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
        var result = await CallTool(workspace, "calor_compile", JsonSerializer.Serialize(new
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
