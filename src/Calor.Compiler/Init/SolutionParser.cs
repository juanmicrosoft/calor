using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Calor.Compiler.Init;

/// <summary>
/// Parses .sln (text format) and .slnx (XML format) solution files.
/// </summary>
internal static class SolutionParser
{
    // C# Project type GUID
    private const string CSharpProjectTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

    // Solution folder type GUID (to skip)
    private const string SolutionFolderTypeGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    // Regex pattern for .sln project lines:
    // Project("{TypeGuid}") = "Name", "Path\Project.csproj", "{ProjectGuid}"
    private static readonly Regex SlnProjectPattern = new(
        @"Project\(\""(?<typeGuid>[^""]+)\""\)\s*=\s*\""(?<name>[^""]+)\""\s*,\s*\""(?<path>[^""]+)\""\s*,\s*\""(?<projectGuid>[^""]+)\""",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a .sln file and returns all C# projects.
    /// </summary>
    public static IEnumerable<SolutionProject> ParseSln(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Solution file not found: {path}");
        }

        var solutionDirectory = Path.GetDirectoryName(path)!;
        var lines = File.ReadAllLines(path);
        var projects = new List<SolutionProject>();

        foreach (var line in lines)
        {
            var match = SlnProjectPattern.Match(line);
            if (!match.Success) continue;

            var typeGuid = match.Groups["typeGuid"].Value.Trim('{', '}');
            var name = match.Groups["name"].Value;
            var relativePath = match.Groups["path"].Value;
            var projectGuid = match.Groups["projectGuid"].Value.Trim('{', '}');

            // Skip solution folders
            if (typeGuid.Equals(SolutionFolderTypeGuid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only include C# projects (.csproj)
            if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Normalize path separators
            relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));

            projects.Add(new SolutionProject(name, fullPath, relativePath, typeGuid, projectGuid));
        }

        return projects;
    }

    /// <summary>
    /// Parses a .slnx file (XML format) and returns all C# projects.
    /// </summary>
    public static IEnumerable<SolutionProject> ParseSlnx(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Solution file not found: {path}");
        }

        var solutionDirectory = Path.GetDirectoryName(path)!;
        var projects = new List<SolutionProject>();

        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;

            if (root == null)
            {
                return projects;
            }

            // .slnx format uses <Project> elements with Path attribute
            // Example: <Project Path="src/MyProject/MyProject.csproj" />
            var projectElements = root.Descendants()
                .Where(e => e.Name.LocalName == "Project");

            foreach (var element in projectElements)
            {
                var relativePath = element.Attribute("Path")?.Value;
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                // Only include C# projects
                if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Normalize path separators
                relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));
                var name = Path.GetFileNameWithoutExtension(relativePath);

                // Get optional type and GUID attributes
                var typeGuid = element.Attribute("Type")?.Value ?? CSharpProjectTypeGuid;
                var projectGuid = element.Attribute("Id")?.Value ?? Guid.NewGuid().ToString();

                projects.Add(new SolutionProject(name, fullPath, relativePath, typeGuid, projectGuid));
            }
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException($"Failed to parse solution file: {ex.Message}", ex);
        }

        return projects;
    }

    /// <summary>
    /// Parses a solution file (auto-detects format based on extension).
    /// </summary>
    public static IEnumerable<SolutionProject> Parse(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".slnx" => ParseSlnx(path),
            ".sln" => ParseSln(path),
            _ => throw new ArgumentException($"Unknown solution file format: {extension}")
        };
    }
}

/// <summary>
/// Represents a project referenced in a solution file.
/// </summary>
public sealed record SolutionProject(
    string Name,
    string FullPath,
    string RelativePath,
    string TypeGuid,
    string ProjectGuid)
{
    /// <summary>
    /// Whether this project file exists on disk.
    /// </summary>
    public bool Exists => File.Exists(FullPath);
}
