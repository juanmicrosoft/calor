namespace Calor.Compiler.Init;

/// <summary>
/// Detects and validates solution files (.sln and .slnx) in a directory.
/// </summary>
public sealed class SolutionDetector
{
    /// <summary>
    /// Detects solution files in the specified directory.
    /// Priority: .slnx files first (newer XML format), then .sln files.
    /// </summary>
    /// <param name="directory">The directory to search.</param>
    /// <param name="specificSolution">Optional specific solution file path.</param>
    /// <returns>Detection result with the solution path or an error.</returns>
    public SolutionDetectionResult Detect(string directory, string? specificSolution = null)
    {
        if (!Directory.Exists(directory))
        {
            return SolutionDetectionResult.Error($"Directory not found: {directory}");
        }

        // If a specific solution is specified, use it
        if (!string.IsNullOrEmpty(specificSolution))
        {
            var solutionPath = Path.IsPathRooted(specificSolution)
                ? specificSolution
                : Path.Combine(directory, specificSolution);

            if (!File.Exists(solutionPath))
            {
                return SolutionDetectionResult.Error($"Solution file not found: {solutionPath}");
            }

            return SolutionDetectionResult.Success(solutionPath);
        }

        // Auto-detect solution files - prefer .slnx over .sln
        var slnxFiles = Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly);
        var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly);

        var allSolutions = slnxFiles.Concat(slnFiles).ToArray();

        if (allSolutions.Length == 0)
        {
            return SolutionDetectionResult.NotFound();
        }

        if (allSolutions.Length == 1)
        {
            return SolutionDetectionResult.Success(allSolutions[0]);
        }

        // Multiple solutions found - require explicit specification
        var solutionNames = allSolutions.Select(s => Path.GetFileName(s)).ToList();
        return SolutionDetectionResult.MultipleSolutions(allSolutions,
            $"Multiple solution files found. Please specify one with --solution:\n  " +
            string.Join("\n  ", solutionNames));
    }

    /// <summary>
    /// Parses a solution file and returns information about all its projects.
    /// </summary>
    public SolutionParseResult ParseSolution(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            return SolutionParseResult.Error($"Solution file not found: {solutionPath}");
        }

        try
        {
            var projects = SolutionParser.Parse(solutionPath).ToList();
            return SolutionParseResult.Success(solutionPath, projects);
        }
        catch (Exception ex)
        {
            return SolutionParseResult.Error($"Failed to parse solution: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of solution detection.
/// </summary>
public sealed class SolutionDetectionResult
{
    public bool IsSuccess { get; private init; }
    public bool WasNotFound { get; private init; }
    public string? SolutionPath { get; private init; }
    public string? ErrorMessage { get; private init; }
    public IReadOnlyList<string>? AvailableSolutions { get; private init; }
    public bool HasMultipleSolutions => AvailableSolutions?.Count > 1;

    private SolutionDetectionResult() { }

    public static SolutionDetectionResult Success(string solutionPath)
    {
        return new SolutionDetectionResult
        {
            IsSuccess = true,
            SolutionPath = solutionPath
        };
    }

    public static SolutionDetectionResult NotFound()
    {
        return new SolutionDetectionResult
        {
            IsSuccess = false,
            WasNotFound = true
        };
    }

    public static SolutionDetectionResult Error(string message)
    {
        return new SolutionDetectionResult
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }

    public static SolutionDetectionResult MultipleSolutions(string[] solutions, string message)
    {
        return new SolutionDetectionResult
        {
            IsSuccess = false,
            ErrorMessage = message,
            AvailableSolutions = solutions
        };
    }
}

/// <summary>
/// Result of parsing a solution file.
/// </summary>
public sealed class SolutionParseResult
{
    public bool IsSuccess { get; private init; }
    public string? SolutionPath { get; private init; }
    public string? ErrorMessage { get; private init; }
    public IReadOnlyList<SolutionProject> Projects { get; private init; } = Array.Empty<SolutionProject>();

    private SolutionParseResult() { }

    public static SolutionParseResult Success(string solutionPath, IReadOnlyList<SolutionProject> projects)
    {
        return new SolutionParseResult
        {
            IsSuccess = true,
            SolutionPath = solutionPath,
            Projects = projects
        };
    }

    public static SolutionParseResult Error(string message)
    {
        return new SolutionParseResult
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }
}
