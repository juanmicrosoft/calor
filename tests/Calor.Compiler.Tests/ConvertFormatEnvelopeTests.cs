using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Envelope adoption for <c>calor convert</c> and <c>calor format</c>
/// (schema v1.1, loop plan D1.3): drives the real CLI and asserts stdout
/// carries exactly one conformant envelope document — including on early-exit
/// error paths — with the new Calor1340-band CLI diagnostic codes, while text
/// mode stays untouched.
/// </summary>
public class ConvertFormatEnvelopeTests : IDisposable
{
    private readonly string _tempDir;

    public ConvertFormatEnvelopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-envelope-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
        => CliTestHarness.RunCli(_tempDir, args);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Unformatted but valid (trailing whitespace forces a reformat).</summary>
    private string WriteUnformattedCalr(string name = "unformatted.calr")
        => WriteFile(name,
            "§M{m1:Demo}\n" +
            "  §F{f1:Main:pub} () -> void\n" +
            "    §E{cw}\n" +
            "    §P \"hi\"   \n");

    private static JsonElement ParseAndValidate(string stdOut, string expectedCommand)
    {
        var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement;
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);
        Assert.Equal(JsonDiagnosticFormatter.SchemaVersion, root.GetProperty("version").GetString());
        Assert.Equal(expectedCommand, root.GetProperty("command").GetString());
        return root;
    }

    // ------------------------------------------------------------------
    // calor format --format json
    // ------------------------------------------------------------------

    [Fact]
    public void Format_Json_Preview_EmitsEnvelopeWithFormattedSource()
    {
        var file = WriteUnformattedCalr();

        var (exitCode, stdOut, stdErr) = RunCli("format", file, "--format", "json");

        Assert.True(exitCode == 0, $"expected exit 0, got {exitCode}. stderr: {stdErr}");
        var root = ParseAndValidate(stdOut, "format");
        Assert.Equal(0, root.GetProperty("summary").GetProperty("errors").GetInt32());

        var entry = root.GetProperty("data").GetProperty("files")[0];
        Assert.Equal(file, entry.GetProperty("path").GetString());
        Assert.True(entry.GetProperty("changed").GetBoolean());
        Assert.Equal("formatted", entry.GetProperty("status").GetString());
        // Preview mode (neither --write nor --check) embeds the formatted source
        Assert.Contains("§M{m1:Demo}", entry.GetProperty("formatted").GetString());

        var totals = root.GetProperty("data").GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("processed").GetInt32());
        Assert.Equal(0, totals.GetProperty("errors").GetInt32());
    }

    [Fact]
    public void Format_Json_Check_ReportsWouldReformat_WithoutEmbeddingSource()
    {
        var file = WriteUnformattedCalr();

        var (exitCode, stdOut, _) = RunCli("format", file, "--check", "--format", "json");

        Assert.Equal(1, exitCode); // --check with unformatted input, unchanged
        var root = ParseAndValidate(stdOut, "format");

        var entry = root.GetProperty("data").GetProperty("files")[0];
        Assert.Equal("would-reformat", entry.GetProperty("status").GetString());
        Assert.False(entry.TryGetProperty("formatted", out _));
    }

    [Fact]
    public void Format_Json_MissingFile_InjectsCalor1340AndStillEmitsDocument()
    {
        var missing = Path.Combine(_tempDir, "missing.calr");

        var (exitCode, stdOut, _) = RunCli("format", missing, "--format", "json");

        Assert.Equal(2, exitCode); // file-level error, unchanged
        var root = ParseAndValidate(stdOut, "format");

        var diag = root.GetProperty("diagnostics")[0];
        Assert.Equal(DiagnosticCode.FormatFileNotFound, diag.GetProperty("code").GetString());
        Assert.Equal("error", diag.GetProperty("severity").GetString());
        Assert.EndsWith("missing.calr", diag.GetProperty("location").GetProperty("file").GetString());

        Assert.Equal("not-found",
            root.GetProperty("data").GetProperty("files")[0].GetProperty("status").GetString());
        Assert.True(root.GetProperty("summary").GetProperty("errors").GetInt32() >= 1);
    }

    [Fact]
    public void Format_Json_NonCalrFile_InjectsCalor1341Warning()
    {
        var csFile = WriteFile("notcalor.cs", "public class C { }");

        var (exitCode, stdOut, _) = RunCli("format", csFile, "--format", "json");

        Assert.Equal(0, exitCode); // skipping non-.calr is not an error, unchanged
        var root = ParseAndValidate(stdOut, "format");

        var diag = root.GetProperty("diagnostics")[0];
        Assert.Equal(DiagnosticCode.FormatUnsupportedFileType, diag.GetProperty("code").GetString());
        Assert.Equal("warning", diag.GetProperty("severity").GetString());
        Assert.Equal("skipped",
            root.GetProperty("data").GetProperty("files")[0].GetProperty("status").GetString());
    }

    [Fact]
    public void Format_Json_ParseError_SurfacesRealParserDiagnostics()
    {
        var file = WriteFile("broken.calr", "§M{m1:Bad}\n  §F{f1:Main:pub () -> void\n");

        var (exitCode, stdOut, _) = RunCli("format", file, "--format", "json");

        Assert.Equal(2, exitCode); // parse failure is a file error, unchanged
        var root = ParseAndValidate(stdOut, "format");

        var diagnostics = root.GetProperty("diagnostics");
        Assert.True(diagnostics.GetArrayLength() >= 1);
        // Real compiler diagnostics with their own codes and locations
        Assert.All(diagnostics.EnumerateArray(), d =>
        {
            Assert.StartsWith("Calor", d.GetProperty("code").GetString());
            Assert.True(d.GetProperty("location").GetProperty("line").GetInt32() >= 1);
        });
        Assert.Equal("error",
            root.GetProperty("data").GetProperty("files")[0].GetProperty("status").GetString());
        Assert.True(root.GetProperty("summary").GetProperty("errors").GetInt32() >= 1);
    }

    [Fact]
    public void Format_TextMode_StdoutStillCarriesFormattedSource_NotJson()
    {
        var file = WriteUnformattedCalr();

        var (exitCode, stdOut, _) = RunCli("format", file);

        Assert.Equal(0, exitCode);
        Assert.Contains("§M{m1:Demo}", stdOut);
        Assert.DoesNotContain("\"diagnostics\"", stdOut);
    }

    // ------------------------------------------------------------------
    // calor convert --format json
    // ------------------------------------------------------------------

    [Fact]
    public void Convert_Json_CSharpToCalor_Success_EmitsEnvelopeWithData()
    {
        var csFile = WriteFile("Sample.cs", """
            namespace N
            {
                public class Sample
                {
                    public int Add(int a, int b) => a + b;
                }
            }
            """);

        var (_, stdOut, stdErr) = RunCli("convert", csFile, "--validate", "--format", "json");

        var root = ParseAndValidate(stdOut, "convert");
        // Human status ("✓ Conversion successful") must be on stderr, not stdout
        Assert.DoesNotContain("Conversion successful", stdOut);
        Assert.Contains("Conversion successful", stdErr);

        var data = root.GetProperty("data");
        Assert.Equal("csharp-to-calor", data.GetProperty("direction").GetString());
        Assert.Equal(csFile, data.GetProperty("inputPath").GetString());
        Assert.EndsWith(".calr", data.GetProperty("outputPath").GetString());
        Assert.True(data.GetProperty("success").GetBoolean());
        Assert.True(data.GetProperty("validated").GetBoolean());
        Assert.Equal(0, data.GetProperty("validationErrorCount").GetInt32());
    }

    [Fact]
    public void Convert_Json_ConversionIssues_BecomeCalor1343_WithFeaturePrefix()
    {
        // A destructor is dropped with a feature-tagged warning issue.
        var csFile = WriteFile("Dtor.cs", """
            namespace N
            {
                public class D
                {
                    ~D() { }
                    public int X() => 1;
                }
            }
            """);

        var (_, stdOut, _) = RunCli("convert", csFile, "--format", "json");

        var root = ParseAndValidate(stdOut, "convert");
        var issue = root.GetProperty("diagnostics").EnumerateArray()
            .Single(d => d.GetProperty("code").GetString() == DiagnosticCode.ConversionIssue);
        Assert.Equal("warning", issue.GetProperty("severity").GetString());
        Assert.StartsWith("[unsupported-member]", issue.GetProperty("message").GetString());
        Assert.Equal(5, issue.GetProperty("location").GetProperty("line").GetInt32());
        Assert.EndsWith("Dtor.cs", issue.GetProperty("location").GetProperty("file").GetString());
        Assert.True(root.GetProperty("summary").GetProperty("warnings").GetInt32() >= 1);
    }

    [Fact]
    public void Convert_Json_CSharpParseError_IsCalor1343Error()
    {
        var csFile = WriteFile("Broken.cs", "public class Broken {");

        var (_, stdOut, _) = RunCli("convert", csFile, "--format", "json");

        var root = ParseAndValidate(stdOut, "convert");
        Assert.Contains(root.GetProperty("diagnostics").EnumerateArray(), d =>
            d.GetProperty("code").GetString() == DiagnosticCode.ConversionIssue &&
            d.GetProperty("severity").GetString() == "error");
        Assert.False(root.GetProperty("data").GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Convert_Json_MissingInput_EmitsDocumentWithCalor1345()
    {
        var missing = Path.Combine(_tempDir, "nope.cs");

        var (exitCode, stdOut, _) = RunCli("convert", missing, "--format", "json");

        // Review of #754: the error exit code now propagates through
        // ctx.ExitCode (previously stomped to 0 by Main's InvokeAsync return).
        Assert.Equal(1, exitCode);

        // A document is ALWAYS emitted, even on early-exit error paths.
        var root = ParseAndValidate(stdOut, "convert");
        var diag = root.GetProperty("diagnostics")[0];
        Assert.Equal(DiagnosticCode.ConvertCommandError, diag.GetProperty("code").GetString());
        Assert.Equal("error", diag.GetProperty("severity").GetString());
        Assert.EndsWith("nope.cs", diag.GetProperty("location").GetProperty("file").GetString());
        Assert.False(root.GetProperty("data").GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Convert_Json_UnknownFileType_EmitsDocumentWithCalor1345()
    {
        var txt = WriteFile("blah.txt", "hello");

        var (exitCode, stdOut, _) = RunCli("convert", txt, "--format", "json");

        Assert.Equal(1, exitCode);
        var root = ParseAndValidate(stdOut, "convert");
        var diag = root.GetProperty("diagnostics")[0];
        Assert.Equal(DiagnosticCode.ConvertCommandError, diag.GetProperty("code").GetString());
        Assert.Contains("Unknown file type", diag.GetProperty("message").GetString());
    }

    [Fact]
    public void Convert_Json_CalorToCSharp_CompileErrors_UseRealDiagnosticCodes()
    {
        // §B{x} with no type and no initializer → deterministic Calor0250 at line 3.
        var calrFile = WriteFile("bad.calr",
            "§M{m1:Bad}\n" +
            "  §F{f1:Main:pub} () -> void\n" +
            "    §B{x}\n");

        var (exitCode, stdOut, _) = RunCli("convert", calrFile, "--format", "json");

        Assert.Equal(1, exitCode);
        var root = ParseAndValidate(stdOut, "convert");
        var data = root.GetProperty("data");
        Assert.Equal("calor-to-csharp", data.GetProperty("direction").GetString());
        Assert.False(data.GetProperty("success").GetBoolean());

        var diag = root.GetProperty("diagnostics").EnumerateArray()
            .Single(d => d.GetProperty("code").GetString() == "Calor0250");
        Assert.Equal("error", diag.GetProperty("severity").GetString());
        Assert.Equal(3, diag.GetProperty("location").GetProperty("line").GetInt32());
    }

    [Fact]
    public void Convert_Json_Benchmark_PopulatesDataPayload()
    {
        var csFile = WriteFile("Bench.cs", """
            namespace N
            {
                public class Bench
                {
                    public int Twice(int a) => a * 2;
                }
            }
            """);

        var (_, stdOut, stdErr) = RunCli("convert", csFile, "--benchmark", "--format", "json");

        var root = ParseAndValidate(stdOut, "convert");
        var benchmark = root.GetProperty("data").GetProperty("benchmark");
        Assert.True(benchmark.GetProperty("originalTokens").GetInt32() > 0);
        Assert.True(benchmark.GetProperty("outputTokens").GetInt32() > 0);
        Assert.True(benchmark.GetProperty("advantageRatio").GetDouble() > 0);
        // The human-readable comparison table moves to stderr
        Assert.Contains("Token Economics", stdErr);
        Assert.DoesNotContain("Token Economics", stdOut);
    }

    [Fact]
    public void Convert_TextMode_StdoutUnchanged_NoJson()
    {
        var csFile = WriteFile("Plain.cs", """
            namespace N
            {
                public class Plain
                {
                    public int One() => 1;
                }
            }
            """);

        var (_, stdOut, _) = RunCli("convert", csFile);

        Assert.Contains("Conversion successful", stdOut);
        Assert.DoesNotContain("\"diagnostics\"", stdOut);
    }
}
