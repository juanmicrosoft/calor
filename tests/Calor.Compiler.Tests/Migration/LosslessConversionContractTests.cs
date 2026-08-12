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
}
