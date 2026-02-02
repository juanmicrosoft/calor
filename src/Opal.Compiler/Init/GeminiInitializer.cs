namespace Opal.Compiler.Init;

/// <summary>
/// Initializer for Google Gemini CLI AI agent.
/// Creates .gemini/skills/ directory with OPAL skills, GEMINI.md project file,
/// and configures hooks to enforce OPAL-first development.
/// Unlike Codex, Gemini CLI supports hooks (as of v0.26.0+).
/// </summary>
public class GeminiInitializer : IAiInitializer
{
    private const string SectionStart = "<!-- BEGIN OPALC SECTION - DO NOT EDIT -->";
    private const string SectionEnd = "<!-- END OPALC SECTION -->";

    public string AgentName => "Google Gemini";

    public async Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Create .gemini/skills/opal/ directory
            var opalSkillDir = Path.Combine(targetDirectory, ".gemini", "skills", "opal");
            Directory.CreateDirectory(opalSkillDir);

            // Create .gemini/skills/opal-convert/ directory
            var convertSkillDir = Path.Combine(targetDirectory, ".gemini", "skills", "opal-convert");
            Directory.CreateDirectory(convertSkillDir);

            // Write skill files (Gemini uses SKILL.md format with YAML frontmatter)
            var opalSkillPath = Path.Combine(opalSkillDir, "SKILL.md");
            var convertSkillPath = Path.Combine(convertSkillDir, "SKILL.md");

            if (await WriteFileIfNeeded(opalSkillPath, EmbeddedResourceHelper.ReadSkill("gemini-opal-SKILL.md"), force))
            {
                createdFiles.Add(opalSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {opalSkillPath}");
            }

            if (await WriteFileIfNeeded(convertSkillPath, EmbeddedResourceHelper.ReadSkill("gemini-opal-convert-SKILL.md"), force))
            {
                createdFiles.Add(convertSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {convertSkillPath}");
            }

            // Create or update GEMINI.md from template with section-aware handling
            var geminiMdPath = Path.Combine(targetDirectory, "GEMINI.md");
            var template = EmbeddedResourceHelper.ReadTemplate("GEMINI.md.template");
            var version = EmbeddedResourceHelper.GetVersion();
            var opalSection = template.Replace("{{VERSION}}", version);

            var geminiMdResult = await UpdateGeminiMdAsync(geminiMdPath, opalSection);
            if (geminiMdResult == GeminiMdUpdateResult.Created)
            {
                createdFiles.Add(geminiMdPath);
            }
            else if (geminiMdResult == GeminiMdUpdateResult.Updated)
            {
                updatedFiles.Add(geminiMdPath);
            }

            // Configure Gemini CLI hooks for OPAL-first enforcement
            var settingsPath = Path.Combine(targetDirectory, ".gemini", "settings.json");
            var settingsResult = await ConfigureHooksAsync(settingsPath, force);
            if (settingsResult == HookSettingsResult.Created)
            {
                createdFiles.Add(settingsPath);
            }
            else if (settingsResult == HookSettingsResult.Updated)
            {
                updatedFiles.Add(settingsPath);
            }

            var allModifiedFiles = createdFiles.Concat(updatedFiles).ToList();
            var messages = new List<string>();

            if (allModifiedFiles.Count > 0)
            {
                messages.Add($"Initialized OPAL project for Google Gemini CLI (opalc v{version})");
                messages.Add("");
                messages.Add("OPAL-first enforcement is enabled via BeforeTool hooks.");
                messages.Add("Gemini CLI will automatically block .cs file creation.");
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

    private enum GeminiMdUpdateResult
    {
        Created,
        Updated,
        Unchanged
    }

    private static async Task<GeminiMdUpdateResult> UpdateGeminiMdAsync(string path, string opalSection)
    {
        if (!File.Exists(path))
        {
            // No file exists - create with just the OPAL section
            await File.WriteAllTextAsync(path, opalSection);
            return GeminiMdUpdateResult.Created;
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
            newContent = before + opalSection + after;
        }
        else
        {
            // Append section at the end
            newContent = existingContent.TrimEnd() + "\n\n" + opalSection + "\n";
        }

        // Normalize trailing whitespace for comparison
        if (newContent.TrimEnd() == existingContent.TrimEnd())
        {
            return GeminiMdUpdateResult.Unchanged;
        }

        await File.WriteAllTextAsync(path, newContent);
        return GeminiMdUpdateResult.Updated;
    }

    private enum HookSettingsResult
    {
        Created,
        Updated,
        Unchanged
    }

    private static async Task<HookSettingsResult> ConfigureHooksAsync(string settingsPath, bool force)
    {
        // Read the template for Gemini settings
        var templateContent = EmbeddedResourceHelper.ReadTemplate("gemini-settings.json.template");

        if (!File.Exists(settingsPath))
        {
            // Create new settings file with hook configuration from template
            await File.WriteAllTextAsync(settingsPath, templateContent);
            return HookSettingsResult.Created;
        }

        // If file exists, check if it already has our hook
        var existingContent = await File.ReadAllTextAsync(settingsPath);

        // Simple check: if our hook command is already present, don't modify
        if (existingContent.Contains("opalc hook validate-write"))
        {
            return HookSettingsResult.Unchanged;
        }

        // File exists but doesn't have our hook - if force, overwrite
        if (force)
        {
            await File.WriteAllTextAsync(settingsPath, templateContent);
            return HookSettingsResult.Updated;
        }

        // Otherwise leave unchanged
        return HookSettingsResult.Unchanged;
    }
}
