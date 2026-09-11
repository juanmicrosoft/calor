using System.Text.Json;
using Calor.Compiler.Migration;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

public class ReportGeneratorTests
{
    [Fact]
    public void EvidenceComparison_JoinsOriginalInputsAndRetainsBothCandidateAndRecovery()
    {
        var baseline = CreatePassingReport();
        baseline.Evidence = new RunEvidence
        {
            Inputs = [new InputEvidence { FileId = "TestProject/Lib/Foo.cs", Path = "Lib/Foo.cs", Sha256 = "original" }],
        };
        baseline.FileResults[0].Candidate = new CandidateEvidence
        {
            FileId = "TestProject/Lib/Foo.cs", InputSha256 = "original", CandidateId = "before",
        };
        var candidate = CreatePassingReport();
        candidate.Evidence = new RunEvidence
        {
            DeclaredTestAttemptsPerLeg = 2,
            Inputs =
            [
                new InputEvidence { FileId = "TestProject/Lib/Foo.cs", Path = "Lib/Foo.cs", Sha256 = "original" },
                new InputEvidence { FileId = "TestProject/Lib/Extra.cs", Path = "Lib/Extra.cs", Sha256 = "new", Excluded = true },
            ],
        };
        candidate.FileResults[0].Candidate = new CandidateEvidence
        {
            FileId = "TestProject/Lib/Foo.cs", InputSha256 = "original", CandidateId = "after",
            Errors = ["candidate error retained"],
        };
        candidate.FileResults[0].Status = FileStatus.Reverted;
        candidate.FileResults[0].Recovery.Add(new RecoveryEvidence
        { Phase = "build-recovery", Attempt = 1, Outcome = "Reverted" });
        using var result = JsonDocument.Parse(ReportGenerator.CompareEvidence(
            ReportGenerator.GenerateJson(baseline), ReportGenerator.GenerateJson(candidate)));
        Assert.False(result.RootElement.GetProperty("same_retry_rule").GetBoolean());
        var rows = result.RootElement.GetProperty("rows");
        Assert.Equal(2, rows.GetArrayLength());
        var file = rows.EnumerateArray().Single(row => row.GetProperty("file_id").GetString() == "TestProject/Lib/Foo.cs");
        Assert.True(file.GetProperty("same_input").GetBoolean());
        Assert.Equal("before", file.GetProperty("baseline_file").GetProperty("candidate").GetProperty("CandidateId").GetString());
        Assert.Equal("after", file.GetProperty("candidate_file").GetProperty("candidate").GetProperty("CandidateId").GetString());
        Assert.Equal("candidate error retained", file.GetProperty("candidate_file").GetProperty("candidate").GetProperty("Errors")[0].GetString());
        Assert.Equal("Reverted", file.GetProperty("candidate_file").GetProperty("recovery")[0].GetProperty("Outcome").GetString());
    }

    [Fact]
    public void EvidenceComparison_RejectsLegacyOnlyReportsInsteadOfInventingProvenance()
    {
        var legacy = ReportGenerator.GenerateJson(CreatePassingReport());
        Assert.Throws<InvalidOperationException>(() => ReportGenerator.CompareEvidence(legacy, legacy));
    }

    [Fact]
    public void CandidateDiagnostics_DoNotGuessProducerFromSharedCode()
    {
        var diagnostic = new Calor.Compiler.Diagnostics.Diagnostic(
            "Calor0200", "missing symbol", new Calor.Compiler.Parsing.TextSpan(0, 3, 1, 1));
        var captured = ReportGenerator.CaptureDiagnostic(diagnostic, "Program.Compile", "Lib/X.cs", "abc\n");
        Assert.Null(captured.Producer);
        Assert.Null(captured.BindingDisposition);
        Assert.Equal(1, captured.Line);
        Assert.Equal(4, captured.EndColumn);
        var binder = new Calor.Compiler.Diagnostics.Diagnostic(
            diagnostic.Code, diagnostic.Message, diagnostic.Span)
        { BindingContext = Calor.Compiler.Binding.BindingDiagnosticContext.General };
        Assert.Equal("Binder", ReportGenerator.CaptureDiagnostic(
            binder, "Program.Compile", "Lib/X.cs", "abc\n").Producer);
    }

