using System.CommandLine;
using System.CommandLine.Invocation;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command that compiles Calor sources and runs tests against them:
/// <c>calor test file.calr</c> or <c>calor test dir/</c>. The sources are
/// materialized as a temporary class library; when the target directory has a
/// tests/ subdirectory of C# files (the benchmark-pair layout: src .calr +
/// tests/*.cs), a generated xUnit project referencing the library runs them via
/// <c>dotnet test</c>. Otherwise <c>dotnet test</c> runs on the materialized
/// library itself, which validates that the generated C# builds.
/// </summary>
public static class TestCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "A .calr file, or a directory containing .calr files (with an optional tests/ subdirectory of *.cs xUnit tests)");

        var permissiveOption = new Option<bool>(
            aliases: ["--permissive"],
            description: "Relax effect enforcement: unknown calls assumed pure, forbidden effects demoted to warnings");

        var keepTempOption = new Option<bool>(
            aliases: ["--keep-temp"],
            description: "Preserve the materialized temp projects and print their path");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show compilation and build details");

        var command = new Command("test", "Compile Calor sources and run tests against them (no project file required)")
        {
            pathArgument,
            permissiveOption,
            keepTempOption,
            verboseOption
        };

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(pathArgument);
            var permissive = ctx.ParseResult.GetValueForOption(permissiveOption);
            var keepTemp = ctx.ParseResult.GetValueForOption(keepTempOption);
            var verbose = ctx.ParseResult.GetValueForOption(verboseOption);
            ctx.ExitCode = await Task.Run(() => Execute(path, permissive, keepTemp, verbose));
        });

        return command;
    }

    private static int Execute(string path, bool permissive, bool keepTemp, bool verbose)
    {
        var sources = ExecutionWorkspace.ResolveSources(path, out var error);
        if (sources == null)
        {
            Console.Error.WriteLine($"Error: {error}");
            return 2;
        }

        var dotnet = ExecutionWorkspace.FindDotnet();
        if (dotnet == null)
        {
            Console.Error.WriteLine(ExecutionWorkspace.DotnetMissingMessage);
            return 2;
        }

        var units = ExecutionWorkspace.CompileSources(sources, permissive, verbose);
        if (units == null)
        {
            return 1;
        }

        var testFiles = FindTestFiles(path);

        var workspace = ExecutionWorkspace.CreateWorkspace("calor-test");
        try
        {
            var srcProjectPath = ExecutionWorkspace.WriteProject(
                Path.Combine(workspace, "src"), "CalorSrc", executable: false, units);

            string testTarget;
            if (testFiles.Count > 0)
            {
                testTarget = WriteTestProject(Path.Combine(workspace, "tests"), testFiles);
            }
            else
            {
                Console.WriteLine("No tests/ directory with .cs files found — running 'dotnet test' on the compiled sources only.");
                testTarget = srcProjectPath;
            }

            return ExecutionWorkspace.RunProcess(dotnet,
                ["test", testTarget, "--nologo", "-v", verbose ? "normal" : "minimal"],
                workspace);
        }
        finally
        {
            ExecutionWorkspace.FinishWorkspace(workspace, keepTemp);
        }
    }

    /// <summary>
    /// Finds test sources for the v1 benchmark-pair layout: a tests/ subdirectory
    /// with top-level *.cs files, plus an optional tests/shims/TestShim.calor.cs
    /// (the Calor-arm shim, copied in as TestShim.cs).
    /// </summary>
    private static List<(string SourcePath, string TargetName)> FindTestFiles(string path)
    {
        var files = new List<(string SourcePath, string TargetName)>();
        if (!Directory.Exists(path))
        {
            return files;
        }

        var testsDir = Path.Combine(path, "tests");
        if (!Directory.Exists(testsDir))
        {
            return files;
        }

        foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            files.Add((file, Path.GetFileName(file)));
        }

        var calorShim = Path.Combine(testsDir, "shims", "TestShim.calor.cs");
        if (File.Exists(calorShim))
        {
            files.Add((calorShim, "TestShim.cs"));
        }

        return files;
    }

    private static string WriteTestProject(string projectDir, List<(string SourcePath, string TargetName)> testFiles)
    {
        Directory.CreateDirectory(projectDir);
        foreach (var (sourcePath, targetName) in testFiles)
        {
            File.Copy(sourcePath, Path.Combine(projectDir, targetName), overwrite: true);
        }

        // Package versions match the repository's own test projects so they
        // resolve from the local NuGet cache in typical dev environments.
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
                <PackageReference Include="xunit" Version="2.6.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
              </ItemGroup>

              <ItemGroup>
                <ProjectReference Include="../src/CalorSrc.csproj" />
              </ItemGroup>

            </Project>
            """;

        var csprojPath = Path.Combine(projectDir, "CalorTests.csproj");
        File.WriteAllText(csprojPath, csproj);
        return csprojPath;
    }
}
