using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;

namespace Calor.Compiler;

/// <summary>
/// Shared multi-file compile orchestration used by the top-level compile command
/// (<see cref="Program"/>), <c>calor watch</c>, and <c>calor run</c> / <c>calor test</c>
/// (<c>ExecutionWorkspace</c>). One place owns the loop semantics:
/// <list type="bullet">
///   <item>Each file is compiled with options from <c>optionsFactory</c>.</item>
///   <item>Warnings are always printed to stderr — including warnings produced by
///     demotion under the permissive policy — not only when a file has errors.</item>
///   <item>Cross-module effect enforcement runs over the successfully compiled
///     modules regardless of whether other files failed, and honors the
///     <see cref="UnknownCallPolicy"/> (permissive demotes violations to warnings).</item>
///   <item>With <see cref="DriverCacheSettings"/>, unchanged files are skipped via the
///     persisted <c>.calor-build-state.json</c> (same format as the MSBuild task);
///     skipped files still participate in cross-module effect enforcement through
///     their cached per-module effect summaries.</item>
/// </list>
/// </summary>
internal static class CompilationDriver
{
    internal sealed record FileResult(FileInfo File, CompilationResult Result);

    internal sealed record DriverResult(List<FileResult> Compiled, bool AnyErrors, List<FileInfo> Skipped);

    /// <summary>
    /// Incremental-build settings for <see cref="CompileAll"/>.
    /// </summary>
    /// <param name="StateDirectory">
    /// Directory holding <c>.calor-build-state.json</c> — the common ancestor of the
    /// inputs, so the state file sits next to the generated <c>.g.cs</c> outputs
    /// (CLI outputs are written alongside their inputs). Also the base directory
    /// for the cache's relative-path keys.
    /// </param>
    /// <param name="OptionsToken">
    /// Canonical string of all diagnostics-affecting compile options; a change
    /// invalidates every cached entry (see <see cref="BuildStateCache.ComputeOptionsHash(string)"/>).
    /// </param>
    /// <param name="ClearFirst">Delete the state file before compiling (<c>--clear-cache</c>).</param>
    /// <param name="OutputPathFor">Maps an input to its output path, used to verify the output still exists before skipping.</param>
    internal sealed record DriverCacheSettings(
        string StateDirectory,
        string OptionsToken,
        bool ClearFirst,
        Func<FileInfo, string> OutputPathFor);

