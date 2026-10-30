using System.CommandLine;
using System.Text;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Formatting;

namespace Calor.Compiler.Commands;

/// <summary>
/// <c>calor fix</c> applies safe diagnostic patches, or explicit reversible
/// source migrations (which can record a <c>migration.log.json</c>).
///
/// Subcommands:
/// <list type="bullet">
///   <item><description><c>--elide-call-closers</c>: elides <c>§/C</c> on
///   zero-arg and one-arg same-line calls.</description></item>
///   <item><description><c>--heal-closers</c>: deletes legacy structural
///   closing tags (<c>§/F</c>, <c>§/M</c>, <c>§/L</c>, …) to rewrite a file
///   into canonical indent-only form. Safe against closer text embedded in
///   string literals or comments (see
///   <see cref="Analysis.LegacyCloserFormLint.ScanForHeal"/>).</description></item>
/// </list>
/// </summary>
public static class FixCommand
{
    public static Command Create()
    {
        var rootArgument = new Argument<string>(
            name: "root",
            getDefaultValue: () => ".",
            description: "Calor file or root directory (default: current directory)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var applyOption = new Option<bool>("--apply", "Apply deterministic safe diagnostic patches");
        var elideCallClosersOption = new Option<bool>(
            aliases: ["--elide-call-closers"],
            description: "Elide §/C on zero-arg + one-arg same-line calls (v0.6.x)");

        var healClosersOption = new Option<bool>(
            aliases: ["--heal-closers"],
            description: "Delete legacy structural closers (§/F, §/M, …) -> indent-only form");

        var revertOption = new Option<bool>(
            aliases: ["--revert"],
            description: "Reverse the operation using --log");

        var logOption = new Option<FileInfo?>(
            aliases: ["--log"],
            description: "Path to write (or read, with --revert) migration.log.json");

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run", "-n"],
            description: "Report what would change without writing files");

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Output format: text or json (envelope v1.1)");

        var command = new Command("fix", "Apply safe diagnostic patches or reversible source migrations")
        {
            rootArgument,
            applyOption,
            elideCallClosersOption,
            healClosersOption,
            revertOption,
            logOption,
            dryRunOption,
            formatOption,
        };

        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext context) =>
        {
            var parse = context.ParseResult;
            if (parse.GetValueForOption(applyOption))
            {
                var json = string.Equals(parse.GetValueForOption(formatOption), "json", StringComparison.OrdinalIgnoreCase);
                if (parse.GetValueForOption(elideCallClosersOption) || parse.GetValueForOption(healClosersOption)
                    || parse.GetValueForOption(revertOption) || parse.GetValueForOption(logOption) != null)
                {
                    const string message = "--apply cannot be combined with migration options (--elide-call-closers, --heal-closers, --revert, --log)";
                    EmitErrorEnvelope(json, DiagnosticCode.CliUsageError, message, null);
                    Console.Error.WriteLine(message);
                    context.ExitCode = 2;
                    return;
                }
                context.ExitCode = await ApplyDiagnosticsAsync(
                    parse.GetValueForArgument(rootArgument),
                    parse.GetValueForOption(dryRunOption), json);
                return;
            }
            context.ExitCode = await ExecuteAsync(
                new DirectoryInfo(parse.GetValueForArgument(rootArgument)),
                parse.GetValueForOption(elideCallClosersOption),
                parse.GetValueForOption(healClosersOption),
                parse.GetValueForOption(revertOption),
                parse.GetValueForOption(logOption),
                parse.GetValueForOption(dryRunOption),
                string.Equals(parse.GetValueForOption(formatOption), "json", StringComparison.OrdinalIgnoreCase));
        });

        return command;
    }

    /// <summary>
    /// Envelope mode (--format json): stdout carries exactly one document,
    /// always — including early-exit error paths, which get a CLI-band
    /// diagnostic (Calor1310 missing input, Calor1311 usage error).
    /// </summary>
    private static void EmitErrorEnvelope(bool json, string code, string message, string? filePath)
    {
        if (!json)
        {
            return;
        }
        Console.WriteLine(EnvelopeWriter.Serialize("fix", null,
            [new Diagnostic(code, DiagnosticSeverity.Error, message, filePath, line: 1, column: 1)]));
    }

    private static async Task<int> ExecuteAsync(
        DirectoryInfo root,
        bool elideCallClosers,
        bool healClosers,
        bool revert,
        FileInfo? log,
        bool dryRun,
        bool json)
    {
        if (!root.Exists)
        {
            EmitErrorEnvelope(json, DiagnosticCode.CliInputNotFound,
                $"root directory not found: {root.FullName}", root.FullName);
            Console.Error.WriteLine($"root directory not found: {root.FullName}");
            return 2;
        }
        var selectedCount = (elideCallClosers ? 1 : 0) + (healClosers ? 1 : 0);
        if (selectedCount == 0)
        {
            EmitErrorEnvelope(json, DiagnosticCode.CliUsageError,
                "specify --apply, --elide-call-closers or --heal-closers", filePath: null);
            Console.Error.WriteLine("specify --apply, --elide-call-closers or --heal-closers");
            return 2;
        }
        if (selectedCount > 1)
        {
            EmitErrorEnvelope(json, DiagnosticCode.CliUsageError,
                "--elide-call-closers and --heal-closers are mutually exclusive; run them separately", filePath: null);
            Console.Error.WriteLine("--elide-call-closers and --heal-closers are mutually exclusive; run them separately");
            return 2;
        }

        if (elideCallClosers)
        {
            return revert
                ? await RevertElideCallClosersAsync(root, log, dryRun, json)
                : await ElideCallClosersAsync(root, log, dryRun, json);
        }
        return revert
            ? await RevertHealClosersAsync(root, log, dryRun, json)
            : await HealClosersAsync(root, log, dryRun, json);
    }

    private static async Task<int> ApplyDiagnosticsAsync(string target, bool dryRun, bool json)
    {
        var diagnostics = new DiagnosticBag();
        var changedFiles = 0;
        var editsApplied = 0;
        try
        {
            var fullPath = Path.GetFullPath(target);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCode.CliInputNotFound, DiagnosticSeverity.Error,
                    "Input path not found", fullPath, 1, 1));
            }
            else
            {
                var paths = File.Exists(fullPath) ? [fullPath]
                    : EnumerateCalrFiles(new DirectoryInfo(fullPath)).Order(StringComparer.Ordinal).ToArray();
                if (paths.Length == 0 || paths.Any(path => !path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Expected a .calr file or a directory containing .calr files.");

                foreach (var path in paths)
                {
                    var snapshot = await SourceFileSnapshot.ReadAsync(path);
                    var fixedSource = DiagnosticFixes.Repair(snapshot.Text, snapshot.Path,
                        new CompilationOptions(), out var result, out var applied);
                    if (fixedSource != snapshot.Text)
                    {
                        if (!dryRun)
                        {
                            await SafeSourceFile.WriteParsedAsync(snapshot, fixedSource, persisted =>
                            {
                                if (!string.Equals(persisted, fixedSource, StringComparison.Ordinal))
                                    throw new InvalidDataException("Persisted source does not match the validated patch.");
                            });
                        }
                        changedFiles++;
                        editsApplied += applied;
                    }
                    // A preview describes the unchanged disk, not a fictional successful repair.
                    if (dryRun)
                        result = Program.Compile(snapshot.Text, snapshot.Path);
                    diagnostics.AddRange(result.Diagnostics);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            diagnostics.Add(new Diagnostic(DiagnosticCode.CliInternalError, DiagnosticSeverity.Error,
                $"Fix failed: {ex.Message}", target, 1, 1));
        }

        if (json)
            Console.WriteLine(EnvelopeWriter.Serialize("fix",
                new { operation = "apply", dryRun, changedFiles, editsApplied }, diagnostics));
        else
        {
            foreach (var diagnostic in diagnostics)
                Console.Error.WriteLine(diagnostic);
            Console.WriteLine($"fix: {(dryRun ? "[dry-run] " : "")}files_changed={changedFiles} edits={editsApplied} errors={diagnostics.Errors.Count} warnings={diagnostics.Warnings.Count}");
        }
        return diagnostics.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Emits the operation summary: envelope v1.1 in json mode (data mirrors the
    /// migration log at file granularity), the existing one-line text otherwise.
    /// The log file itself is written by the caller and is unchanged.
    /// </summary>
    private static void EmitSummary(
        bool json,
        string operation,
        bool dryRun,
        int fixedFiles,
        int totalFixes,
        IEnumerable<string> fixedFileNames,
        FileInfo? logFile,
        string textLine,
        int? filesSkipped = null)
    {
        if (!json)
        {
            Console.WriteLine(textLine);
            if (logFile != null)
            {
                Console.WriteLine($"wrote {logFile.FullName}");
            }
            return;
        }

        var fixes = fixedFileNames
            .GroupBy(f => f, StringComparer.Ordinal)
            .Select(g => new { file = g.Key, count = g.Count() })
            .OrderBy(f => f.file, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine(EnvelopeWriter.Serialize("fix", new
        {
            operation,
            dryRun,
            fixedFiles,
            totalFixes,
            filesSkipped,
            fixes,
            log = logFile?.FullName
        }));
    }

    private static void EmitRevertSummary(bool json, string operation, bool dryRun, int filesRestored)
    {
        if (!json)
        {
            Console.WriteLine($"revert: {(dryRun ? "[dry-run] " : "")}files_restored={filesRestored}");
            return;
        }

        Console.WriteLine(EnvelopeWriter.Serialize("fix", new
        {
            operation,
            revert = true,
            dryRun,
            restoredFiles = filesRestored
        }));
    }

    private static async Task<int> ElideCallClosersAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun, bool json)
    {
        var elider = new CallCloserElider();
        var migrationLog = new BytePreservationVerifier.MigrationLog();
        int filesChanged = 0;
        int totalRemovals = 0;
        int filesSkipped = 0;

        foreach (var path in EnumerateCalrFiles(root))
        {
            var rel = MakeRelativePosix(root, path);
            var original = await File.ReadAllTextAsync(path, Encoding.UTF8);
            var result = elider.Process(original, rel);
            if (result.Skipped)
            {
                filesSkipped++;
                Console.Error.WriteLine($"skip {rel}: {result.SkipReason}");
                continue;
            }
            if (result.Removals.Count == 0)
            {
                continue;
            }
            migrationLog.Entries.AddRange(result.Removals);
            totalRemovals += result.CallsElided;
            filesChanged++;

            if (!dryRun)
            {
                await File.WriteAllTextAsync(path, result.MigratedSource, new UTF8Encoding(false));
            }
        }

        if (logFile != null)
        {
            var logJson = CallCloserElider.SerializeLog(migrationLog);
            await File.WriteAllTextAsync(logFile.FullName, logJson, new UTF8Encoding(false));
        }

        EmitSummary(json, "elide-call-closers", dryRun, filesChanged, totalRemovals,
            migrationLog.Entries.Select(e => e.File), logFile,
            $"elide-call-closers: {(dryRun ? "[dry-run] " : "")}files_changed={filesChanged} elisions={totalRemovals} files_skipped={filesSkipped}",
            filesSkipped);

        return 0;
    }

    private static async Task<int> RevertElideCallClosersAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun, bool json)
    {
        if (logFile == null || !logFile.Exists)
        {
            EmitErrorEnvelope(json, DiagnosticCode.CliUsageError,
                "--revert requires --log <existing migration.log.json>", logFile?.FullName);
            Console.Error.WriteLine("--revert requires --log <existing migration.log.json>");
            return 2;
        }
        var logJson = await File.ReadAllTextAsync(logFile.FullName, Encoding.UTF8);
        var migrationLog = CallCloserElider.DeserializeLog(logJson);

        int filesRestored = 0;
        foreach (var group in migrationLog.Entries.GroupBy(e => e.File))
        {
            var migPath = Path.Combine(root.FullName, group.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(migPath))
            {
                Console.Error.WriteLine($"skip missing file: {migPath}");
                continue;
            }
            var migrated = await File.ReadAllBytesAsync(migPath);
            var restored = ReinsertRemovals(migrated, group);
            if (!dryRun)
            {
                await File.WriteAllBytesAsync(migPath, restored);
            }
            filesRestored++;
        }
        EmitRevertSummary(json, "elide-call-closers", dryRun, filesRestored);
        return 0;
    }

    private static async Task<int> HealClosersAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun, bool json)
    {
        var migrator = new CloserHealMigrator();
        var migrationLog = new BytePreservationVerifier.MigrationLog();
        int filesChanged = 0;
        int totalRemovals = 0;

        foreach (var path in EnumerateCalrFiles(root))
        {
            var rel = MakeRelativePosix(root, path);
            var original = await File.ReadAllTextAsync(path, Encoding.UTF8);
            var (migrated, removals) = migrator.Process(original, rel);
            if (removals.Count == 0)
            {
                continue;
            }
            migrationLog.Entries.AddRange(removals);
            totalRemovals += removals.Count;
            filesChanged++;

            if (!dryRun)
            {
                await File.WriteAllTextAsync(path, migrated, new UTF8Encoding(false));
            }
        }

        if (logFile != null)
        {
            var logJson = CloserHealMigrator.SerializeLog(migrationLog);
            await File.WriteAllTextAsync(logFile.FullName, logJson, new UTF8Encoding(false));
        }

        EmitSummary(json, "heal-closers", dryRun, filesChanged, totalRemovals,
            migrationLog.Entries.Select(e => e.File), logFile,
            $"heal-closers: {(dryRun ? "[dry-run] " : "")}files_changed={filesChanged} removals={totalRemovals}");

        return 0;
    }

    private static async Task<int> RevertHealClosersAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun, bool json)
    {
        if (logFile == null || !logFile.Exists)
        {
            EmitErrorEnvelope(json, DiagnosticCode.CliUsageError,
                "--revert requires --log <existing migration.log.json>", logFile?.FullName);
            Console.Error.WriteLine("--revert requires --log <existing migration.log.json>");
            return 2;
        }
        var logJson = await File.ReadAllTextAsync(logFile.FullName, Encoding.UTF8);
        var migrationLog = CloserHealMigrator.DeserializeLog(logJson);

        int filesRestored = 0;
        foreach (var group in migrationLog.Entries.GroupBy(e => e.File))
        {
            var migPath = Path.Combine(root.FullName, group.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(migPath))
            {
                Console.Error.WriteLine($"skip missing file: {migPath}");
                continue;
            }
            var migrated = await File.ReadAllBytesAsync(migPath);
            var restored = ReinsertRemovals(migrated, group);
            if (!dryRun)
            {
                await File.WriteAllBytesAsync(migPath, restored);
            }
            filesRestored++;
        }
        EmitRevertSummary(json, "heal-closers", dryRun, filesRestored);
        return 0;
    }

    private static byte[] ReinsertRemovals(
        ReadOnlySpan<byte> migrated,
        IEnumerable<BytePreservationVerifier.LogEntry> entries)
    {
        var sorted = entries.OrderBy(e => e.RemovedOffset).ToList();
        int totalLen = migrated.Length + sorted.Sum(e => e.RemovedLength);
        var result = new byte[totalLen];

        int srcIdx = 0;
        int dstIdx = 0;
        foreach (var entry in sorted)
        {
            int preLen = entry.RemovedOffset - dstIdx;
            if (preLen > 0)
            {
                migrated.Slice(srcIdx, preLen).CopyTo(result.AsSpan(dstIdx, preLen));
                srcIdx += preLen;
                dstIdx += preLen;
            }
            var removed = Convert.FromBase64String(entry.RemovedBytesBase64);
            removed.CopyTo(result.AsSpan(dstIdx));
            dstIdx += removed.Length;
        }
        int tail = migrated.Length - srcIdx;
        if (tail > 0)
        {
            migrated.Slice(srcIdx, tail).CopyTo(result.AsSpan(dstIdx, tail));
        }
        return result;
    }

    private static IEnumerable<string> EnumerateCalrFiles(DirectoryInfo root)
    {
        return Directory.EnumerateFiles(root.FullName, "*.calr", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string MakeRelativePosix(DirectoryInfo root, string fullPath)
    {
        var rel = Path.GetRelativePath(root.FullName, fullPath);
        return rel.Replace('\\', '/');
    }
}
