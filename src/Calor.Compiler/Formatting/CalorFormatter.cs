using System.Text.RegularExpressions;
using Calor.Compiler.Ast;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Formatting;

/// <summary>
/// Formats a Calor AST back to canonical indent-only Calor source.
///
/// Phase 4b: the old hand-written closer-form formatter (1004 lines, ~33
/// `§/X{…}` emissions, no indentation, no blank lines, abbreviated IDs)
/// was replaced with a thin adapter over <see cref="CalorEmitter"/> — the
/// production C#→Calor migration emitter, which has emitted indent form
/// since Phase 3. Keeping two emitters in lockstep was the primary risk
/// Phase 4 left on the table: any `calor format` run on a Phase 4 fixture
/// would re-introduce closers and undo the migration.
///
/// What this class still adds on top of <c>CalorEmitter</c>:
///
///   1. <see cref="AbbreviateId"/> post-pass on every <c>§X{…}</c> opener,
///      so production IDs like <c>m001</c> compact to <c>m1</c>, <c>for1</c>
///      to <c>l1</c>, etc. — the lint rule asserted in
///      <see cref="LintRegressionTests.Lint_IdAbbreviation_DetectsExpectedIssues"/>
///      and the corresponding fix path.
///
///   2. Final trailing-newline trim, so `calor format`'s "would change"
///      / "rewrote" diff isn't dominated by a phantom blank tail.
///
/// The adapter is intentionally narrow: <see cref="CalorEmitter"/> owns
/// node-by-node output; this class owns the two opinionated transforms
/// specific to <c>calor format</c>'s contract with users / agents.
/// </summary>
public sealed class CalorFormatter
{
    private static readonly Dictionary<string, string> PrefixMappings = new()
    {
        { "for", "l" },
        { "if", "i" },
        { "while", "w" },
        { "do", "d" },
    };

    private static readonly Regex StripLeadingZeros = new(@"^([a-zA-Z]+)0*(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Opener / closer ID block: matches <c>§X{id:…}</c> or <c>§/X{id}</c>
    /// where the id is the first colon-delimited segment. Captures:
    ///   1 = full tag prefix up to the opening brace (e.g. <c>§F{</c>)
    ///   2 = the id token itself
    ///   3 = remainder of the brace contents (including the leading colon
    ///       if any, and the closing brace)
    /// Designed to be safe to apply line-by-line: it deliberately excludes
    /// <c>{</c> and <c>}</c> from the id and remainder, so nested braces
    /// (like those in interpolated strings) are not chewed up.
    /// </summary>
    private static readonly Regex TagWithId = new(
        @"(§/?[A-Z]+\{)([A-Za-z][A-Za-z0-9_]*)([:}][^\r\n]*?)",
        RegexOptions.Compiled);

    /// <summary>
    /// Format a module AST to canonical Calor source.
    /// </summary>
    public string Format(ModuleNode module)
    {
        var emitter = new CalorEmitter();
        var raw = emitter.Emit(module);
        var abbreviated = AbbreviateIdsInTags(raw);
        return abbreviated.TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Abbreviate IDs by stripping leading zeros from numeric suffix and
    /// mapping verbose loop / branch prefixes to compact letters.
    /// Examples: m001→m1, f001→f1, for1→l1, if1→i1, while1→w1, do1→d1.
    /// </summary>
    internal static string AbbreviateId(string id)
    {
        foreach (var (oldPrefix, newPrefix) in PrefixMappings)
        {
            if (id.StartsWith(oldPrefix, StringComparison.Ordinal))
            {
                var suffix = id[oldPrefix.Length..];
                if (suffix.Length > 0 && char.IsDigit(suffix[0]))
                {
                    id = newPrefix + suffix;
                    break;
                }
            }
        }

        var match = StripLeadingZeros.Match(id);
        if (match.Success)
            return match.Groups[1].Value + match.Groups[2].Value;
        return id;
    }

    /// <summary>
    /// Walk the emitted source and rewrite the first colon-delimited segment
    /// of every <c>§X{…}</c> tag's brace contents through <see cref="AbbreviateId"/>.
    /// Applied line-by-line so each match's capture cannot cross a newline.
    /// </summary>
    private static string AbbreviateIdsInTags(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        return TagWithId.Replace(source, m =>
        {
            var prefix = m.Groups[1].Value;
            var id = m.Groups[2].Value;
            var tail = m.Groups[3].Value;
            return prefix + AbbreviateId(id) + tail;
        });
    }
}
