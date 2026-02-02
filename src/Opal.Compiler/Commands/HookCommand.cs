using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opal.Compiler.Commands;

/// <summary>
/// CLI command for Claude Code hook integration.
/// Validates tool inputs to enforce OPAL-first development.
/// </summary>
public static class HookCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Command Create()
    {
        var command = new Command("hook", "Claude Code hook commands for OPAL-first enforcement");

        command.AddCommand(CreateValidateWriteCommand());

        return command;
    }

    private static Command CreateValidateWriteCommand()
    {
        var inputArgument = new Argument<string>(
            name: "tool-input",
            description: "The JSON tool input from Claude Code");

        var command = new Command("validate-write", "Validate a Write tool call to enforce OPAL-first development")
        {
            inputArgument
        };

        command.SetHandler((System.CommandLine.Invocation.InvocationContext context) =>
        {
            var toolInputJson = context.ParseResult.GetValueForArgument(inputArgument);
            var exitCode = ValidateWrite(toolInputJson);
            context.ExitCode = exitCode;
        });

        return command;
    }

    /// <summary>
    /// Validates a Write tool input and returns exit code.
    /// Returns 0 to allow the operation, 1 to block it.
    /// </summary>
    public static int ValidateWrite(string toolInputJson)
    {
        try
        {
            var input = JsonSerializer.Deserialize<WriteToolInput>(toolInputJson, JsonOptions);

            if (input == null)
            {
                // Can't parse input, allow the operation
                return 0;
            }

            // Check both snake_case (file_path) and camelCase (filePath)
            var path = input.FilePathSnake ?? input.FilePathCamel;

            if (string.IsNullOrEmpty(path))
            {
                // No file path found, allow the operation
                return 0;
            }

            // Allow .opal files
            if (path.EndsWith(".opal", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            // Allow generated files (.g.cs) anywhere
            if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            // Allow files in obj/ directory (build artifacts)
            if (path.Contains("/obj/") || path.Contains("\\obj\\") ||
                path.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("obj\\", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            // Block new .cs files
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var suggestedPath = Path.ChangeExtension(path, ".opal");

                Console.Error.WriteLine($"BLOCKED: Cannot create C# file '{path}'");
                Console.Error.WriteLine();
                Console.Error.WriteLine("This is an OPAL-first project. Create an .opal file instead:");
                Console.Error.WriteLine($"  {suggestedPath}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Use /opal skill for OPAL syntax help.");

                return 1;
            }

            // Allow all other file types
            return 0;
        }
        catch (JsonException)
        {
            // If we can't parse the JSON, allow the operation
            return 0;
        }
    }

    private sealed class WriteToolInput
    {
        [JsonPropertyName("file_path")]
        public string? FilePathSnake { get; set; }

        [JsonPropertyName("filePath")]
        public string? FilePathCamel { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
