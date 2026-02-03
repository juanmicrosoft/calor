namespace Calor.Compiler.Init;

/// <summary>
/// Initializes all projects in a solution with Calor compilation targets.
/// </summary>
public sealed class SolutionInitializer
{
    private readonly SolutionDetector _solutionDetector;
    private readonly ProjectDetector _projectDetector;
    private readonly CsprojInitializer _csprojInitializer;

    public SolutionInitializer()
    {
        _solutionDetector = new SolutionDetector();
        _projectDetector = new ProjectDetector();
        _csprojInitializer = new CsprojInitializer(_projectDetector);
    }

    public SolutionInitializer(
        SolutionDetector solutionDetector,
        ProjectDetector projectDetector,
        CsprojInitializer csprojInitializer)
    {
        _solutionDetector = solutionDetector;
        _projectDetector = projectDetector;
        _csprojInitializer = csprojInitializer;
    }

    /// <summary>
    /// Initializes all projects in a solution with Calor compilation support.
    /// AI agent files are placed in the solution directory.
    /// </summary>
    /// <param name="solutionPath">Path to the solution file.</param>
    /// <param name="force">If true, overwrite existing configurations.</param>
    /// <param name="aiInitializer">Optional AI agent initializer.</param>
    /// <returns>Result of the initialization.</returns>
    public async Task<SolutionInitResult> InitializeAsync(
        string solutionPath,
        bool force,
        IAiInitializer? aiInitializer = null)
    {
        // Parse the solution to get all projects
        var parseResult = _solutionDetector.ParseSolution(solutionPath);
        if (!parseResult.IsSuccess)
        {
            return SolutionInitResult.Error(parseResult.ErrorMessage!);
        }

        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var solutionName = Path.GetFileName(solutionPath);
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();
        var projectResults = new List<ProjectInitStatus>();
        string? agentName = null;

        // Initialize AI agent files in the solution directory
        if (aiInitializer != null)
        {
            agentName = aiInitializer.AgentName;
            var aiResult = await aiInitializer.InitializeAsync(solutionDirectory, force);

            if (!aiResult.Success)
            {
                return SolutionInitResult.Error(string.Join("; ", aiResult.Messages));
            }

            createdFiles.AddRange(aiResult.CreatedFiles);
            updatedFiles.AddRange(aiResult.UpdatedFiles);
            warnings.AddRange(aiResult.Warnings);
        }

        // Initialize each project in the solution
        foreach (var project in parseResult.Projects)
        {
            var projectStatus = await InitializeProjectAsync(project, force);
            projectResults.Add(projectStatus);

            if (projectStatus.Status == InitStatus.Initialized)
            {
                updatedFiles.Add(project.FullPath);
            }
            else if (projectStatus.Status == InitStatus.Skipped)
            {
                warnings.Add(projectStatus.Message ?? $"Skipped: {project.Name}");
            }
            else if (projectStatus.Status == InitStatus.Failed)
            {
                warnings.Add(projectStatus.Message ?? $"Failed: {project.Name}");
            }
        }

        var initializedCount = projectResults.Count(r => r.Status == InitStatus.Initialized);
        var skippedCount = projectResults.Count(r => r.Status == InitStatus.Skipped);
        var failedCount = projectResults.Count(r => r.Status == InitStatus.Failed);
        var alreadyInitializedCount = projectResults.Count(r => r.Status == InitStatus.AlreadyInitialized);

        // Determine overall success - succeed if at least one project was initialized or all were already done
        var anySuccess = initializedCount > 0 || alreadyInitializedCount > 0;
        if (!anySuccess && projectResults.Count > 0)
        {
            return SolutionInitResult.Error($"No projects could be initialized. {failedCount} failed, {skippedCount} skipped.");
        }

        return SolutionInitResult.Success(
            solutionPath,
            solutionName,
            parseResult.Projects.Count,
            projectResults,
            createdFiles,
            updatedFiles,
            warnings,
            agentName);
    }

    private async Task<ProjectInitStatus> InitializeProjectAsync(SolutionProject project, bool force)
    {
        // Check if project file exists
        if (!project.Exists)
        {
            return new ProjectInitStatus(
                project.Name,
                project.FullPath,
                InitStatus.Skipped,
                $"Project file not found: {project.RelativePath}");
        }

        // Validate SDK-style project
        var validation = _projectDetector.ValidateProject(project.FullPath);
        if (!validation.IsValid)
        {
            return new ProjectInitStatus(
                project.Name,
                project.FullPath,
                InitStatus.Skipped,
                $"Non-SDK-style project skipped: {project.Name}");
        }

        // Check if already initialized
        if (_projectDetector.HasCalorTargets(project.FullPath) && !force)
        {
            return new ProjectInitStatus(
                project.Name,
                project.FullPath,
                InitStatus.AlreadyInitialized,
                null);
        }

        // Initialize the project
        var result = await _csprojInitializer.InitializeAsync(project.FullPath, force);

        if (!result.IsSuccess)
        {
            return new ProjectInitStatus(
                project.Name,
                project.FullPath,
                InitStatus.Failed,
                result.ErrorMessage);
        }

        return new ProjectInitStatus(
            project.Name,
            project.FullPath,
            result.WasAlreadyInitialized ? InitStatus.AlreadyInitialized : InitStatus.Initialized,
            null);
    }
}

/// <summary>
/// Status of a single project initialization.
/// </summary>
public enum InitStatus
{
    Initialized,
    AlreadyInitialized,
    Skipped,
    Failed
}

/// <summary>
/// Result of initializing a single project.
/// </summary>
public sealed record ProjectInitStatus(
    string ProjectName,
    string ProjectPath,
    InitStatus Status,
    string? Message);

/// <summary>
/// Result of solution initialization.
/// </summary>
public sealed class SolutionInitResult
{
    public bool IsSuccess { get; private init; }
    public string? SolutionPath { get; private init; }
    public string? SolutionName { get; private init; }
    public int TotalProjects { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? AgentName { get; private init; }
    public IReadOnlyList<ProjectInitStatus> ProjectResults { get; private init; } = Array.Empty<ProjectInitStatus>();
    public IReadOnlyList<string> CreatedFiles { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> UpdatedFiles { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; private init; } = Array.Empty<string>();

    public int InitializedCount => ProjectResults.Count(r => r.Status == InitStatus.Initialized);
    public int AlreadyInitializedCount => ProjectResults.Count(r => r.Status == InitStatus.AlreadyInitialized);
    public int SkippedCount => ProjectResults.Count(r => r.Status == InitStatus.Skipped);
    public int FailedCount => ProjectResults.Count(r => r.Status == InitStatus.Failed);

    private SolutionInitResult() { }

    public static SolutionInitResult Success(
        string solutionPath,
        string solutionName,
        int totalProjects,
        IReadOnlyList<ProjectInitStatus> projectResults,
        IReadOnlyList<string> createdFiles,
        IReadOnlyList<string> updatedFiles,
        IReadOnlyList<string> warnings,
        string? agentName)
    {
        return new SolutionInitResult
        {
            IsSuccess = true,
            SolutionPath = solutionPath,
            SolutionName = solutionName,
            TotalProjects = totalProjects,
            ProjectResults = projectResults,
            CreatedFiles = createdFiles,
            UpdatedFiles = updatedFiles,
            Warnings = warnings,
            AgentName = agentName
        };
    }

    public static SolutionInitResult Error(string message)
    {
        return new SolutionInitResult
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }
}
