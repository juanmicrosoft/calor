using System.Globalization;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Migration;

/// <summary>
/// Removes the leading <c>{id…}</c> block from each structural opener
/// (<c>§M</c>, <c>§F</c>, <c>§AF</c>, <c>§L</c>, <c>§IF</c>, <c>§TR</c>,
/// …) and the trailing <c>{id}</c> from each matching closer, when the
/// first positional value matches the ID shape recognised by
/// <see cref="AttributeHelper.LooksLikeId"/>.
///
/// Implements Phase 1 of the v6 plan (RFC §5.7). Removed byte ranges are
/// recorded in <see cref="MigrationLog"/> so that
/// <see cref="BytePreservationVerifier"/> can confirm byte-equality of
/// non-removed regions, and a reverse migrator can restore the file
/// exactly.
///
/// The dropper is conservative:
/// <list type="bullet">
///   <item><description>It only fires on the structural-opener prefixes
///   listed in <see cref="StructuralOpeners"/>.</description></item>
///   <item><description>If the first positional value does not look
///   like an ID, the block is left untouched (so user-authored
///   <c>§M{Calculator}</c> is preserved verbatim).</description></item>
///   <item><description>It does not touch any expression-level
///   <c>{...}</c> blocks: it only matches blocks immediately following
///   a section marker.</description></item>
/// </list>
/// </summary>
public sealed class StructuralIdDropper
{
    /// <summary>
    /// Section-marker keywords on which a leading <c>{id:…}</c> block
    /// is droppable. Closing forms (<c>/M</c>, <c>/F</c>, etc.) are
    /// included because the closing tag's sole positional is the ID.
    /// </summary>
    private static readonly HashSet<string> StructuralOpeners = new(StringComparer.Ordinal)
    {
        // Opens
        "M", "F", "AF", "L", "IF", "TR", "CL", "IN", "PR", "MT",
        // Closes
        "/M", "/F", "/AF", "/L", "/I", "/TR", "/CL", "/IN", "/PR", "/MT",
    };

    /// <summary>
    /// Section-marker keywords whose first positional is treated as the
    /// ID and whose payload may include further values that must be
    /// retained (e.g. the name on <c>§M{id:Name}</c>). For these
    /// openers the dropper rewrites <c>{id:rest}</c> as <c>{rest}</c>
    /// rather than removing the whole block.
    /// </summary>
    private static readonly HashSet<string> OpenersWithExtraPositionals = new(StringComparer.Ordinal)
    {
        "M", "F", "AF", "L", "IF", "TR", "CL", "IN", "PR", "MT",
    };

    /// <summary>
    /// In-memory representation of <c>migration.log.json</c>.
    /// Schema is fixed and consumed by
    /// <see cref="BytePreservationVerifier"/> and the Python
    /// <c>byte_preservation_check.py</c> harness.
    /// </summary>
    public sealed class MigrationLog
    {
        public List<LogEntry> Entries { get; init; } = new();
    }

    /// <summary>
    /// One removal record. <see cref="RemovedBytesBase64"/> is the
    /// base64-encoded UTF-8 bytes that were removed; the verifier and
    /// reverter use this to reconstruct the original.
    /// </summary>
    public sealed class LogEntry
    {
        public required string File { get; init; }
        public required int RemovedOffset { get; init; }
        public required int RemovedLength { get; init; }
        public required string RemovedBytesBase64 { get; init; }
    }

