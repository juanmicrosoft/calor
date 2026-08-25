using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Calor.Compiler;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;

namespace Calor.Tasks;

internal enum CompileCalorTestPhase
{
    BeforeILAnalysisInitialization,
    AfterSourceRead,
    BeforeCacheEntrySaved,
    BeforeCrossModuleEnforcement
}

internal sealed record CompileCalorCacheInputs(
    string SerializerVersion,
    string CacheSchemaVersion,
    string CompilerSemanticsVersion,
    bool Verbose,
    bool EnforceEffects,
    bool EnableTypeChecking,
    bool UnsafeTranspileOnly,
    bool AllowUnsafeBlocks,
    string ProjectOutputType,
    string DefineConstants,
    string LanguageVersion,
    string ImplicitUsings,
    string Nullable,
    bool TreatWarningsAsErrors,
    bool VerifyContracts,
    bool ElideProvenGuards,
    bool EnableILAnalysis,
    string ExperimentalFlags,
    string ProjectDirectory,
    IReadOnlyList<string> ReferencedAssemblies,
    IReadOnlyList<string> ResolvedImplementationAssemblies,
    string RuntimeDirectory,
    string NuGetPackageRoot,
    string DepsFile)
{
    internal string Serialize()
    {
        var builder = new StringBuilder();
        Append(builder, "serializerVersion", SerializerVersion);
        Append(builder, "cacheSchemaVersion", CacheSchemaVersion);
        Append(builder, "compilerSemanticsVersion", CompilerSemanticsVersion);
        Append(builder, "verbose", Verbose ? "true" : "false");
        Append(builder, "enforceEffects", EnforceEffects ? "true" : "false");
        Append(builder, "enableTypeChecking", EnableTypeChecking ? "true" : "false");
        Append(builder, "unsafeTranspileOnly", UnsafeTranspileOnly ? "true" : "false");
        Append(builder, "allowUnsafeBlocks", AllowUnsafeBlocks ? "true" : "false");
        Append(builder, "projectOutputType", ProjectOutputType);
        Append(builder, "defineConstants", DefineConstants);
        Append(builder, "languageVersion", LanguageVersion);
        Append(builder, "implicitUsings", ImplicitUsings);
        Append(builder, "nullable", Nullable);
        Append(builder, "treatWarningsAsErrors", TreatWarningsAsErrors ? "true" : "false");
        Append(builder, "verifyContracts", VerifyContracts ? "true" : "false");
        Append(builder, "elideProvenGuards", ElideProvenGuards ? "true" : "false");
        Append(builder, "enableILAnalysis", EnableILAnalysis ? "true" : "false");
        Append(builder, "experimentalFlags", ExperimentalFlags);
        Append(builder, "projectDirectory", ProjectDirectory);
        foreach (var reference in ReferencedAssemblies)
            Append(builder, "referencedAssembly", reference);
        foreach (var implementation in ResolvedImplementationAssemblies)
            Append(builder, "resolvedImplementationAssembly", implementation);
        Append(builder, "runtimeDirectory", RuntimeDirectory);
        Append(builder, "nuGetPackageRoot", NuGetPackageRoot);
        Append(builder, "depsFile", DepsFile);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        builder.Append(name.Length).Append(':').Append(name)
            .Append('=').Append(value.Length).Append(':').Append(value).Append(';');
    }
}

