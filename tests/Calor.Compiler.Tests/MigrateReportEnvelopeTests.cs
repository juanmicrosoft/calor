using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// The `calor migrate --report file.json` report is an envelope schema v1.1
/// document ({version, command, diagnostics, summary, data}) with the
/// pre-existing report shape unchanged under `data` (loop plan D1.3 — final
/// envelope sweep). The .md report and text stdout are unchanged.
/// </summary>
public class MigrateReportEnvelopeTests : IDisposable
{
    private readonly string _tempDir;

    public MigrateReportEnvelopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-migrate-envelope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void MigrateReport_Json_IsEnvelopeWrapped()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "Simple.cs"),
            "namespace TestNs { public class Foo { public int Bar() => 42; } }");
        var reportPath = Path.Combine(_tempDir, "report.json");

        var (exitCode, _, stdErr) = CliTestHarness.RunCli(_tempDir,
            "migrate", _tempDir, "--report", reportPath, "--skip-verify", "--skip-analyze");

        Assert.True(exitCode == 0, $"migrate failed with exit {exitCode}: {stdErr}");
        Assert.True(File.Exists(reportPath), "report.json was not written");

        using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = doc.RootElement.Clone();

        // Envelope wrapper (validated against the shared schema checker)
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);
        Assert.Equal(JsonDiagnosticFormatter.SchemaVersion, root.GetProperty("version").GetString());
        Assert.Equal("migrate", root.GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("diagnostics").ValueKind);

        // The pre-existing report shape is unchanged under `data`
        var data = root.GetProperty("data");
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.True(data.TryGetProperty("summary", out var summary));
        Assert.Equal(1, summary.GetProperty("totalFiles").GetInt32());
        Assert.True(data.TryGetProperty("fileResults", out var fileResults));
        Assert.Equal(1, fileResults.GetArrayLength());
    }
}