    /// <summary>
    /// Compiles all <paramref name="sources"/> in order. <paramref name="onCompiled"/>
    /// is invoked for each successfully compiled file (e.g. to write its output),
    /// before cross-module enforcement runs.
    /// </summary>
    /// <param name="crossModuleEnforcement">
    /// Whether to run cross-module effect enforcement when more than one module
    /// compiled successfully. The top-level compile command always passes true
    /// (its historical behavior); run/test pass their effective effects-enforcement
    /// setting.
    /// </param>
    /// <param name="diagnosticSink">
    /// When non-null, diagnostics (per-file and cross-module) are collected into
    /// this bag instead of being printed to stderr. Used by structured output
    /// modes (<c>--format json|sarif</c>) where a <see cref="Diagnostics.IDiagnosticFormatter"/>
    /// serializes the aggregate at the end; fix information is preserved via
    /// <see cref="DiagnosticBag.AddRange"/>.
    /// </param>
    /// <param name="cache">
    /// When non-null, enables the incremental-build cache: unchanged files (by
    /// content hash, guarded by compiler-hash / options-hash / manifest-hash
    /// invalidation) are skipped and contribute their cached effect summary to
    /// cross-module enforcement instead of a fresh AST.
    /// </param>
    /// <param name="onSkipped">
    /// Invoked for each up-to-date file the cache skipped, with its output path.
    /// </param>
    internal static DriverResult CompileAll(
        IReadOnlyList<FileInfo> sources,
        Func<FileInfo, CompilationOptions> optionsFactory,
        bool crossModuleEnforcement,
        UnknownCallPolicy crossModulePolicy,
        Action<FileInfo, CompilationResult>? onCompiled = null,
        DiagnosticBag? diagnosticSink = null,
        DriverCacheSettings? cache = null,
        Action<FileInfo, string>? onSkipped = null,
        Action<FileInfo, string, ModuleNode>? onAst = null)
    {
        var compiled = new List<FileResult>();
        var skipped = new List<FileInfo>();
        // Per-module effect summaries feeding cross-module enforcement: fresh
        // summaries for compiled files, cache-restored summaries for skipped ones.
        var moduleSummaries = new List<(EffectSummary Summary, string FilePath)>();
        var anyErrors = false;

        // --- Cache setup: load prior state, compute global invalidation ---
        BuildState? priorState = null;
        BuildState? newState = null;
        Dictionary<string, BuildFileEntry>? priorFiles = null;
        string? fullStateDir = null;
        var globalInvalidation = true;
        if (cache != null)
        {
            if (cache.ClearFirst)
            {
                BuildStateCache.Delete(cache.StateDirectory);
            }

            fullStateDir = Path.GetFullPath(cache.StateDirectory);
            priorState = BuildStateCache.Load(cache.StateDirectory);
            var compilerHash = BuildStateCache.ComputeCliCompilerHash();
            var optionsHash = BuildStateCache.ComputeOptionsHash(cache.OptionsToken);
            var manifestDirs = sources
                .Select(f => Path.GetDirectoryName(f.FullName)!)
                .Distinct(BuildStateCache.GetPathComparer())
                .ToList();
            var manifestHash = BuildStateCache.ComputeManifestHash(manifestDirs);
            // CLI outputs always sit next to their inputs; "." keeps the field
            // meaningful (and format-compatible) without a separate output root.
            const string outputDirToken = ".";
            globalInvalidation = BuildStateCache.IsGlobalInvalidation(
                priorState, compilerHash, optionsHash, manifestHash, outputDirToken);
            if (!globalInvalidation && priorState?.Files != null)
            {
                priorFiles = new Dictionary<string, BuildFileEntry>(
                    priorState.Files, BuildStateCache.GetPathComparer());
            }

            newState = new BuildState
            {
                CompilerHash = compilerHash,
                OptionsHash = optionsHash,
                ManifestHash = manifestHash,
                OutputDirectory = outputDirToken
            };
        }

        foreach (var file in sources)
        {
            // --- Warm path: skip unchanged files, reusing the cached effect summary ---
            string? relativeKey = null;
            if (cache != null && fullStateDir != null && newState != null)
            {
                (relativeKey, _) = BuildStateCache.ComputeRelativePathFromFullProjectDir(
                    file.FullName, fullStateDir);

                if (priorFiles != null
                    && priorFiles.TryGetValue(relativeKey, out var cachedEntry)
                    // A cache hit without an effect summary is NOT a hit: skipping
                    // would silently drop this module from cross-module effect
                    // enforcement (its Calor0410 violations would vanish on warm
                    // builds). Recompile to rebuild the summary.
                    && cachedEntry.EffectSummary != null
                    && BuildStateCache.IsFileUpToDate(cachedEntry, file.FullName))
                {
                    // The output is only trusted when its content hash matches what
                    // the producing compile observed: a corrupted, truncated, or
                    // manually edited .g.cs must be a miss, not "Up-to-date".
                    var outputPath = cache.OutputPathFor(file);
                    if (cachedEntry.OutputContentHash != null
                        && File.Exists(outputPath)
                        && BuildStateCache.ComputeFileHash(outputPath) == cachedEntry.OutputContentHash)
                    {
                        newState.Files[relativeKey] = cachedEntry;
                        skipped.Add(file);
                        moduleSummaries.Add((cachedEntry.EffectSummary, file.FullName));
                        onSkipped?.Invoke(file, outputPath);
                        continue;
                    }
                }
            }

            var options = optionsFactory(file);
            if (options.Verbose)
            {
                (options.StatusWriter ?? Console.Out).WriteLine($"Compiling: {file.FullName}");
            }

            // Stat first, then read the bytes that are actually compiled. The cache
            // entry is built from these exact bytes (never re-read from disk): a
            // concurrent edit landing mid-compile must not be recorded as compiled,
            // or the next run would skip it and the new content would never build.
            var statBeforeRead = BuildStateCache.StatFile(file.FullName);
            var sourceBytes = File.ReadAllBytes(file.FullName);
            var source = DecodeSource(sourceBytes);
            var result = Program.Compile(source, file.FullName, options);

            // Fires even for error-bearing files: declaration-ID enrichment of
            // their diagnostics needs the AST whenever parsing got far enough.
            if (result.Ast != null)
            {
                onAst?.Invoke(file, source, result.Ast);
            }

            if (diagnosticSink != null)
            {
                diagnosticSink.AddRange(result.Diagnostics);
            }
            else
            {
                PrintDiagnostics(result.Diagnostics);
            }

            if (result.HasErrors)
            {
                // Failed files are never cached — the next run recompiles them
                // and re-reports their diagnostics.
                anyErrors = true;
                continue;
            }

            compiled.Add(new FileResult(file, result));
            onCompiled?.Invoke(file, result);

            EffectSummary? summary = null;
            if (result.Ast != null && (crossModuleEnforcement || newState != null))
            {
                summary = EffectSummaryBuilder.Build(result.Ast);
                moduleSummaries.Add((summary, file.FullName));
            }

            // Only diagnostic-clean files are cached: a skipped file emits nothing,
            // so caching a file with warnings/info would silently drop those
            // diagnostics from warm builds. (Cross-module diagnostics are exempt —
            // they are recomputed from summaries every run, so skipped files still
            // surface them.)
            if (newState != null && relativeKey != null && cache != null
                && result.Diagnostics.Count == 0)
            {
                var entry = BuildStateCache.CreateFileEntry(statBeforeRead, sourceBytes);
                entry.EffectSummary = summary;
                // Record what the output actually contains after onCompiled wrote
                // it; warm builds only skip when the on-disk output still hashes
                // to this value. No output observed -> entry never skips.
                var writtenOutputPath = cache.OutputPathFor(file);
                entry.OutputContentHash = File.Exists(writtenOutputPath)
                    ? BuildStateCache.ComputeFileHash(writtenOutputPath)
                    : null;
                newState.Files[relativeKey] = entry;
            }
        }

        // Cross-module effect enforcement over successfully compiled modules —
        // runs even when other files failed, so all reportable violations surface
        // in one pass (top-level compile semantics). Skipped files participate
        // through their cache-restored summaries.
        if (crossModuleEnforcement && moduleSummaries.Count > 1)
        {
            var registry = CrossModuleEffectRegistry.Build(moduleSummaries);
            foreach (var diagnostic in registry.BuildDiagnostics)
            {
                if (diagnosticSink != null)
                {
                    diagnosticSink.Add(diagnostic);
                }
                else
                {
                    Console.Error.WriteLine(diagnostic);
                }
            }

            var crossPass = new CrossModuleEffectEnforcementPass(crossModulePolicy);
            var crossDiagnostics = crossPass.Enforce(moduleSummaries, registry);

            foreach (var diagnostic in crossDiagnostics)
            {
                if (diagnosticSink != null)
                {
                    diagnosticSink.Add(diagnostic);
                }
                else
                {
                    Console.Error.WriteLine(diagnostic);
                }

                if (diagnostic.IsError)
                {
                    anyErrors = true;
                }
            }
        }

        if (newState != null && cache != null)
        {
            BuildStateCache.Save(newState, cache.StateDirectory);
        }

        return new DriverResult(compiled, anyErrors, skipped);
    }

    /// <summary>
    /// Decodes source bytes with the same semantics as <see cref="File.ReadAllText(string)"/>
    /// (UTF-8 default, BOM detection) so the compiled text matches what a plain
    /// read would have produced while the cache hashes the raw bytes.
    /// </summary>
    private static string DecodeSource(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Prints every diagnostic — including Info severity — to stderr. This
    /// deliberately matches the structured output modes (--format json|sarif),
    /// which serialize all severities, so text and machine output report the
    /// same set of diagnostics.
    /// </summary>
    private static void PrintDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }
    }

    /// <summary>
    /// Parses a --contract-mode CLI value ("off", "debug", "release"; case-insensitive).
    /// Unrecognized values fall back to <see cref="ContractMode.Debug"/>.
    /// </summary>
    internal static ContractMode ParseContractMode(string? contractMode) =>
        contractMode?.ToLowerInvariant() switch
        {
            "off" => ContractMode.Off,
            "release" => ContractMode.Release,
            _ => ContractMode.Debug
        };
}
