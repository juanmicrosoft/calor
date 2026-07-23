using System.CommandLine;
using System.Text;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Commands;

/// <summary>
/// <c>calor fix</c> — bulk source rewrites that are mechanically safe
/// and reversible (each operation records a <c>migration.log.json</c>).
///
/// Subcommands:
/// <list type="bullet">
///   <item><description><c>--drop-structural-ids</c>: strips the leading
///   <c>{id…}</c> blocks from structural closing tags
///   (<c>§/M{id}</c> → <c>§/M</c>, etc.). Closing-tag IDs are optional
///   since v0.5.x — the lexer accepts both forms.</description></item>
///   <item><description><c>--compact-ids</c>: rewrites legacy
///   26-character Crockford-uppercase ULID payloads to the v6 12-character
///   Crockford-lowercase compact form (saves ~9.7 tokens per ID).
///   The mapping is collision-detected across all files in
///   <c>root</c>.</description></item>
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
        var rootArgument = new Argument<DirectoryInfo>(
            name: "root",
            description: "Root directory to walk recursively for .calr files")
        {
            Arity = ArgumentArity.ExactlyOne
        };

        var dropStructuralIdsOption = new Option<bool>(
            aliases: ["--drop-structural-ids"],
            description: "Drop {id...} blocks from structural closing tags");

        var compactIdsOption = new Option<bool>(
            aliases: ["--compact-ids"],
            description: "Rewrite legacy ULID payloads to v6 12-char compact form");

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

        var command = new Command("fix", "Apply mechanical, reversible source rewrites")
        {
            rootArgument,
            dropStructuralIdsOption,
            compactIdsOption,
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
            context.ExitCode = await ExecuteAsync(
                parse.GetValueForArgument(rootArgument),
                parse.GetValueForOption(dropStructuralIdsOption),
                parse.GetValueForOption(compactIdsOption),
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
        bool dropStructuralIds,
        bool compactIds,
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
        var selectedCount = (dropStructuralIds ? 1 : 0)
                          + (compactIds ? 1 : 0)
                          + (elideCallClosers ? 1 : 0)
                          + (healClosers ? 1 : 0);
        if (selectedCount == 0)
        {
            EmitErrorEnvelope(json, DiagnosticCode.CliUsageError,
                "specify --drop-structural-ids, --compact-ids, --elide-call-closers, or --heal-closers", filePath: null);
            Console.Error.WriteLine("specify --drop-structural-ids, --compact-ids, --elide-call-closers, or --heal-closers");
            return 2;
        }
        if (selectedCount > 1)
        {
            EmitErrorEnvelope(json, DiagnosticCode.CliUsageError,
                "--drop-structural-ids, --compact-ids, --elide-call-closers, and --heal-closers are mutually exclusive; run them separately", filePath: null);
            Console.Error.WriteLine("--drop-structural-ids, --compact-ids, --elide-call-closers, and --heal-closers are mutually exclusive; run them separately");
            return 2;
        }

        if (dropStructuralIds)
        {
            return revert
                ? await RevertStructuralIdsAsync(root, log, dryRun, json)
                : await DropStructuralIdsAsync(root, log, dryRun, json);
        }
        if (elideCallClosers)
        {
            return revert
                ? await RevertElideCallClosersAsync(root, log, dryRun, json)
                : await ElideCallClosersAsync(root, log, dryRun, json);
        }
        if (healClosers)
        {
            return revert
                ? await RevertHealClosersAsync(root, log, dryRun, json)
                : await HealClosersAsync(root, log, dryRun, json);
        }
        // compactIds
        return revert
            ? await RevertCompactIdsAsync(root, log, dryRun, json)
            : await DoCompactIdsAsync(root, log, dryRun, json);
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

    private static async Task<int> DropStructuralIdsAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun, bool json)
    {
        var dropper = new StructuralIdDropper();
        var migrationLog = new StructuralIdDropper.MigrationLog();
        int filesChanged = 0;
        int totalRemovals = 0;

        foreach (var path in EnumerateCalrFiles(root))
        {
            var rel = MakeRelativePosix(root, path);
            var original = await File.ReadAllTextAsync(path, Encoding.UTF8);
            var (migrated, removals) = dropper.Process(original, rel);
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
            var logJson = StructuralIdDropper.SerializeLog(migrationLog);
            await File.WriteAllTextAsync(logFile.FullName, logJson, new UTF8Encoding(false));
        }

        EmitSummary(json, "drop-structural-ids", dryRun, filesChanged, totalRemovals,
            migrationLog.Entries.Select(e => e.File), logFile,
            $"drop-structural-ids: {(dryRun ? "[dry-run] " : "")}files_changed={filesChanged} removals={totalRemovals}");

        return 0;
    }

    private static async Task<int> RevertStructuralIdsAsync(
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
        var migrationLog = StructuralIdDropper.DeserializeLog(logJson);

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
        EmitRevertSummary(json, "drop-structural-ids", dryRun, filesRestored);
        return 0;
    }

    private static async Task<int> DoCompactIdsAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun, bool json)
    {
        // Load every .calr file into memory so the migrator can detect
        // cross-file collisions in a single pass.
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var pathByRel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in EnumerateCalrFiles(root))
        {
            var rel = MakeRelativePosix(root, path);
            sources[rel] = await File.ReadAllTextAsync(path, Encoding.UTF8);
            pathByRel[rel] = path;
        }

        var migrator = new CompactIdMigrator();
        var (migrated, log) = migrator.Migrate(sources);

        int filesChanged = 0;
        foreach (var (rel, newText) in migrated)
        {
            if (newText == sources[rel])
            {
                continue;
            }
            filesChanged++;
            if (!dryRun)
            {
                await File.WriteAllTextAsync(pathByRel[rel], newText, new UTF8Encoding(false));
            }
        }

        if (logFile != null)
        {
            var logJson = CompactIdMigrator.SerializeLog(log);
            await File.WriteAllTextAsync(logFile.FullName, logJson, new UTF8Encoding(false));
        }

        EmitSummary(json, "compact-ids", dryRun, filesChanged, log.Entries.Count,
            log.Entries.Select(e => e.File), logFile,
            $"compact-ids: {(dryRun ? "[dry-run] " : "")}files_changed={filesChanged} replacements={log.Entries.Count}");

        return 0;
    }

    private static async Task<int> RevertCompactIdsAsync(
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
        var log = CompactIdMigrator.DeserializeLog(logJson);

        // Read every file that the log references.
        var migrated = new Dictionary<string, string>(StringComparer.Ordinal);
        var pathByRel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in log.Entries.Select(e => e.File).Distinct(StringComparer.Ordinal))
        {
            var migPath = Path.Combine(root.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(migPath))
            {
                Console.Error.WriteLine($"skip missing file: {migPath}");
                continue;
            }
            migrated[rel] = await File.ReadAllTextAsync(migPath, Encoding.UTF8);
            pathByRel[rel] = migPath;
        }

        var migrator = new CompactIdMigrator();
        var restored = migrator.Revert(migrated, log);

        int filesRestored = 0;
        foreach (var (rel, restoredText) in restored)
        {
            if (!dryRun)
            {
                await File.WriteAllTextAsync(pathByRel[rel], restoredText, new UTF8Encoding(false));
            }
            filesRestored++;
        }
        EmitRevertSummary(json, "compact-ids", dryRun, filesRestored);
        return 0;
    }

    private static async Task<int> ElideCallClosersAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun, bool json)
    {
        var elider = new CallCloserElider();
        var migrationLog = new StructuralIdDropper.MigrationLog();
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
        var migrationLog = new StructuralIdDropper.MigrationLog();
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
        IEnumerable<StructuralIdDropper.LogEntry> entries)
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
