using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Init;

/// <summary>
/// Initializer for Claude Code AI agent.
/// Creates .claude/skills/ directory with Calor skills, CLAUDE.md project file,
/// and configures hooks to enforce Calor-first development.
/// </summary>
public class ClaudeInitializer : IAiInitializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        // Note: No PropertyNamingPolicy - Claude Code expects PascalCase (e.g., PreToolUse, not pre_tool_use)
    };
    private const string SectionStart = "<!-- BEGIN CalorC SECTION - DO NOT EDIT -->";
    private const string SectionEnd = "<!-- END CalorC SECTION -->";

    public string AgentName => "Claude Code";

    /// <summary>
    /// For testing: allows injecting a custom path for ~/.claude.json
    /// </summary>
    internal string? ClaudeJsonPathOverride { get; set; }

    public async Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Resolve git root so all artifacts land where Claude Code expects them.
            // Claude Code resolves project config by git root, not by working directory.
            var effectiveDir = FindGitRoot(targetDirectory) ?? targetDirectory;

            // Create .claude/skills/calor/ directory
            var calorSkillDir = Path.Combine(effectiveDir, ".claude", "skills", "calor");
            Directory.CreateDirectory(calorSkillDir);

            // Create .claude/skills/calor-convert/ directory
            var convertSkillDir = Path.Combine(effectiveDir, ".claude", "skills", "calor-convert");
            Directory.CreateDirectory(convertSkillDir);

            // Create .claude/skills/calor-semantics/ directory
            var semanticsSkillDir = Path.Combine(effectiveDir, ".claude", "skills", "calor-semantics");
            Directory.CreateDirectory(semanticsSkillDir);

            // Create .claude/skills/calor-analyze/ directory
            var analyzeSkillDir = Path.Combine(effectiveDir, ".claude", "skills", "calor-analyze");
            Directory.CreateDirectory(analyzeSkillDir);

            // Write skill files (Claude uses SKILL.md format with YAML frontmatter)
            var calorSkillPath = Path.Combine(calorSkillDir, "SKILL.md");
            var convertSkillPath = Path.Combine(convertSkillDir, "SKILL.md");
            var semanticsSkillPath = Path.Combine(semanticsSkillDir, "SKILL.md");
            var analyzeSkillPath = Path.Combine(analyzeSkillDir, "SKILL.md");

            if (await WriteFileIfNeeded(calorSkillPath, EmbeddedResourceHelper.ReadSkill("claude-calor-SKILL.md"), force))
            {
                createdFiles.Add(calorSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {calorSkillPath}");
            }

            if (await WriteFileIfNeeded(convertSkillPath, EmbeddedResourceHelper.ReadSkill("claude-calor-convert-SKILL.md"), force))
            {
                createdFiles.Add(convertSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {convertSkillPath}");
            }

            if (await WriteFileIfNeeded(semanticsSkillPath, EmbeddedResourceHelper.ReadSkill("claude-calor-semantics-SKILL.md"), force))
            {
                createdFiles.Add(semanticsSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {semanticsSkillPath}");
            }

            if (await WriteFileIfNeeded(analyzeSkillPath, EmbeddedResourceHelper.ReadSkill("claude-calor-analyze-SKILL.md"), force))
            {
                createdFiles.Add(analyzeSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {analyzeSkillPath}");
            }

            // Create or update CLAUDE.md from template with section-aware handling
            var claudeMdPath = Path.Combine(effectiveDir, "CLAUDE.md");
            var template = EmbeddedResourceHelper.ReadTemplate("CLAUDE.md.template");
            var version = EmbeddedResourceHelper.GetVersion();
            var calorSection = template.Replace("{{VERSION}}", version);

            var claudeMdResult = await UpdateClaudeMdAsync(claudeMdPath, calorSection);
            if (claudeMdResult == ClaudeMdUpdateResult.Created)
            {
                createdFiles.Add(claudeMdPath);
            }
            else if (claudeMdResult == ClaudeMdUpdateResult.Updated)
            {
                updatedFiles.Add(claudeMdPath);
            }

            // Configure Claude Code hooks for Calor-first enforcement (in .claude/settings.json)
            var settingsPath = Path.Combine(effectiveDir, ".claude", "settings.json");
            var settingsResult = await ConfigureHooksAsync(settingsPath, force);
            if (settingsResult == HookSettingsResult.Created)
            {
                createdFiles.Add(settingsPath);
            }
            else if (settingsResult == HookSettingsResult.Updated)
            {
                updatedFiles.Add(settingsPath);
            }

            // Configure MCP servers in ~/.claude.json (per-project section)
            var claudeJsonPath = ClaudeJsonPathOverride ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            var mcpResult = await ConfigureMcpServersInClaudeJsonAsync(claudeJsonPath, effectiveDir, force);
            if (mcpResult == McpConfigResult.Updated)
            {
                updatedFiles.Add(claudeJsonPath);
            }

            var allModifiedFiles = createdFiles.Concat(updatedFiles).ToList();
            var messages = new List<string>();

            if (allModifiedFiles.Count > 0)
            {
                messages.Add($"Initialized Calor project for Claude Code (calor v{version})");
                messages.Add("  - MCP server 'calor' configured for AI agent tools (compile, verify, analyze, convert, typecheck)");
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

    private enum ClaudeMdUpdateResult
    {
        Created,
        Updated,
        Unchanged
    }

    private static async Task<ClaudeMdUpdateResult> UpdateClaudeMdAsync(string path, string calorSection)
    {
        if (!File.Exists(path))
        {
            // No file exists - create with just the Calor section
            await File.WriteAllTextAsync(path, calorSection);
            return ClaudeMdUpdateResult.Created;
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
            return ClaudeMdUpdateResult.Unchanged;
        }

        await File.WriteAllTextAsync(path, newContent);
        return ClaudeMdUpdateResult.Updated;
    }

    private enum HookSettingsResult
    {
        Created,
        Updated,
        Unchanged
    }

    private static async Task<HookSettingsResult> ConfigureHooksAsync(string settingsPath, bool force)
    {
        // PreToolUse hooks: validate-write, validate-calr-content, and validate-ids for Write, validate-edit for Edit
        var writePreHookConfig = new ClaudeHookMatcher
        {
            Matcher = "Write",
            Hooks = new[]
            {
                new ClaudeHook
                {
                    Type = "command",
                    Command = "calor hook validate-write $TOOL_INPUT"
                },
                new ClaudeHook
                {
                    Type = "command",
                    Command = "calor hook validate-calr-content $TOOL_INPUT"
                },
                new ClaudeHook
                {
                    Type = "command",
                    Command = "calor hook validate-ids $TOOL_INPUT"
                }
            }
        };

        var editPreHookConfig = new ClaudeHookMatcher
        {
            Matcher = "Edit",
            Hooks = new[]
            {
                new ClaudeHook
                {
                    Type = "command",
                    Command = "calor hook validate-edit $TOOL_INPUT"
                }
            }
        };

        // PostToolUse hook: post-write-lint for Write
        var writePostHookConfig = new ClaudeHookMatcher
        {
            Matcher = "Write",
            Hooks = new[]
            {
                new ClaudeHook
                {
                    Type = "command",
                    Command = "calor hook post-write-lint $TOOL_INPUT"
                }
            }
        };

        if (!File.Exists(settingsPath))
        {
            // Create new settings file with hook configuration (MCP servers go in .mcp.json)
            var settings = new ClaudeSettings
            {
                Hooks = new ClaudeHooksConfig
                {
                    PreToolUse = new[] { writePreHookConfig, editPreHookConfig },
                    PostToolUse = new[] { writePostHookConfig }
                }
            };

            await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            return HookSettingsResult.Created;
        }

        // Read existing settings
        var existingJson = await File.ReadAllTextAsync(settingsPath);
        ClaudeSettings? existingSettings;

        try
        {
            existingSettings = JsonSerializer.Deserialize<ClaudeSettings>(existingJson, JsonOptions);
        }
        catch (JsonException)
        {
            // If we can't parse existing settings and force is set, overwrite
            if (force)
            {
                var settings = new ClaudeSettings
                {
                    Hooks = new ClaudeHooksConfig
                    {
                        PreToolUse = new[] { writePreHookConfig, editPreHookConfig },
                        PostToolUse = new[] { writePostHookConfig }
                    }
                };
                await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
                return HookSettingsResult.Updated;
            }
            // Otherwise, leave the file unchanged
            return HookSettingsResult.Unchanged;
        }

        existingSettings ??= new ClaudeSettings();
        existingSettings.Hooks ??= new ClaudeHooksConfig();

        var updated = false;

        // Check and add PreToolUse hooks
        var existingPreHooks = existingSettings.Hooks.PreToolUse?.ToList() ?? new List<ClaudeHookMatcher>();

        // Check for Write validate hook
        var hasWriteValidateHook = existingPreHooks.Any(h =>
            h.Matcher == "Write" &&
            h.Hooks?.Any(hook => hook.Command?.Contains("calor hook validate-write") == true) == true);

        if (!hasWriteValidateHook)
        {
            existingPreHooks.Add(writePreHookConfig);
            updated = true;
        }

        // Check for Edit validate hook
        var hasEditValidateHook = existingPreHooks.Any(h =>
            h.Matcher == "Edit" &&
            h.Hooks?.Any(hook => hook.Command?.Contains("calor hook validate-edit") == true) == true);

        if (!hasEditValidateHook)
        {
            existingPreHooks.Add(editPreHookConfig);
            updated = true;
        }

        existingSettings.Hooks.PreToolUse = existingPreHooks.ToArray();

        // Check and add PostToolUse hooks
        var existingPostHooks = existingSettings.Hooks.PostToolUse?.ToList() ?? new List<ClaudeHookMatcher>();

        var hasPostWriteLintHook = existingPostHooks.Any(h =>
            h.Matcher == "Write" &&
            h.Hooks?.Any(hook => hook.Command?.Contains("calor hook post-write-lint") == true) == true);

        if (!hasPostWriteLintHook)
        {
            existingPostHooks.Add(writePostHookConfig);
            updated = true;
        }

        existingSettings.Hooks.PostToolUse = existingPostHooks.ToArray();

        if (!updated)
        {
            return HookSettingsResult.Unchanged;
        }

        var newJson = JsonSerializer.Serialize(existingSettings, JsonOptions);

        // Check if content actually changed
        if (newJson.TrimEnd() == existingJson.TrimEnd())
        {
            return HookSettingsResult.Unchanged;
        }

        await File.WriteAllTextAsync(settingsPath, newJson);
        return HookSettingsResult.Updated;
    }

    private enum McpConfigResult
    {
        Updated,
        Unchanged
    }

    /// <summary>
    /// Walk up the directory tree looking for a .git entry to find the git repo root.
    /// Detects both normal repos (.git as directory) and worktrees (.git as file).
    /// Returns the git root path, or null if not in a git repo.
    /// </summary>
    internal static string? FindGitRoot(string path)
    {
        var dir = Path.GetFullPath(path);
        for (var depth = 0; depth < 20 && dir != null; depth++)
        {
            var gitPath = Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null || parent.FullName == dir)
                break;
            dir = parent.FullName;
        }
        return null;
    }

    private static async Task<McpConfigResult> ConfigureMcpServersInClaudeJsonAsync(string claudeJsonPath, string projectPath, bool force)
    {
        // MCP server configuration
        var calorMcpConfig = new McpServerConfig
        {
            Type = "stdio",
            Command = "calor",
            Args = new[] { "mcp", "--stdio" }
        };

        // Normalize the project path, then resolve git root for the project key.
        // Claude Code resolves project config by git root, so we must key by that.
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var gitRoot = FindGitRoot(normalizedProjectPath);
        var projectKey = gitRoot ?? normalizedProjectPath;

        // Read existing ~/.claude.json or create new structure
        ClaudeJsonConfig? claudeConfig;
        string existingJson = "";

        if (File.Exists(claudeJsonPath))
        {
            existingJson = await File.ReadAllTextAsync(claudeJsonPath);
            try
            {
                claudeConfig = JsonSerializer.Deserialize<ClaudeJsonConfig>(existingJson, JsonOptions);
            }
            catch (JsonException)
            {
                if (!force)
                {
                    return McpConfigResult.Unchanged;
                }
                claudeConfig = new ClaudeJsonConfig();
            }
        }
        else
        {
            claudeConfig = new ClaudeJsonConfig();
        }

        claudeConfig ??= new ClaudeJsonConfig();
        claudeConfig.Projects ??= new Dictionary<string, ClaudeProjectConfig>();

        // Get or create the project entry (keyed by git root when available)
        if (!claudeConfig.Projects.TryGetValue(projectKey, out var projectConfig))
        {
            projectConfig = new ClaudeProjectConfig();
            claudeConfig.Projects[projectKey] = projectConfig;
        }

        projectConfig.McpServers ??= new Dictionary<string, McpServerConfig>();

        var updated = false;

        // Remove any incorrect calor-lsp entry (LSP is not MCP)
        if (projectConfig.McpServers.ContainsKey("calor-lsp"))
        {
            projectConfig.McpServers.Remove("calor-lsp");
            updated = true;
        }

        // Add calor MCP server if not present
        if (!projectConfig.McpServers.ContainsKey("calor"))
        {
            projectConfig.McpServers["calor"] = calorMcpConfig;
            updated = true;
        }

        if (!updated)
        {
            return McpConfigResult.Unchanged;
        }

        // Serialize with proper formatting
        var newJson = JsonSerializer.Serialize(claudeConfig, JsonOptions);

        if (newJson.TrimEnd() == existingJson.TrimEnd())
        {
            return McpConfigResult.Unchanged;
        }

        await File.WriteAllTextAsync(claudeJsonPath, newJson);
        return McpConfigResult.Updated;
    }
}

// JSON structure classes for Claude Code settings (.claude/settings.json)
// Note: Claude Code expects "hooks" and "PreToolUse" in specific casing
internal class ClaudeSettings
{
    [JsonPropertyName("hooks")]
    public ClaudeHooksConfig? Hooks { get; set; }
}

internal class ClaudeHooksConfig
{
    // PreToolUse must be PascalCase - this is a Claude Code requirement
    public ClaudeHookMatcher[]? PreToolUse { get; set; }

    // PostToolUse must be PascalCase - this is a Claude Code requirement
    public ClaudeHookMatcher[]? PostToolUse { get; set; }
}

internal class ClaudeHookMatcher
{
    [JsonPropertyName("matcher")]
    public string? Matcher { get; set; }

    [JsonPropertyName("hooks")]
    public ClaudeHook[]? Hooks { get; set; }
}

internal class ClaudeHook
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }
}

// JSON structure for ~/.claude.json (user-level Claude Code configuration)
// MCP servers are configured per-project in the "projects" section
internal class ClaudeJsonConfig
{
    [JsonPropertyName("projects")]
    public Dictionary<string, ClaudeProjectConfig>? Projects { get; set; }

    // Allow other properties to be preserved (extensionData captures unknown properties)
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal class ClaudeProjectConfig
{
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig>? McpServers { get; set; }

    // Allow other properties to be preserved
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal class McpServerConfig
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public string[]? Args { get; set; }
}
