using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Commands;

/// <summary>
/// <c>calor watch &lt;dir|files...&gt;</c> — initial full compile, then recompiles on
/// every <c>*.calr</c> (or effect-manifest) change, debounced, using the incremental
/// build cache so unchanged files are skipped. Compile-only in v1 (no --run).
/// Per-rebuild diagnostics respect <c>--format text|json</c>: json mode is NDJSON —
/// one compact JSON document per line, one line per rebuild, on stdout; status
/// always goes to stderr. Exits cleanly on Ctrl+C / SIGTERM.
/// </summary>
public static class WatchCommand
{
    public static Command Create()
    {
        var pathsArgument = new Argument<string[]>(
            name: "paths",
            description: ".calr files and/or directories to watch (directories are scanned recursively; bin/, obj/ and reference/ subdirectories are excluded)")
        { Arity = ArgumentArity.OneOrMore };

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Diagnostic output format per rebuild: text (stderr) or json (NDJSON on stdout: one compact document per line, one line per rebuild)");
        formatOption.FromAmong("text", "json");

        var verboseOption = new Option<bool>(["--verbose", "-v"], "Enable verbose output");
        var noCacheOption = new Option<bool>(["--no-cache"], "Disable the incremental build cache (every rebuild recompiles all files)");
        var clearCacheOption = new Option<bool>(["--clear-cache"], "Clear .calor-build-state.json before the initial compile");
        var strictApiOption = new Option<bool>(["--strict-api"], "Enable strict API mode");
        var requireDocsOption = new Option<bool>(["--require-docs"], "Require documentation on public functions and types");
        var enforceEffectsOption = new Option<bool>(["--enforce-effects"], () => false, "Enforce effect declarations");
        var strictEffectsOption = new Option<bool>(["--strict-effects"], () => false, "Promote unknown external call warnings (Calor0411) to errors");
        var permissiveEffectsOption = new Option<bool>(["--permissive-effects"], () => false, "Permissive effect mode (unknown calls assumed pure, violations demoted to warnings)");
        var contractModeOption = new Option<string>(["--contract-mode"], () => "debug", "Contract enforcement mode: off, debug, or release");
        var debounceOption = new Option<int>(["--debounce-ms"], () => WatchSession.DefaultDebounceMs, "Quiet period in milliseconds before a change burst triggers a rebuild");
        debounceOption.AddValidator(result =>
        {
            if (result.GetValueOrDefault<int>() <= 0)
            {
                result.ErrorMessage = "Debounce must be a positive integer (milliseconds)";
            }
        });

        var command = new Command("watch", "Watch Calor sources and recompile incrementally on change (compile-only)")
        {
            pathsArgument,
            formatOption,
            verboseOption,
            noCacheOption,
            clearCacheOption,
            strictApiOption,
            requireDocsOption,
            enforceEffectsOption,
            strictEffectsOption,
            permissiveEffectsOption,
            contractModeOption,
            debounceOption
        };

        command.SetHandler(async (InvocationContext ctx) =>
        {
            var settings = new WatchSession.WatchSettings(
                Format: ctx.ParseResult.GetValueForOption(formatOption) ?? "text",
                Verbose: ctx.ParseResult.GetValueForOption(verboseOption),
                NoCache: ctx.ParseResult.GetValueForOption(noCacheOption),
                ClearCache: ctx.ParseResult.GetValueForOption(clearCacheOption),
                StrictApi: ctx.ParseResult.GetValueForOption(strictApiOption),
                RequireDocs: ctx.ParseResult.GetValueForOption(requireDocsOption),
                EnforceEffects: ctx.ParseResult.GetValueForOption(enforceEffectsOption),
                StrictEffects: ctx.ParseResult.GetValueForOption(strictEffectsOption),
                PermissiveEffects: ctx.ParseResult.GetValueForOption(permissiveEffectsOption),
                ContractMode: ctx.ParseResult.GetValueForOption(contractModeOption) ?? "debug",
                DebounceMs: ctx.ParseResult.GetValueForOption(debounceOption));

            var paths = ctx.ParseResult.GetValueForArgument(pathsArgument);
            ctx.ExitCode = await ExecuteAsync(paths, settings);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(string[] paths, WatchSession.WatchSettings settings)
    {
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // we own the shutdown — exit cleanly after the loop unwinds
            cts.Cancel();
        };
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            cts.Cancel();
        });

        var session = new WatchSession(paths, settings, Console.Out, Console.Error);
        return await session.RunAsync(cts.Token);
    }
}

/// <summary>
/// The watch loop, separated from the System.CommandLine surface for testability:
/// tests inject change events through <see cref="InjectChange"/> (bypassing the real
/// <see cref="FileSystemWatcher"/>) and observe rebuilds via <see cref="RebuildCompleted"/>.
/// </summary>
internal sealed class WatchSession
{
    internal const int DefaultDebounceMs = 200;

