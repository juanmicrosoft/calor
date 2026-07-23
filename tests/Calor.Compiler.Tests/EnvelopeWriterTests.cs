using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the envelope schema v1.1 wrapper emitted by data-carrying CLI
/// commands (loop plan D1.3): { version, command, diagnostics, summary, data }.
/// </summary>
public class EnvelopeWriterTests
{
    [Fact]
    public void Serialize_DataOnly_EmitsEnvelopeWithEmptyDiagnostics()
    {
        var json = EnvelopeWriter.Serialize("coverage", new { file = "a.cs", success = true });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonDiagnosticFormatter.SchemaVersion, root.GetProperty("version").GetString());
        Assert.Equal("coverage", root.GetProperty("command").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());

        var summary = root.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("total").GetInt32());
        Assert.Equal(0, summary.GetProperty("errors").GetInt32());
        Assert.Equal(0, summary.GetProperty("warnings").GetInt32());
        Assert.Equal(0, summary.GetProperty("info").GetInt32());

        var data = root.GetProperty("data");
        Assert.Equal("a.cs", data.GetProperty("file").GetString());
        Assert.True(data.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Serialize_WithDiagnostics_SummarizesAndCarriesEntries()
    {
        var diagnostics = new[]
        {
            new Diagnostic(
                DiagnosticCode.Calor0800,
                DiagnosticSeverity.Error,
                "Missing ID for Function 'Main'",
                "demo.calr",
                3,
                5)
        };

        var json = EnvelopeWriter.Serialize("ids", new { totalIds = 7 }, diagnostics);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("ids", root.GetProperty("command").GetString());

        var entries = root.GetProperty("diagnostics").EnumerateArray().ToList();
        var entry = Assert.Single(entries);
        Assert.Equal("Calor0800", entry.GetProperty("code").GetString());
        Assert.Equal("error", entry.GetProperty("severity").GetString());
        Assert.Equal(3, entry.GetProperty("location").GetProperty("line").GetInt32());

        var summary = root.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("total").GetInt32());
        Assert.Equal(1, summary.GetProperty("errors").GetInt32());

        Assert.Equal(7, root.GetProperty("data").GetProperty("totalIds").GetInt32());
    }

    [Fact]
    public void SerializeRaw_WrapsPreSerializedPayloadUnchanged()
    {
        var payload = "{\"metadata\":{\"benchmarkCount\":3},\"values\":[1,2,3]}";

        var json = EnvelopeWriter.SerializeRaw("benchmark", payload);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("benchmark", root.GetProperty("command").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());

        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("metadata").GetProperty("benchmarkCount").GetInt32());
        Assert.Equal(3, data.GetProperty("values").GetArrayLength());
    }

    [Fact]
    public void Serialize_NullDataOmitted()
    {
        var json = EnvelopeWriter.Serialize("fix", data: null);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("data", out _));
    }
}
