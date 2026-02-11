using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for AI agent hook integration.
/// Validates tool inputs to enforce Calor-first development.
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
        var command = new Command("hook", "AI agent hook commands for Calor-first enforcement");

        command.AddCommand(CreateValidateWriteCommand());
        command.AddCommand(CreateValidateEditCommand());
        command.AddCommand(CreateValidateCalrContentCommand());
        command.AddCommand(CreatePostWriteLintCommand());
        command.AddCommand(CreateValidateIdsCommand());

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

        var command = new Command("validate-write", "Validate a Write tool call to enforce Calor-first development")
        {
            inputArgument,
            formatOption
        };

        command.SetHandler((System.CommandLine.Invocation.InvocationContext context) =>
        {
            var toolInputJson = context.ParseResult.GetValueForArgument(inputArgument);
            var format = context.ParseResult.GetValueForOption(formatOption);
            var isGemini = string.Equals(format, "gemini", StringComparison.OrdinalIgnoreCase);

            var (exitCode, blockReason, suggestedPath) = ValidateWriteWithReason(toolInputJson);

            // Track hook decision in telemetry
            TrackHookDecision("validate-write", toolInputJson, exitCode, isGemini);

            if (isGemini)
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
            var systemMessage = $"This is a Calor-first project. Create an .calr file instead: {suggestedPath}\n\n" +
                                "Calor has formal semantics v1.0.0. Key rules:\n" +
                                "- Overflow traps, evaluation is left-to-right\n" +
                                "- Always add §SEMVER[1.0.0] to modules\n\n" +
                                "Use @calor for syntax, @calor-semantics for behavior rules.";
            response = new GeminiDenyResponse
            {
                Decision = "deny",
                Reason = blockReason ?? "Cannot create C# file in Calor-first project",
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

            // Allow .calr files
            if (path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
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
                var suggestedPath = Path.ChangeExtension(path, ".calr");

                var blockReason = $"BLOCKED: Cannot create C# file '{path}'\n\n" +
                                  "This is a Calor-first project. Create an .calr file instead:\n" +
                                  $"  {suggestedPath}\n\n" +
                                  "IMPORTANT: Calor has formal semantics that differ from C#:\n" +
                                  "  - Integer overflow TRAPS (throws OverflowException)\n" +
                                  "  - Evaluation is strictly left-to-right\n" +
                                  "  - Always include §SEMVER[1.0.0] in modules\n\n" +
                                  "Use /calor skill for syntax, /calor-semantics for behavior rules.";

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

    private static Command CreateValidateEditCommand()
    {
        var inputArgument = new Argument<string>(
            name: "tool-input",
            description: "The JSON tool input from the AI agent");

        var formatOption = new Option<string?>(
            aliases: new[] { "--format", "-f" },
            description: "Output format: 'gemini' for JSON response, default for exit codes only");

        var command = new Command("validate-edit", "Validate an Edit tool call to enforce Calor-first development")
        {
            inputArgument,
            formatOption
        };

        command.SetHandler((System.CommandLine.Invocation.InvocationContext context) =>
        {
            var toolInputJson = context.ParseResult.GetValueForArgument(inputArgument);
            var format = context.ParseResult.GetValueForOption(formatOption);
            var isGemini = string.Equals(format, "gemini", StringComparison.OrdinalIgnoreCase);

            // Edit validation uses the same logic as Write validation
            var (exitCode, blockReason, suggestedPath) = ValidateWriteWithReason(toolInputJson);

            // Track hook decision in telemetry
            TrackHookDecision("validate-edit", toolInputJson, exitCode, isGemini);

            if (isGemini)
            {
                OutputGeminiResponse(exitCode, blockReason, suggestedPath);
            }
            else
            {
                if (exitCode != 0 && !string.IsNullOrEmpty(blockReason))
                {
                    // Adjust message for Edit context
                    var editBlockReason = blockReason.Replace("Cannot create", "Cannot edit");
                    Console.Error.WriteLine(editBlockReason);
                }
            }

            context.ExitCode = exitCode;
        });

        return command;
    }

    private static Command CreateValidateCalrContentCommand()
    {
        var inputArgument = new Argument<string>(
            name: "tool-input",
            description: "The JSON tool input from the AI agent");

        var command = new Command("validate-calr-content", "Check .calr file content for semantic version declaration")
        {
            inputArgument
        };

        command.SetHandler((System.CommandLine.Invocation.InvocationContext context) =>
        {
            var toolInputJson = context.ParseResult.GetValueForArgument(inputArgument);
            var (exitCode, warning) = ValidateCalrContent(toolInputJson);

            if (!string.IsNullOrEmpty(warning))
            {
                Console.Error.WriteLine(warning);
            }

            context.ExitCode = exitCode;
        });

        return command;
    }

    /// <summary>
    /// Validates .calr file content for semantic version declaration.
    /// Returns (0, warning) - always allows but may warn if §SEMVER is missing.
    /// </summary>
    public static (int ExitCode, string? Warning) ValidateCalrContent(string toolInputJson)
    {
        try
        {
            var input = JsonSerializer.Deserialize<WriteToolInput>(toolInputJson, JsonOptions);

            if (input == null)
            {
                return (0, null);
            }

            var path = input.FilePathSnake ?? input.FilePathCamel;
            var content = input.Content;

            // Only check .calr files
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            {
                return (0, null);
            }

            // Check if content contains §SEMVER declaration
            if (string.IsNullOrEmpty(content))
            {
                return (0, null);
            }

            // Look for §SEMVER[x.y.z] or §SEMVER{x.y.z}
            if (!content.Contains("§SEMVER[") && !content.Contains("§SEMVER{"))
            {
                return (0, "REMINDER: Add §SEMVER[1.0.0] to your module for semantic versioning");
            }

            return (0, null);
        }
        catch (JsonException)
        {
            return (0, null);
        }
    }

    private static Command CreatePostWriteLintCommand()
    {
        var inputArgument = new Argument<string>(
            name: "tool-input",
            description: "The JSON tool input from the AI agent");

        var command = new Command("post-write-lint", "Run lint check after .calr file is written")
        {
            inputArgument
        };

        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext context) =>
        {
            var toolInputJson = context.ParseResult.GetValueForArgument(inputArgument);
            var exitCode = await PostWriteLintAsync(toolInputJson);
            context.ExitCode = exitCode;
        });

        return command;
    }

    /// <summary>
    /// Runs lint check on .calr files after they are written.
    /// Returns 0 if OK or not a .calr file, 1 if lint issues found.
    /// </summary>
    public static async Task<int> PostWriteLintAsync(string toolInputJson)
    {
        try
        {
            var input = JsonSerializer.Deserialize<WriteToolInput>(toolInputJson, JsonOptions);

            if (input == null)
            {
                return 0;
            }

            var path = input.FilePathSnake ?? input.FilePathCamel;

            // Only lint .calr files
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            // Check if file exists
            if (!File.Exists(path))
            {
                return 0;
            }

            // Run calor lint --check on the file
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "calor",
                Arguments = $"lint --check \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null)
                {
                    return 0; // Can't start process, don't block
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var fileName = Path.GetFileName(path);
                    Console.Error.WriteLine($"Lint issues found in {fileName}:");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Console.Error.WriteLine(output);
                    }
                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.Error.WriteLine(error);
                    }
                    Console.Error.WriteLine($"\nRun 'calor lint --fix {fileName}' to auto-fix.");
                    return 1;
                }

                return 0;
            }
            catch
            {
                // If calor lint fails to run, don't block the operation
                return 0;
            }
        }
        catch (JsonException)
        {
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

    private static Command CreateValidateIdsCommand()
    {
        var inputArgument = new Argument<string>(
            name: "tool-input",
            description: "The JSON tool input from the AI agent");

        var formatOption = new Option<string?>(
            aliases: new[] { "--format", "-f" },
            description: "Output format: 'gemini' for JSON response, default for exit codes only");

        var command = new Command("validate-ids", "Validate IDs in .calr file content")
        {
            inputArgument,
            formatOption
        };

        command.SetHandler((System.CommandLine.Invocation.InvocationContext context) =>
        {
            var toolInputJson = context.ParseResult.GetValueForArgument(inputArgument);
            var format = context.ParseResult.GetValueForOption(formatOption);

            var (exitCode, blockReason) = ValidateIds(toolInputJson);

            if (string.Equals(format, "gemini", StringComparison.OrdinalIgnoreCase))
            {
                OutputGeminiIdResponse(exitCode, blockReason);
            }
            else
            {
                if (exitCode != 0 && !string.IsNullOrEmpty(blockReason))
                {
                    Console.Error.WriteLine(blockReason);
                }
            }

            context.ExitCode = exitCode;
        });

        return command;
    }

    private static void OutputGeminiIdResponse(int exitCode, string? blockReason)
    {
        object response;
        if (exitCode == 0)
        {
            response = new GeminiAllowResponse { Decision = "allow" };
        }
        else
        {
            var systemMessage = "ID validation failed. Key rules:\n" +
                                "- NEVER modify existing IDs\n" +
                                "- OMIT IDs for new declarations (run `calor ids assign`)\n" +
                                "- NEVER copy IDs when extracting code\n" +
                                "- Run `calor ids check .` before commit";
            response = new GeminiDenyResponse
            {
                Decision = "deny",
                Reason = blockReason ?? "ID validation failed",
                SystemMessage = systemMessage
            };
        }

        Console.WriteLine(JsonSerializer.Serialize(response, JsonOutputOptions));
    }

    /// <summary>
    /// Validates IDs in .calr file content.
    /// Returns (0, null) to allow, (1, reason) to block.
    /// </summary>
    public static (int ExitCode, string? BlockReason) ValidateIds(string toolInputJson)
    {
        try
        {
            var input = JsonSerializer.Deserialize<WriteToolInput>(toolInputJson, JsonOptions);

            if (input == null)
            {
                return (0, null);
            }

            var path = input.FilePathSnake ?? input.FilePathCamel;
            var content = input.Content;

            // Only validate .calr files
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            {
                return (0, null);
            }

            if (string.IsNullOrEmpty(content))
            {
                return (0, null);
            }

            // Parse the content to scan IDs
            var diagnostics = new DiagnosticBag();
            diagnostics.SetFilePath(path);

            var lexer = new Lexer(content, diagnostics);
            var tokens = lexer.TokenizeAll();

            if (diagnostics.HasErrors)
            {
                // Can't parse, allow but don't validate IDs
                return (0, null);
            }

            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors)
            {
                // Can't parse, allow but don't validate IDs
                return (0, null);
            }

            // Scan IDs in the new content
            var scanner = new IdScanner();
            var newEntries = scanner.Scan(module, path);

            // Check for issues
            var isTestPath = IdValidator.IsTestPath(path);
            var result = IdChecker.Check(newEntries, allowTestIds: isTestPath);

            // Check for duplicates within the file
            if (result.DuplicateGroups.Count > 0)
            {
                var first = result.DuplicateGroups[0][0];
                return (1, $"BLOCKED: Duplicate ID '{first.Id}' detected in {Path.GetFileName(path)}.\n" +
                          "Each declaration must have a unique ID.\n" +
                          "Run `calor ids assign --fix-duplicates` to fix.");
            }

            // Check for ID churn if the file already exists
            if (File.Exists(path))
            {
                var existingContent = File.ReadAllText(path);
                var existingDiagnostics = new DiagnosticBag();
                existingDiagnostics.SetFilePath(path);

                var existingLexer = new Lexer(existingContent, existingDiagnostics);
                var existingTokens = existingLexer.TokenizeAll();

                if (!existingDiagnostics.HasErrors)
                {
                    var existingParser = new Parser(existingTokens, existingDiagnostics);
                    var existingModule = existingParser.Parse();

                    if (!existingDiagnostics.HasErrors)
                    {
                        var existingScanner = new IdScanner();
                        var existingEntries = existingScanner.Scan(existingModule, path);

                        // Detect ID churn
                        var churn = IdChecker.DetectIdChurn(existingEntries, newEntries);
                        if (churn.Count > 0)
                        {
                            var (old, @new) = churn[0];
                            return (1, $"BLOCKED: ID churn detected for {old.Kind} '{old.Name}'.\n" +
                                      $"  Existing ID: {old.Id}\n" +
                                      $"  New ID: {@new.Id}\n\n" +
                                      "IDs are immutable. NEVER modify an existing ID.\n" +
                                      "If you need to rename, preserve the ID.");
                        }
                    }
                }
            }

            // All checks passed
            return (0, null);
        }
        catch (JsonException)
        {
            return (0, null);
        }
        catch (Exception)
        {
            // On any error, allow the operation
            return (0, null);
        }
    }

    /// <summary>
    /// Tracks a hook allow/block decision in telemetry.
    /// Only tracks .calr and .cs files (skips irrelevant file types).
    /// </summary>
    private static void TrackHookDecision(string hookName, string toolInputJson, int exitCode, bool isGemini)
    {
        try
        {
            var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
            if (telemetry == null) return;

            // Extract file path and extension
            var input = JsonSerializer.Deserialize<WriteToolInput>(toolInputJson, JsonOptions);
            var path = input?.FilePathSnake ?? input?.FilePathCamel ?? "";
            var ext = Path.GetExtension(path).ToLowerInvariant();

            // Only track .calr and .cs files — skip irrelevant types
            if (ext != ".calr" && ext != ".cs")
                return;

            var agent = isGemini ? "gemini" : "claude";

            // Discover project config for agent info
            var discovered = CalorConfigManager.Discover(
                !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(Path.GetDirectoryName(path)))
                    ? path
                    : Directory.GetCurrentDirectory());
            if (discovered != null)
                telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered.Value.Config));

            var decision = exitCode == 0 ? "allow" : "block";
            var eventName = exitCode == 0 ? "HookAllow" : "HookBlock";

            telemetry.TrackEvent(eventName, new Dictionary<string, string>
            {
                ["hook"] = hookName,
                ["decision"] = decision,
                ["fileExtension"] = ext,
                ["agent"] = agent
            });
        }
        catch
        {
            // Never crash the hook
        }
    }
}