    [Fact]
    public void CandidateDiagnostics_KeepMultilineUtf16SpanAndAnalysisOnlyRouting()
    {
        const string source = "x\n😀\ny";
        var diagnostic = new Calor.Compiler.Diagnostics.Diagnostic(
            "Calor0273", "nullable", new Calor.Compiler.Parsing.TextSpan(2, 4, 2, 1))
        {
            BindingContext = new(
                Calor.Compiler.Binding.BindingReceivingBoundary.NativeReturn,
                Calor.Compiler.Binding.BindingReceivingShape.ScalarString),
        };
        var captured = ReportGenerator.CaptureDiagnostic(diagnostic, "shadow-bind", "N.cs", source, true);
        Assert.Equal("AnalysisOnly", captured.BindingDisposition);
        Assert.Equal("ScalarString", captured.BindingShape);
        Assert.Equal(3, captured.EndLine);
        Assert.Equal(2, captured.EndColumn);
        Assert.Equal(ReportGenerator.Hash(source), captured.SourceSha256);
    }

    [Theory]
    [InlineData("Lib/Bad.cs(4,8): error CS0246: missing type", 4, 8)]
    [InlineData("Lib/Bad.cs(4,8,4,14): error CS0246: missing type", 4, 8)]
    public void BuildDiagnostics_RetainObservedCoordinatesWithoutClaimingProducer(string line, int row, int column)
    {
        var diagnostic = Assert.Single(ReportGenerator.CaptureBuildDiagnostics([line], "build-recovery", "/work"));
        Assert.Equal("CS0246", diagnostic.Code);
        Assert.Equal("Lib/Bad.cs", diagnostic.Path);
        Assert.Equal(row, diagnostic.Line);
        Assert.Equal(column, diagnostic.Column);
        Assert.Null(diagnostic.Start);
        Assert.Null(diagnostic.Producer);
        Assert.Equal("MSBuildReportedLocation", diagnostic.SourceKind);
    }

