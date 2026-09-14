using System.Reflection;
using System.Diagnostics;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class Issue1401NullableContextConversionTests
{
    [Theory]
    [InlineData(NullableContextOptions.Enable, "(str:value) -> str")]
    [InlineData(NullableContextOptions.Annotations, "(str:value) -> str")]
    [InlineData(NullableContextOptions.Disable, "(?str:value) -> ?str")]
    [InlineData(NullableContextOptions.Warnings, "(?str:value) -> ?str")]
    public void EvaluatedProjectContextControlsObliviousDeclarations(
        NullableContextOptions nullableContext,
        string expectedSignature)
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static string Echo(string value) => value;
            }
            """,
            nullableContext);

        Assert.Equal(nullableContext.ToString(), result.Metadata.NullableContextOptions);
        Assert.True(
            result.CalorSource!.Contains(expectedSignature, StringComparison.Ordinal),
            result.CalorSource);
    }

    [Theory]
    [InlineData(
        NullableContextOptions.Enable,
        "#nullable disable",
        "(?str:value) -> ?str")]
    [InlineData(
        NullableContextOptions.Disable,
        "#nullable enable",
        "(str:value) -> str")]
    public void FileDirectiveOverridesEvaluatedProjectContext(
        NullableContextOptions projectContext,
        string directive,
        string expectedSignature)
    {
        var result = Convert(
            $$"""
            {{directive}}
            public static class Fixture
            {
                public static string Echo(string value) => value;
            }
            """,
            projectContext);

        Assert.True(
            result.CalorSource!.Contains(expectedSignature, StringComparison.Ordinal),
            result.CalorSource);
    }

    [Fact]
    public void NullableDisabledSafeInitializersRemainNonNullWhileUnknownInvocationIsNullable()
    {
        var result = Convert(
            """
            public sealed class Item { }
            public static class Fixture
            {
                public static string Probe()
                {
                    string literal = "safe";
                    Item created = new Item();
                    var unknown = Read();
                    return literal;
                }

                private static string Read() => null;
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{str:literal} \"safe\"", result.CalorSource);
        Assert.Contains("§B{Item:created} §NEW{Item}", result.CalorSource);
        Assert.Contains("§B{?str:unknown}", result.CalorSource);
        Assert.DoesNotContain("Option<", result.CalorSource);
        Assert.DoesNotContain("§NN", result.CalorSource);
        Assert.DoesNotContain("§UNWRAP", result.CalorSource);
    }

    [Fact]
    public void InvocationInitializedVarRetainsSupportedSemanticNullability()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static void Probe()
                {
                    var present = Read();
                    var absent = Maybe();
                }

                private static string Read() => "value";
                private static string? Maybe() => null;
            }
            """,
            NullableContextOptions.Enable);

        Assert.True(
            result.CalorSource!.Contains(
                "§B{str:present}",
                StringComparison.Ordinal),
            result.CalorSource);
        Assert.True(
            result.CalorSource.Contains(
                "§B{?str:absent}",
                StringComparison.Ordinal),
            result.CalorSource);
    }

    [Fact]
    public void InvocationInitializedVarRetainsFlowNullabilityAttributes()
    {
        var result = Convert(
            """
            using System.Diagnostics.CodeAnalysis;
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                [return: MaybeNull]
                private static string Read() => null;
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{?str:value}", result.CalorSource);
    }

    [Fact]
    public void InvocationInitializedVarRetainsNotNullFlowAttribute()
    {
        var result = Convert(
            """
            using System.Diagnostics.CodeAnalysis;
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                [return: NotNull]
                private static string? Read() => "value";
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{str:value}", result.CalorSource);
        Assert.DoesNotContain("§B{?str:value}", result.CalorSource);
    }

    [Fact]
    public void AsyncTaskResultRetainsObliviousReferenceNullability()
    {
        var result = Convert(
            """
            using System.Threading.Tasks;
            public static class Fixture
            {
                public static async Task<string> Read()
                {
                    await Task.Yield();
                    return null;
                }
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("() -> ?str", result.CalorSource);
    }

    [Fact]
    public void AsyncStreamContainerIsNotAnnotatedFromItsElement()
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Disable,
                ValidateRoundTripCSharp = false
            }).Convert(
                """
                using System.Collections.Generic;
                public static class Fixture
                {
                    public static async IAsyncEnumerable<string> Read()
                    {
                        yield return "value";
                        await System.Threading.Tasks.Task.Yield();
                    }
                }
                """,
                "Fixture.cs");

        Assert.Contains(
            "() -> IAsyncEnumerable<str>",
            result.CalorSource);
        Assert.DoesNotContain(
            "() -> ?IAsyncEnumerable<str>",
            result.CalorSource);
    }

    [Fact]
    public void ObliviousArrayDeclarationIsReportedAsUnsupportedNestedShape()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static string[] Read() => null;
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("() -> str[]", result.CalorSource);
        Assert.DoesNotContain("() -> str[]?", result.CalorSource);
        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "nullable-oblivious-nested-shape"
                && issue.Severity == ConversionIssueSeverity.Warning);
        Assert.DoesNotContain("Option<", result.CalorSource);
    }

    [Fact]
    public void SafeInitializerDoesNotNarrowReassignedObliviousLocal()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static string Probe()
                {
                    string value = "safe";
                    value = null;
                    return value;
                }
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{~value:?str} \"safe\"", result.CalorSource);
    }

    [Theory]
    [InlineData("Clear(out value);")]
    [InlineData("(value, _) = (null, 0);")]
    public void SafeInitializerDoesNotNarrowIndirectlyWrittenObliviousLocal(
        string write)
    {
        var result = Convert(
            $$"""
            public static class Fixture
            {
                public static string Probe()
                {
                    string value = "safe";
                    {{write}}
                    return value;
                }

                private static void Clear(out string value) => value = null;
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{~value:?str} \"safe\"", result.CalorSource);
    }

    [Fact]
    public void MemberAssignmentDoesNotInvalidateSafeReferenceInitializer()
    {
        var result = Convert(
            """
            public sealed class Item
            {
                public string Name { get; set; }
            }
            public static class Fixture
            {
                public static Item Probe()
                {
                    Item value = new Item();
                    value.Name = "updated";
                    return value;
                }
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{Item:value} §NEW{Item}", result.CalorSource);
        Assert.DoesNotContain("§B{?Item:value}", result.CalorSource);
    }

    [Fact]
    public void UnresolvedInvocationInitializedVarReportsNullableCoverageLoss()
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Enable,
                ValidateRoundTripCSharp = false,
                Fidelity = ConversionFidelity.Lossy
            }).Convert(
                """
                public static class Fixture
                {
                    public static void Probe()
                    {
                        var value = External.Read();
                    }
                }
                """,
                "Fixture.cs");

        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "nullable-semantic-unresolved"
                && issue.Severity == ConversionIssueSeverity.Warning);
    }

    [Fact]
    public void NullableValueInvocationRemainsValueNullable()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                private static int? Read() => null;
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{?i32:value}", result.CalorSource);
        Assert.DoesNotContain("§B{??i32:value}", result.CalorSource);
    }

    [Fact]
    public void ActualReferenceNullabilityDrivesInvocationVarAndPreservesRuntime()
    {
        var directory = CreateTestDirectory();
        var legacyPath = Path.Combine(directory, "Legacy.dll");
        try
        {
            EmitAssembly(
                legacyPath,
                "Legacy",
                """
                #nullable disable
                namespace Legacy;
                public static class Api
                {
                    public static string Read(bool present)
                        => present ? "value" : null;
                }
                """,
                NullableContextOptions.Disable);

            const string source = """
                #nullable enable
                public static class Consumer
                {
                    public static string? Probe(bool present)
                    {
                        var value = Legacy.Api.Read(present);
                        return value;
                    }
                }
                """;
            var sourceTree = CSharpSyntaxTree.ParseText(source);
            var sourceCompilation = CSharpCompilation.Create(
                "Issue1401SemanticControl",
                [sourceTree],
                GeneratedCSharpCompiler.References.Append(
                    MetadataReference.CreateFromFile(legacyPath)),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary)
                    .WithNullableContextOptions(
                        NullableContextOptions.Enable));
            var invocation = sourceTree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single();
            var invoked = Assert.IsAssignableFrom<IMethodSymbol>(
                sourceCompilation.GetSemanticModel(sourceTree)
                    .GetSymbolInfo(invocation).Symbol);
            Assert.Equal(
                Microsoft.CodeAnalysis.NullableAnnotation.None,
                invoked.ReturnType.NullableAnnotation);
            var options = new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Enable,
                References =
                [
                    new ConversionReference(legacyPath, Array.Empty<string>())
                ]
            };
            var result = new CSharpToCalorConverter(options)
                .Convert(source, "Consumer.cs");

            Assert.True(result.Success, FormatIssues(result));
            Assert.True(
                result.CalorSource!.Contains(
                    "§B{?str:value}",
                    StringComparison.Ordinal),
                result.CalorSource);
            Assert.DoesNotContain("Option<", result.CalorSource);
            Assert.DoesNotContain("default", result.CalorSource);
            Assert.DoesNotContain("§TH", result.CalorSource);
            Assert.DoesNotContain("§UNWRAP", result.CalorSource);

            var compiled = Program.Compile(
                result.CalorSource!,
                "Consumer.calr",
                new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy =
                        Calor.Compiler.Effects.UnknownCallPolicy.Permissive,
                    DeferGeneratedOutputValidation = true,
                    StatusWriter = TextWriter.Null
                });
            Assert.False(
                compiled.HasErrors,
                string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(compiled.GeneratedCode, "Consumer.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    References =
                    [
                        new GeneratedCSharpReference(
                            legacyPath,
                            Array.Empty<string>())
                    ],
                    NullableContextOptions = NullableContextOptions.Enable
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join(
                    Environment.NewLine,
                    validation.FormattedCompilationErrors));
            Assert.DoesNotContain("Calor.Runtime.Option", compiled.GeneratedCode);

            const string legacyRuntimeSource = """
                #nullable disable
                namespace Legacy;
                public static class Api
                {
                    public static string Read(bool present)
                        => present ? "value" : null;
                }
                """;
            var original = EmitInMemory(
                source,
                legacyRuntimeSource,
                NullableContextOptions.Enable);
            var converted = EmitInMemory(
                compiled.GeneratedCode,
                legacyRuntimeSource,
                NullableContextOptions.Enable);

            Assert.Equal(
                InvokeProbe(original, false),
                InvokeProbe(converted, false));
            Assert.Equal(
                InvokeProbe(original, true),
                InvokeProbe(converted, true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnresolvedDeclarationRetainsExplicitDiagnostic()
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Enable,
                ValidateRoundTripCSharp = false,
                Fidelity = ConversionFidelity.Lossy
            }).Convert(
                """
                public static class Fixture
                {
                    public static Missing Read() => External.Read();
                }
                """,
                "Fixture.cs");

        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "nullable-semantic-unresolved"
                && issue.Severity == ConversionIssueSeverity.Warning);
    }

    [Fact]
    public async Task ProjectMigratorCarriesEvaluatedNullableContextAndReferences()
    {
        var directory = CreateTestDirectory();
        try
        {
            var projectPath = Path.Combine(
                directory,
                "ContextFixture.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Fixture.cs"),
                """
                public static class Fixture
                {
                    public static string Echo(string value) => value;
                }
                """);
            await RestoreProjectAsync(projectPath);
            var inputs = Assert.Single(
                await ProjectMigrator.ResolveProjectCompilationInputsAsync(
                    projectPath,
                    "Debug",
                    selectedTargetFramework: null,
                    CancellationToken.None));
            var direct = new CSharpToCalorConverter(
                new ConversionOptions
                {
                    NullableContextOptions = inputs.NullableContextOptions,
                    ReferencesAreComplete = true,
                    References = inputs.ReferencePaths.Select(path =>
                        new ConversionReference(
                            path,
                            inputs.ReferenceAliases.TryGetValue(
                                path,
                                out var aliases)
                                ? aliases
                                : Array.Empty<string>()))
                        .ToArray()
                }).Convert(
                    await File.ReadAllTextAsync(
                        Path.Combine(directory, "Fixture.cs")),
                    Path.Combine(directory, "Fixture.cs"));
            Assert.True(direct.Success, FormatIssues(direct));
            Assert.Contains("(?str:value) -> ?str", direct.CalorSource);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    SkipAnalyze = true,
                    SkipVerify = true,
                    ValidateOutput = true
                });
            var plan = await migrator.CreatePlanAsync(
                projectPath,
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var file = Assert.Single(
                report.FileResults,
                result => result.SourcePath.EndsWith(
                    "Fixture.cs",
                    StringComparison.Ordinal));

            Assert.Contains(
                file.Status,
                new[]
                {
                    FileMigrationStatus.Success,
                    FileMigrationStatus.Partial
                });
            Assert.DoesNotContain(
                file.Issues,
                issue => issue.Severity == ConversionIssueSeverity.Error);
            Assert.Equal(
                NullableContextOptions.Disable.ToString(),
                file.Metadata!.NullableContextOptions);
            Assert.NotEmpty(file.Metadata.References);
            Assert.True(
                file.ConvertedSource!.Contains(
                    "(?str:value) -> ?str",
                    StringComparison.Ordinal),
                file.ConvertedSource);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigratorAllowsAnnotationsUnderDisabledDirectiveWithWarningsAsErrors()
    {
        var directory = CreateTestDirectory();
        try
        {
            var projectPath = Path.Combine(directory, "DirectiveFixture.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Fixture.cs"),
                """
                #nullable disable // legacy source
                public static class Fixture
                {
                    public static string Echo(string value) => value;
                }
                """);
            await RestoreProjectAsync(projectPath);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    SkipAnalyze = true,
                    SkipVerify = true,
                    ValidateOutput = true
                });
            var plan = await migrator.CreatePlanAsync(
                projectPath,
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var file = Assert.Single(
                report.FileResults,
                result => result.SourcePath.EndsWith(
                    "Fixture.cs",
                    StringComparison.Ordinal));

            Assert.Contains(
                file.Status,
                new[]
                {
                    FileMigrationStatus.Success,
                    FileMigrationStatus.Partial
                });
            Assert.DoesNotContain(
                file.Issues,
                issue => issue.Severity == ConversionIssueSeverity.Error);
            Assert.Contains("(?str:value) -> ?str", file.ConvertedSource);
            var compiled = Program.Compile(
                file.ConvertedSource!,
                "Fixture.calr",
                new CompilationOptions
                {
                    EnforceEffects = false,
                    DeferGeneratedOutputValidation = true,
                    StatusWriter = TextWriter.Null
                });
            Assert.False(
                compiled.HasErrors,
                string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            Assert.Contains("#nullable disable warnings", compiled.GeneratedCode);
            Assert.Contains("#nullable enable annotations", compiled.GeneratedCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigratorResolvesInvocationFromSiblingSource()
    {
        var directory = CreateTestDirectory();
        try
        {
            var projectPath = Path.Combine(directory, "SiblingFixture.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Api.cs"),
                """
                public static class Api
                {
                    public static Task<string?> Read() => Task.FromResult<string?>(null);
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Consumer.cs"),
                """
                public static class Consumer
                {
                    public static void Probe()
                    {
                        var value = Api.Read();
                    }
                }
                """);
            await RestoreProjectAsync(projectPath);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    SkipAnalyze = true,
                    SkipVerify = true,
                    ValidateOutput = true
                });
            var plan = await migrator.CreatePlanAsync(
                projectPath,
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var consumer = Assert.Single(
                report.FileResults,
                result => result.SourcePath.EndsWith(
                    "Consumer.cs",
                    StringComparison.Ordinal));

            Assert.DoesNotContain(
                consumer.Issues,
                issue => issue.Feature == "nullable-semantic-unresolved");
            Assert.Contains("§B{Task<?str>:value}", consumer.ConvertedSource);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ConversionResult Convert(
        string source,
        NullableContextOptions nullableContext)
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = nullableContext
            }).Convert(source, "Fixture.cs");
        Assert.True(result.Success, FormatIssues(result));
        return result;
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "issue-1401-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void EmitAssembly(
        string path,
        string assemblyName,
        string source,
        NullableContextOptions nullableContext)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullableContext));
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }

    private static async Task RestoreProjectAsync(string projectPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity:quiet");
        using var process = Process.Start(startInfo)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            (await outputTask) + Environment.NewLine + await errorTask);
    }

    private static Assembly EmitInMemory(
        string source,
        string additionalSource,
        NullableContextOptions nullableContext)
    {
        var compilation = CSharpCompilation.Create(
            "Issue1401_" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText(additionalSource)
            ],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullableContext));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? InvokeProbe(Assembly assembly, bool present)
        => assembly.GetType("Consumer")!
            .GetMethod("Probe")!
            .Invoke(null, [present]);

    private static string FormatIssues(ConversionResult result)
        => string.Join(
            Environment.NewLine,
            result.Issues.Select(issue =>
                $"{issue.Severity}: {issue.Feature}: {issue.Message}"));
}
