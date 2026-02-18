using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Init;

/// <summary>
/// Initializer for GitHub Copilot AI agent.
/// Creates .github/copilot/skills/ directory with Calor skills, copilot-instructions.md project file,
/// and configures MCP servers in .vscode/mcp.json for Copilot Agent mode.
/// Note: GitHub Copilot does not support hooks, so Calor-first enforcement is guidance-based with MCP tool support.
/// </summary>
public class GitHubCopilotInitializer : IAiInitializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SectionStart = "<!-- BEGIN CalorC SECTION - DO NOT EDIT -->";
    private const string SectionEnd = "<!-- END CalorC SECTION -->";

    public string AgentName => "GitHub Copilot";

    public async Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Create .github/copilot/skills/calor/ directory
            var calorSkillDir = Path.Combine(targetDirectory, ".github", "copilot", "skills", "calor");
            Directory.CreateDirectory(calorSkillDir);

            // Create .github/copilot/skills/calor-convert/ directory
            var convertSkillDir = Path.Combine(targetDirectory, ".github", "copilot", "skills", "calor-convert");
            Directory.CreateDirectory(convertSkillDir);

            // Create .github/copilot/skills/calor-analyze/ directory
            var analyzeSkillDir = Path.Combine(targetDirectory, ".github", "copilot", "skills", "calor-analyze");
            Directory.CreateDirectory(analyzeSkillDir);

            // Write skill files (GitHub Copilot uses SKILL.md format with YAML frontmatter)
            var calorSkillPath = Path.Combine(calorSkillDir, "SKILL.md");
            var convertSkillPath = Path.Combine(convertSkillDir, "SKILL.md");
            var analyzeSkillPath = Path.Combine(analyzeSkillDir, "SKILL.md");

            if (await WriteFileIfNeeded(calorSkillPath, EmbeddedResourceHelper.ReadSkill("github-calor-SKILL.md"), force))
            {
                createdFiles.Add(calorSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {calorSkillPath}");
            }

            if (await WriteFileIfNeeded(convertSkillPath, EmbeddedResourceHelper.ReadSkill("github-calor-convert-SKILL.md"), force))
            {
                createdFiles.Add(convertSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {convertSkillPath}");
            }

            if (await WriteFileIfNeeded(analyzeSkillPath, EmbeddedResourceHelper.ReadSkill("github-calor-analyze-SKILL.md"), force))
            {
                createdFiles.Add(analyzeSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {analyzeSkillPath}");
            }

            // Create or update copilot-instructions.md from template with section-aware handling
            var instructionsPath = Path.Combine(targetDirectory, ".github", "copilot-instructions.md");
            var template = EmbeddedResourceHelper.ReadTemplate("copilot-instructions.md.template");
            var version = EmbeddedResourceHelper.GetVersion();
            var calorSection = template.Replace("{{VERSION}}", version);

            var instructionsResult = await UpdateInstructionsAsync(instructionsPath, calorSection);
            if (instructionsResult == InstructionsUpdateResult.Created)
            {
                createdFiles.Add(instructionsPath);
            }
            else if (instructionsResult == InstructionsUpdateResult.Updated)
            {
                updatedFiles.Add(instructionsPath);
            }

            // Configure MCP servers in .vscode/mcp.json (project-level)
            var mcpJsonPath = Path.Combine(targetDirectory, ".vscode", "mcp.json");
            var mcpResult = await ConfigureMcpServersAsync(mcpJsonPath, force);
            if (mcpResult == McpConfigResult.Created)
            {
                createdFiles.Add(mcpJsonPath);
            }
            else if (mcpResult == McpConfigResult.Updated)
            {
                updatedFiles.Add(mcpJsonPath);
            }

            var allModifiedFiles = createdFiles.Concat(updatedFiles).ToList();
            var messages = new List<string>();

            if (allModifiedFiles.Count > 0)
            {
                messages.Add($"Initialized Calor project for GitHub Copilot (calor v{version})");
                messages.Add("  - MCP server 'calor' configured for AI agent tools (compile, verify, analyze, convert, typecheck)");
                messages.Add("");
                messages.Add("NOTE: Calor-first enforcement is guidance-based via copilot-instructions.md and MCP tools.");
            }
            else
            {
                messages.Add("No files created (all files already exist). Use --force to overwrite.");
            }

            return new InitResult
            {
                Success = true,
                CreatedFiles = createdFiles,
                UpdatedFiles = updatedFiles,
                Warnings = warnings,
                Messages = messages
            };
        }
        catch (Exception ex)
        {
            return InitResult.Failed($"Failed to initialize: {ex.Message}");
        }
    }

    private static async Task<bool> WriteFileIfNeeded(string path, string content, bool force)
    {
        if (File.Exists(path) && !force)
        {
            return false;
        }

        await File.WriteAllTextAsync(path, content);
        return true;
    }

    private enum InstructionsUpdateResult
    {
        Created,
        Updated,
        Unchanged
    }

    private static async Task<InstructionsUpdateResult> UpdateInstructionsAsync(string path, string calorSection)
    {
        // Ensure .github directory exists
        var githubDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(githubDir))
        {
            Directory.CreateDirectory(githubDir);
        }

        if (!File.Exists(path))
        {
            // No file exists - create with just the Calor section
            await File.WriteAllTextAsync(path, calorSection);
            return InstructionsUpdateResult.Created;
        }

        var existingContent = await File.ReadAllTextAsync(path);
        var startIdx = existingContent.IndexOf(SectionStart, StringComparison.Ordinal);
        var endIdx = existingContent.IndexOf(SectionEnd, StringComparison.Ordinal);

        string newContent;
        if (startIdx >= 0 && endIdx > startIdx)
        {
            // Replace existing section
            var before = existingContent[..startIdx];
            var after = existingContent[(endIdx + SectionEnd.Length)..];
            newContent = before + calorSection + after;
        }
        else
        {
            // Append section at the end
            newContent = existingContent.TrimEnd() + "\n\n" + calorSection + "\n";
        }

        // Normalize trailing whitespace for comparison
        if (newContent.TrimEnd() == existingContent.TrimEnd())
        {
            return InstructionsUpdateResult.Unchanged;
        }

        await File.WriteAllTextAsync(path, newContent);
        return InstructionsUpdateResult.Updated;
    }

    private enum McpConfigResult
    {
        Created,
        Updated,
        Unchanged
    }

    private static async Task<McpConfigResult> ConfigureMcpServersAsync(string mcpJsonPath, bool force)
    {
        // MCP server configuration for Copilot (no "type" field - command implies stdio)
        var calorMcpConfig = new CopilotMcpServerConfig
        {
            Command = "calor",
            Args = new[] { "mcp", "--stdio" }
        };

        // Ensure .vscode/ directory exists
        var vscodeDir = Path.GetDirectoryName(mcpJsonPath);
        if (!string.IsNullOrEmpty(vscodeDir))
        {
            Directory.CreateDirectory(vscodeDir);
        }

        if (!File.Exists(mcpJsonPath))
        {
            // Create new mcp.json
            var config = new VscodeMcpConfig
            {
                Servers = new Dictionary<string, CopilotMcpServerConfig>
                {
                    ["calor"] = calorMcpConfig
                }
            };
            await File.WriteAllTextAsync(mcpJsonPath, JsonSerializer.Serialize(config, JsonOptions));
            return McpConfigResult.Created;
        }

        // Read existing mcp.json
        var existingJson = await File.ReadAllTextAsync(mcpJsonPath);
        VscodeMcpConfig? existingConfig;

        try
        {
            existingConfig = JsonSerializer.Deserialize<VscodeMcpConfig>(existingJson, JsonOptions);
        }
        catch (JsonException)
        {
            if (force)
            {
                var config = new VscodeMcpConfig
                {
                    Servers = new Dictionary<string, CopilotMcpServerConfig>
                    {
                        ["calor"] = calorMcpConfig
                    }
                };
                await File.WriteAllTextAsync(mcpJsonPath, JsonSerializer.Serialize(config, JsonOptions));
                return McpConfigResult.Updated;
            }
            // Leave invalid JSON unchanged
            return McpConfigResult.Unchanged;
        }

        existingConfig ??= new VscodeMcpConfig();
        existingConfig.Servers ??= new Dictionary<string, CopilotMcpServerConfig>();

        // Skip if calor is already configured (idempotency)
        if (existingConfig.Servers.ContainsKey("calor"))
        {
            return McpConfigResult.Unchanged;
        }

        existingConfig.Servers["calor"] = calorMcpConfig;

        var newJson = JsonSerializer.Serialize(existingConfig, JsonOptions);

        if (newJson.TrimEnd() == existingJson.TrimEnd())
        {
            return McpConfigResult.Unchanged;
        }

        await File.WriteAllTextAsync(mcpJsonPath, newJson);
        return McpConfigResult.Updated;
    }
}

// JSON structure for .vscode/mcp.json (GitHub Copilot MCP configuration)
internal class VscodeMcpConfig
{
    [JsonPropertyName("servers")]
    public Dictionary<string, CopilotMcpServerConfig>? Servers { get; set; }

    // Preserve other top-level properties (e.g., "inputs" array from Copilot config)
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal class CopilotMcpServerConfig
{
    // Note: No "type" field - Copilot infers stdio from "command"
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public string[]? Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    // Preserve other properties
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