    internal sealed record WatchSettings(
        string Format,
        bool Verbose,
        bool NoCache,
        bool ClearCache,
        bool StrictApi,
        bool RequireDocs,
        bool EnforceEffects,
        bool StrictEffects,
        bool PermissiveEffects,
        string ContractMode,
        int DebounceMs);

    internal sealed record RebuildResult(int Compiled, int Skipped, bool AnyErrors);

    private readonly IReadOnlyList<string> _paths;
    private readonly WatchSettings _settings;
    private readonly TextWriter _output;
    private readonly TextWriter _status;
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>();
    private readonly bool _structuredOutput;
    private string? _stateDirectory;
    private bool _clearCachePending;
    private int _rebuildCount;

    /// <summary>Raised after every rebuild (including the initial compile). Test hook.</summary>
    internal event Action<RebuildResult>? RebuildCompleted;

    internal WatchSession(IReadOnlyList<string> paths, WatchSettings settings, TextWriter output, TextWriter status)
    {
        _paths = paths;
        _settings = settings;
        _output = output;
        _status = status;
        _structuredOutput = !settings.Format.Equals("text", StringComparison.OrdinalIgnoreCase);
        _clearCachePending = settings.ClearCache;
    }

    /// <summary>Test hook: feeds a change event as if the file-system watcher had raised it.</summary>
    internal void InjectChange(string path) => _changes.Writer.TryWrite(path);

    internal async Task<int> RunAsync(CancellationToken cancellationToken, bool useFileSystemWatchers = true)
    {
        // Validate the watch roots up front — a nonexistent path is a usage error.
        foreach (var path in _paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                _status.WriteLine($"Error: file or directory not found: {path}");
                return 1;
            }
        }

        var watchers = new List<FileSystemWatcher>();
        try
        {
            if (useFileSystemWatchers)
            {
                foreach (var path in _paths)
                {
                    watchers.AddRange(CreateWatchers(path));
                }
            }

            Rebuild(initial: true);

            _status.WriteLine("Watching for changes... (Ctrl+C to exit)");

            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await WatchDebouncer.ReadBatchAsync(
                    _changes.Reader, TimeSpan.FromMilliseconds(_settings.DebounceMs), TimeProvider.System, cancellationToken);
                if (batch == null)
                {
                    break; // cancelled or channel completed — clean shutdown
                }

                _status.WriteLine($"Change detected ({batch.Count} file(s)); rebuilding...");
                Rebuild(initial: false);
            }

