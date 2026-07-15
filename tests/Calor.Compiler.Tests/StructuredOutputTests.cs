using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Round-trip tests for the unified structured diagnostic output
/// (Phase 1 item 3, agent-native strategy): compile files with known errors
/// through the real CLI with <c>--format json|sarif</c>, parse the output,
/// and assert the machine-readable schema (file/line/column/severity/code/
/// message + fix edits) produced by the shared DiagnosticFormatter surface.
/// </summary>
public class StructuredOutputTests : IDisposable
{
    private readonly string _tempDir;

    public StructuredOutputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-structured-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
        => CliTestHarness.RunCli(_tempDir, args);

    private string WriteBrokenFile()
    {
        // §B{x} with no type and no initializer is a deterministic
        // compile-time error (Calor0250, bind validation) at line 3.
        var file = Path.Combine(_tempDir, "broken.calr");
        File.WriteAllText(file, """
            §M{m001:Broken}
              §F{f001:Main:pub} () -> void
                §B{x}
            """);
        return file;
    }

    private string WriteGoodFile()
    {
        var file = Path.Combine(_tempDir, "good.calr");
        File.WriteAllText(file, """
            §M{m001:Good}
              §F{f001:Main:pub} () -> void
                §E{cw}
                §P "hello"
            """);
        return file;
    }

    // ------------------------------------------------------------------
    // calor --input <file> --format json
    // ------------------------------------------------------------------

    [Fact]
    public void Compile_FormatJson_ErrorFile_EmitsParseableUnifiedSchema()
    {
        var file = WriteBrokenFile();

        var (exitCode, stdOut, stdErr) = RunCli("--input", file, "--format", "json");

        Assert.Equal(1, exitCode);

        // stdout must be pure JSON (jq-compatible)
        using var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement;

        Assert.Equal("1.0", root.GetProperty("version").GetString());

        var diagnostics = root.GetProperty("diagnostics");
        Assert.True(diagnostics.GetArrayLength() >= 1, $"expected diagnostics, stderr: {stdErr}");

        var first = diagnostics[0];
        Assert.Equal("Calor0250", first.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("message").GetString()));
        Assert.Equal("error", first.GetProperty("severity").GetString());

        var location = first.GetProperty("location");
        Assert.EndsWith("broken.calr", location.GetProperty("file").GetString());
        Assert.Equal(3, location.GetProperty("line").GetInt32());
        Assert.True(location.GetProperty("column").GetInt32() >= 1);

