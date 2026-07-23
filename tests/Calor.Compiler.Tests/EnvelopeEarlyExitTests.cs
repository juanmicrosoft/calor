using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Envelope schema v1.1 invariant: in JSON mode stdout carries exactly one
/// parseable document on EVERY path, including early exits (ultrareview
/// finding on PR #754 — ids check / analyze-convertibility / feature-check
/// returned with an empty stdout on their error paths).
/// </summary>
public class EnvelopeEarlyExitTests : IDisposable
{
    private readonly string _tempDir;

    public EnvelopeEarlyExitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-early-exit-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void IdsCheck_Json_NoCalrFiles_StillEmitsEnvelope()
    {
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "ids", "check", _tempDir, "--format", "json");

        Assert.Equal(1, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("ids", root.GetProperty("command").GetString());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.CliInputNotFound, diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        Assert.Equal(0, root.GetProperty("data").GetProperty("totalIds").GetInt32());
    }

    [Fact]
    public void AnalyzeConvertibility_Json_PathNotFound_StillEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "no-such-path");
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "analyze-convertibility", missing, "--format", "json");

        Assert.Equal(2, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("analyze-convertibility", root.GetProperty("command").GetString());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.CliInputNotFound, diagnostic.GetProperty("code").GetString());
        Assert.Contains("not found", diagnostic.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeatureCheck_List_UnknownLevel_StillEmitsEnvelope()
    {
        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "feature-check", "--list", "--level", "definitely-not-a-level");

        Assert.Equal(1, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("feature-check", root.GetProperty("command").GetString());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.CliUsageError, diagnostic.GetProperty("code").GetString());
        Assert.Contains("Unknown support level", diagnostic.GetProperty("message").GetString());
    }
}
