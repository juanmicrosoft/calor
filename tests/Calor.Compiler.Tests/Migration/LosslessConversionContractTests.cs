using System.Text;
using System.Text.Json;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class LosslessConversionContractTests
{
    [Theory]
    [InlineData("#nullable enable\npublic static class Scalar { public static int Take(string value) => 1; public static int Call(string? value) => Take(value); }")]
    [InlineData("#nullable enable\npublic static class Arrays { public static int Take(string[,] values) => 1; public static int Call(string[,]? values) => Take(values); }")]
    [InlineData("#nullable enable\npublic class Box { public Box(string? value) { } public static Box Create(string value) => new Box(value); }")]
    public void DefaultPreservation_DoesNotRelaxNativeReceivingChecks(string original)
    {
        Assert.Empty(Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(original).CompilationErrors);
        var native = new CSharpToCalorConverter(new() { ValidateRoundTripCSharp = false }).Convert(original);
        Assert.True(native.Success);
        Assert.Equal(0, native.InteropPreservationCount);
        Assert.True(Program.Compile(native.CalorSource!, "native-receiving.calr",
            new CompilationOptions { DeferGeneratedOutputValidation = true }).HasErrors);

        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.Equal(1, converted.InteropPreservationCount);
        Assert.DoesNotContain(converted.Losses, loss => loss.IsSemanticLoss);
        var compiled = Program.Compile(converted.CalorSource!, "preserved-receiving.calr",
            new CompilationOptions { DeferGeneratedOutputValidation = true });
        Assert.False(compiled.HasErrors);
        Assert.Contains(original.Split('\n', 2)[1], compiled.GeneratedCode);
        Assert.Empty(Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(compiled.GeneratedCode!).CompilationErrors);
    }

    [Fact]
    public void DefaultPreservation_DoesNotAcceptInvalidIteratorParameters()
    {
        const string original = """
            using System.Collections.Generic;
            public class IteratorParameters
            {
                public void Outer()
                {
                    IEnumerable<int> Local(ref int value)
                    {
                        yield return value;
                    }
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.False(converted.Success);
        Assert.Contains(converted.Issues, issue => issue.Severity == ConversionIssueSeverity.Error
            && issue.Message.Contains("local function 'Local'", StringComparison.Ordinal)
            && issue.Message.Contains("modifier 'ref'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DefaultCli_WholeFilePreservation_ReproducesUtf8BytesAndRuntime(bool bom, bool eofComment)
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-exact-").FullName;
        const string code = """"
            [assembly: System.Reflection.AssemblyMetadata("oracle", "original")]
            #line 20 "original-location.cs"
            public static class Oracle
            {
                public static string Run() => """
                    exact §CSHARP text
                      raw indentation
                    """ + typeof(Oracle).Assembly.GetCustomAttributes(
                        typeof(System.Reflection.AssemblyMetadataAttribute), false).Length;
            }
            """";
        var source = (bom ? "\uFEFF" : "") + code.Replace("\n", "\r\n")
            + (eofComment ? "// original EOF }§/CSHARP" : "\r\n");
        var input = Path.Combine(directory, "Oracle.cs");
        File.WriteAllBytes(input, Encoding.UTF8.GetBytes(source));
        try
        {
            var converted = CliTestHarness.RunCli(directory, "convert", input);
            Assert.True(converted.ExitCode == 0, converted.StdOut + converted.StdErr);
            var calor = Path.ChangeExtension(input, ".calr");
            var generated = CliTestHarness.RunCli(directory, "convert", calor);
            Assert.True(generated.ExitCode == 0, generated.StdOut + generated.StdErr);
            var csharp = Path.Combine(directory, "Oracle.g.cs");
            Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(csharp));
            Assert.Equal("exact §CSHARP text\r\n  raw indentation1", Run(source));
            Assert.Equal(Run(source), Run(Encoding.UTF8.GetString(File.ReadAllBytes(csharp))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static string Run(string source)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator).Select(path =>
                    Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path));
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "Oracle" + Guid.NewGuid().ToString("N"),
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
                references,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            return (string)assembly.GetType("Oracle")!.GetMethod("Run")!.Invoke(null, null)!;
        }
    }

    [Fact]
    public void DefaultCli_MalformedMissingAndWriteFailure_DoNotBlockOtherSources()
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-controls-").FullName;
        File.WriteAllText(Path.Combine(directory, "Valid.cs"),
            "public class Valid { public int Value() => 7; }");
        File.WriteAllText(Path.Combine(directory, "Malformed.cs"),
            "public class Malformed { public int Value( }");
        File.WriteAllText(Path.Combine(directory, "Unwritable.cs"),
            "public class Unwritable { }");
        File.WriteAllText(Path.Combine(directory, "InvalidType.cs"),
            "public class InvalidType { public int Value() => \"not an int\"; }");
        File.WriteAllText(Path.Combine(directory, "InvalidWithMissingContext.cs"),
            "public class InvalidWithMissingContext { MissingContext field; public int Value() => \"not an int\"; }");
        File.WriteAllBytes(Path.Combine(directory, "Unreadable.cs"), [0xFF, 0xFF, 0xFF]);
        Directory.CreateDirectory(Path.Combine(directory, "Unwritable.calr"));
        try
        {
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "Missing.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "migrate", "missing").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "Malformed.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "InvalidType.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "InvalidWithMissingContext.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "Unreadable.cs").ExitCode);
            var reportPath = Path.Combine(directory, "report.json");
            var migration = CliTestHarness.RunCli(directory, "migrate", directory, "--report", reportPath);
            Assert.Equal(1, migration.ExitCode);
            Assert.True(File.Exists(Path.Combine(directory, "Valid.calr")),
                migration.StdOut + migration.StdErr
                    + (File.Exists(reportPath) ? File.ReadAllText(reportPath) : ""));
            Assert.False(File.Exists(Path.Combine(directory, "Malformed.calr")));
            Assert.True(Directory.Exists(Path.Combine(directory, "Unwritable.calr")));
            var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath))
                .RootElement.GetProperty("data");
            Assert.Equal(6, report.GetProperty("summary").GetProperty("totalFiles").GetInt32());
            Assert.Equal(5, report.GetProperty("summary").GetProperty("failedFiles").GetInt32());
            Assert.Contains(report.GetProperty("fileResults").EnumerateArray(), file =>
                file.GetProperty("issues").EnumerateArray().Any(issue =>
                    issue.GetProperty("feature").GetString() == "migration-write-failure"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultCli_Csproj_WritesEveryInputAndRetainsNativeAndPreservedOutcomes()
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-project-").FullName;
        var project = Path.Combine(directory, "Original.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "Native.cs"),
            "public class Native { public int Value() => 7; }");
        const string original = "[assembly: System.Reflection.AssemblyMetadata(\"oracle\", \"exact\")]\r\npublic record Preserved(int Value);\r\n";
        File.WriteAllText(Path.Combine(directory, "Preserved.cs"), original);
        try
        {
            await RestoreAsync(project);
            var reportPath = Path.Combine(directory, "report.json");
            var migrated = CliTestHarness.RunCli(directory, "migrate", project, "--report", reportPath);
            Assert.True(migrated.ExitCode == 0, migrated.StdOut + migrated.StdErr);
            Assert.Equal(2, Directory.GetFiles(directory, "*.calr").Length);
            var files = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath))
                .RootElement.GetProperty("data").GetProperty("fileResults").EnumerateArray().ToList();
            Assert.Contains(files, file =>
                file.GetProperty("conversionPaths").GetProperty("outcome").GetString() == "native");
            Assert.Contains(files, file =>
                file.GetProperty("conversionPaths").GetProperty("outcome").GetString() == "preserved");
            Assert.All(files, file => Assert.Equal("verified",
                file.GetProperty("conversionPaths").GetProperty("roundTrip").GetString()));
            var compiled = Program.Compile(File.ReadAllText(Path.Combine(directory, "Preserved.calr")),
                "Preserved.calr", new CompilationOptions { DeferGeneratedOutputValidation = true });
            Assert.False(compiled.HasErrors);
            Assert.Equal(original, compiled.GeneratedCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultCli_Csproj_MissingGeneratedContextWritesExactOriginalAsUnverified()
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-generated-context-").FullName;
        var project = Path.Combine(directory, "Original.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <Target Name="GenerateRequiredType" BeforeTargets="CoreCompile">
                <WriteLinesToFile File="$(IntermediateOutputPath)Generated.cs"
                  Lines="public class GeneratedDependency { }" Overwrite="true" />
                <ItemGroup><Compile Include="$(IntermediateOutputPath)Generated.cs" /></ItemGroup>
              </Target>
            </Project>
            """);
        const string original = "[assembly: System.Reflection.AssemblyMetadata(\"oracle\", \"generated\")]\r\npublic class Consumer { public GeneratedDependency Create() => new GeneratedDependency(); }\r\n";
        var source = Path.Combine(directory, "Consumer.cs");
        File.WriteAllText(source, original);
        try
        {
            await RestoreAsync(project);
            await RunProjectCommandAsync(project, "build");
            var reportPath = Path.Combine(directory, "report.json");
            var migrated = CliTestHarness.RunCli(directory, "migrate", project, "--report", reportPath);
            Assert.True(migrated.ExitCode == 0, migrated.StdOut + migrated.StdErr);
            var report = JsonDocument.Parse(File.ReadAllText(reportPath))
                .RootElement.GetProperty("data");
            Assert.Equal(1, report.GetProperty("summary").GetProperty("totalFiles").GetInt32());
            Assert.Equal(0, report.GetProperty("summary").GetProperty("failedFiles").GetInt32());
            var file = Assert.Single(report.GetProperty("fileResults").EnumerateArray());
            Assert.Equal("unverified", file.GetProperty("conversionPaths").GetProperty("outcome").GetString());
            Assert.Equal("failed", file.GetProperty("conversionPaths").GetProperty("roundTrip").GetString());
            Assert.Contains(file.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("feature").GetString() == "roundtrip-csharp-validation"
                && issue.GetProperty("message").GetString()!.Contains("CS0246", StringComparison.Ordinal));
            var compiled = Program.Compile(File.ReadAllText(Path.ChangeExtension(source, ".calr")),
                "Consumer.calr", new CompilationOptions { DeferGeneratedOutputValidation = true });
            Assert.False(compiled.HasErrors);
            Assert.Equal(File.ReadAllBytes(source), Encoding.UTF8.GetBytes(compiled.GeneratedCode!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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

    [Theory]
    [InlineData("convert")]
    [InlineData("migrate")]
    public void DefaultCli_HelpAndParser_DoNotExposeDestructiveFlags(string command)
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-help-").FullName;
        try
        {
            var help = CliTestHarness.RunCli(directory, command, "--help");
            Assert.Equal(0, help.ExitCode);
            Assert.DoesNotContain("--lossy", help.StdOut + help.StdErr);
            Assert.DoesNotContain("--select-active-preprocessor-branch-lossy", help.StdOut + help.StdErr);
            foreach (var flag in new[] { "--lossy", "--select-active-preprocessor-branch-lossy" })
            {
                var rejected = CliTestHarness.RunCli(directory, command, directory, flag);
                Assert.NotEqual(0, rejected.ExitCode);
                Assert.Contains(flag, rejected.StdOut + rejected.StdErr);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
                public int Get(int k)
                {
                    // 0.25 F3 (#847): capturing, so outside the native slice.
                    int Local() => 42 + k;
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
        Assert.Contains("int Local() => 42 + k;", result.CalorSource);
        Assert.Equal(1, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
    }

    [Fact]
    public void LossyMode_PreservesUnsupportedInterfaceWithLocation()
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
        var loss = Assert.Single(result.Losses, item =>
            item.Kind == ConversionLossKind.InteropPreserved
            && item.Feature == "interface-member");
        Assert.Equal("IEvents.cs", loss.File);
        Assert.True(loss.Line > 0);
        Assert.Equal(0, result.DropCount);
        Assert.Equal(1, result.InteropPreservationCount);
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
            "public class Interop { public int Get(int k) { int Local() => k; return Local(); } }");

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
    public async Task ProjectMigration_MissingLooseReferencesPreservesSourceAndReportsFailedValidation()
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

            Assert.All(report.FileResults, result =>
            {
                Assert.Equal(FileMigrationStatus.Partial, result.Status);
                Assert.Equal("unverified", result.ConversionPaths!.Outcome);
                Assert.Equal("failed", result.ConversionPaths.RoundTrip);
                Assert.Equal(File.ReadAllText(result.SourcePath), result.GeneratedCSharp);
            });
            Assert.Equal(0, report.Summary.SuccessfulFiles);
            Assert.Equal(0, report.Summary.FailedFiles);
            Assert.True(report.Summary.TotalWarnings > 0);
            Assert.True(File.Exists(Path.ChangeExtension(validPath, ".calr")));
            Assert.True(File.Exists(Path.ChangeExtension(invalidPath, ".calr")));
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
            §M{Library}
              §F{Square:pub} (i32:x) -> i32
                §R (* x x)
            """);
        await File.WriteAllTextAsync(
            consumerPath,
            """
            §M{Consumer}
              §F{UseSquare:pub} (i32:x) -> i32
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
            §M{Consumer}
              §CL{Factory:pub}
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
            §M{Consumer}
              §F{Read:pub} () -> i32
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
            §M{Unsafe}
              §CL{Worker:pub}
                §MT{Run:pub} () -> void
                  §E{unsafe}
                  §UNSAFE
                    §B{~x:i32} INT:42
                  §/UNSAFE
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

    private static Task RestoreAsync(string projectPath)
        => RunProjectCommandAsync(projectPath, "restore");

    private static async Task RunProjectCommandAsync(string projectPath, string command)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add(command == "restore" ? "--ignore-failed-sources" : "--no-restore");
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"{await stdout}{Environment.NewLine}{await stderr}");
    }
}
