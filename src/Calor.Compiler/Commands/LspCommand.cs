using System.CommandLine;
using System.Diagnostics;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for starting the Calor Language Server.
/// Spawns calor-lsp process with stdio transport for editor/agent integration.
/// </summary>
public static class LspCommand
{
    public static Command Create()
    {
        var command = new Command("lsp", "Start the Calor Language Server (stdio transport)");

        command.SetHandler(async () =>
        {
            // Spawn calor-lsp process (can't reference directly due to circular dependency)
            var startInfo = new ProcessStartInfo("calor-lsp")
            {
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Console.Error.WriteLine("Error: Failed to start calor-lsp");
                    Environment.ExitCode = 1;
                    return;
                }
                await process.WaitForExitAsync();
                Environment.ExitCode = process.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Could not start calor-lsp: {ex.Message}");
                Console.Error.WriteLine("Make sure calor-lsp is installed and in your PATH.");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }
}
