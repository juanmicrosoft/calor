using System.CommandLine;
using Calor.Compiler.Mcp;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for starting the Calor MCP (Model Context Protocol) server.
/// Exposes Calor compiler capabilities as tools for AI coding agents.
/// </summary>
public static class McpCommand
{
    public static Command Create()
    {
        var stdioOption = new Option<bool>(
            aliases: ["--stdio"],
            getDefaultValue: () => true,
            description: "Use standard input/output for communication (default: true)");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output to stderr for debugging");

        var rootOption = new Option<string?>(
            aliases: ["--root"],
            description: "Confinement root for project sessions and file writes " +
                         "(calor_session_open/calor_file_write). Defaults to the server " +
                         "process working directory — which depends on how the MCP client " +
                         "spawns the server, so pin it explicitly in harness/CI setups.");

        var command = new Command("mcp", "Start the Calor MCP server for AI coding agents")
        {
            stdioOption,
            verboseOption,
            rootOption
        };

        command.SetHandler(ExecuteAsync, stdioOption, verboseOption, rootOption);

        return command;
    }

    private static async Task ExecuteAsync(bool stdio, bool verbose, string? root)
    {
        if (!stdio)
        {
            Console.Error.WriteLine("Error: Only --stdio mode is currently supported");
            Environment.ExitCode = 1;
            return;
        }

        if (root != null && !Directory.Exists(root))
        {
            Console.Error.WriteLine($"Error: --root directory not found: {root}");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            var server = McpServer.CreateStdio(verbose, root);
            await server.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP server error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
