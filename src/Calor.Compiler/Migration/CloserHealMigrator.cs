using System.Text;
using Calor.Compiler.Analysis;

namespace Calor.Compiler.Migration;

/// <summary>
/// <c>calor fix --heal-closers</c> — deletes legacy structural closing tags
/// (<c>§/F</c>, <c>§/M</c>, <c>§/L</c>, …) from a <c>.calr</c> source, rewriting
/// it into canonical indent-only form. Closer form hard-errors at parse time
/// (<c>Calor0830</c>), so the AST-based <c>calor format</c> / <c>calor lint
/// --fix</c> paths cannot heal such a file — this migrator works at the source
/// level via <see cref="LegacyCloserFormLint.ScanForHeal"/>.
///
/// <para>Removals are recorded as UTF-8 <em>byte</em> ranges plus the removed
/// bytes (base64) using the shared <see cref="StructuralIdDropper.LogEntry"/>
/// schema, so a <c>migration.log.json</c> can byte-exactly revert the change via
/// the same <c>ReinsertRemovals</c> path the other <c>fix</c> subcommands use.
/// <see cref="LegacyCloserFormLint.ScanForHeal"/> returns offsets in CHARACTER
/// units; because <c>§</c> is a two-byte UTF-8 code point, this migrator converts
/// each removal to a byte offset/length for the log (a char offset would corrupt
/// revert whenever any non-ASCII precedes the removal).</para>
/// </summary>
public sealed class CloserHealMigrator
{
    /// <summary>
    /// Heal one file. Returns the rewritten source and the byte-accurate
    /// removal records (empty when there is nothing to heal).
    /// </summary>
    public (string Migrated, List<StructuralIdDropper.LogEntry> Removals) Process(
        string source, string relativeFilePath)
    {
        var removals = new List<StructuralIdDropper.LogEntry>();
        var findings = LegacyCloserFormLint.ScanForHeal(source, relativeFilePath);
        if (findings.Count == 0)
        {
            return (source, removals);
        }

        var ordered = findings.OrderBy(f => f.RemovedOffset).ToList();
        var sb = new StringBuilder(source.Length);
        int cursor = 0; // char cursor into source

        foreach (var finding in ordered)
        {
            int startChar = finding.RemovedOffset;
            int endChar = finding.RemovedOffset + finding.RemovedLength;

            // Defensive: skip a finding that overlaps one already applied.
            if (startChar < cursor || endChar > source.Length)
            {
                continue;
            }

            // Preserve the gap before this removal.
            sb.Append(source, cursor, startChar - cursor);

            // Record the removal in ORIGINAL-source UTF-8 byte coordinates so the
            // shared byte-based revert can reconstruct the file exactly.
            int removedByteStart = Encoding.UTF8.GetByteCount(source.AsSpan(0, startChar));
            var removedBytes = Encoding.UTF8.GetBytes(source.Substring(startChar, endChar - startChar));
            removals.Add(new StructuralIdDropper.LogEntry
            {
                File = relativeFilePath,
                RemovedOffset = removedByteStart,
                RemovedLength = removedBytes.Length,
                RemovedBytesBase64 = Convert.ToBase64String(removedBytes),
            });

            cursor = endChar;
        }

        sb.Append(source, cursor, source.Length - cursor);
        return (sb.ToString(), removals);
    }

    /// <summary>Serialise a heal log (delegates to the shared schema).</summary>
    public static string SerializeLog(StructuralIdDropper.MigrationLog log)
        => StructuralIdDropper.SerializeLog(log);

    /// <summary>Deserialise a heal log (delegates to the shared schema).</summary>
    public static StructuralIdDropper.MigrationLog DeserializeLog(string json)
        => StructuralIdDropper.DeserializeLog(json);
}
