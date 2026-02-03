using System.Xml.Linq;

namespace Calor.Compiler.Init;

/// <summary>
/// Detects and validates .csproj files in a directory.
/// </summary>
public sealed class ProjectDetector
{
    /// <summary>
    /// Finds .csproj files in the specified directory.
    /// </summary>
    /// <param name="directory">The directory to search.</param>
    /// <param name="specificProject">Optional specific .csproj file path.</param>
    /// <returns>A result containing the detected project or an error.</returns>
    public ProjectDetectionResult Detect(string directory, string? specificProject = null)
    {
        if (!Directory.Exists(directory))
        {
            return ProjectDetectionResult.Error($"Directory not found: {directory}");
        }

        // If a specific project is specified, use it
        if (!string.IsNullOrEmpty(specificProject))
        {
            var projectPath = Path.IsPathRooted(specificProject)
                ? specificProject
                : Path.Combine(directory, specificProject);

            if (!File.Exists(projectPath))
            {
                return ProjectDetectionResult.Error($"Project file not found: {projectPath}");
            }

            var validation = ValidateProject(projectPath);
            if (!validation.IsValid)
            {
                return ProjectDetectionResult.Error(validation.ErrorMessage!);
            }

            return ProjectDetectionResult.Success(projectPath);
        }

        // Auto-detect .csproj files in the directory
        var projects = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);

        if (projects.Length == 0)
        {
            return ProjectDetectionResult.Error(
                "No .csproj file found in the current directory. " +
                "Either create a project first with 'dotnet new' or specify a project with --project.");
        }

        if (projects.Length > 1)
        {
            var projectNames = projects.Select(p => Path.GetFileName(p)).ToList();
            return ProjectDetectionResult.MultipleProjects(projects,
                $"Multiple .csproj files found. Please specify one with --project:\n  " +
                string.Join("\n  ", projectNames));
        }

        var singleProject = projects[0];
        var singleValidation = ValidateProject(singleProject);
        if (!singleValidation.IsValid)
        {
            return ProjectDetectionResult.Error(singleValidation.ErrorMessage!);
        }

        return ProjectDetectionResult.Success(singleProject);
    }

    /// <summary>
    /// Validates that a .csproj file is SDK-style and can be used with Calor.
    /// </summary>
    public ProjectValidationResult ValidateProject(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return ProjectValidationResult.Invalid($"Project file not found: {projectPath}");
        }

        try
        {
            var content = File.ReadAllText(projectPath);
            var doc = XDocument.Parse(content);

            if (doc.Root == null)
            {
                return ProjectValidationResult.Invalid("Invalid project file: no root element.");
            }

            // Check for SDK-style project
            var sdkAttribute = doc.Root.Attribute("Sdk");
            if (sdkAttribute == null)
            {
                // Also check for <Import Sdk="..."> pattern (less common but valid)
                var importSdk = doc.Root.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Import" && e.Attribute("Sdk") != null);

                if (importSdk == null)
                {
                    return ProjectValidationResult.Invalid(
                        "Legacy-style .csproj detected. Calor requires SDK-style projects. " +
                        "Please migrate your project to SDK-style format or create a new project with 'dotnet new'.");
                }
            }

            return ProjectValidationResult.Valid();
        }
        catch (Exception ex)
        {
            return ProjectValidationResult.Invalid($"Failed to parse project file: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if Calor targets are already present in a project.
    /// </summary>
    public bool HasCalorTargets(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(projectPath);
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
}

/// <summary>
/// Result of project detection.
/// </summary>
public sealed class ProjectDetectionResult
{
    public bool IsSuccess { get; private init; }
    public string? ProjectPath { get; private init; }
    public string? ErrorMessage { get; private init; }
    public IReadOnlyList<string>? AvailableProjects { get; private init; }
    public bool HasMultipleProjects => AvailableProjects?.Count > 1;

    private ProjectDetectionResult() { }

    public static ProjectDetectionResult Success(string projectPath)
    {
        return new ProjectDetectionResult
        {
            IsSuccess = true,
            ProjectPath = projectPath
        };
    }

    public static ProjectDetectionResult Error(string message)
    {
        return new ProjectDetectionResult
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }

    public static ProjectDetectionResult MultipleProjects(string[] projects, string message)
    {
        return new ProjectDetectionResult
        {
            IsSuccess = false,
            ErrorMessage = message,
            AvailableProjects = projects
        };
    }
}

/// <summary>
/// Result of project validation.
/// </summary>
public sealed class ProjectValidationResult
{
    public bool IsValid { get; private init; }
    public string? ErrorMessage { get; private init; }

    private ProjectValidationResult() { }

    public static ProjectValidationResult Valid()
    {
        return new ProjectValidationResult { IsValid = true };
    }

    public static ProjectValidationResult Invalid(string message)
    {
        return new ProjectValidationResult
        {
            IsValid = false,
            ErrorMessage = message
        };
    }
}
