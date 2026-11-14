using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Calor.Compiler.LegacySyntax;

/// <summary>
/// Rewrites legacy Calor held in C# string literals (test inputs). Each literal value goes
/// through <see cref="Rewriter"/> (and therefore the oracle); the result is re-encoded in
/// the same literal kind. Anything that cannot be handled safely is left byte-for-byte
/// unchanged and logged with a reason.
/// </summary>
internal sealed class CSharpLiteralRewriter
{
    /// <summary>
    /// A member whose text mentions any of these is (or may be) testing that legacy syntax
    /// is rejected; its literals are never rewritten.
    /// </summary>
    internal static readonly string[] RejectionMarkers =
    [
        "Calor0003", "Calor0006", "Calor0007", "Calor0830",
        "UnknownSectionMarker", "InvalidSectionOperator", "InvalidTypedLiteral", "LegacyCloserForm",
        "Legacy section marker", "is removed", "etired", "acktick", "egacy",
    ];

    private readonly Log _log;
    private readonly Dictionary<string, RewriteResult> _cache;
    private readonly FragmentRewriter _fragments;

    public CSharpLiteralRewriter(Log log, Dictionary<string, RewriteResult> cache)
    {
        _log = log;
        _cache = cache;
        _fragments = new FragmentRewriter(cache);
    }

    public int RewrittenLiterals { get; private set; }

