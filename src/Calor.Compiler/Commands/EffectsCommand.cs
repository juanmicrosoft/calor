using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Parsing;

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
        command.AddCommand(CreateSuggestCommand());

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
        var resolver = new EffectResolver(loader);
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
            typePart = EffectEnforcementPass.MapShortTypeNameToFullName(typePart);
        }

        return (typePart, methodName);
    }

    private sealed record TypeListEntry(string Type, string Source, List<string> DefaultEffects, int MethodCount);

    // ========================================================================
    // effects suggest
    // ========================================================================

    private static Command CreateSuggestCommand()
    {
        var inputOption = new Option<FileInfo[]>(
            aliases: ["--input", "-i"],
            description: "Calor source file(s) to analyze")
        { IsRequired = true, Arity = ArgumentArity.OneOrMore };

        var projectOption = new Option<DirectoryInfo?>(
            aliases: ["--project", "-p"],
            description: "Project directory for manifest loading context");

        var solutionOption = new Option<DirectoryInfo?>(
            aliases: ["--solution", "-s"],
            description: "Solution directory for manifest loading context");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Output file path (default: .calor-effects.suggested.json)");

        var mergeOption = new Option<bool>(
            aliases: ["--merge"],
            description: "Merge into existing .calor-effects.json instead of writing a separate file",
            getDefaultValue: () => false);

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Output JSON to stdout",
            getDefaultValue: () => false);

        var command = new Command("suggest", "Generate a manifest template for unresolved external calls")
        {
            inputOption, projectOption, solutionOption, outputOption, mergeOption, jsonOption
        };

        command.SetHandler(ExecuteSuggest, inputOption, projectOption, solutionOption, outputOption, mergeOption, jsonOption);

        return command;
    }

    private static void ExecuteSuggest(
        FileInfo[] inputFiles,
        DirectoryInfo? project,
        DirectoryInfo? solution,
        FileInfo? output,
        bool merge,
        bool json)
    {
        if (merge && json)
        {
            Console.Error.WriteLine("Error: --merge and --json are mutually exclusive.");
            Environment.ExitCode = 2;
            return;
        }

        // Step 1+2: Parse each file and build combined function maps
        var allModules = new List<ModuleNode>();
        var combinedFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        var combinedMethodNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var inputFile in inputFiles)
        {
            if (!inputFile.Exists)
            {
                Console.Error.WriteLine($"Error: File not found: {inputFile.FullName}");
                Environment.ExitCode = 1;
                return;
            }

            var source = File.ReadAllText(inputFile.FullName);
            var diagnostics = new DiagnosticBag();

            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAll();
            if (diagnostics.HasErrors)
            {
                Console.Error.WriteLine($"Error: Failed to lex {inputFile.Name}:");
                foreach (var diag in diagnostics.Errors)
                    Console.Error.WriteLine($"  {diag.Message}");
                Environment.ExitCode = 1;
                return;
            }

            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();
            if (diagnostics.HasErrors)
            {
                Console.Error.WriteLine($"Error: Failed to parse {inputFile.Name}:");
                foreach (var diag in diagnostics.Errors)
                    Console.Error.WriteLine($"  {diag.Message}");
                Environment.ExitCode = 1;
                return;
            }

            allModules.Add(module);

            // Build internal function map for this module
            var callGraph = CallGraphAnalysis.Build(module);
            foreach (var name in callGraph.FunctionNameToId.Keys)
                combinedFunctionNames.Add(name);
            foreach (var name in callGraph.MethodNameToIds.Keys)
                combinedMethodNames.Add(name);
        }

        // Step 3: Collect all external calls from all modules
        var allCalls = new List<CollectedCall>();
        foreach (var module in allModules)
        {
            allCalls.AddRange(ExternalCallCollector.Collect(module));
        }

        // Step 4: Filter internal calls, then resolve against manifests
        var loader = new ManifestLoader();
        var resolver = new EffectResolver(loader);
        resolver.Initialize(project?.FullName, solution?.FullName);

        var unresolvedCalls = allCalls
            .Where(call => !IsInternalCall(call, combinedFunctionNames, combinedMethodNames))
            .Where(call =>
            {
                if (call.Kind == CallKind.Constructor)
                    return resolver.ResolveConstructor(call.TypeName).Status == EffectResolutionStatus.Unknown;
                return resolver.Resolve(call.TypeName, call.MethodName).Status == EffectResolutionStatus.Unknown;
            })
            .Distinct()
            .ToList();

        // Handle "all resolved" case
        if (unresolvedCalls.Count == 0)
        {
            if (!json)
                Console.WriteLine("All external calls are resolved. No supplemental manifest needed.");
            else
                Console.WriteLine("{\"unresolved\": 0, \"types\": []}");
            return;
        }

        // Step 5: Build manifest
        var manifest = BuildSuggestedManifest(unresolvedCalls, inputFiles);

        // Output
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var manifestJson = JsonSerializer.Serialize(manifest, jsonOptions);

        if (json)
        {
            Console.WriteLine(manifestJson);
            return;
        }

        // Console summary
        var fileNames = string.Join(", ", inputFiles.Select(f => f.Name));
        Console.WriteLine($"Analyzing {fileNames}...");

        var groupedByType = unresolvedCalls
            .GroupBy(c => c.TypeName)
            .OrderBy(g => g.Key);

        var totalMethods = unresolvedCalls.Count;
        var totalTypes = groupedByType.Count();
        Console.WriteLine($"Found {totalMethods} unresolved external call{(totalMethods != 1 ? "s" : "")} across {totalTypes} type{(totalTypes != 1 ? "s" : "")}:");
        Console.WriteLine();

        foreach (var group in groupedByType)
        {
            var typeName = group.Key;
            var isLikelyVariable = !typeName.Contains('.') &&
                typeName.Length > 0 && char.IsLower(typeName[0]) &&
                EffectEnforcementPass.MapShortTypeNameToFullName(typeName) == typeName;

            if (isLikelyVariable)
                Console.WriteLine($"  {typeName}  (⚠ likely a variable name — replace with the actual type)");
            else
                Console.WriteLine($"  {typeName}");

            foreach (var call in group.OrderBy(c => c.MethodName))
            {
                var kindLabel = call.Kind switch
                {
                    CallKind.Constructor => " [ctor]",
                    CallKind.Getter => " [get]",
                    CallKind.Setter => " [set]",
                    _ => ""
                };
                Console.WriteLine($"    {call.MethodName,-30} → []  (fill in effects){kindLabel}");
            }
            Console.WriteLine();
        }

        // Write file
        var outputPath = output?.FullName;
        if (merge)
        {
            outputPath ??= Path.Combine(project?.FullName ?? ".", ".calor-effects.json");
            if (File.Exists(outputPath))
            {
                manifest = MergeManifest(outputPath, manifest);
                manifestJson = JsonSerializer.Serialize(manifest, jsonOptions);
            }
        }
        else
        {
            outputPath ??= Path.Combine(project?.FullName ?? ".", ".calor-effects.suggested.json");
        }

        try
        {
            File.WriteAllText(outputPath, manifestJson);
            Console.WriteLine($"Written to {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing file: {ex.Message}");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Hint: Common effects are cw (console), fs:r/fs:w (file), db:r/db:w (database),");
        Console.WriteLine("      net:r/net:w (network), mut (mutation), rand (random), time (system time).");
        Console.WriteLine("      Use [] for pure methods with no side effects.");
    }

    private static bool IsInternalCall(
        CollectedCall call,
        HashSet<string> functionNames,
        HashSet<string> methodNames)
    {
        // For constructor calls, the type name itself isn't a function
        if (call.Kind == CallKind.Constructor)
            return false;

        // Check if the method name matches an internal function
        if (functionNames.Contains(call.MethodName))
            return true;

        // For dotted targets, check bare method name against class methods
        if (methodNames.Contains(call.MethodName))
            return true;

        // Check the full "Type.Method" as a function name (some modules use dotted names)
        var fullTarget = $"{call.TypeName}.{call.MethodName}";
        if (functionNames.Contains(fullTarget))
            return true;

        return false;
    }

    private static EffectManifest BuildSuggestedManifest(
        List<CollectedCall> unresolvedCalls,
        FileInfo[] inputFiles)
    {
        var fileNames = string.Join(", ", inputFiles.Select(f => f.Name));

        var manifest = new EffectManifest
        {
            Version = "1.0",
            Description = $"Suggested effects for unresolved calls in {fileNames}",
            Confidence = "inferred",
            GeneratedBy = "calor effects suggest",
            GeneratedAt = DateTime.UtcNow.ToString("O")
        };

        var grouped = unresolvedCalls
            .GroupBy(c => c.TypeName)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var mapping = new TypeMapping { Type = group.Key };

            foreach (var call in group.OrderBy(c => c.MethodName).DistinctBy(c => (c.MethodName, c.Kind)))
            {
                switch (call.Kind)
                {
                    case CallKind.Constructor:
                        mapping.Constructors ??= new Dictionary<string, List<string>>();
                        mapping.Constructors["()"] = new List<string>();
                        break;
                    case CallKind.Getter:
                        mapping.Getters ??= new Dictionary<string, List<string>>();
                        mapping.Getters[call.MethodName] = new List<string>();
                        break;
                    case CallKind.Setter:
                        mapping.Setters ??= new Dictionary<string, List<string>>();
                        mapping.Setters[call.MethodName] = new List<string>();
                        break;
                    default:
                        mapping.Methods ??= new Dictionary<string, List<string>>();
                        mapping.Methods[call.MethodName] = new List<string>();
                        break;
                }
            }

            manifest.Mappings.Add(mapping);
        }

        return manifest;
    }

    internal static EffectManifest MergeManifest(string existingPath, EffectManifest suggested)
    {
        try
        {
            var existingJson = File.ReadAllText(existingPath);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var existing = JsonSerializer.Deserialize<EffectManifest>(existingJson, jsonOptions);
            if (existing == null)
                return suggested;

            // Preserve existing metadata
            suggested.Version = existing.Version;
            suggested.Description = existing.Description;
            suggested.Confidence = existing.Confidence;
            suggested.GeneratedBy = existing.GeneratedBy;
            suggested.GeneratedAt = existing.GeneratedAt;
            suggested.Library = existing.Library;
            suggested.LibraryVersion = existing.LibraryVersion;
            suggested.NamespaceDefaults = existing.NamespaceDefaults;

            // Merge mappings: add new types/methods, preserve existing
            var existingTypes = existing.Mappings.ToDictionary(m => m.Type, StringComparer.Ordinal);

            foreach (var suggestedMapping in suggested.Mappings)
            {
                if (existingTypes.TryGetValue(suggestedMapping.Type, out var existingMapping))
                {
                    // Type exists — merge methods
                    MergeDictionary(existingMapping.Methods, suggestedMapping.Methods);
                    MergeDictionary(existingMapping.Getters, suggestedMapping.Getters);
                    MergeDictionary(existingMapping.Setters, suggestedMapping.Setters);
                    MergeDictionary(existingMapping.Constructors, suggestedMapping.Constructors);
                }
                else
                {
                    // New type — add it
                    existing.Mappings.Add(suggestedMapping);
                }
            }

            return existing;
        }
        catch
        {
            // If existing file can't be parsed, return suggested as-is
            return suggested;
        }
    }

    internal static void MergeDictionary(
        Dictionary<string, List<string>>? existing,
        Dictionary<string, List<string>>? suggested)
    {
        if (suggested == null) return;
        if (existing == null) return;

        foreach (var (key, value) in suggested)
        {
            // Only add if the key doesn't exist (never overwrite existing entries)
            existing.TryAdd(key, value);
        }
    }
}
