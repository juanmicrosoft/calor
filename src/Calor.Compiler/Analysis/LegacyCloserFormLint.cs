using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Opt-in lint (Calor0830): flag legacy structural closing tags
/// (<c>§/F</c>, <c>§/CL</c>, <c>§/L</c>, …) in source that should
/// otherwise use indent form. Indent form alone terminates every
/// structural block; closers that still carry payload
/// (<c>§/DO</c>, <c>§/PP</c>, <c>§/K</c>) and inline expression
/// closers (<c>§/C</c>, <c>§/T</c>, <c>§/NEW</c>, etc.) are not
/// flagged.
///
/// This is a SOURCE-level scanner (not AST) so it can be applied to
/// any <c>.calr</c> source without parsing — useful precisely because
/// closer form hard-errors (<c>Calor0830</c>) during parsing, which
/// prevents the AST-based <c>calor format</c> / <c>calor lint --fix</c>
/// paths from healing such files. Each <see cref="Finding"/> carries the
/// exact source range to delete (<see cref="Finding.RemovedOffset"/> /
/// <see cref="Finding.RemovedLength"/>); removing those ranges rewrites
/// the source into canonical indent form.
///
/// Wiring into the standard <c>calor lint</c> pipeline is intentionally
/// gated: callers must request the lint explicitly so repositories
/// still mid-migration do not see noisy diagnostics.
/// </summary>
public static class LegacyCloserFormLint
{
    /// <summary>
    /// Structural closers that indent form replaces. These should be
    /// removed entirely; the matching opener's indented body alone
    /// terminates the block at the next dedent.
    /// </summary>
    private static readonly HashSet<string> StructuralClosers = new(StringComparer.Ordinal)
    {
        // Top-level declarations
        "M", "F", "AF", "MT", "AMT",
        "CL", "IFACE",
        "EN",
        // Control flow
        "L", "WH", "I", "TR",
        "EACH", "EACHKV",
        "USE",
        // Unsafe / checked blocks
        "UNSAFE", "CHECKED", "UNCHECKED",
        // Class members
        "PROP", "CTOR", "OP", "IXER",
        // Match block (§/K case delimiter remains)
        "W", "SW",
    };

    /// <summary>
    /// One finding. Coordinates are 1-based line/column at the opening
    /// <c>§</c> of the closer.
    /// </summary>
    public sealed record Finding(
        string File,
        int Line,
        int Column,
        string Keyword,
        int RemovedOffset,
        int RemovedLength);

    /// <summary>
    /// Scan source text for legacy structural closers. Returns one
    /// finding per occurrence in source order.
    /// </summary>
    public static IReadOnlyList<Finding> Scan(string source, string filePath)
    {
        var findings = new List<Finding>();
        int line = 1, col = 1;

        for (int i = 0; i < source.Length; i++)
        {
            char ch = source[i];

            // Detect '§/' (section marker followed by slash).
            if (ch == '\u00A7' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int kwStart = i + 2;
                int kwEnd = kwStart;
                while (kwEnd < source.Length
                       && (char.IsLetter(source[kwEnd]) || char.IsDigit(source[kwEnd])))
                {
                    kwEnd++;
                }
                var kw = source.Substring(kwStart, kwEnd - kwStart);

                if (StructuralClosers.Contains(kw))
                {
                    // Compute the byte range we suggest deleting:
                    // the closer token itself plus any attached '{…}'
                    // payload (the legacy formatter sometimes echoed
                    // the opener id back into the closer).
                    int removalEnd = kwEnd;
                    if (removalEnd < source.Length && source[removalEnd] == '{')
                    {
                        int brace = 1;
                        removalEnd++;
                        while (removalEnd < source.Length && brace > 0)
                        {
                            if (source[removalEnd] == '{') brace++;
                            else if (source[removalEnd] == '}') brace--;
                            removalEnd++;
                        }
                    }

                    findings.Add(new Finding(
                        File: filePath,
                        Line: line,
                        Column: col,
                        Keyword: kw,
                        RemovedOffset: i,
                        RemovedLength: removalEnd - i));
                }
            }

            if (ch == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        return findings;
    }

    /// <summary>
    /// Like <see cref="Scan"/>, but returns ONLY the findings that are safe to
    /// delete from a <c>.calr</c> source file (used by <c>calor fix
    /// --heal-closers</c>). A raw <see cref="Scan"/> matches the literal text
    /// <c>§/…</c> anywhere — including inside a string literal (e.g.
    /// <c>§P "see §/F"</c>) or a <c>//</c> comment — so a bulk rewriter driven
    /// by raw findings would corrupt such content. The parser/LSP/MCP auto-heal
    /// never has this problem because it operates on the token/AST level.
    ///
    /// <para>This method restores that safety at the source level by tokenizing
    /// with the real <see cref="Lexer"/> and keeping only findings whose offset
    /// coincides with a genuine closer TOKEN. Closer text that lands inside a
    /// string/char literal is part of a <c>StrLiteral</c> token, and text inside
    /// a <c>//</c> comment is skipped entirely, so neither produces a closer
    /// token — such findings are dropped and the content is left untouched. If
    /// the source cannot be tokenized at all, no findings are returned (heal
    /// nothing rather than risk corruption).</para>
    /// </summary>
    public static IReadOnlyList<Finding> ScanForHeal(string source, string filePath)
    {
        var raw = Scan(source, filePath);
        if (raw.Count == 0)
        {
            return raw;
        }

        HashSet<int> closerTokenOffsets;
        try
        {
            var lexer = new Lexer(source, new DiagnosticBag());
            closerTokenOffsets = new HashSet<int>();
            foreach (var token in lexer.TokenizeAll())
            {
                // A genuine closer token's text is the source slice starting at
                // the '§' (e.g. "§/F"); its span start is that '§' offset, which
                // is exactly Finding.RemovedOffset.
                if (token.Text.StartsWith("\u00A7/", StringComparison.Ordinal))
                {
                    closerTokenOffsets.Add(token.Span.Start);
                }
            }
        }
        catch
        {
            // Untokenizable source — be conservative and heal nothing.
            return Array.Empty<Finding>();
        }

        var safe = new List<Finding>();
        foreach (var finding in raw)
        {
            if (closerTokenOffsets.Contains(finding.RemovedOffset))
            {
                safe.Add(finding);
            }
        }

        return safe;
    }
}
