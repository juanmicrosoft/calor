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
            description: "A .calr file, or a directory containing .calr files (bin/, obj/ and reference/ subdirectories are excluded)");

        var command = new Command("run", "Compile and execute a Calor program (no project file required)")
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

        var workspace = ExecutionWorkspace.CreateWorkspace("calor-run");
        try
        {
            var projectPath = ExecutionWorkspace.WriteProject(
                Path.Combine(workspace, "app"), "CalorApp", executable: true, prepared.Units);

            var outDir = Path.Combine(workspace, "out");
            var buildExit = ExecutionWorkspace.RunProcessCaptured(prepared.Dotnet,
                ["build", projectPath, "--nologo", "-o", outDir, "-v", settings.Verbose ? "minimal" : "quiet"],
                workspace,
                stream: settings.Verbose,
                settings.Timeout);
            if (buildExit != 0)
            {
                Console.Error.WriteLine("Error: build of the generated C# project failed (see output above).");
                return buildExit;
            }

            // Run from the user's current directory so relative-path file IO in the
            // program behaves as expected; exit code is the program's exit code.
            return ExecutionWorkspace.RunProcess(prepared.Dotnet,
                [Path.Combine(outDir, "CalorApp.dll")],
                Environment.CurrentDirectory,
                settings.Timeout);
        }
        finally
        {
            ExecutionWorkspace.FinishWorkspace(workspace, settings.KeepTemp);
        }
    }
}
