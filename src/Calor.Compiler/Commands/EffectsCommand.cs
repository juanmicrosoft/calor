using System.CommandLine;
using System.Text.Json;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI commands for working with effect manifests and resolving effects.
/// </summary>
public static class EffectsCommand
{
    public static Command Create()
    {
        var command = new Command("effects", "Work with effect declarations and manifests");

        command.AddCommand(CreateResolveCommand());
        command.AddCommand(CreateValidateCommand());
        command.AddCommand(CreateListCommand());

        return command;
    }

    private static Command CreateResolveCommand()
    {
        var signatureArgument = new Argument<string>(
            name: "signature",
            description: "Method signature to resolve (e.g., 'System.Console.WriteLine' or 'File.ReadAllText')");

        var projectOption = new Option<DirectoryInfo?>(
            aliases: ["--project", "-p"],
            description: "Project directory for loading project-local manifests");

        var solutionOption = new Option<DirectoryInfo?>(
            aliases: ["--solution", "-s"],
            description: "Solution directory for loading solution-level manifests");

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Output in JSON format");

        var command = new Command("resolve", "Resolve effects for a method signature")
        {
            signatureArgument,
            projectOption,
            solutionOption,
            jsonOption
        };

        command.SetHandler(ExecuteResolve, signatureArgument, projectOption, solutionOption, jsonOption);

        return command;
    }

    private static Command CreateValidateCommand()
    {
        var projectOption = new Option<DirectoryInfo?>(
            aliases: ["--project", "-p"],
            description: "Project directory for loading project-local manifests");

        var solutionOption = new Option<DirectoryInfo?>(
            aliases: ["--solution", "-s"],
            description: "Solution directory for loading solution-level manifests");

        var command = new Command("validate", "Validate all manifests in search path")
        {
            projectOption,
            solutionOption
        };

        command.SetHandler(ExecuteValidate, projectOption, solutionOption);

        return command;
    }

    private static Command CreateListCommand()
    {
        var projectOption = new Option<DirectoryInfo?>(
            aliases: ["--project", "-p"],
            description: "Project directory for loading project-local manifests");

        var solutionOption = new Option<DirectoryInfo?>(
            aliases: ["--solution", "-s"],
            description: "Solution directory for loading solution-level manifests");

        var typeOption = new Option<string?>(
            aliases: ["--type", "-t"],
            description: "Filter by type name (partial match)");

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Output in JSON format");

        var command = new Command("list", "List all types with effect declarations")
        {
            projectOption,
            solutionOption,
            typeOption,
            jsonOption
        };

        command.SetHandler(ExecuteList, projectOption, solutionOption, typeOption, jsonOption);

        return command;
    }

    private static void ExecuteResolve(string signature, DirectoryInfo? project, DirectoryInfo? solution, bool json)
    {
        var (typeName, methodName) = ParseSignature(signature);
        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
        {
            Console.Error.WriteLine($"Error: Could not parse signature '{signature}'");
            Console.Error.WriteLine("Expected format: Type.Method (e.g., 'Console.WriteLine' or 'System.IO.File.ReadAllText')");
            Environment.ExitCode = 1;
            return;
        }

        var loader = new ManifestLoader();
        var resolver = new EffectResolver(loader, EffectsCatalog.CreateDefault());
        resolver.Initialize(project?.FullName, solution?.FullName);

        var resolution = resolver.Resolve(typeName, methodName);

        if (json)
        {
            var output = new
            {
                signature = $"{typeName}.{methodName}",
                status = resolution.Status.ToString(),
                effects = resolution.Effects.IsUnknown
                    ? new[] { "unknown" }
                    : resolution.Effects.Effects.Select(e => EffectSetExtensions.ToSurfaceCode(e.Kind, e.Value)).ToArray(),
                source = resolution.Source
            };
            Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Signature: {typeName}.{methodName}");
            Console.WriteLine($"Status:    {resolution.Status}");
            Console.WriteLine($"Effects:   {resolution.Effects.ToDisplayString()}");
            Console.WriteLine($"Source:    {resolution.Source}");
        }

        // Report any load errors
        if (loader.LoadErrors.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Manifest load errors:");
            foreach (var error in loader.LoadErrors)
            {
                Console.Error.WriteLine($"  - {error}");
            }
        }
    }

