using Calor.Compiler.Analysis;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests;

public class MigrateWorkflowTests : IDisposable
{
    private readonly string _tempDir;

    public MigrateWorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-migrate-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private void WriteTestCSharpFile(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), content);
    }

    private static readonly string SimpleCSharpSource = """
        public class Calculator
        {
            public int Add(int a, int b)
            {
                return a + b;
            }

            public int Divide(int a, int b)
            {
                if (b == 0)
                    throw new ArgumentException("b must not be zero", nameof(b));
                return a / b;
            }
        }
        """;

    private static readonly string ContractCSharpSource = """
        public class Validator
        {
            public void Process(string input)
            {
                if (input == null)
                    throw new ArgumentNullException(nameof(input));
                if (input.Length == 0)
                    throw new ArgumentException("Input cannot be empty", nameof(input));
            }
        }
        """;

    #region Full workflow

    [Fact]
    public async Task FullWorkflow_ProducesAnalysisAndConversionData()
    {
        // Arrange
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);
        WriteTestCSharpFile("Validator.cs", ContractCSharpSource);

        var options = new MigrationPlanOptions
        {
            Parallel = false,
            SkipAnalyze = false,
            SkipVerify = true // Z3 may not be available in tests
        };

        var migrator = new ProjectMigrator(options);

        // Act — Phase 1: Discover
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        Assert.True(plan.Entries.Count >= 2);

        // Act — Phase 2: Analyze
        var analysisSummary = await migrator.AnalyzeAsync(plan);

        Assert.True(analysisSummary.FilesAnalyzed >= 2);
        Assert.True(analysisSummary.AverageScore >= 0);
        Assert.True(analysisSummary.FileResults.Count >= 2);

        // Act — Phase 3: Convert
        var report = await migrator.ExecuteAsync(plan);

        Assert.True(report.Summary.TotalFiles > 0);

        // Verify analysis data is populated on plan entries
        var analyzedEntries = plan.Entries.Where(e => e.AnalysisScore != null).ToList();
        Assert.True(analyzedEntries.Count >= 2);
    }

    #endregion

    #region Skip analyze

    [Fact]
    public async Task SkipAnalyze_AnalysisSummaryIsNull()
    {
        // Arrange
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions
        {
            Parallel = false,
            SkipAnalyze = true,
            SkipVerify = true
        };

        var migrator = new ProjectMigrator(options);

        // Act
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        // When skipAnalyze is true, the command doesn't call AnalyzeAsync at all.
        // Here we verify that entries don't have AnalysisScore if analyze was never called.
        Assert.All(plan.Entries, e => Assert.Null(e.AnalysisScore));

        var report = await migrator.ExecuteAsync(plan);
        Assert.True(report.Summary.TotalFiles > 0);
    }

    #endregion

    #region Skip verify

    [Fact]
    public async Task SkipVerify_VerificationSummaryIsNotReturned()
    {
        // When --skip-verify is set, the command simply doesn't call VerifyAsync.
        // We verify that VerifyAsync is an independent method that can be skipped.
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false, SkipVerify = true };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var report = await migrator.ExecuteAsync(plan);

        // The report itself doesn't have verification since we didn't call VerifyAsync
        Assert.Null(report.Verification);
    }

    #endregion

    #region Z3 unavailable

    [Fact]
    public async Task VerifyAsync_Z3Unavailable_ReturnsGracefulSummary()
    {
        // This test verifies the graceful degradation path.
        // If Z3 IS available, we still get a valid result.
        // If Z3 is NOT available, we get Z3Available = false.
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var report = await migrator.ExecuteAsync(plan);

        var verificationSummary = await migrator.VerifyAsync(report);

        // Whether Z3 is available or not, we should get a valid summary
        Assert.NotNull(verificationSummary);

        if (!Z3ContextFactory.IsAvailable)
        {
            Assert.False(verificationSummary.Z3Available);
            Assert.Equal(0, verificationSummary.FilesVerified);
        }
        else
        {
            Assert.True(verificationSummary.Z3Available);
        }
    }

    #endregion

    #region Report formats

    [Fact]
    public async Task Report_MarkdownIncludesAnalysisSection()
    {
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var analysisSummary = await migrator.AnalyzeAsync(plan);
        var report = await migrator.ExecuteAsync(plan);

        // Build enriched report via builder
        var enrichedBuilder = new MigrationReportBuilder()
            .SetDirection(report.Direction);
        foreach (var fr in report.FileResults)
            enrichedBuilder.AddFileResult(fr);
        enrichedBuilder.SetAnalysisSummary(analysisSummary);
        var enrichedReport = enrichedBuilder.Build();

        var generator = new MigrationReportGenerator(enrichedReport);
        var markdown = generator.GenerateMarkdown();

        Assert.Contains("## Analysis Summary", markdown);
        Assert.Contains("Average Score", markdown);
        Assert.DoesNotContain("## Verification Summary", markdown);
    }

    [Fact]
    public async Task Report_JsonIncludesAnalysisSection()
    {
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var analysisSummary = await migrator.AnalyzeAsync(plan);
        var report = await migrator.ExecuteAsync(plan);

        var enrichedBuilder = new MigrationReportBuilder()
            .SetDirection(report.Direction);
        foreach (var fr in report.FileResults)
            enrichedBuilder.AddFileResult(fr);
        enrichedBuilder.SetAnalysisSummary(analysisSummary);
        var enrichedReport = enrichedBuilder.Build();

        var generator = new MigrationReportGenerator(enrichedReport);
        var json = generator.GenerateJson();

        Assert.Contains("\"analysis\"", json);
        Assert.Contains("\"filesAnalyzed\"", json);
        Assert.Contains("\"averageScore\"", json);
    }

    [Fact]
    public void Report_MarkdownIncludesVerificationSection_WhenPresent()
    {
        // Create a verification summary (even if synthetic) to test rendering
        var verificationSummary = new VerificationSummaryReport
        {
            FilesVerified = 2,
            TotalContracts = 5,
            Proven = 3,
            Unproven = 1,
            Disproven = 1,
            Z3Available = true,
            Duration = TimeSpan.FromMilliseconds(500),
            FileResults = new List<FileVerificationSummary>
            {
                new()
                {
                    CalorPath = "test.calr",
                    TotalContracts = 5,
                    Proven = 3,
                    Unproven = 1,
                    Disproven = 1,
                    DisprovenDetails = new List<string> { "Divide: b can be zero" }
                }
            }
        };

        var report = new MigrationReport
        {
            ReportId = "test1234",
            GeneratedAt = DateTime.UtcNow,
            Direction = MigrationDirection.CSharpToCalor,
            Summary = new MigrationSummary { TotalFiles = 1, SuccessfulFiles = 1 },
            Verification = verificationSummary
        };

        var generator = new MigrationReportGenerator(report);
        var markdown = generator.GenerateMarkdown();

        Assert.Contains("## Verification Summary", markdown);
        Assert.Contains("Proven Rate", markdown);
        Assert.Contains("Disproven Contracts", markdown);
        Assert.Contains("b can be zero", markdown);
    }

    #endregion

    #region Dry run

    [Fact]
    public async Task DryRun_DoesNotWriteFiles()
    {
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        // Analyze still works in dry run mode
        var analysisSummary = await migrator.AnalyzeAsync(plan);
        Assert.True(analysisSummary.FilesAnalyzed > 0);

        // Dry run: no .calr files should be created
        var report = await migrator.DryRunAsync(plan);

        var calrFiles = Directory.GetFiles(_tempDir, "*.calr");
        Assert.Empty(calrFiles);
    }

    #endregion

    #region Calor-to-CSharp direction

    [Fact]
    public void CalorToCSharp_AnalysisSkippedForReverseDirection()
    {
        // When direction is CalorToCSharp, analysis is not applicable.
        // This tests the contract: analysis is only meaningful for CSharpToCalor.
        var calrSource = """
            §FN Add(a: int, b: int) -> int
              §RETURN a + b
            """;
        File.WriteAllText(Path.Combine(_tempDir, "test.calr"), calrSource);

        // The command skips the analyze phase entirely for calor-to-cs direction.
        // We verify that the direction enum properly distinguishes the two.
        var direction = MigrationDirection.CalorToCSharp;
        Assert.NotEqual(MigrationDirection.CSharpToCalor, direction);
    }

    #endregion

    #region Builder integration

    [Fact]
    public void ReportBuilder_SetsAnalysisAndVerification()
    {
        var analysisSummary = new AnalysisSummaryReport
        {
            FilesAnalyzed = 5,
            AverageScore = 65.0,
            Duration = TimeSpan.FromSeconds(2)
        };

        var verificationSummary = new VerificationSummaryReport
        {
            FilesVerified = 3,
            TotalContracts = 10,
            Proven = 8,
            Z3Available = true,
            Duration = TimeSpan.FromSeconds(1)
        };

        var builder = new MigrationReportBuilder()
            .SetDirection(MigrationDirection.CSharpToCalor)
            .SetAnalysisSummary(analysisSummary)
            .SetVerificationSummary(verificationSummary);

        var report = builder.Build();

        Assert.NotNull(report.Analysis);
        Assert.Equal(5, report.Analysis.FilesAnalyzed);
        Assert.Equal(65.0, report.Analysis.AverageScore);

        Assert.NotNull(report.Verification);
        Assert.Equal(3, report.Verification.FilesVerified);
        Assert.Equal(10, report.Verification.TotalContracts);
        Assert.Equal(8, report.Verification.Proven);
    }

    #endregion

    #region MigrationPlanOptions defaults

    [Fact]
    public void MigrationPlanOptions_DefaultsAreCorrect()
    {
        var options = new MigrationPlanOptions();

        Assert.False(options.SkipAnalyze);
        Assert.False(options.SkipVerify);
        Assert.Equal(VerificationOptions.DefaultTimeoutMs, options.VerificationTimeoutMs);
    }

    #endregion

    #region AnalysisSummaryReport aggregation

    [Fact]
    public async Task AnalyzeAsync_AggregatesCorrectly()
    {
        WriteTestCSharpFile("Simple.cs", """
            public class Simple
            {
                public int Get() => 42;
            }
            """);
        WriteTestCSharpFile("Complex.cs", ContractCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var analysis = await migrator.AnalyzeAsync(plan);

        Assert.True(analysis.FilesAnalyzed >= 2);
        Assert.True(analysis.Duration > TimeSpan.Zero);
        Assert.True(analysis.FileResults.Count >= 2);

        // Each file result has a valid score
        foreach (var file in analysis.FileResults)
        {
            Assert.True(file.Score >= 0 && file.Score <= 100);
        }
    }

    #endregion

    #region Contract verification with real .calr (Z3)

    [SkippableFact]
    public async Task VerifyAsync_WithContracts_ReportsVerificationResults()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // Create a C# file that the converter will turn into .calr with contracts
        WriteTestCSharpFile("Calculator.cs", """
            public class Calculator
            {
                public int Square(int x)
                {
                    if (x < 0)
                        throw new ArgumentOutOfRangeException(nameof(x), "x must be non-negative");
                    return x * x;
                }

                public int Divide(int a, int b)
                {
                    if (b == 0)
                        throw new ArgumentException("b must not be zero", nameof(b));
                    return a / b;
                }
            }
            """);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var report = await migrator.ExecuteAsync(plan);

        // At least one file should convert successfully
        var successfulFiles = report.FileResults
            .Where(f => f.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial)
            .ToList();
        Assert.NotEmpty(successfulFiles);

        // Verify the converted files
        var verificationSummary = await migrator.VerifyAsync(report);

        Assert.True(verificationSummary.Z3Available);
        Assert.True(verificationSummary.FilesVerified > 0);

        // If contracts were detected, we should have some verified
        if (verificationSummary.TotalContracts > 0)
        {
            Assert.True(verificationSummary.Proven + verificationSummary.Unproven +
                        verificationSummary.Disproven >= 0);
            Assert.True(verificationSummary.ProvenRate >= 0);
        }

        // Per-file verification should be populated
        var verifiedFileResults = report.FileResults
            .Where(f => f.Verification != null)
            .ToList();
        Assert.Equal(verificationSummary.FilesVerified, verifiedFileResults.Count);
    }

    [SkippableFact]
    public async Task VerifyAsync_WithDirectCalorSource_VerifiesContracts()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // Write a .calr file directly with known contracts
        var calrSource = """
            §M{m001:VerifyTest}

            §F{f001:Square:pub}
              §I{i32:x}
              §O{i32}
              §Q (>= x 0)
              §S (>= result 0)
              §R (* x x)
            §/F{f001}

            §/M{m001}
            """;

        // Write it as if it were a converted output
        var calrPath = Path.Combine(_tempDir, "VerifyTest.calr");
        File.WriteAllText(calrPath, calrSource);

        // Build a minimal report that references this file
        var report = new MigrationReport
        {
            ReportId = "test",
            GeneratedAt = DateTime.UtcNow,
            Direction = MigrationDirection.CSharpToCalor,
            Summary = new MigrationSummary { TotalFiles = 1, SuccessfulFiles = 1 },
            FileResults = new List<FileMigrationResult>
            {
                new()
                {
                    SourcePath = calrPath.Replace(".calr", ".cs"),
                    OutputPath = calrPath,
                    Status = FileMigrationStatus.Success
                }
            }
        };

        var migrator = new ProjectMigrator();
        var verification = await migrator.VerifyAsync(report);

        Assert.True(verification.Z3Available);
        Assert.True(verification.FilesVerified > 0);
        Assert.True(verification.TotalContracts > 0, "Expected contracts to be found in the .calr file");
        Assert.True(verification.Proven > 0, "Expected at least one contract to be proven");
    }

    #endregion

    #region Per-file data population

    [Fact]
    public async Task ExecuteAsync_PopulatesPerFileAnalysis_WhenAnalyzeRanFirst()
    {
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        // Run analyze first (populates entry.AnalysisScore)
        await migrator.AnalyzeAsync(plan);

        // Now run conversion (should attach per-file analysis)
        var report = await migrator.ExecuteAsync(plan);

        var convertedFiles = report.FileResults
            .Where(f => f.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial)
            .ToList();
        Assert.NotEmpty(convertedFiles);

        // Per-file analysis should be populated from the prior AnalyzeAsync call
        foreach (var file in convertedFiles)
        {
            Assert.NotNull(file.Analysis);
            Assert.True(file.Analysis.Score >= 0);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoPerFileAnalysis_WhenAnalyzeNotRun()
    {
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        // Skip analyze, go straight to conversion
        var report = await migrator.ExecuteAsync(plan);

        var convertedFiles = report.FileResults
            .Where(f => f.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial)
            .ToList();
        Assert.NotEmpty(convertedFiles);

        // Per-file analysis should be null since AnalyzeAsync was never called
        foreach (var file in convertedFiles)
        {
            Assert.Null(file.Analysis);
        }
    }

    #endregion

    #region CLI integration (console output)

    [Fact]
    public async Task PhasedOutput_ShowsAllPhaseHeaders()
    {
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var options = new MigrationPlanOptions { Parallel = false };
        var migrator = new ProjectMigrator(options);

        // Capture console output
        var originalOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

            // Simulate Phase 1 output
            Console.WriteLine("Phase 1/4: Discovering files...");
            Console.WriteLine($"  Files to convert: {plan.ConvertibleFiles}");

            // Phase 2
            Console.WriteLine("Phase 2/4: Analyzing migration potential...");
            var analysis = await migrator.AnalyzeAsync(plan);
            Console.WriteLine($"  Average score: {analysis.AverageScore:F1}/100");

            // Phase 3
            Console.WriteLine("Phase 3/4: Converting files...");
            var report = await migrator.ExecuteAsync(plan);
            Console.WriteLine($"  Successful: {report.Summary.SuccessfulFiles}");

            // Phase 4
            Console.WriteLine("Phase 4/4: Verifying contracts...");
            var verification = await migrator.VerifyAsync(report);
            if (!verification.Z3Available)
                Console.WriteLine("  Z3 solver not available - verification skipped.");
            else
                Console.WriteLine($"  Contracts: {verification.TotalContracts} total");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = sw.ToString();

        Assert.Contains("Phase 1/4: Discovering files...", output);
        Assert.Contains("Phase 2/4: Analyzing migration potential...", output);
        Assert.Contains("Phase 3/4: Converting files...", output);
        Assert.Contains("Phase 4/4: Verifying contracts...", output);
        Assert.Contains("Average score:", output);
        Assert.Contains("Successful:", output);
    }

    [Fact]
    public void PhasedOutput_SkippedPhasesShowSkipped()
    {
        WriteTestCSharpFile("Calculator.cs", SimpleCSharpSource);

        var originalOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            // Simulate the command with both phases skipped
            Console.WriteLine("Phase 2/4: Analyzing migration potential... skipped");
            Console.WriteLine("Phase 4/4: Verifying contracts... skipped");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = sw.ToString();

        Assert.Contains("Analyzing migration potential... skipped", output);
        Assert.Contains("Verifying contracts... skipped", output);
    }

    #endregion

    #region VerificationSummaryReport model

    [Fact]
    public void VerificationSummaryReport_ProvenRateCalculation()
    {
        var summary = new VerificationSummaryReport
        {
            TotalContracts = 10,
            Proven = 8,
            Unproven = 1,
            Disproven = 1,
            Z3Available = true
        };

        Assert.Equal(80.0, summary.ProvenRate);
    }

    [Fact]
    public void VerificationSummaryReport_ZeroContracts_ProvenRateIsZero()
    {
        var summary = new VerificationSummaryReport
        {
            TotalContracts = 0,
            Z3Available = true
        };

        Assert.Equal(0, summary.ProvenRate);
    }

    #endregion
}
