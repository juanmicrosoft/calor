namespace Opal.Compiler.Init;

/// <summary>
/// Initializer for Claude Code AI agent.
/// Creates .claude/skills/ directory with OPAL skills and CLAUDE.md project file.
/// </summary>
public class ClaudeInitializer : IAiInitializer
{
    private const string SectionStart = "<!-- BEGIN OPALC SECTION - DO NOT EDIT -->";
    private const string SectionEnd = "<!-- END OPALC SECTION -->";

    public string AgentName => "Claude Code";

    public async Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Create .claude/skills/ directory
            var skillsDir = Path.Combine(targetDirectory, ".claude", "skills");
            Directory.CreateDirectory(skillsDir);

            // Write skill files
            var opalSkillPath = Path.Combine(skillsDir, "opal.md");
            var convertSkillPath = Path.Combine(skillsDir, "opal-convert.md");

            if (await WriteFileIfNeeded(opalSkillPath, EmbeddedResourceHelper.ReadSkill("opal.md"), force))
            {
                createdFiles.Add(opalSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {opalSkillPath}");
            }

            if (await WriteFileIfNeeded(convertSkillPath, EmbeddedResourceHelper.ReadSkill("opal-convert.md"), force))
            {
                createdFiles.Add(convertSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {convertSkillPath}");
            }

            // Create or update CLAUDE.md from template with section-aware handling
            var claudeMdPath = Path.Combine(targetDirectory, "CLAUDE.md");
            var template = EmbeddedResourceHelper.ReadTemplate("CLAUDE.md.template");
            var version = EmbeddedResourceHelper.GetVersion();
            var opalSection = template.Replace("{{VERSION}}", version);

            var claudeMdResult = await UpdateClaudeMdAsync(claudeMdPath, opalSection);
            if (claudeMdResult == ClaudeMdUpdateResult.Created)
            {
                createdFiles.Add(claudeMdPath);
            }
            else if (claudeMdResult == ClaudeMdUpdateResult.Updated)
            {
                updatedFiles.Add(claudeMdPath);
            }

            var allModifiedFiles = createdFiles.Concat(updatedFiles).ToList();
            var messages = new List<string>();

            if (allModifiedFiles.Count > 0)
            {
                messages.Add($"Initialized OPAL project for Claude Code (opalc v{version})");
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

    private static async Task<ClaudeMdUpdateResult> UpdateClaudeMdAsync(string path, string opalSection)
    {
        if (!File.Exists(path))
        {
            // No file exists - create with just the OPAL section
            await File.WriteAllTextAsync(path, opalSection);
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
            return ClaudeMdUpdateResult.Unchanged;
        }

        await File.WriteAllTextAsync(path, newContent);
        return ClaudeMdUpdateResult.Updated;
    }
}