            _status.WriteLine("Watch stopped.");
            return 0;
        }
        finally
        {
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }
        }
    }

    private IEnumerable<FileSystemWatcher> CreateWatchers(string path)
    {
        if (Directory.Exists(path))
        {
            // Sources, recursively — plus effect manifests, whose changes feed the
            // manifest hash and must invalidate cached files on the next rebuild.
            yield return CreateWatcher(path, "*.calr", includeSubdirectories: true);
            yield return CreateWatcher(path, "*.calor-effects.json", includeSubdirectories: false);
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory != null)
            {
                yield return CreateWatcher(directory, Path.GetFileName(path), includeSubdirectories: false);
            }
        }
    }

    private FileSystemWatcher CreateWatcher(string directory, string filter, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(directory, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        watcher.Changed += (_, e) => _changes.Writer.TryWrite(e.FullPath);
        watcher.Created += (_, e) => _changes.Writer.TryWrite(e.FullPath);
        watcher.Deleted += (_, e) => _changes.Writer.TryWrite(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            _changes.Writer.TryWrite(e.OldFullPath);
            _changes.Writer.TryWrite(e.FullPath);
        };
        watcher.Error += (_, e) =>
            _status.WriteLine($"Warning: file watcher error: {e.GetException().Message}");
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    /// <summary>
    /// Re-resolves the current source set (created/deleted files are picked up here)
    /// and compiles it through <see cref="CompilationDriver"/> with the incremental
    /// cache, emitting one diagnostics document per rebuild in json mode.
    /// </summary>
    private void Rebuild(bool initial)
    {
        var rebuildNumber = ++_rebuildCount;
        var sources = ResolveSources();
        if (sources.Count == 0)
        {
            _status.WriteLine("No .calr files found; waiting for changes...");
            RebuildCompleted?.Invoke(new RebuildResult(0, 0, AnyErrors: false));
            return;
        }

        var diagnosticSink = _structuredOutput ? new DiagnosticBag() : null;
        // Per-rebuild span→declaration-ID resolver (envelope schema v1.1): fed
        // from each freshly compiled file's AST so the rebuild's diagnostics
        // carry declarationId. Cache-skipped files contribute no diagnostics,
        // so they need no resolver entries.
        var declarationIds = _structuredOutput ? new Ids.DeclarationIdResolver() : null;
        var policy = _settings.PermissiveEffects ? UnknownCallPolicy.Permissive : UnknownCallPolicy.Strict;

        CompilationDriver.DriverCacheSettings? cache = null;
        if (!_settings.NoCache)
        {
            // The state directory is pinned on the first rebuild (the common ancestor
            // of the watch roots) so cache keys stay stable as files come and go.
            _stateDirectory ??= BuildStateCache.ComputeCommonDirectoryOfDirs(
                _paths.Select(p => Directory.Exists(p)
                    ? Path.GetFullPath(p)
                    : Path.GetDirectoryName(Path.GetFullPath(p))!).ToList());
            cache = new CompilationDriver.DriverCacheSettings(
                _stateDirectory,
                Program.BuildOptionsToken(_settings.StrictApi, _settings.RequireDocs,
                    _settings.EnforceEffects, _settings.StrictEffects, _settings.PermissiveEffects,
                    _settings.ContractMode, verify: false, verificationTimeout: 0, analyze: false,
                    allFindings: false, strictBindInference: true, experimentalFlags: null),
                ClearFirst: _clearCachePending,
                OutputPathFor: file => Path.ChangeExtension(file.FullName, ".g.cs"));
            _clearCachePending = false;
        }

        DriverResultSummary summary;
        try
        {
            var result = CompilationDriver.CompileAll(
                sources,
                file => new CompilationOptions
                {
                    Verbose = _settings.Verbose,
                    StatusWriter = _status,
                    StrictApi = _settings.StrictApi,
                    RequireDocs = _settings.RequireDocs,
                    EnforceEffects = _settings.EnforceEffects,
                    StrictEffects = _settings.StrictEffects,
                    UnknownCallPolicy = policy,
                    ContractMode = CompilationDriver.ParseContractMode(_settings.ContractMode),
                    ProjectDirectory = Path.GetDirectoryName(file.FullName)
                },
                crossModuleEnforcement: true,
                crossModulePolicy: policy,
                onCompiled: (file, compileResult) =>
                {
                    var outputPath = Path.ChangeExtension(file.FullName, ".g.cs");
                    File.WriteAllText(outputPath, compileResult.GeneratedCode);
                    if (_settings.Verbose)
                    {
                        _status.WriteLine($"Compiled: {outputPath}");
                    }
                },
                diagnosticSink: diagnosticSink,
                cache: cache,
                onSkipped: (_, outputPath) =>
                {
                    if (_settings.Verbose)
                    {
                        _status.WriteLine($"Up-to-date (cached): {outputPath}");
                    }
                },
                onAst: declarationIds != null
                    ? (file, source, ast) => declarationIds.AddFile(file.FullName, source, ast)
                    : null);

            summary = new DriverResultSummary(result.Compiled.Count, result.Skipped.Count, result.AnyErrors);
        }
        catch (Exception ex)
        {
            diagnosticSink?.Add(new Diagnostic(
                DiagnosticCode.CliInternalError,
                $"Unhandled error: {ex.Message}",
                new TextSpan(0, 0, 1, 1),
                DiagnosticSeverity.Error));
            _status.WriteLine($"Error: {ex.Message}");
            summary = new DriverResultSummary(0, 0, AnyErrors: true);
        }

        // NDJSON: exactly one *compact* JSON document per line, one line per
        // rebuild — even for crashed or clean rebuilds. Consumers split the
        // stream on newlines and parse each line independently; a pretty-printed
        // document here would make the concatenated stream unsplittable.
        if (diagnosticSink != null)
        {
            _output.WriteLine(new JsonDiagnosticFormatter(writeIndented: false) { DeclarationIds = declarationIds }.Format(diagnosticSink));
            _output.Flush();
        }

        var label = initial ? "Initial compile" : $"Rebuild #{rebuildNumber - 1}";
        _status.WriteLine(
            $"{label}: {summary.Compiled} compiled, {summary.Skipped} up-to-date" +
            (summary.AnyErrors ? " — FAILED" : " — ok"));

        RebuildCompleted?.Invoke(new RebuildResult(summary.Compiled, summary.Skipped, summary.AnyErrors));
    }

    private readonly record struct DriverResultSummary(int Compiled, int Skipped, bool AnyErrors);

    /// <summary>
    /// Current watched source set: directory roots are re-enumerated every rebuild
    /// (so created/deleted files are picked up); explicit file roots are included
    /// while they exist. Duplicates across overlapping roots are removed.
    /// </summary>
    private List<FileInfo> ResolveSources()
    {
        var seen = new HashSet<string>(BuildStateCache.GetPathComparer());
        var sources = new List<FileInfo>();

        foreach (var path in _paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.calr", SearchOption.AllDirectories)
                             .OrderBy(f => f, StringComparer.Ordinal))
                {
                    if (ExecutionWorkspace.IsInExcludedDirectory(path, file))
                    {
                        continue;
                    }
                    var full = Path.GetFullPath(file);
                    if (seen.Add(full))
                    {
                        sources.Add(new FileInfo(full));
                    }
                }
            }
            else if (File.Exists(path) && path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full))
                {
                    sources.Add(new FileInfo(full));
                }
            }
        }

        return sources;
    }
}
