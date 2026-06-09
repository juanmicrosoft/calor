using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Opt-in lint (Calor0820): flag legacy <c>{id:…}</c> blocks on
/// structural openers that the Phase 1 migrator can safely strip.
///
/// This is a SOURCE-level scanner (not AST) — it intentionally matches
/// exactly what <see cref="StructuralIdDropper"/> would rewrite, so a
/// developer can run the diagnostic to discover migration opportunities
/// and then apply <c>calor fix --drop-structural-ids</c> for the
/// machine-applicable fix.
///
/// Wiring into the standard <c>calor lint</c> pipeline is intentionally
/// gated: the lint surfaces only when callers ask for it. This avoids
/// noisy warnings in repositories that have not yet adopted v6 syntax.
/// </summary>
public static class LegacyStructuralIdLint
{
    /// <summary>
    /// One finding. Coordinates are 1-based line/column at the opening
    /// section marker (<c>§</c>) so they line up with the diagnostic
    /// addresses described in RFC §8.3.
    /// </summary>
    public sealed record Finding(
        string File,
        int Line,
        int Column,
        string Keyword,
        string IdValue,
        int RemovedOffset,
        int RemovedLength);

    /// <summary>
    /// Scan source text for legacy structural-ID blocks. Returns one
    /// finding per occurrence in source order.
    /// </summary>
    public static IReadOnlyList<Finding> Scan(string source, string filePath)
    {
        var dropper = new StructuralIdDropper();
        var (_, removals) = dropper.Process(source, filePath);
        if (removals.Count == 0)
        {
            return Array.Empty<Finding>();
        }

        // Compute byte→(line,col) using the original source.
        var bytes = System.Text.Encoding.UTF8.GetBytes(source);
        var findings = new List<Finding>(removals.Count);
        foreach (var entry in removals)
        {
            var (line, col) = ByteOffsetToLineCol(bytes, entry.RemovedOffset);
            // Walk back to the nearest preceding § to find the keyword.
            var (kw, kwOffset) = FindOpenerBefore(source, entry.RemovedOffset);
            findings.Add(new Finding(
                File: filePath,
                Line: line,
                Column: col,
                Keyword: kw,
                IdValue: DecodeRemoved(entry.RemovedBytesBase64, entry.RemovedLength),
                RemovedOffset: entry.RemovedOffset,
                RemovedLength: entry.RemovedLength));
        }
        return findings;
    }

    private static (int Line, int Column) ByteOffsetToLineCol(byte[] bytes, int offset)
    {
        int line = 1, col = 1;
        for (int i = 0; i < offset && i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                line++;
                col = 1;
            }
            else if ((bytes[i] & 0xC0) != 0x80)
            {
                // Count one column per leading byte of a UTF-8 sequence.
                col++;
            }
        }
        return (line, col);
    }

    private static (string Keyword, int Offset) FindOpenerBefore(string source, int byteOffset)
    {
        // We have the byte offset of the removal, not the char offset.
        // Walk character-wise from the start, tracking byte cursor, to
        // find the §Keyword{ preceding it.
        int cursor = 0;
        int lastSectionOffset = -1;
        string lastKw = "";
        for (int i = 0; i < source.Length; i++)
        {
            int b = Utf8Len(source[i]);
            if (cursor + b > byteOffset) break;
            if (source[i] == '\u00a7')
            {
                int kwStart = i + 1;
                int kwEnd = kwStart;
                if (kwEnd < source.Length && source[kwEnd] == '/') kwEnd++;
                while (kwEnd < source.Length && (char.IsLetter(source[kwEnd]) || char.IsDigit(source[kwEnd])))
                {
                    kwEnd++;
                }
                lastSectionOffset = cursor;
                lastKw = source.Substring(kwStart, kwEnd - kwStart);
            }
            cursor += b;
        }
        return (lastKw, lastSectionOffset);
    }

    private static string DecodeRemoved(string base64, int expectedLen)
    {
        try
        {
            var raw = Convert.FromBase64String(base64);
            return System.Text.Encoding.UTF8.GetString(raw);
        }
        catch
        {
            return $"<{expectedLen} bytes>";
        }
    }

    private static int Utf8Len(char ch)
    {
        if (ch < 0x0080) return 1;
        if (ch < 0x0800) return 2;
        if (char.IsHighSurrogate(ch)) return 4;
        if (char.IsLowSurrogate(ch)) return 0;
        return 3;
    }
}
