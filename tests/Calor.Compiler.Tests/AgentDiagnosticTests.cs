using System.Reflection;
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
    private const string Broken = "§M{Calc}\n  §F{Add:i32:pub}\n    §R (+ 1 2)\n  §/F\n§/M";
    private const string NativeLayout = "module Calc Calc   \n\tmodule CalcModule  \n"
        + "\t\tfunc Add public () → int  \n\t\t\t!\n\t\t\treturn 1 + 2  ";

    public AgentDiagnosticTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static DiagnosticWithFix Fix(params TextEdit[] edits) =>
        new("Calor0001", "test", TextSpan.Empty, new SuggestedFix("test", edits, isSafeToApply: true));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task NativeWhitespaceFixes_AreSharedByLintCliAndMcp(string newline)
    {
        var source = "module White   " + newline
            + "  func Value public () → string" + newline
            + "    !" + newline
            + "    return \"tail  \" \t";
        var expected = "module White" + newline
            + "  func Value public () → string" + newline
            + "    !" + newline
            + "    return \"tail  \"";
        var original = Program.Compile(source);
        Assert.False(original.HasErrors, string.Join("\n", original.Diagnostics.Errors));
        var path = Path.Combine(_directory, "whitespace.calr");
        var encoding = new UnicodeEncoding(false, true);
        File.WriteAllText(path, source, encoding);
        var originalBytes = File.ReadAllBytes(path);

        var (lintExit, lintJson, _) = CliTestHarness.RunCli(_directory, "lint", path, "--format", "json");
        Assert.Equal(1, lintExit);
        var entries = JsonDocument.Parse(lintJson).RootElement.GetProperty("diagnostics");
        Assert.Equal(2, entries.GetArrayLength());
        var first = entries[0];
        Assert.Equal(DiagnosticCode.LintTrailingWhitespace, first.GetProperty("code").GetString());
        Assert.Equal(13, first.GetProperty("location").GetProperty("column").GetInt32());
        Assert.Equal(3, first.GetProperty("location").GetProperty("length").GetInt32());
        var patch = first.GetProperty("fix");
        Assert.True(patch.GetProperty("isSafeToApply").GetBoolean());
        var edit = Assert.Single(patch.GetProperty("edits").EnumerateArray());
        Assert.Equal(path, edit.GetProperty("filePath").GetString());
        Assert.Equal(1, edit.GetProperty("startLine").GetInt32());
        Assert.Equal(13, edit.GetProperty("startColumn").GetInt32());
        Assert.Equal(1, edit.GetProperty("endLine").GetInt32());
        Assert.Equal(16, edit.GetProperty("endColumn").GetInt32());
        Assert.Equal("", edit.GetProperty("newText").GetString());
        Assert.Equal(originalBytes, File.ReadAllBytes(path));

        var lint = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "lint", source }));
        Assert.False(lint.IsError);
        var issues = JsonDocument.Parse(lint.Content[0].Text!).RootElement.GetProperty("issues");
        Assert.Equal(2, issues.GetArrayLength());
        Assert.Equal(DiagnosticCode.LintTrailingWhitespace, issues[0].GetProperty("code").GetString());
        var mcpPatch = issues[0].GetProperty("fix");
        Assert.True(mcpPatch.GetProperty("isSafeToApply").GetBoolean());
        Assert.Equal(13, mcpPatch.GetProperty("edits")[0].GetProperty("startColumn").GetInt32());
        Assert.Equal(16, mcpPatch.GetProperty("edits")[0].GetProperty("endColumn").GetInt32());

        var fixedResult = await new FixTool().ExecuteAsync(JsonSerializer.SerializeToElement(new { source }));
        Assert.False(fixedResult.IsError, fixedResult.Content[0].Text);
        var fixedOutput = JsonDocument.Parse(fixedResult.Content[0].Text!).RootElement;
        Assert.Equal(expected, fixedOutput.GetProperty("fixedSource").GetString());
        Assert.Equal(2, fixedOutput.GetProperty("fixCount").GetInt32());
        Assert.Equal(original.GeneratedCode, Program.Compile(expected).GeneratedCode);

        var checkedResult = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "diagnose", source, apply = true }));
        Assert.False(checkedResult.IsError, checkedResult.Content[0].Text);
        var checkedOutput = JsonDocument.Parse(checkedResult.Content[0].Text!).RootElement;
        Assert.Equal(expected, checkedOutput.GetProperty("fixedSource").GetString());
        Assert.Equal(2, checkedOutput.GetProperty("fixesApplied").GetInt32());
        Assert.Empty(checkedOutput.GetProperty("diagnostics").EnumerateArray());

        var (fixExit, fixJson, _) = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");
        Assert.Equal(0, fixExit);
        Assert.Equal(2, JsonDocument.Parse(fixJson).RootElement.GetProperty("data")
            .GetProperty("editsApplied").GetInt32());
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray(), File.ReadAllBytes(path));
        var (againExit, againJson, _) = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");
        Assert.Equal(0, againExit);
        Assert.Equal(0, JsonDocument.Parse(againJson).RootElement.GetProperty("data")
            .GetProperty("editsApplied").GetInt32());
    }

    [Fact]
    public void NativeWhitespaceFixes_ProtectCommentsAndCountedPayloadBytes()
    {
        const string payload = "public static class Saved { public const string Text = \"tail  \"; }  \r\n";
        var source = "module White   \r\n  // keep \t\r\n"
            + $"  raw member {Encoding.UTF8.GetByteCount(payload)}\r\n{payload}\r\n";
        var diagnostics = LosslessSourceDocument.DiagnoseTrailingWhitespace(source, "raw.calr");
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.LintTrailingWhitespace, diagnostic.Code);
        var result = DiagnosticFixes.Apply(source, source, "raw.calr", diagnostics.DiagnosticsWithFixes, out var count);
        Assert.Equal(1, count);
        Assert.Equal(source.Replace("module White   ", "module White", StringComparison.Ordinal), result);
        Assert.Contains(payload, result);
        var lexerDiagnostics = new DiagnosticBag();
        _ = new Lexer(result, lexerDiagnostics).TokenizeAllForParser();
        Assert.False(lexerDiagnostics.HasErrors, string.Join("\n", lexerDiagnostics.Errors));
    }

    [Fact]
    public void NativeWhitespaceFixes_DoNotEditRetiredSourceOnFailedCompilation()
    {
        const string source = "module White   \n§/M\n";
        var result = DiagnosticFixes.Repair(source, "retired.calr", new CompilationOptions(),
            out var compilation, out var count);
        Assert.True(compilation.HasErrors);
        Assert.Equal(0, count);
        Assert.Equal(source, result);
    }

    [Fact]
    public void NativeWhitespaceFixes_DoNotOfferEditsWhenRawFramingIsInvalid()
    {
        const string source = "module White   \n  raw member 999\nclass C {}   ";
        var lexicalDiagnostics = new DiagnosticBag();
        _ = new Lexer(source, lexicalDiagnostics).TokenizeAllForParser();
        Assert.True(lexicalDiagnostics.HasErrors);
        var diagnostics = LosslessSourceDocument.DiagnoseTrailingWhitespace(source, "raw.calr");
        Assert.Empty(diagnostics);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
    }

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
        AssertRetiredSourceRejected(Broken);
        var original = CompileNative(NativeLayout);
        var result = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "diagnose", source = NativeLayout, apply = true }));
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.False(result.IsError);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Empty(json.GetProperty("diagnostics").EnumerateArray());
        Assert.True(json.GetProperty("fixesApplied").GetInt32() >= 3);
        Assert.False(Program.Compile(json.GetProperty("fixedSource").GetString()!).HasErrors);
        Assert.Equal(original.GeneratedCode, CompileNative(json.GetProperty("fixedSource").GetString()!).GeneratedCode);
    }

    [Fact]
    public async Task McpCheck_DoesNotApplyFuzzySuggestions()
    {
        const string retired = "§M{Test}\n  §F{Contains:pub} () -> bool\n    §R (cotains \"hello\" \"h\")";
        AssertRetiredSourceRejected(retired);
        const string source = "module Test\n  func Contains public (string:value) → string\n    !\n    return vlaue";
        AssertNativeFrontend(source);
        var original = Program.Compile(source);
        Assert.Contains(original.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var result = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "diagnose", source, apply = true }));
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.True(result.IsError);
        Assert.Equal(source, json.GetProperty("fixedSource").GetString());
        Assert.Equal(0, json.GetProperty("fixesApplied").GetInt32());
        var fix = json.GetProperty("diagnostics").EnumerateArray().First(d => d.TryGetProperty("fix", out _)).GetProperty("fix");
        Assert.False(fix.GetProperty("isSafeToApply").GetBoolean());
        var edit = Assert.Single(fix.GetProperty("edits").EnumerateArray());
        Assert.Equal("value", edit.GetProperty("newText").GetString());
        Assert.Equal(0, json.GetProperty("fixesApplied").GetInt32());
    }

    [Theory]
    [InlineData("§M{T}\n    §F{Value:pub} () -> i32\n        §E{}\n        §R 1")]
    [InlineData("§M{T}\n\t§F{Value:pub} () -> i32\n\t\t§E{}\n\t\t§R 1")]
    [InlineData("§M{T}\n  §F{Value:pub} () -> i32\n    §E{}\n    §R 1 §/F\n§/M")]
    public async Task SafeFixes_PreserveGeneratedCodeAndInlineCodeBeforeClosers(string source)
    {
        AssertRetiredSourceRejected(source);
        if (source.Contains("§/F", StringComparison.Ordinal))
            AssertInternalCloserRemovalPreservesInlineCode(source);
        var native = source switch
        {
            "§M{T}\n    §F{Value:pub} () -> i32\n        §E{}\n        §R 1"
                => "module T T\n  module TModule\n      func Value public () → int\n          !\n          return 1",
            "§M{T}\n\t§F{Value:pub} () -> i32\n\t\t§E{}\n\t\t§R 1"
                => "module T T\n\tmodule TModule\n\t\tfunc Value public () → int\n\t\t\t!\n\t\t\treturn 1",
            "§M{T}\n  §F{Value:pub} () -> i32\n    §E{}\n    §R 1 §/F\n§/M"
                => "module T T\n  module TModule\n    func Value public () → int\n      !\n      return 1   ",
            _ => throw new ArgumentException("No native recipe is registered for this diagnostic fixture.", nameof(source))
        };
        var original = CompileNative(native);
        var tool = new CheckTool();
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { source = native, apply = true }));
        var output = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.False(result.IsError, result.Content[0].Text);
        var fixedSource = output.GetProperty("fixedSource").GetString()!;
        Assert.NotEqual(native, fixedSource);
        Assert.Contains("return 1", fixedSource);
        Assert.False(Program.Compile(fixedSource).HasErrors);
        Assert.Equal(original.GeneratedCode, CompileNative(fixedSource).GeneratedCode);
    }

    [Theory]
    [InlineData("SW", "W")]
    [InlineData("ENUM", "EN")]
    [InlineData("WR", "WHERE")]
    [InlineData("ADD", "PUSH")]
    [InlineData("when", "WHEN")]
    public void RemovedCanonicalAlias_IsAnErrorWithoutAnotherSigilReplacement(string marker, string canonical)
    {
        var diagnostics = new DiagnosticBag();
        var source = $"§{marker}";
        _ = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.True(diagnostics.HasErrors);
        var result = DiagnosticFixes.Apply(source, source, "file.calr", diagnostics.DiagnosticsWithFixes, out var applied);
        Assert.Equal(source, result);
        Assert.Equal(0, applied);
        Assert.DoesNotContain(diagnostics.DiagnosticsWithFixes,
            fix => fix.Fix?.Edits.Any(edit => edit.NewText == $"§{canonical}") == true);
    }

    [Theory]
    [InlineData("∀")]
    [InlineData("∃")]
    public void RemovedQuantifier_DoesNotRewriteOnlyItsPrefix(string old)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Lexer(old, diagnostics).TokenizeAllForParser();
        Assert.True(diagnostics.HasErrors);
        Assert.Equal(old, DiagnosticFixes.Apply(old, old, "file.calr", diagnostics.DiagnosticsWithFixes, out var applied));
        Assert.Equal(0, applied);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
    }

    [Theory]
    [InlineData("§/M")]
    [InlineData("§FUNC")]
    [InlineData("∀")]
    [InlineData("∃")]
    public void CliApply_RetiredSyntaxRemainsAnErrorAndPreservesEncodedBytes(string retired)
    {
        const string canonical = "module Test\r\n  func Value () → int\r\n    !\n    return 1\r\n";
        Assert.False(Program.Compile(canonical).HasErrors);
        var path = Path.Combine(_directory, "retired.calr");
        File.WriteAllText(path, canonical + retired + "\r\n", new UnicodeEncoding(false, true));
        var original = File.ReadAllBytes(path);
        Assert.True(Program.Compile(File.ReadAllText(path)).HasErrors);

        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");

        Assert.NotEqual(0, exit);
        var result = JsonDocument.Parse(stdout).RootElement;
        Assert.True(result.GetProperty("summary").GetProperty("errors").GetInt32() > 0);
        Assert.Equal(0, result.GetProperty("data").GetProperty("changedFiles").GetInt32());
        Assert.Equal(0, result.GetProperty("data").GetProperty("editsApplied").GetInt32());
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_directory));
        Assert.False(Directory.Exists(Path.Combine(_directory, ".calor")));
    }

    [Fact]
    public void CliApply_PreservesEncodingAndLeavesNoTemporaryFiles()
    {
        AssertRetiredSourceRejected(Broken);
        var original = CompileNative(NativeLayout);
        var path = Path.Combine(_directory, "broken.calr");
        File.WriteAllText(path, NativeLayout.Replace("\n", "\r\n"), new UnicodeEncoding(false, true));
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
        Assert.DoesNotContain("\t", fixedSource);
        Assert.DoesNotContain("\n", fixedSource.Replace("\r\n", ""));
        Assert.False(Program.Compile(fixedSource).HasErrors);
        Assert.Equal(original.GeneratedCode, CompileNative(fixedSource).GeneratedCode);
        Assert.Single(Directory.GetFiles(_directory));
        var repeated = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");
        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal(0, JsonDocument.Parse(repeated.StdOut).RootElement.GetProperty("data").GetProperty("changedFiles").GetInt32());
    }

    [Fact]
    public void CliPreview_IsReadOnlyAndKeepsErrorExitAndFixPayload()
    {
        const string source = "module Calc\n\tfunc Add public () → int\n\t\t!\n\t\treturn missing";
        AssertNativeFrontend(source);
        var original = Program.Compile(source);
        Assert.Contains(original.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        Assert.Contains(original.Diagnostics.DiagnosticsWithFixes, diagnostic => diagnostic.Fix.IsSafeToApply);
        var path = Path.Combine(_directory, "broken.calr");
        File.WriteAllText(path, source);
        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--dry-run", "--format", "json");
        Assert.NotEqual(0, exit);
        Assert.Equal(source, File.ReadAllText(path));
        var document = JsonDocument.Parse(stdout).RootElement;
        Assert.True(document.GetProperty("summary").GetProperty("errors").GetInt32() > 0);
        Assert.Contains(document.GetProperty("diagnostics").EnumerateArray(),
            d => d.TryGetProperty("fix", out var fix) && fix.GetProperty("isSafeToApply").GetBoolean());
    }

    [Fact]
    public void CliApply_UnfixableErrorsRemainErrors()
    {
        var path = Path.Combine(_directory, "broken.calr");
        const string retired = "§M{Test}\n  §F{Broken:pub} () -> i32\n    §R missing";
        AssertRetiredSourceRejected(retired);
        const string source = "module Test\n  func Broken public () → int\n    !\n    return missing";
        AssertNativeFrontend(source);
        Assert.Contains(Program.Compile(source).Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        File.WriteAllText(path, source);
        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "fix", "--apply", "--format", "json");
        Assert.NotEqual(0, exit);
        Assert.Equal(source, File.ReadAllText(path));
        Assert.True(JsonDocument.Parse(stdout).RootElement.GetProperty("summary").GetProperty("errors").GetInt32() > 0);
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
            var source = $"module {name} {name}\n  module {name}Module\n"
                + "    func Value public () → int\n      !\n      return 1";
            CompileNative(source);
            File.WriteAllText(path, source);
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

    private static void AssertNativeFrontend(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        _ = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
    }

    private static CompilationResult CompileNative(string source)
    {
        AssertNativeFrontend(source);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        return compiled;
    }

    private static void AssertRetiredSourceRejected(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var markers = tokens.Where(token => token.Text.StartsWith('§')).ToArray();
        Assert.NotEmpty(markers);
        foreach (var token in markers)
        {
            Assert.Equal(TokenKind.Error, token.Kind);
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownSectionMarker
                && diagnostic.Span.Equals(token.Span));
            Assert.Equal(token.Text, source.Substring(token.Span.Start, token.Span.Length));
        }
    }

    private static void AssertInternalCloserRemovalPreservesInlineCode(string source)
    {
        var diagnostics = new DiagnosticBag();
        var report = typeof(Parser).GetMethod("ReportLegacyCloser", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(report);
        foreach (var (marker, kind) in new[] { ("§/F", TokenKind.EndFunc), ("§/M", TokenKind.EndModule) })
        {
            var offset = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(offset >= 0);
            var span = new TextSpan(offset, marker.Length, source[..offset].Count(c => c == '\n') + 1,
                offset - source.LastIndexOf('\n', offset));
            var token = new Token(kind, marker, span);
            var parser = new Parser([token, new Token(TokenKind.Eof, "", TextSpan.Empty)], diagnostics);
            report.Invoke(parser, [token]);
        }
        Assert.Equal(2, diagnostics.Errors.Count());
        Assert.All(diagnostics.Errors, diagnostic => Assert.Equal(DiagnosticCode.LegacyCloserForm, diagnostic.Code));
        var fixedSource = DiagnosticFixes.Apply(source, source, "", diagnostics.DiagnosticsWithFixes, out var applied);
        Assert.Equal(2, applied);
        Assert.Contains("§R 1", fixedSource);
        Assert.Equal(source.Replace("§/F", "", StringComparison.Ordinal).Replace("§/M", "", StringComparison.Ordinal), fixedSource);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task CanonicalLiteralReturn_SharedPatchPreservesBytesAndIsIdempotent(string newline)
    {
        var source = string.Join(newline,
            "module Choices", "  func Pick public (bool:condition) → int",
            "    !", "    if condition", "      return 1", "    else", "      return 2");
        var expected = string.Join(newline,
            "module Choices", "  func Pick public (bool:condition) → int",
            "    !", "    return (condition) ? 1 : 2", "", "", "");
        var original = Program.Compile(source, "choice.calr");
        Assert.False(original.HasErrors, string.Join("\n", original.Diagnostics.Errors));
        var diagnosis = LosslessSourceDocument.DiagnoseCanonicalUsage(source, "choice.calr", original.Ast!);
        Assert.Equal(DiagnosticCode.LintConditionalReturn, Assert.Single(diagnosis).Code);
        Assert.True(Assert.Single(diagnosis.DiagnosticsWithFixes).Fix.IsSafeToApply);
        var fixedSource = DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(),
            out var repaired, out var applied);
        Assert.Equal(1, applied);
        Assert.Equal(expected, fixedSource);
        Assert.False(repaired.HasErrors, string.Join("\n", repaired.Diagnostics.Errors));
        Assert.Equal(original.GeneratedCode, repaired.GeneratedCode);
        Assert.Empty(LosslessSourceDocument.DiagnoseCanonicalUsage(fixedSource, "choice.calr", repaired.Ast!));
        Assert.Equal(fixedSource, DiagnosticFixes.Repair(fixedSource, "choice.calr",
            new CompilationOptions(), out _, out var again));
        Assert.Equal(0, again);

        var lint = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "lint", source, fix = true }));
        Assert.False(lint.IsError, lint.Content[0].Text);
        var output = JsonDocument.Parse(lint.Content[0].Text!).RootElement;
        Assert.Equal(expected, output.GetProperty("fixedCode").GetString());
        var issue = Assert.Single(output.GetProperty("issues").EnumerateArray());
        Assert.Equal(DiagnosticCode.LintConditionalReturn, issue.GetProperty("code").GetString());
        Assert.True(issue.GetProperty("fix").GetProperty("isSafeToApply").GetBoolean());
        var checkedResult = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "diagnose", source, apply = true }));
        Assert.False(checkedResult.IsError, checkedResult.Content[0].Text);
        Assert.Equal(expected, JsonDocument.Parse(checkedResult.Content[0].Text!)
            .RootElement.GetProperty("fixedSource").GetString());

        var path = Path.Combine(_directory, "choice.calr");
        var encoding = new UnicodeEncoding(false, true);
        File.WriteAllText(path, source, encoding);
        var (lintExit, lintJson, _) = CliTestHarness.RunCli(_directory, "lint", path, "--format", "json");
        Assert.Equal(1, lintExit);
        Assert.Equal(DiagnosticCode.LintConditionalReturn, Assert.Single(
            JsonDocument.Parse(lintJson).RootElement.GetProperty("diagnostics").EnumerateArray())
            .GetProperty("code").GetString());
        var (fixExit, _, fixErrors) = CliTestHarness.RunCli(_directory, "lint", path, "--fix", "--format", "json");
        Assert.True(fixExit == 0, fixErrors);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray(), File.ReadAllBytes(path));
        File.WriteAllText(path, source, encoding);
        var (applyExit, _, applyErrors) = CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");
        Assert.True(applyExit == 0, applyErrors);
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("object", "1", "2")]
    [InlineData("bool", "true", "false")]
    [InlineData("float", "1.25f", "2.5f")]
    [InlineData("double", "1.25", "2.5")]
    [InlineData("decimal", "1.25m", "2.5m")]
    [InlineData("string", "\"one\"", "\"two\"")]
    public void CanonicalLiteralReturn_ConverterAndRuntimePreserveTheSelectedValue(
        string type, string first, string second)
    {
        var csharp = $"#nullable enable\npublic static class Probe {{ public static {type} Pick(bool condition) "
            + $"{{ if (condition) return {first}; else return {second}; }} }}";
        var conversion = new Migration.CSharpToCalorConverter().Convert(csharp, "choice.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        var calor = conversion.CalorSource!;
        Assert.Contains(" ? ", calor);
        var compiled = CompileNative(calor);
        Assert.Empty(LosslessSourceDocument.DiagnoseCanonicalUsage(calor, "choice.calr", compiled.Ast!));
        foreach (var condition in new[] { false, true })
            Assert.Equal(InvokePick(csharp, condition), InvokePick(compiled.GeneratedCode, condition));
    }

    private static (Type? Type, object? Value, int? Calls) InvokePick(string csharp, bool condition)
    {
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Choice_" + Guid.NewGuid().ToString("N"),
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(csharp)],
            CodeGen.GeneratedCSharpCompiler.References,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        stream.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var types = context.LoadFromStream(stream).GetTypes();
            var value = types
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(method => method.Name == "Pick").Invoke(null, [condition]);
            var calls = types.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                .SingleOrDefault(field => field.Name == "Calls")?.GetValue(null);
            return (value?.GetType(), value, calls is int count ? count : null);
        }
        finally { context.Unload(); }
    }

    [Theory]
    [InlineData("    if condition\n      return 1\n    else\n      return 2U")]
    [InlineData("    if condition\n      return 1\n    else\n      return \"two\"")]
    [InlineData("    if condition\n      return 1\n    else\n      return value")]
    [InlineData("    if condition\n      // keep this explanation\n      return 1\n    else\n      return 2")]
    [InlineData("    if condition\n      return 1 // keep this explanation\n    else\n      return 2")]
    public void CanonicalLiteralReturn_LeavesMixedTypesNonliteralBranchesAndCommentsAlone(string body)
    {
        var source = "module Choices\n  func Pick public (bool:condition, int:value) → object\n    !\n" + body;
        var original = CompileNative(source);
        Assert.Empty(LosslessSourceDocument.DiagnoseCanonicalUsage(source, "choice.calr", original.Ast!));
        var unchanged = DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(), out _, out var applied);
        Assert.Equal(source, unchanged);
        Assert.Equal(0, applied);
    }

    [Fact]
    public void CanonicalLiteralReturn_WhitespaceAndStructuralFixesDoNotOverlap()
    {
        const string source = "module Choices   \n  func Pick public (bool:condition) → int\n"
            + "    !\n    if condition  \n      return 1  \n    else\n      return 2";
        var fixedSource = DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(),
            out var result, out var applied);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        Assert.Equal(4, applied);
        Assert.Equal("module Choices\n  func Pick public (bool:condition) → int\n"
            + "    !\n    return (condition) ? 1 : 2\n\n\n", fixedSource);
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("short")]
    public void CanonicalLiteralReturn_DoesNotIntroduceAnIntConditionalIntoANarrowReturn(string type)
    {
        var source = $"module Choices\n  func Pick public (bool:condition) → {type}\n    !\n"
            + "    !\n    if condition\n      return 1\n    else\n      return 2";
        var original = CompileNative(source);
        Assert.Empty(LosslessSourceDocument.DiagnoseCanonicalUsage(source, "choice.calr", original.Ast!));
        Assert.Equal(source, DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(),
            out _, out var applied));
        Assert.Equal(0, applied);
    }

    [Fact]
    public void CanonicalLiteralReturn_PreservesLaterStatementSourceMappings()
    {
        const string source = "module Choices\n  func Pick public (bool:condition) → int\n    !\n"
            + "    !\n    if condition\n      return 1\n    else\n      return 2\n"
            + "  func Other public () → int\n    !\n    return 3";
        var original = Program.Compile(source, "choice.calr");
        Assert.False(original.HasErrors, string.Join("\n", original.Diagnostics.Errors));
        var fixedSource = DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(),
            out var repaired, out var applied);
        Assert.Equal(1, applied);
        Assert.Equal(original.GeneratedCode, repaired.GeneratedCode);
        Assert.Equal(source.Count(c => c == '\n'), fixedSource.Count(c => c == '\n'));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void CanonicalLiteralReturn_EvaluatesTheConditionOnceAndRetainsFinally(bool condition, int expected)
    {
        const string source = """
            module Choices
              module Probe
                field int:Calls public = 0
                func Check public (bool:value) → bool
                  ! mut
                  set Calls Calls + 1
                  return value
                func Pick public (bool:condition) → int
                  ! mut
                  try
                    if Check(condition)
                      return 1
                    else
                      return 2
                  finally
                    set Calls Calls + 10
            """;
        var original = Program.Compile(source, "choice.calr");
        Assert.False(original.HasErrors, string.Join("\n", original.Diagnostics.Errors));
        var fixedSource = DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(),
            out var repaired, out var applied);
        Assert.Equal(1, applied);
        Assert.Equal(original.GeneratedCode, repaired.GeneratedCode);
        Assert.Equal((typeof(int), (object)expected, (int?)11), InvokePick(repaired.GeneratedCode, condition));
        Assert.Contains("finally", repaired.GeneratedCode);
        Assert.DoesNotContain("if Check", fixedSource);
    }

    [Theory]
    [InlineData(ContractMode.Debug)]
    [InlineData(ContractMode.Release)]
    [InlineData(ContractMode.Off)]
    public void CanonicalLiteralReturn_RetainsThePostcondition(ContractMode mode)
    {
        const string source = """
            module Choices
              func Pick public (bool:condition) → int
                !
                => result == 1
                if condition
                  return 1
                else
                  return 2
            """;
        var options = new CompilationOptions { ContractMode = mode };
        var original = Program.Compile(source, "choice.calr", options);
        Assert.False(original.HasErrors, string.Join("\n", original.Diagnostics.Errors));
        _ = DiagnosticFixes.Repair(source, "choice.calr", options, out var repaired, out var applied);
        Assert.Equal(1, applied);
        Assert.Equal(original.GeneratedCode, repaired.GeneratedCode);
        Assert.Equal(1, InvokePick(repaired.GeneratedCode, true).Value);
        if (mode == ContractMode.Off)
            Assert.Equal(2, InvokePick(repaired.GeneratedCode, false).Value);
        else
            Assert.Equal("ContractViolationException",
                Assert.Throws<TargetInvocationException>(() => InvokePick(repaired.GeneratedCode, false))
                    .InnerException?.GetType().Name);
    }

    [Fact]
    public void CanonicalLiteralReturn_DoesNotRewriteAContextuallyTypedLambda()
    {
        const string source = """
            module Choices
              func Make public () → int
                !
                let System.Func<bool, int>:pick = (bool:condition) ! =>
                  if condition
                    return 1
                  else
                    return 2
                return 0
            """;
        var result = CompileNative(source);
        Assert.Empty(LosslessSourceDocument.DiagnoseCanonicalUsage(source, "choice.calr", result.Ast!));
        Assert.Contains("if (condition)", result.GeneratedCode);
        Assert.Contains("if condition", new Migration.CalorEmitter().Emit(result.Ast!));
        Assert.Equal(source, DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(),
            out _, out var applied));
        Assert.Equal(0, applied);
    }
}
