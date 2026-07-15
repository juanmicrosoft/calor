using System.Text;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Formatting;

/// <summary>
/// Source-level, best-effort repair of the most common Calor authoring
/// mistakes ("write-path robustness", agent-native strategy Phase 1 item 5).
/// Unlike <see cref="CalorFormatter"/> — which needs a parseable file — the
/// healer works on raw text, so it can repair exactly the class of files the
/// AST formatter cannot touch:
///
///   1. Forbidden structural closers (<c>§/F</c>, <c>§/M</c>, …, Calor0830)
///      are stripped via <see cref="CloserHealMigrator"/>; a line left
///      whitespace-only by the strip is deleted.
///   2. Indentation is re-derived from the file's own relative nesting and
///      normalized to 2 spaces per level: tabs are expanded, 3-/4-space
///      levels collapse to 2, and misaligned dedents snap to the nearest
///      enclosing level.
///   3. Chain clauses (<c>§EI</c>/<c>§EL</c> for <c>§IF</c>, <c>§CA</c>/
///      <c>§FI</c> for <c>§TR</c>) are re-aligned to the column of the
///      opener they belong to, even when the author put them at body level
///      or dedented them too far.
///   4. Trailing whitespace is stripped and line endings normalize to LF.
///
/// Verbatim regions are preserved untouched: <c>§RAW…§/RAW</c> and
/// <c>§CSHARP…§/CSHARP</c> blocks, plus any line that starts inside an
/// unclosed bracket/brace (multi-line expressions, <c>§CS{…}</c>).
///
/// The transform is idempotent: healed output re-heals to itself, because
/// every emitted indent is exactly the canonical column its own structure
/// implies on re-reading.
///
/// <para><b>Healing is NOT semantics-preserving.</b> Re-anchoring a trailing
/// statement into a chain-clause body (see <see cref="Ambiguities"/>) is a
/// guess about the author's intended control flow. Callers must surface
/// ambiguous decisions and tell the author to review the healed output.</para>
/// </summary>
public sealed class SourceHealer
{
    /// <summary>Chain-clause tags and the opener tag each aligns with.</summary>
    private static readonly Dictionary<string, string> ChainClauseOpeners = new(StringComparer.Ordinal)
    {
        ["EI"] = "IF",
        ["EL"] = "IF",
        ["CA"] = "TR",
        ["FI"] = "TR",
    };

    private readonly List<HealAmbiguity> _ambiguities = new();

    /// <summary>
    /// Control-flow guesses made by the most recent <see cref="Heal"/> call.
    /// Healing is NOT semantics-preserving: when a chain clause (<c>§EI</c>/
    /// <c>§EL</c>/<c>§CA</c>/<c>§FI</c>) was written deeper than its opener,
    /// any following statement written at the clause's own column is
    /// ambiguous — it could belong to the clause body or be a sibling after
    /// the chain. The healer keeps it inside the clause body and records the
    /// decision here so callers can surface it for review.
    /// </summary>
    public IReadOnlyList<HealAmbiguity> Ambiguities => _ambiguities;

    /// <summary>Heal a Calor source text. Returns the healed text (may equal the input).</summary>
    public string Heal(string source)
    {
        _ambiguities.Clear();
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        bool endsWithNewline = source.EndsWith('\n');

        // Step 1 — strip forbidden structural closers at the source level.
        var (stripped, removals) = new CloserHealMigrator().Process(source, "heal");

        // The migrator removes closer text but keeps the line; delete lines
        // that were non-blank before the strip and whitespace-only after it.
        // Original 1-based line numbers ride along so ambiguity reports
        // reference the file as the author wrote it.
        var originalLines = SplitLines(source);
        var strippedLines = SplitLines(stripped);
        bool canDropEmptied = removals.Count > 0 && originalLines.Length == strippedLines.Length;
        var lines = new List<(string Text, int OriginalLine)>(strippedLines.Length);
        for (int i = 0; i < strippedLines.Length; i++)
        {
            if (canDropEmptied
                && string.IsNullOrWhiteSpace(strippedLines[i])
                && !string.IsNullOrWhiteSpace(originalLines[i]))
            {
                continue; // line emptied by the closer strip
            }
            lines.Add((strippedLines[i], i + 1));
        }

        // Step 2 — classify lines (verbatim regions, bracket continuations)
        // and normalize whitespace on structural lines.
        var infos = ClassifyLines(lines);

        // Step 3 — re-derive indentation from relative nesting.
        var healed = Relevel(infos, _ambiguities);

        var result = string.Join('\n', healed);
        result = result.TrimEnd('\r', '\n');
        if (endsWithNewline)
        {
            result += "\n";
        }
        return result;
    }

