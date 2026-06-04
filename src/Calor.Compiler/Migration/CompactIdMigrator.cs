using System.Text;
using System.Text.Json;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Migration;

/// <summary>
/// Rewrites legacy 26-character Crockford-uppercase ULID payloads inside
/// Calor section markers to the v6 12-character Crockford-lowercase
/// compact form. Implements the <c>calor fix --compact-ids</c>
/// migration documented in
/// <c>docs/plans/path-2-drop-ids-v6-implementation.md</c>.
///
/// <para>The migrator runs in two passes across the full set of files
/// supplied to <see cref="Migrate"/>:</para>
/// <list type="number">
///   <item><description><b>Pass 1.</b> Scan every file for
///   <c>§&lt;TAG&gt;{&lt;id&gt;…}</c> patterns. For each first-positional
///   that <see cref="IdValidator.IsLegacyUlidId"/> recognises, record the
///   byte range, the surrounding prefix and the ULID payload. Also
///   collect <i>existing</i> v6 compact payloads (so newly-derived
///   compacts do not collide with them).</description></item>
///   <item><description><b>Pass 2.</b> Build a deterministic mapping
///   from each unique ULID payload to a compact payload via
///   <see cref="CompactIdGenerator.DeriveFromUlid"/> (last 12 chars,
///   lowercased — preserves visual stability and review-friendliness).
///   Detect collisions: (a) two ULIDs deriving to the same compact, or
///   (b) a derived compact colliding with a pre-existing compact ID.
///   In a collision, the migrator mints a fresh random compact payload
///   instead of the derived one until the mapping is globally
///   unique.</description></item>
///   <item><description>Apply the mapping per file, producing a
///   rewritten source plus a <see cref="MigrationLog"/> that supports
///   byte-exact reversion via <see cref="Revert"/>.</description></item>
/// </list>
///
/// <para>The migrator is <b>idempotent</b>: re-running it on
/// already-migrated sources is a no-op (no ULID-shaped payloads remain
/// inside section markers, so no rewrites happen).</para>
/// </summary>
public sealed class CompactIdMigrator
{
    /// <summary>
    /// In-memory log capturing every replacement performed. Serialised
    /// to JSON via <see cref="SerializeLog"/> for storage alongside the
    /// migrated tree.
    /// </summary>
    public sealed class MigrationLog
    {
        public List<LogEntry> Entries { get; init; } = new();
    }

    /// <summary>
    /// One replacement record. <see cref="MigratedOffset"/> and
    /// <see cref="MigratedLength"/> locate the replacement inside the
    /// <i>migrated</i> file's UTF-8 byte stream; the verifier and
    /// <see cref="Revert"/> use this to find the bytes to restore.
    /// <see cref="OriginalBytesBase64"/> is what to put back.
    /// </summary>
    public sealed class LogEntry
    {
        public required string File { get; init; }
        public required int MigratedOffset { get; init; }
        public required int MigratedLength { get; init; }
        public required string ReplacementText { get; init; }
        public required string OriginalBytesBase64 { get; init; }
    }

    /// <summary>
    /// Section-marker keywords whose first positional is an ID. The
    /// migrator only inspects these (the same conservative set used by
    /// <see cref="StructuralIdDropper"/>, plus other ID-bearing markers
    /// known to the parser). Other markers (e.g. <c>§B</c> for
    /// bindings) put non-ID values first and are left alone.
    /// </summary>
    private static readonly HashSet<string> IdBearingMarkers = new(StringComparer.Ordinal)
    {
        // Opens (kept in sync with StructuralIdDropper.StructuralOpeners)
        "M", "F", "AF", "L", "IF", "TR", "CL", "IN", "PR", "MT",
        // Additional ID-bearing kinds
        "ENUM", "E", "EXT", "RT", "PO", "IT", "IX", "OP", "CTOR",
        // Closes
        "/M", "/F", "/AF", "/L", "/I", "/TR", "/CL", "/IN", "/PR", "/MT",
        "/ENUM", "/E", "/EXT", "/RT",
    };

    private sealed record Occurrence(
        string File,
        int CharStart,     // start of the ID inside the source string
        int CharLength,    // length in chars of the ID (prefix + payload)
        string Prefix,
        string UlidPayload);

