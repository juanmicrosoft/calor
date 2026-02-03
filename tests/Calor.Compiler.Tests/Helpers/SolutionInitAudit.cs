using System.Xml.Linq;

namespace Calor.Compiler.Tests.Helpers;

/// <summary>
/// Audits Calor initialization results for a solution directory.
/// </summary>
public static class SolutionInitAudit
{
    /// <summary>
    /// Performs a comprehensive audit of Calor initialization in a solution directory.
    /// </summary>
    /// <param name="solutionDir">The solution directory to audit.</param>
    /// <returns>Detailed audit results.</returns>
    public static AuditResult Audit(string solutionDir)
    {
        var result = new AuditResult();

        // Find all .csproj files
        var allCsprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj") && !f.Contains("bin")) // Exclude build outputs
            .ToList();

        result.TotalProjects = allCsprojFiles.Count;

        // Check each project for Calor targets
        foreach (var csproj in allCsprojFiles)
        {
            if (HasCalorTargets(csproj))
            {
                result.InitializedProjects++;
            }
            else
            {
                result.MissingTargets.Add(Path.GetRelativePath(solutionDir, csproj));
            }
        }

        // Check for AI files in solution root
        result.HasClaudeMd = File.Exists(Path.Combine(solutionDir, "CLAUDE.md"));
        result.HasSkills = Directory.Exists(Path.Combine(solutionDir, ".claude", "skills", "calor")) &&
                          File.Exists(Path.Combine(solutionDir, ".claude", "skills", "calor", "SKILL.md"));
        result.HasHooks = File.Exists(Path.Combine(solutionDir, ".claude", "settings.json"));

        // Check for CODEX files
        result.HasCodexMd = File.Exists(Path.Combine(solutionDir, "AGENTS.md"));

        // Check for Gemini files
        result.HasGeminiMd = File.Exists(Path.Combine(solutionDir, "GEMINI.md"));

        return result;
    }

    /// <summary>
    /// Checks if a .csproj file has Calor compilation targets.
    /// </summary>
    public static bool HasCalorTargets(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(csprojPath);
            var doc = XDocument.Parse(content);

            return doc.Descendants()
                .Any(e => e.Name.LocalName == "Target" &&
                         e.Attribute("Name")?.Value == "CompileCalorFiles");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a .csproj is SDK-style.
    /// </summary>
    public static bool IsSdkStyleProject(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(csprojPath);
            var doc = XDocument.Parse(content);

            if (doc.Root == null) return false;

            // Check for Sdk attribute on root
            if (doc.Root.Attribute("Sdk") != null)
            {
                return true;
            }

            // Check for <Import Sdk="..."> pattern
            return doc.Root.Elements()
                .Any(e => e.Name.LocalName == "Import" && e.Attribute("Sdk") != null);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Results of auditing Calor initialization.
/// </summary>
public class AuditResult
{
    /// <summary>
    /// Total number of .csproj files found.
    /// </summary>
    public int TotalProjects { get; set; }

    /// <summary>
    /// Number of projects with Calor targets.
    /// </summary>
    public int InitializedProjects { get; set; }

    /// <summary>
    /// Relative paths to projects missing Calor targets.
    /// </summary>
    public List<string> MissingTargets { get; set; } = new();

    /// <summary>
    /// Whether CLAUDE.md exists in the solution root.
    /// </summary>
    public bool HasClaudeMd { get; set; }

    /// <summary>
    /// Whether Calor skills exist in .claude/skills/calor/.
    /// </summary>
    public bool HasSkills { get; set; }

    /// <summary>
    /// Whether hooks are configured in .claude/settings.json.
    /// </summary>
    public bool HasHooks { get; set; }

    /// <summary>
    /// Whether AGENTS.md (Codex) exists in the solution root.
    /// </summary>
    public bool HasCodexMd { get; set; }

    /// <summary>
    /// Whether GEMINI.md exists in the solution root.
    /// </summary>
    public bool HasGeminiMd { get; set; }

    /// <summary>
    /// Percentage of projects initialized.
    /// </summary>
    public double InitializationRate =>
        TotalProjects > 0 ? (double)InitializedProjects / TotalProjects * 100 : 0;

    /// <summary>
    /// Whether all SDK-style projects are initialized (ignoring legacy projects).
    /// </summary>
    public bool IsFullyInitialized => MissingTargets.Count == 0 && InitializedProjects > 0;

    /// <summary>
    /// Returns a summary string for test output.
    /// </summary>
    public override string ToString()
    {
        return $"Audit: {InitializedProjects}/{TotalProjects} projects initialized ({InitializationRate:F1}%), " +
               $"CLAUDE.md={HasClaudeMd}, Skills={HasSkills}, Hooks={HasHooks}";
    }
}