    public void ProcessFile(string path, bool write)
    {
        var file = TextFile.Read(path);
        if (!file.Text.Contains('§'))
            return;
        var tree = CSharpSyntaxTree.ParseText(file.Text, new CSharpParseOptions(LanguageVersion.Preview), path);
        var root = tree.GetRoot();
        var fileNewline = TextFile.UniformNewline(file.Text) ?? "\n";
        var edits = new List<(TextSpan Span, string Text)>();
        var handledChains = new HashSet<SyntaxNode>();

        foreach (var interpolated in root.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
        {
            if (interpolated.ToString().Contains('§'))
                _log.Skip(path, Line(interpolated), "interpolated-string", null);
        }

        foreach (var token in root.DescendantTokens())
        {
            if (!token.IsKind(SyntaxKind.StringLiteralToken)
                && !token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken)
                && !token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
                continue;
            if (!token.ValueText.Contains('§'))
                continue;
            var line = Line(token);
            if (token.Parent is not LiteralExpressionSyntax literal)
            {
                _log.Skip(path, line, "unexpected-literal-parent", token.Parent?.Kind().ToString());
                continue;
            }

            var marker = RejectionMarker(literal);
            if (marker != null)
            {
                _log.Skip(path, line, "legacy-rejection-test", marker);
                continue;
            }

            // String concatenation chains are rewritten as one unit.
            var chain = ChainRoot(literal);
            var inspection = InspectionUse((ExpressionSyntax?)chain ?? literal);
            if (inspection != null)
            {
                if (chain == null || handledChains.Add(chain))
                    _log.Skip(path, line, "string-inspection-argument", inspection);
                continue;
            }
            if (chain != null)
            {
                if (!handledChains.Add(chain))
                    continue;
                var pieces = ChainLeaves(chain);
                if (pieces.Any(piece => piece is not LiteralExpressionSyntax { Token.RawKind: (int)SyntaxKind.StringLiteralToken }))
                {
                    _log.Skip(path, line, "concatenation-nonliteral", null);
                    continue;
                }
                var value = string.Concat(pieces.Select(piece => ((LiteralExpressionSyntax)piece).Token.ValueText));
                var native = RewriteValue(path, line, value);
                if (native == null)
                    continue;
                var newline = TextFile.UniformNewline(value) ?? "\n";
                edits.Add((chain.Span, EncodeChain(chain, pieces, TextFile.Shape(native, value, newline), newline, file.Text)));
                _log.Rewritten(path, line, "concatenation");
                RewrittenLiterals++;
                continue;
            }

            var original = token.ValueText;
            string? replacement;
            string expected;
            if (token.IsKind(SyntaxKind.StringLiteralToken))
            {
                var verbatim = token.Text.StartsWith('@');
                var newline = TextFile.UniformNewline(original) ?? (verbatim ? fileNewline : "\n");
                var native = RewriteValue(path, line, original);
                if (native == null)
                    continue;
                expected = Preserve(original, TextFile.Shape(native, original, newline));
                replacement = verbatim ? EncodeVerbatim(expected) : EncodeRegular(expected);
            }
            else if (token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
            {
                var newline = TextFile.UniformNewline(token.Text) ?? fileNewline;
                var native = RewriteValue(path, line, original);
                if (native == null)
                    continue;
                expected = TextFile.Shape(native, original, newline);
                replacement = EncodeMultiLineRaw(token.Text, expected, newline);
            }
            else
            {
                var native = RewriteValue(path, line, original);
                if (native == null)
                    continue;
                expected = TextFile.Shape(native, original, "\n");
                if (expected.Contains('\n'))
                {
                    _log.Skip(path, line, "single-line-raw-needs-newlines", null);
                    continue;
                }
                replacement = EncodeSingleLineRaw(token.Text, expected);
            }

            // Belt and braces: the new literal must denote exactly the shaped value.
            if (!RoundTrips(replacement, out var decoded) || decoded != expected)
            {
                _log.Skip(path, line, "encoding-check-failed", null);
                continue;
            }
            edits.Add((token.Span, replacement));
            _log.Rewritten(path, line, token.Kind().ToString());
            RewrittenLiterals++;
        }

        if (edits.Count == 0 || !write)
            return;
        var builder = new StringBuilder(file.Text);
        foreach (var (span, text) in edits.OrderByDescending(edit => edit.Span.Start))
        {
            builder.Remove(span.Start, span.Length);
            builder.Insert(span.Start, text);
        }
        var result = builder.ToString();
        var check = CSharpSyntaxTree.ParseText(result, new CSharpParseOptions(LanguageVersion.Preview));
        var before = tree.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        var after = check.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        if (after > before)
        {
            _log.Skip(path, 0, "file-syntax-check-failed", check.GetDiagnostics().First().ToString());
            return;
        }
        file.Write(result);
    }

    private string? RewriteValue(string path, int line, string value)
    {
        var trimmed = value.TrimStart();
        RewriteResult result;
        if (trimmed.StartsWith("§M{", StringComparison.Ordinal) || trimmed.StartsWith("§M ", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal))
        {
            if (!_cache.TryGetValue(value, out result!))
                _cache[value] = result = Rewriter.Rewrite(value);
        }
        else
        {
            result = _fragments.Rewrite(value);
        }
        if (!result.Ok)
        {
            _log.Skip(path, line, result.Reason!, result.Detail);
            return null;
        }
        return result.Native;
    }

    /// <summary>Keeps any leading blank lines the original value had.</summary>
    private static string Preserve(string original, string shaped)
    {
        var i = 0;
        while (i < original.Length && original[i] is '\r' or '\n')
            i++;
        return original[..i] + shaped;
    }

    /// <summary>
    /// Methods that search, compare or edit text. A literal passed to one of them is a
    /// pattern over some other text (usually emitter output), not a program; rewriting it
    /// in isolation is not meaning-preserving.
    /// </summary>
    private static readonly HashSet<string> InspectionMethods = new(StringComparer.Ordinal)
    {
        "Contains", "DoesNotContain", "StartsWith", "EndsWith", "IndexOf", "LastIndexOf", "IndexOfAny",
        "Replace", "Split", "Equal", "NotEqual", "Matches", "DoesNotMatch", "IsMatch", "Match", "Count",
        "Trim", "TrimStart", "TrimEnd", "Substring", "Remove", "Insert", "Same", "NotSame",
        "Equals", "CompareOrdinal", "Compare", "Escape", "Single", "Any", "All", "Where", "First",
    };

    private static string? InspectionUse(ExpressionSyntax expression)
    {
        SyntaxNode node = expression;
        while (node.Parent is ParenthesizedExpressionSyntax)
            node = node.Parent;
        switch (node.Parent)
        {
            case ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } }:
            {
                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    IdentifierNameSyntax id => id.Identifier.ValueText,
                    GenericNameSyntax generic => generic.Identifier.ValueText,
                    _ => null
                };
                return name != null && InspectionMethods.Contains(name) ? name : null;
            }
            case MemberAccessExpressionSyntax member when member.Expression == node:
                return InspectionMethods.Contains(member.Name.Identifier.ValueText) ? "receiver of " + member.Name.Identifier.ValueText : null;
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                return "==";
            default:
                return null;
        }
    }

    private static string? RejectionMarker(SyntaxNode node)
    {
        var member = node.Ancestors().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m is not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax);
        if (member == null)
            return null;
        var text = member.ToFullString();
        var hit = RejectionMarkers.FirstOrDefault(marker => text.Contains(marker, StringComparison.Ordinal));
        if (hit != null)
            return hit;

        // A field/property holding shared source: check every member of the type that uses it.
        string? name = member switch
        {
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            _ => null
        };
        if (name == null || member.Parent is not TypeDeclarationSyntax type)
            return null;
        foreach (var other in type.Members)
        {
            if (other == member)
                continue;
            if (!other.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == name))
                continue;
            var otherText = other.ToFullString();
            hit = RejectionMarkers.FirstOrDefault(marker => otherText.Contains(marker, StringComparison.Ordinal));
            if (hit != null)
                return hit + " (via " + name + ")";
        }
        return null;
    }

    private static BinaryExpressionSyntax? ChainRoot(SyntaxNode node)
    {
        BinaryExpressionSyntax? top = null;
        var current = node;
        while (current.Parent is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            top = binary;
            current = binary;
        }
        while (top?.Parent is ParenthesizedExpressionSyntax)
            break;
        return top;
    }

    private static List<ExpressionSyntax> ChainLeaves(ExpressionSyntax node)
    {
        var leaves = new List<ExpressionSyntax>();
        void Walk(ExpressionSyntax e)
        {
            if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression))
            {
                Walk(b.Left);
                Walk(b.Right);
            }
            else
                leaves.Add(e);
        }
        Walk(node);
        return leaves;
    }

    /// <summary>
    /// Re-encodes a concatenation chain as one regular literal per line, keeping the
    /// original operator placement (trailing or leading +) and continuation indentation.
    /// </summary>
    private static string EncodeChain(ExpressionSyntax chain, List<ExpressionSyntax> pieces, string value,
        string newline, string fileText)
    {
        var lines = SplitKeepingNewlines(value, newline);
        if (pieces.Count == 1 || lines.Count == 1)
            return EncodeRegular(value);
        var verbatim = pieces.Any(p => ((LiteralExpressionSyntax)p).Token.Text.StartsWith('@'));
        if (verbatim)
            return EncodeVerbatim(value);
        var leading = chain.DescendantTokens().Any(t => t.IsKind(SyntaxKind.PlusToken)
            && t.LeadingTrivia.Any(tr => tr.IsKind(SyntaxKind.EndOfLineTrivia) || tr.IsKind(SyntaxKind.WhitespaceTrivia))
            && t.GetPreviousToken().TrailingTrivia.Any(tr => tr.IsKind(SyntaxKind.EndOfLineTrivia)));
        var second = pieces[1];
        var lineStart = fileText.LastIndexOf('\n', Math.Max(0, second.SpanStart - 1)) + 1;
        var indent = new string(fileText.AsSpan(lineStart, second.SpanStart - lineStart).ToString()
            .TakeWhile(char.IsWhiteSpace).ToArray());
        var fileNewline = fileText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var encoded = EncodeRegular(lines[i]);
            if (i == 0)
                sb.Append(encoded);
            else if (leading)
                sb.Append(fileNewline).Append(indent).Append("+ ").Append(encoded);
            else
                sb.Append(" +").Append(fileNewline).Append(indent).Append(encoded);
        }
        return sb.ToString();
    }

    private static List<string> SplitKeepingNewlines(string value, string newline)
    {
        var parts = new List<string>();
        var start = 0;
        while (true)
        {
            var i = value.IndexOf(newline, start, StringComparison.Ordinal);
            if (i < 0)
            {
                if (start < value.Length)
                    parts.Add(value[start..]);
                break;
            }
            parts.Add(value[start..(i + newline.Length)]);
            start = i + newline.Length;
        }
        return parts;
    }

    internal static string EncodeRegular(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(c) => $"\\u{(int)c:X4}",
                _ => c.ToString()
            });
        }
        return sb.Append('"').ToString();
    }

    internal static string EncodeVerbatim(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";

    private static string EncodeMultiLineRaw(string originalText, string value, string newline)
    {
        var quotes = originalText.TakeWhile(c => c == '"').Count();
        quotes = Math.Max(quotes, LongestQuoteRun(value) + 1);
        var lastBreak = originalText.LastIndexOf('\n');
        var indent = originalText[(lastBreak + 1)..].TakeWhile(c => c is ' ' or '\t').ToArray();
        var indentText = new string(indent);
        var delimiter = new string('"', quotes);
        var lines = value.Split(newline);
        var sb = new StringBuilder(delimiter).Append(newline);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0)
                sb.Append(indentText).Append(lines[i]);
            sb.Append(newline);
        }
        return sb.Append(indentText).Append(delimiter).ToString();
    }

    private static string EncodeSingleLineRaw(string originalText, string value)
    {
        var quotes = Math.Max(originalText.TakeWhile(c => c == '"').Count(), LongestQuoteRun(value) + 1);
        var delimiter = new string('"', quotes);
        if (value.StartsWith('"') || value.EndsWith('"'))
            return EncodeRegular(value);
        return delimiter + value + delimiter;
    }

    private static int LongestQuoteRun(string value)
    {
        var best = 0;
        var run = 0;
        foreach (var c in value)
        {
            run = c == '"' ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return best;
    }

    private static bool RoundTrips(string literalText, out string? value)
    {
        value = null;
        var expr = SyntaxFactory.ParseExpression(literalText, options: new CSharpParseOptions(LanguageVersion.Preview));
        if (expr is not LiteralExpressionSyntax literal || expr.GetDiagnostics().Any()
            || expr.FullSpan.Length != literalText.Length)
            return false;
        value = literal.Token.ValueText;
        return true;
    }

    private static int Line(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