    private sealed class LineInfo
    {
        /// <summary>Line emitted exactly as-is (raw/interop content, bracket continuation).</summary>
        public bool Verbatim;
        public bool Blank;
        /// <summary>Indent width after tab expansion (structural lines only).</summary>
        public int RawIndent;
        /// <summary>Trimmed line content (no leading/trailing whitespace).</summary>
        public string Content = "";
        /// <summary>Original text (used when <see cref="Verbatim"/>).</summary>
        public string Original = "";
        /// <summary>Leading §-tag letters (e.g. "IF", "EI", "/C"), or null.</summary>
        public string? Tag;
        /// <summary>Raw indent of the next non-blank structural line, or -1.</summary>
        public int NextStructuralIndent = -1;
        /// <summary>1-based line number in the original (pre-strip) source.</summary>
        public int OriginalLine;
    }

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>
    /// Upper bound on how many lines a bracket continuation may span. A
    /// single unbalanced <c>{</c>/<c>(</c> (the canonical agent typo:
    /// <c>§F{f1:Main:pub ( ) -> void</c> with the header brace never closed)
    /// would otherwise mark every following line as continuation and make
    /// heal a silent no-op on the whole file. Genuine multi-line bracketed
    /// expressions are short; long C# payloads belong in §RAW/§CSHARP blocks.
    /// </summary>
    private const int MaxBracketContinuationLines = 10;

    private static LineInfo[] ClassifyLines(List<(string Text, int OriginalLine)> lines)
    {
        var infos = new LineInfo[lines.Count];
        bool inRawBlock = false;
        string rawEndMarker = "";
        int bracketDepth = 0;
        int continuationRun = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Text;
            var info = new LineInfo { Original = line, OriginalLine = lines[i].OriginalLine };
            infos[i] = info;

            if (inRawBlock)
            {
                info.Verbatim = true;
                if (line.Contains(rawEndMarker, StringComparison.Ordinal))
                {
                    inRawBlock = false;
                }
                continue;
            }

            if (bracketDepth > 0)
            {
                // A continuation may not cross a line that starts a §-tag,
                // and may not exceed MaxBracketContinuationLines: past either
                // bound the open bracket is assumed to be an authoring typo
                // (never closed), and the line is treated as structural again.
                bool startsStructuralTag = line.TrimStart(' ', '\t').StartsWith('§');
                if (!startsStructuralTag && continuationRun < MaxBracketContinuationLines)
                {
                    // Continuation of a multi-line bracketed expression: the
                    // parser ignores its indentation, and its content may be a
                    // §CS{…} C# payload — pass through untouched (except CR).
                    info.Verbatim = true;
                    info.Original = line.TrimEnd('\r');
                    bracketDepth = Math.Max(0, bracketDepth + BracketDelta(line));
                    continuationRun = bracketDepth > 0 ? continuationRun + 1 : 0;
                    continue;
                }
                bracketDepth = 0;
                continuationRun = 0;
            }

            var trimmedEnd = line.TrimEnd();
            if (trimmedEnd.Length == 0)
            {
                info.Blank = true;
                continue;
            }

            // Expand leading tabs and measure the indent. Tabs count as 4
            // columns for MEASUREMENT only: levels are re-derived and emitted
            // at 2 spaces each regardless, and width 4 orders tab-indented
            // lines correctly against both 2- and 4-space neighbours in
            // files that mix conventions.
            int ws = 0;
            var indent = new StringBuilder();
            while (ws < trimmedEnd.Length && (trimmedEnd[ws] == ' ' || trimmedEnd[ws] == '\t'))
            {
                indent.Append(trimmedEnd[ws] == '\t' ? "    " : " ");
                ws++;
            }

            info.RawIndent = indent.Length;
            info.Content = trimmedEnd[ws..];
            info.Tag = LeadingTag(info.Content);

            bracketDepth = Math.Max(0, bracketDepth + BracketDelta(trimmedEnd));

            // Verbatim block openers: content until the end marker must not
            // be touched. (A same-line end marker needs no mode change.)
            if (ContainsMarker(info.Content, "§RAW") && !info.Content.Contains("§/RAW", StringComparison.Ordinal))
            {
                inRawBlock = true;
                rawEndMarker = "§/RAW";
            }
            else if (ContainsMarker(info.Content, "§CSHARP") && !info.Content.Contains("§/CSHARP", StringComparison.Ordinal))
            {
                inRawBlock = true;
                rawEndMarker = "§/CSHARP";
            }
        }

