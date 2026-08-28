using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;

namespace Calor.Compiler;

/// <summary>
/// Which generated C# outputs may be validated when some file in the same run
/// FAILED — the one place the rule lives, shared by every surface that batches
/// generated sources into a single Roslyn compilation
/// (<see cref="CompilationDriver"/> and the MSBuild <c>CompileCalor</c> task).
///
/// <para><b>The problem.</b> Generated-output validation compiles this run's
/// outputs together. A file that failed contributes no output, so every clean
/// file that CALLS it fails to compile for a reason that is not its own —
/// <c>Calor1002</c>, "the name 'Map' does not exist" — while the failing file's
/// real diagnostic scrolls past above. Worse, the two paths disagreed: a warm
/// build whose only uncached file was the failing one had nothing pending and
/// skipped validation entirely, so the same workspace reported different
/// diagnostics cold and warm (found by ES-08, `tests/TestData/EditScripts`).</para>
///
/// <para><b>The rule.</b> Drop from the validation set exactly the outputs that
/// reference a failed file's module, and validate everything else. A genuine
/// <c>Calor1002</c> in an unrelated file is therefore still reported in the same
/// run — the naive fix, "skip validation whenever anything failed", hides it
/// (review round 2, C1). Suppressed outputs are treated as UNVALIDATED: they are
/// not written, not cached, and not reported successful, because nothing checked
/// them. They are re-validated on the next run, once the failing file is fixed
/// or its callers are.</para>
///
/// <para><b>Reference</b> means: the output's C# mentions an identifier the
/// failed module owns — its module name, the emitted static-class name for any
/// of its functions, its function names, and the names of the types it declares.
/// Matching is on whole identifier tokens. It is deliberately over-inclusive: a
/// false positive defers one file's validation by one run, while a false
/// negative brings the cascade back.</para>
///
/// <para><b>Parse failures.</b> A file that did not parse yields no module, so
/// its owned identifiers are unknowable and no scope can be computed. Callers
/// are told so (<c>scopeIsComplete: false</c>) and must then do BOTH halves:
/// validate nothing, and treat every output of the run as suppressed. Doing only
/// the first half is what the round-2 fix did, and it was permissive rather than
/// conservative — nothing was checked, yet everything was written, cached and
/// reported successful (review round 3, C1-residual). A syntax error is the most
/// common build failure, so this branch runs constantly.</para>
/// </summary>
internal static class GeneratedValidationScope
{
    /// <summary>
    /// The identifiers the failed modules own in emitted C#.
    /// <paramref name="scopeIsComplete"/> is false when any failed file has no
    /// module (it did not parse), in which case the set is meaningless and the
    /// caller must not validate anything.
    /// </summary>
    internal static HashSet<string> OwnedIdentifiers(
        IEnumerable<ModuleNode?> failedModules,
        out bool scopeIsComplete)
    {
        ArgumentNullException.ThrowIfNull(failedModules);

        var owned = new HashSet<string>(StringComparer.Ordinal);
        scopeIsComplete = true;

        foreach (var module in failedModules)
        {
            if (module == null)
            {
                scopeIsComplete = false;
                continue;
            }

            Add(module.Name);
            foreach (var function in module.Functions)
            {
                Add(function.Name);
                var target = CrossModuleFunctionTarget.Create(module, function);
                if (target == null)
                    continue;
                Add(target.ModuleClassName);
                foreach (var segment in target.NamespaceIdentity.Split(
                             '.', StringSplitOptions.RemoveEmptyEntries))
                {
                    Add(segment);
                }
            }

            // Every declaration family that becomes a name in emitted C#. Missing
            // one is a false NEGATIVE — the cascade comes back for anything that
            // referenced it — so the list is exhaustive over ModuleNode rather
            // than "the common cases" (review round 3).
            foreach (var declaration in module.Classes)
                Add(declaration.Name);
            foreach (var declaration in module.Interfaces)
                Add(declaration.Name);
            foreach (var declaration in module.Enums)
                Add(declaration.Name);
            foreach (var declaration in module.EnumExtensions)
                Add(declaration.EnumName);
            foreach (var declaration in module.Delegates)
                Add(declaration.Name);
            foreach (var declaration in module.RefinementTypes)
                Add(declaration.Name);
            foreach (var declaration in module.IndexedTypes)
                Add(declaration.Name);

            // Types the module declares inside a §CSHARP block are emitted
            // verbatim and are just as referenceable; their names are not in the
            // AST as declarations, so they are read out of the interop text.
            foreach (var interop in module.InteropBlocks)
                AddInteropDeclarations(interop.CSharpCode);
            // …and the class- and interface-level blocks, which is where an
            // interop type most often sits in practice (review round 4, optional
            // minor — false-negative direction).
            foreach (var declaration in module.Classes)
                foreach (var interop in declaration.InteropBlocks)
                    AddInteropDeclarations(interop.CSharpCode);
            foreach (var declaration in module.Interfaces)
                foreach (var interop in declaration.InteropBlocks)
                    AddInteropDeclarations(interop.CSharpCode);
        }

        return owned;

        void Add(string? identifier)
        {
            if (string.IsNullOrEmpty(identifier) || identifier == "_global")
                return;
            // Defence in depth (review round 4, V1): a C# keyword is never a
            // declaration name, and admitting one is catastrophic rather than
            // merely wrong — every generated file contains `class`, so a single
            // keyword in the owned set makes References() true for everything
            // and silently degrades the scoped rule to "suppress the whole run".
            // The regex below is the first line of defence; this caps the blast
            // radius of any future slip in it.
            if (ReservedWords.Contains(identifier))
                return;
            owned.Add(identifier);
        }

        // `class Foo`, `record class Money`, `record struct Point`,
        // `interface IBaz`, `enum Qux`, `delegate int Quux<T>(...)` — the
        // identifier after a type-declaring keyword. Text-level on purpose:
        // interop is preserved verbatim and never parsed into the AST, so there
        // is nothing else to read.
        void AddInteropDeclarations(string? code)
        {
            if (string.IsNullOrEmpty(code))
                return;

            foreach (System.Text.RegularExpressions.Match match in
                     InteropTypeDeclaration.Matches(code))
            {
                Add(match.Groups["name"].Value);
            }
        }
    }

