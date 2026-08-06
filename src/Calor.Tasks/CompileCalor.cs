using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Calor.Compiler;
using Calor.Compiler.Effects;

namespace Calor.Tasks;

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
    /// The options token, extracted so it can be pinned directly. Note the type-check parameter is
    /// the EFFECTIVE value (<c>TypeCheck &amp;&amp; CompilationOptions.TypeCheckingDefault</c>), not the
    /// task property: the first cut hashed the property, so flipping <c>CALOR_NO_TYPE_CHECK</c>
    /// against a warm cache changed what was reported without invalidating anything, and every
    /// unchanged file was silently skipped — the #788 defect described at the call site.
    /// </summary>
    internal static string OptionsToken(
        bool enforceEffects,
        bool effectiveTypeCheck,
        bool verify,
        bool ilAnalysis,
        string canonicalExperimentalFlags)
        => $"enforceEffects:{enforceEffects}|typeCheck:{effectiveTypeCheck}|verify:{verify}"
           + $"|ilAnalysis:{ilAnalysis}|experimental:{canonicalExperimentalFlags}";

    /// <summary>
    /// Run static contract verification during compilation (Annex A-1.3
    /// instrumentation item 1): refutations surface as Calor0712-band build
    /// diagnostics (Warning severity — the build still succeeds). Off by
    /// default; the Guarantees probe epoch's v0.10 arm turns it on via the
    /// workspace template.
    /// </summary>
    public bool Verify { get; set; }

    /// <summary>
    /// Semicolon- or comma-separated list of experimental feature flag names to enable.
    /// Plumbed through to <see cref="Calor.Compiler.CompilationOptions.ExperimentalFlags"/>.
    /// Unknown flags are accepted silently — see <see cref="Calor.Compiler.ExperimentalFlags"/>.
    /// </summary>
    public string ExperimentalFlags { get; set; } = string.Empty;

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
        BuildState? priorCache;
        try
        {
            priorCache = BuildStateCache.Load(OutputDirectory);
        }
        catch
        {
            priorCache = null;
        }

        // 2. Compute global hashes
        var tasksAssemblyPath = typeof(CompileCalor).Assembly.Location;
        var compilerHash = BuildStateCache.ComputeCompilerHash(tasksAssemblyPath);
        // Every diagnostics-affecting task option must be in the options token:
        // flipping one with a warm cache has to force a recompile, or findings
        // the new option set would report on unchanged files are silently
        // missed (#788: ExperimentalFlags and EnableILAnalysis were omitted).
        // Experimental flags are canonicalized (parsed, sorted, case-folded) so
        // "a;b" and "B,a" hash identically.
        var canonicalExperimentalFlags = string.Join(",",
            Calor.Compiler.ExperimentalFlags.Parse(ExperimentalFlags).EnabledFlags
                .Select(f => f.ToLowerInvariant())
                .OrderBy(f => f, StringComparer.Ordinal));
        var optionsHash = BuildStateCache.ComputeOptionsHash(OptionsToken(
            EnforceEffects,
            TypeCheck && CompilationOptions.TypeCheckingDefault,
            Verify,
            EnableILAnalysis,
            canonicalExperimentalFlags));
        var manifestHash = BuildStateCache.ComputeManifestHash(ProjectDirectory);

        // 3. Global invalidation check
        // Pre-compute full project dir once — avoids per-file Path.GetFullPath
        var fullProjectDir = Path.GetFullPath(ProjectDirectory);
        // Store output directory relative to project for cache portability across machines
        var relativeOutputDir = BuildStateCache.NormalizeRelativePath(
            Path.GetRelativePath(fullProjectDir, Path.GetFullPath(OutputDirectory)));
        var globalInvalidation = BuildStateCache.IsGlobalInvalidation(
            priorCache, compilerHash, optionsHash, manifestHash, relativeOutputDir);

        if (globalInvalidation && Verbose)
        {
            Log.LogMessage(MessageImportance.High, "Calor: global invalidation — recompiling all files.");
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
                var assemblyPaths = ReferencedAssemblies
                    .Select(item => item.GetMetadata("FullPath"))
                    .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                    .ToList();

                if (assemblyPaths.Count > 0)
                {
                    var ilOptions = new Compiler.Effects.IL.ILAnalysisOptions
                    {
                        RuntimeDirectory = !string.IsNullOrEmpty(RuntimeDirectory) ? RuntimeDirectory : null,
                        NuGetPackageRoot = !string.IsNullOrEmpty(NuGetPackageRoot) ? NuGetPackageRoot : null,
                        DepsFilePath = !string.IsNullOrEmpty(DepsFilePath) ? DepsFilePath : null
                    };

                    var resolver = new Compiler.Effects.EffectResolver();
                    resolver.Initialize(ProjectDirectory);

                    ilAnalyzer = new Compiler.Effects.IL.ILEffectAnalyzer(
                        assemblyPaths, resolver, ilOptions);

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
        var success = true;
        var pathComparer = BuildStateCache.GetPathComparer();
        var currentRelativePaths = new HashSet<string>(pathComparer);
        // Collect per-module effect summaries (from fresh ASTs + cached entries) so the
        // cross-module pass can always see every module, even when incremental caching
        // skipped recompilation.
        var moduleSummaries = new List<(Calor.Compiler.Effects.EffectSummary Summary, string FilePath)>();
        // Track output paths to detect collisions from out-of-project file sanitization
        var outputPaths = new Dictionary<string, string>(pathComparer);

        // On warm path: build lookup dictionary + pre-scan outputs to avoid per-file stat calls
        Dictionary<string, BuildFileEntry>? priorFiles = null;
        HashSet<string>? existingOutputFiles = null;
        if (!globalInvalidation && priorCache?.Files != null)
        {
            priorFiles = new Dictionary<string, BuildFileEntry>(priorCache.Files, pathComparer);
            existingOutputFiles = new HashSet<string>(
                Directory.Exists(OutputDirectory)
                    ? Directory.GetFiles(OutputDirectory, "*.g.cs", SearchOption.AllDirectories)
                    : [],
                pathComparer);
        }

        // Cross-module call qualification map (G3/#809, #823 review M2): MSBuild is
        // the surface where csc actually consumes the outputs, so it needs the same
        // map the CLI driver builds. Warm-skip validity: a changed map invalidates
        // every skip (a cached .g.cs may carry stale qualification).
        IReadOnlyDictionary<string, string>? crossModuleMap = null;
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
        if (priorFiles != null && priorCache?.CrossModuleMapHash != crossModuleMapHash)
        {
            priorFiles = null;
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

                // The skip conditions mirror CompilationDriver.cs:178-198 deliberately — the two
                // sites decide the same thing (is this file's cached output trustworthy?) and had
                // drifted apart, with this one weaker on both counts.
                //
                //  - OutputContentHash: presence of the .g.cs is NOT enough. A truncated, corrupted,
                //    or hand-edited output must be a miss, or the build compiles stale bytes and
                //    reports "up-to-date". Entries predating this check carry a null hash and are
                //    therefore a miss — one cold rebuild, fail-closed, as the driver does.
                //  - EffectSummary: skipping without one silently drops the module from
                //    cross-module effect enforcement, so its Calor0410 violations vanish on warm
                //    builds. Not reachable through this task today (a null Ast implies HasErrors,
                //    which returns before caching), so this is defence in depth against a future
                //    caller — not a fix for a live bug.
                if (cachedEntry != null
                    && existingOutputFiles!.Contains(outputPath)
                    && cachedEntry.EffectSummary != null
                    && cachedEntry.OutputContentHash != null
                    && BuildStateCache.ComputeFileHash(outputPath) == cachedEntry.OutputContentHash
                    && BuildStateCache.IsFileUpToDate(cachedEntry, inputPath))
                {
                    // Skip — carry entry forward
                    newState.Files[relativePath] = cachedEntry;
                    var outputItem = new TaskItem(outputPath);
                    outputItem.SetMetadata("SourceFile", inputPath);
                    generatedFiles.Add(outputItem);

                    // Carry forward cached effect summary so cross-module enforcement
                    // still sees this module on warm builds.
                    if (cachedEntry.EffectSummary != null)
                    {
                        moduleSummaries.Add((cachedEntry.EffectSummary, inputPath));
                    }

                    if (Verbose)
                    {
                        Log.LogMessage(MessageImportance.Normal,
                            "Calor: skipping (up-to-date): {0}", inputPath);
                    }
                    continue;
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
                var source = File.ReadAllText(inputPath);
                var compileOptions = new CompilationOptions
                {
                    Verbose = Verbose,
                    EnforceEffects = EnforceEffects,
                    EnableTypeChecking = TypeCheck && CompilationOptions.TypeCheckingDefault,
                    ProjectDirectory = ProjectDirectory,
                    Context = compilationContext,
                    EnableILAnalysis = EnableILAnalysis,
                    ExperimentalFlags = Calor.Compiler.ExperimentalFlags.Parse(ExperimentalFlags),
                    VerifyContracts = Verify
                };
                compileOptions.CrossModuleFunctionModules = crossModuleMap;
                var result = Program.Compile(source, inputPath, compileOptions);

                // Log ALL diagnostics, not only on failure: verification findings
                // (e.g. Calor0711/0712 refutation warnings) arrive on otherwise
                // successful compiles and must reach MSBuild output — dropping
                // them here would make the verify gate silent exactly when it
                // has something to say.
                foreach (var diagnostic in result.Diagnostics)
                {
                    if (diagnostic.IsError)
                    {
                        Log.LogError(
                            subcategory: "Calor",
                            errorCode: diagnostic.Code,
                            helpKeyword: null,
                            file: diagnostic.FilePath ?? inputPath,
                            lineNumber: diagnostic.Span.Line,
                            columnNumber: diagnostic.Span.Column,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            message: diagnostic.Message);
                    }
                    else if (diagnostic.IsWarning)
                    {
                        Log.LogWarning(
                            subcategory: "Calor",
                            warningCode: diagnostic.Code,
                            helpKeyword: null,
                            file: diagnostic.FilePath ?? inputPath,
                            lineNumber: diagnostic.Span.Line,
                            columnNumber: diagnostic.Span.Column,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            message: diagnostic.Message);
                    }
                    else
                    {
                        Log.LogMessage(
                            subcategory: "Calor",
                            code: diagnostic.Code,
                            helpKeyword: null,
                            file: diagnostic.FilePath ?? inputPath,
                            lineNumber: diagnostic.Span.Line,
                            columnNumber: diagnostic.Span.Column,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            importance: MessageImportance.Normal,
                            message: diagnostic.Message);
                    }
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

                File.WriteAllText(outputPath, result.GeneratedCode);

                // Compute effect summary from the fresh AST and cache it for future warm builds.
                var fileEntry = BuildStateCache.CreateFileEntry(inputPath);

                // Record what this compile actually wrote, so the next build can tell whether the
                // .g.cs on disk is still that output. Without this every entry is a permanent miss
                // under the check above — which is safe, but defeats incrementality entirely.
                fileEntry.OutputContentHash = File.Exists(outputPath)
                    ? BuildStateCache.ComputeFileHash(outputPath)
                    : null;

                if (result.Ast != null)
                {
                    var summary = Calor.Compiler.Effects.EffectSummaryBuilder.Build(result.Ast);
                    fileEntry.EffectSummary = summary;
                    moduleSummaries.Add((summary, inputPath));
                }
                newState.Files[relativePath] = fileEntry;

                var item = new TaskItem(outputPath);
                item.SetMetadata("SourceFile", inputPath);
                generatedFiles.Add(item);

                Log.LogMessage(MessageImportance.Normal, "Generated: {0}", outputPath);
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

        // 4d. Cross-module effect enforcement — runs over ALL module summaries (fresh from
        //     this build + cached from skipped files). Because summaries are persisted in
        //     the build cache, warm builds get complete cross-module coverage without any
        //     re-parsing: skipped files contribute their cached summary and freshly-compiled
        //     files contribute a newly-built one.
        if (EnforceEffects && moduleSummaries.Count > 1)
        {
            try
            {
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
        if (priorFiles != null)
        {
            foreach (var kvp in priorFiles)
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
            BuildStateCache.Save(newState, OutputDirectory);
        }
        catch (Exception ex)
        {
            Log.LogWarning("Calor: failed to save build state cache: {0}", ex.Message);
        }

        GeneratedFiles = generatedFiles.ToArray();
        return success;
        }
        finally
        {
            ilAnalyzer?.Dispose();
            compilationContext?.Dispose();
        }
    }
}
