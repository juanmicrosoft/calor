using System.Text;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Migration;

/// <summary>
/// Rewrites <c>.calr</c> source files to apply the v0.6.x call-closer
/// elision in-place, matching what <see cref="CalorEmitter"/> now produces
/// for newly-generated code.
///
/// <para>Supported transforms:</para>
/// <list type="bullet">
///   <item><description>Zero-arg, same line: <c>§C{X} §/C</c> →
///   <c>§C{X}</c> (v0.6.1, both stmt and expr context).</description></item>
///   <item><description>One-arg positional, same line:
///   <c>§C{X} §A arg §/C</c> → <c>§C{X} arg</c>
///   (v0.6.2 stmt-context / v0.6.3 expr-context).</description></item>
/// </list>
///
/// <para>Design notes (from the rubber-duck review of the v0.6.3 plan):</para>
/// <list type="bullet">
///   <item><description><b>Token-precise edits.</b> Removals are computed
///   from token spans on the original source so the migration is fully
///   reversible and preserves comments, whitespace, and IDs outside the
///   call.</description></item>
///   <item><description><b>Conservative same-line gate.</b> One-arg
///   elision only fires when the entire <c>§C{X} §A arg §/C</c> is on a
///   single line. Multi-line forms are skipped because removing
///   <c>§A</c> across a newline would let the parser absorb the next
///   sibling statement as an argument (Parser.cs same-line discipline
///   for inline calls).</description></item>
///   <item><description><b>Canonical-emit verification.</b> After
///   computing edits, the migrator re-parses the candidate output and
///   emits both the original and the new AST through
///   <see cref="CalorEmitter"/>. If the two canonical forms differ, the
///   entire file's edits are discarded. This catches every semantic
///   divergence (e.g. a following <c>§+ y</c> that the parser would now
///   absorb into the call's arg expression).</description></item>
/// </list>
///
/// <para>The <see cref="StructuralIdDropper.LogEntry"/> shape is reused
/// so the same <c>--revert --log &lt;file&gt;</c> round-trip semantics
/// used by <c>calor fix --drop-structural-ids</c> apply to this
/// migrator as well.</para>
/// </summary>
public sealed class CallCloserElider
{
    /// <summary>
    /// Result of processing a single file.
    /// </summary>
    public sealed record FileResult(
        string MigratedSource,
        List<StructuralIdDropper.LogEntry> Removals,
        int CallsElided,
        bool Skipped,
        string? SkipReason);

