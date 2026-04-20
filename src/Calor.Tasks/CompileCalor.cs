using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Calor.Compiler;

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
        var optionsHash = BuildStateCache.ComputeOptionsHash();
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

        var generatedFiles = new List<ITaskItem>();
        var success = true;
        var pathComparer = BuildStateCache.GetPathComparer();
        var currentRelativePaths = new HashSet<string>(pathComparer);
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

                if (cachedEntry != null
                    && existingOutputFiles!.Contains(outputPath)
                    && BuildStateCache.IsFileUpToDate(cachedEntry, inputPath))
                {
                    // Skip — carry entry forward
                    newState.Files[relativePath] = cachedEntry;
                    var outputItem = new TaskItem(outputPath);
                    outputItem.SetMetadata("SourceFile", inputPath);
                    generatedFiles.Add(outputItem);

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
                var result = Program.Compile(source, inputPath, false);

                if (result.HasErrors)
                {
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

                    // Failure: delete prior .g.cs if exists, do NOT cache
                    if (File.Exists(outputPath))
                    {
                        try { File.Delete(outputPath); } catch { /* best-effort */ }
                    }

                    success = false;
                    continue;
                }

                File.WriteAllText(outputPath, result.GeneratedCode);

                // Record in new state
                newState.Files[relativePath] = BuildStateCache.CreateFileEntry(inputPath);

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
}
