using System.CommandLine;
using System.Text;
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

        var revertOption = new Option<bool>(
            aliases: ["--revert"],
            description: "Reverse the operation using --log");

        var logOption = new Option<FileInfo?>(
            aliases: ["--log"],
            description: "Path to write (or read, with --revert) migration.log.json");

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run", "-n"],
            description: "Report what would change without writing files");

        var command = new Command("fix", "Apply mechanical, reversible source rewrites")
        {
            rootArgument,
            dropStructuralIdsOption,
            compactIdsOption,
            elideCallClosersOption,
            revertOption,
            logOption,
            dryRunOption,
        };

        command.SetHandler(
            ExecuteAsync,
            rootArgument, dropStructuralIdsOption, compactIdsOption,
            elideCallClosersOption, revertOption, logOption, dryRunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        DirectoryInfo root,
        bool dropStructuralIds,
        bool compactIds,
        bool elideCallClosers,
        bool revert,
        FileInfo? log,
        bool dryRun)
    {
        if (!root.Exists)
        {
            Console.Error.WriteLine($"root directory not found: {root.FullName}");
            return 2;
        }
        var selectedCount = (dropStructuralIds ? 1 : 0)
                          + (compactIds ? 1 : 0)
                          + (elideCallClosers ? 1 : 0);
        if (selectedCount == 0)
        {
            Console.Error.WriteLine("specify --drop-structural-ids, --compact-ids, or --elide-call-closers");
            return 2;
        }
        if (selectedCount > 1)
        {
            Console.Error.WriteLine("--drop-structural-ids, --compact-ids, and --elide-call-closers are mutually exclusive; run them separately");
            return 2;
        }

        if (dropStructuralIds)
        {
            return revert
                ? await RevertStructuralIdsAsync(root, log, dryRun)
                : await DropStructuralIdsAsync(root, log, dryRun);
        }
        if (elideCallClosers)
        {
            return revert
                ? await RevertElideCallClosersAsync(root, log, dryRun)
                : await ElideCallClosersAsync(root, log, dryRun);
        }
        // compactIds
        return revert
            ? await RevertCompactIdsAsync(root, log, dryRun)
            : await DoCompactIdsAsync(root, log, dryRun);
    }

    private static async Task<int> DropStructuralIdsAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun)
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

        Console.WriteLine(
            $"drop-structural-ids: {(dryRun ? "[dry-run] " : "")}files_changed={filesChanged} removals={totalRemovals}");

        if (logFile != null)
        {
            var json = StructuralIdDropper.SerializeLog(migrationLog);
            await File.WriteAllTextAsync(logFile.FullName, json, new UTF8Encoding(false));
            Console.WriteLine($"wrote {logFile.FullName}");
        }

        return 0;
    }

    private static async Task<int> RevertStructuralIdsAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun)
    {
        if (logFile == null || !logFile.Exists)
        {
            Console.Error.WriteLine("--revert requires --log <existing migration.log.json>");
            return 2;
        }
        var json = await File.ReadAllTextAsync(logFile.FullName, Encoding.UTF8);
        var migrationLog = StructuralIdDropper.DeserializeLog(json);

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
        Console.WriteLine(
            $"revert: {(dryRun ? "[dry-run] " : "")}files_restored={filesRestored}");
        return 0;
    }

    private static async Task<int> DoCompactIdsAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun)
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

        Console.WriteLine(
            $"compact-ids: {(dryRun ? "[dry-run] " : "")}files_changed={filesChanged} replacements={log.Entries.Count}");

        if (logFile != null)
        {
            var json = CompactIdMigrator.SerializeLog(log);
            await File.WriteAllTextAsync(logFile.FullName, json, new UTF8Encoding(false));
            Console.WriteLine($"wrote {logFile.FullName}");
        }

        return 0;
    }

    private static async Task<int> RevertCompactIdsAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun)
    {
        if (logFile == null || !logFile.Exists)
        {
            Console.Error.WriteLine("--revert requires --log <existing migration.log.json>");
            return 2;
        }
        var json = await File.ReadAllTextAsync(logFile.FullName, Encoding.UTF8);
        var log = CompactIdMigrator.DeserializeLog(json);

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
        Console.WriteLine(
            $"revert: {(dryRun ? "[dry-run] " : "")}files_restored={filesRestored}");
        return 0;
    }

    private static async Task<int> ElideCallClosersAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun)
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

        Console.WriteLine(
            $"elide-call-closers: {(dryRun ? "[dry-run] " : "")}files_changed={filesChanged} elisions={totalRemovals} files_skipped={filesSkipped}");

        if (logFile != null)
        {
            var json = CallCloserElider.SerializeLog(migrationLog);
            await File.WriteAllTextAsync(logFile.FullName, json, new UTF8Encoding(false));
            Console.WriteLine($"wrote {logFile.FullName}");
        }

        return 0;
    }

    private static async Task<int> RevertElideCallClosersAsync(
        DirectoryInfo root, FileInfo? logFile, bool dryRun)
    {
        if (logFile == null || !logFile.Exists)
        {
            Console.Error.WriteLine("--revert requires --log <existing migration.log.json>");
            return 2;
        }
        var json = await File.ReadAllTextAsync(logFile.FullName, Encoding.UTF8);
        var migrationLog = CallCloserElider.DeserializeLog(json);

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
        Console.WriteLine(
            $"revert: {(dryRun ? "[dry-run] " : "")}files_restored={filesRestored}");
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
