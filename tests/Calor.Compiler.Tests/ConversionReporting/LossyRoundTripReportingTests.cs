using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Mcp;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests.ConversionReporting;

/// <summary>
/// #1514: <c>--lossy</c> used to skip the round-trip compile check, so a file whose
/// output does not compile could be reported as a clean conversion (no recorded loss,
/// "✓ Conversion successful", exit 0). Lossy mode now runs the check. A failure keeps
/// the output but reports <c>roundTrip: "failed"</c> and <c>outcome: "unverified"</c>,
/// exits 2 on the CLI and sets <c>isError</c> in MCP. Lossless behavior is unchanged,
/// and callers that compile the C# themselves (project migration, the round-trip
/// harness) keep their opt-out, reported as <c>roundTrip: "deferred"</c>.
/// </summary>
public class LossyRoundTripReportingTests : IDisposable
{
    /// <summary>A label before the last statement: the lossy output drops the return and does not compile.</summary>
    private const string TrailingLabel = "public class G { public int M(int x) { here: return x; } }";

    /// <summary>A second, unrelated input: yield inside try/finally emits C# that does not compile.</summary>
    private const string YieldInTryFinally =
        "public class P { public System.Collections.Generic.IEnumerable<int> M() { try { yield return 1; } finally { System.Console.WriteLine(); } } }";

    /// <summary>Negative control: converts natively and compiles.</summary>
    private const string Plain = "public static class Plain { public static int Add(int a, int b) => a + b; }";

    /// <summary>Negative control with a recorded lossy drop (the destructor) whose output still compiles.</summary>
    private const string Destructor = "public class D { public int V = 1; ~D() { V = 0; } public int M() => V; }";

    private static readonly string[] FailingInputs = [TrailingLabel, YieldInTryFinally];

