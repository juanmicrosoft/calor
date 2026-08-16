using Calor.Compiler.Migration;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

public sealed class RoundTripPipelineSafetyTests
{
    [Fact]
    public async Task HarnessConversion_UsesExplicitSelectedModeAndEvaluatedSymbols()
    {
        var root = CreateProject(
            """
            #if FEATURE
            public static class Selected
            {
                public static int Read() => 1;
            }
            #else
            public record Fallback(int Value);
            #endif
            """);
        try
        {
            var project = Path.Combine(root, "Safety.csproj");
            await File.WriteAllTextAsync(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="Lib/Lib.csproj" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Lib", "Lib.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>netstandard2.0;net6.0</TargetFrameworks>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(TargetFramework)' == 'net6.0'">
                    <DefineConstants>FEATURE</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup><Compile Include="**/*.cs" /></ItemGroup>
                </Project>
                """);
            var sourcePath = Path.Combine(root, "Lib", "Invalid.cs");
            var config = new RoundTripConfig
            {
                ProjectName = "SelectedMode",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Lib",
                SolutionOrProjectFile = "Safety.csproj",
                TargetFramework = "net10.0",
                Configuration = "Release",
            };
            var restore = await ProcessRunner.RunAsync(
                config.DotnetPath,
                "restore \"Safety.csproj\" --verbosity quiet",
                root,
                config.BuildTimeout);
            Assert.Equal(0, restore.ExitCode);
            var contexts = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);
            Assert.Single(contexts);
            var context = contexts[
                ProjectParseContextResolver.Canonicalize(sourcePath)];
            Assert.Equal("net6.0", context.TargetFramework);
            Assert.Equal(
                "net471",
                ProjectParseContextResolver.SelectCompatibleTargetFramework(
                    ["net471", "netstandard2.0"],
                    "net48"));
            Assert.Contains(
                "FEATURE",
                context.ParseOptions.PreprocessorSymbolNames);
            var options = RoundTripPipeline.CreateHarnessConversionOptions(
                config,
                context);

            Assert.Equal(
                ConversionFidelity.Lossy,
                options.Fidelity);
            Assert.Equal(
                PreprocessorConversionMode.SelectActiveBranchLossy,
                options.PreprocessorMode);
            var selected = new CSharpToCalorConverter(options)
                .Convert(
                    await File.ReadAllTextAsync(sourcePath),
                    sourcePath);
            Assert.True(selected.Success);
            Assert.Contains("Selected", selected.CalorSource);
            Assert.DoesNotContain("Fallback", selected.CalorSource);
            Assert.Equal(
                PreprocessorConversionMode.SelectActiveBranchLossy,
                selected.Metadata.PreprocessorMode);
            Assert.Equal("Release", selected.Metadata.Configuration);
            Assert.Equal("net6.0", selected.Metadata.TargetFramework);
            Assert.Contains("FEATURE", selected.Metadata.DefinedSymbols);
            Assert.Contains(
                selected.Losses,
                loss => loss.Kind
                    == ConversionLossKind.PreprocessorStripped);
            var measured = new FileConversionResult
            {
                FilePath = "Lib/Invalid.cs",
                Status = FileStatus.Replaced,
                PreprocessorMode =
                    selected.Metadata.PreprocessorMode.ToString()
            };
            measured.ApplyLossLedger(
                [
                    .. selected.Losses,
                    new ConversionLoss
                    {
                        Kind = ConversionLossKind.InteropPreserved,
                        Feature = "pragma",
                        Description = "active pragma retained"
                    },
                    new ConversionLoss
                    {
                        Kind = ConversionLossKind.DirectiveRemoved,
                        Feature = "nullable-directive",
                        Description = "inactive directive removed"
                    }
                ]);
            Assert.False(measured.ConvertedNative);
            Assert.Equal(
                "Release",
                Calor.RoundTrip.Harness.TaskGen.TaskGenerator.Clone(
                    config,
                    workDir: null).Configuration);

            var preserved = new CSharpToCalorConverter()
                .Convert(
                    await File.ReadAllTextAsync(sourcePath),
                    sourcePath);
            Assert.True(preserved.Success);
            Assert.Equal(
                PreprocessorConversionMode.PreserveAllBranches,
                preserved.Metadata.PreprocessorMode);
            Assert.Contains("§PP{FEATURE}", preserved.CalorSource);
            Assert.Contains("Selected", preserved.CalorSource);
            Assert.Contains("Fallback", preserved.CalorSource);

            var report = new RoundTripReport
            {
                ProjectName = "SelectedMode"
            };
            var pipelineResults =
                await new RoundTripPipeline().ConvertAndReplaceAsync(
                    root,
                    config,
                    report);
            var pipelineFile = Assert.Single(pipelineResults);
            Assert.Equal(
                "SelectActiveBranchLossy",
                pipelineFile.PreprocessorMode);
            Assert.Equal("Release", pipelineFile.Configuration);
            Assert.Equal("net6.0", pipelineFile.TargetFramework);
            Assert.Contains("FEATURE", pipelineFile.DefinedSymbols);
            Assert.Single(report.EvaluatedParseContexts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseContextResolver_RejectsUnavailableConfiguredRootFramework()
    {
        var root = CreateProject("public class RootType { }");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Safety.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Lib/**/*.cs" />
                  </ItemGroup>
                </Project>
                """);
            var config = new RoundTripConfig
            {
                ProjectName = "UnavailableFramework",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Lib",
                SolutionOrProjectFile = "Safety.csproj",
                TargetFramework = "net10.0",
                Configuration = "Release",
            };

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProjectParseContextResolver.ResolveAsync(
                    root,
                    config,
                    [Path.Combine(root, "Lib", "Invalid.cs")],
                    CancellationToken.None));

            Assert.Contains(
                "is not declared by root project",
                error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseContextResolver_RejectsMissingConfiguredProject()
    {
        var root = CreateProject("public class MissingProjectContext { }");
        try
        {
            var config = new RoundTripConfig
            {
                ProjectName = "MissingProject",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Lib",
                SolutionOrProjectFile = "Missing.csproj",
                TargetFramework = "net10.0",
                Configuration = "Release",
            };

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProjectParseContextResolver.ResolveAsync(
                    root,
                    config,
                    [Path.Combine(root, "Lib", "Invalid.cs")],
                    CancellationToken.None));

            Assert.Contains(
                "does not exist",
                error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseContextResolver_UsesEffectiveProjectReferenceGlobalProperties()
    {
        var root = CreateProject(
            """
            #if !CHILD_SPECIAL
            #error child-configuration-not-applied
            #endif
            #if !PLATFORM_EDGE
            #error child-platform-not-applied
            #endif
            #if !EDGE_ON
            #error child-additional-properties-not-applied
            #endif
            #if !ROOT_REMOVED
            #error child-global-property-not-removed
            #endif
            #if !EDGE_ESCAPED
            #error child-escaped-property-not-applied
            #endif
            #if !ROOT_QUOTED
            #error quoted-root-property-not-applied
            #endif
            public static class SelectedChildBranch
            {
                public static int Read() => 42;
            }
            """);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Root.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                    <CurrentSolutionConfigurationContents><![CDATA[
                      <SolutionConfiguration>
                        <ProjectConfiguration
                          Project="{11111111-1111-1111-1111-111111111111}"
                          AbsolutePath="$(MSBuildThisFileDirectory)Lib/Lib.csproj"
                          BuildProjectInSolution="True">
                          ChildSpecial|EdgePlatform
                        </ProjectConfiguration>
                      </SolutionConfiguration>
                    ]]></CurrentSolutionConfigurationContents>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="Lib/Lib.csproj">
                      <AdditionalProperties>EdgeSymbol=EDGE_ON;Flavor=alpha%3Bbeta</AdditionalProperties>
                      <GlobalPropertiesToRemove>RootOnly</GlobalPropertiesToRemove>
                      <UndefineProperties>RootOnly</UndefineProperties>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Lib", "Lib.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ProjectGuid>{11111111-1111-1111-1111-111111111111}</ProjectGuid>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'ChildSpecial'">
                    <DefineConstants>$(DefineConstants);CHILD_SPECIAL</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Platform)' == 'EdgePlatform'">
                    <DefineConstants>$(DefineConstants);PLATFORM_EDGE</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(EdgeSymbol)' == 'EDGE_ON'">
                    <DefineConstants>$(DefineConstants);EDGE_ON</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(RootOnly)' == ''">
                    <DefineConstants>$(DefineConstants);ROOT_REMOVED</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Flavor)' == 'alpha;beta'">
                    <DefineConstants>$(DefineConstants);EDGE_ESCAPED</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(RootFlavor)' == 'A B'">
                    <DefineConstants>$(DefineConstants);ROOT_QUOTED</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Invalid.cs" />
                  </ItemGroup>
                </Project>
                """);
            var config = new RoundTripConfig
            {
                ProjectName = "ReferenceProperties",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Lib",
                SolutionOrProjectFile = "Root.csproj",
                TargetFramework = "net10.0",
                Configuration = "Release",
                ExtraBuildProperties =
                    "-p:RootOnly=1 -p:\"RootFlavor=A B\"",
            };
            var restore = await ProcessRunner.RunAsync(
                config.DotnetPath,
                "restore \"Root.csproj\" --verbosity quiet "
                + "-p:RootOnly=1 -p:\"RootFlavor=A B\"",
                root,
                config.BuildTimeout);
            Assert.Equal(0, restore.ExitCode);
            var build = await ProcessRunner.RunAsync(
                config.DotnetPath,
                "build \"Root.csproj\" --no-restore --verbosity quiet "
                + "-p:RootOnly=1 -p:\"RootFlavor=A B\"",
                root,
                config.BuildTimeout);
            Assert.True(
                build.ExitCode == 0,
                string.Join("\n", build.Stdout, build.Stderr));

            var sourcePath = Path.Combine(root, "Lib", "Invalid.cs");
            var contexts = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);
            var context = Assert.Single(contexts).Value;
            Assert.Equal("ChildSpecial", context.Configuration);
            Assert.Equal("net10.0", context.TargetFramework);
            Assert.Contains(
                "CHILD_SPECIAL",
                context.ParseOptions.PreprocessorSymbolNames);
            Assert.Contains(
                "PLATFORM_EDGE",
                context.ParseOptions.PreprocessorSymbolNames);
            Assert.Contains(
                "EDGE_ON",
                context.ParseOptions.PreprocessorSymbolNames);
            Assert.Contains(
                "ROOT_REMOVED",
                context.ParseOptions.PreprocessorSymbolNames);
            Assert.Contains(
                "EDGE_ESCAPED",
                context.ParseOptions.PreprocessorSymbolNames);
            Assert.Contains(
                "ROOT_QUOTED",
                context.ParseOptions.PreprocessorSymbolNames);

            var conversion = new CSharpToCalorConverter(
                    RoundTripPipeline.CreateHarnessConversionOptions(
                        config,
                        context))
                .Convert(
                    await File.ReadAllTextAsync(sourcePath),
                    sourcePath);
            Assert.True(conversion.Success);
            Assert.Contains(
                "SelectedChildBranch",
                conversion.CalorSource);
            Assert.DoesNotContain(
                "child-configuration-not-applied",
                conversion.CalorSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TypeInvalidGeneratedOutput_IsNotWritten()
    {
        var root = CreateProject(
            """
            public static class Invalid
            {
                public static int Read() => "wrong";
            }
            """);
        try
        {
            var config = CreateConfig(root);
            var report = new RoundTripReport { ProjectName = "Safety" };
            var sourcePath = Path.Combine(root, "Lib", "Invalid.cs");
            var original = await File.ReadAllTextAsync(sourcePath);

            var results = await new RoundTripPipeline().ConvertAndReplaceAsync(
                root, config, report);

            Assert.NotEqual(FileStatus.Replaced, Assert.Single(results).Status);
            Assert.Equal(original, await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildOutputSources_AreNotCandidatesOrCoverageExclusions()
    {
        var root = CreateProject("public static class A { public static int Read() => 1; }");
        try
        {
            var generatedDirectory = Path.Combine(root, "Lib", "obj", "Debug", "net10.0");
            Directory.CreateDirectory(generatedDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(generatedDirectory, "Lib.AssemblyInfo.cs"),
                "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]");
            var report = new RoundTripReport { ProjectName = "BuildOutputs" };

            var results = await new RoundTripPipeline().ConvertAndReplaceAsync(
                root,
                CreateConfig(root),
                report);

            Assert.Single(results);
            Assert.Equal(0, report.ExcludedFileCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AggregateValidation_PublishesOnlyCompilableSurvivors()
    {
        var root = CreateProject("public static class A { public static int Read() => 1; }");
        try
        {
            var secondPath = Path.Combine(root, "Lib", "B.cs");
            const string originalSecond =
                "public static class B { public static int Read() => 2; }";
            await File.WriteAllTextAsync(secondPath, originalSecond);
            var results = new List<FileConversionResult>
            {
                new()
                {
                    FilePath = "Lib/Invalid.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp =
                        "public static class A { public static int Read() => 3; }"
                },
                new()
                {
                    FilePath = "Lib/B.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp =
                        "public static class B { public static int Read() => \"wrong\"; }"
                },
            };

            await RoundTripPipeline.ValidateAndPublishGeneratedFilesAsync(
                root,
                results,
                Directory.GetFiles(Path.Combine(root, "Lib"), "*.cs"),
                CancellationToken.None);

            Assert.Equal(FileStatus.Replaced, results[0].Status);
            Assert.Contains(
                "=> 3",
                await File.ReadAllTextAsync(Path.Combine(root, "Lib", "Invalid.cs")));
            Assert.Equal(FileStatus.EmitCompilationError, results[1].Status);
            Assert.Equal(originalSecond, await File.ReadAllTextAsync(secondPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AggregateValidation_RejectsGeneratedTypeThatBreaksUnchangedConsumer()
    {
        var root = CreateProject(
            """
            [Marker]
            public static class A { }
            """);
        try
        {
            var markerPath = Path.Combine(root, "Lib", "Marker.cs");
            const string originalMarker =
                "public sealed class MarkerAttribute : System.Attribute { }";
            await File.WriteAllTextAsync(markerPath, originalMarker);
            var goodPath = Path.Combine(root, "Lib", "Good.cs");
            await File.WriteAllTextAsync(
                goodPath,
                "public static class Good { public static int Read() => 1; }");
            var results = new List<FileConversionResult>
            {
                new()
                {
                    FilePath = "Lib/Marker.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp = "public sealed class MarkerAttribute { }"
                },
                new()
                {
                    FilePath = "Lib/Good.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp =
                        "public static class Good { public static int Read() => 2; }"
                },
            };

            await RoundTripPipeline.ValidateAndPublishGeneratedFilesAsync(
                root,
                results,
                Directory.GetFiles(Path.Combine(root, "Lib"), "*.cs"),
                CancellationToken.None);

            Assert.Equal(FileStatus.EmitCompilationError, results[0].Status);
            Assert.Equal(originalMarker, await File.ReadAllTextAsync(markerPath));
            Assert.Equal(FileStatus.Replaced, results[1].Status);
            Assert.Contains("=> 2", await File.ReadAllTextAsync(goodPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectValidation_UsesActualProjectSettingsBeforePublication()
    {
        var root = CreateProject(
            """
            public static class Conditional
            {
                public static int Read() => 1;
            }
            """);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Safety.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <DefineConstants>FEATURE_ENABLED</DefineConstants>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Lib/**/*.cs" />
                  </ItemGroup>
                </Project>
                """);
            var sourcePath = Path.Combine(root, "Lib", "Invalid.cs");
            var original = await File.ReadAllTextAsync(sourcePath);
            var results = new List<FileConversionResult>
            {
                new()
                {
                    FilePath = "Lib/Invalid.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp =
                        """
                        public static class Conditional
                        {
                        #if FEATURE_ENABLED
                            public static int Read() => "wrong";
                        #else
                            public static int Read() => 1;
                        #endif
                        }
                        """
                },
            };
            var config = new RoundTripConfig
            {
                ProjectName = "ProjectSettings",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Lib",
                SolutionOrProjectFile = "Safety.csproj",
            };

            await new RoundTripPipeline().ValidateAndPublishProjectCandidatesAsync(
                root,
                config,
                results,
                CancellationToken.None);

            Assert.Equal(FileStatus.EmitCompilationError, results[0].Status);
            Assert.Equal(original, await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectValidation_PrefersDirectErrorPathOverMentionedType()
    {
        var root = CreateProject(
            "public static class A { public static B Read() => new(); }");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Lib", "B.cs"),
                "public sealed class B { public int Value => 1; }");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Safety.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Lib/**/*.cs" />
                  </ItemGroup>
                </Project>
                """);
            var results = new List<FileConversionResult>
            {
                new()
                {
                    FilePath = "Lib/Invalid.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp =
                        "public static class A { public static B Read() => \"wrong\"; }"
                },
                new()
                {
                    FilePath = "Lib/B.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp =
                        "public sealed class B { public int Value => 2; }"
                },
            };
            var config = new RoundTripConfig
            {
                ProjectName = "DirectAttribution",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Lib",
                SolutionOrProjectFile = "Safety.csproj",
            };

            await new RoundTripPipeline().ValidateAndPublishProjectCandidatesAsync(
                root,
                config,
                results,
                CancellationToken.None);

            Assert.Equal(FileStatus.EmitCompilationError, results[0].Status);
            Assert.Equal(FileStatus.Replaced, results[1].Status);
            Assert.Contains(
                "Value => 2",
                await File.ReadAllTextAsync(Path.Combine(root, "Lib", "B.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Converter_ObservesPreCanceledToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var converter = new CSharpToCalorConverter();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            converter.Convert(
                "public static class A { }",
                "A.cs",
                cancellation.Token));
    }

    [Fact]
    public async Task AggregateValidation_IgnoresSourcesOutsideConfiguredLibrary()
    {
        var root = CreateProject("public static class A { public static int Read() => 1; }");
        try
        {
            var unrelatedDirectory = Path.Combine(root, "Tests");
            Directory.CreateDirectory(unrelatedDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(unrelatedDirectory, "Broken.cs"),
                "public static class Broken { public static int Read() => \"wrong\"; }");
            var results = new List<FileConversionResult>
            {
                new()
                {
                    FilePath = "Lib/Invalid.cs",
                    Status = FileStatus.Replaced,
                    EmittedCSharp =
                        "public static class A { public static int Read() => 2; }"
                },
            };

            await RoundTripPipeline.ValidateAndPublishGeneratedFilesAsync(
                root,
                results,
                Directory.GetFiles(Path.Combine(root, "Lib"), "*.cs"),
                CancellationToken.None);

            Assert.Equal(FileStatus.Replaced, results[0].Status);
            Assert.Contains(
                "=> 2",
                await File.ReadAllTextAsync(Path.Combine(root, "Lib", "Invalid.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectReferenceDiscovery_ExcludesPlatformAndSubjectAssemblies()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"calor-roundtrip-references-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var subjectAssembly = typeof(RoundTripPipelineSafetyTests).Assembly.Location;
            var subjectAssemblyName = System.Reflection.AssemblyName
                .GetAssemblyName(subjectAssembly)
                .Name!;
            File.WriteAllText(
                Path.Combine(root, "Subject.csproj"),
                $"<Project><PropertyGroup><AssemblyName>{subjectAssemblyName}</AssemblyName></PropertyGroup></Project>");
            var output = Path.Combine(root, "bin");
            Directory.CreateDirectory(output);
            File.Copy(
                subjectAssembly,
                Path.Combine(output, "Subject.Custom.dll"));
            File.Copy(
                typeof(object).Assembly.Location,
                Path.Combine(output, "System.Private.CoreLib.dll"));

            var references = RoundTripPipeline.DiscoverProjectReferencePaths(root);

            Assert.Empty(references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CandidateReferenceDetection_MatchesDeclaredTypeInBuildError()
    {
        var candidate = new FileConversionResult
        {
            FilePath = "CustomDefaultMethodImplementationAttribute.cs",
            Status = FileStatus.Replaced,
            EmittedCSharp =
                """
                #if FEATURE_DEFAULT_INTERFACE
                namespace CustomDefaultMethodImplementationAttribute
                {
                    internal sealed class CustomDefaultMethodImplementationAttribute
                    {
                    }
                }
                #endif
                """
        };

        Assert.True(RoundTripPipeline.CandidateIsReferenced(
            candidate,
            "error CS0616: 'CustomDefaultMethodImplementationAttribute' is not an attribute class"));
    }

    [Fact]
    public async Task InteropPreservedFile_IsCountedAsNonNative()
    {
        var root = CreateProject("public record Person(string Name);");
        try
        {
            var config = CreateConfig(root);
            var report = new RoundTripReport { ProjectName = "Interop" };

            var result = Assert.Single(
                await new RoundTripPipeline().ConvertAndReplaceAsync(
                    root, config, report));

            Assert.Equal(FileStatus.Replaced, result.Status);
            Assert.True(result.InteropBlocks > 0);
            Assert.False(result.ConvertedNative);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationAfterConversionStarts_StopsRemainingFiles()
    {
        var root = CreateProject("public static class Seed { public static int Value => 1; }");
        try
        {
            var library = Path.Combine(root, "Lib");
            for (var i = 0; i < 100; i++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(library, $"Type{i:D3}.cs"),
                    $"public static class Type{i:D3} {{ public static int Value => {i}; }}");
            }

            var firstPath = Path.Combine(library, "Invalid.cs");
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var baseConfig = CreateConfig(root);
            var config = new RoundTripConfig
            {
                ProjectName = baseConfig.ProjectName,
                OriginalProjectPath = baseConfig.OriginalProjectPath,
                LibrarySourceRelativePath = baseConfig.LibrarySourceRelativePath,
                SolutionOrProjectFile = baseConfig.SolutionOrProjectFile,
                LooseDirectoryMode = baseConfig.LooseDirectoryMode,
                FileConversionStarted = _ => started.TrySetResult(),
            };
            using var cancellation = new CancellationTokenSource();
            var pipelineTask = new RoundTripPipeline().ConvertAndReplaceAsync(
                root,
                config,
                new RoundTripReport { ProjectName = "Cancellation" },
                cancellation.Token);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipelineTask);

            Assert.Equal(
                "public static class Seed { public static int Value => 1; }",
                await File.ReadAllTextAsync(firstPath));
            var unchanged = Enumerable.Range(0, 100).Count(i =>
                File.ReadAllText(Path.Combine(library, $"Type{i:D3}.cs")) ==
                $"public static class Type{i:D3} {{ public static int Value => {i}; }}");
            Assert.True(unchanged > 0, "cancellation should stop before every file is converted");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RoundTripConfig CreateConfig(string root) => new()
    {
        ProjectName = "Safety",
        OriginalProjectPath = root,
        LibrarySourceRelativePath = "Lib",
        SolutionOrProjectFile = "unused.csproj",
        LooseDirectoryMode = true,
    };

    private static string CreateProject(string source)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"calor-roundtrip-safety-{Guid.NewGuid():N}");
        var library = Path.Combine(root, "Lib");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "Invalid.cs"), source);
        return root;
    }
}
