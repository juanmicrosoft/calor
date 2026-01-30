using System.CommandLine;
using Opal.Compiler.Init;

namespace Opal.Compiler.Commands;

/// <summary>
/// CLI command for initializing OPAL projects with AI agent support and .csproj integration.
/// </summary>
public static class InitCommand
{
    public static Command Create()
    {
        var aiOption = new Option<string>(
            aliases: new[] { "--ai", "-a" },
            description: $"The AI agent to configure ({string.Join(", ", AiInitializerFactory.SupportedAgents)})")
        {
            IsRequired = true
        };

        var projectOption = new Option<string?>(
            aliases: new[] { "--project", "-p" },
            description: "The .csproj file to configure (auto-detects if single .csproj exists)");

        var forceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Overwrite existing files without prompting");

        var command = new Command("init", "Initialize the current directory for OPAL development with AI coding agents")
        {
            aiOption,
            projectOption,
            forceOption
        };

        command.SetHandler(ExecuteAsync, aiOption, projectOption, forceOption);

        return command;
    }

    private static async Task ExecuteAsync(string ai, string? project, bool force)
    {
        try
        {
            var targetDirectory = Directory.GetCurrentDirectory();
            var createdFiles = new List<string>();
            var updatedFiles = new List<string>();
            var warnings = new List<string>();

            // Validate AI agent type
            if (!AiInitializerFactory.IsSupported(ai))
            {
                Console.Error.WriteLine($"Error: Unknown AI agent type: '{ai}'");
                Console.Error.WriteLine($"Supported types: {string.Join(", ", AiInitializerFactory.SupportedAgents)}");
                Environment.ExitCode = 1;
                return;
            }

            // Step 1: Detect and validate .csproj file
            var detector = new ProjectDetector();
            var detection = detector.Detect(targetDirectory, project);

            if (!detection.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {detection.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            var projectPath = detection.ProjectPath!;

            // Step 2: Initialize AI agent configuration
            var aiInitializer = AiInitializerFactory.Create(ai);
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

            // Step 3: Initialize .csproj with OPAL targets
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
                warnings.Add($"Project already has OPAL targets: {Path.GetFileName(projectPath)}");
            }
            else
            {
                updatedFiles.Add(projectPath);
            }

            // Check if opalc is available in PATH
            if (!IsOpalcInPath())
            {
                warnings.Add("'opalc' not found in PATH. Ensure opalc is installed and accessible.");
            }

            // Show success message
            var version = EmbeddedResourceHelper.GetVersion();
            Console.WriteLine($"Initialized OPAL project for {aiInitializer.AgentName} (opalc v{version})");

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
            Console.WriteLine("  1. Create .opal files in your project");
            Console.WriteLine("  2. Run 'dotnet build' to compile OPAL to C#");
            Console.WriteLine("  3. Generated code will be in obj/<config>/<tfm>/opal/");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static bool IsOpalcInPath()
    {
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            var paths = pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            var opalcNames = OperatingSystem.IsWindows()
                ? new[] { "opalc.exe", "opalc.cmd", "opalc.bat" }
                : new[] { "opalc" };

            foreach (var path in paths)
            {
                foreach (var name in opalcNames)
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
