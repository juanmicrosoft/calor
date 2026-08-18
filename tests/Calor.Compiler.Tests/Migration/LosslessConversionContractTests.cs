using System.Text;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class LosslessConversionContractTests
{
    [Fact]
    public void CliPolicy_DefaultsToLossless()
    {
        var options = Commands.ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false,
            verbose: false,
            explain: false,
            noFallback: false,
            passthrough: false,
            explicitCallClosers: false);

        Assert.Equal(ConversionFidelity.Lossless, options.Fidelity);
    }

    [Fact]
    public void CliPolicy_SelectedBranch_IsExplicitLossyAndCarriesParseMetadata()
    {
        var options = Commands.ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false,
            verbose: false,
            explain: false,
            noFallback: false,
            passthrough: false,
            explicitCallClosers: false,
            selectActivePreprocessorBranchLossy: true,
            definedSymbols: ["FEATURE"],
            configuration: "Release",
            targetFramework: "net10.0",
            languageVersion: "preview",
            documentationMode: "diagnose",
            sourceKind: "script",
            features: ["test_feature=enabled"],
            references: ["reference.dll=One,Two"]);

        Assert.Equal(ConversionFidelity.Lossy, options.Fidelity);
        Assert.Equal(
            PreprocessorConversionMode.SelectActiveBranchLossy,
            options.PreprocessorMode);
        Assert.Contains("FEATURE", options.DefinedSymbols);
        Assert.Equal("Release", options.Configuration);
        Assert.Equal("net10.0", options.TargetFramework);
        Assert.Equal(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            options.ParseOptions!.LanguageVersion);
        Assert.Equal(
            Microsoft.CodeAnalysis.DocumentationMode.Diagnose,
            options.ParseOptions.DocumentationMode);
        Assert.Equal(
            Microsoft.CodeAnalysis.SourceCodeKind.Script,
            options.ParseOptions.Kind);
        Assert.Equal(
            "enabled",
            options.ParseOptions.Features
                .ToDictionary(
                    feature => feature.Key,
                    feature => feature.Value)["test_feature"]);
        var reference = Assert.Single(options.References);
        Assert.Equal(["One", "Two"], reference.Aliases);
    }

    [Fact]
    public void UnsupportedDescendant_IsPreservedAtMemberBoundary()
    {
        const string source = """
            public class Example
            {
                public int Get()
                {
                    int Local() => 42;
                    return Local();
                }
            }
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossless
        }).Convert(source, "Example.cs");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("int Local() => 42;", result.CalorSource);
        Assert.Equal(1, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
    }

    [Fact]
    public void LossyMode_ReportsEveryDropLocation()
    {
        const string source = """
            public interface IEvents
            {
                event System.EventHandler Changed;
            }
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy
        }).Convert(source, "IEvents.cs");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        var loss = Assert.Single(result.Losses, item => item.Kind == ConversionLossKind.Dropped);
        Assert.Equal("IEvents.cs", loss.File);
        Assert.True(loss.Line > 0);
        Assert.Equal(1, result.DropCount);
    }

    [Fact]
    public void NativeMarkerTextInsideStringDoesNotCreateFallbackLoss()
    {
        const string source = """
            public class Example
            {
                public string Get() => "§RAW §CS{ §CSHARP{";
            }
            """;

        var result = new CSharpToCalorConverter().Convert(source, "Example.cs");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        Assert.DoesNotContain(
            result.Losses,
            loss => loss.Kind == ConversionLossKind.EmitterFallback);
    }

    [Fact]
    public void GeneratedCalorValidation_IsMandatoryInLossyMode()
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy
        })
        {
            ParseValidatorOverride = _ => false
        };

        var result = converter.Convert("public class Example { public int Get() => 1; }");

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Feature == "generated-calor-validation");
    }

    [Fact]
    public async Task AtomicWrite_CancellationLeavesExistingDestinationUnchanged()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-lossless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "output.calr");
        await File.WriteAllTextAsync(path, "original", Encoding.UTF8);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ConversionFileWriter.WriteAtomicAsync(path, "replacement", cancellationToken: cts.Token));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task ProjectMigration_LosslessMixedFilesPreservesUnsupportedMembers()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-lossless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var nativePath = Path.Combine(directory, "Native.cs");
        var interopPath = Path.Combine(directory, "Interop.cs");
        await File.WriteAllTextAsync(nativePath, "public class Native { public int Get() => 1; }");
        await File.WriteAllTextAsync(
            interopPath,
            "public class Interop { public int Get() { int Local() => 2; return Local(); } }");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                Entry(nativePath),
                Entry(interopPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            Fidelity = ConversionFidelity.Lossless
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(
                report.FileResults,
                result => Assert.Contains(
                    result.Status,
                    new[] { FileMigrationStatus.Success, FileMigrationStatus.Partial }));
            var interop = Assert.Single(report.FileResults, result => result.SourcePath == interopPath);
            Assert.Contains(interop.Losses, loss => loss.Kind == ConversionLossKind.InteropPreserved);
            Assert.Contains("§CSHARP", await File.ReadAllTextAsync(Path.ChangeExtension(interopPath, ".calr")));
        }

        finally
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_CallerCancellation_PropagatesInsteadOfTimingOut()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"calor-project-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Cancelled.cs");
        await File.WriteAllTextAsync(
            sourcePath,
            "public class Cancelled { }");
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries = [Entry(sourcePath)]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => migrator.ExecuteAsync(
                    plan,
                    cancellationToken: cts.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_LosslessValidatesCrossFileReferencesTogether()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-cross-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sharedPath = Path.Combine(directory, "Shared.cs");
        var consumerPath = Path.Combine(directory, "Consumer.cs");
        await File.WriteAllTextAsync(sharedPath, "namespace Demo; public class Shared { }");
        await File.WriteAllTextAsync(
            consumerPath,
            "namespace Demo; public class Consumer { public Shared Create() => new Shared(); }");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                Entry(sharedPath),
                Entry(consumerPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            Fidelity = ConversionFidelity.Lossless
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(report.FileResults, result => Assert.Equal(FileMigrationStatus.Success, result.Status));
            Assert.True(File.Exists(Path.ChangeExtension(sharedPath, ".calr")));
            Assert.True(File.Exists(Path.ChangeExtension(consumerPath, ".calr")));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_LosslessAggregateFailureWritesNothingAndRefreshesSummary()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-invalid-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var validPath = Path.Combine(directory, "Valid.cs");
        var invalidPath = Path.Combine(directory, "Invalid.cs");
        await File.WriteAllTextAsync(validPath, "namespace Demo; public class Valid { }");
        await File.WriteAllTextAsync(
            invalidPath,
            "namespace Demo; public class Invalid { public Missing Create() => new Missing(); }");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                Entry(validPath),
                Entry(invalidPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            Fidelity = ConversionFidelity.Lossless
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(report.FileResults, result => Assert.Equal(FileMigrationStatus.Failed, result.Status));
            Assert.Equal(0, report.Summary.SuccessfulFiles);
            Assert.Equal(2, report.Summary.FailedFiles);
            Assert.True(report.Summary.TotalErrors > 0);
            Assert.False(File.Exists(Path.ChangeExtension(validPath, ".calr")));
            Assert.False(File.Exists(Path.ChangeExtension(invalidPath, ".calr")));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_CalorToCSharpValidatesCrossFileReferencesTogether()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-reverse-cross-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var libraryPath = Path.Combine(directory, "Library.calr");
        var consumerPath = Path.Combine(directory, "Consumer.calr");
        await File.WriteAllTextAsync(
            libraryPath,
            """
            §M{m001:Library}
              §F{f001:Square:pub} (i32:x) -> i32
                §R (* x x)
            """);
        await File.WriteAllTextAsync(
            consumerPath,
            """
            §M{m002:Consumer}
              §F{f002:UseSquare:pub} (i32:x) -> i32
                §R §C{Square} §A x §/C
            """);

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                Entry(libraryPath),
                Entry(consumerPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(
                report.FileResults,
                result => Assert.Equal(FileMigrationStatus.Success, result.Status));
            Assert.True(File.Exists(Path.ChangeExtension(libraryPath, ".g.cs")));
            Assert.True(File.Exists(Path.ChangeExtension(consumerPath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_CalorToCSharpUsesExistingProjectSources()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-existing-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Sample.csproj");
        var sourcePath = Path.Combine(directory, "Consumer.calr");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net10.0</TargetFrameworks>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Existing.cs"),
            "public sealed class Existing { }");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            §M{m001:Consumer}
              §CL{c001:Factory:pub}
                §CSHARP{public Existing Create() => new Existing();}§/CSHARP
            """);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = projectPath,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(
                new MigrationPlanOptions { Parallel = false }).ExecuteAsync(plan);

            Assert.Equal(FileMigrationStatus.Success, Assert.Single(report.FileResults).Status);
            Assert.True(File.Exists(Path.ChangeExtension(sourcePath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_AmbiguousDirectoryProjectContextFailsExplicitly()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-ambiguous-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Consumer.calr");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "First.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Second.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            §M{m001:Consumer}
              §F{f001:Read:pub} () -> i32
                §R INT:42
            """);
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(
                new MigrationPlanOptions { Parallel = false }).ExecuteAsync(plan);

            var result = Assert.Single(report.FileResults);
            Assert.Equal(FileMigrationStatus.Failed, result.Status);
            Assert.Contains(
                result.Issues,
                issue => issue.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(Path.ChangeExtension(sourcePath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_CalorToCSharpHonorsUnsafeProjectSetting()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-unsafe-setting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Sample.csproj");
        var sourcePath = Path.Combine(directory, "Unsafe.calr");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            §M{m001:Unsafe}
              §CL{c001:Worker:pub}
                §MT{m001:Run:pub} () -> void
                  §E{unsafe}
                  §UNSAFE{u001}
                    §B{~x:i32} INT:42
                  §/UNSAFE{u001}
            """);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = projectPath,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(
                new MigrationPlanOptions { Parallel = false }).ExecuteAsync(plan);

            var result = Assert.Single(report.FileResults);
            Assert.Equal(FileMigrationStatus.Failed, result.Status);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("CS0227"));
            Assert.False(File.Exists(Path.ChangeExtension(sourcePath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_TimedOutLossyConversionCannotWriteLater()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Slow.cs");
        var outputPath = Path.ChangeExtension(sourcePath, ".calr");
        await File.WriteAllTextAsync(sourcePath, "public class Item { }");
        await File.WriteAllTextAsync(outputPath, "original");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = outputPath,
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            Fidelity = ConversionFidelity.Lossy,
            PerFileTimeoutSeconds = 0
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.Equal(FileMigrationStatus.TimedOut, Assert.Single(report.FileResults).Status);
            Assert.Equal("original", await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }
    }

    private static async Task RestoreAsync(string projectPath)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"{await stdout}{Environment.NewLine}{await stderr}");
    }
}
