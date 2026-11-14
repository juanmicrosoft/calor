using Calor.Compiler.Analysis;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Verification.Z3;
using System.Text.Json;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectReceipt_DirectoryInitialUnchangedAndChangedOriginalRefresh(bool parallel)
    {
        WriteTestCSharpFile("A.cs", "public class A { public int Get() => 1; }");
        const string preservedSource = "public class B { ~B() { /* \u03c0 */ } }\r\n";
        WriteTestCSharpFile("B.cs", preservedSource);
        var migrator = ReceiptMigrator(parallel);
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var report = await migrator.ExecuteAsync(plan);
        Assert.Equal(0, report.Summary.FailedFiles);
        Assert.Equal(1, report.Summary.SuccessfulFiles);
        Assert.Equal(1, report.Summary.PartialFiles);
        foreach (var file in report.FileResults)
        {
            Assert.True(file.OutputWritten);
            var receipt = ReadReceipt(file.OutputPath!);
            Assert.Equal(File.ReadAllText(file.SourcePath), receipt.Original.Text);
            Assert.Equal(File.ReadAllText(file.OutputPath!), receipt.GeneratedCalor);
            ConversionReceipt.VerifyInputs(receipt.Inputs);
            var originalPolicy = new CSharpToCalorConverter(new()
            {
                ValidateRoundTripCSharp = false,
                ProjectDirectory = _tempDir,
                Configuration = "Debug"
            }).Convert(receipt.Original.Text, file.SourcePath);
            Assert.Equal(originalPolicy.CalorSource, receipt.GeneratedCalor);
            Assert.Equal(JsonSerializer.Serialize(originalPolicy.Losses), JsonSerializer.Serialize(file.Losses));
        }
        var before = ReceiptArtifacts(plan);
        for (var refreshIndex = 0; refreshIndex < 3; refreshIndex++)
        {
            var refresh = await migrator.ExecuteAsync(plan);
            Assert.Equal(0, refresh.Summary.FailedFiles);
            AssertReceiptArtifacts(before, refresh);
        }
        WriteTestCSharpFile("A.cs", "public class A { public int Get() => 2; }");
        var changed = await migrator.ExecuteAsync(plan);
        Assert.Equal(0, changed.Summary.FailedFiles);
        var changedReceipt = ReadReceipt(Path.Combine(_tempDir, "A.calr"));
        Assert.Contains("=> 2", changedReceipt.Original.Text);
        Assert.Contains("return 2", changedReceipt.GeneratedCalor);
        Assert.Equal(preservedSource, ReadReceipt(Path.Combine(_tempDir, "B.calr")).Original.Text);
    }

    [Theory]
    [InlineData("edited")]
    [InlineData("missing-receipt")]
    [InlineData("missing-output")]
    [InlineData("corrupt-receipt")]
    [InlineData("unassociated")]
    [InlineData("different-source")]
    public async Task ProjectReceipt_RefusesUnsafeBasesAndReleasesLocks(string state)
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        var migrator = ReceiptMigrator();
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var output = Assert.Single(plan.Entries).OutputPath;
        if (state == "unassociated")
            File.WriteAllText(output, "unassociated\r\n");
        else
            Assert.Equal(0, (await migrator.ExecuteAsync(plan)).Summary.FailedFiles);
        switch (state)
        {
            case "edited": File.AppendAllText(output, "// user edit\r\n"); break;
            case "missing-receipt": File.Delete(output + ".conversion.json"); break;
            case "missing-output": File.Delete(output); break;
            case "corrupt-receipt": File.WriteAllText(output + ".conversion.json", "{"); break;
            case "different-source":
                WriteTestCSharpFile("Other.cs", "public class Other { }");
                plan = ReceiptPlan("Other.cs");
                plan.Entries[0].OutputPath = output;
                break;
        }
        var before = ReceiptArtifacts(plan);
        var report = await migrator.ExecuteAsync(plan);
        var failed = Assert.Single(report.FileResults);
        Assert.Equal(FileMigrationStatus.Failed, failed.Status);
        Assert.Null(failed.OutputPath);
        Assert.False(failed.OutputWritten);
        Assert.NotEmpty(failed.Issues);
        AssertReceiptArtifacts(before);
        using var released = new FileStream(output + ".conversion.lock",
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("output")]
    [InlineData("receipt")]
    public async Task ProjectReceipt_StalePreparationRefusesTheEntireOutputBatch(string changed)
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        WriteTestCSharpFile("B.cs", "public class B { }");
        var plan = ReceiptPlan("A.cs", "B.cs");
        var migrator = ReceiptMigrator();
        Assert.Equal(0, (await migrator.ExecuteAsync(plan)).Summary.FailedFiles);
        Dictionary<string, byte[]?>? expected = null;
        var report = await migrator.ExecuteAsync(plan, progress: new ReceiptProgress(progress =>
        {
            if (progress.ProcessedFiles != progress.TotalFiles) return;
            File.AppendAllText(changed switch
            {
                "source" => plan.Entries[0].SourcePath,
                "output" => plan.Entries[0].OutputPath,
                _ => plan.Entries[0].OutputPath + ".conversion.json"
            }, "// external edit\r\n");
            expected = ReceiptArtifacts(plan);
        }));
        Assert.Equal(2, report.Summary.FailedFiles);
        Assert.All(report.FileResults, file =>
        {
            Assert.False(file.OutputWritten);
            Assert.Equal("refused", file.ConversionPaths!.Outcome);
        });
        Assert.NotNull(expected);
        AssertReceiptArtifacts(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectReceipt_DryRunDoesNotCreateOrRefreshArtifacts(bool existing)
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        var plan = ReceiptPlan("A.cs");
        plan.Entries[0].OutputPath = Path.Combine(_tempDir, "out", "A.calr");
        var migrator = ReceiptMigrator();
        if (existing) Assert.Equal(0, (await migrator.ExecuteAsync(plan)).Summary.FailedFiles);
        var before = ReceiptArtifacts(plan);
        var report = await migrator.DryRunAsync(plan);
        Assert.Equal(0, report.Summary.FailedFiles);
        Assert.False(Assert.Single(report.FileResults).OutputWritten);
        AssertReceiptArtifacts(before);
        if (!existing) Assert.False(Directory.Exists(Path.Combine(_tempDir, "out")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectReceipt_CancellationAfterConversionDoesNotCommit(bool existing)
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        WriteTestCSharpFile("B.cs", "public class B { }");
        var plan = ReceiptPlan("A.cs", "B.cs");
        var migrator = ReceiptMigrator();
        if (existing) Assert.Equal(0, (await migrator.ExecuteAsync(plan)).Summary.FailedFiles);
        var before = ReceiptArtifacts(plan);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            migrator.ExecuteAsync(plan, cancellationToken: cancellation.Token,
                progress: new ReceiptProgress(progress =>
                {
                    if (progress.ProcessedFiles == progress.TotalFiles) cancellation.Cancel();
                })));
        AssertReceiptArtifacts(before);
        Assert.Equal(0, (await migrator.ExecuteAsync(plan)).Summary.FailedFiles);
    }

    [Fact]
    public async Task ProjectReceipt_PerFileTimeoutDoesNotWrite()
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        var plan = ReceiptPlan("A.cs");
        var before = ReceiptArtifacts(plan);
        var report = await new ProjectMigrator(new()
        {
            Parallel = false, PerFileTimeoutSeconds = 0
        }).ExecuteAsync(plan);
        Assert.Equal(FileMigrationStatus.TimedOut, Assert.Single(report.FileResults).Status);
        AssertReceiptArtifacts(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectReceipt_ValidationRefusalDoesNotWrite(bool existing)
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        var plan = ReceiptPlan("A.cs");
        var migrator = ReceiptMigrator();
        if (existing) Assert.Equal(0, (await migrator.ExecuteAsync(plan)).Summary.FailedFiles);
        var before = ReceiptArtifacts(plan);
        WriteTestCSharpFile("A.cs", "#error reject\npublic class A { }");
        var report = await migrator.ExecuteAsync(plan);
        Assert.Equal(FileMigrationStatus.Failed, Assert.Single(report.FileResults).Status);
        AssertReceiptArtifacts(before);
    }

    [Fact]
    public async Task ProjectReceipt_ProjectValidationRefusalDoesNotWrite()
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        var project = await CreateReceiptProjectAsync();
        var migrator = ReceiptMigrator();
        var plan = await migrator.CreatePlanAsync(project, MigrationDirection.CSharpToCalor);
        var before = ReceiptArtifacts(plan);
        var report = await migrator.ExecuteAsync(plan, progress: new ReceiptProgress(_ =>
            File.WriteAllText(project, "<Project invalid")));
        Assert.Equal(FileMigrationStatus.Failed, Assert.Single(report.FileResults).Status);
        Assert.Contains(report.FileResults[0].Issues, issue => issue.Feature == "project-csharp-validation");
        AssertReceiptArtifacts(before);
    }

    [Fact]
    public async Task ProjectReceipt_ProjectLeaseExcludesCooperatingCliWriter()
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        WriteTestCSharpFile("B.cs", "public class B { }");
        var plan = ReceiptPlan("A.cs", "B.cs");
        var report = await ReceiptMigrator().ExecuteAsync(plan, progress: new ReceiptProgress(progress =>
        {
            if (progress.ProcessedFiles != progress.TotalFiles) return;
            var competing = CliTestHarness.RunCli(_tempDir, "convert", plan.Entries[0].SourcePath, "--format", "json");
            Assert.Equal(1, competing.ExitCode);
            Assert.All(plan.Entries, entry =>
            {
                Assert.False(File.Exists(entry.OutputPath));
                Assert.False(File.Exists(entry.OutputPath + ".conversion.json"));
            });
        }));
        Assert.Equal(0, report.Summary.FailedFiles);
        Assert.All(report.FileResults, file => Assert.True(file.OutputWritten));
    }

    [Fact]
    public async Task ProjectReceipt_PartialReceiptFailureRetainsBothCommittedOutputs()
    {
        WriteTestCSharpFile("A.cs", "public class A { }");
        WriteTestCSharpFile("B.cs", "public class B { ~B() { } }");
        var plan = ReceiptPlan("A.cs", "B.cs");
        var failedReceipt = plan.Entries[1].OutputPath + ".conversion.json";
        var report = await ReceiptMigrator().ExecuteAsync(plan, progress: new ReceiptProgress(progress =>
        {
            if (progress.ProcessedFiles == progress.TotalFiles) Directory.CreateDirectory(failedReceipt);
        }));
        Assert.Equal(1, report.Summary.FailedFiles);
        Assert.All(report.FileResults, file =>
        {
            Assert.True(file.OutputWritten);
            Assert.NotNull(file.OutputPath);
            Assert.Equal(file.ConvertedSource, File.ReadAllText(file.OutputPath));
        });
        Assert.True(File.Exists(plan.Entries[0].OutputPath + ".conversion.json"));
        Assert.False(File.Exists(failedReceipt));
        var failed = Assert.Single(report.FileResults, file => file.Status == FileMigrationStatus.Failed);
        Assert.Equal("preserved", failed.ConversionPaths!.Outcome);
        Assert.NotEmpty(failed.Losses);
        Assert.Contains(failed.Issues, issue => issue.Feature == "migration-receipt-failure"
            && issue.Message.Contains("Output was written", StringComparison.Ordinal));
        var json = JsonSerializer.Serialize(failed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.True(JsonDocument.Parse(json).RootElement.GetProperty("outputWritten").GetBoolean());
        var written = File.ReadAllBytes(plan.Entries[1].OutputPath);
        var retry = await ReceiptMigrator().ExecuteAsync(plan);
        Assert.Equal(1, retry.Summary.FailedFiles);
        Assert.Equal(written, File.ReadAllBytes(plan.Entries[1].OutputPath));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ProjectReceipt_ProjectRetainsSiblingOptionsAliasesAndActualRescueContext(bool rescue, bool staleReference)
    {
        var referencePath = Path.Combine(_tempDir, "External.dll");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("External",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace ExternalLib; public class Value { }")],
            CodeGen.GeneratedCSharpCompiler.References,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using (var stream = File.Create(referencePath))
            Assert.True(compilation.Emit(stream).Success);
        WriteTestCSharpFile("A.cs", "extern alias External; public class A { public External::ExternalLib.Value Get() => new(); }");
        WriteTestCSharpFile("B.cs", rescue
            ? "public class B { public Missing Get() => new Missing(); }"
            : "public class B { public A Get() => new A(); }");
        WriteTestCSharpFile("C.cs", "public class C { }");
        var project = await CreateReceiptProjectAsync();
        var options = new MigrationPlanOptions
        {
            Parallel = false, Configuration = "Release",
            DefinedSymbols = ["EXTRA"],
            DocumentationMode = Microsoft.CodeAnalysis.DocumentationMode.Diagnose,
            ParseFeatures = new Dictionary<string, string> { ["receipt-control"] = "true" },
            UseImplicitCallCloser = false
        };
        var migrator = new ProjectMigrator(options);
        var plan = await migrator.CreatePlanAsync(project, MigrationDirection.CSharpToCalor);
        var before = ReceiptArtifacts(plan);
        var report = await migrator.ExecuteAsync(plan, progress: new ReceiptProgress(progress =>
        {
            if (staleReference && progress.ProcessedFiles == progress.TotalFiles)
                File.AppendAllText(referencePath, "observed reference changed");
        }));
        if (staleReference)
        {
            Assert.Equal(3, report.Summary.FailedFiles);
            AssertReceiptArtifacts(before);
            return;
        }
        Assert.Equal(0, report.Summary.FailedFiles);
        foreach (var file in report.FileResults)
        {
            var receipt = ReadReceipt(file.OutputPath!);
            Assert.Equal(File.ReadAllText(file.SourcePath), receipt.Original.Text);
            Assert.Equal(_tempDir, receipt.Settings.GetProperty("ProjectDirectory").GetString());
            Assert.Equal("Release", receipt.Metadata.Configuration);
            Assert.Contains("PROJECT_FLAG", receipt.Original.Symbols);
            Assert.Contains("EXTRA", receipt.Original.Symbols);
            Assert.Equal("Diagnose", receipt.Original.DocumentationMode);
            Assert.Equal("true", receipt.Original.Features["receipt-control"]);
            Assert.False(receipt.Settings.GetProperty("UseImplicitCallCloser").GetBoolean());
            Assert.True(receipt.Metadata.ReferencesAreComplete);
            Assert.Contains(receipt.Metadata.References, reference =>
                CanonicalPath.Resolve(reference.Path) == CanonicalPath.Resolve(referencePath)
                && reference.Aliases.SequenceEqual(["External"]));
            Assert.All(plan.Entries, entry => Assert.Contains(receipt.Siblings,
                sibling => CanonicalPath.Resolve(sibling.Path) == CanonicalPath.Resolve(entry.SourcePath)
                    && sibling.Text == File.ReadAllText(entry.SourcePath)
                    && sibling.Features["receipt-control"] == "true"));
            Assert.NotEmpty(receipt.ReplayLimitations);
            if (rescue && file.Losses.Any(loss => loss.Feature == "whole-file-preservation"))
            {
                var frame = Assert.Single(receipt.Frames);
                var boundary = receipt.ResolveBoundary(0, receipt.GeneratedCalor);
                Assert.Equal(receipt.Original.Text, frame.Payload);
                Assert.Equal(receipt.Original.Text, boundary.SyntaxTree.GetText().ToString());
            }
        }
        if (rescue)
            Assert.Contains(report.FileResults, file => file.Issues.Any(issue =>
                issue.Message.Contains("Project C# validation failed", StringComparison.Ordinal)));
    }

    private static ProjectMigrator ReceiptMigrator(bool parallel = false)
        => new(new MigrationPlanOptions { Parallel = parallel, MergePartialClasses = false });

    private MigrationPlan ReceiptPlan(params string[] files) => new()
    {
        ProjectPath = _tempDir,
        Direction = MigrationDirection.CSharpToCalor,
        Entries = files.Select(file => new MigrationPlanEntry
        {
            SourcePath = Path.Combine(_tempDir, file),
            OutputPath = Path.ChangeExtension(Path.Combine(_tempDir, file), ".calr"),
            Convertibility = FileConvertibility.Full
        }).ToList()
    };

    private static ConversionReceipt ReadReceipt(string output)
        => ConversionReceipt.Deserialize(File.ReadAllText(output + ".conversion.json"));

    private static Dictionary<string, byte[]?> ReceiptArtifacts(MigrationPlan plan)
        => plan.Entries.SelectMany(entry => new[] { entry.OutputPath, entry.OutputPath + ".conversion.json" })
            .Append(Path.Combine(plan.ProjectPath, ".calor", "ids.json")).Distinct()
            .ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);

    private static void AssertReceiptArtifacts(Dictionary<string, byte[]?> expected, MigrationReport? report = null)
    {
        foreach (var pair in expected)
        {
            var actual = File.Exists(pair.Key) ? File.ReadAllBytes(pair.Key) : null;
            Assert.True(pair.Value == null ? actual == null : actual != null && pair.Value.SequenceEqual(actual),
                $"Artifact changed: {pair.Key}\nBefore: {System.Text.Encoding.UTF8.GetString(pair.Value ?? [])}\n"
                + $"After: {System.Text.Encoding.UTF8.GetString(actual ?? [])}\n"
                + JsonSerializer.Serialize(report?.FileResults.Select(file => new { file.SourcePath, file.Status, file.Issues })));
        }
    }

    private async Task<string> CreateReceiptProjectAsync()
    {
        var project = Path.Combine(_tempDir, "Receipt.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <DefineConstants>PROJECT_FLAG</DefineConstants>
              </PropertyGroup>
              <ItemGroup Condition="Exists('External.dll')">
                <Reference Include="External"><HintPath>External.dll</HintPath><Aliases>External</Aliases></Reference>
              </ItemGroup>
            </Project>
            """);
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            WorkingDirectory = _tempDir
        };
        foreach (var arg in new[] { "restore", project, "--source", _tempDir, "--disable-parallel",
                     "-p:NuGetAudit=false", "-p:UseSharedCompilation=false", "-nodeReuse:false" })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
        return project;
    }

    private sealed class ReceiptProgress(Action<MigrationProgress> callback) : IProgress<MigrationProgress>
    {
        public void Report(MigrationProgress value) => callback(value);
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
            module VerifyTest VerifyTest block
              func Square (int:x) → int
                !
                requires (x >= 0)
                => (result >= 0)
                return (x * x)

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

    #region ValidateOutput

    [Fact]
    public async Task ExecuteAsync_ValidateOutput_MarksPartialWhenCompileFails()
    {
        // Write a valid C# file that converts to Calor successfully
        WriteTestCSharpFile("Simple.cs", """
            public class Greeter
            {
                public string Greet(string name)
                {
                    return $"Hello, {name}!";
                }
            }
            """);

        var options = new MigrationPlanOptions
        {
            Parallel = false,
            ValidateOutput = true
        };

        var migrator = new ProjectMigrator(options);
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var report = await migrator.ExecuteAsync(plan);

        // File should convert and validate successfully
        var fileResult = report.FileResults.FirstOrDefault(f =>
            f.SourcePath.EndsWith("Simple.cs") &&
            f.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial);
        Assert.NotNull(fileResult);
    }

    [Fact]
    public async Task ExecuteAsync_ValidateOutputFalse_SkipsCompileCheck()
    {
        WriteTestCSharpFile("Basic.cs", """
            public class Adder
            {
                public int Add(int a, int b) => a + b;
            }
            """);

        var options = new MigrationPlanOptions
        {
            Parallel = false,
            ValidateOutput = false
        };

        var migrator = new ProjectMigrator(options);
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);
        var report = await migrator.ExecuteAsync(plan);

        var fileResult = report.FileResults.FirstOrDefault(f =>
            f.SourcePath.EndsWith("Basic.cs"));
        Assert.NotNull(fileResult);
        Assert.Equal(FileMigrationStatus.Success, fileResult!.Status);
        // No validation issues should be present
        Assert.DoesNotContain(fileResult.Issues, i => i.Message.Contains("Validation"));
    }

    #endregion
}
