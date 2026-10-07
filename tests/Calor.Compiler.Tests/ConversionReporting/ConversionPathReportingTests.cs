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

    private const string Destructor = "public class D { public int V = 1; ~D() { V = 0; } }";

    [Fact]
    public void F6_REPORT_12_LossyStandard_PreservationOnlyThroughPassthrough_IsPassthrough()
    {
        // Lossless keeps the destructor as converter interop; lossy drops it; lossy plus
        // passthrough keeps it only because passthrough opened the preservation gate.
        var lossless = Convert(Destructor, new ConversionOptions());
        Assert.Contains(lossless.Losses, l => l.Path == ConversionPath.Interop && l.Trigger == null);

        var lossy = Convert(Destructor, new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        Assert.DoesNotContain(lossy.Losses, l => l.Path is ConversionPath.Interop or ConversionPath.Passthrough);

        var lossyPassthrough = Convert(Destructor, new ConversionOptions { Fidelity = ConversionFidelity.Lossy, PassthroughOnError = true });
        var kept = Assert.Single(lossyPassthrough.Losses, l => l.Kind == ConversionLossKind.InteropPreserved);
        Assert.Equal(ConversionPath.Passthrough, kept.Path);
        Assert.Equal(ConversionTrigger.UnsupportedConstruct, kept.Trigger);
        Assert.Equal(ConversionEnabledBy.PassthroughOnError, kept.EnabledBy);

        // A preservation that happens without passthrough stays interop under lossy plus passthrough.
        var usingDecl = Convert("public class S { public int M() { using var d = new System.IO.MemoryStream(); return 1; } }",
            new ConversionOptions { Fidelity = ConversionFidelity.Lossy, PassthroughOnError = true });
        var usingKept = usingDecl.Losses.Where(l => l.Kind == ConversionLossKind.InteropPreserved).ToList();
        Assert.NotEmpty(usingKept);
        Assert.All(usingKept, l => Assert.Equal(ConversionPath.Interop, l.Path));

        // An escalated expression is preserved without passthrough too (at the member
        // boundary), so passthrough moving it to the statement boundary is not passthrough.
        const string escalating = "public class P { public int M(int x) { x >>>= 1; return x; } }";
        var lossyEscalation = Convert(escalating, new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        Assert.Contains(lossyEscalation.Losses, l => l.Path == ConversionPath.Interop);
        var ptEscalation = Convert(escalating, new ConversionOptions { Fidelity = ConversionFidelity.Lossy, PassthroughOnError = true });
        var escalated = ptEscalation.Losses.Where(l => l.Kind == ConversionLossKind.InteropPreserved).ToList();
        Assert.NotEmpty(escalated);
        Assert.All(escalated, l => Assert.Equal(ConversionPath.Interop, l.Path));
    }

    [Fact]
    public void F6_REPORT_12_InterfaceMemberEscalation_UnderLossyPassthrough_StaysInterop()
    {
        const string source = "public interface I { void M(int x = 8 >>> 1); }";
        foreach (var passthrough in new[] { false, true })
        {
            var result = Convert(source, new ConversionOptions { Fidelity = ConversionFidelity.Lossy, PassthroughOnError = passthrough });
            var kept = result.Losses.Where(l => l.Kind == ConversionLossKind.InteropPreserved).ToList();
            Assert.NotEmpty(kept);
            Assert.All(kept, l => Assert.Equal(ConversionPath.Interop, l.Path));
        }
    }

    [Fact]
    public void F6_REPORT_13_StaleGateRead_DoesNotLabelALaterLoss()
    {
        var context = new ConversionContext { Fidelity = ConversionFidelity.Lossy, PassthroughOnError = true };
        Assert.True(context.ShouldPreserveCSharp);
        context.Fidelity = ConversionFidelity.Lossless;
        context.RecordLoss(ConversionLossKind.InteropPreserved, "record", "kept");
        Assert.Equal(ConversionPath.Interop, context.Losses[^1].Path);

        context.Fidelity = ConversionFidelity.Lossy;
        Assert.True(context.ShouldPreserveCSharp);
        context.RecordLoss(ConversionLossKind.Dropped, "x", "dropped");
        context.RecordLoss(ConversionLossKind.InteropPreserved, "record", "kept");
        Assert.Equal(ConversionPath.Interop, context.Losses[^1].Path);
    }

    [Fact]
    public void F6_REPORT_11_ModuleNameAlias_RefusesAmbiguousGlobalAndNamespaceTypes()
    {
        // Global Probe would be keyed as Custom.Probe; a real Custom.Probe makes that ambiguous.
        var source = Fixture("F5-ARRAY-02").Replace("public static class Probe", "public static partial class Probe")
            + "\nnamespace Custom { public static partial class Probe { public static int Other() => 1; } }\n";
        Assert.Contains("partial class Probe", source);

        var result = Convert(source, new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy, PassthroughOnError = true, ModuleName = "Custom"
        });

        Assert.DoesNotContain(result.Losses, l => l.Feature == "post-validation-fallback" && l.Description.Contains("Custom.Probe"));

        // Control: without the real Custom.Probe the aliased global Probe is rescued.
        var control = Convert(Fixture("F5-ARRAY-02"), new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy, PassthroughOnError = true, ModuleName = "Custom"
        });
        Assert.Contains(control.Losses, l => l.Feature == "post-validation-fallback" && l.Description.Contains("Custom.Probe"));
    }

    [Fact]
    public void F6_REPORT_13_WithSuccess_RecomputesTheOutcomeFromCounts()
    {
        var preserved = Convert(Fixture("F5-ARRAY-02"), new ConversionOptions()).Paths;
        Assert.Equal("preserved", preserved.Outcome);
        Assert.Equal("refused", preserved.WithSuccess(false).Outcome);
        Assert.Equal("preserved", preserved.WithSuccess(false).WithSuccess(true).Outcome);
        Assert.Equal("native", Convert(Fixture("F1-REFOUT-01"), new ConversionOptions()).Paths.WithSuccess(false).WithSuccess(true).Outcome);
    }

    [Fact]
    public void F6_REPORT_12_Precedence_SelectedBranchLossy_BothOptions_IsPassthrough()
    {
        // Under lossy fidelity only passthrough opens the preservation gate, so even with
        // RescueUnusableMembers on the rescue could not have fired without the request.
        ConversionOptions Options(bool rescue, bool passthrough) => new()
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            RescueUnusableMembers = rescue,
            PassthroughOnError = passthrough
        };

        Assert.Equal("refused", Convert(TrailingLabel, Options(rescue: true, passthrough: false)).Paths.Outcome);
        AssertPaths(Convert(TrailingLabel, Options(rescue: true, passthrough: true)),
            ConversionPath.Passthrough, ConversionTrigger.RoundTripFailure, ConversionEnabledBy.PassthroughOnError, 1);
    }

    [Fact]
    public async Task F6_REPORT_07_LibraryMissingFile_ReportsTheConfiguredOptions()
    {
        var result = await new CSharpToCalorConverter(new ConversionOptions { RescueUnusableMembers = true, PassthroughOnError = true })
            .ConvertFileAsync(Path.Combine(_tempDir, "missing.cs"));

        Assert.Equal("refused", result.Paths.Outcome);
        Assert.True(result.Paths.RescueUnusableMembers);
        Assert.True(result.Paths.PassthroughOnError);
    }

    // ── CLI convert (F6-REPORT-01, -02, -03, -10, -12 CLI cells) ──────────

    private (int Exit, string Out, string Err) Cli(string source, params string[] flags)
        => CliNamed($"In{Guid.NewGuid():N}.cs", source, flags);

    /// <summary>Runs <c>calor convert</c> on a file given by its relative name, as a user would type it.</summary>
    private (int Exit, string Out, string Err) CliNamed(string fileName, string source, params string[] flags)
    {
        var dir = Path.Combine(_tempDir, "cli" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), source);
        return CliTestHarness.RunCli(dir, ["convert", fileName, "-o", Path.ChangeExtension(fileName, ".calr"), .. flags]);
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

        var (exit, stdout, stderr) = CliNamed("Counter.cs", counter);
        Assert.True(exit == 0, stderr);
        // The CLI prints the input's full path before "Counter.cs".
        Assert.Contains("/Counter.cs:? [post-validation-fallback] [rescue: round-trip-failure] Converted Calor for 'global::Counter' did not parse or did not survive the C# round trip; original C# preserved (#717).", stdout);
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
    public void F6_REPORT_10_CliLossy_RoundTripIsNotChecked_SoNoRescueFires()
    {
        // --lossy skips the generated-C# round trip, so the rescue never fires and the
        // report says so: no rescue, no trigger. (The lossy contract allows this.)
        foreach (var flags in new[] { new[] { "--lossy", "--format", "json" }, new[] { "--lossy", "--passthrough", "--format", "json" } })
        {
            var (exit, stdout, stderr) = Cli(TrailingLabel, flags);
            Assert.True(exit == 0, stderr);
            var paths = Data(stdout).GetProperty("conversionPaths");
            Assert.Equal(0, paths.GetProperty("rescue").GetInt32() + paths.GetProperty("passthrough").GetInt32());
            Assert.Empty(paths.GetProperty("triggers").EnumerateObject());
        }
    }

    [Fact]
    public void F6_REPORT_10_CliWriteFailure_KeepsLossesAndReportsRefused()
    {
        // The output path is an existing directory, so the write fails after conversion.
        var dir = Path.Combine(_tempDir, "w" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Out.calr"));
        File.WriteAllText(Path.Combine(dir, "In.cs"), TrailingLabel);

        var (exit, stdout, _) = CliTestHarness.RunCli(dir, "convert", "In.cs", "-o", "Out.calr", "--format", "json");

        Assert.Equal(1, exit);
        var data = Data(stdout);
        Assert.False(data.GetProperty("success").GetBoolean());
        Assert.Equal("refused", data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        Assert.Equal("rescue", data.GetProperty("losses")[0].GetProperty("path").GetString());
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
        var (isError, text) = await McpText(tool, arguments);
        return (isError, JsonDocument.Parse(text).RootElement);
    }

    private static async Task<(bool IsError, string Text)> McpText(string tool, object arguments)
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
        return (isError, text);
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
    public async Task F6_REPORT_13_McpValidateAndRoundtripModes_CarryPaths()
    {
        var (_, validate) = await Mcp("calor_convert", new { source = Fixture("F5-ARRAY-02"), mode = "validate" });
        var validatePaths = validate.GetProperty("lossSummary").GetProperty("paths");
        Assert.Equal(1, validatePaths.GetProperty("rescue").GetInt32());

        var (_, roundtrip) = await Mcp("calor_convert", new { source = TrailingLabel, mode = "roundtrip", passthroughOnError = true });
        var location = Assert.Single(roundtrip.GetProperty("lossSummary").GetProperty("locations").EnumerateArray());
        Assert.Equal("passthrough", location.GetProperty("path").GetString());
        Assert.Equal("round-trip-failure", location.GetProperty("trigger").GetString());
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
        // Each fixture declares Probe (and some share helper type names); suffix every
        // identifier that could collide so a failure comes only from the fixture itself.
        foreach (var id in fixtureIds)
        {
            var suffix = id.Replace("-", "");
            var text = Fixture(id);
            foreach (var name in new[] { "Probe", "Bag", "Grid" })
                text = System.Text.RegularExpressions.Regex.Replace(text, $@"\b{name}\b", name + suffix);
            File.WriteAllText(Path.Combine(dir, suffix + ".cs"), text);
        }
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
    public async Task F6_REPORT_06_McpBatchSummaryMode_ReportsPaths()
    {
        var dir = Project("F5-ARRAY-02", "F4-ITER-01");

        var (_, payload) = await Mcp("calor_batch", new { action = "convert", projectPath = dir, dryRun = true, summary = true });

        Assert.Equal("Preservation paths: 1 by automatic rescue (parse-failure: 1; no passthrough request needed)",
            payload.GetProperty("preservationPaths").GetString());
        Assert.All(payload.GetProperty("failedFiles").EnumerateArray(), f =>
            Assert.Equal("refused", f.GetProperty("conversionPaths").GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task F6_REPORT_06_McpBatchAbort_KeepsPreservationPaths()
    {
        var dir = Project("F5-ARRAY-02", "F4-ITER-01");

        var (isError, payload) = await Mcp("calor_batch", new { action = "convert", projectPath = dir, dryRun = true, skipOnError = false });

        Assert.True(isError);
        Assert.StartsWith("Batch aborted (skipOnError=false)", payload.GetProperty("error").GetString());
        Assert.Equal("Preservation paths: 1 by automatic rescue (parse-failure: 1; no passthrough request needed)",
            payload.GetProperty("preservationPaths").GetString());
        var rescued = FileEntry(payload.GetProperty("files"), "F5-ARRAY-02").GetProperty("conversionPaths");
        Assert.Equal(1, rescued.GetProperty("rescue").GetInt32());
        Assert.Equal("refused", rescued.GetProperty("outcome").GetString());
    }

    [Fact]
    public void F6_REPORT_04_CliMigrateMarkdown_NamesEachFilesPaths_EvenWhenRefused()
    {
        var dir = Project("F5-ARRAY-02", "F4-ITER-01");
        var md = Path.Combine(_tempDir, $"r{Guid.NewGuid():N}.md");

        Assert.Equal(1, CliTestHarness.RunCli(dir, "migrate", dir, "--skip-verify", "--skip-analyze", "--report", md).ExitCode);

        var report = File.ReadAllText(md);
        Assert.Contains("### Failed", report);
        Assert.Contains("  - rescue (parse-failure, enabled by lossless-fidelity): Converted Calor for 'global::ProbeF5ARRAY02'", report);
    }

    [Fact]
    public void F6_REPORT_04_CliMigrateReports_MarkdownAndJsonCarryPaths()
    {
        var dir = Project("F5-ARRAY-02");
        var md = Path.Combine(_tempDir, $"r{Guid.NewGuid():N}.md");
        var json = Path.Combine(_tempDir, $"r{Guid.NewGuid():N}.json");

        Assert.Equal(0, CliTestHarness.RunCli(dir, "migrate", dir, "--skip-verify", "--skip-analyze", "--report", md).ExitCode);
        Assert.Contains("Preservation paths: 1 by automatic rescue (parse-failure: 1", File.ReadAllText(md));

        Assert.Equal(0, CliTestHarness.RunCli(dir, "migrate", dir, "--skip-verify", "--skip-analyze", "--report", json).ExitCode);
        var file = JsonDocument.Parse(File.ReadAllText(json)).RootElement.GetProperty("data").GetProperty("fileResults")[0];
        Assert.Equal("preserved", file.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        var loss = file.GetProperty("losses")[0];
        Assert.Equal("rescue", loss.GetProperty("path").GetString());
        Assert.Equal("parse-failure", loss.GetProperty("trigger").GetString());
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
