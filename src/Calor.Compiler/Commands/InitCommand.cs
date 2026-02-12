using System.CommandLine;
using System.Diagnostics;
using Calor.Compiler.Init;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for initializing Calor projects with AI agent support and .csproj integration.
/// Supports both single projects and solutions.
/// </summary>
public static class InitCommand
{
    public static Command Create()
    {
        var aiOption = new Option<string[]>(
            aliases: new[] { "--ai", "-a" },
            description: $"AI agent(s) to configure (can specify multiple): {string.Join(", ", AiInitializerFactory.SupportedAgents)}")
        {
            AllowMultipleArgumentsPerToken = true
        };

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

    private static async Task ExecuteAsync(string[] ai, string? project, string? solution, bool force)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("init");
        var sw = Stopwatch.StartNew();

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

            // Validate AI agent types and create initializers
            var aiInitializers = new List<IAiInitializer>();
            var agentNames = new List<string>();
            if (ai.Length > 0)
            {
                foreach (var agent in ai)
                {
                    if (!AiInitializerFactory.IsSupported(agent))
                    {
                        Console.Error.WriteLine($"Error: Unknown AI agent type: '{agent}'");
                        Console.Error.WriteLine($"Supported types: {string.Join(", ", AiInitializerFactory.SupportedAgents)}");
                        Environment.ExitCode = 1;
                        return;
                    }
                    aiInitializers.Add(AiInitializerFactory.Create(agent));
                    agentNames.Add(agent.ToLowerInvariant());
                }
            }

            // Determine mode: solution, explicit project, or auto-detect
            if (!string.IsNullOrEmpty(solution))
            {
                // Explicit solution mode
                await ExecuteSolutionModeAsync(targetDirectory, solution, force, aiInitializers, agentNames);
                return;
            }

            if (!string.IsNullOrEmpty(project))
            {
                // Explicit project mode
                await ExecuteProjectModeAsync(targetDirectory, project, force, aiInitializers, agentNames);
                return;
            }

            // Auto-detection mode: try solution first, then project
            var solutionDetector = new SolutionDetector();
            var solutionDetection = solutionDetector.Detect(targetDirectory);

            if (solutionDetection.IsSuccess)
            {
                // Found a solution - use solution mode
                await ExecuteSolutionModeAsync(targetDirectory, solutionDetection.SolutionPath!, force, aiInitializers, agentNames);
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
            await ExecuteProjectModeAsync(targetDirectory, null, force, aiInitializers, agentNames);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            telemetry?.TrackException(ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            sw.Stop();
            telemetry?.TrackCommand("init", Environment.ExitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            });
            if (Environment.ExitCode != 0)
            {
                IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "init", "Init failed");
            }
        }
    }

    private static async Task ExecuteSolutionModeAsync(
        string targetDirectory,
        string solutionPathOrName,
        bool force,
        List<IAiInitializer> aiInitializers,
        List<string> agentNames)
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
        var firstInitializer = aiInitializers.Count > 0 ? aiInitializers[0] : null;
        var result = await solutionInitializer.InitializeAsync(solutionPath, force, firstInitializer);

        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {result.ErrorMessage}");
            Environment.ExitCode = 1;
            return;
        }

        // Initialize remaining AI agents (solution initializer handles the first one)
        for (var i = 1; i < aiInitializers.Count; i++)
        {
            var aiResult = await aiInitializers[i].InitializeAsync(solutionDirectory, force);
            if (!aiResult.Success)
            {
                foreach (var message in aiResult.Messages)
                    Console.Error.WriteLine($"Error: {message}");
                Environment.ExitCode = 1;
                return;
            }
        }

        // Always create/update .calor/config.json
        var configCreated = CalorConfigManager.AddAgents(solutionDirectory, agentNames, force);

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
        if (agentNames.Count > 0)
        {
            Console.WriteLine($"Initialized Calor solution for {string.Join(", ", agentNames)} (calor v{version})");
        }
        else
        {
            Console.WriteLine($"Initialized Calor solution with MSBuild integration (calor v{version})");
        }

        Console.WriteLine();
        Console.WriteLine($"Solution: {result.SolutionName} ({result.TotalProjects} projects)");

        // Build lists of created and updated files including .gitattributes and .calor/config.json
        var createdFiles = new List<string>(result.CreatedFiles);
        var updatedFiles = new List<string>();

        if (configCreated)
        {
            createdFiles.Add(CalorConfigManager.GetConfigPath(solutionDirectory));
        }
        else
        {
            updatedFiles.Add(CalorConfigManager.GetConfigPath(solutionDirectory));
        }

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

        // Show agent-specific configuration
        if (agentNames.Any(a => a == "claude"))
        {
            Console.WriteLine();
            Console.WriteLine("Agent configuration:");
            Console.WriteLine("  - MCP server 'calor-lsp' configured for language features");
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
        if (agentNames.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Optional: Run 'calor init --ai claude' to add Claude Code skills");
        }
        else
        {
            // Show configured agents summary
            var currentConfig = CalorConfigManager.Read(solutionDirectory);
            if (currentConfig != null && currentConfig.Agents.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Configured agents: {string.Join(", ", currentConfig.Agents.Select(a => a.Name))}");
            }
        }
    }

    private static async Task ExecuteProjectModeAsync(
        string targetDirectory,
        string? specificProject,
        bool force,
        List<IAiInitializer> aiInitializers,
        List<string> agentNames)
    {
        var createdFiles = new List<string>();
        var updatedFiles = new List<string>();
        var warnings = new List<string>();

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

        // Step 2: Initialize AI agent configurations (if --ai specified)
        var agentConfigMessages = new List<string>();
        foreach (var aiInitializer in aiInitializers)
        {
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

            // Collect agent-specific configuration messages (skip the first "Initialized..." message)
            foreach (var message in aiResult.Messages.Skip(1))
            {
                agentConfigMessages.Add(message);
            }
        }

        // Step 3: Always create/update .calor/config.json
        var configCreated = CalorConfigManager.AddAgents(targetDirectory, agentNames, force);
        if (configCreated)
        {
            createdFiles.Add(CalorConfigManager.GetConfigPath(targetDirectory));
        }
        else
        {
            updatedFiles.Add(CalorConfigManager.GetConfigPath(targetDirectory));
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
        if (agentNames.Count > 0)
        {
            Console.WriteLine($"Initialized Calor project for {string.Join(", ", agentNames)} (calor v{version})");
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

        // Show agent-specific configuration
        if (agentConfigMessages.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Agent configuration:");
            foreach (var message in agentConfigMessages)
            {
                Console.WriteLine(message);
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
        if (agentNames.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Optional: Run 'calor init --ai claude' to add Claude Code skills");
        }
        else
        {
            // Show configured agents summary
            var currentConfig = CalorConfigManager.Read(targetDirectory);
            if (currentConfig != null && currentConfig.Agents.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Configured agents: {string.Join(", ", currentConfig.Agents.Select(a => a.Name))}");
            }
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
