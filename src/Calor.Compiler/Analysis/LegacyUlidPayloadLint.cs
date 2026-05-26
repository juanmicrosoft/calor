using System.Text.RegularExpressions;
using Calor.Compiler.Ids;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Opt-in lint (Calor0821): flag declarations that still use 26-char
/// ULID payloads which the Phase 2 migrator
/// (<c>calor fix --compact-ids</c>) can rewrite to 12-char
/// Crockford-lowercase compact IDs.
///
/// This is a SOURCE-level scanner (not AST). It pairs every recognised
/// prefix (<c>m_</c>, <c>f_</c>, <c>c_</c>, <c>i_</c>, <c>p_</c>,
/// <c>mt_</c>, <c>ctor_</c>, <c>e_</c>, <c>op_</c>) with a 26-char
/// Crockford-uppercase suffix (the ULID alphabet). Findings are
/// emitted in source order with the byte range to replace, which is
/// also what the migrator's mapping log records.
///
/// The diagnostic is intentionally opt-in: wiring into the standard
/// <c>calor lint</c> pipeline is left to callers so repositories that
/// have not yet adopted v6 syntax are not flooded with noise.
/// </summary>
public static class LegacyUlidPayloadLint
{
    /// <summary>
    /// Crockford ULID alphabet (uppercase; <c>I</c>, <c>L</c>, <c>O</c>,
    /// <c>U</c> excluded). Used to bound the lint's pattern so it does
    /// not match arbitrary 26-char identifiers.
    /// </summary>
    private const string UlidAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// One legacy-ULID finding. Coordinates are 1-based line/column at
    /// the start of the ID (its prefix character), matching the
    /// diagnostic-address scheme in RFC §8.3.
    /// </summary>
    public sealed record Finding(
        string File,
        int Line,
        int Column,
        string Prefix,
        string Payload,
        int Offset,
        int Length);

    private static readonly Regex Pattern = BuildPattern();

    /// <summary>
    /// Scan source text for legacy ULID payloads. Returns one finding
    /// per match, in source order.
    /// </summary>
    public static IReadOnlyList<Finding> Scan(string source, string filePath)
    {
        if (string.IsNullOrEmpty(source))
        {
            return Array.Empty<Finding>();
        }

        var findings = new List<Finding>();
        foreach (Match m in Pattern.Matches(source))
        {
            // Word-boundary check on the right edge: reject matches that
            // are a strict prefix of a longer identifier (e.g.
            // "m_…XYZA0" with a trailing alphanumeric character).
            int after = m.Index + m.Length;
            if (after < source.Length)
            {
                char next = source[after];
                if (char.IsLetterOrDigit(next) || next == '_')
                {
                    continue;
                }
            }

            // Word-boundary on the left edge: only accept matches that
            // begin at a non-identifier boundary so we don't catch
            // longer identifiers ending in our prefix.
            if (m.Index > 0)
            {
                char prev = source[m.Index - 1];
                if (char.IsLetterOrDigit(prev) || prev == '_')
                {
                    continue;
                }
            }

            var prefix = m.Groups["prefix"].Value;
            var payload = m.Groups["payload"].Value;
            var (line, col) = OffsetToLineCol(source, m.Index);
            findings.Add(new Finding(
                File: filePath,
                Line: line,
                Column: col,
                Prefix: prefix,
                Payload: payload,
                Offset: m.Index,
                Length: m.Length));
        }
        return findings;
    }

    private static Regex BuildPattern()
    {
        // Prefix alternation in longest-first order, matching
        // IdGenerator.GetKindFromId so we never split "ctor_" as "c"
        // followed by "tor_…".
        var prefixes = new[]
        {
            IdGenerator.ConstructorPrefix,
            IdGenerator.OperatorOverloadPrefix,
            IdGenerator.MethodPrefix,
            IdGenerator.ModulePrefix,
            IdGenerator.FunctionPrefix,
            IdGenerator.ClassPrefix,
            IdGenerator.InterfacePrefix,
            IdGenerator.PropertyPrefix,
            IdGenerator.EnumPrefix,
        };
        var prefixAlternation = string.Join("|", prefixes.Select(Regex.Escape));
        var payload = $"[{UlidAlphabet}]{{26}}";
        var pattern = $@"(?<prefix>{prefixAlternation})(?<payload>{payload})";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static (int Line, int Column) OffsetToLineCol(string source, int offset)
    {
        int line = 1, col = 1;
        for (int i = 0; i < offset && i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }
        return (line, col);
    }
}
