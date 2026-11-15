using System.Text;
using System.Text.RegularExpressions;

namespace Calor.Compiler.LegacySyntax;

/// <summary>One piece of a legacy template: literal text, or a hole filled at run time.</summary>
internal abstract record TemplatePart;
internal sealed record TextPart(string Text) : TemplatePart;
internal sealed record HolePart(int Index) : TemplatePart;

/// <summary>
/// Rewrites a legacy template whose holes (interpolations, non-literal concatenation
/// operands) are only known at run time. Each hole is replaced by a placeholder the legacy
/// parser accepts, the whole text goes through the normal oracle, and the placeholders are
/// located in the native text and turned back into holes.
///
/// Two placeholder roles:
///   - a hole that fills a whole line (only indentation before it, a line break or the end
///     after it) becomes the call statement <c>§C{zqhNq} §/C</c>, printed natively as
///     <c>zqhNq()</c>. When the hole starts at column 0 it carries its own indentation, so
///     a few plausible columns are tried, and the native line must sit at the same column.
///   - any other hole becomes the identifier <c>zqhNq</c> (a name, type or expression).
/// Each placeholder must appear exactly once in the native text, at the same role.
/// </summary>
internal static class TemplateRewriter
{
    private static string Name(int i) => $"zqh{i}q";

