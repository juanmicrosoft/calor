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

        var command = new Command("mcp", "Start the Calor MCP server for AI coding agents")
        {
            stdioOption,
            verboseOption
        };

        command.SetHandler(ExecuteAsync, stdioOption, verboseOption);

        return command;
    }

    private static async Task ExecuteAsync(bool stdio, bool verbose)
    {
        if (!stdio)
        {
            Console.Error.WriteLine("Error: Only --stdio mode is currently supported");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            var server = McpServer.CreateStdio(verbose);
            await server.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP server error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
