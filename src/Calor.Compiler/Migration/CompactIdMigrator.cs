using System.Text;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Migration;

/// <summary>
/// Rewrites every legacy 26-char ULID payload in a Calor source file
/// to a 12-char compact ID, recording an old→new mapping so the
/// migration is reversible.
///
/// The migrator preserves byte-exact substitution semantics: every
/// occurrence of a given ULID in the source — whether on a declaration
/// site or in a cross-reference — is replaced with the same compact
/// payload. This keeps internal references intact.
///
/// Inputs are processed file-by-file. The mapping table is shared
/// across files in one invocation so that an ID declared in
/// <c>a.calr</c> and referenced from <c>b.calr</c> rewrites identically
/// in both.
/// </summary>
public sealed class CompactIdMigrator
{
    private readonly Dictionary<string, string> _mapping = new(StringComparer.Ordinal);
    private readonly IdRegistry _registry = new();

    /// <summary>
    /// Single-file mapping log entry. Multiple entries for the same
    /// (oldId, newId) pair across files are allowed — the reverter
    /// keys solely off the new ID, so duplicates are no-ops.
    /// </summary>
    public sealed class MappingLog
    {
        public List<MappingEntry> Entries { get; init; } = new();
    }

    public sealed class MappingEntry
    {
        public required string OldId { get; init; }
        public required string NewId { get; init; }
        public required string File { get; init; }
        public required int OccurrenceCount { get; init; }
    }

    /// <summary>
    /// Rewrite all ULID payloads in <paramref name="source"/>. Returns
    /// the rewritten text and any new mapping entries created during
    /// this call. The mapping table persists across calls on the same
    /// instance.
    /// </summary>
    public (string Migrated, List<MappingEntry> NewEntries) Process(
        string source, string relativeFilePath)
    {
        var newEntries = new List<MappingEntry>();
        var perIdCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Two-pass scan: first identify ULIDs, then perform the
        // rewrite in one pass. This keeps the replacement code
        // straightforward (no overlapping spans to manage).
        var ulids = FindUlidOccurrences(source);
        if (ulids.Count == 0)
        {
            return (source, newEntries);
        }

        var sb = new StringBuilder(source.Length);
        int cursor = 0;
        foreach (var span in ulids)
        {
            if (span.Start > cursor) sb.Append(source, cursor, span.Start - cursor);

            var oldId = source.Substring(span.Start, span.Length);
            var newId = MapId(oldId);
            sb.Append(newId);
            cursor = span.Start + span.Length;

            perIdCounts[oldId] = perIdCounts.GetValueOrDefault(oldId) + 1;
        }
        if (cursor < source.Length) sb.Append(source, cursor, source.Length - cursor);

        foreach (var (oldId, count) in perIdCounts)
        {
            newEntries.Add(new MappingEntry
            {
                OldId = oldId,
                NewId = _mapping[oldId],
                File = relativeFilePath,
                OccurrenceCount = count,
            });
        }
        return (sb.ToString(), newEntries);
    }

    /// <summary>
    /// Reverse the rewrite: given a previously-written mapping log,
    /// substitute every <c>NewId</c> back to <c>OldId</c> in the
    /// migrated file.
    /// </summary>
    public static string Revert(string migratedSource, IEnumerable<MappingEntry> entries)
    {
        // Build the reverse lookup once.
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            reverse[e.NewId] = e.OldId;
        }
        if (reverse.Count == 0) return migratedSource;

