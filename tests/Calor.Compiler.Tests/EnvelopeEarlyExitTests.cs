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

    // ------------------------------------------------------------------
    // Review of #754 item 1 — error-path matrix: for each adopted command,
    // a missing input in JSON mode must (a) exit non-zero (the exit code
    // must survive Main's InvokeAsync return, not be parked on
    // Environment.ExitCode) and (b) still put exactly one schema-valid
    // envelope on stdout carrying at least one error-severity diagnostic.
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the CLI and asserts the shared error-path contract: expected
    /// exit code, exactly one schema-valid envelope document on stdout for
    /// <paramref name="expectedCommand"/>, and ≥1 error-severity diagnostic
    /// (with <paramref name="expectedCode"/> present when given).
    /// </summary>
    private JsonElement AssertErrorEnvelope(
        int expectedExitCode, string expectedCommand, string? expectedCode, params string[] args)
    {
        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir, args);

        Assert.True(expectedExitCode == exitCode,
            $"expected exit {expectedExitCode}, got {exitCode} for 'calor {string.Join(' ', args)}'. stderr: {stdErr}");
        Assert.False(string.IsNullOrWhiteSpace(stdOut),
            $"expected an envelope on stdout for 'calor {string.Join(' ', args)}', got empty stdout. stderr: {stdErr}");

        var root = ParseSingleDocument(stdOut);
        Assert.Equal(expectedCommand, root.GetProperty("command").GetString());

        var errors = root.GetProperty("diagnostics").EnumerateArray()
            .Where(d => d.GetProperty("severity").GetString() == "error")
            .ToList();
        Assert.True(errors.Count >= 1, "expected at least one error-severity diagnostic in the envelope");
        if (expectedCode != null)
        {
            Assert.Contains(errors, d => d.GetProperty("code").GetString() == expectedCode);
        }

        return root;
    }

    [Fact]
    public void Verify_Json_MissingFile_ExitsNonZero_AndEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "gone.calr");
        AssertErrorEnvelope(1, "verify", DiagnosticCode.CliInputNotFound,
            "verify", missing, "--format", "json");
    }

    [Fact]
    public void Convert_Json_MissingInput_ExitsNonZero_AndEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "gone.cs");
        AssertErrorEnvelope(1, "convert", DiagnosticCode.ConvertCommandError,
            "convert", missing, "--format", "json");
    }

    [Fact]
    public void Coverage_MissingFile_ExitsNonZero_AndEmitsEnvelope()
    {
        // coverage always emits the JSON envelope (no --format option).
        var missing = Path.Combine(_tempDir, "gone.cs");
        AssertErrorEnvelope(1, "coverage", DiagnosticCode.CliInputNotFound,
            "coverage", missing);
    }

    [Fact]
    public void Benchmark_Json_MissingProject_ExitsNonZero_AndEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "no-such-project");
        AssertErrorEnvelope(1, "benchmark", DiagnosticCode.CliInputNotFound,
            "benchmark", missing, "--format", "json");
    }

    [Fact]
    public void Benchmark_Json_MissingCalorFile_ExitsNonZero_AndEmitsEnvelope()
    {
        var missingCalr = Path.Combine(_tempDir, "gone.calr");
        var missingCs = Path.Combine(_tempDir, "gone.cs");
        AssertErrorEnvelope(1, "benchmark", DiagnosticCode.CliInputNotFound,
            "benchmark", "--calor", missingCalr, "--csharp", missingCs, "--format", "json");
    }

    [Fact]
    public void Benchmark_Json_NoInputs_ExitsNonZero_AndEmitsEnvelope()
    {
        AssertErrorEnvelope(1, "benchmark", DiagnosticCode.CliUsageError,
            "benchmark", "--format", "json");
    }

    [Fact]
    public void Assess_Json_MissingDirectory_Exits2_AndEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "no-such-dir");
        AssertErrorEnvelope(2, "assess", DiagnosticCode.CliInputNotFound,
            "assess", missing, "--format", "json");
    }

    [Fact]
    public void Fix_Json_MissingRoot_Exits2_AndEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "no-such-root");
        AssertErrorEnvelope(2, "fix", DiagnosticCode.CliInputNotFound,
            "fix", missing, "--drop-structural-ids", "--format", "json");
    }

    [Fact]
    public void Fix_Json_NoOperationSelected_Exits2_AndEmitsEnvelope()
    {
        AssertErrorEnvelope(2, "fix", DiagnosticCode.CliUsageError,
            "fix", _tempDir, "--format", "json");
    }

    [Fact]
    public void EffectsSuggest_Json_MissingInput_ExitsNonZero_AndEmitsEnvelope()
    {
        var missing = Path.Combine(_tempDir, "gone.calr");
        AssertErrorEnvelope(1, "effects", DiagnosticCode.CliInputNotFound,
            "effects", "suggest", "-i", missing, "--json");
    }

    [Fact]
    public void EffectsResolve_Json_UnparsableSignature_ExitsNonZero_AndEmitsEnvelope()
    {
        AssertErrorEnvelope(1, "effects", DiagnosticCode.CliUsageError,
            "effects", "resolve", "nodotsignature", "--json");
    }

    // ------------------------------------------------------------------
    // verify exit-code semantics (review of #754, intentional change):
    // exit 1 when any file is missing, any compile error occurs, OR any
    // contract is refuted; exit 0 when all contracts are proven or merely
    // unknown/timeout/unsupported. Both text and JSON modes.
    // ------------------------------------------------------------------

    private string WriteRefutableFile()
    {
        // §S (> result 10) is refutable under §Q (> x 0) for §R (- x 1):
        // Z3 finds a counterexample (e.g. x = 1 → result = 0).
        var file = Path.Combine(_tempDir, "refutable.calr");
        File.WriteAllText(file,
            "§M{m1:Demo}\n  §F{f1:Dec:pub} (i32:x) -> i32\n    §Q (> x 0)\n    §S (> result 10)\n    §R (- x 1)\n");
        return file;
    }

    private string WriteProvableFile()
    {
        // §S (>= x 1) follows directly from §Q (> x 0) for §R x.
        var file = Path.Combine(_tempDir, "provable.calr");
        File.WriteAllText(file,
            "§M{m1:Demo}\n  §F{f1:Ok:pub} (i32:x) -> i32\n    §Q (> x 0)\n    §S (>= x 1)\n    §R x\n");
        return file;
    }

    [SkippableFact]
    public void Verify_RefutedContract_TextMode_Exits1()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var (exitCode, _, stdErr) = CliTestHarness.RunCli(_tempDir,
            "verify", WriteRefutableFile(), "--no-cache");

        Assert.True(exitCode == 1, $"refuted contract must exit 1 in text mode, got {exitCode}. stderr: {stdErr}");
    }

    [SkippableFact]
    public void Verify_RefutedContract_JsonMode_Exits1_WithEnvelope()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir,
            "verify", WriteRefutableFile(), "--format", "json", "--no-cache");

        Assert.True(exitCode == 1, $"refuted contract must exit 1 in JSON mode, got {exitCode}. stderr: {stdErr}");
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("verify", root.GetProperty("command").GetString());
        Assert.True(root.GetProperty("data").GetProperty("summary").GetProperty("refuted").GetInt32() >= 1,
            "expected at least one refuted contract in data.summary");
    }

    [SkippableFact]
    public void Verify_ProvenContract_TextMode_Exits0()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var (exitCode, _, stdErr) = CliTestHarness.RunCli(_tempDir,
            "verify", WriteProvableFile(), "--no-cache");

        Assert.True(exitCode == 0, $"fully-proven contracts must exit 0 in text mode, got {exitCode}. stderr: {stdErr}");
    }

    [SkippableFact]
    public void Verify_ProvenContract_JsonMode_Exits0()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir,
            "verify", WriteProvableFile(), "--format", "json", "--no-cache");

        Assert.True(exitCode == 0, $"fully-proven contracts must exit 0 in JSON mode, got {exitCode}. stderr: {stdErr}");
        var root = ParseSingleDocument(stdOut);
        Assert.Equal(0, root.GetProperty("data").GetProperty("summary").GetProperty("refuted").GetInt32());
    }

    [Fact]
    public void Verify_CompileError_JsonMode_Exits1_WithEnvelope()
    {
        // §B{x} with no type and no initializer is a deterministic compile
        // error (Calor0250) — a compile error must exit 1 (not Z3-dependent).
        var file = Path.Combine(_tempDir, "broken.calr");
        File.WriteAllText(file,
            "§M{m1:Broken}\n  §F{f1:Main:pub} () -> void\n    §B{x}\n");

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir,
            "verify", file, "--format", "json", "--no-cache");

        Assert.True(exitCode == 1, $"compile error must exit 1, got {exitCode}. stderr: {stdErr}");
        var root = ParseSingleDocument(stdOut);
        Assert.Contains(root.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("severity").GetString() == "error");
    }

    private const string ValidSource = "\u00a7M{m001:T}\n  \u00a7F{f001:Main:pub} () -> void\n    \u00a7E{cw}\n    \u00a7P \"hi\"\n";

    /// <summary>
    /// The PP-A1 item-7 containment: `format --write` is refused by release policy (#793/#760).
    /// The refusal is an early exit like the others in this file, and in JSON mode it was writing
    /// a bare line to stderr with EMPTY stdout — so an agent that asked for JSON saw a parse
    /// failure rather than a policy decision. The one message whose entire purpose is to be
    /// understood was the one it could not read.
    /// </summary>
    [Fact]
    public void FormatWrite_Json_RefusedWithoutExperimental_StillEmitsEnvelope()
    {
        var file = Path.Combine(_tempDir, "a.calr");
        File.WriteAllText(file, ValidSource);

        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir, NotAcknowledged,
            "format", file, "--write", "--format", "json");

        Assert.Equal(1, exitCode);
        var root = ParseSingleDocument(stdOut);
        Assert.Equal("format", root.GetProperty("command").GetString());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.FormatWriteExperimentalRequired, diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());

        // The refusal must name both escape hatches, or it is not actionable.
        var message = diagnostic.GetProperty("message").GetString()!;
        Assert.Contains("--experimental", message);
        Assert.Contains("CALOR_EXPERIMENTAL_FORMAT_WRITE", message);

        // Refused means refused: the file is untouched.
        Assert.Equal(ValidSource, File.ReadAllText(file));
    }

    /// <summary>
    /// Explicitly UN-acknowledged. Without this the refusal tests inherit the ambient environment,
    /// and `docs/cli/format.md` itself recommends `export CALOR_EXPERIMENTAL_FORMAT_WRITE=1` for a
    /// session or CI job — under which these tests would pass vacuously by never reaching the gate.
    /// </summary>
    private static readonly Dictionary<string, string> NotAcknowledged =
        new() { ["CALOR_EXPERIMENTAL_FORMAT_WRITE"] = "" };

    /// <summary>Text mode keeps its human-oriented stderr line and the same exit code.</summary>
    [Fact]
    public void FormatWrite_Text_RefusedWithoutExperimental()
    {
        var file = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(file, ValidSource);

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir, NotAcknowledged,
            "format", file, "--write");

        Assert.Equal(1, exitCode);
        Assert.Contains(DiagnosticCode.FormatWriteExperimentalRequired, stdErr);
        Assert.DoesNotContain("{", stdOut);
        Assert.Equal(ValidSource, File.ReadAllText(file));
    }

    /// <summary>
    /// `lint --fix` is the SAME containment as `format --write` — same diagnostic code, same
    /// acknowledgement, named together in the CHANGELOG and in structured-output.md. Its refusal
    /// was still ignoring --format after the format side was fixed, which is exactly the half-fix
    /// this file exists to catch. Covers `sarif` too, since lint offers it and `format` does not.
    /// </summary>
    [Theory]
    [InlineData("json")]
    [InlineData("sarif")]
    public void LintFix_Structured_RefusedWithoutExperimental_StillEmitsDocument(string format)
    {
        var file = Path.Combine(_tempDir, $"lint-{format}.calr");
        File.WriteAllText(file, ValidSource);

        var (exitCode, stdOut, _) = CliTestHarness.RunCli(_tempDir, NotAcknowledged,
            "lint", file, "--fix", "--format", format);

        Assert.Equal(1, exitCode);

        using var doc = JsonDocument.Parse(stdOut);
        Assert.Contains(DiagnosticCode.FormatWriteExperimentalRequired, stdOut);
        Assert.Equal(ValidSource, File.ReadAllText(file));
    }

    /// <summary>
    /// The control that makes the two above meaningful: the gate is an ACKNOWLEDGEMENT, not a
    /// removal. With the env var set the write path runs — so the tests above pin a policy gate
    /// rather than an unconditionally broken command.
    /// </summary>
    [Fact]
    public void FormatWrite_WithEnvAcknowledgement_IsAllowedThrough()
    {
        // Deliberately mis-formatted, so a successful run must CHANGE the file — feeding it
        // already-canonical source would let this pass without a write ever happening. The noise
        // is blank lines and trailing whitespace rather than bad indentation: over-indenting is a
        // parse error, and the formatter (correctly) declines to write a file it cannot parse,
        // which would make the test fail for a reason unrelated to the gate.
        var file = Path.Combine(_tempDir, "c.calr");
        var misformatted = ValidSource.Replace("\u00a7M{m001:T}\n", "\u00a7M{m001:T}\n\n\n")
                                      .Replace("-> void", "-> void   ");
        File.WriteAllText(file, misformatted);

        var (exitCode, _, stdErr) = CliTestHarness.RunCli(_tempDir,
            new Dictionary<string, string> { ["CALOR_EXPERIMENTAL_FORMAT_WRITE"] = "1" },
            "format", file, "--write");

        Assert.DoesNotContain(DiagnosticCode.FormatWriteExperimentalRequired, stdErr);
        Assert.NotEqual(1, exitCode);

        // The gate is an ACKNOWLEDGEMENT: past it, the write path actually writes.
        Assert.NotEqual(misformatted, File.ReadAllText(file));
    }
}
