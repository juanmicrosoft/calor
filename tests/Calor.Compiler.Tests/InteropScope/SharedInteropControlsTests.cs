using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Mcp;
using Calor.Compiler.Migration;
using Calor.Compiler.Tests.EvidenceContract;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1428 (0.25 T1): the shared behavioral controls. Every control registered in the T1 register
/// (<c>docs/plans/evidence/v0.25-t1-1428/register.json</c>) is converted on every measured surface
/// (library, CLI convert, CLI migrate, MCP convert with and without passthroughOnError, MCP migrate,
/// MCP batch), compiled, run and compared with the original C#. Each fixture's <c>Probe.Run()</c>
/// returns a trace of values, aliasing, evaluation order, exceptions and iterator timing, so a
/// compile that changes behavior fails. A cell's outcome must be one the control allows and must
/// equal the committed observation in <c>controls-results.json</c>: a reclassified cell fails until
/// the results are regenerated (<c>CALOR_REGENERATE_T1_CONTROLS=1</c>) and the register approves the
/// new results hash.
/// </summary>
public sealed class SharedInteropControlsTests : IDisposable
{
    public const string Dir = "docs/plans/evidence/v0.25-t1-1428";
    public const string ResultsPath = Dir + "/controls-results.json";

    /// <summary>The surfaces measured in-repo, in register order.</summary>
    public static readonly string[] Surfaces =
        ["library", "cli-convert", "cli-migrate", "mcp-convert", "mcp-convert-passthroughOnError", "mcp-migrate", "mcp-batch"];

    private static readonly bool Regenerate = Environment.GetEnvironmentVariable("CALOR_REGENERATE_T1_CONTROLS") == "1";
    private static readonly object RecordLock = new();
    private static JsonObject? _recorded;

    private readonly string _temp = Directory.CreateTempSubdirectory("calor-t1-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    public static TheoryData<string, string> Cells()
    {
        var data = new TheoryData<string, string>();
        foreach (var control in Register()["controls"]!.AsArray())
            foreach (var surface in Surfaces)
                data.Add(control!["id"]!.GetValue<string>(), surface);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task Control_BehavesLikeTheOriginal_OnEverySurface(string id, string surface)
    {
        // #1564: in-process surfaces are hermetic. The library surface injects an empty manifest root; the MCP
        // surfaces resolve theirs from CALOR_HOME when each conversion starts, and the test assembly pins that
        // to an empty directory (tests/Shared/HermeticCalorHome.cs). A user's ~/.calor/manifests never applies.
        Assert.Equal(global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot, Effects.Manifests.ManifestLoader.GetUserManifestsDirectory());
        var control = Register()["controls"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;
        var source = File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), control["fixture"]!.GetValue<string>()));
        var original = Run(source);
        var (calor, paths, surfaceError) = await ConvertAsync(source, surface);
        var cell = Classify(id, calor, paths, surfaceError, original);
        var outcome = cell["outcome"]!.GetValue<string>();

        // Compile success alone is never enough: a converted program that runs differently fails here.
        Assert.False(outcome.EndsWith("-mismatch", StringComparison.Ordinal),
            $"{id} on {surface}: behavior changed. original: {original}; generated: {cell["generatedTrace"]}\n{calor}");
        var allowed = control["expected"]!.GetValue<string>().Split("-or-");
        Assert.True(allowed.Contains(outcome), $"{id} on {surface}: {outcome}, allowed {control["expected"]}\n{calor}");

        cell.Remove("generatedTrace");
        if (Regenerate)
        {
            Record(id, original, surface, cell);
            return;
        }
        var committed = Results()[id];
        Assert.True(committed is not null, $"{id} has no committed observation; regenerate with CALOR_REGENERATE_T1_CONTROLS=1");
        Assert.Equal(committed["originalTrace"]!.GetValue<string>(), original);
        Assert.True(JsonNode.DeepEquals(committed["surfaces"]?[surface], cell),
            $"{id} on {surface} reclassified: committed {committed["surfaces"]?[surface]?.ToJsonString()}, observed {cell.ToJsonString()}");
    }

    /// <summary>The surface defaults the register records are what each entry point actually builds.</summary>
    [Fact]
    public void RegisteredDefaults_MatchTheEntryPoints()
    {
        var surfaces = Register()["surfaces"]!.AsArray().ToDictionary(s => s!["id"]!.GetValue<string>(), s => s!);
        void Check(string id, ConversionOptions options)
        {
            Assert.Equal(surfaces[id]["defaults"]!["rescueUnusableMembers"]!.GetValue<bool>(), options.RescueUnusableMembers);
            Assert.Equal(surfaces[id]["defaults"]!["passthroughOnError"]!.GetValue<bool>(), options.PassthroughOnError);
        }
        Check("library", new ConversionOptions());
        Check("cli-convert", Commands.ConvertCommand.BuildCSharpToCalorOptions(benchmark: false, verbose: false, explain: false,
            noFallback: false, passthrough: false, explicitCallClosers: false));
        // Every committed cell reports the options that applied; they must agree with the registered defaults.
        foreach (var (control, node) in Results())
            foreach (var (surface, cell) in node!["surfaces"]!.AsObject())
                if (cell!["paths"] is JsonObject p)
                {
                    Assert.True(surfaces[surface]["defaults"]!["rescueUnusableMembers"]!.GetValue<bool>() == p["rescueUnusableMembers"]!.GetValue<bool>()
                        && surfaces[surface]["defaults"]!["passthroughOnError"]!.GetValue<bool>() == p["passthroughOnError"]!.GetValue<bool>(),
                        $"{control} on {surface}: reported options differ from the registered defaults");
                }
    }

