namespace Calor.Compiler.Init;

/// <summary>
/// Initializer for Google Gemini CLI AI agent.
/// Creates .gemini/skills/ directory with Calor skills, GEMINI.md project file,
/// and configures hooks to enforce Calor-first development.
/// Unlike Codex, Gemini CLI supports hooks (as of v0.26.0+).
/// </summary>
public class GeminiInitializer : IAiInitializer
{
    private const string SectionStart = "<!-- BEGIN CalorC SECTION - DO NOT EDIT -->";
    private const string SectionEnd = "<!-- END CalorC SECTION -->";

    public string AgentName => "Google Gemini";

    public async Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Create .gemini/skills/calor/ directory
            var calorSkillDir = Path.Combine(targetDirectory, ".gemini", "skills", "calor");
            Directory.CreateDirectory(calorSkillDir);

            // Create .gemini/skills/calor-convert/ directory
            var convertSkillDir = Path.Combine(targetDirectory, ".gemini", "skills", "calor-convert");
            Directory.CreateDirectory(convertSkillDir);

            // Create .gemini/skills/calor-analyze/ directory
            var analyzeSkillDir = Path.Combine(targetDirectory, ".gemini", "skills", "calor-analyze");
            Directory.CreateDirectory(analyzeSkillDir);

            // Write skill files (Gemini uses SKILL.md format with YAML frontmatter)
            var calorSkillPath = Path.Combine(calorSkillDir, "SKILL.md");
            var convertSkillPath = Path.Combine(convertSkillDir, "SKILL.md");
            var analyzeSkillPath = Path.Combine(analyzeSkillDir, "SKILL.md");

            if (await WriteFileIfNeeded(calorSkillPath, EmbeddedResourceHelper.ReadSkill("gemini-calor-SKILL.md"), force))
            {
                createdFiles.Add(calorSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {calorSkillPath}");
            }

            if (await WriteFileIfNeeded(convertSkillPath, EmbeddedResourceHelper.ReadSkill("gemini-calor-convert-SKILL.md"), force))
            {
                createdFiles.Add(convertSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {convertSkillPath}");
            }

            if (await WriteFileIfNeeded(analyzeSkillPath, EmbeddedResourceHelper.ReadSkill("gemini-calor-analyze-SKILL.md"), force))
            {
                createdFiles.Add(analyzeSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {analyzeSkillPath}");
            }

            // Create or update GEMINI.md from template with section-aware handling
            var geminiMdPath = Path.Combine(targetDirectory, "GEMINI.md");
            var template = EmbeddedResourceHelper.ReadTemplate("GEMINI.md.template");
            var version = EmbeddedResourceHelper.GetVersion();
            var calorSection = template.Replace("{{VERSION}}", version);

            var geminiMdResult = await UpdateGeminiMdAsync(geminiMdPath, calorSection);
            if (geminiMdResult == GeminiMdUpdateResult.Created)
            {
                createdFiles.Add(geminiMdPath);
            }
            else if (geminiMdResult == GeminiMdUpdateResult.Updated)
            {
                updatedFiles.Add(geminiMdPath);
            }

            // Configure Gemini CLI hooks for Calor-first enforcement
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
                messages.Add($"Initialized Calor project for Google Gemini CLI (calor v{version})");
                messages.Add("");
                messages.Add("Calor-first enforcement is enabled via BeforeTool hooks.");
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

    private static async Task<GeminiMdUpdateResult> UpdateGeminiMdAsync(string path, string calorSection)
    {
        if (!File.Exists(path))
        {
            // No file exists - create with just the Calor section
            await File.WriteAllTextAsync(path, calorSection);
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
        if (existingContent.Contains("calor hook validate-write"))
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