    /// <summary>
    /// Drops structural IDs from <paramref name="source"/>. Returns the
    /// rewritten source and a list of removals (offsets are in terms
    /// of the ORIGINAL source's UTF-8 byte layout).
    /// </summary>
    /// <param name="source">The original source as a string. The
    /// dropper operates on UTF-8 byte ranges so callers can losslessly
    /// reconstruct the file.</param>
    /// <param name="relativeFilePath">Path stored in the log entries.
    /// Use a forward-slashed path relative to the migration root so
    /// logs are platform-portable.</param>
    public (string Migrated, List<LogEntry> Removals) Process(
        string source, string relativeFilePath)
    {
        var originalBytes = Encoding.UTF8.GetBytes(source);
        var removals = new List<LogEntry>();
        var sb = new StringBuilder(source.Length);

        int i = 0;
        int byteCursor = 0;
        while (i < source.Length)
        {
            var ch = source[i];
            if (ch != '\u00a7')
            {
                sb.Append(ch);
                byteCursor += Utf8ByteCount(ch);
                i++;
                continue;
            }

            // Scan the section keyword (letters and a possible leading slash).
            var kwStart = i + 1;
            var kwEnd = kwStart;
            if (kwEnd < source.Length && source[kwEnd] == '/')
            {
                kwEnd++;
            }
            while (kwEnd < source.Length &&
                   (char.IsLetter(source[kwEnd]) || char.IsDigit(source[kwEnd])))
            {
                kwEnd++;
            }
            var keyword = source.Substring(kwStart, kwEnd - kwStart);

            // Copy the "§Keyword" portion verbatim.
            for (int j = i; j < kwEnd; j++)
            {
                sb.Append(source[j]);
            }
            var sectionByteLen = Utf8ByteCount(source, i, kwEnd);
            byteCursor += sectionByteLen;
            i = kwEnd;

            if (!StructuralOpeners.Contains(keyword) || i >= source.Length || source[i] != '{')
            {
                // Not a candidate, or no attribute block follows. Move on.
                continue;
            }

            // Find the matching close brace (no nesting allowed in our
            // attribute grammar).
            int braceStart = i;
            int braceEnd = source.IndexOf('}', braceStart + 1);
            if (braceEnd < 0)
            {
                // Malformed source — leave it for the parser to diagnose.
                continue;
            }
            var inner = source.Substring(braceStart + 1, braceEnd - braceStart - 1);
            var firstColon = inner.IndexOf(':');
            var firstPositional = firstColon < 0 ? inner : inner.Substring(0, firstColon);

            if (!AttributeHelper.LooksLikeId(firstPositional))
            {
                // First value isn't an ID-shaped token. Pass through.
                CopyRange(source, braceStart, braceEnd + 1, sb);
                byteCursor += Utf8ByteCount(source, braceStart, braceEnd + 1);
                i = braceEnd + 1;
                continue;
            }

            // We are going to remove the ID. Two cases:
            //   (a) Closing tags (`/M`, `/F`, …) or any tag whose only
            //       positional is the ID — drop the whole `{id}` block,
            //       including the braces themselves.
            //   (b) Openers with extra positionals (`M`, `F`, `L`, …) —
            //       drop `id:` and keep `{rest}`.
            bool wholeBlock = !OpenersWithExtraPositionals.Contains(keyword) || firstColon < 0;
            int removedStartChar, removedEndChar;
            if (wholeBlock)
            {
                removedStartChar = braceStart;
                removedEndChar = braceEnd + 1;
            }
            else
            {
                // Drop "id:" — that's the chars from braceStart+1
                // through firstColon (inclusive of the colon).
                removedStartChar = braceStart + 1;
                removedEndChar = braceStart + 1 + firstColon + 1;
            }

            int removedByteStart = byteCursor + Utf8ByteCount(source, braceStart, removedStartChar);
            int removedByteLen = Utf8ByteCount(source, removedStartChar, removedEndChar);
            var removedBytes = new byte[removedByteLen];
            Array.Copy(originalBytes, removedByteStart, removedBytes, 0, removedByteLen);

            removals.Add(new LogEntry
            {
                File = relativeFilePath,
                RemovedOffset = removedByteStart,
                RemovedLength = removedByteLen,
                RemovedBytesBase64 = Convert.ToBase64String(removedBytes),
            });

            // Emit the preserved chars (those before the removal).
            CopyRange(source, braceStart, removedStartChar, sb);
            // Emit the chars after the removal up to and including '}'.
            CopyRange(source, removedEndChar, braceEnd + 1, sb);

            byteCursor += Utf8ByteCount(source, braceStart, braceEnd + 1);
            i = braceEnd + 1;
        }

        return (sb.ToString(), removals);
    }

    /// <summary>
    /// Convenience: serialise a <see cref="MigrationLog"/> to JSON in
    /// the schema consumed by the Python and C# verifiers.
    /// </summary>
    public static string SerializeLog(MigrationLog log)
    {
        return JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
    }

    /// <summary>
    /// Convenience: deserialise a previously-written log.
    /// </summary>
    public static MigrationLog DeserializeLog(string json)
    {
        return JsonSerializer.Deserialize<MigrationLog>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        }) ?? new MigrationLog();
    }

    private static void CopyRange(string source, int startInclusive, int endExclusive, StringBuilder sink)
    {
        for (int j = startInclusive; j < endExclusive; j++)
        {
            sink.Append(source[j]);
        }
    }

    private static int Utf8ByteCount(char ch)
    {
        if (ch < 0x0080) return 1;
        if (ch < 0x0800) return 2;
        if (char.IsHighSurrogate(ch)) return 4; // one half of a surrogate pair
        if (char.IsLowSurrogate(ch)) return 0;  // accounted for by high surrogate
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

    // Unused but available for callers who need a culture-explicit int parse.
    private static int ParseInt(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