    [Fact]
    public void EvidenceSerialization_KeepsCandidateRecoveryAndUnknownClassificationsDistinct()
    {
        var report = CreatePassingReport();
        report.Evidence = new RunEvidence
        {
            Inputs =
            [
                new InputEvidence { FileId = "TestProject/Lib/Foo.cs", Path = "Lib/Foo.cs", Sha256 = "input" },
                new InputEvidence { FileId = "TestProject/Lib/Skip.g.cs", Path = "Lib/Skip.g.cs", Excluded = true },
            ],
            DeclaredTestAttemptsPerLeg = 2,
            TestAttempts =
            [
                new TestAttemptEvidence { Leg = "baseline", Attempt = 1, Result = new TestRunResult { ExitCode = -1 } },
                new TestAttemptEvidence { Leg = "baseline", Attempt = 2, Result = new TestRunResult { ExitCode = 0 } },
            ],
        };
        var file = report.FileResults[0];
        file.Candidate = new CandidateEvidence
        {
            FileId = "TestProject/Lib/Foo.cs", InputSha256 = "input", CandidateId = "candidate",
            Attempted = true, CompilationOutcome = "Accepted", StatusBeforeRecovery = FileStatus.Replaced,
            Diagnostics = [ReportGenerator.CaptureDiagnostic(new Calor.Compiler.Diagnostics.Diagnostic(
                "Calor0200", "warning", Calor.Compiler.Parsing.TextSpan.Empty,
                Calor.Compiler.Diagnostics.DiagnosticSeverity.Warning), "Program.Compile", file.FilePath, "")],
        };
        file.Status = FileStatus.Reverted;
        file.Errors = ["later recovery"];
        file.Recovery.Add(new RecoveryEvidence
        {
            Phase = "build-recovery", Attempt = 1, Outcome = "Reverted",
            Diagnostics = ReportGenerator.CaptureBuildDiagnostics(
                ["Lib/Foo.cs(2,3): error CS0246: type"], "build-recovery", "/work"),
        });
        using var json = JsonDocument.Parse(ReportGenerator.GenerateJson(report));
        var detail = json.RootElement.GetProperty("file_detail").EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == file.FilePath);
        Assert.Equal("Replaced", detail.GetProperty("candidate").GetProperty("StatusBeforeRecovery").GetString());
        Assert.Equal("Calor0200", detail.GetProperty("candidate").GetProperty("Diagnostics")[0].GetProperty("Code").GetString());
        Assert.Equal("CS0246", detail.GetProperty("recovery")[0].GetProperty("Diagnostics")[0].GetProperty("Code").GetString());
        Assert.Equal(-1, json.RootElement.GetProperty("evidence").GetProperty("TestAttempts")[0]
            .GetProperty("Result").GetProperty("ExitCode").GetInt32());
        using var classifications = JsonDocument.Parse(ReportGenerator.GenerateClassificationTemplate(report));
        Assert.All(classifications.RootElement.GetProperty("entries").EnumerateArray(), entry =>
        {
            Assert.Equal("unresolved", entry.GetProperty("classification").GetString());
            Assert.False(entry.TryGetProperty("reviewer", out _));
        });
        Assert.Contains(classifications.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("path").GetString() == "Lib/Skip.g.cs");
    }

    [Fact]
    public void ConversionOptionCapture_UsesActualParseSettingsWithoutSerializingRoslynObjectGraph()
    {
        var options = RoundTripPipeline.CreateHarnessConversionOptions(new RoundTripConfig
        {
            ProjectName = "Probe", OriginalProjectPath = ".", LibrarySourceRelativePath = ".",
            SolutionOrProjectFile = "none.csproj",
        }, null);
        var captured = ReportGenerator.SnapshotOptions(options);
        Assert.Equal("Preview", captured["ParseOptions"].GetProperty("SpecifiedLanguageVersion").GetString());
        Assert.True(captured["PassthroughOnError"].GetBoolean());
        Assert.False(captured["ValidateRoundTripCSharp"].GetBoolean());
    }

    [Fact]
    public void GenerateMarkdown_PassVerdict_ContainsPassText()
    {
        var report = CreatePassingReport();
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("PASS", md);
        Assert.Contains("0 regressions", md);
        Assert.Contains("## Pipeline Summary", md);
        Assert.Contains("## File-by-File Results", md);
    }

    [Fact]
    public void GenerateMarkdown_BuildFailed_ShowsBuildErrors()
    {
        var report = CreateBuildFailedReport();
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("build failed", md);
        Assert.Contains("## Build Errors", md);
        Assert.Contains("CS0246", md);
    }

    [Fact]
    public void GenerateMarkdown_WithRegressions_ShowsRegressionDetails()
    {
        var report = CreateRegressionReport();
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("## Regressions", md);
        Assert.Contains("FailingTest", md);
        Assert.Contains("Expected 5 but got 6", md);
    }

    [Fact]
    public void GenerateJson_ProducesValidJson()
    {
        var report = CreatePassingReport();
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("TestProject", root.GetProperty("project").GetString());
        Assert.Equal("pass", root.GetProperty("verdict").GetString());
        Assert.Equal(0, root.GetProperty("regressions").GetInt32());
        Assert.True(root.GetProperty("build_succeeded").GetBoolean());
        Assert.Equal(10, root.GetProperty("baseline").GetProperty("passed").GetInt32());
        Assert.Equal(10, root.GetProperty("round_trip").GetProperty("passed").GetInt32());
    }

    [Fact]
    public void GenerateJson_BuildFailed_HasCorrectVerdict()
    {
        var report = CreateBuildFailedReport();
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        Assert.Equal("fail", doc.RootElement.GetProperty("verdict").GetString());
        Assert.False(doc.RootElement.GetProperty("build_succeeded").GetBoolean());
    }

    [Fact]
    public void GenerateJson_FileCounts_AreAccurate()
    {
        var report = CreatePassingReport();
        report.ExcludedFileCount = 2;
        report.Fidelity = ProjectFidelity.Compute(report);
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        var files = doc.RootElement.GetProperty("files");

        Assert.Equal(5, files.GetProperty("total").GetInt32());
        Assert.Equal(2, files.GetProperty("replaced").GetInt32());
        Assert.Equal(1, files.GetProperty("compile_error").GetInt32());
        Assert.Equal(2, files.GetProperty("excluded_by_pattern").GetInt32());
        Assert.Contains("2/5 (40.0%)", ReportGenerator.GenerateMarkdown(report));
    }

    [Fact]
    public void GenerateJson_IncludesFidelityDimensions()
    {
        var report = CreatePassingReport();
        report.FileResults.Add(new FileConversionResult
        {
            FilePath = "Lib/Rev.cs",
            Status = FileStatus.Reverted,
            RevertReason = "build-recovery round 1",
        });
        report.FileResults[0].ApplyLossLedger(
        [
            new()
            {
                Kind = ConversionLossKind.InteropPreserved,
                Feature = "record",
                Description = "preserved as interop"
            },
            new()
            {
                Kind = ConversionLossKind.Dropped,
                Feature = "checked-expression",
                Description = "overflow semantics dropped"
            },
        ]);
        report.FileResults[0].PreprocessorMode =
            nameof(PreprocessorConversionMode.SelectActiveBranchLossy);
        report.FileResults[0].Configuration = "Debug";
        report.FileResults[0].TargetFramework = "net8.0";
        report.FileResults[0].LanguageVersion = "CSharp14";
        report.FileResults[0].DefinedSymbols = ["FEATURE"];
        report.Fidelity = ProjectFidelity.Compute(report);
        var json = ReportGenerator.GenerateJson(report);

        var doc = JsonDocument.Parse(json);
        var fidelity = doc.RootElement.GetProperty("fidelity");

        var coverage = fidelity.GetProperty("coverage");
        Assert.Equal(4, coverage.GetProperty("total_convertible_files").GetInt32());
        Assert.Equal(1, coverage.GetProperty("reverted").GetInt32());
        Assert.Equal(0.5, coverage.GetProperty("coverage_fraction").GetDouble());
        Assert.Equal(1, coverage.GetProperty("total_interop_blocks").GetInt32());
        Assert.Equal(
            "checked-expression",
            coverage.GetProperty("distinct_gaps")[0].GetString());

        var build = fidelity.GetProperty("build");
        Assert.True(build.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, build.GetProperty("recovery_reverted_files").GetInt32());

        var tests = fidelity.GetProperty("tests");
        Assert.Equal(10, tests.GetProperty("baseline_total").GetInt32());
        Assert.Equal(0, tests.GetProperty("inventory_delta").GetInt32());
        Assert.Equal("Pass", tests.GetProperty("comparison_status").GetString());

        // Per-file detail carries revert visibility
        var detail = doc.RootElement.GetProperty("file_detail");
        var reverted = detail.EnumerateArray().Single(e => e.GetProperty("status").GetString() == "Reverted");
        Assert.Equal("build-recovery round 1", reverted.GetProperty("revert_reason").GetString());
        var first = detail.EnumerateArray().Single(e =>
            e.GetProperty("path").GetString() == "Lib/Foo.cs");
        Assert.Equal(
            "SelectActiveBranchLossy",
            first.GetProperty("preprocessor_mode").GetString());
        Assert.Equal("Debug", first.GetProperty("configuration").GetString());
        Assert.Equal("net8.0", first.GetProperty("target_framework").GetString());
        Assert.Equal("FEATURE", first.GetProperty("defined_symbols")[0].GetString());
        Assert.Contains(
            first.GetProperty("losses").EnumerateArray(),
            loss => loss.GetProperty("Feature").GetString() == "record");
    }

    [Fact]
    public void GenerateMarkdown_IncludesFidelitySection_AndRevertedRow()
    {
        var report = CreatePassingReport();
        report.FileResults.Add(new FileConversionResult
        {
            FilePath = "Lib/Rev.cs",
            Status = FileStatus.Reverted,
            RevertReason = "build-recovery round 1",
            Errors = ["Reverted: build error in round-tripped output (recovery round 1)"],
        });
        report.Fidelity = ProjectFidelity.Compute(report);
        var md = ReportGenerator.GenerateMarkdown(report);

        Assert.Contains("## Fidelity (separated verdict dimensions)", md);
        Assert.Contains("### Conversion Coverage", md);
        Assert.Contains("### Build Outcome", md);
        Assert.Contains("### Test Outcome", md);
        Assert.Contains("REVERTED", md);
    }

    [Fact]
    public void InconclusiveRun_EmitsNoCoverageFraction()
    {
        // A run whose recovery build failed unattributably (timeout) must NOT emit a
        // coverage/native fraction — it would be spuriously inflated (M2 guard).
        var report = CreateInconclusiveReport();

        var json = ReportGenerator.GenerateJson(report);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("inconclusive", root.GetProperty("verdict").GetString());
        Assert.True(root.GetProperty("inconclusive").GetBoolean());
        // fidelity is nulled → serialized as absent-or-null (WhenWritingNull), never an
        // object with fractions. No coverage_fraction / native_fraction anywhere.
        Assert.False(root.TryGetProperty("fidelity", out var fid) && fid.ValueKind != JsonValueKind.Null,
            "fidelity must be absent or null for an inconclusive run");
        Assert.DoesNotContain("native_fraction", json);
        Assert.DoesNotContain("coverage_fraction", json);

        var md = ReportGenerator.GenerateMarkdown(report);
        Assert.Contains("INCONCLUSIVE", md);
        Assert.Contains("No coverage fraction is reported", md);
    }

    private static RoundTripReport CreateInconclusiveReport()
    {
        var report = new RoundTripReport
        {
            ProjectName = "TimeoutProject",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            FinishedAt = DateTimeOffset.UtcNow,
            // Files that WOULD produce a high native fraction if trusted.
            FileResults =
            [
                new() { FilePath = "Lib/A.cs", Status = FileStatus.Replaced },
                new() { FilePath = "Lib/B.cs", Status = FileStatus.Replaced },
                new() { FilePath = "Lib/C.cs", Status = FileStatus.Replaced },
            ],
            // Build failed with NO extractable error files (timeout signature).
            BuildResult = new BuildResult { Succeeded = false, ExitCode = -1, Errors = [] },
            Inconclusive = true,
            InconclusiveReason = "recovery build did not complete within the build timeout",
        };
        report.Comparison = new TestComparison { Status = ComparisonStatus.BuildFailed };
        report.Fidelity = ProjectFidelity.Compute(report);
        return report;
    }

    private static RoundTripReport CreatePassingReport()
    {
        var report = new RoundTripReport
        {
            ProjectName = "TestProject",
            CalorVersion = "0.2.9",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            FinishedAt = DateTimeOffset.UtcNow,
            BaselineBuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            Baseline = new TestRunResult
            {
                ExitCode = 0, TotalTests = 10, Passed = 10, Failed = 0, Skipped = 0,
                TrxFiles = ["baseline.trx"],
                Results = Enumerable.Range(1, 10).Select(i => new TestResult
                {
                    TestName = $"Test{i}", Outcome = "Passed"
                }).ToList(),
            },
            FileResults =
            [
                new() { FilePath = "Lib/Foo.cs", Status = FileStatus.Replaced, ConversionRate = 100 },
                new() { FilePath = "Lib/Bar.cs", Status = FileStatus.Replaced, ConversionRate = 95 },
                new() { FilePath = "Lib/Baz.cs", Status = FileStatus.CompileError, ConversionRate = 80, Errors = ["Parse error"] },
            ],
            BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
            RoundTripTests = new TestRunResult
            {
                ExitCode = 0, TotalTests = 10, Passed = 10, Failed = 0, Skipped = 0,
                TrxFiles = ["roundtrip.trx"],
                Results = Enumerable.Range(1, 10).Select(i => new TestResult
                {
                    TestName = $"Test{i}", Outcome = "Passed"
                }).ToList(),
            },
            Comparison = new TestComparison
            {
                Status = ComparisonStatus.Pass,
                BaselineTotal = 10, BaselinePassed = 10,
                RoundTripTotal = 10, RoundTripPassed = 10,
            },
        };
        report.Fidelity = ProjectFidelity.Compute(report);
        return report;
    }

    private static RoundTripReport CreateBuildFailedReport() => new()
    {
        ProjectName = "FailProject",
        CalorVersion = "0.2.9",
        StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
        FinishedAt = DateTimeOffset.UtcNow,
        Baseline = new TestRunResult { ExitCode = 0, TotalTests = 20, Passed = 20 },
        FileResults =
        [
            new() { FilePath = "Lib/X.cs", Status = FileStatus.Replaced, ConversionRate = 100 },
        ],
        BuildResult = new BuildResult
        {
            Succeeded = false, ExitCode = 1,
            Errors = ["X.cs(10,5): error CS0246: Type not found"],
        },
        Comparison = new TestComparison { Status = ComparisonStatus.BuildFailed },
    };

    private static RoundTripReport CreateRegressionReport() => new()
    {
        ProjectName = "RegProject",
        CalorVersion = "0.2.9",
        StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
        FinishedAt = DateTimeOffset.UtcNow,
        Baseline = new TestRunResult
        {
            ExitCode = 0, TotalTests = 5, Passed = 5,
            Results = Enumerable.Range(1, 5).Select(i => new TestResult
            {
                TestName = $"Test{i}", Outcome = "Passed"
            }).ToList(),
        },
        FileResults = [new() { FilePath = "Lib/A.cs", Status = FileStatus.Replaced, ConversionRate = 100 }],
        BuildResult = new BuildResult { Succeeded = true, ExitCode = 0 },
        RoundTripTests = new TestRunResult
        {
            ExitCode = 1, TotalTests = 5, Passed = 4, Failed = 1,
            Results =
            [
                new() { TestName = "Test1", Outcome = "Passed" },
                new() { TestName = "Test2", Outcome = "Passed" },
                new() { TestName = "Test3", Outcome = "Passed" },
                new() { TestName = "Test4", Outcome = "Passed" },
                new() { TestName = "FailingTest", Outcome = "Failed", ErrorMessage = "Expected 5 but got 6" },
            ],
        },
        Comparison = new TestComparison
        {
            Status = ComparisonStatus.MinorRegressions,
            BaselineTotal = 5, BaselinePassed = 5,
            RoundTripTotal = 5, RoundTripPassed = 4,
            Regressions = [new() { TestName = "FailingTest", Outcome = "Failed", ErrorMessage = "Expected 5 but got 6" }],
        },
    };
}