    public static (List<TemplatePart>? Parts, string? Reason, string? Detail) Rewrite(
        List<TemplatePart> parts, Func<string, RewriteResult> rewrite)
    {
        var holes = parts.OfType<HolePart>().ToList();
        if (holes.Count == 0)
            return (null, "template-no-holes", null);
        var allText = string.Concat(parts.OfType<TextPart>().Select(p => p.Text));
        if (allText.Contains("zqh", StringComparison.Ordinal))
            return (null, "template-placeholder-collision", null);

        // Roles.
        var lineHole = new Dictionary<int, bool>();
        var ownsIndent = new Dictionary<int, bool>();
        var indentCandidates = new Dictionary<int, List<int>>();
        for (var k = 0; k < parts.Count; k++)
        {
            if (parts[k] is not HolePart hole)
                continue;
            var before = LineTail(parts, k);
            var after = LineHead(parts, k);
            var isLine = before != null && before.All(c => c == ' ') && after != null && after.Length == 0;
            lineHole[hole.Index] = isLine;
            ownsIndent[hole.Index] = isLine && before!.Length == 0;
            if (ownsIndent[hole.Index])
            {
                var prev = NeighbourIndent(parts, k, -1);
                var next = NeighbourIndent(parts, k, +1);
                var list = new List<int>();
                foreach (var c in new int?[] { prev + 2, prev, next, prev + 4 })
                    if (c is { } v && v >= 0 && !list.Contains(v))
                        list.Add(v);
                if (list.Count == 0)
                    list.Add(0);
                indentCandidates[hole.Index] = list;
            }
        }

        var attempts = indentCandidates.Count == 0 ? 1 : indentCandidates.Values.Max(l => l.Count);
        RewriteResult? last = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var chosen = indentCandidates.ToDictionary(kv => kv.Key, kv => kv.Value[Math.Min(attempt, kv.Value.Count - 1)]);
            var legacy = new StringBuilder();
            foreach (var part in parts)
            {
                if (part is TextPart t)
                    legacy.Append(t.Text);
                else
                {
                    var h = ((HolePart)part).Index;
                    if (!lineHole[h])
                        legacy.Append(Name(h));
                    else
                        legacy.Append(ownsIndent[h] ? new string(' ', chosen[h]) : "").Append("§C{").Append(Name(h)).Append("} §/C");
                }
            }
            var result = rewrite(legacy.ToString());
            if (!result.Ok)
            {
                last = result;
                continue;
            }
            var split = Split(result.Native!, holes.Select(h => h.Index).ToList(), lineHole, ownsIndent, chosen);
            if (split.Parts != null)
                return (split.Parts, null, null);
            last = RewriteResult.Fail(split.Reason!, split.Detail);
        }
        return (null, last?.Reason ?? "template", last?.Detail);
    }

    private static (List<TemplatePart>? Parts, string? Reason, string? Detail) Split(
        string native, List<int> holes, Dictionary<int, bool> lineHole, Dictionary<int, bool> ownsIndent,
        Dictionary<int, int> chosen)
    {
        // Find each placeholder's span in the native text.
        var spans = new List<(int Start, int Length, int Hole)>();
        foreach (var h in holes)
        {
            var name = Name(h);
            var count = Regex.Matches(native, @"\b" + name + @"\b").Count;
            if (count != 1)
                return (null, "template-placeholder-count", $"{name} x{count}");
            var at = Regex.Match(native, @"\b" + name + @"\b").Index;
            if (!lineHole[h])
            {
                if (native.AsSpan(at + name.Length).StartsWith("("))
                    return (null, "template-placeholder-role", name + " became a call");
                spans.Add((at, name.Length, h));
                continue;
            }
            var lineStart = native.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
            if (at == 0) lineStart = 0;
            var lineEnd = native.IndexOf('\n', at);
            if (lineEnd < 0) lineEnd = native.Length;
            var line = native[lineStart..lineEnd].TrimEnd('\r');
            var indent = line.Length - line.TrimStart(' ').Length;
            if (line.Trim() != name + "()")
                return (null, "template-placeholder-role", line.Trim());
            if (ownsIndent[h])
            {
                if (indent != chosen[h])
                    return (null, "template-placeholder-indent", $"{name} at {indent}, tried {chosen[h]}");
                spans.Add((lineStart, line.Length, h));
            }
            else
                spans.Add((lineStart + indent, line.Length - indent, h));
        }
        if (native.Contains("zqh", StringComparison.Ordinal) && spans.Count != holes.Count)
            return (null, "template-placeholder-count", "stray placeholder");

        var parts = new List<TemplatePart>();
        var pos = 0;
        foreach (var (start, length, hole) in spans.OrderBy(s => s.Start))
        {
            if (start < pos)
                return (null, "template-placeholder-overlap", null);
            if (start > pos)
                parts.Add(new TextPart(native[pos..start]));
            parts.Add(new HolePart(hole));
            pos = start + length;
        }
        if (pos < native.Length)
            parts.Add(new TextPart(native[pos..]));
        if (string.Concat(parts.OfType<TextPart>().Select(p => p.Text)).Contains("zqh", StringComparison.Ordinal))
            return (null, "template-placeholder-count", "stray placeholder");
        return (parts, null, null);
    }

    /// <summary>The text between the previous line break and part k, or null if another hole is on that stretch.</summary>
    private static string? LineTail(List<TemplatePart> parts, int k)
    {
        var sb = new StringBuilder();
        for (var j = k - 1; j >= 0; j--)
        {
            if (parts[j] is not TextPart t)
                return null;
            var nl = t.Text.LastIndexOf('\n');
            if (nl >= 0)
                return t.Text[(nl + 1)..] + sb;
            sb.Insert(0, t.Text);
        }
        return sb.ToString();
    }

    /// <summary>The text between part k and the next line break (exclusive), or null if another hole is on that stretch.</summary>
    private static string? LineHead(List<TemplatePart> parts, int k)
    {
        var sb = new StringBuilder();
        for (var j = k + 1; j < parts.Count; j++)
        {
            if (parts[j] is not TextPart t)
                return null;
            var nl = t.Text.IndexOf('\n');
            if (nl >= 0)
                return sb + t.Text[..nl].TrimEnd('\r');
            sb.Append(t.Text);
        }
        return sb.ToString();
    }

    /// <summary>Indentation of the nearest non-blank text line before (dir -1) or after (+1) part k.</summary>
    private static int? NeighbourIndent(List<TemplatePart> parts, int k, int dir)
    {
        for (var j = k + dir; j >= 0 && j < parts.Count; j += dir)
        {
            if (parts[j] is not TextPart t)
                continue;
            var lines = t.Text.Replace("\r\n", "\n").Split('\n');
            var ordered = dir < 0 ? lines.Reverse() : lines;
            var skipFirst = true;
            foreach (var line in ordered)
            {
                // The fragment adjoining the hole is part of the hole's own line.
                if (skipFirst) { skipFirst = false; continue; }
                if (line.Trim().Length > 0)
                    return line.Length - line.TrimStart(' ').Length;
            }
        }
        return null;
    }
}
