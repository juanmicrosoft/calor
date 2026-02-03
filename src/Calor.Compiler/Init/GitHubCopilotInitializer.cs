namespace Calor.Compiler.Init;

/// <summary>
/// Initializer for GitHub Copilot AI agent.
/// Creates .github/copilot/skills/ directory with Calor skills and copilot-instructions.md project file.
/// Note: GitHub Copilot does not support hooks, so Calor-first enforcement is guidance-based only.
/// </summary>
public class GitHubCopilotInitializer : IAiInitializer
{
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

            // Write skill files (GitHub Copilot uses SKILL.md format with YAML frontmatter)
            var calorSkillPath = Path.Combine(calorSkillDir, "SKILL.md");
            var convertSkillPath = Path.Combine(convertSkillDir, "SKILL.md");

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

            var allModifiedFiles = createdFiles.Concat(updatedFiles).ToList();
            var messages = new List<string>();

            if (allModifiedFiles.Count > 0)
            {
                messages.Add($"Initialized Calor project for GitHub Copilot (calor v{version})");
                messages.Add("");
                messages.Add("Note: GitHub Copilot does not support hooks. Calor-first enforcement is");
                messages.Add("guidance-based only. Review file extensions after generation.");
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
}
