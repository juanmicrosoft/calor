using System.CommandLine;
using Calor.Compiler.Init;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for initializing Calor projects with AI agent support and .csproj integration.
/// Supports both single projects and solutions.
/// </summary>
public static class InitCommand
{
    public static Command Create()
    {
        var aiOption = new Option<string?>(
            aliases: new[] { "--ai", "-a" },
            description: $"AI agent to configure (optional): {string.Join(", ", AiInitializerFactory.SupportedAgents)}");

        var projectOption = new Option<string?>(
            aliases: new[] { "--project", "-p" },
            description: "The .csproj file to configure (auto-detects if single .csproj exists)");

        var solutionOption = new Option<string?>(
            aliases: new[] { "--solution", "-s" },
            description: "The .sln or .slnx file to configure (initializes all projects in solution)");

        var forceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Overwrite existing files without prompting");

        var command = new Command("init", "Initialize the current directory for Calor development with AI coding agents")
        {
            aiOption,
            projectOption,
            solutionOption,
            forceOption
        };

        command.SetHandler(ExecuteAsync, aiOption, projectOption, solutionOption, forceOption);

        return command;
    }

    private static async Task ExecuteAsync(string? ai, string? project, string? solution, bool force)
    {
        try
        {
            var targetDirectory = Directory.GetCurrentDirectory();

            // Validate mutual exclusivity of --project and --solution
            if (!string.IsNullOrEmpty(project) && !string.IsNullOrEmpty(solution))
            {
                Console.Error.WriteLine("Error: Cannot specify both --project and --solution. Use one or the other.");
                Environment.ExitCode = 1;
                return;
            }

            // Validate AI agent type if provided
            IAiInitializer? aiInitializer = null;
            if (!string.IsNullOrEmpty(ai))
            {
                if (!AiInitializerFactory.IsSupported(ai))
                {
                    Console.Error.WriteLine($"Error: Unknown AI agent type: '{ai}'");
                    Console.Error.WriteLine($"Supported types: {string.Join(", ", AiInitializerFactory.SupportedAgents)}");
                    Environment.ExitCode = 1;
                    return;
                }
                aiInitializer = AiInitializerFactory.Create(ai);
            }

            // Determine mode: solution, explicit project, or auto-detect
            if (!string.IsNullOrEmpty(solution))
            {
                // Explicit solution mode
                await ExecuteSolutionModeAsync(targetDirectory, solution, force, aiInitializer);
                return;
            }

            if (!string.IsNullOrEmpty(project))
            {
                // Explicit project mode
                await ExecuteProjectModeAsync(targetDirectory, project, force, aiInitializer);
                return;
            }

            // Auto-detection mode: try solution first, then project
            var solutionDetector = new SolutionDetector();
            var solutionDetection = solutionDetector.Detect(targetDirectory);

            if (solutionDetection.IsSuccess)
            {
                // Found a solution - use solution mode
                await ExecuteSolutionModeAsync(targetDirectory, solutionDetection.SolutionPath!, force, aiInitializer);
                return;
            }

            if (solutionDetection.HasMultipleSolutions)
            {
                // Multiple solutions found - require explicit selection
                Console.Error.WriteLine($"Error: {solutionDetection.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            // No solution found - fall back to project mode
            await ExecuteProjectModeAsync(targetDirectory, null, force, aiInitializer);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task ExecuteSolutionModeAsync(
        string targetDirectory,
        string solutionPathOrName,
        bool force,
        IAiInitializer? aiInitializer)
    {
        var solutionDetector = new SolutionDetector();

        // Resolve solution path
        var solutionPath = Path.IsPathRooted(solutionPathOrName)
            ? solutionPathOrName
            : Path.Combine(targetDirectory, solutionPathOrName);

        if (!File.Exists(solutionPath))
        {
            Console.Error.WriteLine($"Error: Solution file not found: {solutionPath}");
            Environment.ExitCode = 1;
            return;
        }

        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var solutionInitializer = new SolutionInitializer();
        var result = await solutionInitializer.InitializeAsync(solutionPath, force, aiInitializer);

        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {result.ErrorMessage}");
            Environment.ExitCode = 1;
            return;
        }

        // Initialize .gitattributes for GitHub linguist
        var (gitAttrCreated, gitAttrUpdated) = await GitAttributesInitializer.InitializeAsync(solutionDirectory);

        // Check if calor is available in PATH
        var warnings = new List<string>(result.Warnings);
        if (!IsCalorcInPath())
        {
            warnings.Add("'calor' not found in PATH. Ensure calor is installed and accessible.");
        }

        // Show success message
        var version = EmbeddedResourceHelper.GetVersion();
        if (result.AgentName != null)
        {
            Console.WriteLine($"Initialized Calor solution for {result.AgentName} (calor v{version})");
        }
        else
        {
            Console.WriteLine($"Initialized Calor solution with MSBuild integration (calor v{version})");
        }

        Console.WriteLine();
        Console.WriteLine($"Solution: {result.SolutionName} ({result.TotalProjects} projects)");

        // Build lists of created and updated files including .gitattributes
        var createdFiles = new List<string>(result.CreatedFiles);
        var updatedFiles = new List<string>();
        if (gitAttrCreated)
        {
            createdFiles.Add(Path.Combine(solutionDirectory, ".gitattributes"));
        }
        else if (gitAttrUpdated)
        {
            updatedFiles.Add(Path.Combine(solutionDirectory, ".gitattributes"));
        }

        // Show created files
        if (createdFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Created files:");
            foreach (var file in createdFiles)
            {
                var relativePath = Path.GetRelativePath(solutionDirectory, file);
                Console.WriteLine($"  {relativePath}");
            }
        }

        // Show updated files (non-project files like .gitattributes)
        if (updatedFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Updated files:");
            foreach (var file in updatedFiles)
            {
                var relativePath = Path.GetRelativePath(solutionDirectory, file);
                Console.WriteLine($"  {relativePath}");
            }
        }

        // Show updated projects
        var updatedProjects = result.ProjectResults
            .Where(r => r.Status == InitStatus.Initialized)
            .ToList();

        if (updatedProjects.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Updated projects:");
            foreach (var proj in updatedProjects)
            {
                var relativePath = Path.GetRelativePath(solutionDirectory, proj.ProjectPath);
                Console.WriteLine($"  {relativePath}");
            }
        }

        // Show MSBuild configuration summary
        if (result.InitializedCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine("MSBuild configuration:");
            Console.WriteLine($"  - Added Calor compilation targets to {result.InitializedCount} projects");
        }

        // Show skipped/already initialized info
        if (result.AlreadyInitializedCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Skipped {result.AlreadyInitializedCount} projects (already initialized). Use --force to reinitialize.");
        }

        // Show warnings
        if (warnings.Count > 0)
        {
            Console.WriteLine();
            foreach (var warning in warnings)
            {
                Console.WriteLine($"Warning: {warning}");
            }
        }

        // Show next steps
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. Run 'calor analyze ./src' to find migration candidates");
        Console.WriteLine("  2. Create .calr files in your projects");
        Console.WriteLine("  3. Run 'dotnet build' to compile Calor to C#");
        if (result.AgentName == null)
        {
            Console.WriteLine();
            Console.WriteLine("Optional: Run 'calor init --ai claude' to add Claude Code skills");
        }
    }

    private static async Task ExecuteProjectModeAsync(
        string targetDirectory,
        string? specificProject,
        bool force,
        IAiInitializer? aiInitializer)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();
        string? agentName = null;

        // Step 1: Detect and validate .csproj file
        var detector = new ProjectDetector();
        var detection = detector.Detect(targetDirectory, specificProject);

        if (!detection.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {detection.ErrorMessage}");
            Environment.ExitCode = 1;
            return;
        }

        var projectPath = detection.ProjectPath!;

        // Step 2: Initialize AI agent configuration (if --ai specified)
        if (aiInitializer != null)
        {
            agentName = aiInitializer.AgentName;
            var aiResult = await aiInitializer.InitializeAsync(targetDirectory, force);

            if (!aiResult.Success)
            {
                foreach (var message in aiResult.Messages)
                {
                    Console.Error.WriteLine($"Error: {message}");
                }
                Environment.ExitCode = 1;
                return;
            }

            createdFiles.AddRange(aiResult.CreatedFiles);
            updatedFiles.AddRange(aiResult.UpdatedFiles);
            warnings.AddRange(aiResult.Warnings);
        }

        // Step 3: Initialize .csproj with Calor targets
        var csprojInitializer = new CsprojInitializer(detector);
        var csprojResult = await csprojInitializer.InitializeAsync(projectPath, force);

        if (!csprojResult.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {csprojResult.ErrorMessage}");
            Environment.ExitCode = 1;
            return;
        }

        if (csprojResult.WasAlreadyInitialized)
        {
            warnings.Add($"Project already has Calor targets: {Path.GetFileName(projectPath)}");
        }
        else
        {
            updatedFiles.Add(projectPath);
        }

        // Step 4: Initialize .gitattributes for GitHub linguist
        var (gitAttrCreated, gitAttrUpdated) = await GitAttributesInitializer.InitializeAsync(targetDirectory);
        if (gitAttrCreated)
        {
            createdFiles.Add(Path.Combine(targetDirectory, ".gitattributes"));
        }
        else if (gitAttrUpdated)
        {
            updatedFiles.Add(Path.Combine(targetDirectory, ".gitattributes"));
        }

        // Check if calor is available in PATH
        if (!IsCalorcInPath())
        {
            warnings.Add("'calor' not found in PATH. Ensure calor is installed and accessible.");
        }

        // Show success message
        var version = EmbeddedResourceHelper.GetVersion();
        if (agentName != null)
        {
            Console.WriteLine($"Initialized Calor project for {agentName} (calor v{version})");
        }
        else
        {
            Console.WriteLine($"Initialized Calor project with MSBuild integration (calor v{version})");
        }

        // Show created files
        if (createdFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Created files:");
            foreach (var file in createdFiles)
            {
                var relativePath = Path.GetRelativePath(targetDirectory, file);
                Console.WriteLine($"  {relativePath}");
            }
        }

        // Show updated files
        if (updatedFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Updated files:");
            foreach (var file in updatedFiles)
            {
                var relativePath = Path.GetRelativePath(targetDirectory, file);
                Console.WriteLine($"  {relativePath}");
            }
        }

        // Show .csproj changes
        if (!csprojResult.WasAlreadyInitialized && csprojResult.Changes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("MSBuild configuration:");
            foreach (var change in csprojResult.Changes)
            {
                Console.WriteLine($"  - {change}");
            }
        }

        // Show warnings
        if (warnings.Count > 0)
        {
            Console.WriteLine();
            foreach (var warning in warnings)
            {
                Console.WriteLine($"Warning: {warning}");
            }
        }

        // Show next steps
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. Run 'calor analyze ./src' to find migration candidates");
        Console.WriteLine("  2. Create .calr files in your project");
        Console.WriteLine("  3. Run 'dotnet build' to compile Calor to C#");
        if (agentName == null)
        {
            Console.WriteLine();
            Console.WriteLine("Optional: Run 'calor init --ai claude' to add Claude Code skills");
        }
    }

    private static bool IsCalorcInPath()
    {
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            var paths = pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            var calorNames = OperatingSystem.IsWindows()
                ? new[] { "calor.exe", "calor.cmd", "calor.bat" }
                : new[] { "calor" };

            foreach (var path in paths)
            {
                foreach (var name in calorNames)
                {
                    var fullPath = Path.Combine(path, name);
                    if (File.Exists(fullPath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