    /// <summary>
    /// Migrate a set of files from ULID-bearing IDs to v6 compact IDs.
    /// The returned dictionary contains the rewritten source for every
    /// input file (files with no ULIDs are returned unchanged).
    /// </summary>
    /// <param name="sources">Map of relative-path → source text. Use
    /// forward-slashed paths for platform portability of the log.</param>
    /// <returns>The rewritten sources keyed by the same paths, plus a
    /// log of every replacement.</returns>
    public (IReadOnlyDictionary<string, string> Migrated, MigrationLog Log) Migrate(
        IReadOnlyDictionary<string, string> sources)
    {
        // Pass 1: scan every file.
        var occurrences = new List<Occurrence>();
        var existingCompactPayloads = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, source) in sources)
        {
            ScanFile(path, source, occurrences, existingCompactPayloads);
        }

        // Pass 2: build collision-free ULID → compact mapping.
        var mapping = BuildMapping(occurrences, existingCompactPayloads);

        // Apply per-file rewrites.
        var migrated = new Dictionary<string, string>(sources.Count, StringComparer.Ordinal);
        var log = new MigrationLog();
        foreach (var (path, source) in sources)
        {
            var fileOccurrences = occurrences.Where(o => o.File == path).ToList();
            if (fileOccurrences.Count == 0)
            {
                migrated[path] = source;
                continue;
            }
            migrated[path] = RewriteFile(source, fileOccurrences, mapping, path, log);
        }

        return (migrated, log);
    }

    /// <summary>
    /// Reverse a previously-applied migration. Returns the original
    /// source for each file by undoing the recorded replacements.
    /// </summary>
    public IReadOnlyDictionary<string, string> Revert(
        IReadOnlyDictionary<string, string> migratedSources, MigrationLog log)
    {
        var byFile = log.Entries
            .GroupBy(e => e.File, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.MigratedOffset).ToList(),
                          StringComparer.Ordinal);

        var result = new Dictionary<string, string>(migratedSources.Count, StringComparer.Ordinal);
        foreach (var (path, migratedSource) in migratedSources)
        {
            if (!byFile.TryGetValue(path, out var entries))
            {
                result[path] = migratedSource;
                continue;
            }

            // Splice in reverse offset order so prior offsets stay valid.
            var bytes = new List<byte>(Encoding.UTF8.GetBytes(migratedSource));
            foreach (var entry in entries)
            {
                var originalBytes = Convert.FromBase64String(entry.OriginalBytesBase64);
                bytes.RemoveRange(entry.MigratedOffset, entry.MigratedLength);
                bytes.InsertRange(entry.MigratedOffset, originalBytes);
            }
            result[path] = Encoding.UTF8.GetString(bytes.ToArray());
        }

        return result;
    }

    /// <summary>Serialise a log to JSON in the on-disk schema.</summary>
    public static string SerializeLog(MigrationLog log)
    {
        return JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
    }

    /// <summary>Deserialise a previously-written log.</summary>
    public static MigrationLog DeserializeLog(string json)
    {
        return JsonSerializer.Deserialize<MigrationLog>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        }) ?? new MigrationLog();
    }

    private static void ScanFile(string path, string source,
        List<Occurrence> occurrences, HashSet<string> existingCompactPayloads)
    {
        int i = 0;
        while (i < source.Length)
        {
            if (source[i] != '\u00a7')
            {
                i++;
                continue;
            }

            int kwStart = i + 1;
            int kwEnd = kwStart;
            if (kwEnd < source.Length && source[kwEnd] == '/')
            {
                kwEnd++;
            }
            while (kwEnd < source.Length &&
                   (char.IsLetter(source[kwEnd]) || char.IsDigit(source[kwEnd])))
            {
                kwEnd++;
            }

            if (kwEnd >= source.Length || source[kwEnd] != '{')
            {
                i = kwEnd;
                continue;
            }

            var keyword = source.Substring(kwStart, kwEnd - kwStart);
            if (!IdBearingMarkers.Contains(keyword))
            {
                i = kwEnd;
                continue;
            }

            int braceStart = kwEnd;
            int braceEnd = source.IndexOf('}', braceStart + 1);
            if (braceEnd < 0)
            {
                break;
            }

            var inner = source.Substring(braceStart + 1, braceEnd - braceStart - 1);
            var firstColon = inner.IndexOf(':');
            var firstPositional = firstColon < 0 ? inner : inner.Substring(0, firstColon);

            var idStart = braceStart + 1;

            if (!AttributeHelper.LooksLikeId(firstPositional))
            {
                i = braceEnd + 1;
                continue;
            }

            // Classify the ID payload.
            if (IdValidator.IsLegacyUlidId(firstPositional))
            {
                var payload = IdGenerator.ExtractPayload(firstPositional)!;
                var prefix = firstPositional.Substring(0, firstPositional.Length - payload.Length);
                occurrences.Add(new Occurrence(
                    File: path,
                    CharStart: idStart,
                    CharLength: firstPositional.Length,
                    Prefix: prefix,
                    UlidPayload: payload));
            }
            else if (IdValidator.IsCompactId(firstPositional))
            {
                var payload = IdGenerator.ExtractPayload(firstPositional)!;
                existingCompactPayloads.Add(payload);
            }

            i = braceEnd + 1;
        }
    }

    private static Dictionary<string, string> BuildMapping(
        List<Occurrence> occurrences, HashSet<string> existingCompactPayloads)
    {
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedCompacts = new HashSet<string>(existingCompactPayloads, StringComparer.Ordinal);

        // Deterministic order: sort by the ULID payload so the mapping
        // is reproducible across runs given the same set of inputs.
        var uniqueUlids = occurrences
            .Select(o => o.UlidPayload)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var ulid in uniqueUlids)
        {
            // Try the deterministic derivation first.
            var derived = CompactIdGenerator.DeriveFromUlid(ulid);
            if (derived != null && usedCompacts.Add(derived))
            {
                mapping[ulid] = derived;
                continue;
            }

            // Derivation collided (or was invalid). Mint a fresh
            // compact payload until we find one that's globally unique.
            string fresh;
            do
            {
                fresh = CompactIdGenerator.GeneratePayload();
            }
            while (!usedCompacts.Add(fresh));
            mapping[ulid] = fresh;
        }

        return mapping;
    }

    private static string RewriteFile(string source, List<Occurrence> occurrences,
        Dictionary<string, string> mapping, string path, MigrationLog log)
    {
        // Apply rewrites in source order, tracking running byte offset.
        var ordered = occurrences.OrderBy(o => o.CharStart).ToList();
        var sb = new StringBuilder(source.Length);
        int cursor = 0;
        int byteCursor = 0;

        foreach (var occ in ordered)
        {
            // Copy [cursor, occ.CharStart) verbatim.
            sb.Append(source, cursor, occ.CharStart - cursor);
            byteCursor += Utf8ByteCount(source, cursor, occ.CharStart);

            var compactPayload = mapping[occ.UlidPayload];
            var replacement = occ.Prefix + compactPayload;
            var originalText = source.Substring(occ.CharStart, occ.CharLength);
            var originalBytes = Encoding.UTF8.GetBytes(originalText);
            var replacementBytes = Encoding.UTF8.GetBytes(replacement);

            log.Entries.Add(new LogEntry
            {
                File = path,
                MigratedOffset = byteCursor,
                MigratedLength = replacementBytes.Length,
                ReplacementText = replacement,
                OriginalBytesBase64 = Convert.ToBase64String(originalBytes),
            });

            sb.Append(replacement);
            byteCursor += replacementBytes.Length;
            cursor = occ.CharStart + occ.CharLength;
        }

        // Tail.
        sb.Append(source, cursor, source.Length - cursor);
        return sb.ToString();
    }

    private static int Utf8ByteCount(char ch)
    {
        if (ch < 0x0080) return 1;
        if (ch < 0x0800) return 2;
        if (char.IsHighSurrogate(ch)) return 4;
        if (char.IsLowSurrogate(ch)) return 0;
        return 3;
    }

    private static int Utf8ByteCount(string source, int startInclusive, int endExclusive)
    {
        int total = 0;
        for (int j = startInclusive; j < endExclusive; j++)
        {
            total += Utf8ByteCount(source[j]);
        }
        return total;
    }
}