    private readonly string _tempDir = Directory.CreateTempSubdirectory("calor-1514-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private static ConversionResult Convert(string source, ConversionOptions options) =>
        new CSharpToCalorConverter(options).Convert(source);

    private static ConversionOptions Lossy(bool validate = true) => new()
    {
        Fidelity = ConversionFidelity.Lossy,
        ValidateRoundTripCSharp = validate
    };

    // ── Library ─────────────────────────────────────────────────────────

    [Fact]
    public void Library_Lossy_FailingRoundTrip_IsUnverified_NotClean()
    {
        foreach (var source in FailingInputs)
        {
            var result = Convert(source, Lossy());

            Assert.True(result.Success); // lossy keeps its output
            Assert.NotNull(result.CalorSource);
            Assert.False(result.HasErrors);
            Assert.Equal(ConversionRoundTrip.Failed, result.Context.RoundTrip);
            Assert.Equal(ConversionRoundTrip.Failed, result.Paths.RoundTrip);
            Assert.Equal("unverified", result.Paths.Outcome);
            Assert.Equal(ConversionPathSummary.CurrentSchemaVersion, result.Paths.SchemaVersion);
            Assert.Contains(result.Issues, i =>
                i.Severity == ConversionIssueSeverity.Warning
                && i.Feature == "lossy-roundtrip-unverified");
            Assert.Contains(result.Issues, i =>
                i.Severity == ConversionIssueSeverity.Warning
                && i.Feature is "roundtrip-csharp-validation" or "roundtrip-calor-validation");
        }
    }

    [Fact]
    public void Library_Lossy_ValidOutput_StaysClean()
    {
        var plain = Convert(Plain, Lossy());
        Assert.True(plain.Success);
        Assert.Equal(ConversionRoundTrip.Verified, plain.Paths.RoundTrip);
        Assert.Equal("native", plain.Paths.Outcome);
        Assert.DoesNotContain(plain.Issues, i => i.Feature == "lossy-roundtrip-unverified");

        var dropped = Convert(Destructor, Lossy());
        Assert.True(dropped.Success);
        Assert.Equal(ConversionRoundTrip.Verified, dropped.Paths.RoundTrip);
        Assert.Equal("lossy", dropped.Paths.Outcome);
    }

    [Fact]
    public void Library_Lossless_IsUnchanged_RefusesAndReportsTheFailedCheck()
    {
        var refused = Convert(TrailingLabel, new ConversionOptions());
        Assert.False(refused.Success);
        Assert.Equal("refused", refused.Paths.Outcome);
        Assert.Equal(ConversionRoundTrip.Failed, refused.Paths.RoundTrip);
        Assert.Contains(refused.Issues, i =>
            i.Severity == ConversionIssueSeverity.Error && i.Feature == "roundtrip-csharp-validation");
        Assert.DoesNotContain(refused.Issues, i => i.Feature == "lossy-roundtrip-unverified");

        var native = Convert(Plain, new ConversionOptions());
        Assert.True(native.Success);
        Assert.Equal("native", native.Paths.Outcome);
        Assert.Equal(ConversionRoundTrip.Verified, native.Paths.RoundTrip);
    }

    [Fact]
    public void Library_OptOut_IsReportedAsDeferred_AndAddsNoDiagnostics()
    {
        // The round-trip harness converts with Lossy and ValidateRoundTripCSharp = false
        // because it compiles the bytes itself. Its results must not change.
        foreach (var source in FailingInputs)
        {
            var optedOut = Convert(source, Lossy(validate: false));
            Assert.True(optedOut.Success);
            Assert.Equal(ConversionRoundTrip.Deferred, optedOut.Paths.RoundTrip);
            Assert.NotEqual("unverified", optedOut.Paths.Outcome);
            Assert.DoesNotContain(optedOut.Issues, i =>
                i.Feature is "lossy-roundtrip-unverified" or "roundtrip-csharp-validation" or "roundtrip-calor-validation");
            Assert.Equal(Convert(source, Lossy(validate: true)).CalorSource, optedOut.CalorSource);
        }
    }

    [Fact]
    public void Library_ParseRefusal_ReportsNotRun()
    {
        var result = Convert("public class Broken { public int M( { }", Lossy());
        Assert.False(result.Success);
        Assert.Equal(ConversionRoundTrip.NotRun, result.Paths.RoundTrip);
        Assert.Equal("refused", result.Paths.Outcome);
    }

    [Fact]
    public void Library_LossyPassthrough_ReportsButDoesNotRescueTheRoundTripFailure()
    {
        // The lossy check only reports. It does not open the round-trip rescue: a file that
        // depends on a sibling file also fails a standalone compile, and a rescue that cannot
        // recover would turn PassthroughOnError's completed lossy conversion into a refusal
        // (Issue769NamespaceTopologyTests pins that case).
        var result = Convert(TrailingLabel, new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PassthroughOnError = true
        });

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Losses, l => l.Feature == "post-validation-fallback");
        Assert.Equal(ConversionRoundTrip.Failed, result.Paths.RoundTrip);
        Assert.Equal("unverified", result.Paths.Outcome);
    }

    [Fact]
    public void Library_Lossy_SiblingDependency_IsUnverified_NotRefused()
    {
        // A derived class whose base lives in another file: correct in its project, but a
        // standalone compile cannot see the base. Lossy keeps the output and says it is unverified.
        const string derived = "namespace N; public class D : B { public override int M() => 1; }";
        foreach (var passthrough in new[] { false, true })
        {
            var result = Convert(derived, new ConversionOptions
            {
                Fidelity = ConversionFidelity.Lossy,
                PassthroughOnError = passthrough
            });

            Assert.True(result.Success);
            Assert.Equal("unverified", result.Paths.Outcome);
        }
    }

    [Fact]
    public void Summary_UnverifiedOutcome_TakesPrecedenceOverCounts_ButNotOverRefusal()
    {
        var losses = new[]
        {
            new ConversionLoss { Kind = ConversionLossKind.Dropped, Feature = "x", Description = "d" }
        };
        var summary = ConversionPathSummary.From(losses, success: true, false, false, ConversionRoundTrip.Failed);
        Assert.Equal("unverified", summary.Outcome);
        Assert.Equal("refused", summary.WithSuccess(false).Outcome);
        Assert.Equal("unverified", summary.WithSuccess(false).WithSuccess(true).Outcome);
        Assert.Equal("lossy", ConversionPathSummary.From(losses, true, false, false, ConversionRoundTrip.Deferred).Outcome);
    }

    // ── CLI convert ─────────────────────────────────────────────────────

    private (int Exit, string Out, string Err, string OutputPath) Cli(string source, params string[] flags)
    {
        var dir = Path.Combine(_tempDir, "cli" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "In.cs"), source);
        var (exit, stdout, stderr) = CliTestHarness.RunCli(dir, ["convert", "In.cs", "-o", "Out.calr", .. flags]);
        return (exit, stdout, stderr, Path.Combine(dir, "Out.calr"));
    }

    private static JsonElement Data(string stdout) => JsonDocument.Parse(stdout).RootElement.GetProperty("data");

    [Fact]
    public void Cli_Lossy_FailingRoundTrip_WritesOutput_ExitsTwo_AndSaysSo()
    {
        foreach (var source in FailingInputs)
        {
            var (exit, stdout, stderr, output) = Cli(source, "--lossy");

            Assert.True(exit == 2, $"exit {exit}\n{stdout}\n{stderr}");
            Assert.True(File.Exists(output));
            Assert.DoesNotContain("✓ Conversion successful", stdout);
            Assert.Contains("✗ Round-trip check failed: the generated C# does not compile", stdout);
            Assert.Contains("it is unverified (exit 2)", stdout);
        }
    }

    [Fact]
    public void Cli_LossyJson_FailingRoundTrip_ReportsUnverified()
    {
        foreach (var source in FailingInputs)
        {
            var (exit, stdout, stderr, output) = Cli(source, "--lossy", "--format", "json");

            Assert.True(exit == 2, stderr);
            Assert.True(File.Exists(output));
            var data = Data(stdout);
            Assert.True(data.GetProperty("success").GetBoolean());
            var paths = data.GetProperty("conversionPaths");
            Assert.Equal(2, paths.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("unverified", paths.GetProperty("outcome").GetString());
            Assert.Equal("failed", paths.GetProperty("roundTrip").GetString());

            var diagnostics = JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics").EnumerateArray()
                .Select(d => d.GetProperty("message").GetString()!)
                .ToList();
            Assert.Contains(diagnostics, m => m.StartsWith("[lossy-roundtrip-unverified]", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Cli_Lossy_ValidOutput_StaysClean()
    {
        var (exit, stdout, stderr, output) = Cli(Plain, "--lossy");
        Assert.True(exit == 0, stderr);
        Assert.True(File.Exists(output));
        Assert.Contains("✓ Conversion successful", stdout);
        Assert.DoesNotContain("Round-trip check failed", stdout);

        var (jsonExit, json, jsonErr, _) = Cli(Plain, "--lossy", "--format", "json");
        Assert.True(jsonExit == 0, jsonErr);
        var paths = Data(json).GetProperty("conversionPaths");
        Assert.Equal("native", paths.GetProperty("outcome").GetString());
        Assert.Equal("verified", paths.GetProperty("roundTrip").GetString());
    }

    [Fact]
    public void Cli_Default_IsUnchanged_RescuesAndVerifies()
    {
        var (exit, stdout, stderr, _) = Cli(TrailingLabel, "--format", "json");
        Assert.True(exit == 0, stderr);
        var paths = Data(stdout).GetProperty("conversionPaths");
        Assert.Equal("preserved", paths.GetProperty("outcome").GetString());
        Assert.Equal(1, paths.GetProperty("rescue").GetInt32());
        Assert.Equal("verified", paths.GetProperty("roundTrip").GetString());
    }

    /// <summary>
    /// The keys of <c>data.conversionPaths</c> match the example on
    /// website/content/cli/convert.mdx, and every documented outcome and
    /// roundTrip value is one the code can produce.
    /// </summary>
    [Fact]
    public void Cli_ConversionPathsFields_MatchTheDocumentation()
    {
        var page = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(), "website/content/cli/convert.mdx"));
        var example = Regex.Match(page, "```json illustrative\\s*\"conversionPaths\": (\\{.*?\\n\\})", RegexOptions.Singleline);
        Assert.True(example.Success, "convert.mdx no longer carries the conversionPaths example");
        var documented = JsonDocument.Parse(example.Groups[1].Value).RootElement
            .EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var (_, stdout, _, _) = Cli(TrailingLabel, "--lossy", "--format", "json");
        var actual = Data(stdout).GetProperty("conversionPaths")
            .EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(documented, actual);

        foreach (var value in new[] { "native", "preserved", "lossy", "unverified", "refused" })
            Assert.Contains($"`{value}`", page);
        foreach (var value in new[]
                 {
                     ConversionRoundTrip.Verified, ConversionRoundTrip.Failed,
                     ConversionRoundTrip.Deferred, ConversionRoundTrip.NotRun
                 })
            Assert.Contains($"`{value}`", page);
    }

    // ── MCP calor_convert ───────────────────────────────────────────────

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
    public async Task Mcp_Lossy_FailingRoundTrip_IsAnError_WithUnverifiedOutcome()
    {
        foreach (var source in FailingInputs)
        {
            var output = Path.Combine(_tempDir, $"mcp{Guid.NewGuid():N}.calr");
            var (isError, payload) = await Mcp("calor_convert", new { source, fidelity = "lossy", outputPath = output });

            Assert.True(isError);
            Assert.False(string.IsNullOrEmpty(payload.GetProperty("calorSource").GetString()));
            Assert.True(File.Exists(output));
            var paths = payload.GetProperty("lossSummary").GetProperty("paths");
            Assert.Equal("unverified", paths.GetProperty("outcome").GetString());
            Assert.Equal("failed", paths.GetProperty("roundTrip").GetString());
        }
    }

    [Fact]
    public async Task Mcp_Lossy_ValidOutput_StaysClean()
    {
        var (isError, payload) = await Mcp("calor_convert", new { source = Plain, fidelity = "lossy" });

        Assert.False(isError);
        var paths = payload.GetProperty("lossSummary").GetProperty("paths");
        Assert.Equal("native", paths.GetProperty("outcome").GetString());
        Assert.Equal("verified", paths.GetProperty("roundTrip").GetString());
    }

    [Fact]
    public async Task McpBatch_ProjectMigration_ReportsDeferred()
    {
        // Project migration compiles every generated file together, so each file's
        // own check is deferred to that step rather than skipped silently.
        var dir = Path.Combine(_tempDir, "proj" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Plain.cs"), Plain);

        var (isError, payload) = await Mcp("calor_batch", new { action = "convert", projectPath = dir, dryRun = true });

        Assert.False(isError);
        var file = Assert.Single(payload.GetProperty("files").EnumerateArray());
        var paths = file.GetProperty("conversionPaths");
        Assert.Equal("native", paths.GetProperty("outcome").GetString());
        Assert.Equal("deferred", paths.GetProperty("roundTrip").GetString());
    }
}
