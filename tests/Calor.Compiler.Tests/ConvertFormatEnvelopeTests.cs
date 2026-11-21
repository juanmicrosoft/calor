using System.CommandLine;
using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
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
            "module Demo Demo block\n" +
            "  func Main () → void\n" +
            "    ! cw\n" +
            "    Console.WriteLine(\"hi\")   \n");

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
        Assert.Contains("module Demo Demo block", entry.GetProperty("formatted").GetString());
        Assert.False(entry.TryGetProperty("conservativeFallbackReason", out _));

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
        var file = WriteFile("broken.calr", "§M{Bad}\n  §F{f1:Main:pub () -> void\n");

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
        Assert.Contains("module Demo Demo block", stdOut);
        Assert.DoesNotContain("\"diagnostics\"", stdOut);
    }

    [Fact]
    public void Lint_ReadOnlySemanticError_ReportsIssuesWithoutParseFailure()
    {
        var source =
            "module LintReadOnly LintReadOnly block\n" +
            "  func Main () → void\n" +
            "    !\n" +
            "    Console.WriteLine(\"missing effect\")   \n";
        var file = WriteFile("lint-read-only.calr", source);

        var (exitCode, stdOut, stdErr) =
            RunCli("lint", file, "--format", "json");

        Assert.Equal(1, exitCode);
        var root = JsonDocument.Parse(stdOut).RootElement;
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);
        Assert.Equal(0, root.GetProperty("summary").GetProperty("errors").GetInt32());
        Assert.Contains(
            root.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString()
                == DiagnosticCode.LintTrailingWhitespace);
        Assert.DoesNotContain(
            root.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString()
                == DiagnosticCode.FormatConservativeFallback);
        Assert.DoesNotContain("Error parsing", stdErr, StringComparison.Ordinal);
        Assert.Equal(source, File.ReadAllText(file));
    }

    [Fact]
    public void LintFix_SemanticError_SuppressesWriteWithWarningAndExitOne()
    {
        var source =
            "module LintFix LintFix block\n" +
            "  func Main () → void\n" +
            "    !\n" +
            "    Console.WriteLine(\"missing effect\")   \n";
        var file = WriteFile("lint-fix.calr", source);

        var (exitCode, stdOut, stdErr) =
            RunCli("lint", file, "--fix", "--format", "json");

        Assert.Equal(1, exitCode);
        var root = JsonDocument.Parse(stdOut).RootElement;
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);
        Assert.Equal(0, root.GetProperty("summary").GetProperty("errors").GetInt32());
        var fallback = Assert.Single(
            root.GetProperty("diagnostics").EnumerateArray().Where(
                diagnostic => diagnostic.GetProperty("code").GetString()
                    == DiagnosticCode.FormatConservativeFallback));
        Assert.Equal("warning", fallback.GetProperty("severity").GetString());
        Assert.DoesNotContain(
            root.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString()
                == DiagnosticCode.LintProcessingError);
        Assert.Contains("fix suppressed", stdErr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Error parsing", stdErr, StringComparison.Ordinal);
        Assert.Equal(source, File.ReadAllText(file));
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
        Assert.Equal("info", issue.GetProperty("severity").GetString());
        Assert.StartsWith("[destructor]", issue.GetProperty("message").GetString());
        Assert.Equal(5, issue.GetProperty("location").GetProperty("line").GetInt32());
        Assert.EndsWith("Dtor.cs", issue.GetProperty("location").GetProperty("file").GetString());
        Assert.True(root.GetProperty("summary").GetProperty("info").GetInt32() >= 1);
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
        // Native inferred bindings require an initializer during parsing, before binding.
        var calrFile = WriteFile("bad.calr",
            "module Bad\n" +
            "  func Main public () → void\n" +
            "    let x\n");

        var (exitCode, stdOut, _) = RunCli("convert", calrFile, "--format", "json");

        Assert.Equal(1, exitCode);
        var root = ParseAndValidate(stdOut, "convert");
        var data = root.GetProperty("data");
        Assert.Equal("calor-to-csharp", data.GetProperty("direction").GetString());
        Assert.False(data.GetProperty("success").GetBoolean());

        var diag = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCode.UnexpectedToken, diag.GetProperty("code").GetString());
        Assert.Equal("An inferred binding requires '= expression'.", diag.GetProperty("message").GetString());
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
        Assert.True(benchmark.GetProperty("compactnessRatio").GetDouble() > 0);
        Assert.Equal(
            benchmark.GetProperty("compactnessRatio").GetDouble(),
            benchmark.GetProperty("advantageRatio").GetDouble());
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

    // ------------------------------------------------------------------
    // W1 Slice 3 (#770): no false "✓ Conversion successful" — a conversion
    // with recorded semantic losses prints a located loss summary instead,
    // and the JSON envelope carries the structured loss accounting.
    // ------------------------------------------------------------------

    [Fact]
    public void Convert_TextMode_WithLosses_PrintsLossSummary_NotSuccessLine()
    {
        // Record structs remain exact, counted interop rather than record classes.
        var csFile = WriteFile("Rec.cs", """
            namespace N
            {
                public record struct Person(string Name, int Age);
            }
            """);

        var (exitCode, stdOut, _) = RunCli("convert", csFile);

        Assert.Equal(0, exitCode); // conversion still succeeds — but honestly
        Assert.DoesNotContain("Conversion successful", stdOut);
        Assert.Contains("interop preservation", stdOut);
        Assert.Contains("InteropPreserved", stdOut);
        // The loss line carries a file:line location.
        Assert.Contains("Rec.cs:", stdOut);
        Assert.Contains("[record-struct]", stdOut);
    }

    [Fact]
    public void Convert_Json_WithLosses_EnvelopeCarriesLossAccounting()
    {
        var csFile = WriteFile("Rec2.cs", """
            namespace N
            {
                public record struct Point(int X, int Y);
            }
            """);

        var (_, stdOut, stdErr) = RunCli("convert", csFile, "--format", "json");

        var root = ParseAndValidate(stdOut, "convert");
        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("success").GetBoolean());
        Assert.True(data.GetProperty("lossCount").GetInt32() >= 1);
        Assert.Equal(1, data.GetProperty("interopPreservationCount").GetInt32());
        Assert.Equal(0, data.GetProperty("lossySubstitutionCount").GetInt32());
        Assert.Equal(0, data.GetProperty("dropCount").GetInt32());

        var loss = data.GetProperty("losses").EnumerateArray()
            .Single(l => l.GetProperty("feature").GetString() == "record-struct");
        Assert.Equal("InteropPreserved", loss.GetProperty("kind").GetString());
        Assert.True(loss.GetProperty("line").GetInt32() >= 1);

        // Human loss summary goes to stderr in envelope mode; no success line.
        Assert.DoesNotContain("Conversion successful", stdErr);
        Assert.Contains("interop preservation", stdErr);
    }

    [Fact]
    public void Convert_Json_DefaultPreservesDestructorInsteadOfDroppingIt()
    {
        var csFile = WriteFile("LossyDtor.cs", """
            public class D
            {
                ~D() { }
            }
            """);

        var (exitCode, stdOut, _) = RunCli("convert", csFile, "--format", "json");

        Assert.Equal(0, exitCode);
        var data = ParseAndValidate(stdOut, "convert").GetProperty("data");
        Assert.Equal("lossless", data.GetProperty("fidelity").GetString());
        Assert.Equal(0, data.GetProperty("dropCount").GetInt32());
        var loss = Assert.Single(data.GetProperty("losses").EnumerateArray());
        Assert.Equal("InteropPreserved", loss.GetProperty("kind").GetString());
        Assert.EndsWith("LossyDtor.cs", loss.GetProperty("file").GetString());
        Assert.True(loss.GetProperty("line").GetInt32() > 0);
    }

    [Fact]
    public void Convert_ValidationFailureLeavesExistingDestinationUnchanged()
    {
        var csFile = WriteFile("InvalidEmission.cs", """
            public class Service
            {
                public string Process(bool enabled, int code) =>
                    enabled ? code switch { 0 => "Zero", _ => "Other" } : "Disabled";
            }
            """);
        var output = WriteFile("Existing.calr", "original bytes");

        var (exitCode, _, _) = RunCli(
            "convert",
            csFile,
            "--lossy",
            "--output",
            output);

        Assert.Equal(1, exitCode);
        Assert.Equal("original bytes", File.ReadAllText(output));
    }

    [Fact]
    public void Convert_TextMode_NoLosses_StillPrintsSuccessLine()
    {
        var csFile = WriteFile("CleanFile.cs", """
            namespace N
            {
                public class Clean
                {
                    public int Two() => 2;
                }
            }
            """);

        var (exitCode, stdOut, _) = RunCli("convert", csFile, "--validate");

        Assert.Equal(0, exitCode);
        Assert.Contains("Conversion successful", stdOut);
        Assert.DoesNotContain("semantic loss", stdOut);
    }

    [Theory]
    [InlineData("public class C { public int M() => 1; }", false)]
    [InlineData("public class C { public void M() { for (int i = 0; int.TryParse(\"0\", out var n); i++) { } } }", true)]
    [InlineData("public class C { ~C() { /* \u03c0 */ } }\r\n", true)]
    [InlineData("namespace A { class C { } }\r\nnamespace B { class D { } }", true)]
    public void Convert_Receipt_InitialGenerationAndUnchangedRefresh(string source, bool preserved)
    {
        var input = WriteFile("Original.cs", source);
        var output = Path.ChangeExtension(input, ".calr");
        var (exit, json, error) = RunCli("convert", input, "--format", "json");
        Assert.True(exit == 0, error);
        var data = ParseAndValidate(json, "convert").GetProperty("data");
        Assert.True(data.GetProperty("validated").GetBoolean());
        Assert.Equal(preserved ? "preserved" : "native",
            data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        var receiptText = File.ReadAllText(output + ".conversion.json");
        var receipt = ConversionReceipt.Deserialize(receiptText);
        Assert.Equal(source, receipt.Original.Text);
        Assert.Equal(input, receipt.Original.Path);
        Assert.Equal(File.ReadAllText(output), receipt.GeneratedCalor);
        Assert.Equal(preserved ? 1 : 0, receipt.Frames.Count);
        if (preserved)
        {
            var boundary = receipt.ResolveBoundary(0, receipt.GeneratedCalor);
            Assert.Equal(source, boundary.SyntaxTree.GetText().ToString());
        }
        ConversionReceipt.VerifyInputs(receipt.Inputs);

        var refresh = RunCli("convert", input, "--format", "json");
        Assert.True(refresh.ExitCode == 0, refresh.StdErr);
        Assert.Equal(receipt.GeneratedCalor, File.ReadAllText(output));
        Assert.Equal(receiptText, File.ReadAllText(output + ".conversion.json"));
    }

    [Theory]
    [InlineData("regular", "Regular")]
    [InlineData("script", "Script")]
    public void Convert_Receipt_RecordsOriginalOptionsAndParseContext(string sourceKind, string expectedKind)
    {
        const string source = "#if ENABLED\r\npublic record struct R(int X);\r\n#else\r\nnot C# \u03c0\r\n#endif";
        var input = WriteFile("Context.cs", source);
        var run = RunCli("convert", input, "--format", "json", "--define", "ENABLED",
            "--configuration", "Release", "--framework", "net10.0", "--language-version", "preview",
            "--documentation-mode", "diagnose", "--source-kind", sourceKind,
            "--reference", typeof(Console).Assembly.Location + "=console_alias",
            "--feature", "receipt-control=true");
        Assert.True(run.ExitCode == 0, run.StdErr);
        var receipt = ConversionReceipt.Deserialize(
            File.ReadAllText(Path.ChangeExtension(input, ".calr") + ".conversion.json"));
        Assert.Equal(source, receipt.Original.Text);
        Assert.Equal(["ENABLED"], receipt.Original.Symbols);
        Assert.Equal("Preview", receipt.Original.LanguageVersion);
        Assert.Equal("Diagnose", receipt.Original.DocumentationMode);
        Assert.Equal(expectedKind, receipt.Original.SourceCodeKind);
        Assert.Equal("true", receipt.Original.Features["receipt-control"]);
        Assert.Equal("Release", receipt.Metadata.Configuration);
        Assert.Equal("net10.0", receipt.Metadata.TargetFramework);
        Assert.Contains(receipt.Metadata.References, reference => reference.Path == typeof(Console).Assembly.Location
            && reference.Aliases.SequenceEqual(["console_alias"]));
        Assert.False(receipt.Settings.TryGetProperty("UseImplicitCallCloser", out _));
        Assert.True(receipt.Settings.GetProperty("ValidateRoundTripCSharp").GetBoolean());
        Assert.True(receipt.Settings.GetProperty("RescueUnusableMembers").GetBoolean());
        Assert.False(receipt.Settings.GetProperty("PassthroughOnError").GetBoolean());
        Assert.Contains(receipt.Inputs, observed => observed.Path == input
            && observed.Sha256 == Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))));
        Assert.Contains(receipt.Frames, frame => frame.Payload.Contains("not C# \u03c0", StringComparison.Ordinal));
    }

    [Fact]
    public void Convert_Receipt_UnchangedOutputAllowsChangedOriginalRefresh()
    {
        var input = WriteFile("Refresh.cs", "public class C { public int M() => 1; }");
        Assert.Equal(0, RunCli("convert", input).ExitCode);
        const string changed = "public class C { public int M() => 2; }";
        File.WriteAllText(input, changed);
        var refresh = RunCli("convert", input, "--format", "json");
        Assert.True(refresh.ExitCode == 0, refresh.StdErr);
        var output = Path.ChangeExtension(input, ".calr");
        var receipt = ConversionReceipt.Deserialize(File.ReadAllText(output + ".conversion.json"));
        Assert.Equal(changed, receipt.Original.Text);
        Assert.Equal(File.ReadAllText(output), receipt.GeneratedCalor);
        Assert.Contains("return 2", receipt.GeneratedCalor);
    }

    [Theory]
    [InlineData("edited")]
    [InlineData("missing-receipt")]
    [InlineData("missing-output")]
    [InlineData("unassociated")]
    [InlineData("malformed-receipt")]
    [InlineData("other-source")]
    public void Convert_Receipt_RefusesEditedOrUnassociatedBasesWithoutOverwriting(string state)
    {
        var input = WriteFile("Guarded.cs", "public class C { public int M() => 1; }");
        var output = Path.ChangeExtension(input, ".calr");
        var receiptPath = output + ".conversion.json";
        if (state == "unassociated")
            File.WriteAllText(output, "unassociated output\r\n");
        else
            Assert.Equal(0, RunCli("convert", input).ExitCode);
        switch (state)
        {
            case "edited": File.AppendAllText(output, "// user edit\r\n"); break;
            case "missing-receipt": File.Delete(receiptPath); break;
            case "missing-output": File.Delete(output); break;
            case "malformed-receipt": File.WriteAllText(receiptPath, "{"); break;
            case "other-source": input = WriteFile("Other.cs", File.ReadAllText(input)); break;
        }
        var before = ArtifactBytes(output);
        var beforeReceipt = ArtifactBytes(receiptPath);
        var run = RunCli("convert", input, "-o", output, "--format", "json");
        Assert.Equal(1, run.ExitCode);
        Assert.False(ParseAndValidate(run.StdOut, "convert").GetProperty("data").GetProperty("success").GetBoolean());
        Assert.Equal(before, ArtifactBytes(output));
        Assert.Equal(beforeReceipt, ArtifactBytes(receiptPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Convert_Receipt_ConversionRefusalWritesNeitherArtifact(bool existing)
    {
        var input = WriteFile("Broken.cs", "public class C { public int M() => 1; }");
        if (existing) Assert.Equal(0, RunCli("convert", input).ExitCode);
        File.WriteAllText(input, "public class Broken { public int M( { }");
        var output = Path.ChangeExtension(input, ".calr");
        var before = ArtifactBytes(output);
        var beforeReceipt = ArtifactBytes(output + ".conversion.json");
        var run = RunCli("convert", input, "--format", "json");
        Assert.Equal(1, run.ExitCode);
        var data = ParseAndValidate(run.StdOut, "convert").GetProperty("data");
        Assert.False(data.GetProperty("success").GetBoolean());
        Assert.Equal("refused", data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        Assert.Equal(before, ArtifactBytes(output));
        Assert.Equal(beforeReceipt, ArtifactBytes(output + ".conversion.json"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Convert_Receipt_MandatoryValidationPrecedesBothWrites(bool existing)
    {
        var input = WriteFile("Validation.cs", "public record struct R(int X);");
        var output = Path.ChangeExtension(input, ".calr");
        if (existing) Assert.Equal(0, RunCli("convert", input).ExitCode);
        var before = ArtifactBytes(output);
        var beforeReceipt = ArtifactBytes(output + ".conversion.json");
        var conversions = 0;
        var run = await InvokeReceiptCommandAsync([input, "--format", "json"],
            completed: (_, _) => conversions++,
            additionalValidation: _ => new Diagnostic(DiagnosticCode.ExpectedExpression,
                "injected additional validation rejection", new TextSpan(0, 0, 1, 1), DiagnosticSeverity.Error));
        Assert.Equal(1, conversions);
        Assert.Equal(1, run.ExitCode);
        var root = ParseAndValidate(run.StdOut, "convert");
        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("validated").GetBoolean());
        Assert.Equal(1, data.GetProperty("validationErrorCount").GetInt32());
        Assert.Equal("refused", data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        Assert.True(data.GetProperty("lossCount").GetInt32() > 0);
        Assert.Contains(root.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == DiagnosticCode.ConvertValidationError);
        Assert.Contains("Destination was not modified.", run.StdErr);
        Assert.Equal(before, ArtifactBytes(output));
        Assert.Equal(beforeReceipt, ArtifactBytes(output + ".conversion.json"));

        var positive = await InvokeReceiptCommandAsync([input, "--format", "json"],
            completed: (_, _) => conversions++);
        Assert.True(positive.ExitCode == 0, positive.StdErr);
        Assert.Equal(2, conversions);
        Assert.NotNull(ArtifactBytes(output + ".conversion.json"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Convert_Receipt_CancellationAfterConversionWritesNeitherArtifact(bool existing)
    {
        var input = WriteFile("Cancellation.cs", "public record struct R(int X);");
        var output = Path.ChangeExtension(input, ".calr");
        if (existing) Assert.Equal(0, RunCli("convert", input).ExitCode);
        var before = ArtifactBytes(output);
        var beforeReceipt = ArtifactBytes(output + ".conversion.json");
        var conversions = 0;
        var run = await InvokeReceiptCommandAsync([input, "--format", "json", "--timeout", "30"],
            completed: (_, timeout) =>
            {
                conversions++;
                Assert.NotNull(timeout);
                timeout.Cancel();
            });
        Assert.Equal(1, conversions);
        Assert.Equal(1, run.ExitCode);
        var data = ParseAndValidate(run.StdOut, "convert").GetProperty("data");
        Assert.False(data.GetProperty("success").GetBoolean());
        Assert.Equal("refused", data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        Assert.True(data.GetProperty("lossCount").GetInt32() > 0);
        Assert.Contains("Destination was not modified.", run.StdErr);
        Assert.Equal(before, ArtifactBytes(output));
        Assert.Equal(beforeReceipt, ArtifactBytes(output + ".conversion.json"));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("output")]
    [InlineData("receipt")]
    public async Task Convert_Receipt_RefusesStalePreparedInputsOrGenerationBases(string changed)
    {
        var input = WriteFile("Stale.cs", "public record struct R(int X);");
        var output = Path.ChangeExtension(input, ".calr");
        var receiptPath = output + ".conversion.json";
        Assert.Equal(0, RunCli("convert", input).ExitCode);
        byte[]? expectedOutput = null;
        byte[]? expectedReceipt = null;
        var run = await InvokeReceiptCommandAsync([input, "--format", "json"], completed: (_, _) =>
        {
            File.AppendAllText(changed switch
            {
                "source" => input,
                "output" => output,
                _ => receiptPath
            }, "// external edit\r\n");
            expectedOutput = ArtifactBytes(output);
            expectedReceipt = ArtifactBytes(receiptPath);
        });
        Assert.Equal(1, run.ExitCode);
        var data = ParseAndValidate(run.StdOut, "convert").GetProperty("data");
        Assert.Equal("refused", data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        Assert.True(data.GetProperty("lossCount").GetInt32() > 0);
        Assert.Equal(expectedOutput, ArtifactBytes(output));
        Assert.Equal(expectedReceipt, ArtifactBytes(receiptPath));
    }

    [Fact]
    public async Task Convert_Receipt_CooperatingCliCannotRacePreparedConversion()
    {
        var input = WriteFile("Concurrent.cs", "public class C { public int M() => 1; }");
        var output = Path.ChangeExtension(input, ".calr");
        var conversions = 0;
        var run = await InvokeReceiptCommandAsync([input, "--format", "json"], completed: (_, _) =>
        {
            conversions++;
            var competing = RunCli("convert", input, "--format", "json");
            Assert.Equal(1, competing.ExitCode);
            Assert.False(ParseAndValidate(competing.StdOut, "convert")
                .GetProperty("data").GetProperty("success").GetBoolean());
            Assert.Null(ArtifactBytes(output));
            Assert.Null(ArtifactBytes(output + ".conversion.json"));
        });
        Assert.True(run.ExitCode == 0, run.StdErr);
        Assert.Equal(1, conversions);
        Assert.Equal(File.ReadAllText(output),
            ConversionReceipt.Deserialize(File.ReadAllText(output + ".conversion.json")).GeneratedCalor);
    }

    [Fact]
    public async Task Convert_Receipt_FinalReceiptFailureReportsAlreadyWrittenOutput()
    {
        var input = WriteFile("FinalReceipt.cs", "public record struct R(int X);");
        var output = Path.ChangeExtension(input, ".calr");
        var receiptPath = output + ".conversion.json";
        var run = await InvokeReceiptCommandAsync([input, "--format", "json"],
            completed: (_, _) => Directory.CreateDirectory(receiptPath));
        Assert.Equal(1, run.ExitCode);
        var data = ParseAndValidate(run.StdOut, "convert").GetProperty("data");
        Assert.False(data.GetProperty("success").GetBoolean());
        Assert.Equal("preserved", data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        Assert.True(data.GetProperty("lossCount").GetInt32() > 0);
        Assert.NotNull(ArtifactBytes(output));
        Assert.Null(ArtifactBytes(receiptPath));
        Assert.Contains("Output was written but its conversion context could not be saved:", run.StdErr);
        Assert.DoesNotContain("Destination was not modified.", run.StdErr);
        var written = ArtifactBytes(output);
        Assert.Equal(1, RunCli("convert", input, "--format", "json").ExitCode);
        Assert.Equal(written, ArtifactBytes(output));
    }

    [Fact]
    public void Convert_Receipt_UnverifiedOutputKeepsExitTwoAndItsGenerationContext()
    {
        var input = WriteFile("Unverified.cs",
            "public class C { public ExternalType M() => new ExternalType(); }");
        var run = RunCli("convert", input, "--format", "json");
        Assert.Equal(2, run.ExitCode);
        var data = ParseAndValidate(run.StdOut, "convert").GetProperty("data");
        Assert.Equal("unverified", data.GetProperty("conversionPaths").GetProperty("outcome").GetString());
        var output = Path.ChangeExtension(input, ".calr");
        Assert.Equal(File.ReadAllText(output),
            ConversionReceipt.Deserialize(File.ReadAllText(output + ".conversion.json")).GeneratedCalor);
    }

    private static byte[]? ArtifactBytes(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeReceiptCommandAsync(
        string[] args, Action<ConversionResult, CancellationTokenSource?>? completed = null,
        Func<string, Diagnostic?>? additionalValidation = null)
    {
        var oldOut = Console.Out;
        var oldError = Console.Error;
        var oldCompleted = ConvertCommand.ConversionCompletedObserver.Value;
        var oldValidation = ConvertCommand.AdditionalValidationError.Value;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            ConvertCommand.ConversionCompletedObserver.Value = completed;
            ConvertCommand.AdditionalValidationError.Value = additionalValidation;
            var exit = await ConvertCommand.Create().InvokeAsync(args);
            return (exit, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldError);
            ConvertCommand.ConversionCompletedObserver.Value = oldCompleted;
            ConvertCommand.AdditionalValidationError.Value = oldValidation;
        }
    }
}