    // ---- Surfaces -------------------------------------------------------------------------------

    /// <summary>Converts on one surface; SurfaceError is the surface's own failure signal (exit code, isError, Success).</summary>
    private async Task<(string? Calor, JsonNode? Paths, bool SurfaceError)> ConvertAsync(string source, string surface)
    {
        var dir = Path.Combine(_temp, surface + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "Input.cs");
        File.WriteAllText(input, source);
        switch (surface)
        {
            case "library":
            {
                var result = new CSharpToCalorConverter(new ConversionOptions { UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot }).Convert(source);
                return (result.CalorSource, JsonSerializer.SerializeToNode(result.Paths), !result.Success);
            }
            case "cli-convert":
            {
                var (exit, stdout, _) = CliTestHarness.RunCli(dir, IsolatedHome(), "convert", "Input.cs", "-o", "Input.calr", "--format", "json");
                return (ReadCalor(dir), JsonNode.Parse(stdout)?["data"]?["conversionPaths"]?.DeepClone(), exit != 0);
            }
            case "cli-migrate":
            {
                var report = Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".json");
                var (exit, _, _) = CliTestHarness.RunCli(dir, IsolatedHome(), "migrate", dir, "--skip-verify", "--skip-analyze", "--report", report);
                var file = File.Exists(report) ? JsonNode.Parse(File.ReadAllText(report))?["data"]?["fileResults"]?[0] : null;
                return (ReadCalor(dir), file?["conversionPaths"]?.DeepClone(), exit != 0);
            }
            case "mcp-convert":
            case "mcp-convert-passthroughOnError":
            {
                var (isError, payload) = await Mcp("calor_convert", surface == "mcp-convert"
                    ? new { source } : new { source, passthroughOnError = true });
                return (payload["calorSource"]?.GetValue<string>(), payload["lossSummary"]?["paths"]?.DeepClone(), isError);
            }
            case "mcp-migrate":
            {
                var (isError, payload) = await Mcp("calor_migrate", new { projectPath = dir, phase = "convert" });
                return (ReadCalor(dir), payload["perFile"]?[0]?["conversionPaths"]?.DeepClone(), isError);
            }
            case "mcp-batch":
            {
                var (isError, payload) = await Mcp("calor_batch", new { action = "convert", projectPath = dir });
                return (ReadCalor(dir), payload["files"]?[0]?["conversionPaths"]?.DeepClone(), isError);
            }
            default:
                throw new ArgumentException(surface);
        }
    }

    /// <summary>CLI children get their own home, so no user manifest or cache under ~/.calor applies.</summary>
    private IReadOnlyDictionary<string, string> IsolatedHome()
    {
        var home = Path.Combine(_temp, "home");
        Directory.CreateDirectory(home);
        return new Dictionary<string, string> { ["HOME"] = home, ["USERPROFILE"] = home, ["CALOR_HOME"] = Path.Combine(home, ".calor") };
    }

    private static bool Refused(JsonNode? paths) => paths?["outcome"]?.GetValue<string>() is null or "refused";

    private static string? ReadCalor(string dir) =>
        Directory.EnumerateFiles(dir, "*.calr", SearchOption.AllDirectories).SingleOrDefault() is { } f ? File.ReadAllText(f) : null;

    private static async Task<(bool IsError, JsonNode Payload)> Mcp(string tool, object arguments)
    {
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

    // ---- Classification ---------------------------------------------------------------------------

    /// <summary>
    /// native: no §CSHARP/§CS/§RAW and no reported interop, rescue, passthrough, lossy or dropped member;
    /// preserved: some C# kept verbatim, nothing lossy or dropped; rejected: an explicit refusal (no output, or
    /// output that still compiles).
    /// Either of the first two is suffixed -mismatch when the generated program runs differently. Output
    /// that was emitted but fails a non-effect compile is invalid-output, which no control allows.
    /// </summary>
    private static JsonObject Classify(string id, string? calor, JsonNode? paths, bool surfaceError, string original)
    {
        // The surface's own error signal is recorded, so a cell whose surface starts failing is reclassified.
        var cell = new JsonObject { ["paths"] = Paths(paths), ["surfaceError"] = surfaceError };
        var refused = surfaceError || Refused(paths);
        if (string.IsNullOrWhiteSpace(calor))
        {
            cell["outcome"] = "rejected";
            cell["compile"] = "no-output";
            return Ordered(cell);
        }
        int Count(string key) => paths?[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
        if (Count("lossy") + Count("dropped") > 0)
        {
            cell["outcome"] = "lossy";
            cell["compile"] = "not-run";
            return Ordered(cell);
        }
        var interop = calor.Contains("§CSHARP{", StringComparison.Ordinal) || calor.Contains("§CS{", StringComparison.Ordinal)
            || calor.Contains("§RAW", StringComparison.Ordinal) || Count("interop") + Count("rescue") + Count("passthrough") > 0;
        var strict = Compile(id, calor, enforceEffects: true);
        var errors = strict.Diagnostics.Errors.Select(e => e.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        var effectsOnly = errors.Count > 0 && errors.All(c => c.StartsWith("Calor04", StringComparison.Ordinal));
        var final = effectsOnly ? Compile(id, calor, enforceEffects: false) : strict;
        if (final.HasErrors)
        {
            cell["outcome"] = "invalid-output";
            cell["compile"] = "errors:" + string.Join(",", final.Diagnostics.Errors.Select(e => e.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal));
            return Ordered(cell);
        }
        cell["compile"] = errors.Count == 0 ? "default" : "permissive-effects:" + string.Join(",", errors);
        // Output is always run and compared, even when the surface refused: a refusal that still returns
        // behavior-changing code is a mismatch, never an allowed rejection.
        var generated = Run(final.GeneratedCode);
        cell["outcome"] = (refused ? "rejected" : interop ? "preserved" : "native") + (generated == original ? "" : "-mismatch");
        cell["generatedTrace"] = generated;
        return Ordered(cell);
    }

    private static JsonNode? Paths(JsonNode? p) => p is not JsonObject o ? null : new JsonObject
    {
        ["outcome"] = o["outcome"]?.DeepClone(), ["interop"] = o["interop"]?.DeepClone(), ["rescue"] = o["rescue"]?.DeepClone(),
        ["passthrough"] = o["passthrough"]?.DeepClone(), ["lossy"] = o["lossy"]?.DeepClone(), ["dropped"] = o["dropped"]?.DeepClone(),
        ["rescueUnusableMembers"] = o["rescueUnusableMembers"]?.DeepClone(), ["passthroughOnError"] = o["passthroughOnError"]?.DeepClone(),
    };

    private static JsonObject Ordered(JsonObject cell) => new(cell.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())));

    /// <summary>Compiles with built-in effect manifests only, so ~/.calor/manifests cannot change a cell.</summary>
    private static CompilationResult Compile(string id, string calor, bool enforceEffects)
    {
        var resolver = new Effects.EffectResolver(new Effects.Manifests.ManifestLoader(loadUserLevelManifests: false));
        resolver.Initialize(projectDirectory: null, solutionDirectory: null);
        using var context = new CompilationContext { SharedEffectResolver = resolver };
        return Program.Compile(calor, id + ".calr", new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            EnforceEffects = enforceEffects,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            Context = context,
        });
    }

    /// <summary>Compiles C# in memory and returns <c>Probe.Run()</c>, or "!" and the exception type, under the invariant culture.</summary>
    private static string Run(string csharp)
    {
        var compilation = CSharpCompilation.Create("T1_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(LanguageVersion.Preview))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var run = context.LoadFromStream(stream).GetTypes().Single(t => t.Name == "Probe").GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            try { return run.Invoke(null, null)?.ToString() ?? "<null>"; }
            catch (TargetInvocationException e) { return "!" + e.InnerException!.GetType().Name; }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            context.Unload();
        }
    }

    // ---- Register and committed results ------------------------------------------------------------

    public static JsonNode Register() => JsonNode.Parse(File.ReadAllText(Path.Combine(EvidenceContractTests.RepoRoot(), Dir, "register.json")))!;

    private static JsonObject Results()
    {
        var path = Path.Combine(EvidenceContractTests.RepoRoot(), ResultsPath);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!["controls"]!.AsObject() : new JsonObject();
    }

    /// <summary>Regeneration only: merges one observed cell into the results file, in register order.</summary>
    private static void Record(string id, string original, string surface, JsonObject cell)
    {
        lock (RecordLock)
        {
            _recorded ??= new JsonObject();
            var entry = _recorded[id] as JsonObject ?? new JsonObject { ["originalTrace"] = original, ["surfaces"] = new JsonObject() };
            _recorded[id] = entry;
            entry["surfaces"]![surface] = cell;
            var ordered = new JsonObject();
            foreach (var c in Register()["controls"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).Where(_recorded.ContainsKey))
            {
                var e = _recorded[c]!;
                var s = new JsonObject();
                foreach (var name in Surfaces.Where(n => e["surfaces"]![n] is not null))
                    s[name] = e["surfaces"]![name]!.DeepClone();
                ordered[c] = new JsonObject { ["originalTrace"] = e["originalTrace"]!.DeepClone(), ["surfaces"] = s };
            }
            var file = new JsonObject
            {
                ["schema"] = "calor.v0.25.t1-controls-results/1",
                ["producer"] = "tests/Calor.Compiler.Tests/InteropScope/SharedInteropControlsTests.cs (CALOR_REGENERATE_T1_CONTROLS=1)",
                ["controls"] = ordered,
            };
            File.WriteAllText(Path.Combine(EvidenceContractTests.RepoRoot(), ResultsPath),
                file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        }
    }
}
