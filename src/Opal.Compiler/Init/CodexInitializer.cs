namespace Opal.Compiler.Init;

/// <summary>
/// Initializer for OpenAI Codex CLI AI agent.
/// Creates .codex/skills/ directory with OPAL skills and AGENTS.md project file.
/// Note: Codex does not support hooks, so OPAL-first enforcement is guidance-based only.
/// </summary>
public class CodexInitializer : IAiInitializer
{
    private const string SectionStart = "<!-- BEGIN OPALC SECTION - DO NOT EDIT -->";
    private const string SectionEnd = "<!-- END OPALC SECTION -->";

    public string AgentName => "OpenAI Codex";

    public async Task<InitResult> InitializeAsync(string targetDirectory, bool force)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Create .codex/skills/opal/ directory
            var opalSkillDir = Path.Combine(targetDirectory, ".codex", "skills", "opal");
            Directory.CreateDirectory(opalSkillDir);

            // Create .codex/skills/opal-convert/ directory
            var convertSkillDir = Path.Combine(targetDirectory, ".codex", "skills", "opal-convert");
            Directory.CreateDirectory(convertSkillDir);

            // Write skill files (Codex uses SKILL.md format with YAML frontmatter)
            var opalSkillPath = Path.Combine(opalSkillDir, "SKILL.md");
            var convertSkillPath = Path.Combine(convertSkillDir, "SKILL.md");

            if (await WriteFileIfNeeded(opalSkillPath, EmbeddedResourceHelper.ReadSkill("codex-opal-SKILL.md"), force))
            {
                createdFiles.Add(opalSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {opalSkillPath}");
            }

            if (await WriteFileIfNeeded(convertSkillPath, EmbeddedResourceHelper.ReadSkill("codex-opal-convert-SKILL.md"), force))
            {
                createdFiles.Add(convertSkillPath);
            }
            else
            {
                warnings.Add($"Skipped existing file: {convertSkillPath}");
            }

            // Create or update AGENTS.md from template with section-aware handling
            var agentsMdPath = Path.Combine(targetDirectory, "AGENTS.md");
            var template = EmbeddedResourceHelper.ReadTemplate("AGENTS.md.template");
            var version = EmbeddedResourceHelper.GetVersion();
            var opalSection = template.Replace("{{VERSION}}", version);

            var agentsMdResult = await UpdateAgentsMdAsync(agentsMdPath, opalSection);
            if (agentsMdResult == AgentsMdUpdateResult.Created)
            {
                createdFiles.Add(agentsMdPath);
            }
            else if (agentsMdResult == AgentsMdUpdateResult.Updated)
            {
                updatedFiles.Add(agentsMdPath);
            }

            var allModifiedFiles = createdFiles.Concat(updatedFiles).ToList();
            var messages = new List<string>();

            if (allModifiedFiles.Count > 0)
            {
                messages.Add($"Initialized OPAL project for OpenAI Codex (opalc v{version})");
                messages.Add("");
                messages.Add("Note: Codex CLI does not support hooks. OPAL-first enforcement is");
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

    private enum AgentsMdUpdateResult
    {
        Created,
        Updated,
        Unchanged
    }

    private static async Task<AgentsMdUpdateResult> UpdateAgentsMdAsync(string path, string opalSection)
    {
        if (!File.Exists(path))
        {
            // No file exists - create with just the OPAL section
            await File.WriteAllTextAsync(path, opalSection);
            return AgentsMdUpdateResult.Created;
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
            return AgentsMdUpdateResult.Unchanged;
        }

        await File.WriteAllTextAsync(path, newContent);
        return AgentsMdUpdateResult.Updated;
    }
}