/// <summary>
/// MSBuild task that compiles Calor source files to C#.
/// Owns all incremental logic — MSBuild-level Inputs/Outputs should not be used.
/// </summary>
public sealed class CompileCalor : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// The Calor source files to compile.
    /// </summary>
    [Required]
    public ITaskItem[] SourceFiles { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// The output directory for generated C# files.
    /// </summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The project directory, used for computing relative paths and finding manifests.
    /// </summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The generated C# files.
    /// </summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Enable verbose logging.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Enable cross-assembly IL analysis for effect resolution.
    /// </summary>
    public bool EnableILAnalysis { get; set; }

    /// <summary>
    /// Referenced assemblies for cross-assembly IL effect analysis.
    /// </summary>
    public ITaskItem[] ReferencedAssemblies { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Path to the .NET shared runtime directory for resolving BCL implementation assemblies.
    /// </summary>
    public string RuntimeDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Path to the NuGet global packages folder.
    /// </summary>
    public string NuGetPackageRoot { get; set; } = string.Empty;

    /// <summary>
    /// Path to the project's .deps.json file.
    /// </summary>
    public string DepsFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Enforce effect declarations (§E coverage) during compilation, including
    /// cross-module enforcement. Default: true, matching
    /// <see cref="Calor.Compiler.CompilationOptions.EnforceEffects"/>.
    /// Set the MSBuild property <c>CalorEnforceEffects</c> to override.
    /// </summary>
    public bool EnforceEffects { get; set; } = true;

    /// <summary>
    /// Whether to run the type checker. Mirrors
    /// <see cref="Calor.Compiler.CompilationOptions.EnableTypeChecking"/>, default-on since
    /// v0.12. Set the MSBuild property <c>CalorTypeCheck</c> to <c>false</c> to opt out —
    /// without it a consumer of the published SDK has no way off a default that can reject a
    /// program their previous build accepted.
    /// </summary>
    public bool TypeCheck { get; set; } = true;

    /// <summary>
    /// Explicitly emit generated C# without type checking or Roslyn validation.
    /// Unsafe outputs are never recorded in the incremental cache.
    /// </summary>
    public bool TranspileOnly { get; set; }

    /// <summary>Existing C# files compiled alongside the generated output.</summary>
    public ITaskItem[] ProjectSourceFiles { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>Whether the consuming C# project permits unsafe code.</summary>
    public bool AllowUnsafeBlocks { get; set; }

    /// <summary>The consuming project's MSBuild OutputType.</summary>
    public string ProjectOutputType { get; set; } = "Library";

    /// <summary>The consuming project's preprocessor symbols.</summary>
    public string DefineConstants { get; set; } = string.Empty;

    /// <summary>The consuming project's C# language version.</summary>
    public string LanguageVersion { get; set; } = string.Empty;

    /// <summary>The consuming project's MSBuild ImplicitUsings setting.</summary>
    public string ImplicitUsings { get; set; } = string.Empty;

    /// <summary>The consuming project's nullable context setting.</summary>
    public string Nullable { get; set; } = string.Empty;

    /// <summary>Whether the consuming project promotes compiler warnings to errors.</summary>
    public bool TreatWarningsAsErrors { get; set; }

    /// <summary>The generated analyzer config passed to source generators.</summary>
    public string AnalyzerConfigPath { get; set; } = string.Empty;

    /// <summary>Roslyn analyzers and source generators used by the consuming project.</summary>
    public ITaskItem[] AnalyzerAssemblies { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>Additional files supplied to source generators.</summary>
    public ITaskItem[] AdditionalFiles { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Run static contract verification during compilation (Annex A-1.3
    /// instrumentation item 1): refutations surface as Calor0712-band build
    /// diagnostics (Warning severity — the build still succeeds). Off by
    /// default; the Guarantees probe epoch's v0.10 arm turns it on via the
    /// workspace template.
    /// </summary>
    public bool Verify { get; set; }

    /// <summary>
    /// Delete runtime contract guards on clean Proven verdicts (MSBuild property
    /// <c>CalorElideProvenGuards</c>). Default true since v0.15, matching
    /// <c>CompilationOptions.ElideProvenGuards</c>; set
    /// <c>&lt;CalorElideProvenGuards&gt;false&lt;/CalorElideProvenGuards&gt;</c> to keep
    /// every guard (verification stays diagnostic). Only matters with <c>CalorVerify</c>.
    /// </summary>
    public bool ElideProvenGuards { get; set; } = true;

    /// <summary>
    /// Semicolon- or comma-separated list of experimental feature flag names to enable.
    /// Plumbed through to <see cref="Calor.Compiler.CompilationOptions.ExperimentalFlags"/>.
    /// Unknown flags are accepted silently — see <see cref="Calor.Compiler.ExperimentalFlags"/>.
    /// </summary>
    public string ExperimentalFlags { get; set; } = string.Empty;

    internal Action<string, CompileCalorTestPhase>? CacheTestHook { get; set; }

    internal CompileCalorCacheInputs ComputeCacheInputs()
        => ComputeCacheInputs(ResolveReferencedAssemblies());

    private sealed record ResolvedReference(
        string Path, string Descriptor, bool Exists, bool IsUsable);

    private CompileCalorCacheInputs ComputeCacheInputs(
        IReadOnlyList<ResolvedReference> referencedAssemblies)
    {
        var canonicalExperimentalFlags = string.Join(",",
            Calor.Compiler.ExperimentalFlags.Parse(ExperimentalFlags).EnabledFlags
                .Select(flag => flag.ToLowerInvariant())
                .OrderBy(flag => flag, StringComparer.Ordinal));

        var resolvedImplementations = EnableILAnalysis
            ? Compiler.Effects.IL.AssemblyIndex.ResolveImplementationAssemblyPaths(
                referencedAssemblies.Select(reference => reference.Path).ToList(),
                CreateILAnalysisOptions())
            : [];

        return new CompileCalorCacheInputs(
            BuildStateCache.CurrentOptionsSerializerVersion,
            BuildStateCache.CurrentFormatVersion,
            BuildStateCache.CurrentCompilerSemanticsVersion,
            Verbose,
            EnforceEffects,
            !TranspileOnly && TypeCheck && CompilationOptions.TypeCheckingDefault,
            TranspileOnly,
            AllowUnsafeBlocks,
            ProjectOutputType,
            DefineConstants,
            LanguageVersion,
            ImplicitUsings,
            Nullable,
            TreatWarningsAsErrors,
            Verify,
            ElideProvenGuards,
            EnableILAnalysis,
            canonicalExperimentalFlags,
            DescribePath(ProjectDirectory, includeContent: false),
            referencedAssemblies.Select(reference => reference.Descriptor).ToList(),
            resolvedImplementations.Select((path, index) =>
                path == null
                    ? $"unresolved:{referencedAssemblies[index].Descriptor}"
                    : DescribeAssembly(path, File.Exists(path))).ToList(),
            EnableILAnalysis
                ? DescribePath(RuntimeDirectory, includeContent: false)
                : "unused",
            EnableILAnalysis
                ? DescribePath(NuGetPackageRoot, includeContent: false)
                : "unused",
            EnableILAnalysis
                ? DescribePath(DepsFilePath, includeContent: true)
                : "unused");
    }

    private Compiler.Effects.IL.ILAnalysisOptions CreateILAnalysisOptions()
        => new()
        {
            RuntimeDirectory = !string.IsNullOrEmpty(RuntimeDirectory) ? RuntimeDirectory : null,
            NuGetPackageRoot = !string.IsNullOrEmpty(NuGetPackageRoot) ? NuGetPackageRoot : null,
            DepsFilePath = !string.IsNullOrEmpty(DepsFilePath) ? DepsFilePath : null
        };

    private IReadOnlyList<ResolvedReference> ResolveReferencedAssemblies()
    {
        return ReferencedAssemblies
            .Select(item =>
            {
                var path = item.GetMetadata("FullPath");
                if (string.IsNullOrEmpty(path))
                    path = item.ItemSpec;
                var fullPath = NormalizeFullPath(path);
                var exists = !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath);
                var descriptor = DescribeAssembly(fullPath, exists);
                return new ResolvedReference(
                    fullPath,
                    descriptor,
                    exists,
                    exists
                    && !descriptor.Contains("non-managed:", StringComparison.Ordinal)
                    && !descriptor.Contains("unreadable", StringComparison.Ordinal));
            })
            .OrderBy(reference => reference.Descriptor, StringComparer.Ordinal)
            .ThenBy(reference => PrivacySafePathFingerprint(reference.Path), StringComparer.Ordinal)
            .ToList();
    }

    private static string DescribeAssembly(string path, bool exists)
    {
        if (!exists)
            return $"missing:{PrivacySafePathFingerprint(path)}";

        string identity;
        try
        {
            identity = AssemblyName.GetAssemblyName(path).FullName
                ?? Path.GetFileName(path);
        }
        catch (BadImageFormatException)
        {
            identity = $"non-managed:{CanonicalFileName(path)}";
        }
        catch (FileLoadException)
        {
            identity = $"unreadable:{CanonicalFileName(path)}";
        }
        catch (IOException)
        {
            identity = $"unreadable:{CanonicalFileName(path)}";
        }
        catch (UnauthorizedAccessException)
        {
            identity = $"unreadable:{CanonicalFileName(path)}";
        }

        string contentHash;
        try
        {
            contentHash = BuildStateCache.ComputeFileHash(path);
        }
        catch (IOException)
        {
            contentHash = "unreadable";
        }
        catch (UnauthorizedAccessException)
        {
            contentHash = "unreadable";
        }
        return $"identity:{identity}|content:{contentHash}";
    }

    private static string DescribePath(string path, bool includeContent)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "unset";

        var fullPath = NormalizeFullPath(path);
        var kind = File.Exists(fullPath) ? "file"
            : Directory.Exists(fullPath) ? "directory"
            : "missing";
        var descriptor = $"{kind}:{PrivacySafePathFingerprint(fullPath)}";
        if (!includeContent || kind != "file")
            return descriptor;

        try
        {
            return $"{descriptor}|content:{BuildStateCache.ComputeFileHash(fullPath)}";
        }
        catch (IOException)
        {
            return $"{descriptor}|content:unreadable";
        }
        catch (UnauthorizedAccessException)
        {
            return $"{descriptor}|content:unreadable";
        }
    }

    private static string NormalizeFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        try
        {
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                   or PathTooLongException)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }

    private static string PrivacySafePathFingerprint(string path)
        => string.IsNullOrEmpty(path) ? "unset" : BuildStateCache.ComputePathHash(path);

    private static string CanonicalFileName(string path)
    {
        var name = Path.GetFileName(path);
        return OperatingSystem.IsWindows() ? name.ToUpperInvariant() : name;
    }

    public override bool Execute()
    {
        if (SourceFiles.Length == 0)
        {
            Log.LogMessage(MessageImportance.Normal, "No Calor source files to compile.");
            return true;
        }

        // Ensure output directory exists
        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        // 1. Load cache
        var cacheLoad = BuildStateCache.LoadWithStatus(OutputDirectory);
        var priorCache = cacheLoad.State;

        // 2. Compute global hashes
        var tasksAssemblyPath = typeof(CompileCalor).Assembly.Location;
        var compilerHash = BuildStateCache.ComputeCompilerHash(
            ResolveCompilerClosurePaths(tasksAssemblyPath));
        var referencedAssemblyInputs = ResolveReferencedAssemblies();
        var optionsHash = BuildStateCache.ComputeOptionsHash(
            ComputeCacheInputs(referencedAssemblyInputs).Serialize());
        var manifestHash = BuildStateCache.ComputeManifestHash(ProjectDirectory);

        // 3. Global invalidation check
        // Pre-compute full project dir once — avoids per-file Path.GetFullPath
        var fullProjectDir = Path.GetFullPath(ProjectDirectory);
        // Store output directory relative to project for cache portability across machines
        var relativeOutputDir = BuildStateCache.NormalizeRelativePath(
            Path.GetRelativePath(fullProjectDir, Path.GetFullPath(OutputDirectory)));
        var globalReasons = BuildStateCache.GetGlobalInvalidationReasons(
            priorCache, compilerHash, optionsHash, manifestHash, relativeOutputDir).ToList();
        if (cacheLoad.Status == CacheLoadStatus.CorruptOrPartial)
        {
            globalReasons.Clear();
            globalReasons.Add(GlobalCacheInvalidationReason.CorruptOrPartialCache);
            BuildStateCache.Delete(OutputDirectory);
        }
        else if (cacheLoad.Status == CacheLoadStatus.Unreadable)
        {
            globalReasons.Clear();
            globalReasons.Add(GlobalCacheInvalidationReason.UnreadableCache);
        }
        var newState = new BuildState
        {
            CompilerHash = compilerHash,
            OptionsHash = optionsHash,
            ManifestHash = manifestHash,
            OutputDirectory = relativeOutputDir
        };

        // Construct shared CompilationContext for IL analysis (once per build, reused across files)
        CompilationContext? compilationContext = null;
        Compiler.Effects.IL.ILEffectAnalyzer? ilAnalyzer = null;
        if (EnableILAnalysis && ReferencedAssemblies.Length > 0)
        {
            try
            {
                CacheTestHook?.Invoke(
                    ProjectDirectory, CompileCalorTestPhase.BeforeILAnalysisInitialization);
                var unusableReferenceCount = referencedAssemblyInputs.Count(
                    input => !input.IsUsable);
                if (unusableReferenceCount != 0)
                {
                    throw new InvalidOperationException(
                        $"{unusableReferenceCount} referenced assembly input(s) were missing, "
                        + "unreadable, or not managed assemblies");
                }
                var assemblyPaths = referencedAssemblyInputs
                    .Where(input => input.Exists)
                    .Select(input => input.Path)
                    .ToList();

                if (assemblyPaths.Count > 0)
                {
                    var ilOptions = CreateILAnalysisOptions();
                    var resolvedImplementationPaths =
                        Compiler.Effects.IL.AssemblyIndex.ResolveImplementationAssemblyPaths(
                            assemblyPaths,
                            ilOptions);
                    var unresolvedReferenceCount = resolvedImplementationPaths.Count(
                        path => path == null);
                    if (unresolvedReferenceCount != 0)
                    {
                        throw new InvalidOperationException(
                            $"{unresolvedReferenceCount} managed reference assembly input(s) "
                            + "could not be resolved to implementation assemblies");
                    }

                    var resolver = new Compiler.Effects.EffectResolver();
                    resolver.Initialize(ProjectDirectory);

                    ilAnalyzer = new Compiler.Effects.IL.ILEffectAnalyzer(
                        assemblyPaths, resolver, ilOptions);
                    if (ilAnalyzer.LoadedAssemblyCount != assemblyPaths.Count)
                    {
                        throw new InvalidOperationException(
                            $"IL analysis loaded {ilAnalyzer.LoadedAssemblyCount} of "
                            + $"{assemblyPaths.Count} referenced assembly input(s)");
                    }

                    var sharedResolver = new Compiler.Effects.EffectResolver(ilAnalyzer: ilAnalyzer);
                    sharedResolver.Initialize(ProjectDirectory);

                    compilationContext = new CompilationContext { SharedEffectResolver = sharedResolver };

                    if (Verbose)
                    {
                        Log.LogMessage(MessageImportance.High,
                            "Calor: IL analysis enabled with {0} referenced assemblies ({1} loaded).",
                            assemblyPaths.Count, ilAnalyzer.LoadedAssemblyCount);
                    }

                }
            }
            catch (Exception ex)
            {
                // Fail closed (#788): EnableILAnalysis=true is a request for a
                // safety analysis — a swallowed initialization failure would
                // silently skip it and let effect violations through. Fail the
                // build instead of downgrading to a warning.
                compilationContext?.Dispose();
                ilAnalyzer?.Dispose();
                Log.LogError(
                    "Calor: EnableILAnalysis=true but IL analysis failed to initialize ({0}: {1}). "
                    + "The build fails rather than silently skipping the requested analysis; "
                    + "set CalorEnableILAnalysis=false to build without it.",
                    ex.GetType().Name, ex.Message);
                return false;
            }
        }

        try
        {

        // An armed verify gate whose solver cannot load must be loud (#826
        // review C3): without this, a missing native libz3 silently turns
        // Verify=true into a no-op — every contract reports Skipped at info
        // severity, invisible at normal MSBuild verbosity.
        if (Verify && !Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable)
        {
            Log.LogWarning(
                subcategory: "Calor",
                warningCode: "Calor0710",
                helpKeyword: null,
                file: null,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: "Verify=true but the Z3 SMT solver is not available in the MSBuild task context — "
                    + "contract verification will be silently skipped. Ensure the Z3 native library sits "
                    + "next to Calor.Tasks.dll.");
        }

        var generatedFiles = new List<ITaskItem>();
        var pendingOutputs = new List<(
            string InputPath,
            string OutputPath,
            string RelativePath,
            byte[] GeneratedBytes,
            BuildFileEntry CacheEntry)>();
        var generatedValidationFailed = false;
        var success = true;
        var pathComparer = BuildStateCache.GetPathComparer();
        var currentRelativePaths = new HashSet<string>(pathComparer);
        // Collect per-module effect summaries (from fresh ASTs + cached entries) so the
        // cross-module pass can always see every module, even when incremental caching
        // skipped recompilation.
        var moduleSummaries = new List<(Calor.Compiler.Effects.EffectSummary Summary, string FilePath)>();
        // Track output paths to detect collisions from out-of-project file sanitization
        var outputPaths = new Dictionary<string, string>(pathComparer);

        // Cross-module call qualification map (G3/#809, #823 review M2): MSBuild is
        // the surface where csc actually consumes the outputs, so it needs the same
        // map the CLI driver builds. Warm-skip validity: a changed map invalidates
        // every skip (a cached .g.cs may carry stale qualification).
        IReadOnlyDictionary<string, CrossModuleFunctionTarget>? crossModuleMap = null;
        string? crossModuleMapHash = null;
        if (SourceFiles.Length > 1)
        {
            var sourceFileInfos = SourceFiles
                .Select(sf =>
                {
                    var path = sf.GetMetadata("FullPath");
                    return new FileInfo(string.IsNullOrEmpty(path) ? sf.ItemSpec : path);
                })
                .ToList();
            crossModuleMap = Calor.Compiler.CompilationDriver.BuildCrossModuleFunctionMap(sourceFileInfos);
            crossModuleMapHash = Calor.Compiler.CompilationDriver.ComputeCrossModuleMapHash(crossModuleMap);
        }
        newState.CrossModuleMapHash = crossModuleMapHash;
        if (priorCache != null && priorCache.CrossModuleMapHash != crossModuleMapHash)
        {
            globalReasons.Add(GlobalCacheInvalidationReason.CrossModuleMapChanged);
        }

        var globalInvalidation = globalReasons.Count != 0;
        if (Verbose)
        {
            foreach (var reason in globalReasons.Distinct())
            {
                Log.LogMessage(
                    MessageImportance.High,
                    "Calor: global invalidation [{0}] — recompiling all files.",
                    BuildStateCache.GetReasonCode(reason));
            }
        }

        Dictionary<string, BuildFileEntry>? priorFiles = null;
        if (!globalInvalidation && priorCache?.Files != null)
        {
            priorFiles = new Dictionary<string, BuildFileEntry>(priorCache.Files, pathComparer);
        }

        // 4. Process each source file
        foreach (var sourceFile in SourceFiles)
        {
            var inputPath = sourceFile.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(inputPath))
            {
                inputPath = sourceFile.ItemSpec;
            }

            // 4a. Compute relative path (with escape detection + sanitization)
            var (relativePath, isOutOfProject) = BuildStateCache.ComputeRelativePathFromFullProjectDir(
                inputPath, fullProjectDir);

            currentRelativePaths.Add(relativePath);

            // Compute output path preserving directory structure
            var outputRelative = Path.ChangeExtension(relativePath.Replace('/', Path.DirectorySeparatorChar), ".g.cs");
            var outputPath = Path.Combine(OutputDirectory, outputRelative);

            // Detect output path collisions (from out-of-project file sanitization)
            var normalizedOutput = BuildStateCache.NormalizeRelativePath(outputRelative);
            if (outputPaths.TryGetValue(normalizedOutput, out var existingInput))
            {
                Log.LogError(
                    "Calor output path collision: '{0}' and '{1}' both map to '{2}'",
                    existingInput, inputPath, outputPath);
                success = false;
                continue;
            }
            outputPaths[normalizedOutput] = inputPath;

            // 4b. Check cache: can we skip this file?
            if (!globalInvalidation && priorFiles != null)
            {
                priorFiles.TryGetValue(relativePath, out var cachedEntry);
                var missReason = BuildStateCache.GetFileCacheMissReason(
                    cachedEntry, inputPath, outputPath);
                if (missReason == null)
                {
                    var trustedEntry = cachedEntry!;
                    // Skip — carry entry forward
                    newState.Files[relativePath] = trustedEntry;
                    var outputItem = new TaskItem(outputPath);
                    outputItem.SetMetadata("SourceFile", inputPath);
                    generatedFiles.Add(outputItem);

                    // Carry forward cached effect summary so cross-module enforcement
                    // still sees this module on warm builds.
                    if (trustedEntry.EffectSummary != null)
                    {
                        moduleSummaries.Add((trustedEntry.EffectSummary, inputPath));
                    }
                    foreach (var diagnostic in trustedEntry.Diagnostics ?? [])
                    {
                        LogDiagnostic(
                            diagnostic.Code,
                            diagnostic.Severity,
                            diagnostic.Message,
                            inputPath,
                            diagnostic.Line,
                            diagnostic.Column);
                    }

                    if (Verbose)
                    {
                        Log.LogMessage(MessageImportance.Normal,
                            "Calor: skipping (up-to-date): {0}", inputPath);
                    }
                    continue;
                }

                if (Verbose)
                {
                    Log.LogMessage(
                        MessageImportance.High,
                        "Calor: file cache miss [{0}]: {1}",
                        BuildStateCache.GetReasonCode(missReason.Value),
                        inputPath);
                }
            }

            // Validate input file exists (only when we need to compile)
            if (!File.Exists(inputPath))
            {
                Log.LogError("Calor source file not found: {0}", inputPath);
                success = false;
                continue;
            }

            // Ensure output subdirectory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (outputDir != null && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 4c. Compile
            if (Verbose)
            {
                Log.LogMessage(MessageImportance.High,
                    "Compiling Calor: {0} -> {1}", inputPath, outputPath);
            }

            try
            {
                var statBeforeRead = BuildStateCache.StatFile(inputPath);
                var sourceBytes = File.ReadAllBytes(inputPath);
                CacheTestHook?.Invoke(inputPath, CompileCalorTestPhase.AfterSourceRead);
                var source = DecodeSource(sourceBytes);
                var compileOptions = new CompilationOptions
                {
                    Verbose = Verbose,
                    EnforceEffects = EnforceEffects,
                    EnableTypeChecking = !TranspileOnly && TypeCheck && CompilationOptions.TypeCheckingDefault,
                    UnsafeTranspileOnly = TranspileOnly,
                    AllowUnsafeCode = AllowUnsafeBlocks,
                    ReferencedAssemblyPaths = referencedAssemblyInputs
                        .Where(reference => reference.Exists)
                        .Select(reference => reference.Path)
                        .ToList(),
                    ProjectDirectory = ProjectDirectory,
                    Context = compilationContext,
                    EnableILAnalysis = EnableILAnalysis,
                    ExperimentalFlags = Calor.Compiler.ExperimentalFlags.Parse(ExperimentalFlags),
                    VerifyContracts = Verify,
                    ElideProvenGuards = ElideProvenGuards
                };
                compileOptions.DeferGeneratedOutputValidation = true;
                compileOptions.CrossModuleFunctionModules = crossModuleMap;
                var result = Program.Compile(source, inputPath, compileOptions);

                // Log ALL diagnostics, not only on failure: verification findings
                // (e.g. Calor0711/0712 refutation warnings) arrive on otherwise
                // successful compiles and must reach MSBuild output — dropping
                // them here would make the verify gate silent exactly when it
                // has something to say.
                foreach (var diagnostic in result.Diagnostics)
                {
                    LogDiagnostic(
                        diagnostic.Code,
                        diagnostic.IsError ? "error"
                            : diagnostic.IsWarning ? "warning" : "info",
                        diagnostic.Message,
                        diagnostic.FilePath ?? inputPath,
                        diagnostic.Span.Line,
                        diagnostic.Span.Column);
                }

                if (result.HasErrors)
                {
                    // Failure: delete prior .g.cs if exists, do NOT cache
                    if (File.Exists(outputPath))
                    {
                        try { File.Delete(outputPath); } catch { /* best-effort */ }
                    }

                    success = false;
                    continue;
                }

                var generatedBytes = new UTF8Encoding(false).GetBytes(result.GeneratedCode);
                EffectSummary? summary = null;
                if (result.Ast != null)
                {
                    summary = Calor.Compiler.Effects.EffectSummaryBuilder.Build(result.Ast);
                    moduleSummaries.Add((summary, inputPath));
                }

                var fileEntry = BuildStateCache.CreateFileEntry(statBeforeRead, sourceBytes);
                fileEntry.OutputContentHash = BuildStateCache.ComputeContentHash(generatedBytes);
                fileEntry.EffectSummary = summary;
                fileEntry.Diagnostics = result.Diagnostics.Select(diagnostic => new CachedDiagnostic
                {
                    Code = diagnostic.Code,
                    Severity = diagnostic.IsError ? "error"
                        : diagnostic.IsWarning ? "warning" : "info",
                    Message = diagnostic.Message,
                    Line = diagnostic.Span.Line,
                    Column = diagnostic.Span.Column
                }).ToList();
                pendingOutputs.Add((
                    inputPath, outputPath, relativePath, generatedBytes, fileEntry));
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to compile {0}: {1}", inputPath, ex.Message);

                // Failure: delete prior .g.cs if exists, do NOT cache
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { /* best-effort */ }
                }

                success = false;
            }
        }

        if (!TranspileOnly && (generatedFiles.Count > 0 || pendingOutputs.Count > 0))
        {
            var generatedSources = generatedFiles
                .Select(item => item.GetMetadata("SourceFile") is { Length: > 0 } sourcePath
                    ? new Calor.Compiler.CodeGen.GeneratedCSharpSource(
                        File.ReadAllText(item.ItemSpec), item.ItemSpec, sourcePath)
                    : new Calor.Compiler.CodeGen.GeneratedCSharpSource(
                        File.ReadAllText(item.ItemSpec), item.ItemSpec))
                .Concat(pendingOutputs.Select(item =>
                    new Calor.Compiler.CodeGen.GeneratedCSharpSource(
                        Encoding.UTF8.GetString(item.GeneratedBytes),
                        item.OutputPath,
                        item.InputPath)))
                .ToList();
            var generatedOutputPaths = generatedFiles
                .Select(item => Path.GetFullPath(item.ItemSpec))
                .Concat(pendingOutputs.Select(item => Path.GetFullPath(item.OutputPath)))
                .ToHashSet(pathComparer);
            var projectSources = ProjectSourceFiles
                .Select(item => item.GetMetadata("FullPath") is { Length: > 0 } fullPath
                    ? fullPath
                    : item.ItemSpec)
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Where(path => !generatedOutputPaths.Contains(path))
                .Select(path => new Calor.Compiler.CodeGen.GeneratedCSharpSource(
                    File.ReadAllText(path), path))
                .ToList();
            var validation = Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(
                generatedSources,
                new Calor.Compiler.CodeGen.GeneratedCSharpCompilationContext
                {
                    ReferencePaths = referencedAssemblyInputs
                        .Where(reference => reference.IsUsable)
                        .Select(reference => reference.Path),
                    AdditionalSources = projectSources,
                    AllowUnsafe = AllowUnsafeBlocks,
                    OutputKind = ParseOutputKind(ProjectOutputType),
                    LanguageVersion = ParseLanguageVersion(LanguageVersion),
                    IncludeImplicitGlobalUsings = IsImplicitUsingsEnabled(ImplicitUsings),
                    AnalyzerPaths = AnalyzerAssemblies
                        .Select(item => item.GetMetadata("FullPath") is { Length: > 0 } fullPath
                            ? fullPath
                            : item.ItemSpec),
                    AdditionalFilePaths = AdditionalFiles
                        .Select(item => item.GetMetadata("FullPath") is { Length: > 0 } fullPath
                            ? fullPath
                            : item.ItemSpec),
                    NullableContextOptions = ParseNullableContextOptions(Nullable),
                    TreatWarningsAsErrors = TreatWarningsAsErrors,
                    AnalyzerConfigPath = AnalyzerConfigPath,
                    PreprocessorSymbols = DefineConstants.Split(
                        [';', ','],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                });
            if (!validation.CompilationSuccess)
            {
                generatedValidationFailed = true;
                success = false;
                var diagnostics = new DiagnosticBag();
                Program.AddGeneratedOutputDiagnostics(
                    validation,
                    diagnostics,
                    generatedSources[0].Path);
                foreach (var diagnostic in diagnostics)
                {
                    LogDiagnostic(
                        diagnostic.Code,
                        "error",
                        diagnostic.Message,
                        diagnostic.FilePath ?? generatedSources[0].Path,
                        diagnostic.Span.Line,
                        diagnostic.Span.Column);
                }

                foreach (var pending in pendingOutputs)
                {
                    if (File.Exists(pending.OutputPath))
                    {
                        try { File.Delete(pending.OutputPath); } catch { /* best-effort */ }
                    }
                }
            }
        }

        if (!generatedValidationFailed)
        {
            foreach (var pending in pendingOutputs)
            {
                try
                {
                    File.WriteAllBytes(pending.OutputPath, pending.GeneratedBytes);
                    CacheTestHook?.Invoke(
                        pending.InputPath, CompileCalorTestPhase.BeforeCacheEntrySaved);
                    if (!TranspileOnly)
                    {
                        newState.Files[pending.RelativePath] = pending.CacheEntry;
                    }

                    var item = new TaskItem(pending.OutputPath);
                    item.SetMetadata("SourceFile", pending.InputPath);
                    generatedFiles.Add(item);
                    Log.LogMessage(
                        MessageImportance.Normal, "Generated: {0}", pending.OutputPath);
                }
                catch (Exception ex)
                {
                    Log.LogError(
                        "Failed to write generated output for {0}: {1}",
                        pending.InputPath,
                        ex.Message);
                    if (File.Exists(pending.OutputPath))
                    {
                        try { File.Delete(pending.OutputPath); } catch { /* best-effort */ }
                    }
                    success = false;
                }
            }
        }

        // 4d. Cross-module effect enforcement — runs over ALL module summaries (fresh from
        //     this build + cached from skipped files). Because summaries are persisted in
        //     the build cache, warm builds get complete cross-module coverage without any
        //     re-parsing: skipped files contribute their cached summary and freshly-compiled
        //     files contribute a newly-built one.
        if (EnforceEffects && moduleSummaries.Count > 1)
        {
            try
            {
                CacheTestHook?.Invoke(
                    ProjectDirectory, CompileCalorTestPhase.BeforeCrossModuleEnforcement);
                Log.LogMessage(MessageImportance.Normal,
                    "Calor: running cross-module effect enforcement over {0} modules",
                    moduleSummaries.Count);

                var registry = CrossModuleEffectRegistry.Build(moduleSummaries);

                foreach (var diagnostic in registry.BuildDiagnostics)
                {
                    Log.LogWarning(
                        subcategory: "Calor",
                        warningCode: diagnostic.Code,
                        helpKeyword: null,
                        file: diagnostic.FilePath ?? string.Empty,
                        lineNumber: diagnostic.Span.Line,
                        columnNumber: diagnostic.Span.Column,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: diagnostic.Message);
                }

                var crossPass = new CrossModuleEffectEnforcementPass();
                var crossDiagnostics = crossPass.Enforce(moduleSummaries, registry);

                foreach (var diagnostic in crossDiagnostics)
                {
                    if (diagnostic.IsError)
                    {
                        Log.LogError(
                            subcategory: "Calor",
                            errorCode: diagnostic.Code,
                            helpKeyword: null,
                            file: diagnostic.FilePath ?? string.Empty,
                            lineNumber: diagnostic.Span.Line,
                            columnNumber: diagnostic.Span.Column,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            message: diagnostic.Message);
                        success = false;
                    }
                    else if (diagnostic.IsWarning)
                    {
                        Log.LogWarning(
                            subcategory: "Calor",
                            warningCode: diagnostic.Code,
                            helpKeyword: null,
                            file: diagnostic.FilePath ?? string.Empty,
                            lineNumber: diagnostic.Span.Line,
                            columnNumber: diagnostic.Span.Column,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            message: diagnostic.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail closed (#788): EnforceEffects=true means cross-module
                // enforcement is part of the requested safety guarantee — an
                // exception here must not silently downgrade the build to
                // single-module checking only.
                Log.LogError(
                    "Calor: cross-module effect enforcement failed ({0}: {1}). "
                    + "The build fails rather than silently skipping the requested enforcement; "
                    + "set CalorEnforceEffects=false to build without it.",
                    ex.GetType().Name, ex.Message);
                success = false;
            }
        }

        // 5. Orphan cleanup (scoped to prior cache entries only)
        if (priorCache?.Files != null)
        {
            foreach (var kvp in priorCache.Files)
            {
                if (!currentRelativePaths.Contains(kvp.Key))
                {
                    // This file was in the prior cache but not in current SourceFiles — orphan
                    var orphanRelative = Path.ChangeExtension(
                        kvp.Key.Replace('/', Path.DirectorySeparatorChar), ".g.cs");
                    var orphanPath = Path.Combine(OutputDirectory, orphanRelative);
                    if (File.Exists(orphanPath))
                    {
                        try
                        {
                            File.Delete(orphanPath);
                            if (Verbose)
                            {
                                Log.LogMessage(MessageImportance.Normal,
                                    "Calor: removed orphan output: {0}", orphanPath);
                            }
                        }
                        catch { /* best-effort */ }
                    }
                }
            }
        }

        // 6. Save new cache state
        try
        {
            if (!TranspileOnly && !generatedValidationFailed)
            {
                BuildStateCache.Save(newState, OutputDirectory);
            }
        }
        catch (Exception ex)
        {
            if (BuildStateCache.Delete(OutputDirectory))
            {
                Log.LogWarning(
                    "Calor: failed to save build state cache; the cache was purged and the next build "
                    + "will run cold: {0}", ex.Message);
            }
            else
            {
                Log.LogError(
                    "Calor: failed to save or purge build state cache. Incremental state cannot be "
                    + "trusted, so the build fails closed: {0}", ex.Message);
                success = false;
            }
        }

        GeneratedFiles = generatedValidationFailed ? [] : generatedFiles.ToArray();
        return success;
        }
        finally
        {
            ilAnalyzer?.Dispose();
            compilationContext?.Dispose();
        }
    }

    private void LogDiagnostic(
        string code, string severity, string message,
        string filePath, int line, int column)
    {
        if (severity == "error")
        {
            Log.LogError(
                "Calor", code, null, filePath, line, column, 0, 0, message);
        }
        else if (severity == "warning")
        {
            Log.LogWarning(
                "Calor", code, null, filePath, line, column, 0, 0, message);
        }
        else
        {
            Log.LogMessage(
                "Calor", code, null, filePath, line, column, 0, 0,
                MessageImportance.Normal, message);
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile", "IL3000",
        Justification = "The MSBuild task is deployed as files; missing locations are represented in the cache fingerprint.")]
    internal static IReadOnlyList<string> ResolveCompilerClosurePaths(string tasksAssemblyPath)
    {
        var tasksDirectory = Path.GetDirectoryName(tasksAssemblyPath) ?? string.Empty;
        var compilerPath = typeof(Program).Assembly.Location;
        var compilerDirectory = Path.GetDirectoryName(compilerPath) ?? tasksDirectory;
        var nativeZ3Name = OperatingSystem.IsWindows() ? "libz3.dll"
            : OperatingSystem.IsMacOS() ? "libz3.dylib" : "libz3.so";
        return new[]
        {
            tasksAssemblyPath,
            compilerPath,
            typeof(Calor.Runtime.Option<int>).Assembly.Location,
            Path.Combine(compilerDirectory, "Microsoft.Z3.dll"),
            Path.Combine(compilerDirectory, nativeZ3Name)
        }
        .Concat(Compiler.Verification.Z3.Z3ContextFactory.GetNativeLibraryProbePaths())
        .Distinct(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal)
        .ToList();
    }

    private static string DecodeSource(byte[] bytes)
    {
        using var reader = new StreamReader(
            new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static Microsoft.CodeAnalysis.OutputKind ParseOutputKind(string outputType)
        => outputType.ToLowerInvariant() switch
        {
            "exe" => Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            "winexe" => Microsoft.CodeAnalysis.OutputKind.WindowsApplication,
            _ => Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary
        };

    private static Microsoft.CodeAnalysis.CSharp.LanguageVersion ParseLanguageVersion(
        string languageVersion)
        => Microsoft.CodeAnalysis.CSharp.LanguageVersionFacts.TryParse(
            languageVersion,
            out var parsed)
            ? parsed
            : Microsoft.CodeAnalysis.CSharp.LanguageVersion.Default;

    private static bool IsImplicitUsingsEnabled(string value)
        => value.Equals("enable", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static Microsoft.CodeAnalysis.NullableContextOptions ParseNullableContextOptions(
        string value)
        => value.ToLowerInvariant() switch
        {
            "enable" => Microsoft.CodeAnalysis.NullableContextOptions.Enable,
            "warnings" => Microsoft.CodeAnalysis.NullableContextOptions.Warnings,
            "annotations" => Microsoft.CodeAnalysis.NullableContextOptions.Annotations,
            _ => Microsoft.CodeAnalysis.NullableContextOptions.Disable
        };
}
