using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.IL;
using Calor.Compiler.Effects.Manifests;

namespace Calor.Compiler.Commands;

/// <summary>
/// <c>calor import &lt;package&gt;</c> — the adoption-surface half of package
/// ingestion (wedge plan v0.11 D-W3.1 / D-W3.2).
///
/// Effect half: generates a <c>.calor-effects.json</c> manifest for a
/// package's public surface via the three-tier strategy — Tier A IL
/// derivation for concrete call chains (provenance <c>inferred</c>), Tier B
/// curated manifests reported as already-covered, Tier C unresolved members
/// surfaced loudly (never silently under-approximated).
///
/// Contract half (narrow mechanical set): §Q non-null facts from NRT
/// metadata and non-negativity facts from unsigned parameter types, written
/// to a <c>.calor-contracts.json</c> sidecar with <c>assumed</c> provenance.
/// The sidecar is annotation-only: no compiler surface consumes it as
/// trusted input to verification or elision (the consumption path is the
/// recorded D-W3.2 shed point).
/// </summary>
public static class ImportCommand
{
    public static Command Create()
    {
        var packageArgument = new Argument<string>(
            name: "package",
            description: "NuGet package id (resolved from the global packages folder) or a path to a .dll");

        var versionOption = new Option<string?>(
            aliases: ["--version"],
            description: "Package version to import (default: highest version in the packages folder)");

        var referencesOption = new Option<string[]>(
            aliases: ["--references", "-r"],
            description: "Additional assembly files or directories loaded for call-chain resolution")
        { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true };

        var projectOption = new Option<DirectoryInfo?>(
            aliases: ["--project", "-p"],
            description: "Project directory for loading project-local manifests");

        var solutionOption = new Option<DirectoryInfo?>(
            aliases: ["--solution", "-s"],
            description: "Solution directory for loading solution-level manifests");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Manifest output path (default: <package>.calor-effects.json)");

        var noContractsOption = new Option<bool>(
            aliases: ["--no-contracts"],
            description: "Skip the mechanical contract synthesis (assumed-provenance sidecar)");

        var jsonOption = new Option<bool>(
            aliases: ["--json"],
            description: "Emit the envelope document to stdout instead of writing files");

        var command = new Command("import",
            "Generate effect manifests (and assumed-provenance contract facts) for a package's public surface")
        {
            packageArgument,
            versionOption,
            referencesOption,
            projectOption,
            solutionOption,
            outputOption,
            noContractsOption,
            jsonOption
        };

        // Exit code returned through ctx.ExitCode: a code parked only on
        // Environment.ExitCode is overwritten by Main's InvokeAsync return.
        command.SetHandler((InvocationContext ctx) =>
        {
            ctx.ExitCode = Execute(
                ctx.ParseResult.GetValueForArgument(packageArgument),
                ctx.ParseResult.GetValueForOption(versionOption),
                ctx.ParseResult.GetValueForOption(referencesOption) ?? [],
                ctx.ParseResult.GetValueForOption(projectOption),
                ctx.ParseResult.GetValueForOption(solutionOption),
                ctx.ParseResult.GetValueForOption(outputOption),
                ctx.ParseResult.GetValueForOption(noContractsOption),
                ctx.ParseResult.GetValueForOption(jsonOption));
        });

        return command;
    }