    /// <summary>
    /// The `record` alternative comes FIRST and consumes an optional
    /// `class`/`struct` modifier. Written the other way round (review round 4,
    /// V1) `record class Money` matched the `record` branch and captured
    /// **`class`** as the type name — which every generated file contains, so
    /// the whole run was suppressed on ordinary modern C#.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex InteropTypeDeclaration = new(
        @"\brecord\s+(?:class\s+|struct\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)"
            + @"|\b(?:class|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)"
            + @"|\bdelegate\s+[^\s(]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^(>]*>)?\s*\(",
        System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Words that can never be a declaration name. Admitting one poisons the
    /// scope (see <see cref="InteropTypeDeclaration"/>), so they are refused at
    /// the point of entry regardless of how they were produced.
    /// </summary>
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "class", "struct", "record", "interface", "enum", "delegate", "namespace",
        "using", "var", "void", "public", "private", "protected", "internal",
        "static", "sealed", "abstract", "partial", "readonly", "const", "new",
        "object", "string", "int", "long", "short", "byte", "bool", "char",
        "float", "double", "decimal", "uint", "ulong", "ushort", "sbyte",
        "dynamic", "this", "base", "null", "true", "false", "ref", "out", "in",
    };

    /// <summary>
    /// Whether one generated output mentions any owned identifier, matched on
    /// whole identifier tokens (so <c>MapReduce</c> is not a hit for <c>Map</c>).
    /// </summary>
    internal static bool References(string generatedCSharp, IReadOnlySet<string> owned)
    {
        ArgumentNullException.ThrowIfNull(generatedCSharp);
        ArgumentNullException.ThrowIfNull(owned);
        if (owned.Count == 0)
            return false;

        var start = -1;
        for (var i = 0; i <= generatedCSharp.Length; i++)
        {
            var isTokenChar = i < generatedCSharp.Length
                && (char.IsLetterOrDigit(generatedCSharp[i]) || generatedCSharp[i] == '_');
            if (isTokenChar)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0)
            {
                if (owned.Contains(generatedCSharp[start..i]))
                    return true;
                start = -1;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits the candidate outputs into the ones that may be validated and the
    /// source paths whose outputs are cascade-suppressed. Suppressed paths are
    /// full paths, compared with the platform's path comparer.
    /// </summary>
    internal static List<GeneratedCSharpSource> Retain(
        IEnumerable<GeneratedCSharpSource> candidates,
        IReadOnlySet<string> owned,
        out HashSet<string> suppressedSourcePaths)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var retained = new List<GeneratedCSharpSource>();
        suppressedSourcePaths = new HashSet<string>(BuildStateCache.GetPathComparer());

        foreach (var candidate in candidates)
        {
            if (References(candidate.Text, owned))
            {
                if (!string.IsNullOrEmpty(candidate.SourcePath))
                    suppressedSourcePaths.Add(Path.GetFullPath(candidate.SourcePath));
                continue;
            }

            retained.Add(candidate);
        }

        return retained;
    }
}

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

    private sealed record PendingFile(
        FileInfo File,
        CompilationResult Result,
        CompilationOptions Options,
        string? RelativeKey,
        BuildStateCache.FileStat StatBeforeRead,
        byte[] SourceBytes,
        EffectSummary? EffectSummary);

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
        Action<FileInfo, string, ModuleNode>? onAst = null,
        Action<FileInfo>? onFailed = null)
    {
        var compiled = new List<FileResult>();
        var pending = new List<PendingFile>();
        var skipped = new List<FileInfo>();
        var skippedGeneratedSources = new List<GeneratedCSharpSource>();
        // Modules of files that failed this run; null for a file that did not
        // parse. Scopes generated-output validation (GeneratedValidationScope).
        var failedModules = new List<ModuleNode?>();
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

        // --- Cross-module call qualification map (G3/#809): a light pre-parse of
        // every input collects each module's public function names so the emitter
        // can qualify bare-name cross-module calls (they otherwise emit verbatim
        // and fail csc with CS0103). Unambiguous names only — a name defined in
        // two modules is dropped, matching cross-module effect resolution's
        // skip-ambiguous rule so emission and enforcement agree. Warm-skip
        // validity depends on this map: its hash participates in global
        // invalidation below.
        IReadOnlyDictionary<string, CrossModuleFunctionTarget>? crossModuleMap = null;
        string? crossModuleMapHash = null;
        if (sources.Count > 1)
        {
            crossModuleMap = BuildCrossModuleFunctionMap(sources);
            crossModuleMapHash = ComputeCrossModuleMapHash(crossModuleMap);
        }
        if (newState != null)
        {
            newState.CrossModuleMapHash = crossModuleMapHash;
        }
        // Compared UNCONDITIONALLY (#823 review m1): a single-file rebuild of a
        // formerly-multi project (hash → null) must also invalidate — its cached
        // output may carry qualification against modules no longer in the build.
        if (priorFiles != null && priorState?.CrossModuleMapHash != crossModuleMapHash)
        {
            // Map changed (module added/removed/renamed or ambiguity introduced):
            // every cached output may carry stale qualification — full re-emit.
            priorFiles = null;
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
                        skippedGeneratedSources.Add(new GeneratedCSharpSource(
                            File.ReadAllText(outputPath),
                            outputPath,
                            file.FullName));
                        moduleSummaries.Add((cachedEntry.EffectSummary, file.FullName));
                        onSkipped?.Invoke(file, outputPath);
                        continue;
                    }
                }
            }

            var options = optionsFactory(file);
            options.CrossModuleFunctionModules = crossModuleMap;
            options.DeferGeneratedOutputValidation = true;
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
                Program.TrackCompilationOutcome(
                    Calor.Compiler.Telemetry.CalorTelemetry.IsInitialized
                        ? Calor.Compiler.Telemetry.CalorTelemetry.Instance
                        : null,
                    result.Diagnostics,
                    validated: false);
                onFailed?.Invoke(file);
                anyErrors = true;
                // Its module (null when the file did not parse) scopes which
                // other outputs may be validated — see GeneratedValidationScope.
                failedModules.Add(result.Ast);
                continue;
            }

            EffectSummary? summary = null;
            if (result.Ast != null && (crossModuleEnforcement || newState != null))
            {
                summary = EffectSummaryBuilder.Build(result.Ast);
                moduleSummaries.Add((summary, file.FullName));
            }

            pending.Add(new PendingFile(
                file, result, options, relativeKey, statBeforeRead, sourceBytes, summary));
        }

        var generatedValidationFailed = false;
        // Generated-output validation runs over the outputs of THIS run's clean
        // files plus every cached output, as one Roslyn compilation. A file that
        // failed above contributes no output, so its CALLERS fail against the
        // hole with cascade Calor1002s that are not theirs — and cold and warm
        // disagreed, because a warm build whose only uncached file was the
        // failing one had nothing pending and skipped validation altogether
        // (found by ES-08, tests/TestData/EditScripts).
        //
        // The rule (GeneratedValidationScope, shared with the MSBuild task):
        // validate every output EXCEPT the ones that reference a failed file's
        // module. A genuine Calor1002 in an unrelated file still surfaces in the
        // same run. Suppressed outputs are unvalidated, so they are neither
        // published nor cached below.
        var owned = GeneratedValidationScope.OwnedIdentifiers(
            failedModules, out var validationScopeIsComplete);
        var cascadeSuppressed = new HashSet<string>(BuildStateCache.GetPathComparer());
        if (!validationScopeIsComplete)
        {
            // A file that did not parse hides what it owns, so nothing in this
            // run can be validated — and nothing may CLAIM to have been. Marking
            // every pending file suppressed is what makes that true: without it
            // the run published and cached unvalidated output and reported
            // success, which is the permissive answer, not the conservative one
            // (review round 3, C1-residual). An unterminated string or a stray
            // marker is what reaches here — a plain syntax error keeps its AST
            // and takes the scoped path below.
            //
            // `--transpile-only` is exempt, exactly as it is on the scoped path
            // and in the MSBuild task: it opts out of the validation this
            // suppression protects, so withholding its output would remove the
            // artifact the flag exists to produce (review round 4, V2 — the two
            // surfaces disagreed here, which is the class M1 closed).
            foreach (var item in pending)
            {
                if (!item.Options.UnsafeTranspileOnly)
                    cascadeSuppressed.Add(Path.GetFullPath(item.File.FullName));
            }
        }
        else if (pending.Any(item => !item.Options.UnsafeTranspileOnly)
            || skippedGeneratedSources.Count > 0)
        {
            // Cached outputs count: a warm run with nothing pending still has a
            // generated set worth validating.
            var generatedSources = GeneratedValidationScope.Retain(
                pending
                    .Where(item => !item.Options.UnsafeTranspileOnly)
                    .Select(item => new GeneratedCSharpSource(
                        item.Result.GeneratedCode,
                        cache?.OutputPathFor(item.File)
                            ?? Path.ChangeExtension(item.File.FullName, ".g.cs"),
                        item.File.FullName))
                    .Concat(skippedGeneratedSources),
                owned,
                out cascadeSuppressed);
            var references = pending
                .SelectMany(item => item.Options.ReferencedAssemblyPaths ?? [])
                .Distinct(BuildStateCache.GetPathComparer());
            var validation = generatedSources.Count > 0
                ? GeneratedCSharpCompiler.Validate(generatedSources, references)
                : null;
            if (validation != null && !validation.CompilationSuccess)
            {
                generatedValidationFailed = true;
                anyErrors = true;
                var validationDiagnostics = new DiagnosticBag();
                // Fallback file for a diagnostic with no source location. Pending
                // first; on a warm run validating only cached outputs there is no
                // pending file, so the generated set's own first entry names it.
                var fallbackPath = pending.Count > 0
                    ? pending[0].File.FullName
                    : generatedSources[0].SourcePath ?? generatedSources[0].Path;
                Program.AddGeneratedOutputDiagnostics(
                    validation, validationDiagnostics, fallbackPath);
                foreach (var diagnostic in validationDiagnostics)
                {
                    var owner = pending.FirstOrDefault(item =>
                        BuildStateCache.GetPathComparer().Equals(
                            Path.GetFullPath(item.File.FullName),
                            Path.GetFullPath(diagnostic.FilePath ?? item.File.FullName)));
                    owner ??= pending.Count > 0 ? pending[0] : null;
                    owner?.Result.Diagnostics.Add(diagnostic);
                    if (diagnosticSink != null)
                    {
                        diagnosticSink.Add(diagnostic);
                    }
                    else
                    {
                        Console.Error.WriteLine(diagnostic);
                    }
                }
                if (Calor.Compiler.Telemetry.CalorTelemetry.IsInitialized)
                {
                    Program.TrackDiagnostics(
                        Calor.Compiler.Telemetry.CalorTelemetry.Instance,
                        validationDiagnostics);
                }
            }
        }

        // Cross-module effect enforcement over successfully compiled modules —
        // runs even when other files failed, so all reportable violations surface
        // in one pass (top-level compile semantics). Skipped files participate
        // through their cache-restored summaries.
        var crossModuleDiagnostics = new DiagnosticBag();
        if (crossModuleEnforcement && moduleSummaries.Count > 1)
        {
            var registry = CrossModuleEffectRegistry.Build(moduleSummaries);
            foreach (var diagnostic in registry.BuildDiagnostics)
            {
                crossModuleDiagnostics.Add(diagnostic);
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
                crossModuleDiagnostics.Add(diagnostic);
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

            Program.TrackDiagnostics(
                Calor.Compiler.Telemetry.CalorTelemetry.IsInitialized
                    ? Calor.Compiler.Telemetry.CalorTelemetry.Instance
                    : null,
                crossModuleDiagnostics);
        }

        var publishFailed = generatedValidationFailed || crossModuleDiagnostics.HasErrors;
        var telemetry = Calor.Compiler.Telemetry.CalorTelemetry.IsInitialized
            ? Calor.Compiler.Telemetry.CalorTelemetry.Instance
            : null;
        foreach (var item in pending)
        {
            var outcomeDiagnostics = new DiagnosticBag();
            outcomeDiagnostics.AddRange(item.Result.Diagnostics);
            outcomeDiagnostics.AddRange(crossModuleDiagnostics);
            Program.TrackCompilationOutcome(
                telemetry,
                outcomeDiagnostics,
                validated: !item.Options.UnsafeTranspileOnly &&
                    !generatedValidationFailed &&
                    !crossModuleDiagnostics.HasErrors &&
                    // Cascade-suppressed: nothing validated this output, so no
                    // validated-success claim may be made for it.
                    !cascadeSuppressed.Contains(Path.GetFullPath(item.File.FullName)));
        }

        if (!publishFailed)
        {
            foreach (var item in pending)
            {
                // A cascade-suppressed output was never validated (its module
                // calls a file that failed), so it is not published, not cached
                // and not reported successful — the same treatment a file whose
                // validation failed gets, for the same reason: nothing checked
                // it. The next run validates it.
                if (cascadeSuppressed.Contains(Path.GetFullPath(item.File.FullName)))
                {
                    onFailed?.Invoke(item.File);
                    continue;
                }

                compiled.Add(new FileResult(item.File, item.Result));
                onCompiled?.Invoke(item.File, item.Result);

                // Only diagnostic-clean files are cached: a skipped file emits nothing,
                // so caching a file with warnings/info would silently drop those
                // diagnostics from warm builds.
                if (newState != null && item.RelativeKey != null && cache != null
                    && !item.Options.UnsafeTranspileOnly
                    && item.Result.Diagnostics.Count == 0)
                {
                    var entry = BuildStateCache.CreateFileEntry(
                        item.StatBeforeRead, item.SourceBytes);
                    entry.EffectSummary = item.EffectSummary;
                    entry.OutputContentHash = BuildStateCache.ComputeContentHash(
                        System.Text.Encoding.UTF8.GetBytes(item.Result.GeneratedCode));
                    newState.Files[item.RelativeKey] = entry;
                }
            }
        }
        else
        {
            foreach (var item in pending)
                onFailed?.Invoke(item.File);
        }

        if (newState != null && cache != null && !publishFailed)
        {
            BuildStateCache.Save(newState, cache.StateDirectory);
        }

        return new DriverResult(compiled, anyErrors, skipped);
    }

    /// <summary>
    /// Pre-parses every input for its module name and public function names,
    /// producing the call-target → emitted namespace/static-class map used for
    /// cross-module call qualification (G3/#809). Files that fail to parse
    /// contribute nothing (they fail properly in the main compile loop);
    /// ambiguous names are dropped entirely.
    /// </summary>
    internal static IReadOnlyDictionary<string, CrossModuleFunctionTarget> BuildCrossModuleFunctionMap(
        IReadOnlyList<FileInfo> sources)
    {
        var modules = new List<ModuleNode>();
        foreach (var file in sources)
        {
            try
            {
                var text = File.ReadAllText(file.FullName);
                var diagnostics = new Diagnostics.DiagnosticBag();
                var lexer = new Parsing.Lexer(text, diagnostics);
                var parser = new Parsing.Parser(lexer.TokenizeAllForParser(), diagnostics);
                var module = parser.Parse();
                if (module == null)
                {
                    continue;
                }
                modules.Add(module);
            }
            catch (IOException)
            {
            }
        }

        return BuildCrossModuleFunctionMap(modules);
    }

    /// <summary>
    /// Builds the production cross-module call-qualification map from already
    /// parsed modules. Editor validation uses this overload so it shares the
    /// driver's exact visibility and ambiguity rules without reparsing snapshots.
    /// </summary>
    internal static IReadOnlyDictionary<string, CrossModuleFunctionTarget> BuildCrossModuleFunctionMap(
        IReadOnlyList<ModuleNode> modules)
        => BuildFunctionTargetMap(
            modules,
            function => function.Visibility is Ast.Visibility.Public or Ast.Visibility.Internal);

    /// <summary>
    /// Builds the emitted target map for every function declared by one
    /// <see cref="ModuleNode"/>, including private functions. Namespace scopes
    /// split one Calor module into multiple generated static classes, but they
    /// do not split the module's function visibility or binding scope.
    /// </summary>
    internal static IReadOnlyDictionary<string, CrossModuleFunctionTarget>
        BuildIntraModuleFunctionMap(ModuleNode module)
        => BuildFunctionTargetMap([module], _ => true);

    private static IReadOnlyDictionary<string, CrossModuleFunctionTarget>
        BuildFunctionTargetMap(
            IReadOnlyList<ModuleNode> modules,
            Func<FunctionNode, bool> includeFunction)
    {
        var byName = new Dictionary<string, List<CrossModuleFunctionTarget>>(
            StringComparer.Ordinal);
        var byQualifiedName =
            new Dictionary<string, List<CrossModuleFunctionTarget>>(
                StringComparer.Ordinal);
        foreach (var module in modules)
        {
            foreach (var fn in module.Functions)
            {
                if (!includeFunction(fn))
                    continue;

                var target = CrossModuleFunctionTarget.Create(module, fn);
                if (target == null)
                    continue;

                var functionName = GetFunctionLookupName(fn.Name);
                if (!byName.TryGetValue(functionName, out var definingTargets))
                {
                    definingTargets = [];
                    byName[functionName] = definingTargets;
                }
                if (!definingTargets.Contains(target))
                    definingTargets.Add(target);

                AddQualifiedTarget(module.Name);
                AddQualifiedTarget(target.NamespaceIdentity);

                void AddQualifiedTarget(string qualifier)
                {
                    if (string.IsNullOrEmpty(qualifier))
                        return;

                    var qualifiedName = $"{qualifier}.{functionName}";
                    if (!byQualifiedName.TryGetValue(
                            qualifiedName,
                            out var qualifiedTargets))
                    {
                        qualifiedTargets = [];
                        byQualifiedName[qualifiedName] = qualifiedTargets;
                    }
                    if (!qualifiedTargets.Contains(target))
                        qualifiedTargets.Add(target);
                }
            }
        }

        var map = new Dictionary<string, CrossModuleFunctionTarget>(
            StringComparer.Ordinal);
        foreach (var (name, definingTargets) in byName)
        {
            if (definingTargets.Count == 1)
                map[name] = definingTargets[0];
        }
        foreach (var (qualifiedName, targets) in byQualifiedName)
        {
            if (targets.Count == 1)
                map[qualifiedName] = targets[0];
        }
        return map;
    }

    private static string GetFunctionLookupName(string name)
    {
        var genericStart = name.LastIndexOf('<');
        return genericStart > 0 && name.EndsWith('>')
            ? name[..genericStart]
            : name;
    }

    internal static string ComputeCrossModuleMapHash(
        IReadOnlyDictionary<string, CrossModuleFunctionTarget> map)
    {
        var canonical = string.Join(";", map.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
                $"{kv.Key}={kv.Value.ModuleName}|{kv.Value.NamespaceIdentity}|{kv.Value.ModuleClassName}"));
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
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
