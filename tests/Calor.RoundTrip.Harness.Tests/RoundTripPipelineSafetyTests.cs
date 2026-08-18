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
            var context = Assert.IsType<ResolvedProjectFileParseContext>(
                contexts[
                    ProjectParseContextResolver.Canonicalize(sourcePath)])
                .Context;
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
                ParseContextProjectFile = "Lib/Lib.csproj",
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
            var context = Assert.IsType<ResolvedProjectFileParseContext>(
                Assert.Single(contexts).Value).Context;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseContextResolver_CollapsesDuplicateIdenticalGraphPaths(
        bool reverseTraversalOrder)
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sharedSource = await CreateDiamondProjectGraphAsync(
                root,
                differingContexts: false,
                reverseTraversalOrder: reverseTraversalOrder);
            var config = CreateGraphConfig(root);
            var restore = await ProcessRunner.RunAsync(
                config.DotnetPath,
                "restore \"Root.csproj\" --verbosity quiet",
                root,
                config.BuildTimeout);
            Assert.Equal(0, restore.ExitCode);

            var contexts = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sharedSource],
                CancellationToken.None);

            var context = Assert.IsType<ResolvedProjectFileParseContext>(
                Assert.Single(contexts).Value).Context;
            Assert.True(context.Provenance.Count >= 2);
            Assert.All(
                context.Provenance,
                provenance => Assert.Equal(
                    ProjectParseContextResolver.Canonicalize(
                        Path.Combine(root, "Leaf", "Leaf.csproj")),
                    provenance.ProjectGraphPath[^1]));
            Assert.Contains(
                context.Provenance,
                provenance => provenance.ProjectGraphPath.Any(path =>
                    path.EndsWith(
                        "/A/A.csproj",
                        StringComparison.Ordinal)));
            Assert.Contains(
                context.Provenance,
                provenance => provenance.ProjectGraphPath.Any(path =>
                    path.EndsWith(
                        "/B/B.csproj",
                        StringComparison.Ordinal)));
            Assert.Equal(
                Microsoft.CodeAnalysis.DocumentationMode.Diagnose,
                context.ParseOptions.DocumentationMode);
            Assert.Contains(
                context.ParseOptions.Features,
                feature => feature.Key == "strict"
                    && feature.Value == "true");
            Assert.NotEmpty(context.AdditionalInputHashes);
            Assert.Equal(
                "A",
                context.GlobalProperties["Irrelevant"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseContextResolver_RejectsMateriallyDifferentDuplicateContexts(
        bool reverseTraversalOrder)
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sharedSource = await CreateDiamondProjectGraphAsync(
                root,
                differingContexts: true,
                reverseTraversalOrder: reverseTraversalOrder);
            var config = CreateGraphConfig(root);
            var restore = await ProcessRunner.RunAsync(
                config.DotnetPath,
                "restore \"Root.csproj\" --verbosity quiet",
                root,
                config.BuildTimeout);
            Assert.Equal(0, restore.ExitCode);

            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sharedSource],
                CancellationToken.None);
            var ambiguity = Assert.IsType<AmbiguousProjectFileParseContext>(
                Assert.Single(resolutions).Value);
            var error = ambiguity.Diagnostic;

            Assert.True(
                error.Contains(
                    "materially different evaluated parse/reference contexts",
                    StringComparison.Ordinal),
                error);
            Assert.Contains("/A/A.csproj", error, StringComparison.Ordinal);
            Assert.Contains("globals=", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseContextResolver_RejectsOwnerAndLinkedSemanticDifference()
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            foreach (var directory in new[] { "Owner", "Linked" })
                Directory.CreateDirectory(Path.Combine(root, directory));
            await File.WriteAllTextAsync(
                Path.Combine(root, "Root.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="Owner/Owner.csproj" />
                    <ProjectReference Include="Linked/Linked.csproj" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Owner", "Owner.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <DefineConstants>$(DefineConstants);OWNER_CONTEXT</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Linked", "Linked.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <DefineConstants>$(DefineConstants);LINKED_CONTEXT</DefineConstants>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="../Owner/Shared.cs"
                      Link="Shared.cs" />
                  </ItemGroup>
                </Project>
                """);
            var sourcePath = Path.Combine(root, "Owner", "Shared.cs");
            await File.WriteAllTextAsync(
                sourcePath,
                """
                #if OWNER_CONTEXT
                public class OwnerVersion { }
                #elif LINKED_CONTEXT
                public class LinkedVersion { }
                #endif
                """);
            var config = new RoundTripConfig
            {
                ProjectName = "OwnerLinked",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Owner",
                SolutionOrProjectFile = "Root.csproj",
                TargetFramework = "net10.0",
                Configuration = "Release",
            };
            var restore = await ProcessRunner.RunAsync(
                config.DotnetPath,
                "restore \"Root.csproj\" --verbosity quiet",
                root,
                config.BuildTimeout);
            Assert.Equal(0, restore.ExitCode);

            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);
            var error = Assert.IsType<AmbiguousProjectFileParseContext>(
                Assert.Single(resolutions).Value).Diagnostic;

            Assert.Contains(
                "materially different evaluated parse/reference contexts",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "OWNER_CONTEXT",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "LINKED_CONTEXT",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseContextResolver_RootTargetFrameworkDoesNotFilterChildFramework()
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sourcePath = await CreateSelectedMultiTfmGraphAsync(
                root,
                includeNet10State: false,
                includeSecondNet10State: false,
                includeNetstandardState: true,
                reverseTraversalOrder: false);
            var config = CreateSelectedMultiTfmConfig(
                root,
                targetFramework: "net10.0");

            var contexts = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);

            var context = Assert.Single(contexts).Value;
            Assert.Equal("netstandard2.0", context.TargetFramework);
            Assert.Contains(
                "TFM_NETSTANDARD",
                context.ParseOptions.PreprocessorSymbolNames);
            Assert.DoesNotContain(
                "TFM_NET10",
                context.ParseOptions.PreprocessorSymbolNames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseContextResolver_SelectedProjectIncludesAllReachableFrameworks(
        bool reverseTraversalOrder)
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sourcePath = await CreateSelectedMultiTfmGraphAsync(
                root,
                includeNet10State: true,
                includeSecondNet10State: false,
                includeNetstandardState: true,
                reverseTraversalOrder: reverseTraversalOrder);
            var config = CreateSelectedMultiTfmConfig(
                root,
                targetFramework: "net10.0");

            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);
            var error = Assert.IsType<AmbiguousProjectFileParseContext>(
                Assert.Single(resolutions).Value).Diagnostic;

            Assert.Contains(
                "materially different evaluated parse/reference contexts",
                error,
                StringComparison.Ordinal);
            Assert.Contains("TFM_NET10", error, StringComparison.Ordinal);
            Assert.Contains("TFM_NETSTANDARD", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseContextResolver_SelectedProjectRejectsDifferingSameTfmStates(
        bool reverseTraversalOrder)
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sourcePath = await CreateSelectedMultiTfmGraphAsync(
                root,
                includeNet10State: true,
                includeSecondNet10State: true,
                includeNetstandardState: false,
                reverseTraversalOrder: reverseTraversalOrder);
            var config = CreateSelectedMultiTfmConfig(
                root,
                targetFramework: "net10.0");

            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);
            var error = Assert.IsType<AmbiguousProjectFileParseContext>(
                Assert.Single(resolutions).Value).Diagnostic;
            Assert.Contains("FLAVOR_A", error, StringComparison.Ordinal);
            Assert.Contains("FLAVOR_B", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseContextResolver_DeeperSelectedStateWithIdenticalSemanticsMerges(
        bool reverseTraversalOrder)
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sourcePath = await CreateDeeperSelectedStateGraphAsync(
                root,
                differingContexts: false,
                reverseTraversalOrder: reverseTraversalOrder);
            var config = CreateDeeperSelectedStateConfig(root);

            var contexts = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);

            var context = Assert.Single(contexts).Value;
            Assert.True(context.Provenance.Count >= 2);
            Assert.Contains(
                context.Provenance,
                provenance => provenance.ProjectGraphPath.Any(path =>
                    path.EndsWith(
                        "/Bridge/Bridge.csproj",
                        StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseContextResolver_DeeperSelectedStateWithDifferentSemanticsRejects(
        bool reverseTraversalOrder)
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sourcePath = await CreateDeeperSelectedStateGraphAsync(
                root,
                differingContexts: true,
                reverseTraversalOrder: reverseTraversalOrder);
            var config = CreateDeeperSelectedStateConfig(root);

            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);
            var error = Assert.IsType<AmbiguousProjectFileParseContext>(
                Assert.Single(resolutions).Value).Diagnostic;

            Assert.Contains(
                "materially different evaluated parse/reference contexts",
                error,
                StringComparison.Ordinal);
            Assert.Contains("DIRECT_STATE", error, StringComparison.Ordinal);
            Assert.Contains("DEEP_STATE", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseContextResolver_IgnoresDisabledProjectReference()
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sourcePath = await CreateDeeperSelectedStateGraphAsync(
                root,
                differingContexts: true,
                reverseTraversalOrder: false);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Bridge", "Bridge.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Selected/Selected.csproj">
                      <AdditionalProperties>State=DEEP</AdditionalProperties>
                      <BuildReference>false</BuildReference>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);

            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                CreateDeeperSelectedStateConfig(root),
                [sourcePath],
                CancellationToken.None);
            var resolved = Assert.IsType<ResolvedProjectFileParseContext>(
                Assert.Single(resolutions).Value);

            Assert.Contains(
                "DIRECT_STATE",
                resolved.Context.ParseOptions.PreprocessorSymbolNames);
            Assert.DoesNotContain(
                "DEEP_STATE",
                resolved.Context.ParseOptions.PreprocessorSymbolNames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RoundTripPipeline_SelectsIdenticalAndPreservesDivergentContexts()
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            Directory.Delete(Path.Combine(root, "Lib"), recursive: true);
            var selectedSource =
                await CreateDeeperSelectedStateGraphAsync(
                    root,
                    differingContexts: true,
                    reverseTraversalOrder: false);
            var divergentSource = Path.Combine(
                root,
                "Selected",
                "Divergent.cs");
            var stableSource = Path.Combine(root, "Stable", "Stable.cs");
            var config = new RoundTripConfig
            {
                ProjectName = "AmbiguousContinuation",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = ".",
                SolutionOrProjectFile = "Root.csproj",
                ParseContextProjectFile = "Selected/Selected.csproj",
                TargetFramework = "net10.0",
                Configuration = "Release",
            };
            var report = new RoundTripReport
            {
                ProjectName = config.ProjectName
            };

            var results = await new RoundTripPipeline()
                .ConvertAndReplaceAsync(
                    root,
                    config,
                    report,
                    CancellationToken.None);

            var selected = Assert.Single(results, result =>
                result.FilePath.EndsWith(
                    "Selected/Selected.cs",
                    StringComparison.Ordinal));
            Assert.True(
                selected.Status == FileStatus.Replaced,
                string.Join(Environment.NewLine, selected.Errors));
            Assert.True(selected.ConvertedNative);
            Assert.Equal(
                "multi-context-identical-selected",
                selected.ContextSelectionMode);
            Assert.Equal(2, selected.ValidatedContexts.Count);

            var divergent = Assert.Single(results, result =>
                result.FilePath.EndsWith(
                    "Selected/Divergent.cs",
                    StringComparison.Ordinal));
            Assert.True(
                divergent.Status == FileStatus.Replaced,
                string.Join(Environment.NewLine, divergent.Errors));
            Assert.Equal(
                "multi-context-divergent-preserve",
                divergent.ContextSelectionMode);
            Assert.Equal(2, divergent.ValidatedContexts.Count);
            var divergentGenerated =
                await File.ReadAllTextAsync(divergentSource);
            Assert.Contains("#if DIRECT_STATE", divergentGenerated);
            Assert.Contains("#else", divergentGenerated);

            var stable = Assert.Single(results, result =>
                result.FilePath.EndsWith(
                    "Stable/Stable.cs",
                    StringComparison.Ordinal));
            Assert.Equal(FileStatus.Replaced, stable.Status);
            Assert.True(stable.ConvertedNative);
            Assert.NotEqual(
                "public static class StableCandidate { public static int Read() => 7; }",
                await File.ReadAllTextAsync(stableSource));
            var coverage = ConversionCoverage.Compute(
                results,
                report.ExcludedFileCount);
            Assert.Equal(3, coverage.TotalConvertibleFiles);
            Assert.True(coverage.ConvertedNative >= 2);
            Assert.Equal(0, coverage.FailedConversion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MultiContextPlan_RejectsActiveErrorInAnyObservedContext()
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            var sourcePath = await CreateDeeperSelectedStateGraphAsync(
                root,
                differingContexts: true,
                reverseTraversalOrder: false);
            var config = CreateDeeperSelectedStateConfig(root);
            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);
            var contexts = Assert.IsType<AmbiguousProjectFileParseContext>(
                Assert.Single(resolutions).Value).Contexts;

            var plan = RoundTripPipeline.CreateMultiContextConversionPlan(
                """
                #if DEEP_STATE
                #error deep-state-error
                #endif
                public class ActiveErrorHost { }
                """,
                "ActiveError.cs",
                contexts);

            Assert.Equal("active-error-rejected", plan.SelectionMode);
            Assert.Contains(
                plan.Errors,
                error => error.Contains(
                    "CS1029",
                    StringComparison.Ordinal)
                    && error.Contains(
                        "DEEP_STATE",
                        StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ParseContextResolver_ReferenceOnlyStateDifferenceIsAmbiguous()
    {
        var root = CreateProject("public class UnusedRootSource { }");
        try
        {
            Directory.Delete(Path.Combine(root, "Lib"), recursive: true);
            foreach (var directory in new[]
                     {
                         "ParentA",
                         "ParentB",
                         "Selected",
                         "Dependency"
                     })
            {
                Directory.CreateDirectory(Path.Combine(root, directory));
            }
            await File.WriteAllTextAsync(
                Path.Combine(root, "Root.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="ParentA/ParentA.csproj" />
                    <ProjectReference Include="ParentB/ParentB.csproj" />
                  </ItemGroup>
                </Project>
                """);
            foreach (var parent in new[] { "A", "B" })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(
                        root,
                        $"Parent{parent}",
                        $"Parent{parent}.csproj"),
                    $$"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                      </PropertyGroup>
                      <ItemGroup>
                        <ProjectReference Include="../Selected/Selected.csproj">
                          <AdditionalProperties>ApiMode={{parent}}</AdditionalProperties>
                        </ProjectReference>
                      </ItemGroup>
                    </Project>
                    """);
            }
            await File.WriteAllTextAsync(
                Path.Combine(root, "Selected", "Selected.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Dependency/Dependency.csproj" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Selected", "Selected.cs"),
                "public class ReferenceOnlyCandidate { }");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Dependency", "Dependency.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <Import Project="Api.props" />
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Dependency", "Api.props"),
                """
                <Project>
                  <PropertyGroup Condition="'$(ApiMode)' == 'A'">
                    <DefineConstants>$(DefineConstants);API_A</DefineConstants>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(ApiMode)' == 'B'">
                    <DefineConstants>$(DefineConstants);API_B</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Dependency", "Dependency.cs"),
                """
                #if API_A
                public class DependencyApiA { }
                #else
                public class DependencyApiB { }
                #endif
                """);
            var sourcePath = Path.Combine(
                root,
                "Selected",
                "Selected.cs");
            var config = new RoundTripConfig
            {
                ProjectName = "ReferenceOnlyAmbiguity",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Selected",
                SolutionOrProjectFile = "Root.csproj",
                ParseContextProjectFile = "Selected/Selected.csproj",
                TargetFramework = "net10.0",
                Configuration = "Release",
            };

            var resolutions = await ProjectParseContextResolver.ResolveAsync(
                root,
                config,
                [sourcePath],
                CancellationToken.None);

            var ambiguity = Assert.IsType<AmbiguousProjectFileParseContext>(
                Assert.Single(resolutions).Value);
            Assert.Equal(2, ambiguity.Contexts.Count);
            Assert.Equal(
                2,
                ambiguity.Contexts
                    .SelectMany(context => context.References)
                    .Where(reference =>
                        reference.EvaluationKey.Length > 0)
                    .Select(reference => reference.ContentHash)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
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
    public async Task ProjectValidation_RejectsPreExistingObservedContextFailure()
    {
        var root = CreateProject(
            """
            public static class Conditional
            {
            #if BROKEN_CONTEXT
                public static int Read() => "wrong";
            #else
                public static int Read() => 1;
            #endif
            }
            """);
        try
        {
            var projectPath = Path.Combine(root, "Safety.csproj");
            await File.WriteAllTextAsync(
                projectPath,
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
            var sourcePath = Path.Combine(root, "Lib", "Invalid.cs");
            var original = await File.ReadAllTextAsync(sourcePath);
            var contextProperties =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DefineConstants"] = "BROKEN_CONTEXT"
                };
            var context = new ProjectFileParseContext(
                new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                    preprocessorSymbols: ["BROKEN_CONTEXT"]),
                "Release",
                "AnyCPU",
                "net10.0",
                projectPath,
                contextProperties,
                [],
                [],
                [],
                new Dictionary<string, string>(),
                [],
                [],
                [
                    new ProjectBuildState(
                        projectPath,
                        "Release",
                        "AnyCPU",
                        "net10.0",
                        contextProperties,
                        [projectPath])
                ],
                []);
            var candidate = new FileConversionResult
            {
                FilePath = "Lib/Invalid.cs",
                Status = FileStatus.Replaced,
                EmittedCSharp =
                    "public static class Conditional { public static int Read() => 2; }",
                ObservedContexts = [context]
            };
            var config = new RoundTripConfig
            {
                ProjectName = "PreExistingContextFailure",
                OriginalProjectPath = root,
                LibrarySourceRelativePath = "Lib",
                SolutionOrProjectFile = "Safety.csproj",
            };

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new RoundTripPipeline()
                    .ValidateAndPublishProjectCandidatesAsync(
                        root,
                        config,
                        [candidate],
                        CancellationToken.None));

            Assert.Contains(
                "Original project sources fail validation",
                error.Message,
                StringComparison.Ordinal);
            Assert.Equal(FileStatus.Replaced, candidate.Status);
            Assert.Equal(original, await File.ReadAllTextAsync(sourcePath));
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

    private static RoundTripConfig CreateGraphConfig(string root) => new()
    {
        ProjectName = "GraphContexts",
        OriginalProjectPath = root,
        LibrarySourceRelativePath = "Leaf",
        SolutionOrProjectFile = "Root.csproj",
        TargetFramework = "net10.0",
        Configuration = "Release",
    };

    private static RoundTripConfig CreateSelectedMultiTfmConfig(
        string root,
        string? targetFramework) => new()
    {
        ProjectName = "SelectedMultiTfm",
        OriginalProjectPath = root,
        LibrarySourceRelativePath = "Selected",
        SolutionOrProjectFile = "Root.csproj",
        ParseContextProjectFile = "Selected/Selected.csproj",
        TargetFramework = targetFramework,
        Configuration = "Release",
    };

    private static RoundTripConfig CreateDeeperSelectedStateConfig(
        string root) => new()
    {
        ProjectName = "DeeperSelectedState",
        OriginalProjectPath = root,
        LibrarySourceRelativePath = "Selected",
        SolutionOrProjectFile = "Root.csproj",
        ParseContextProjectFile = "Selected/Selected.csproj",
        TargetFramework = "net10.0",
        Configuration = "Release",
    };

    private static async Task<string> CreateDeeperSelectedStateGraphAsync(
        string root,
        bool differingContexts,
        bool reverseTraversalOrder)
    {
        foreach (var directory in new[] { "Bridge", "Selected", "Stable" })
            Directory.CreateDirectory(Path.Combine(root, directory));
        var directProperties = differingContexts
            ? "State=DIRECT"
            : "Irrelevant=DIRECT";
        var deepProperties = differingContexts
            ? "State=DEEP"
            : "Irrelevant=DEEP";
        var references = new[]
        {
            $"""
            <ProjectReference Include="Selected/Selected.csproj">
              <AdditionalProperties>{directProperties}</AdditionalProperties>
            </ProjectReference>
            """,
            """<ProjectReference Include="Bridge/Bridge.csproj" />"""
        };
        if (reverseTraversalOrder)
            Array.Reverse(references);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Root.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                {{string.Join(Environment.NewLine, references)}}
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Bridge", "Bridge.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Selected/Selected.csproj">
                  <AdditionalProperties>{{deepProperties}}</AdditionalProperties>
                </ProjectReference>
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Selected", "Selected.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <PropertyGroup Condition="'$(State)' == 'DIRECT'">
                <DefineConstants>$(DefineConstants);DIRECT_STATE</DefineConstants>
              </PropertyGroup>
              <PropertyGroup Condition="'$(State)' == 'DEEP'">
                <DefineConstants>$(DefineConstants);DEEP_STATE</DefineConstants>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Stable/Stable.csproj" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Stable", "Stable.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Stable", "Stable.cs"),
            "public static class StableCandidate { public static int Read() => 7; }");
        var sourcePath = Path.Combine(root, "Selected", "Selected.cs");
        await File.WriteAllTextAsync(
            sourcePath,
            "public class DeeperSelectedStateSource { }");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Selected", "Divergent.cs"),
            """
            #if DIRECT_STATE
            public class DirectStateType { }
            #else
            public class DeepStateType { }
            #endif
            """);
        return sourcePath;
    }

    private static async Task<string> CreateSelectedMultiTfmGraphAsync(
        string root,
        bool includeNet10State,
        bool includeSecondNet10State,
        bool includeNetstandardState,
        bool reverseTraversalOrder)
    {
        foreach (var directory in new[] { "A", "B", "C", "Selected" })
            Directory.CreateDirectory(Path.Combine(root, directory));
        var projects = new List<string>();
        if (includeNet10State)
            projects.Add("A");
        if (includeSecondNet10State)
            projects.Add("B");
        if (includeNetstandardState)
            projects.Add("C");
        if (reverseTraversalOrder)
            projects.Reverse();
        await File.WriteAllTextAsync(
            Path.Combine(root, "Root.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                {{string.Join(
                    Environment.NewLine,
                    projects.Select(project =>
                        $"<ProjectReference Include=\"{project}/{project}.csproj\" />"))}}
              </ItemGroup>
            </Project>
            """);
        foreach (var project in projects)
        {
            var targetFramework = project == "C"
                ? "netstandard2.0"
                : "net10.0";
            var flavor = project == "C" ? "STANDARD" : project;
            await File.WriteAllTextAsync(
                Path.Combine(root, project, $"{project}.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{{targetFramework}}</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Selected/Selected.csproj">
                      <AdditionalProperties>Flavor={{flavor}}</AdditionalProperties>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);
        }
        await File.WriteAllTextAsync(
            Path.Combine(root, "Selected", "Selected.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
              </PropertyGroup>
              <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
                <DefineConstants>$(DefineConstants);TFM_NET10</DefineConstants>
              </PropertyGroup>
              <PropertyGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
                <DefineConstants>$(DefineConstants);TFM_NETSTANDARD</DefineConstants>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Flavor)' == 'A'">
                <DefineConstants>$(DefineConstants);FLAVOR_A</DefineConstants>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Flavor)' == 'B'">
                <DefineConstants>$(DefineConstants);FLAVOR_B</DefineConstants>
              </PropertyGroup>
            </Project>
            """);
        var sourcePath = Path.Combine(root, "Selected", "Selected.cs");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            public class SelectedMultiTfmSource { }
            """);
        return sourcePath;
    }

    private static async Task<string> CreateDiamondProjectGraphAsync(
        string root,
        bool differingContexts,
        bool reverseTraversalOrder)
    {
        foreach (var directory in new[] { "A", "B", "Mid", "Shared", "Leaf" })
            Directory.CreateDirectory(Path.Combine(root, directory));
        var firstProject = reverseTraversalOrder ? "B" : "A";
        var secondProject = reverseTraversalOrder ? "A" : "B";
        await File.WriteAllTextAsync(
            Path.Combine(root, "Root.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{firstProject}}/{{firstProject}}.csproj" />
                <ProjectReference Include="{{secondProject}}/{{secondProject}}.csproj" />
              </ItemGroup>
            </Project>
            """);
        var directProject = reverseTraversalOrder ? "B" : "A";
        var indirectProject = directProject == "A" ? "B" : "A";
        foreach (var projectName in new[] { "A", "B" })
        {
            var additionalProperties = differingContexts
                ? $"<AdditionalProperties>Flavor={projectName}</AdditionalProperties>"
                : $"<AdditionalProperties>Irrelevant={projectName}</AdditionalProperties>";
            var referencedProject = projectName == directProject
                ? "../Shared/Shared.csproj"
                : "../Mid/Mid.csproj";
            await File.WriteAllTextAsync(
                Path.Combine(root, projectName, $"{projectName}.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{referencedProject}}">
                      {{additionalProperties}}
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);
        }
        await File.WriteAllTextAsync(
            Path.Combine(root, "Mid", "Mid.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Shared/Shared.csproj">
                  <AdditionalProperties>{{(differingContexts ? $"Flavor={indirectProject}" : $"Irrelevant={indirectProject}")}}</AdditionalProperties>
                </ProjectReference>
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Shared", "Shared.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Leaf/Leaf.csproj" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Leaf", "Leaf.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Features>strict</Features>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Flavor)' == 'A'">
                <DefineConstants>$(DefineConstants);CONTEXT_A</DefineConstants>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Flavor)' == 'B'">
                <DefineConstants>$(DefineConstants);CONTEXT_B</DefineConstants>
              </PropertyGroup>
            </Project>
            """);
        var sourcePath = Path.Combine(root, "Leaf", "Leaf.cs");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            #if CONTEXT_A
            public class SharedContextA { }
            #elif CONTEXT_B
            public class SharedContextB { }
            #else
            public class SharedContextDefault { }
            #endif
            """);
        return sourcePath;
    }

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
