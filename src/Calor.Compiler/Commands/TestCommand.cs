using System.CommandLine;
using System.CommandLine.Invocation;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command that compiles Calor sources and runs tests against them:
/// <c>calor test file.calr</c> or <c>calor test dir/</c>. The sources are
/// materialized as a temporary class library; when the target directory has a
/// tests/ subdirectory of C# files (the benchmark-pair layout: src .calr +
/// tests/*.cs), a generated xUnit project referencing the library runs them via
/// <c>dotnet test</c>. When no tests are found, the library is still built to
/// validate the generated C#, and the command exits with
/// <see cref="NoTestsExitCode"/>.
/// </summary>
public static class TestCommand
{
    /// <summary>
    /// Exit code when the target compiled and built but no tests were found —
    /// distinct from test failure (non-zero from dotnet test) and usage errors (2).
    /// </summary>
    public const int NoTestsExitCode = 3;

    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "A .calr file, or a directory containing .calr files (with an optional tests/ subdirectory of *.cs xUnit tests; bin/, obj/ and reference/ subdirectories are excluded)");

        var command = new Command("test", "Compile Calor sources and run tests against them (no project file required)")
        {
            pathArgument
        };

        var bindSettings = ExecutionWorkspace.AddCommonOptions(command);

        command.SetHandler((InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(pathArgument);
            ctx.ExitCode = Execute(path, bindSettings(ctx));
        });

        return command;
    }

    private static int Execute(string path, ExecutionWorkspace.ExecutionSettings settings)
    {
        var prepared = ExecutionWorkspace.Prepare(path, settings, out var exitCode);
        if (prepared == null)
        {
            return exitCode;
        }

        var testFiles = FindTestFiles(path);

        var workspace = ExecutionWorkspace.CreateWorkspace("calor-test");
        try
        {
            var srcProjectPath = ExecutionWorkspace.WriteProject(
                Path.Combine(workspace, "src"), "CalorSrc", executable: false, prepared.Units);

            if (testFiles.Count == 0)
            {
                // No tests to run: still build the library so the generated C#
                // is validated, then report the distinct "no tests" exit code.
                var buildExit = ExecutionWorkspace.RunProcessCaptured(prepared.Dotnet,
                    ["build", srcProjectPath, "--nologo", "-v", settings.Verbose ? "minimal" : "quiet"],
                    workspace,
                    stream: settings.Verbose,
                    settings.Timeout);
                if (buildExit != 0)
                {
                    Console.Error.WriteLine("Error: build of the generated C# project failed (see output above).");
                    return buildExit;
                }

                Console.Error.WriteLine(
                    "No tests found: no tests/ directory with .cs files next to the sources. " +
                    "The generated C# builds successfully.");
                return NoTestsExitCode;
            }

            var testProjectPath = WriteTestProject(Path.Combine(workspace, "tests"), testFiles);

            return ExecutionWorkspace.RunProcess(prepared.Dotnet,
                ["test", testProjectPath, "--nologo", "-v", settings.Verbose ? "normal" : "minimal"],
                workspace,
                settings.Timeout);
        }
        finally
        {
            ExecutionWorkspace.FinishWorkspace(workspace, settings.KeepTemp);
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

        // Package versions match the phase0 benchmark harness pins
        // (bench/phase0-agent-native/run-pair.sh) so both execution paths
        // resolve identical test-stack versions from the local NuGet cache.
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
                <PackageReference Include="xunit" Version="2.9.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
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
