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
    private readonly HashSet<string> _skipList;

    /// <summary>
    /// Skip reasons that leave legacy text in a file on purpose (the literal is a pattern,
    /// a key or a rejection input). Any other skip means the file still holds legacy
    /// programs, so its fragments' consumers are probably legacy too.
    /// </summary>
    private static readonly HashSet<string> IntentionalLegacy = new(StringComparer.Ordinal)
    {
        "legacy-rejection-test", "lookup-key", "string-inspection-argument", "theory-data-collision",
        "legacy-pattern-use", "skip-list",
    };
    private readonly Dictionary<string, RewriteResult> _cache;
    private readonly FragmentRewriter _fragments;

    public CSharpLiteralRewriter(Log log, Dictionary<string, RewriteResult> cache, HashSet<string> skipList)
    {
        _log = log;
        _skipList = skipList;
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
        _log.BeginFile();
        var relative = _log.Relative(path);
        var edits = new List<(TextSpan Span, string Text, int Line, string Kind)>();
        var fragmentEdits = new HashSet<TextSpan>();
        var handledChains = new HashSet<SyntaxNode>();
        // String values used as switch/case keys in this file: a literal equal to one is a
        // lookup key (often a legacy→current fixture table), not a program to rewrite.
        var keys = root.DescendantNodes()
            .Where(n => n is ConstantPatternSyntax or CaseSwitchLabelSyntax)
            .SelectMany(n => n.DescendantTokens())
            .Where(t => t.IsKind(SyntaxKind.StringLiteralToken) || t.IsKind(SyntaxKind.SingleLineRawStringLiteralToken)
                || t.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
            .Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);

        foreach (var interpolated in root.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
        {
            if (!interpolated.ToString().Contains('§'))
                continue;
            if (interpolated.Ancestors().Any(a => a is InterpolatedStringExpressionSyntax))
                continue; // part of an outer template
            var iline = Line(interpolated);
            var refusal = TemplateRefusal(path, relative, iline, interpolated, keys: null);
            if (refusal != null)
            {
                _log.Skip(path, iline, refusal.Value.Reason, refusal.Value.Detail);
                continue;
            }
            if (ChainRoot(interpolated) != null)
            {
                _log.Skip(path, iline, "interpolated-string", "piece of a concatenation");
                continue;
            }
            var edit = RewriteInterpolated(path, iline, interpolated, out var isFragment);
            if (edit == null)
                continue;
            if (isFragment)
                fragmentEdits.Add(interpolated.Span);
            edits.Add((interpolated.Span, edit, iline, "interpolated"));
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

            if (_skipList.Contains($"{relative}:{line}"))
            {
                _log.Skip(path, line, "skip-list", null);
                continue;
            }

            if (literal.Ancestors().Any(a => a is InterpolatedStringExpressionSyntax))
            {
                _log.Skip(path, line, "inside-interpolated-template", null);
                continue;
            }

            if (keys.Contains(token.ValueText))
            {
                _log.Skip(path, line, "lookup-key", null);
                continue;
            }

            var marker = RejectionMarker(literal);
            if (marker != null)
            {
                _log.Skip(path, line, "legacy-rejection-test", marker);
                continue;
            }
            var patternUse = LegacyPatternUse(literal);
            if (patternUse != null)
            {
                _log.Skip(path, line, "legacy-pattern-use", patternUse);
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
                    var chainEdit = RewriteTemplateChain(path, line, chain, pieces, file.Text, out var chainFragment);
                    if (chainEdit == null)
                        continue;
                    if (chainFragment)
                        fragmentEdits.Add(chain.Span);
                    edits.Add((chain.Span, chainEdit, line, "template-concatenation"));
                    continue;
                }
                var value = string.Concat(pieces.Select(piece => ((LiteralExpressionSyntax)piece).Token.ValueText));
                var native = RewriteValue(path, line, value, out var isFragment);
                if (native == null)
                    continue;
                var newline = TextFile.UniformNewline(value) ?? "\n";
                if (isFragment)
                    fragmentEdits.Add(chain.Span);
                edits.Add((chain.Span, EncodeChain(chain, pieces, TextFile.Shape(native, value, newline), newline, file.Text),
                    line, "concatenation"));
                continue;
            }

            var original = token.ValueText;
            var fragment = false;
            string? replacement;
            string expected;
            if (token.IsKind(SyntaxKind.StringLiteralToken))
            {
                var verbatim = token.Text.StartsWith('@');
                var newline = TextFile.UniformNewline(original) ?? (verbatim ? fileNewline : "\n");
                var native = RewriteValue(path, line, original, out var isFragment);
                if (native == null)
                    continue;
                fragment = isFragment;
                expected = Preserve(original, TextFile.Shape(native, original, newline));
                replacement = verbatim ? EncodeVerbatim(expected) : EncodeRegular(expected);
            }
            else if (token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
            {
                var newline = TextFile.UniformNewline(token.Text) ?? fileNewline;
                var native = RewriteValue(path, line, original, out var isFragment);
                if (native == null)
                    continue;
                fragment = isFragment;
                expected = TextFile.Shape(native, original, newline);
                replacement = EncodeMultiLineRaw(token.Text, expected, newline);
            }
            else
            {
                var native = RewriteValue(path, line, original, out var isFragment);
                if (native == null)
                    continue;
                fragment = isFragment;
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
            if (fragment)
                fragmentEdits.Add(token.Span);
            edits.Add((token.Span, replacement, line, token.Kind().ToString()));
        }

        DropTheoryDataCollisions(root, edits, path);

        // Fragments are only meaningful inside their consumer. If the file keeps legacy
        // programs (anything but intentional legacy), keep its fragments legacy as well.
        var blocking = _log.FileReasons.FirstOrDefault(reason => !IntentionalLegacy.Contains(reason));
        if (blocking != null)
        {
            foreach (var edit in edits.Where(e => fragmentEdits.Contains(e.Span)).ToList())
            {
                edits.Remove(edit);
                _log.Skip(path, edit.Line, "fragment-context-legacy", blocking);
            }
        }
        foreach (var edit in edits)
        {
            _log.Rewritten(path, edit.Line, edit.Kind);
            RewrittenLiterals++;
        }

        if (edits.Count == 0 || !write)
            return;
        var builder = new StringBuilder(file.Text);
        foreach (var (span, text, _, _) in edits.OrderByDescending(edit => edit.Span.Start))
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

    private RewriteResult TryRewriteValue(string value, out bool isFragment)
    {
        var trimmed = value.TrimStart();
        isFragment = !(trimmed.StartsWith("§M{", StringComparison.Ordinal) || trimmed.StartsWith("§M ", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal));
        if (isFragment)
            return _fragments.Rewrite(value);
        if (!_cache.TryGetValue(value, out var result))
            _cache[value] = result = Rewriter.Rewrite(value);
        return result;
    }

    private string? RewriteValue(string path, int line, string value, out bool isFragment)
    {
        var trimmed = value.TrimStart();
        RewriteResult result;
        isFragment = false;
        if (trimmed.StartsWith("§M{", StringComparison.Ordinal) || trimmed.StartsWith("§M ", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal))
        {
            if (!_cache.TryGetValue(value, out result!))
                _cache[value] = result = Rewriter.Rewrite(value);
        }
        else
        {
            isFragment = true;
            result = _fragments.Rewrite(value);
        }
        if (!result.Ok)
        {
            _log.Skip(path, line, result.Reason!, result.Detail);
            return null;
        }
        return result.Native;
    }

    /// <summary>The same exclusions a plain literal gets, applied to a whole template expression.</summary>
    private (string Reason, string? Detail)? TemplateRefusal(string path, string relative, int line, ExpressionSyntax node,
        HashSet<string>? keys)
    {
        if (_skipList.Contains($"{relative}:{line}"))
            return ("skip-list", null);
        var marker = RejectionMarker(node);
        if (marker != null)
            return ("legacy-rejection-test", marker);
        var patternUse = LegacyPatternUse(node);
        if (patternUse != null)
            return ("legacy-pattern-use", patternUse);
        var inspection = InspectionUse((ExpressionSyntax?)ChainRoot(node) ?? node);
        if (inspection != null)
            return ("string-inspection-argument", inspection);
        return null;
    }

    /// <summary>
    /// Runs a template through <see cref="TemplateRewriter"/>, shaping each candidate's native
    /// text like the legacy text it replaces (newline convention, trailing newline, leading
    /// blank lines). Logs the failure, if any.
    /// </summary>
    private List<TemplatePart>? RewriteTemplate(string path, int line, List<TemplatePart> parts, List<string> holeTexts,
        out bool isFragment)
    {
        isFragment = false;
        if (holeTexts.Any(h => h.Contains('§')))
        {
            _log.Skip(path, line, "template-hole-legacy", holeTexts.First(h => h.Contains('§')));
            return null;
        }
        if (holeTexts.Any(h => h.Contains('\n')))
        {
            _log.Skip(path, line, "template-hole-multiline", null);
            return null;
        }
        var text = string.Concat(parts.OfType<TextPart>().Select(p => p.Text));
        var newline = TextFile.UniformNewline(text) ?? "\n";
        var fragment = false;
        var (native, reason, detail) = TemplateRewriter.Rewrite(parts, legacy =>
        {
            var result = TryRewriteValue(legacy, out fragment);
            return result.Ok ? result with { Native = Preserve(legacy, TextFile.Shape(result.Native!, legacy, newline)) } : result;
        });
        if (native == null)
        {
            _log.Skip(path, line, reason!, detail);
            return null;
        }
        isFragment = fragment;
        return native;
    }

    private string? RewriteInterpolated(string path, int line, InterpolatedStringExpressionSyntax node, out bool isFragment)
    {
        isFragment = false;
        var start = node.StringStartToken.Text;
        var raw = start.Contains("\"\"\"", StringComparison.Ordinal);
        if (raw && !node.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken))
        {
            _log.Skip(path, line, "interpolated-string", "single-line raw");
            return null;
        }
        var (parts, holeTexts) = ReadInterpolated(node);
        var native = RewriteTemplate(path, line, parts, holeTexts, out isFragment);
        if (native == null)
            return null;
        var encoded = EncodeInterpolated(node, native, holeTexts);
        if (encoded == null)
        {
            _log.Skip(path, line, "interpolated-encoding", null);
            return null;
        }
        var reparsed = SyntaxFactory.ParseExpression(encoded, options: new CSharpParseOptions(LanguageVersion.Preview));
        if (reparsed is not InterpolatedStringExpressionSyntax again || reparsed.GetDiagnostics().Any()
            || reparsed.FullSpan.Length != encoded.Length || !SameParts(ReadInterpolated(again), native, holeTexts))
        {
            _log.Skip(path, line, "encoding-check-failed", "interpolated");
            return null;
        }
        return encoded;
    }

    private static (List<TemplatePart> Parts, List<string> Holes) ReadInterpolated(InterpolatedStringExpressionSyntax node)
    {
        var parts = new List<TemplatePart>();
        var holes = new List<string>();
        foreach (var content in node.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                if (parts.Count > 0 && parts[^1] is TextPart previous)
                    parts[^1] = new TextPart(previous.Text + text.TextToken.ValueText);
                else
                    parts.Add(new TextPart(text.TextToken.ValueText));
            }
            else
            {
                parts.Add(new HolePart(holes.Count));
                holes.Add(content.ToString());
            }
        }
        return (parts, holes);
    }

    private static bool SameParts((List<TemplatePart> Parts, List<string> Holes) actual, List<TemplatePart> intended,
        List<string> holeTexts)
    {
        static List<TemplatePart> Merge(List<TemplatePart> parts)
        {
            var merged = new List<TemplatePart>();
            foreach (var p in parts)
            {
                if (p is TextPart { Text.Length: 0 })
                    continue;
                if (p is TextPart t && merged.Count > 0 && merged[^1] is TextPart prev)
                    merged[^1] = new TextPart(prev.Text + t.Text);
                else
                    merged.Add(p);
            }
            return merged;
        }
        static string Flat(List<TemplatePart> parts, Func<int, string> hole) =>
            string.Concat(parts.Select(p => p is TextPart t ? "T:" + t.Text + "\u0001" : "H:" + hole(((HolePart)p).Index) + "\u0001"));
        return Flat(Merge(actual.Parts), i => actual.Holes[i]) == Flat(Merge(intended), i => holeTexts[i]);
    }

    private static string? EncodeInterpolated(InterpolatedStringExpressionSyntax node, List<TemplatePart> parts, List<string> holeTexts)
    {
        var start = node.StringStartToken.Text;
        if (start.Contains("\"\"\"", StringComparison.Ordinal))
        {
            var dollars = start.TakeWhile(c => c == '$').Count();
            var original = node.ToString();
            var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var content = new StringBuilder();
            foreach (var part in parts)
            {
                if (part is TextPart t)
                {
                    if (LongestRun(t.Text, '{') >= dollars || LongestRun(t.Text, '}') >= dollars)
                        return null;
                    content.Append(t.Text);
                }
                else
                    content.Append(holeTexts[((HolePart)part).Index]);
            }
            var value = content.ToString().Replace("\r\n", "\n");
            if (value.EndsWith('\n'))
                return null; // a raw literal cannot end with a line break
            var lastBreak = original.LastIndexOf('\n');
            var indent = new string(original[(lastBreak + 1)..].TakeWhile(c => c is ' ' or '\t').ToArray());
            var quotes = Math.Max(start.Count(c => c == '"'), LongestQuoteRun(value) + 1);
            var delimiter = new string('"', quotes);
            var sb = new StringBuilder(new string('$', dollars)).Append(delimiter).Append(newline);
            foreach (var l in value.Split('\n'))
            {
                if (l.Length > 0)
                    sb.Append(indent).Append(l);
                sb.Append(newline);
            }
            return sb.Append(indent).Append(delimiter).ToString();
        }

        var verbatim = start.Contains('@');
        var body = new StringBuilder();
        foreach (var part in parts)
        {
            if (part is TextPart t)
            {
                var escaped = verbatim ? t.Text.Replace("\"", "\"\"") : EncodeRegular(t.Text)[1..^1];
                body.Append(escaped.Replace("{", "{{").Replace("}", "}}"));
            }
            else
                body.Append(holeTexts[((HolePart)part).Index]);
        }
        return start + body + "\"";
    }

    private static int LongestRun(string value, char c)
    {
        var best = 0;
        var run = 0;
        foreach (var x in value)
        {
            run = x == c ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return best;
    }

    /// <summary>
    /// A + chain mixing string literals with other operands: the literals are template
    /// text, the other operands are holes. Re-emitted as a chain with the same operands in
    /// the same order; the first operand keeps its kind (C# + is left-associative).
    /// </summary>
    private string? RewriteTemplateChain(string path, int line, BinaryExpressionSyntax chain, List<ExpressionSyntax> pieces,
        string fileText, out bool isFragment)
    {
        isFragment = false;
        var parts = new List<TemplatePart>();
        var holeTexts = new List<string>();
        var verbatim = false;
        foreach (var piece in pieces)
        {
            if (piece is LiteralExpressionSyntax { Token.RawKind: (int)SyntaxKind.StringLiteralToken } literal)
            {
                verbatim |= literal.Token.Text.StartsWith('@');
                if (parts.Count > 0 && parts[^1] is TextPart previous)
                    parts[^1] = new TextPart(previous.Text + literal.Token.ValueText);
                else
                    parts.Add(new TextPart(literal.Token.ValueText));
                continue;
            }
            if (piece.ToString().Contains('§'))
            {
                _log.Skip(path, line, "concatenation-nonliteral", "operand holds legacy text: " + Clip(piece.ToString()));
                return null;
            }
            parts.Add(new HolePart(holeTexts.Count));
            holeTexts.Add(piece.ToString());
        }
        var native = RewriteTemplate(path, line, parts, holeTexts, out isFragment);
        if (native == null)
            return null;

        // Elements: holes verbatim; text split one literal per line (verbatim: one literal).
        var elements = new List<(string Code, bool EndsLine, bool IsText)>();
        foreach (var part in native)
        {
            if (part is HolePart h)
            {
                elements.Add((holeTexts[h.Index], false, false));
                continue;
            }
            var text = ((TextPart)part).Text;
            if (text.Length == 0)
                continue;
            if (verbatim)
                elements.Add((EncodeVerbatim(text), text.EndsWith('\n'), true));
            else
                foreach (var l in SplitKeepingNewlines(text, "\n"))
                    elements.Add((EncodeRegular(l), l.EndsWith('\n'), true));
        }
        if (elements.Count == 0 || elements[0].IsText != pieces[0] is LiteralExpressionSyntax
            || !elements[0].IsText && elements.Count > 1 && !elements[1].IsText)
        {
            _log.Skip(path, line, "encoding-check-failed", "template chain head");
            return null;
        }
        var multiLine = chain.ToString().Contains('\n');
        var leading = chain.DescendantTokens().Any(t => t.IsKind(SyntaxKind.PlusToken)
            && t.GetPreviousToken().TrailingTrivia.Any(tr => tr.IsKind(SyntaxKind.EndOfLineTrivia)));
        var second = pieces[1];
        var lineStart = fileText.LastIndexOf('\n', Math.Max(0, second.SpanStart - 1)) + 1;
        var indent = new string(fileText.AsSpan(lineStart, second.SpanStart - lineStart).ToString()
            .TakeWhile(char.IsWhiteSpace).ToArray());
        var fileNewline = fileText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var sb = new StringBuilder();
        for (var i = 0; i < elements.Count; i++)
        {
            if (i == 0)
                sb.Append(elements[i].Code);
            else if (!multiLine || !elements[i - 1].EndsLine)
                sb.Append(" + ").Append(elements[i].Code);
            else if (leading)
                sb.Append(fileNewline).Append(indent).Append("+ ").Append(elements[i].Code);
            else
                sb.Append(" +").Append(fileNewline).Append(indent).Append(elements[i].Code);
        }
        var encoded = sb.ToString();

        // Check: same operands in order, literal text equal to the native template text.
        var reparsed = SyntaxFactory.ParseExpression(encoded, options: new CSharpParseOptions(LanguageVersion.Preview));
        if (reparsed.GetDiagnostics().Any() || reparsed.FullSpan.Length != encoded.Length)
        {
            _log.Skip(path, line, "encoding-check-failed", "template chain parse");
            return null;
        }
        var readParts = new List<TemplatePart>();
        var readHoles = new List<string>();
        foreach (var leaf in ChainLeaves(reparsed))
        {
            if (leaf is LiteralExpressionSyntax { Token.RawKind: (int)SyntaxKind.StringLiteralToken } lit)
                readParts.Add(new TextPart(lit.Token.ValueText));
            else
            {
                readParts.Add(new HolePart(readHoles.Count));
                readHoles.Add(leaf.ToString());
            }
        }
        if (!SameParts((readParts, readHoles), native, holeTexts))
        {
            _log.Skip(path, line, "encoding-check-failed", "template chain");
            return null;
        }
        return encoded;
    }

    private static string Clip(string s) => s.Length > 80 ? s[..80] : s;

    /// <summary>Keeps any leading blank lines the original value had.</summary>
    private static string Preserve(string original, string shaped)
    {
        var i = 0;
        while (i < original.Length && original[i] is '\r' or '\n')
            i++;
        return original[..i] + shaped;
    }

    /// <summary>
    /// Two distinct legacy inputs can print to the same native text (e.g. §ASSIGN §IDX and
    /// §SETIDX). When rewriting would make two data attributes of one method identical, the
    /// method's attribute edits are dropped so no theory case is silently merged.
    /// </summary>
    private void DropTheoryDataCollisions(SyntaxNode root, List<(TextSpan Span, string Text, int Line, string Kind)> edits, string path)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var attributes = method.AttributeLists.SelectMany(list => list.Attributes).ToList();
            if (attributes.Count < 2)
                continue;
            var inMethod = edits.Where(e => attributes.Any(a => a.Span.Contains(e.Span))).ToList();
            if (inMethod.Count == 0)
                continue;
            var rendered = attributes.Select(attribute =>
            {
                var text = attribute.ToString();
                foreach (var edit in inMethod.Where(e => attribute.Span.Contains(e.Span)).OrderByDescending(e => e.Span.Start))
                {
                    var offset = edit.Span.Start - attribute.SpanStart;
                    text = text[..offset] + edit.Text + text[(offset + edit.Span.Length)..];
                }
                return text;
            }).ToList();
            var originalDistinct = attributes.Select(a => a.ToString()).Distinct(StringComparer.Ordinal).Count();
            if (rendered.Distinct(StringComparer.Ordinal).Count() == originalDistinct)
                continue;
            foreach (var edit in inMethod)
            {
                edits.Remove(edit);
                _log.Skip(path, edit.Line, "theory-data-collision", method.Identifier.ValueText);
            }
        }
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

    /// <summary>
    /// A literal stored in a variable, field or property that some code then searches or
    /// edits with legacy patterns (`Source.Replace("§F{C:pub}...", ...)`): the patterns stay
    /// legacy, so the stored text must too.
    /// </summary>
    private static string? LegacyPatternUse(SyntaxNode literal)
    {
        string? name = null;
        SyntaxNode? scope = null;
        foreach (var ancestor in literal.Ancestors())
        {
            if (ancestor is VariableDeclaratorSyntax declarator)
            {
                name = declarator.Identifier.ValueText;
                scope = declarator.Parent?.Parent is FieldDeclarationSyntax field
                    ? field.Parent
                    : declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>() as SyntaxNode
                      ?? declarator.FirstAncestorOrSelf<LocalFunctionStatementSyntax>();
                break;
            }
            if (ancestor is PropertyDeclarationSyntax property)
            {
                name = property.Identifier.ValueText;
                scope = property.Parent;
                break;
            }
            if (ancestor is StatementSyntax or MemberDeclarationSyntax)
                break;
        }
        if (name == null || scope == null)
            return null;
        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax receiver, Name: var method
                } || receiver.Identifier.ValueText != name || !InspectionMethods.Contains(method.Identifier.ValueText))
                continue;
            if (invocation.ArgumentList.Arguments.Any(argument => argument.ToString().Contains('§')))
                return $"{name}.{method.Identifier.ValueText}(\"§...\")";
        }
        return null;
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