    private static int Execute(
        string package,
        string? version,
        string[] references,
        DirectoryInfo? project,
        DirectoryInfo? solution,
        FileInfo? output,
        bool noContracts,
        bool json)
    {
        try
        {
            // Resolve the import target: a dll path or a NuGet package id.
            var target = ResolveTarget(package, version);
            if (target is null)
            {
                var message = File.Exists(package) || package.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? $"Assembly not found: {package}"
                    : $"Package '{package}' not found in the NuGet global packages folder"
                      + (version is null ? "" : $" at version {version}")
                      + ". Pass a .dll path, or restore the package first.";
                if (json)
                {
                    Console.WriteLine(EnvelopeWriter.Serialize("import", null,
                        [new Diagnostic(DiagnosticCode.ImportInputNotFound, DiagnosticSeverity.Error,
                            message, filePath: null, line: 1, column: 1)]));
                }
                Console.Error.WriteLine($"Error: {message}");
                return 1;
            }

            var referencePaths = ExpandReferencePaths(references);
            // Explicit --references first (they win the AssemblyIndex
            // first-loaded-wins collision rule), then the package's own
            // dependency closure.
            referencePaths.AddRange(target.DependencyAssemblyPaths);

            var loader = new ManifestLoader();
            var resolver = new EffectResolver(loader);
            resolver.Initialize(project?.FullName, solution?.FullName);

            var ilOptions = new ILAnalysisOptions
            {
                RuntimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location),
                NuGetPackageRoot = GetNuGetPackagesRoot(),
                DepsFilePath = target.DepsFilePath
            };

            var report = PackageManifestGenerator.Generate(
                target.AssemblyPaths,
                referencePaths,
                resolver,
                library: target.Library,
                libraryVersion: target.Version,
                synthesizeContracts: !noContracts,
                ilOptions: ilOptions);

            // M1: an input that is not a usable assembly must fail loudly —
            // no exit 0, no empty manifest on disk.
            if (report.PublicMethodCount == 0 && report.LoadErrors.Count > 0)
            {
                var message = "Import failed: no usable assembly metadata in "
                    + string.Join(", ", target.AssemblyPaths.Select(Path.GetFileName))
                    + " — " + string.Join("; ", report.LoadErrors);
                if (json)
                {
                    Console.WriteLine(EnvelopeWriter.Serialize("import", null,
                        [new Diagnostic(DiagnosticCode.ImportCommandError, DiagnosticSeverity.Error,
                            message, filePath: null, line: 1, column: 1)]));
                }
                Console.Error.WriteLine($"Error: {message}");
                return 1;
            }

            var diagnostics = new List<Diagnostic>();
            if (report.Unresolved.Count > 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ImportUnresolvedEffects, DiagnosticSeverity.Warning,
                    $"{report.Unresolved.Count} public member(s) could not be resolved (Tier C): "
                    + "their effects are UNKNOWN and are NOT emitted as pure. "
                    + "Add curated or supplemental manifest entries for the members listed in the report.",
                    filePath: null, line: 1, column: 1));
            }
            if (report.SynthesizedContracts.Count > 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ImportSynthesizedContractsAssumed, DiagnosticSeverity.Info,
                    $"{report.SynthesizedContracts.Count} contract fact(s) synthesized with 'assumed' provenance. "
                    + "They are annotation-only: verification never consumes them as trusted and they never elide runtime checks.",
                    filePath: null, line: 1, column: 1));
            }

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            string? manifestPath = null;
            string? contractsPath = null;
            if (!json)
            {
                var stem = target.Library ?? Path.GetFileNameWithoutExtension(target.AssemblyPaths[0]);
                manifestPath = output?.FullName ?? Path.Combine(
                    project?.FullName ?? Directory.GetCurrentDirectory(),
                    $"{stem}.calor-effects.json");
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(report.Manifest, jsonOptions));

                if (!noContracts && report.SynthesizedContracts.Count > 0)
                {
                    contractsPath = manifestPath.EndsWith(".calor-effects.json", StringComparison.Ordinal)
                        ? manifestPath[..^".calor-effects.json".Length] + ".calor-contracts.json"
                        : manifestPath + ".calor-contracts.json";
                    File.WriteAllText(contractsPath, JsonSerializer.Serialize(new ContractSidecar(
                        Library: target.Library,
                        LibraryVersion: target.Version,
                        GeneratedBy: "calor import",
                        GeneratedAt: DateTime.UtcNow.ToString("O"),
                        Provenance: PackageManifestGenerator.AssumedProvenance,
                        Note: "Annotation-only synthesized contract facts. Never consumed by verification as trusted; never eligible for Proven or runtime-check elision.",
                        Contracts: report.SynthesizedContracts), jsonOptions));
                }
            }

            var data = BuildEnvelopeData(report, manifestPath, contractsPath);

            if (json)
            {
                Console.WriteLine(EnvelopeWriter.Serialize("import", data, diagnostics));
                return 0;
            }

            PrintTextReport(report, manifestPath, contractsPath);
            return 0;
        }
        catch (Exception ex)
        {
            if (json)
            {
                Console.WriteLine(EnvelopeWriter.Serialize("import", null,
                    [new Diagnostic(DiagnosticCode.ImportCommandError, DiagnosticSeverity.Error,
                        $"Import failed: {ex.Message}", filePath: null, line: 1, column: 1)]));
            }
            Console.Error.WriteLine($"Error: Import failed: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // Target resolution
    // ------------------------------------------------------------------

    private sealed record ImportTarget(
        IReadOnlyList<string> AssemblyPaths,
        string? Library,
        string? Version,
        IReadOnlyList<string> DependencyAssemblyPaths,
        string? DepsFilePath);

    private static ImportTarget? ResolveTarget(string package, string? version)
    {
        // Direct assembly path
        if (package.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(package))
                return null;
            var fullPath = Path.GetFullPath(package);
            // A sibling deps.json lets the analyzer resolve reference
            // assemblies to implementation assemblies (C1: tier A needs the
            // dependency closure before falling back to unresolved).
            var depsPath = Path.ChangeExtension(fullPath, ".deps.json");
            return new ImportTarget(
                [fullPath],
                Path.GetFileNameWithoutExtension(fullPath),
                Version: null,
                DependencyAssemblyPaths: [],
                DepsFilePath: File.Exists(depsPath) ? depsPath : null);
        }

        // NuGet package id in the global packages folder
        var root = GetNuGetPackagesRoot();
        if (root is null)
            return null;

        var packageDir = Path.Combine(root, package.ToLowerInvariant());
        if (!Directory.Exists(packageDir))
            return null;

        string? versionDir = null;
        if (version is not null)
        {
            var candidate = Path.Combine(packageDir, version);
            if (Directory.Exists(candidate))
                versionDir = candidate;
        }
        else
        {
            versionDir = Directory.GetDirectories(packageDir)
                .OrderByDescending(d => ParseVersionKey(Path.GetFileName(d)))
                .ThenByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        if (versionDir is null)
            return null;

        var libDir = Path.Combine(versionDir, "lib");
        if (!Directory.Exists(libDir))
            return null;

        var tfmDir = Directory.GetDirectories(libDir)
            .OrderBy(d => TfmRank(Path.GetFileName(d)))
            .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (tfmDir is null)
            return null;

        var dlls = Directory.GetFiles(tfmDir, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (dlls.Count == 0)
            return null;

        // C1: wire the package's own dependency closure (from the .nuspec,
        // resolved against the global packages folder) so Tier-A analysis has
        // the assemblies a chain needs before falling back to unresolved.
        var dependencyDlls = ResolvePackageDependencyClosure(root, package);

        return new ImportTarget(dlls, package, Path.GetFileName(versionDir), dependencyDlls, DepsFilePath: null);
    }

    /// <summary>
    /// Walks the .nuspec dependency graph of <paramref name="packageId"/>
    /// through the global packages folder, collecting each installed
    /// dependency's best-TFM lib assemblies. Dependencies that are not
    /// restored locally are simply absent — their chains then surface as
    /// unresolved (Tier C), never as silently pure.
    /// </summary>
    private static List<string> ResolvePackageDependencyClosure(string packagesRoot, string packageId)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { packageId };
        var queue = new Queue<string>(ReadNuspecDependencyIds(packagesRoot, packageId));

        while (queue.Count > 0)
        {
            var depId = queue.Dequeue();
            if (!visited.Add(depId))
                continue;

            var depDir = Path.Combine(packagesRoot, depId.ToLowerInvariant());
            if (!Directory.Exists(depDir))
                continue; // not restored — chains through it stay unresolved, honestly

            var versionDir = Directory.GetDirectories(depDir)
                .OrderByDescending(d => ParseVersionKey(Path.GetFileName(d)))
                .ThenByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (versionDir is null)
                continue;

            var libDir = Path.Combine(versionDir, "lib");
            if (Directory.Exists(libDir))
            {
                var tfmDir = Directory.GetDirectories(libDir)
                    .OrderBy(d => TfmRank(Path.GetFileName(d)))
                    .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (tfmDir is not null)
                {
                    result.AddRange(Directory.GetFiles(tfmDir, "*.dll", SearchOption.TopDirectoryOnly)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                }
            }

            foreach (var transitive in ReadNuspecDependencyIds(packagesRoot, depId))
                queue.Enqueue(transitive);
        }

        return result;
    }

    private static IEnumerable<string> ReadNuspecDependencyIds(string packagesRoot, string packageId)
    {
        var packageDir = Path.Combine(packagesRoot, packageId.ToLowerInvariant());
        if (!Directory.Exists(packageDir))
            return [];

        var versionDir = Directory.GetDirectories(packageDir)
            .OrderByDescending(d => ParseVersionKey(Path.GetFileName(d)))
            .ThenByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (versionDir is null)
            return [];

        var nuspec = Path.Combine(versionDir, $"{packageId.ToLowerInvariant()}.nuspec");
        if (!File.Exists(nuspec))
            return [];

        try
        {
            var doc = System.Xml.Linq.XDocument.Load(nuspec);
            return doc.Descendants()
                .Where(e => e.Name.LocalName == "dependency")
                .Select(e => e.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return [];
        }
    }

    private static string? GetNuGetPackagesRoot()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return null;
        var fallback = Path.Combine(home, ".nuget", "packages");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static (int Major, int Minor, int Patch) ParseVersionKey(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return (0, 0, 0);
        var core = name.Split('-', '+')[0];
        var parts = core.Split('.');
        return (
            parts.Length > 0 && int.TryParse(parts[0], out var maj) ? maj : 0,
            parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0,
            parts.Length > 2 && int.TryParse(parts[2], out var pat) ? pat : 0);
    }

    /// <summary>Lower rank = preferred target framework.</summary>
    private static int TfmRank(string tfm) => tfm.ToLowerInvariant() switch
    {
        "net10.0" => 0,
        "net9.0" => 1,
        "net8.0" => 2,
        "net7.0" => 3,
        "net6.0" => 4,
        "net5.0" => 5,
        "netstandard2.1" => 6,
        "netstandard2.0" => 7,
        _ => 100
    };

    private static List<string> ExpandReferencePaths(string[] references)
    {
        var paths = new List<string>();
        foreach (var reference in references)
        {
            if (Directory.Exists(reference))
            {
                paths.AddRange(Directory.GetFiles(reference, "*.dll", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(reference))
            {
                paths.Add(Path.GetFullPath(reference));
            }
        }
        return paths;
    }

    // ------------------------------------------------------------------
    // Output
    // ------------------------------------------------------------------

    private sealed record ContractSidecar(
        string? Library,
        string? LibraryVersion,
        string GeneratedBy,
        string GeneratedAt,
        string Provenance,
        string Note,
        IReadOnlyList<SynthesizedContract> Contracts);

    private static object BuildEnvelopeData(PackageImportReport report, string? manifestPath, string? contractsPath)
    {
        return new
        {
            library = report.Library,
            libraryVersion = report.LibraryVersion,
            targetAssemblies = report.TargetAssemblies,
            counts = new
            {
                publicMethods = report.PublicMethodCount,
                derived = report.Derived.Count,
                curated = report.Curated.Count,
                unresolved = report.Unresolved.Count,
                synthesizedContracts = report.SynthesizedContracts.Count
            },
            provenance = new
            {
                derived = PackageManifestGenerator.DerivedConfidence,
                synthesizedContracts = PackageManifestGenerator.AssumedProvenance,
                note = "Nothing emitted by calor import carries 'verified' confidence."
            },
            manifestPath,
            contractsPath,
            manifest = report.Manifest,
            derived = report.Derived,
            curated = report.Curated,
            unresolved = report.Unresolved,
            synthesizedContracts = report.SynthesizedContracts,
            loadErrors = report.LoadErrors.Count > 0 ? report.LoadErrors : null
        };
    }

    private static void PrintTextReport(PackageImportReport report, string? manifestPath, string? contractsPath)
    {
        var title = report.Library is null
            ? string.Join(", ", report.TargetAssemblies)
            : $"{report.Library} {report.LibraryVersion}".TrimEnd();
        Console.WriteLine($"Imported {title}");
        Console.WriteLine($"  Public members analyzed: {report.PublicMethodCount}");
        Console.WriteLine($"  Derived via IL analysis (Tier A, provenance: {PackageManifestGenerator.DerivedConfidence}): {report.Derived.Count}");
        Console.WriteLine($"  Covered by curated manifests (Tier B): {report.Curated.Count}");
        Console.WriteLine($"  Unresolved (Tier C — effects UNKNOWN, not emitted): {report.Unresolved.Count}");
        if (report.SynthesizedContracts.Count > 0)
        {
            Console.WriteLine($"  Synthesized contract facts (provenance: {PackageManifestGenerator.AssumedProvenance}, annotation-only): {report.SynthesizedContracts.Count}");
        }

        if (report.Unresolved.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Unresolved members (add curated or supplemental manifest entries):");
            foreach (var group in report.Unresolved.GroupBy(u => u.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {group.Key}");
                foreach (var entry in group.Take(20))
                    Console.WriteLine($"    {entry.Member}  [{entry.Kind}] — {entry.Reason}");
                if (group.Count() > 20)
                    Console.WriteLine($"    … and {group.Count() - 20} more");
            }
        }

        if (report.LoadErrors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Load errors:");
            foreach (var error in report.LoadErrors)
                Console.WriteLine($"  [ERROR] {error}");
        }

        Console.WriteLine();
        if (manifestPath is not null)
            Console.WriteLine($"Manifest written to {manifestPath}");
        if (contractsPath is not null)
        {
            Console.WriteLine($"Contract facts written to {contractsPath} (assumed provenance — annotation-only; never consumed by verification as trusted)");
        }
    }
}
