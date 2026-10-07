using System.Text.Json;
using Calor.Compiler.Mcp;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests.ConversionReporting;

/// <summary>
/// 0.25 F6 (#1144): every conversion surface reports the path that actually preserved
/// each member (converter interop, automatic rescue, requested passthrough) with the
/// trigger that fired, or reports the file as refused. Test names carry the registered
/// F6-REPORT case ids. Fixtures are the R0 packet's (#1426) committed C# inputs, plus
/// one inline input (a trailing label) that only rescue or passthrough can convert.
/// </summary>
public class ConversionPathReportingTests : IDisposable
{
    private const string Fixtures = "docs/plans/evidence/v0.25-r0-1426/fixtures";

    /// <summary>A label before the last statement: native output fails the C# round trip, so the library default refuses.</summary>
    private const string TrailingLabel = "public class G { public int M(int x) { here: return x; } }";

    private readonly string _tempDir = Directory.CreateTempSubdirectory("calor-f6-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private static string Fixture(string id) =>
        File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), Fixtures, id + ".cs.txt"));

    private static ConversionResult Convert(string source, ConversionOptions options) =>
        new CSharpToCalorConverter(options).Convert(source);

    private static void AssertPaths(ConversionResult result, string path, string trigger, string enabledBy, int count)
    {
        var fallbacks = result.Losses.Where(l => l.Feature == "post-validation-fallback").ToList();
        Assert.Equal(count, fallbacks.Count);
        Assert.All(fallbacks, l =>
        {
            Assert.Equal(path, l.Path);
            Assert.Equal(trigger, l.Trigger);
            Assert.Equal(enabledBy, l.EnabledBy);
        });
    }

    // ── Library (F6-REPORT-07, -08, -09, -11, -12 library cells) ──────────

    [Fact]
    public void F6_REPORT_07_LibraryDefault_RefusesRoundTripFailure_AndSaysRescueWasOff()
    {
        var result = Convert(Fixture("F4-ITER-01"), new ConversionOptions());

        Assert.False(result.Success);
        Assert.Equal("refused", result.Paths.Outcome);
        Assert.False(result.Paths.RescueUnusableMembers);
        Assert.False(result.Paths.PassthroughOnError);
        Assert.Equal(0, result.Paths.Rescue + result.Paths.Passthrough);
    }

    [Fact]
    public void F6_REPORT_12_Library_RescueOnly_PassthroughOnly_Both_AreDistinguished()
    {
        var source = Fixture("F4-ITER-01");

        var rescue = Convert(source, new ConversionOptions { RescueUnusableMembers = true });
        AssertPaths(rescue, ConversionPath.Rescue, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.RescueUnusableMembers, 2);

        var passthrough = Convert(source, new ConversionOptions { PassthroughOnError = true });
        AssertPaths(passthrough, ConversionPath.Passthrough, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.PassthroughOnError, 2);
        Assert.Equal(2, passthrough.Paths.Passthrough);

        // Precedence: with both on, the rescue would have fired without the request.
        var both = Convert(source, new ConversionOptions { RescueUnusableMembers = true, PassthroughOnError = true });
        AssertPaths(both, ConversionPath.Rescue, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.RescueUnusableMembers, 2);
        Assert.True(both.Paths.PassthroughOnError);
        Assert.Equal("preserved", both.Paths.Outcome);
        Assert.Equal(new Dictionary<string, int> { [ConversionTrigger.RoundTripFailure] = 2 }, both.Paths.Triggers);
    }

    [Fact]
    public void F6_REPORT_12_Library_ParseFailureRescue_IsAutomaticUnderLosslessWhateverTheFlags()
    {
        foreach (var options in new[]
                 {
                     new ConversionOptions(),
                     new ConversionOptions { RescueUnusableMembers = true },
                     new ConversionOptions { PassthroughOnError = true },
                 })
        {
            var result = Convert(Fixture("F5-ARRAY-02"), options);
            Assert.True(result.Success);
            AssertPaths(result, ConversionPath.Rescue, ConversionTrigger.ParseFailure, ConversionEnabledBy.LosslessFidelity, 1);
        }
    }

    [Fact]
    public void F6_REPORT_11_CustomModuleName_NoLongerDisablesRescue()
    {
        var passthrough = Convert(Fixture("F4-ITER-01"), new ConversionOptions { PassthroughOnError = true, ModuleName = "Custom" });
        Assert.True(passthrough.Success);
        AssertPaths(passthrough, ConversionPath.Passthrough, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.PassthroughOnError, 2);

        var rescue = Convert(Fixture("F4-ITER-01"), new ConversionOptions { RescueUnusableMembers = true, ModuleName = "Custom" });
        Assert.True(rescue.Success);
        AssertPaths(rescue, ConversionPath.Rescue, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.RescueUnusableMembers, 2);
        Assert.Contains("§CSHARP", rescue.CalorSource);
    }

    [Fact]
    public void F6_REPORT_08_NativeConversion_HasNoPathAttribution()
    {
        var result = Convert(Fixture("F1-REFOUT-01"), new ConversionOptions { RescueUnusableMembers = true, PassthroughOnError = true });

        Assert.True(result.Success);
        Assert.Empty(result.Losses);
        Assert.Equal("native", result.Paths.Outcome);
        Assert.Empty(result.Paths.Triggers);
        Assert.Null(ConversionPathSummary.Describe(result.Losses, "--passthrough"));
    }

    [Fact]
    public void F6_REPORT_09_ConverterInterop_HasNoRescueOrPassthroughAttribution()
    {
        foreach (var options in new[] { new ConversionOptions(), new ConversionOptions { RescueUnusableMembers = true, PassthroughOnError = true } })
        {
            var result = Convert(Fixture("F3-LOCAL-01"), options);
            Assert.NotEmpty(result.Losses);
            Assert.All(result.Losses, l =>
            {
                Assert.Equal(ConversionPath.Interop, l.Path);
                Assert.Null(l.Trigger);
                Assert.Null(l.EnabledBy);
            });
            Assert.Equal(0, result.Paths.Rescue + result.Paths.Passthrough);
        }
    }

    [Fact]
    public void Library_TrailingLabel_DiscriminatesRefusedPassthroughAndRescue()
    {
        Assert.Equal("refused", Convert(TrailingLabel, new ConversionOptions()).Paths.Outcome);
        AssertPaths(Convert(TrailingLabel, new ConversionOptions { PassthroughOnError = true }),
            ConversionPath.Passthrough, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.PassthroughOnError, 1);
        AssertPaths(Convert(TrailingLabel, new ConversionOptions { RescueUnusableMembers = true }),
            ConversionPath.Rescue, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.RescueUnusableMembers, 1);
    }

    // ── CLI convert (F6-REPORT-01, -02, -03, -10, -12 CLI cells) ──────────

    private (int Exit, string Out, string Err) Cli(string source, params string[] flags)
    {
        var input = Path.Combine(_tempDir, $"In{Guid.NewGuid():N}.cs");
        File.WriteAllText(input, source);
        return CliTestHarness.RunCli(_tempDir, ["convert", input, "-o", Path.ChangeExtension(input, ".calr"), .. flags]);
    }

    private static JsonElement Data(string stdout) => JsonDocument.Parse(stdout).RootElement.GetProperty("data");

    [Fact]
    public void F6_REPORT_01_CliNoFlags_NamesAutomaticRescue_NotPassthrough()
    {
        var (exit, stdout, stderr) = Cli(Fixture("F5-ARRAY-01"));

        Assert.True(exit == 0, stderr);
        Assert.DoesNotContain("via --passthrough", stdout);
        Assert.Contains("Preservation paths: 2 by automatic rescue (round-trip-failure: 2; no passthrough request needed)", stdout);
        Assert.Contains("[rescue: round-trip-failure]", stdout);
    }

    [Fact]
    public void F6_REPORT_02_CliJson_RecordsTheTrigger_ForNoFlagAndPassthroughRuns()
    {
        foreach (var flags in new[] { new[] { "--format", "json" }, new[] { "--format", "json", "--passthrough" } })
        {
            var (exit, stdout, stderr) = Cli(Fixture("F5-ARRAY-01"), flags);
            Assert.True(exit == 0, stderr);
            var data = Data(stdout);
            var paths = data.GetProperty("conversionPaths");
            Assert.Equal(1, paths.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("preserved", paths.GetProperty("outcome").GetString());
            Assert.Equal(2, paths.GetProperty("rescue").GetInt32());
            Assert.Equal(0, paths.GetProperty("passthrough").GetInt32());
            Assert.True(paths.GetProperty("rescueUnusableMembers").GetBoolean());
            Assert.Equal(flags.Contains("--passthrough"), paths.GetProperty("passthroughOnError").GetBoolean());
            foreach (var loss in data.GetProperty("losses").EnumerateArray())
            {
                Assert.Equal("rescue", loss.GetProperty("path").GetString());
                Assert.Equal("round-trip-failure", loss.GetProperty("trigger").GetString());
                Assert.Equal("rescueUnusableMembers", loss.GetProperty("enabledBy").GetString());
            }
        }
    }

    /// <summary>The runnable example quoted on website/content/cli/convert.mdx.</summary>
    [Fact]
    public void F6_REPORT_12_CliWebsiteExample_MatchesTheDocumentedOutput()
    {
        const string counter = "public class Counter\n{\n    public int Next(int x)\n    {\n        done:\n        return x + 1;\n    }\n}\n";

        var (exit, stdout, stderr) = Cli(counter);
        Assert.True(exit == 0, stderr);
        Assert.Contains("[post-validation-fallback] [rescue: round-trip-failure] Converted Calor for 'global::Counter'", stdout);
        Assert.Contains("  Preservation paths: 1 by automatic rescue (round-trip-failure: 1; no passthrough request needed)", stdout);

        var (_, json, _) = Cli(counter, "--format", "json");
        var paths = Data(json).GetProperty("conversionPaths");
        Assert.Equal("preserved", paths.GetProperty("outcome").GetString());
        Assert.Equal(1, paths.GetProperty("triggers").GetProperty("round-trip-failure").GetInt32());
        Assert.False(paths.GetProperty("passthroughOnError").GetBoolean());
    }

    [Fact]
    public void F6_REPORT_10_CliRefusal_ExitsOne_AndReportsRefused()
    {
        var (exit, stdout, stderr) = Cli("public class Broken { public int M( { }", "--format", "json");

        Assert.Equal(1, exit);
        Assert.Contains("Conversion refused; no output written (automatic rescue: on, --passthrough: off)", stderr);
        Assert.Equal("refused", Data(stdout).GetProperty("conversionPaths").GetProperty("outcome").GetString());
    }

    [Fact]
    public void F6_REPORT_08_CliNative_PrintsNoPathLine()
    {
        var (exit, stdout, stderr) = Cli(Fixture("F1-REFOUT-01"));

        Assert.True(exit == 0, stderr);
        Assert.DoesNotContain("Preservation paths", stdout);
    }

    // ── MCP calor_convert (F6-REPORT-03, -10, -11, -13) ──────────────────

    private static async Task<(bool IsError, JsonElement Payload)> Mcp(string tool, object arguments)
    {
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("1").RootElement,
            Method = "tools/call",
            Params = JsonSerializer.SerializeToElement(new { name = tool, arguments })
        };
        var response = await new McpMessageHandler().HandleRequestAsync(request);
        var result = JsonSerializer.SerializeToElement(response!.Result, McpJsonOptions.Default);
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        var isError = result.TryGetProperty("isError", out var e) && e.GetBoolean();
        return (isError, JsonDocument.Parse(text).RootElement);
    }

    [Fact]
    public async Task F6_REPORT_03_McpDefault_RefusesAndSaysRescueWasOff_PassthroughRescues()
    {
        var (defaultError, defaultPayload) = await Mcp("calor_convert", new { source = Fixture("F4-ITER-01") });
        Assert.True(defaultError);
        var defaultPaths = defaultPayload.GetProperty("lossSummary").GetProperty("paths");
        Assert.Equal("refused", defaultPaths.GetProperty("outcome").GetString());
        Assert.False(defaultPaths.GetProperty("rescueUnusableMembers").GetBoolean());

        var (ptError, ptPayload) = await Mcp("calor_convert", new { source = Fixture("F4-ITER-01"), passthroughOnError = true });
        Assert.False(ptError);
        var summary = ptPayload.GetProperty("lossSummary");
        Assert.Equal(2, summary.GetProperty("paths").GetProperty("passthrough").GetInt32());
        foreach (var location in summary.GetProperty("locations").EnumerateArray())
        {
            Assert.Equal("passthrough", location.GetProperty("path").GetString());
            Assert.Equal("round-trip-failure", location.GetProperty("trigger").GetString());
            Assert.Equal("passthroughOnError", location.GetProperty("enabledBy").GetString());
        }
    }

    [Fact]
    public async Task F6_REPORT_11_McpPassthroughWithCustomModuleName_Rescues()
    {
        var (isError, payload) = await Mcp("calor_convert",
            new { source = Fixture("F4-ITER-01"), passthroughOnError = true, moduleName = "Custom" });

        Assert.False(isError);
        Assert.Equal(2, payload.GetProperty("lossSummary").GetProperty("paths").GetProperty("passthrough").GetInt32());
    }

    /// <summary>The example quoted on website/content/cli/mcp.mdx, with and without passthroughOnError.</summary>
    [Fact]
    public async Task F6_REPORT_13_McpWebsiteExample_MatchesTheDocumentedPayload()
    {
        const string source = "public class Bag { public System.Collections.Generic.IEnumerable<int> Items { get { yield return 1; } } }";

        var (ptError, ptPayload) = await Mcp("calor_convert", new { source, passthroughOnError = true });
        Assert.False(ptError);
        var paths = ptPayload.GetProperty("lossSummary").GetProperty("paths");
        Assert.Equal("preserved", paths.GetProperty("outcome").GetString());
        Assert.Equal(1, paths.GetProperty("passthrough").GetInt32());
        Assert.Equal(1, paths.GetProperty("triggers").GetProperty("round-trip-failure").GetInt32());
        Assert.False(paths.GetProperty("rescueUnusableMembers").GetBoolean());
        Assert.True(paths.GetProperty("passthroughOnError").GetBoolean());

        var (defaultError, defaultPayload) = await Mcp("calor_convert", new { source });
        Assert.True(defaultError);
        Assert.Equal("refused", defaultPayload.GetProperty("lossSummary").GetProperty("paths").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task F6_REPORT_11_McpInputPath_DerivedModuleName_Rescues()
    {
        var input = Path.Combine(_tempDir, "IterBag.cs");
        File.WriteAllText(input, Fixture("F4-ITER-01"));

        var (isError, payload) = await Mcp("calor_convert", new { inputPath = input, passthroughOnError = true });

        Assert.False(isError);
        Assert.Equal(2, payload.GetProperty("lossSummary").GetProperty("paths").GetProperty("passthrough").GetInt32());
    }

    [Fact]
    public async Task F6_REPORT_13_McpInteropLocation_HasPathButNoTrigger()
    {
        var (isError, payload) = await Mcp("calor_convert", new { source = Fixture("F3-LOCAL-01") });

        Assert.False(isError);
        foreach (var location in payload.GetProperty("lossSummary").GetProperty("locations").EnumerateArray())
        {
            Assert.Equal("interop", location.GetProperty("path").GetString());
            Assert.False(location.TryGetProperty("trigger", out _));
        }
    }

    // ── Project migration: CLI migrate, MCP calor_migrate, calor_batch (F6-REPORT-04, -05, -06) ──

    private string Project(params string[] fixtureIds)
    {
        var dir = Path.Combine(_tempDir, "proj" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var id in fixtureIds)
            File.WriteAllText(Path.Combine(dir, id.Replace("-", "") + ".cs"), Fixture(id));
        return dir;
    }

    private static JsonElement FileEntry(JsonElement files, string fixtureId) =>
        files.EnumerateArray().Single(f =>
            (f.TryGetProperty("path", out var p) ? p : f.GetProperty("sourcePath")).GetString()!
                .EndsWith(fixtureId.Replace("-", "") + ".cs", StringComparison.Ordinal));

    [Fact]
    public void F6_REPORT_04_CliMigrate_ReportsParseFailureRescue_AndRefusesRoundTripFailure()
    {
        var dir = Project("F5-ARRAY-02", "F4-ITER-01");

        var (exit, stdout, _) = CliTestHarness.RunCli(dir, "migrate", dir, "--skip-verify", "--skip-analyze");

        // F4-ITER-01 fails (project migration has no round-trip rescue), and lossless
        // project migration then writes nothing, so both files fail; the rescue that
        // happened during conversion is still reported.
        Assert.Equal(1, exit);
        Assert.Contains("Preservation paths: 1 by automatic rescue (parse-failure: 1; no passthrough request needed)", stdout);
        Assert.Contains("Failed: 2", stdout);
    }

    [Fact]
    public async Task F6_REPORT_06_McpBatch_SuccessfulProject_ReportsRescueAndNative()
    {
        // Fixtures each declare Probe, so the native file is inline to keep the project compiling.
        var dir = Project("F5-ARRAY-02");
        File.WriteAllText(Path.Combine(dir, "F1REFOUT01.cs"), "public static class Plain { public static int One() => 1; }");

        var (isError, payload) = await Mcp("calor_batch", new { action = "convert", projectPath = dir, dryRun = true });

        Assert.False(isError);
        var files = payload.GetProperty("files");
        var rescued = FileEntry(files, "F5-ARRAY-02").GetProperty("conversionPaths");
        Assert.Equal("preserved", rescued.GetProperty("outcome").GetString());
        Assert.Equal(1, rescued.GetProperty("rescue").GetInt32());
        Assert.Equal("native", FileEntry(files, "F1-REFOUT-01").GetProperty("conversionPaths").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task F6_REPORT_11_McpBatch_ModuleNameOverride_KeepsParseFailureRescue()
    {
        var dir = Project("F5-ARRAY-02");

        var (isError, payload) = await Mcp("calor_batch",
            new { action = "convert", projectPath = dir, dryRun = true, moduleNameOverride = "Custom" });

        Assert.False(isError);
        var paths = FileEntry(payload.GetProperty("files"), "F5-ARRAY-02").GetProperty("conversionPaths");
        Assert.Equal("preserved", paths.GetProperty("outcome").GetString());
        Assert.Equal(1, paths.GetProperty("rescue").GetInt32());
    }

    [Fact]
    public async Task F6_REPORT_05_McpMigrate_ReportsPerFilePaths_IncludingRefusal()
    {
        var dir = Project("F5-ARRAY-02", "F4-ITER-01");

        var (_, payload) = await Mcp("calor_migrate", new { projectPath = dir, phase = "convert" });
        var files = payload.GetProperty("perFile");

        var rescued = FileEntry(files, "F5-ARRAY-02");
        Assert.Equal(1, rescued.GetProperty("conversionPaths").GetProperty("rescue").GetInt32());
        var loss = rescued.GetProperty("losses")[0];
        Assert.Equal("rescue", loss.GetProperty("path").GetString());
        Assert.Equal("parse-failure", loss.GetProperty("trigger").GetString());
        Assert.Equal("lossless-fidelity", loss.GetProperty("enabledBy").GetString());

        var refused = FileEntry(files, "F4-ITER-01");
        Assert.Equal("refused", refused.GetProperty("conversionPaths").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task F6_REPORT_06_McpBatch_PartialFailure_ReportsEachFilesPath_WithAndWithoutPassthrough()
    {
        var dir = Project("F5-ARRAY-02", "F4-ITER-01", "F1-REFOUT-01");

        foreach (var passthroughOnError in new[] { false, true })
        {
            var (isError, payload) = await Mcp("calor_batch",
                new { action = "convert", projectPath = dir, dryRun = true, passthroughOnError });
            Assert.True(isError); // one failed file
            var files = payload.GetProperty("files");

            var rescued = FileEntry(files, "F5-ARRAY-02").GetProperty("conversionPaths");
            Assert.Equal(1, rescued.GetProperty("rescue").GetInt32());
            Assert.Equal(1, rescued.GetProperty("triggers").GetProperty("parse-failure").GetInt32());
            Assert.Equal(passthroughOnError, rescued.GetProperty("passthroughOnError").GetBoolean());

            // Documented difference: project migration does not run the per-file
            // round-trip rescue, so passthroughOnError does not rescue this file.
            var refused = FileEntry(files, "F4-ITER-01").GetProperty("conversionPaths");
            Assert.Equal("refused", refused.GetProperty("outcome").GetString());

            // Lossless project migration is all-or-nothing: one failed file means no
            // file is written, so every file reports refused while keeping its counts.
            Assert.Equal("refused", rescued.GetProperty("outcome").GetString());
            var native = FileEntry(files, "F1-REFOUT-01").GetProperty("conversionPaths");
            Assert.Equal("refused", native.GetProperty("outcome").GetString());
            Assert.Equal(0, native.GetProperty("interop").GetInt32() + native.GetProperty("rescue").GetInt32());
        }
    }
}