        // Opener detection input: raw indent of the next structural line.
        int next = -1;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            infos[i].NextStructuralIndent = next;
            if (!infos[i].Verbatim && !infos[i].Blank)
            {
                next = infos[i].RawIndent;
            }
        }

        return infos;
    }

    /// <summary>An open block on the relevel stack.</summary>
    private sealed class OpenBlock
    {
        /// <summary>Raw (as-written) indent of the opener line.</summary>
        public int OpenRaw;
        /// <summary>
        /// Raw indent used for pop comparisons: a line stays inside this
        /// block while written deeper than <c>AnchorRaw</c>. Normally equals
        /// <see cref="OpenRaw"/>; a chain clause written at body level moves
        /// it just below the clause's own indent so the clause body (written
        /// at the clause's level) still nests inside the block.
        /// </summary>
        public int AnchorRaw;
        /// <summary>Canonical level of the opener line (0 = top level).</summary>
        public int Level;
        public string Tag = "";
        /// <summary>
        /// Written indent of the chain clause that re-anchored this block
        /// deeper (<see cref="AnchorRaw"/> ≠ <see cref="OpenRaw"/>), or -1.
        /// A following statement written at exactly this column is an
        /// ambiguous re-anchoring decision — see <see cref="Ambiguities"/>.
        /// </summary>
        public int ClauseRaw = -1;
        /// <summary>Original line of that clause (valid when ClauseRaw ≥ 0).</summary>
        public int ClauseLine;
        /// <summary>Tag of that clause, e.g. "EI" (valid when ClauseRaw ≥ 0).</summary>
        public string ClauseTag = "";
    }

    private static List<string> Relevel(LineInfo[] infos, List<HealAmbiguity> ambiguities)
    {
        var output = new List<string>(infos.Length);
        var stack = new List<OpenBlock>();

        foreach (var info in infos)
        {
            if (info.Verbatim)
            {
                output.Add(info.Original);
                continue;
            }
            if (info.Blank)
            {
                output.Add("");
                continue;
            }

            int r = info.RawIndent;
            int level;

            if (info.Tag != null && ChainClauseOpeners.TryGetValue(info.Tag, out var openerTag)
                && TryFindChainOpener(stack, openerTag, r, out int openerIndex))
            {
                // Chain clause: align with its opener's column, popping any
                // blocks opened inside the preceding clause body. When the
                // clause was written DEEPER than its opener (the classic
                // body-level §EI mistake), re-anchor the block just below
                // the clause's written indent so the clause body — written
                // at the clause's own level — still nests inside the block.
                stack.RemoveRange(openerIndex + 1, stack.Count - openerIndex - 1);
                var opener = stack[openerIndex];
                level = opener.Level;
                if (r > opener.OpenRaw)
                {
                    opener.AnchorRaw = r - 1;
                    opener.ClauseRaw = r;
                    opener.ClauseLine = info.OriginalLine;
                    opener.ClauseTag = info.Tag!;
                }
                else
                {
                    opener.AnchorRaw = opener.OpenRaw;
                    opener.ClauseRaw = -1;
                }
            }
            else
            {
                while (stack.Count > 0 && r <= stack[^1].AnchorRaw)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                level = stack.Count;

                // Ambiguous re-anchoring: this statement is written at the
                // same column as a chain clause that we re-anchored deeper.
                // Keeping it inside the clause body is a control-flow GUESS —
                // it could equally be a sibling after the chain. Record the
                // decision so the CLI can tell the author to review it.
                if (stack.Count > 0 && stack[^1].ClauseRaw == r)
                {
                    ambiguities.Add(new HealAmbiguity(info.OriginalLine,
                        $"statement is at the same column as the §{stack[^1].ClauseTag} on line " +
                        $"{stack[^1].ClauseLine}; heal kept it inside that clause's body, but it may have " +
                        "been intended as a statement after the chain — review the healed control flow"));
                }

                // Empirical opener detection: a structural line followed by a
                // deeper structural line opens a block. (Arrow-form one-liners
                // and plain statements are never followed by deeper lines in
                // intent, so relative indentation is the most robust signal
                // on broken input.)
                if (info.NextStructuralIndent > r)
                {
                    stack.Add(new OpenBlock { OpenRaw = r, AnchorRaw = r, Level = level, Tag = info.Tag ?? "" });
                }
            }

            output.Add(new string(' ', 2 * level) + info.Content);
        }

        return output;
    }

    /// <summary>
    /// Finds the chain-clause opener (e.g. the <c>§IF</c> an <c>§EI</c>
    /// belongs to): the stack entry with the matching tag whose written
    /// indent is closest to the clause's written indent; ties prefer the
    /// innermost candidate.
    /// </summary>
    private static bool TryFindChainOpener(List<OpenBlock> stack, string openerTag, int rawIndent, out int index)
    {
        index = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < stack.Count; i++)
        {
            if (!string.Equals(stack[i].Tag, openerTag, StringComparison.Ordinal))
            {
                continue;
            }
            int distance = Math.Abs(stack[i].OpenRaw - rawIndent);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                index = i;
            }
        }
        return index >= 0;
    }

    /// <summary>Extracts the leading §-tag letters of a line ("IF", "EI", "/C", …), or null.</summary>
    private static string? LeadingTag(string content)
    {
        if (content.Length < 2 || content[0] != '§')
        {
            return null;
        }
        int i = 1;
        if (i < content.Length && content[i] == '/')
        {
            i++;
        }
        int start = 1;
        while (i < content.Length && (char.IsAsciiLetterUpper(content[i]) || char.IsAsciiDigit(content[i])))
        {
            i++;
        }
        return i > start ? content[start..i] : null;
    }

    /// <summary>True when the line begins with the given marker followed by a non-tag character.</summary>
    private static bool ContainsMarker(string content, string marker)
    {
        int idx = content.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }
        int after = idx + marker.Length;
        return after >= content.Length
            || !(char.IsAsciiLetterUpper(content[after]) || char.IsAsciiDigit(content[after]));
    }

    /// <summary>
    /// Net bracket/brace/paren depth change of a line, skipping string
    /// literals, char literals, and <c>//</c> comments — mirrors the lexer's
    /// implicit-continuation rules closely enough for healing purposes.
    /// </summary>
    private static int BracketDelta(string line)
    {
        int delta = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            switch (c)
            {
                case '"':
                    i++;
                    while (i < line.Length && line[i] != '"')
                    {
                        if (line[i] == '\\')
                        {
                            i++;
                        }
                        i++;
                    }
                    break;
                case '\'':
                    // Skip a short char literal ('x' or '\n'); a lone quote
                    // is left alone.
                    int close = -1;
                    for (int j = i + 1; j < line.Length && j <= i + 3; j++)
                    {
                        if (line[j] == '\'')
                        {
                            close = j;
                            break;
                        }
                    }
                    if (close > i)
                    {
                        i = close;
                    }
                    break;
                case '/':
                    if (i + 1 < line.Length && line[i + 1] == '/')
                    {
                        return delta; // line comment: ignore the rest
                    }
                    break;
                case '(':
                case '[':
                case '{':
                    delta++;
                    break;
                case ')':
                case ']':
                case '}':
                    delta--;
                    break;
            }
        }
        return delta;
    }
}

/// <summary>
/// A control-flow guess made while healing. Line is the 1-based line number
/// in the original source; Message explains the decision the healer took and
/// why it is ambiguous.
/// </summary>
public readonly record struct HealAmbiguity(int Line, string Message);
