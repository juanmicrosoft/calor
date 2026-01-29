using System.CommandLine;
using Opal.Compiler.Init;

namespace Opal.Compiler.Commands;

/// <summary>
/// CLI command for initializing OPAL projects with AI agent support.
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

        var forceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Overwrite existing files without prompting");

        var command = new Command("init", "Initialize the current directory for OPAL development with AI coding agents")
        {
            aiOption,
            forceOption
        };

        command.SetHandler(ExecuteAsync, aiOption, forceOption);

        return command;
    }

    private static async Task ExecuteAsync(string ai, bool force)
    {
        try
        {
            if (!AiInitializerFactory.IsSupported(ai))
            {
                Console.Error.WriteLine($"Error: Unknown AI agent type: '{ai}'");
                Console.Error.WriteLine($"Supported types: {string.Join(", ", AiInitializerFactory.SupportedAgents)}");
                Environment.ExitCode = 1;
                return;
            }

            var initializer = AiInitializerFactory.Create(ai);
            var targetDirectory = Directory.GetCurrentDirectory();

            var result = await initializer.InitializeAsync(targetDirectory, force);

            if (!result.Success)
            {
                foreach (var message in result.Messages)
                {
                    Console.Error.WriteLine($"Error: {message}");
                }
                Environment.ExitCode = 1;
                return;
            }

            // Show success message
            foreach (var message in result.Messages)
            {
                Console.WriteLine(message);
            }

            // Show created files
            if (result.CreatedFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Created files:");
                foreach (var file in result.CreatedFiles)
                {
                    var relativePath = Path.GetRelativePath(targetDirectory, file);
                    Console.WriteLine($"  {relativePath}");
                }
            }

            // Show updated files
            if (result.UpdatedFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Updated files:");
                foreach (var file in result.UpdatedFiles)
                {
                    var relativePath = Path.GetRelativePath(targetDirectory, file);
                    Console.WriteLine($"  {relativePath}");
                }
            }

            // Show warnings
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine();
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"Warning: {warning}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