        var summary = root.GetProperty("summary");
        Assert.True(summary.GetProperty("errors").GetInt32() >= 1);
        Assert.Equal(summary.GetProperty("total").GetInt32(), diagnostics.GetArrayLength());
    }

    [Fact]
    public void Compile_FormatJson_SuccessFile_EmitsEmptyDiagnosticsAndKeepsStdoutClean()
    {
        var file = WriteGoodFile();

        var (exitCode, stdOut, stdErr) = RunCli("--input", file, "--format", "json");

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}");

        // Status messages must not pollute machine-readable stdout
        Assert.DoesNotContain("Compilation successful", stdOut);
        Assert.Contains("Compilation successful", stdErr);

        using var doc = JsonDocument.Parse(stdOut);
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("errors").GetInt32());
    }

    // ------------------------------------------------------------------
    // calor --input <file> --format sarif
    // ------------------------------------------------------------------

    [Fact]
    public void Compile_FormatSarif_ErrorFile_EmitsSarif210()
    {
        var file = WriteBrokenFile();

        var (exitCode, stdOut, _) = RunCli("--input", file, "--format", "sarif");

        Assert.Equal(1, exitCode);

        using var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement;

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.Contains("sarif-schema-2.1.0", root.GetProperty("$schema").GetString());

        var run = root.GetProperty("runs")[0];
        var driver = run.GetProperty("tool").GetProperty("driver");
        Assert.Equal("calor", driver.GetProperty("name").GetString());

        var results = run.GetProperty("results");
        Assert.True(results.GetArrayLength() >= 1);

        var result = results[0];
        Assert.StartsWith("Calor", result.GetProperty("ruleId").GetString());
        Assert.Equal("error", result.GetProperty("level").GetString());
        Assert.False(string.IsNullOrEmpty(result.GetProperty("message").GetProperty("text").GetString()));

        var region = result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region");
        Assert.True(region.GetProperty("startLine").GetInt32() >= 1);

        // Rules metadata present for every emitted code
        var rules = driver.GetProperty("rules");
        Assert.True(rules.GetArrayLength() >= 1);
        Assert.StartsWith("Calor", rules[0].GetProperty("id").GetString());
    }

    // ------------------------------------------------------------------
    // Default text behavior unchanged
    // ------------------------------------------------------------------

    [Fact]
    public void Compile_DefaultTextFormat_PrintsDiagnosticsToStderrNotJson()
    {
        var file = WriteBrokenFile();

        var (exitCode, stdOut, stdErr) = RunCli("--input", file);

        Assert.Equal(1, exitCode);
        Assert.Contains("Calor", stdErr); // human-readable diagnostics on stderr
        Assert.DoesNotContain("\"diagnostics\"", stdOut); // no JSON payload in text mode
    }

    [Fact]
    public void Compile_UnknownFormat_IsRejected()
    {
        var file = WriteBrokenFile();

        var (exitCode, _, stdErr) = RunCli("--input", file, "--format", "xml");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("xml", stdErr);
    }

    // ------------------------------------------------------------------
    // calor lint --format json
    // ------------------------------------------------------------------

    [Fact]
    public void Lint_FormatJson_ReportsStyleIssuesAsWarnings()
    {
        var file = Path.Combine(_tempDir, "style.calr");
        // Trailing whitespace on the module line is a lint style issue.
        File.WriteAllText(file,
            "§M{m001:Style}   \n" +
            "  §F{f001:Main:pub} () -> void\n" +
            "    §E{cw}\n" +
            "    §P \"hi\"\n");

        // Note: lint exit codes are not asserted here — `calor lint` sets
        // Environment.ExitCode, which Main's returned InvokeAsync result
        // overrides (pre-existing behavior, unchanged by structured output).
        var (_, stdOut, _) = RunCli("lint", file, "--format", "json");

        using var doc = JsonDocument.Parse(stdOut);
        var diagnostics = doc.RootElement.GetProperty("diagnostics");
        Assert.True(diagnostics.GetArrayLength() >= 1);

        var issue = diagnostics[0];
        Assert.Equal(DiagnosticCode.LintStyleIssue, issue.GetProperty("code").GetString());
        Assert.Equal("warning", issue.GetProperty("severity").GetString());
        Assert.Equal(1, issue.GetProperty("location").GetProperty("line").GetInt32());
        Assert.Contains("whitespace", issue.GetProperty("message").GetString());
    }

    [Fact]
    public void Lint_FormatJson_ParseError_SurfacesErrorDiagnostics()
    {
        var file = Path.Combine(_tempDir, "badparse.calr");
        File.WriteAllText(file, "§M{m001:Bad}\n  §F{f001:Main:pub () -> void\n");

        var (_, stdOut, _) = RunCli("lint", file, "--format", "json");

        using var doc = JsonDocument.Parse(stdOut);
        Assert.True(doc.RootElement.GetProperty("summary").GetProperty("errors").GetInt32() >= 1);
    }

    // ------------------------------------------------------------------
    // calor assess --format sarif (shared SARIF implementation)
    // ------------------------------------------------------------------

    [Fact]
    public void Assess_FormatSarif_UsesSharedSarifFormatter()
    {
        var csFile = Path.Combine(_tempDir, "Sample.cs");
        File.WriteAllText(csFile, """
            public class Sample
            {
                public int Divide(int a, int b)
                {
                    if (b == 0) throw new System.ArgumentException("b");
                    return a / b;
                }

                public string? Find(string? name)
                {
                    if (name == null) return null;
                    return name.Trim();
                }
            }
            """);

        var (exitCode, stdOut, stdErr) = RunCli("assess", _tempDir, "--format", "sarif");

        Assert.True(exitCode == 0 || exitCode == 1, $"unexpected exit {exitCode}. stderr: {stdErr}");

        using var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement;
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());

        var driver = root.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver");
        Assert.Equal("calor-assess", driver.GetProperty("name").GetString());

        // Results (if any patterns detected) carry per-dimension rule IDs and
        // rules carry migration-doc help URIs supplied via the provider hooks.
        var rules = driver.GetProperty("rules");
        if (rules.GetArrayLength() > 0)
        {
            Assert.StartsWith("Calor-", rules[0].GetProperty("id").GetString());
            Assert.Contains("/docs/migration/", rules[0].GetProperty("helpUri").GetString());
        }
    }

    // ------------------------------------------------------------------
    // calor verify --format json embeds the unified diagnostic schema
    // ------------------------------------------------------------------

    [Fact]
    public void Verify_FormatJson_IncludesUnifiedDiagnosticsArray()
    {
        var file = WriteGoodFile();

        var (exitCode, stdOut, stdErr) = RunCli("verify", file, "--format", "json");

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}");

        using var doc = JsonDocument.Parse(stdOut);
        var files = doc.RootElement.GetProperty("files");
        Assert.True(files.GetArrayLength() == 1);

        // The new unified diagnostics array coexists with legacy errors/warnings.
        var fileEntry = files[0];
        Assert.Equal(JsonValueKind.Array, fileEntry.GetProperty("diagnostics").ValueKind);
        Assert.Equal(JsonValueKind.Array, fileEntry.GetProperty("errors").ValueKind);
        Assert.Equal(JsonValueKind.Array, fileEntry.GetProperty("warnings").ValueKind);
    }

    // ------------------------------------------------------------------
    // Fix edits round-trip (machine-applicable edits in JSON and SARIF)
    // ------------------------------------------------------------------

    [Fact]
    public void JsonFormatter_FixEdits_RoundTripThroughParsedJson()
    {
        var bag = new DiagnosticBag();
        bag.SetFilePath("test.calr");

        var fix = new SuggestedFix(
            "Change 'wrong' to 'right'",
            TextEdit.Replace("test.calr", 4, 7, 4, 12, "right"));
        bag.ReportErrorWithFix(new TextSpan(0, 5, 4, 7), "Calor0101", "id mismatch", fix);

        var json = new JsonDiagnosticFormatter().Format(bag);

        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("diagnostics")[0];

        Assert.Equal("Calor0101", entry.GetProperty("code").GetString());
        Assert.Equal("Change 'wrong' to 'right'", entry.GetProperty("suggestion").GetString());

        var fixElement = entry.GetProperty("fix");
        Assert.Equal("Change 'wrong' to 'right'", fixElement.GetProperty("description").GetString());

        var edit = fixElement.GetProperty("edits")[0];
        Assert.Equal("test.calr", edit.GetProperty("filePath").GetString());
        Assert.Equal(4, edit.GetProperty("startLine").GetInt32());
        Assert.Equal(7, edit.GetProperty("startColumn").GetInt32());
        Assert.Equal(4, edit.GetProperty("endLine").GetInt32());
        Assert.Equal(12, edit.GetProperty("endColumn").GetInt32());
        Assert.Equal("right", edit.GetProperty("newText").GetString());
    }

    [Fact]
    public void SarifFormatter_FixEdits_RoundTripThroughParsedSarif()
    {
        var bag = new DiagnosticBag();
        bag.SetFilePath("test.calr");

        var fix = new SuggestedFix(
            "Change 'wrong' to 'right'",
            TextEdit.Replace("test.calr", 4, 7, 4, 12, "right"));
        bag.ReportErrorWithFix(new TextSpan(0, 5, 4, 7), "Calor0101", "id mismatch", fix);

        var sarif = new SarifDiagnosticFormatter().Format(bag);

        using var doc = JsonDocument.Parse(sarif);
        var result = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0];

        var sarifFix = result.GetProperty("fixes")[0];
        Assert.Equal("Change 'wrong' to 'right'",
            sarifFix.GetProperty("description").GetProperty("text").GetString());

        var change = sarifFix.GetProperty("artifactChanges")[0];
        Assert.Equal("test.calr",
            change.GetProperty("artifactLocation").GetProperty("uri").GetString());

        var replacement = change.GetProperty("replacements")[0];
        var deleted = replacement.GetProperty("deletedRegion");
        Assert.Equal(4, deleted.GetProperty("startLine").GetInt32());
        Assert.Equal(7, deleted.GetProperty("startColumn").GetInt32());
        Assert.Equal(4, deleted.GetProperty("endLine").GetInt32());
        Assert.Equal(12, deleted.GetProperty("endColumn").GetInt32());
        Assert.Equal("right",
            replacement.GetProperty("insertedContent").GetProperty("text").GetString());
    }
}
