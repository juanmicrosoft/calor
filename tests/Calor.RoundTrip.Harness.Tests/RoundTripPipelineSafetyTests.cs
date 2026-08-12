using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

public sealed class RoundTripPipelineSafetyTests
{
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
