using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 E1 slice 1 — CLI-level observation of the <c>calor effects suggest</c>
/// contract for receivers the binder cannot vouch for: such calls are reported
/// (<c>Calor1360</c>, stderr in text mode / <c>diagnostics</c> in JSON) and are
/// never written into the suggested manifest under the receiver's source text;
/// <c>data.untypedReceivers</c> carries the count on BOTH JSON paths.
/// </summary>
public class EffectsSuggestCliTests : IDisposable
{
    private readonly string _tempDir;

    public EffectsSuggestCliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-effects-suggest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    // OrderRepo.Save: a type-qualified external call → manifest entry.
    // x.Run: x is inferred from an unknown callee → receiver has no resolved type.
    private const string MixedSource = @"
§M{m001:Sample}
  §F{f001:DoWork:pub}
      §O{void}
      §C{OrderRepo.Save} §/C
      §B{x} §C{Unknown.Make} §/C
      §C{x.Run} §/C
";

    // Every typed external call resolves (Console.WriteLine is in the built-in
    // manifests); the only unresolved calls are on receivers without a type.
    private const string OnlyUntypedSource = @"
§M{m001:Sample}
  §F{f001:DoWork:pub}
      §O{void}
      §C{Console.WriteLine} §A STR:""hi"" §/C
      §B{x} §C{foo.Bar} §/C
      §C{x.Run} §/C
";

    private string WriteSource(string name, string source)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, source);
        return path;
    }

    private static JsonElement ParseEnvelope(string stdOut)
    {
        using var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement.Clone();
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);
        return root;
    }

    [Fact]
    public void Suggest_Text_UntypedReceiver_IsWarnedAndKeptOutOfManifest()
    {
        var input = WriteSource("mixed.calr", MixedSource);
        var output = Path.Combine(_tempDir, "suggested.json");

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir,
            "effects", "suggest", "-i", input, "-o", output);

        Assert.Equal(0, exitCode);
        Assert.Contains(DiagnosticCode.EffectsSuggestUntypedReceiver, stdErr, StringComparison.Ordinal);
        Assert.Contains("'x.Run'", stdErr, StringComparison.Ordinal);
        Assert.Contains("on receivers with no resolved type", stdOut, StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(File.ReadAllText(output));
        var types = manifest.RootElement.GetProperty("mappings").EnumerateArray()
            .Select(m => m.GetProperty("type").GetString())
            .ToList();
        Assert.Contains("OrderRepo", types);
        Assert.Contains("Unknown", types);
        Assert.DoesNotContain("x", types);
    }

    [Fact]
    public void Suggest_Json_ManifestPath_CarriesUntypedReceiversAndDiagnostic()
    {
        var input = WriteSource("mixed.calr", MixedSource);

        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "effects", "suggest", "-i", input, "--json");

        Assert.Equal(0, exitCode);
        var root = ParseEnvelope(stdOut);
        Assert.Equal("effects", root.GetProperty("command").GetString());

        var data = root.GetProperty("data");
        Assert.Equal(1, data.GetProperty("untypedReceivers").GetInt32());
        var types = data.GetProperty("mappings").EnumerateArray()
            .Select(m => m.GetProperty("type").GetString())
            .ToList();
        Assert.Contains("OrderRepo", types);
        Assert.DoesNotContain("x", types);

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.EffectsSuggestUntypedReceiver, diagnostic.GetProperty("code").GetString());
        Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("warnings").GetInt32());
    }

    [Fact]
    public void Suggest_Json_AllResolvedPath_CarriesUntypedReceiversAndDiagnostics()
    {
        var input = WriteSource("untyped.calr", OnlyUntypedSource);

        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir,
            "effects", "suggest", "-i", input, "--json");

        Assert.Equal(0, exitCode);
        var root = ParseEnvelope(stdOut);

        var data = root.GetProperty("data");
        Assert.Equal(0, data.GetProperty("unresolved").GetInt32());
        Assert.Equal(2, data.GetProperty("untypedReceivers").GetInt32());

        var diagnostics = root.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d =>
            Assert.Equal(DiagnosticCode.EffectsSuggestUntypedReceiver, d.GetProperty("code").GetString()));
        Assert.Equal(2, root.GetProperty("summary").GetProperty("warnings").GetInt32());
    }
}