    /// <summary>
    /// Processes a single source file. On any error or verification
    /// failure, returns the original source unchanged with
    /// <see cref="FileResult.Skipped"/> = true.
    /// </summary>
    public FileResult Process(string source, string relativeFilePath)
    {
        // Tokenize once (raw, no Indent/Dedent).
        var lexDiags = new DiagnosticBag();
        var tokens = new Lexer(source, lexDiags).TokenizeAll().ToList();
        if (lexDiags.HasErrors)
        {
            return new FileResult(source, new(), 0, true, "lexer errors");
        }

        // Plan removals by scanning tokens for §C openers.
        var removals = new List<(int Start, int End)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != TokenKind.Call)
            {
                continue;
            }
            TryPlanCallElision(source, tokens, i, removals);
        }

        if (removals.Count == 0)
        {
            return new FileResult(source, new(), 0, false, null);
        }

        // Sort removals descending by start offset and check for overlaps.
        removals.Sort((a, b) => b.Start.CompareTo(a.Start));
        for (var i = 1; i < removals.Count; i++)
        {
            if (removals[i].End > removals[i - 1].Start)
            {
                return new FileResult(source, new(), 0, true, "overlapping removals");
            }
        }

        var migrated = ApplyRemovals(source, removals);

        // Canonical-emit verification: parse original + migrated and
        // compare their CalorEmitter output. If they differ, the edits
        // changed program semantics — drop them.
        if (!CanonicalEmitMatches(source, migrated, out var reason))
        {
            return new FileResult(source, new(), 0, true, reason);
        }

        // Build log entries in ascending offset order so the revert path
        // (which reuses StructuralIdDropper.ReinsertRemovals) works.
        var originalBytes = Encoding.UTF8.GetBytes(source);
        var logEntries = new List<StructuralIdDropper.LogEntry>(removals.Count);
        foreach (var (startChar, endChar) in removals.OrderBy(r => r.Start))
        {
            var byteStart = Utf8ByteCount(source, 0, startChar);
            var byteLen = Utf8ByteCount(source, startChar, endChar);
            var removedBytes = new byte[byteLen];
            Array.Copy(originalBytes, byteStart, removedBytes, 0, byteLen);
            logEntries.Add(new StructuralIdDropper.LogEntry
            {
                File = relativeFilePath,
                RemovedOffset = byteStart,
                RemovedLength = byteLen,
                RemovedBytesBase64 = Convert.ToBase64String(removedBytes),
            });
        }

        return new FileResult(migrated, logEntries, removals.Count, false, null);
    }

    private static void TryPlanCallElision(
        string source,
        List<Token> tokens,
        int openerIdx,
        List<(int Start, int End)> removals)
    {
        // Find matching §/C, tracking nested §C/§/C depth.
        var endCallIdx = FindMatchingEndCall(tokens, openerIdx);
        if (endCallIdx == null)
        {
            return; // already elided or malformed
        }

        var openerLine = tokens[openerIdx].Span.Line;
        var endTok = tokens[endCallIdx.Value];

        // Skip multi-line calls. Same-line discipline is required for the
        // parser to accept the elided form correctly (Parser.cs inline-arg
        // path), and avoids touching deliberately-laid-out multi-line
        // calls.
        if (endTok.Span.Line != openerLine)
        {
            return;
        }

        // Collect §A tokens at depth 0 within (openerIdx, endCallIdx).
        var argTokenIndices = new List<int>();
        var depth = 0;
        for (var i = openerIdx + 1; i < endCallIdx.Value; i++)
        {
            var k = tokens[i].Kind;
            if (IsCallOpener(k))
            {
                depth++;
            }
            else if (IsCallCloser(k))
            {
                if (depth > 0) depth--;
            }
            else if (k == TokenKind.Arg && depth == 0)
            {
                argTokenIndices.Add(i);
            }
        }

        // ZERO-ARG: source has no §A and AST will have no Arguments.
        if (argTokenIndices.Count == 0)
        {
            // Remove from end of previous meaningful token through §/C.
            var prevEnd = PreviousMeaningfulTokenEnd(tokens, endCallIdx.Value);
            if (prevEnd < 0)
            {
                return;
            }
            removals.Add((prevEnd, endTok.Span.End));
            return;
        }

        // ONE-ARG POSITIONAL: exactly one §A at depth 0 + same-line + safe.
        if (argTokenIndices.Count != 1)
        {
            return;
        }
        var argTokenIdx = argTokenIndices[0];
        var argTok = tokens[argTokenIdx];
        if (argTok.Span.Line != openerLine)
        {
            return; // §A spilled to another line
        }

        // The §A must not be followed by '[' (named-arg form: §A[name] x).
        var nextIdx = NextNonTriviaIndex(tokens, argTokenIdx + 1, endCallIdx.Value);
        if (nextIdx < 0)
        {
            return;
        }
        var nextTok = tokens[nextIdx];
        if (nextTok.Kind == TokenKind.OpenBracket)
        {
            return; // named-arg syntax
        }
        // ref/out/in argument modifiers — leave alone.
        if (IsArgModifierKeyword(nextTok))
        {
            return;
        }
        // The arg's first character must match the parser's safe inline-
        // expression starter set (mirror of
        // CalorEmitter.StartsWithExpressionStarter).
        if (!IsSafeExpressionStarter(source, nextTok.Span.Start))
        {
            return;
        }
        if (nextTok.Span.Line != openerLine)
        {
            return;
        }

        // The last meaningful token strictly before §/C is the end of the
        // arg expression. It must also be on the opener's line.
        var lastArgTokIdx = PreviousMeaningfulTokenIndex(tokens, endCallIdx.Value);
        if (lastArgTokIdx < nextIdx)
        {
            return;
        }
        var lastArgTok = tokens[lastArgTokIdx];
        if (lastArgTok.Span.Line != openerLine)
        {
            return;
        }

        // Remove "§A " (the §A token + any trailing whitespace up to the
        // first arg token).
        removals.Add((argTok.Span.Start, nextTok.Span.Start));
        // Remove " §/C" (from end of last arg token through §/C end).
        removals.Add((lastArgTok.Span.End, endTok.Span.End));
    }

    /// <summary>
    /// Returns the index of the §/C that matches the §C at
    /// <paramref name="openerIdx"/>, or null if not found at depth 0
    /// before end-of-tokens.
    /// </summary>
    private static int? FindMatchingEndCall(List<Token> tokens, int openerIdx)
    {
        var depth = 1;
        for (var i = openerIdx + 1; i < tokens.Count; i++)
        {
            var k = tokens[i].Kind;
            if (k == TokenKind.Call)
            {
                depth++;
            }
            else if (k == TokenKind.EndCall)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }
        return null;
    }

    private static bool IsCallOpener(TokenKind k) => k == TokenKind.Call;

    private static bool IsCallCloser(TokenKind k) => k == TokenKind.EndCall;

    private static int NextNonTriviaIndex(List<Token> tokens, int from, int upperExclusive)
    {
        for (var i = from; i < upperExclusive; i++)
        {
            if (!IsTrivia(tokens[i].Kind))
            {
                return i;
            }
        }
        return -1;
    }

    private static int PreviousMeaningfulTokenIndex(List<Token> tokens, int beforeIdx)
    {
        for (var i = beforeIdx - 1; i >= 0; i--)
        {
            if (!IsTrivia(tokens[i].Kind))
            {
                return i;
            }
        }
        return -1;
    }

    private static int PreviousMeaningfulTokenEnd(List<Token> tokens, int beforeIdx)
    {
        var idx = PreviousMeaningfulTokenIndex(tokens, beforeIdx);
        return idx < 0 ? -1 : tokens[idx].Span.End;
    }

    private static bool IsTrivia(TokenKind k)
        => k == TokenKind.Indent || k == TokenKind.Dedent || k == TokenKind.Newline;

    private static bool IsArgModifierKeyword(Token t)
    {
        if (t.Kind != TokenKind.Identifier)
        {
            return false;
        }
        return t.Text is "ref" or "out" or "in";
    }

    /// <summary>
    /// Mirror of <c>CalorEmitter.StartsWithExpressionStarter</c>: the
    /// parser's inline-arg path only accepts these starter characters,
    /// so the migrator restricts elision to args that begin with one.
    /// </summary>
    private static bool IsSafeExpressionStarter(string source, int offset)
    {
        if (offset < 0 || offset >= source.Length)
        {
            return false;
        }
        var c = source[offset];
        return c == '§'
            || char.IsLetter(c) || c == '_'
            || char.IsDigit(c)
            || c == '(' || c == '"' || c == '@' || c == '#';
    }

    private static string ApplyRemovals(string source, List<(int Start, int End)> removalsDesc)
    {
        // Walk source from offset 0 to length, skipping each removed
        // range. removalsDesc is sorted descending by Start; iterate it
        // in reverse so we can scan ascending.
        var sb = new StringBuilder(source.Length);
        var cursor = 0;
        for (var ri = removalsDesc.Count - 1; ri >= 0; ri--)
        {
            var (start, end) = removalsDesc[ri];
            if (start > cursor)
            {
                sb.Append(source, cursor, start - cursor);
            }
            cursor = end;
        }
        if (cursor < source.Length)
        {
            sb.Append(source, cursor, source.Length - cursor);
        }
        return sb.ToString();
    }

    private static int Utf8ByteCount(string s, int startChar, int endCharExclusive)
        => Encoding.UTF8.GetByteCount(s.AsSpan(startChar, endCharExclusive - startChar));

    /// <summary>
    /// Parses both the original and migrated source, emits both through
    /// <see cref="CalorEmitter"/>, and returns true iff the canonical
    /// forms are identical. This is the migrator's safety net against
    /// edits that change program semantics (e.g. accidentally merging a
    /// trailing expression into a call's arg list).
    /// </summary>
    private static bool CanonicalEmitMatches(string original, string migrated, out string? reason)
    {
        reason = null;

        var origDiags = new DiagnosticBag();
        var origTokens = new Lexer(original, origDiags).TokenizeAllForParser();
        if (origDiags.HasErrors)
        {
            reason = "original source has lex errors";
            return false;
        }
        var origAst = new Parser(origTokens, origDiags).Parse();
        if (origDiags.HasErrors || origAst == null)
        {
            reason = "original source has parse errors";
            return false;
        }

        var newDiags = new DiagnosticBag();
        var newTokens = new Lexer(migrated, newDiags).TokenizeAllForParser();
        if (newDiags.HasErrors)
        {
            reason = "post-edit lex failed";
            return false;
        }
        var newAst = new Parser(newTokens, newDiags).Parse();
        if (newDiags.HasErrors || newAst == null)
        {
            reason = "post-edit parse failed";
            return false;
        }

        var origCanonical = new CalorEmitter().Emit(origAst);
        var newCanonical = new CalorEmitter().Emit(newAst);
        if (!string.Equals(origCanonical, newCanonical, StringComparison.Ordinal))
        {
            reason = "post-edit canonical form diverged";
            return false;
        }
        return true;
    }

    public static string SerializeLog(StructuralIdDropper.MigrationLog log)
        => JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

    public static StructuralIdDropper.MigrationLog DeserializeLog(string json)
        => JsonSerializer.Deserialize<StructuralIdDropper.MigrationLog>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        }) ?? new StructuralIdDropper.MigrationLog();
}