        var sb = new StringBuilder(migratedSource.Length);
        int i = 0;
        while (i < migratedSource.Length)
        {
            // Lex-skip until we land on a likely prefix start.
            var matched = TryMatchAnyPrefix(migratedSource, i, reverse, out var newId, out var oldId);
            if (matched)
            {
                sb.Append(oldId);
                i += newId.Length;
            }
            else
            {
                sb.Append(migratedSource[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private string MapId(string oldId)
    {
        if (_mapping.TryGetValue(oldId, out var cached)) return cached;

        // Figure out the prefix to reuse.
        var underscore = oldId.IndexOf('_');
        var prefix = underscore <= 0 ? "x_" : oldId.Substring(0, underscore + 1);
        var newId = _registry.GenerateAndRegister(prefix);
        _mapping[oldId] = newId;
        return newId;
    }

    /// <summary>
    /// Locate every occurrence of a 26-character ULID-shaped payload
    /// preceded by a recognised prefix. Returns spans in source order.
    /// </summary>
    private static List<(int Start, int Length)> FindUlidOccurrences(string source)
    {
        var spans = new List<(int Start, int Length)>();
        for (int i = 0; i < source.Length; i++)
        {
            // Quick reject: a Calor ID never starts with a digit.
            if (!IsLowerLetter(source[i])) continue;

            // Word-boundary on the left: previous char (if any) must not
            // be a letter or digit. Without this, "prefixm_<26>" would
            // be greedily consumed as a 7-char prefix.
            if (i > 0 && (IsLowerLetter(source[i - 1]) || IsBase32(source[i - 1])))
            {
                continue;
            }

            // Greedily consume lowercase prefix letters until an underscore.
            int j = i;
            while (j < source.Length && IsLowerLetter(source[j])) j++;
            if (j == i || j >= source.Length || source[j] != '_') continue;
            var prefix = source.Substring(i, j - i + 1); // includes the '_'
            if (!KnownPrefixes.Contains(prefix)) continue;
            int payloadStart = j + 1;

            // Must be followed by exactly 26 Crockford base-32 chars.
            if (payloadStart + 26 > source.Length) continue;
            bool allOk = true;
            for (int k = payloadStart; k < payloadStart + 26; k++)
            {
                if (!IsBase32(source[k])) { allOk = false; break; }
            }
            if (!allOk) continue;

            // The next char (if any) must NOT extend the payload, else
            // this isn't a 26-char run.
            int after = payloadStart + 26;
            if (after < source.Length && IsBase32(source[after])) continue;

            spans.Add((i, after - i));
            i = after - 1; // -1 because the loop will increment.
        }
        return spans;
    }

    /// <summary>
    /// The exact set of legal Calor ID prefixes (lowercase letters
    /// terminated by '_'). Anything outside this set is not an ID.
    /// </summary>
    private static readonly HashSet<string> KnownPrefixes = new(StringComparer.Ordinal)
    {
        "m_", "f_", "c_", "i_", "p_", "mt_", "ctor_", "e_", "op_",
    };

    private static bool TryMatchAnyPrefix(
        string source, int i,
        Dictionary<string, string> reverse,
        out string newId, out string oldId)
    {
        // Word-boundary on the left.
        if (i > 0 && (IsLowerLetter(source[i - 1]) || IsBase32(source[i - 1])))
        {
            newId = oldId = "";
            return false;
        }

        // Walk a lowercase prefix.
        int j = i;
        while (j < source.Length && IsLowerLetter(source[j])) j++;
        if (j == i || j >= source.Length || source[j] != '_')
        {
            newId = oldId = "";
            return false;
        }
        var prefix = source.Substring(i, j - i + 1);
        if (!KnownPrefixes.Contains(prefix))
        {
            newId = oldId = "";
            return false;
        }
        int payloadStart = j + 1;
        if (payloadStart + CompactIdGenerator.PayloadLength > source.Length)
        {
            newId = oldId = "";
            return false;
        }
        for (int k = payloadStart; k < payloadStart + CompactIdGenerator.PayloadLength; k++)
        {
            if (!IsBase32(source[k]))
            {
                newId = oldId = "";
                return false;
            }
        }
        int after = payloadStart + CompactIdGenerator.PayloadLength;
        if (after < source.Length && IsBase32(source[after]))
        {
            newId = oldId = "";
            return false;
        }
        var candidate = source.Substring(i, after - i);
        if (reverse.TryGetValue(candidate, out var resolved))
        {
            newId = candidate;
            oldId = resolved;
            return true;
        }
        newId = oldId = "";
        return false;
    }

    private static bool IsLowerLetter(char c) => c >= 'a' && c <= 'z';
    private static bool IsBase32(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
}
