using System.CommandLine;
using System.CommandLine.Invocation;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command that compiles and executes a Calor program in one step:
/// <c>calor run file.calr</c> or <c>calor run dir/</c>. Materializes a temporary
/// executable project from the generated C#, builds it with the dotnet CLI,
/// runs it streaming its output, and propagates the program's exit code.
/// </summary>
public static class RunCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "A .calr file, or a directory containing .calr files (bin/, obj/ and tests/ subdirectories are excluded)");

        var permissiveOption = new Option<bool>(
            aliases: ["--permissive"],
            description: "Relax effect enforcement: unknown calls assumed pure, forbidden effects demoted to warnings");

        var keepTempOption = new Option<bool>(
            aliases: ["--keep-temp"],
            description: "Preserve the materialized temp project and print its path");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show compilation and build details");

        var command = new Command("run", "Compile and execute a Calor program (no project file required)")
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

        var workspace = ExecutionWorkspace.CreateWorkspace("calor-run");
        try
        {
            var projectPath = ExecutionWorkspace.WriteProject(
                Path.Combine(workspace, "app"), "CalorApp", executable: true, units);

            var outDir = Path.Combine(workspace, "out");
            var buildExit = ExecutionWorkspace.RunProcessCaptured(dotnet,
                ["build", projectPath, "--nologo", "-o", outDir, "-v", verbose ? "minimal" : "quiet"],
                workspace,
                echoAlways: verbose);
            if (buildExit != 0)
            {
                Console.Error.WriteLine("Error: build of the generated C# project failed (see output above).");
                return buildExit;
            }

            // Run from the user's current directory so relative-path file IO in the
            // program behaves as expected; exit code is the program's exit code.
            return ExecutionWorkspace.RunProcess(dotnet,
                [Path.Combine(outDir, "CalorApp.dll")],
                Environment.CurrentDirectory);
        }
        finally
        {
            ExecutionWorkspace.FinishWorkspace(workspace, keepTemp);
        }
    }
}
