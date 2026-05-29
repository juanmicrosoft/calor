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
///   since v0.5.x — the lexer accepts both forms. Run repeatedly: each
///   invocation drops only IDs that match the recognized shape.</description></item>
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
            revertOption,
            logOption,
            dryRunOption,
        };

        command.SetHandler(
            ExecuteAsync,
            rootArgument, dropStructuralIdsOption,
            revertOption, logOption, dryRunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        DirectoryInfo root,
        bool dropStructuralIds,
        bool revert,
        FileInfo? log,
        bool dryRun)
    {
        if (!root.Exists)
        {
            Console.Error.WriteLine($"root directory not found: {root.FullName}");
            return 2;
        }
        if (!dropStructuralIds)
        {
            Console.Error.WriteLine("specify --drop-structural-ids");
            return 2;
        }

        if (revert)
        {
            return await RevertStructuralIdsAsync(root, log, dryRun);
        }
        return await DropStructuralIdsAsync(root, log, dryRun);
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
