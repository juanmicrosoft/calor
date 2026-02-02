using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opal.Compiler.Commands;

/// <summary>
/// CLI command for AI agent hook integration.
/// Validates tool inputs to enforce OPAL-first development.
/// Supports both Claude Code (exit codes) and Gemini CLI (JSON output) formats.
/// </summary>
public static class HookCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static Command Create()
    {
        var command = new Command("hook", "AI agent hook commands for OPAL-first enforcement");

        command.AddCommand(CreateValidateWriteCommand());

        return command;
    }

    private static Command CreateValidateWriteCommand()
    {
        var inputArgument = new Argument<string>(
            name: "tool-input",
            description: "The JSON tool input from the AI agent");

        var formatOption = new Option<string?>(
            aliases: new[] { "--format", "-f" },
            description: "Output format: 'gemini' for JSON response, default for exit codes only");

        var command = new Command("validate-write", "Validate a Write tool call to enforce OPAL-first development")
        {
            inputArgument,
            formatOption
        };

        command.SetHandler((System.CommandLine.Invocation.InvocationContext context) =>
        {
            var toolInputJson = context.ParseResult.GetValueForArgument(inputArgument);
            var format = context.ParseResult.GetValueForOption(formatOption);

            var (exitCode, blockReason, suggestedPath) = ValidateWriteWithReason(toolInputJson);

            if (string.Equals(format, "gemini", StringComparison.OrdinalIgnoreCase))
            {
                // Gemini CLI expects JSON response via stdout
                OutputGeminiResponse(exitCode, blockReason, suggestedPath);
            }
            else
            {
                // Claude Code uses exit codes and stderr for human-readable output
                if (exitCode != 0 && !string.IsNullOrEmpty(blockReason))
                {
                    Console.Error.WriteLine(blockReason);
                }
            }

            context.ExitCode = exitCode;
        });

        return command;
    }

    private static void OutputGeminiResponse(int exitCode, string? blockReason, string? suggestedPath)
    {
        object response;
        if (exitCode == 0)
        {
            response = new GeminiAllowResponse { Decision = "allow" };
        }
        else
        {
            var systemMessage = $"This is an OPAL-first project. Create an .opal file instead: {suggestedPath}\n\nUse @opal skill for OPAL syntax help.";
            response = new GeminiDenyResponse
            {
                Decision = "deny",
                Reason = blockReason ?? "Cannot create C# file in OPAL-first project",
                SystemMessage = systemMessage
            };
        }

        Console.WriteLine(JsonSerializer.Serialize(response, JsonOutputOptions));
    }

    /// <summary>
    /// Validates a Write tool input and returns exit code.
    /// Returns 0 to allow the operation, 1 to block it.
    /// </summary>
    public static int ValidateWrite(string toolInputJson)
    {
        var (exitCode, _, _) = ValidateWriteWithReason(toolInputJson);
        return exitCode;
    }

    /// <summary>
    /// Validates a Write tool input and returns exit code with reason for blocking.
    /// Returns (0, null, null) to allow, (1, reason, suggestedPath) to block.
    /// </summary>
    public static (int ExitCode, string? BlockReason, string? SuggestedPath) ValidateWriteWithReason(string toolInputJson)
    {
        try
        {
            var input = JsonSerializer.Deserialize<WriteToolInput>(toolInputJson, JsonOptions);

            if (input == null)
            {
                // Can't parse input, allow the operation
                return (0, null, null);
            }

            // Check both snake_case (file_path) and camelCase (filePath)
            var path = input.FilePathSnake ?? input.FilePathCamel;

            if (string.IsNullOrEmpty(path))
            {
                // No file path found, allow the operation
                return (0, null, null);
            }

            // Allow .opal files
            if (path.EndsWith(".opal", StringComparison.OrdinalIgnoreCase))
            {
                return (0, null, null);
            }

            // Allow generated files (.g.cs) anywhere
            if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                return (0, null, null);
            }

            // Allow files in obj/ directory (build artifacts)
            if (path.Contains("/obj/") || path.Contains("\\obj\\") ||
                path.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("obj\\", StringComparison.OrdinalIgnoreCase))
            {
                return (0, null, null);
            }

            // Block new .cs files
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var suggestedPath = Path.ChangeExtension(path, ".opal");

                var blockReason = $"BLOCKED: Cannot create C# file '{path}'\n\n" +
                                  "This is an OPAL-first project. Create an .opal file instead:\n" +
                                  $"  {suggestedPath}\n\n" +
                                  "Use /opal skill for OPAL syntax help.";

                return (1, blockReason, suggestedPath);
            }

            // Allow all other file types
            return (0, null, null);
        }
        catch (JsonException)
        {
            // If we can't parse the JSON, allow the operation
            return (0, null, null);
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

    private sealed class GeminiAllowResponse
    {
        public string Decision { get; set; } = "allow";
    }

    private sealed class GeminiDenyResponse
    {
        public string Decision { get; set; } = "deny";
        public string? Reason { get; set; }
        public string? SystemMessage { get; set; }
    }
}
