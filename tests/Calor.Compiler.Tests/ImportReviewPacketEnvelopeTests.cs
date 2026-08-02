using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Envelope conformance for the two WS-W3 adoption-surface commands
/// (<c>calor import</c>, <c>calor review-packet</c>): in JSON mode stdout
/// carries exactly one parseable envelope document on every path, including
/// early exits; diagnostics carry the registered Calor135x codes; and the
/// import provenance invariants (derived = inferred, contracts = assumed,
/// never verified) survive the CLI boundary.
/// </summary>
public class ImportReviewPacketEnvelopeTests : IDisposable
{
    private readonly string _tempDir;

    public ImportReviewPacketEnvelopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-w3-envelope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private static JsonElement ParseSingleDocument(string stdOut)
    {
        using var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement.Clone();
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);
        return root;
    }

    // ------------------------------------------------------------------
    // calor import
    // ------------------------------------------------------------------

    [Fact]
    public void Import_Json_PackageNotFound_StillEmitsEnvelope()
    {
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "import", "no.such.package.zzz", "--json");

        Assert.Equal(1, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("import", root.GetProperty("command").GetString());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.ImportInputNotFound, diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
    }

    [Fact]
    public void Import_Json_RealAssembly_ReportsTiersAndProvenance()
    {
        // Calor.Runtime.dll ships beside the test assembly (transitive
        // project reference) — a small, real assembly with NRT metadata.
        var runtimeDll = Path.Combine(AppContext.BaseDirectory, "Calor.Runtime.dll");
        Assert.True(File.Exists(runtimeDll), $"expected {runtimeDll}");

        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "import", runtimeDll, "--json");

        Assert.Equal(0, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("import", root.GetProperty("command").GetString());

        var data = root.GetProperty("data");
        var counts = data.GetProperty("counts");
        Assert.True(counts.GetProperty("publicMethods").GetInt32() > 0);

        // The three tiers partition the surface.
        Assert.Equal(counts.GetProperty("publicMethods").GetInt32(),
            counts.GetProperty("derived").GetInt32()
            + counts.GetProperty("curated").GetInt32()
            + counts.GetProperty("unresolved").GetInt32());

        // Provenance invariants across the CLI boundary (PP-T1 shape).
        var provenance = data.GetProperty("provenance");
        Assert.Equal("inferred", provenance.GetProperty("derived").GetString());
        Assert.Equal("assumed", provenance.GetProperty("synthesizedContracts").GetString());

        var manifest = data.GetProperty("manifest");
        Assert.Equal("inferred", manifest.GetProperty("confidence").GetString());
        Assert.NotEqual("verified", manifest.GetProperty("confidence").GetString());

        // Every synthesized contract fact rides the assumed channel.
        foreach (var contract in data.GetProperty("synthesizedContracts").EnumerateArray())
        {
            Assert.Equal("assumed", contract.GetProperty("provenance").GetString());
        }
    }

    // ------------------------------------------------------------------
    // calor review-packet
    // ------------------------------------------------------------------

    private string WriteModule()
    {
        var path = Path.Combine(_tempDir, "pricing.calr");
        File.WriteAllText(path,
            "§M{m001:Pricing}\n"
            + "  §F{f001:ClampToCap:pub} (i32:amount, i32:cap) -> i32\n"
            + "    §Q (>= cap 0)\n"
            + "    §IF{if1} (> amount cap)\n"
            + "      §R cap\n"
            + "    §R amount\n");
        return path;
    }

    [Fact]
    public void ReviewPacket_Json_EmitsEnvelope_WithHonestyNote()
    {
        var module = WriteModule();
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "review-packet", module, "--json");

        Assert.Equal(0, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("review-packet", root.GetProperty("command").GetString());

        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("summary").GetProperty("totalContracts").GetInt32() >= 1);

        // The #782 honesty note is unconditional.
        var notes = data.GetProperty("honestyNotes").EnumerateArray()
            .Select(n => n.GetString()).ToList();
        Assert.Contains(notes, n => n != null && n.Contains("#782"));
    }

    [Fact]
    public void ReviewPacket_Json_ContractModeOff_DisclosesWaiver()
    {
        var module = WriteModule();
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "review-packet", module, "--contract-mode", "off", "--json");

        Assert.Equal(0, exitCode);
        var root = ParseSingleDocument(stdOut);

        var codes = root.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("code").GetString()).ToList();
        Assert.Contains(DiagnosticCode.ReviewPacketWaiverDisclosure, codes);

        var waivers = root.GetProperty("data").GetProperty("waivers").EnumerateArray()
            .Select(w => w.GetString()).ToList();
        Assert.Contains(waivers, w => w != null && w.Contains("WAIVER"));
    }

    [Fact]
    public void ReviewPacket_Json_MissingFile_StillEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "nope.calr");
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "review-packet", missing, "--json");

        Assert.Equal(1, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("review-packet", root.GetProperty("command").GetString());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.ReviewPacketCommandError, diagnostic.GetProperty("code").GetString());
    }

    [Fact]
    public void ReviewPacket_Markdown_LeadsWithUnprovenRemainder()
    {
        var module = WriteModule();
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "review-packet", module);

        Assert.Equal(0, exitCode);
        Assert.Contains("## Unproven remainder", stdOut);
        Assert.Contains("NOT runtime-enforced", stdOut);
    }
}
