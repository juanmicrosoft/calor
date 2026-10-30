using System.Text;
using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class AgentDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"calor-agent-diag-{Guid.NewGuid():N}");
    private const string Broken = "§M{m1:Calc}\n  §F{f1:Add:i32:pub}\n    §R (+ 1 2)\n  §/F{f1}\n§/M{m1}";

    public AgentDiagnosticTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static DiagnosticWithFix Fix(params TextEdit[] edits) =>
        new("Calor0001", "test", TextSpan.Empty, new SuggestedFix("test", edits, isSafeToApply: true));

    [Theory]
    [InlineData(DiagnosticSeverity.Error, "")]
    [InlineData(DiagnosticSeverity.Warning, "warning: ")]
    [InlineData(DiagnosticSeverity.Info, "info: ")]
    public void Text_PreservesSeverityAndUnknownMultilineMessage(DiagnosticSeverity severity, string prefix)
    {
        var diagnostic = new Diagnostic("Vendor001", "first\nsecond\rthird\tlast\u2028end",
            new TextSpan(0, 0, 2, 3), severity, "file.calr");
        Assert.Equal($"file.calr:2:3 Vendor001 {prefix}first\\nsecond\\rthird\\tlast\\u2028end", diagnostic.ToString());
    }

    [Fact]
    public async Task EffectSummary_RetainsRowsAndOffersLongHelpWithoutChangingJson()
    {
        const string message = "Returned value conditional value has effect row [unknown] and the return of 'Get' declares row [pure], so it cannot be decided whether the row fits. State a row on both sides, or compile with --permissive-effects.";
        var diagnostic = new Diagnostic(DiagnosticCode.EffectRowUnknown, message, new TextSpan(0, 1, 3, 5),
            DiagnosticSeverity.Warning, "test.calr");
        Assert.Equal("test.calr:3:5 Calor0425 warning: Returned value conditional value row [unknown] vs the return of 'Get' row [pure]: compatibility unknown",
            diagnostic.ToString());
        var json = JsonDocument.Parse(new JsonDiagnosticFormatter().Format([diagnostic])).RootElement;
        Assert.Equal(message, json.GetProperty("diagnostics")[0].GetProperty("message").GetString());
        var help = await new HelpTool().ExecuteAsync(JsonSerializer.SerializeToElement(new { action = "error", error = "Calor0425" }));
        Assert.False(help.IsError);
        Assert.Contains("not a proof", help.Content[0].Text);
        Assert.Contains("--strict-effects", help.Content[0].Text);
    }

    [Fact]
    public void Text_PreservesWindowsPathBackslashes()
    {
        var diagnostic = new Diagnostic("Vendor001", @"Missing C:\work\value",
            new TextSpan(0, 0, 2, 3), DiagnosticSeverity.Error, @"C:\work\file.calr");
        Assert.Equal(@"C:\work\file.calr:2:3 Vendor001 Missing C:\work\value", diagnostic.ToString());
    }

    [Fact]
    public void Patches_ApplyInSourceOrderAndPreserveCrLf()
    {
        const string source = "abc\r\ndef\r\n";
        var result = DiagnosticFixes.Apply(source, source, "file.calr",
            [Fix(TextEdit.Replace("", 2, 1, 2, 4, "D\nE")),
             Fix(TextEdit.Replace("", 1, 2, 1, 3, "B"))], out var applied);
        Assert.Equal("aBc\r\nD\r\nE\r\n", result);
        Assert.Equal(2, applied);
    }

    [Fact]
    public void Patches_MultilineAndEofInsertion()
    {
        const string source = "abc\ndef";
        var result = DiagnosticFixes.Apply(source, source, "file.calr",
            [Fix(TextEdit.Replace("", 1, 2, 2, 2, "X"), TextEdit.Insert("", 2, 4, "!"))], out _);
        Assert.Equal("aXef!", result);
    }

    [Fact]
    public void Patches_PreserveLoneCrLinesAndUtf16Columns()
    {
        const string source = "😀abc\rdef\r";
        var result = DiagnosticFixes.Apply(source, source, "file.calr",
            [Fix(TextEdit.Replace("", 1, 3, 1, 4, "A"), TextEdit.Insert("", 2, 4, "\nx"))], out _);
        Assert.Equal("😀Abc\rdef\rx\r", result);
        Assert.Throws<InvalidDataException>(() => DiagnosticFixes.Apply(source, source, "file.calr",
            [Fix(TextEdit.Insert("", 1, 2, "split"))], out _));
    }

    [Theory]
    [InlineData(0, 1, 1, 2)]
    [InlineData(1, 0, 1, 2)]
    [InlineData(1, 1, 2, 1)]
    [InlineData(1, 1, 1, 5)]
    [InlineData(1, 3, 1, 2)]
    public void Patches_RejectInvalidSpan(int line, int column, int endLine, int endColumn)
    {
        Assert.Throws<InvalidDataException>(() => DiagnosticFixes.Apply("abc", "abc", "file.calr",
            [Fix(TextEdit.Replace("", line, column, endLine, endColumn, "x"))], out _));
    }

    [Fact]
    public void Patches_RejectOverlapConflictingInsertionsStaleSourceAndForeignFiles()
    {
        Assert.Throws<InvalidDataException>(() => DiagnosticFixes.Apply("abc", "abc", "file.calr",
            [Fix(TextEdit.Replace("", 1, 1, 1, 3, "x"), TextEdit.Replace("", 1, 2, 1, 4, "y"))], out _));
        Assert.Throws<InvalidDataException>(() => DiagnosticFixes.Apply("abc", "abc", "file.calr",
            [Fix(TextEdit.Insert("", 1, 2, "x"), TextEdit.Insert("", 1, 2, "y"))], out _));
        Assert.Throws<InvalidDataException>(() => DiagnosticFixes.Apply("changed", "original", "file.calr", [], out _));
        Assert.Throws<InvalidDataException>(() => DiagnosticFixes.Apply("abc", "abc", "file.calr",
            [Fix(TextEdit.Insert("other.calr", 1, 1, "x"))], out _));
    }

    [Fact]
    public void Patches_DeduplicateIdenticalEditsAndDoNotApplySuggestions()
    {
        var edit = TextEdit.Insert("", 1, 1, "x");
        var suggestion = new DiagnosticWithFix("Calor0200", "typo", TextSpan.Empty,
            new SuggestedFix("guess", TextEdit.Insert("", 1, 1, "unsafe")));
        var result = DiagnosticFixes.Apply("abc", "abc", "file.calr", [Fix(edit), Fix(edit), suggestion], out var applied);
        Assert.Equal("xabc", result);
        Assert.Equal(1, applied);
    }

    [Fact]
    public void Envelope_FixesJoinByFileAsWellAsPosition()
    {
        var bag = new DiagnosticBag();
        foreach (var file in new[] { "a.calr", "b.calr" })
        {
            bag.SetFilePath(file);
            bag.ReportErrorWithFix(TextSpan.Empty, "Calor0001", "same",
                new SuggestedFix(file, TextEdit.Insert(file, 1, 1, file), isSafeToApply: true));
        }
        var entries = DiagnosticEnvelope.Build(bag);
        Assert.Equal("a.calr", entries[0].Fix!.Edits[0].FilePath);
        Assert.Equal("b.calr", entries[1].Fix!.Edits[0].FilePath);
        Assert.All(entries, d => Assert.True(d.Fix!.IsSafeToApply));
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Error, 1)]
    [InlineData(DiagnosticSeverity.Warning, 0)]
    [InlineData(DiagnosticSeverity.Info, 0)]
    public void LiveProbe_UsesStructuredLocationsMessagesSeverityAndExit(DiagnosticSeverity severity, int exit)
    {
        var diagnostic = new Diagnostic("Calor0425", "full\n  multiline message", new TextSpan(0, 1, 3, 5),
            severity, "a.calr");
        var json = new JsonDiagnosticFormatter().Format([diagnostic]);
        const string stderr = "not a diagnostic: error Calor0410 Calor0425";
        var actual = Assert.Single(PpE1Probe.ParseStructured(json, stderr, exit));
        Assert.Equal(3, actual.Line);
        Assert.Equal(5, actual.Column);
        Assert.Equal("Calor0425", actual.Code);
        Assert.Equal(severity.ToString().ToLowerInvariant(), actual.Severity);
        Assert.Equal(diagnostic.Message, actual.Message);
        Assert.Throws<InvalidDataException>(() => PpE1Probe.ParseStructured(json, stderr, exit == 0 ? 1 : 0));
    }

    [Fact]
    public async Task McpCheck_ReturnsFreshDiagnosticsAfterTargetedPatches()
    {
        var result = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "diagnose", source = Broken, apply = true }));
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.False(result.IsError);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Empty(json.GetProperty("diagnostics").EnumerateArray());
        Assert.True(json.GetProperty("fixesApplied").GetInt32() >= 3);
        Assert.False(Program.Compile(json.GetProperty("fixedSource").GetString()!).HasErrors);
    }

    [Fact]
    public async Task McpCheck_DoesNotApplyFuzzySuggestions()
    {
        const string source = "§M{m:Test}\n  §F{f:Contains:pub} () -> bool\n    §R (cotains \"hello\" \"h\")";
        var result = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "diagnose", source, apply = true }));
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.True(result.IsError);
        Assert.Equal(source, json.GetProperty("fixedSource").GetString());
        Assert.Equal(0, json.GetProperty("fixesApplied").GetInt32());
        var fix = json.GetProperty("diagnostics").EnumerateArray().First(d => d.TryGetProperty("fix", out _)).GetProperty("fix");
        Assert.False(fix.GetProperty("isSafeToApply").GetBoolean());
    }

    [Theory]
    [InlineData("§M{m:T}\n    §F{f:Value:pub} () -> i32\n        §E{}\n        §R 1")]
    [InlineData("§M{m:T}\n\t§F{f:Value:pub} () -> i32\n\t\t§E{}\n\t\t§R 1")]
    [InlineData("§M{m:T}\n  §F{f:Value:pub} () -> i32\n    §E{}\n    §R 1 §/F{f}\n§/M{m}")]
    public async Task SafeFixes_PreserveGeneratedCodeAndInlineCodeBeforeClosers(string source)
    {
        var tool = new CheckTool();
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { source, apply = true }));
        var output = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.False(result.IsError, result.Content[0].Text);
        var fixedSource = output.GetProperty("fixedSource").GetString()!;
        Assert.NotEqual(source, fixedSource);
        Assert.Contains("§R 1", fixedSource);
        Assert.False(Program.Compile(fixedSource).HasErrors);
    }

    [Theory]
    [InlineData("SW", "W")]
    [InlineData("ENUM", "EN")]
    [InlineData("WR", "WHERE")]
    [InlineData("ADD", "PUSH")]
    [InlineData("when", "WHEN")]
    public void RemovedCanonicalAlias_IsAnErrorWithSafeReplacementNotAnAcceptedAlias(string marker, string canonical)
    {
        var diagnostics = new DiagnosticBag();
        var source = $"§{marker}";
        _ = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.True(diagnostics.HasErrors);
        var result = DiagnosticFixes.Apply(source, source, "file.calr", diagnostics.DiagnosticsWithFixes, out var applied);
        Assert.Equal($"§{canonical}", result);
        Assert.Equal(1, applied);
    }

    [Theory]
    [InlineData("∀", "forall")]
    [InlineData("∃", "exists")]
    public void RemovedQuantifier_CarriesAnExactSafeReplacement(string old, string canonical)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Lexer(old, diagnostics).TokenizeAllForParser();
        Assert.True(diagnostics.HasErrors);
        Assert.Equal(canonical, DiagnosticFixes.Apply(old, old, "file.calr", diagnostics.DiagnosticsWithFixes, out _));
    }

    [Fact]
    public void CliApply_PreservesEncodingAndLeavesNoTemporaryFiles()
    {
        var path = Path.Combine(_directory, "broken.calr");
        File.WriteAllText(path, Broken.Replace("\n", "\r\n"), new UnicodeEncoding(false, true));
        var (exit, stdout, stderr) = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");
        Assert.Equal(0, exit);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        var document = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("fix", document.GetProperty("command").GetString());
        Assert.Equal(0, document.GetProperty("summary").GetProperty("errors").GetInt32());
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xff, 0xfe }, bytes[..2]);
        var fixedSource = File.ReadAllText(path);
        Assert.DoesNotContain("§/F", fixedSource);
        Assert.DoesNotContain("\n", fixedSource.Replace("\r\n", ""));
        Assert.False(Program.Compile(fixedSource).HasErrors);
        Assert.Single(Directory.GetFiles(_directory));
        var repeated = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");
        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal(0, JsonDocument.Parse(repeated.StdOut).RootElement.GetProperty("data").GetProperty("changedFiles").GetInt32());
    }

    [Fact]
    public void CliPreview_IsReadOnlyAndKeepsErrorExitAndFixPayload()
    {
        var path = Path.Combine(_directory, "broken.calr");
        File.WriteAllText(path, Broken);
        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--dry-run", "--format", "json");
        Assert.NotEqual(0, exit);
        Assert.Equal(Broken, File.ReadAllText(path));
        var document = JsonDocument.Parse(stdout).RootElement;
        Assert.True(document.GetProperty("summary").GetProperty("errors").GetInt32() > 0);
        Assert.Contains(document.GetProperty("diagnostics").EnumerateArray(),
            d => d.TryGetProperty("fix", out var fix) && fix.GetProperty("isSafeToApply").GetBoolean());
    }

    [Fact]
    public void CliApply_UnfixableErrorsRemainErrors()
    {
        var path = Path.Combine(_directory, "broken.calr");
        const string source = "§M{m:Test}\n  §F{f:Broken:pub} () -> i32\n    §R missing";
        File.WriteAllText(path, source);
        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "fix", "--apply", "--format", "json");
        Assert.NotEqual(0, exit);
        Assert.Equal(source, File.ReadAllText(path));
        Assert.True(JsonDocument.Parse(stdout).RootElement.GetProperty("summary").GetProperty("errors").GetInt32() > 0);
    }

    [Theory]
    [InlineData("--heal-closers")]
    [InlineData("--elide-call-closers")]
    [InlineData("--revert")]
    public void CliApply_RejectsIncompatibleMigrationFlags(string flag)
    {
        var (exit, stdout, stderr) = CliTestHarness.RunCli(_directory, "fix", _directory, "--apply", flag, "--format", "json");
        Assert.Equal(2, exit);
        Assert.Contains("cannot be combined", stderr);
        var document = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal(DiagnosticCode.CliUsageError,
            Assert.Single(document.GetProperty("diagnostics").EnumerateArray()).GetProperty("code").GetString());
    }

    [Fact]
    public void CliApply_MissingInputEmitsAnErrorEnvelope()
    {
        var missing = Path.Combine(_directory, "missing.calr");
        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "fix", missing, "--apply", "--format", "json");
        Assert.NotEqual(0, exit);
        var document = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal(DiagnosticCode.CliInputNotFound,
            Assert.Single(document.GetProperty("diagnostics").EnumerateArray()).GetProperty("code").GetString());
    }

    [Fact]
    public void CleanMultiFileCompile_PrintsExactlyOneLineColdAndCached()
    {
        var paths = new[] { "Alpha", "Beta" }.Select(name =>
        {
            var path = Path.Combine(_directory, $"{name}.calr");
            File.WriteAllText(path, $"§M{{m:{name}}}\n  §F{{f:Value:pub}} () -> i32\n    §E{{}}\n    §R 1");
            return path;
        }).ToArray();
        for (var pass = 0; pass < 2; pass++)
        {
            var (exit, stdout, stderr) = CliTestHarness.RunCli(_directory, "--input", paths[0], "--input", paths[1], "--cache");
            Assert.True(exit == 0, stdout + stderr);
            Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
            Assert.Single(stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Contains(pass == 0 ? "Compilation successful" : "Up-to-date (cached)", stdout);
        }
    }

    [Fact]
    public async Task AtomicWrite_RejectsConcurrentDiskChange()
    {
        var path = Path.Combine(_directory, "race.calr");
        await File.WriteAllTextAsync(path, Broken);
        var snapshot = await SourceFileSnapshot.ReadAsync(path);
        await Assert.ThrowsAsync<IOException>(() => SafeSourceFile.WriteParsedAsync(snapshot, "replacement",
            _ => { }, async _ => await File.WriteAllTextAsync(path, "external change")));
        Assert.Equal("external change", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }
}