    private static void ExecuteValidate(DirectoryInfo? project, DirectoryInfo? solution)
    {
        var loader = new ManifestLoader();
        loader.LoadAll(project?.FullName, solution?.FullName);

        Console.WriteLine($"Loaded {loader.LoadedManifests.Count} manifest(s):");
        foreach (var (manifest, source) in loader.LoadedManifests)
        {
            Console.WriteLine($"  - {source} ({manifest.Mappings.Count} type mappings)");
        }

        var errors = loader.ValidateManifests();

        if (loader.LoadErrors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Load errors:");
            foreach (var error in loader.LoadErrors)
            {
                Console.WriteLine($"  [ERROR] {error}");
            }
        }

        if (errors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Validation errors:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  [ERROR] {error}");
            }
            Environment.ExitCode = 1;
        }
        else if (loader.LoadErrors.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("All manifests are valid.");
        }
    }

    private static void ExecuteList(DirectoryInfo? project, DirectoryInfo? solution, string? typeFilter, bool json)
    {
        var loader = new ManifestLoader();
        loader.LoadAll(project?.FullName, solution?.FullName);

        var types = new List<TypeListEntry>();

        foreach (var (manifest, source) in loader.LoadedManifests)
        {
            foreach (var mapping in manifest.Mappings)
            {
                if (!string.IsNullOrEmpty(typeFilter) &&
                    !mapping.Type.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var methodCount = (mapping.Methods?.Count ?? 0) +
                                  (mapping.Getters?.Count ?? 0) +
                                  (mapping.Setters?.Count ?? 0) +
                                  (mapping.Constructors?.Count ?? 0);

                types.Add(new TypeListEntry(
                    mapping.Type,
                    source.FilePath,
                    mapping.DefaultEffects ?? new List<string>(),
                    methodCount
                ));
            }
        }

        // Also include built-in catalog types
        var builtInTypes = new HashSet<string>();
        foreach (var sig in EffectsCatalog.CreateDefault().AllSignatures)
        {
            var colonIndex = sig.IndexOf("::", StringComparison.Ordinal);
            if (colonIndex > 0)
            {
                var typeName = sig[..colonIndex];
                if (string.IsNullOrEmpty(typeFilter) ||
                    typeName.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    builtInTypes.Add(typeName);
                }
            }
        }

        foreach (var typeName in builtInTypes.OrderBy(t => t))
        {
            var existing = types.FirstOrDefault(t => t.Type == typeName);
            if (existing == null)
            {
                types.Add(new TypeListEntry(typeName, "built-in", new List<string>(), 0));
            }
        }

        types = types.OrderBy(t => t.Type).ToList();

        if (json)
        {
            var output = types.Select(t => new
            {
                type = t.Type,
                source = t.Source,
                defaultEffects = t.DefaultEffects,
                methodCount = t.MethodCount
            });
            Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Types with effect declarations ({types.Count}):");
            Console.WriteLine();

            foreach (var entry in types)
            {
                var defaultStr = entry.DefaultEffects.Count > 0
                    ? $" (default: {string.Join(", ", entry.DefaultEffects)})"
                    : entry.DefaultEffects.Count == 0 && entry.MethodCount == 0 ? " (pure)" : "";
                Console.WriteLine($"  {entry.Type}{defaultStr}");
                Console.WriteLine($"    Source: {entry.Source}");
                if (entry.MethodCount > 0)
                {
                    Console.WriteLine($"    Methods: {entry.MethodCount}");
                }
            }
        }
    }

    private static (string TypeName, string MethodName) ParseSignature(string signature)
    {
        // Handle patterns like "Console.WriteLine", "File.ReadAllText", "System.IO.File.ReadAllText"
        var lastDot = signature.LastIndexOf('.');
        if (lastDot <= 0)
            return ("", "");

        var methodName = signature[(lastDot + 1)..];
        var typePart = signature[..lastDot];

        // If type part doesn't contain a dot, try common namespaces
        if (!typePart.Contains('.'))
        {
            // Map common short names to full types
            typePart = typePart switch
            {
                "Console" => "System.Console",
                "File" => "System.IO.File",
                "Directory" => "System.IO.Directory",
                "Path" => "System.IO.Path",
                "Random" => "System.Random",
                "DateTime" => "System.DateTime",
                "Environment" => "System.Environment",
                "Process" => "System.Diagnostics.Process",
                "HttpClient" => "System.Net.Http.HttpClient",
                "Math" => "System.Math",
                "Guid" => "System.Guid",
                _ => typePart
            };
        }

        return (typePart, methodName);
    }

    private sealed record TypeListEntry(string Type, string Source, List<string> DefaultEffects, int MethodCount);
}
