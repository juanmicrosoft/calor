namespace Calor.Compiler.LegacySyntax;

/// <summary>
/// Rewrites legacy text that is not a complete module by wrapping it in a minimal legacy
/// module, rewriting that module under the full oracle, and unwrapping the native result.
/// The unwrap is checked: the native module must be exactly the native wrapper header
/// followed by the fragment's lines at the wrapper's body indentation.
///
/// Only indentation-unambiguous fragments are attempted: single-line ones, and multi-line
/// ones whose first line is at the fragment's minimum indentation. (A fragment such as
/// "§I{a}\n      §I{b}" is written for insertion at a helper's column; its continuation
/// lines are absolute and cannot be re-indented without knowing that column.)
/// </summary>
internal sealed class FragmentRewriter
{
    private const string ModuleName = "LegacyFragmentWrapper";
    private const string FunctionName = "LegacyFragmentBody";
    private const string ClassName = "LegacyFragmentClass";

    private static readonly string[] ModuleItemMarkers =
        ["§F{", "§AF{", "§CL{", "§IFACE{", "§EN{", "§EEXT{", "§DEL{", "§RTYPE{", "§ITYPE{", "§NS{", "§USE{", "§CSHARP", "§RAW"];

    private static readonly string[] ClassMemberMarkers =
        ["§MT{", "§AMT{", "§FLD{", "§PROP{", "§CTOR{", "§OP{", "§IDX{", "§EVT{"];

    private readonly Dictionary<string, RewriteResult> _cache;

    public FragmentRewriter(Dictionary<string, RewriteResult> cache) => _cache = cache;

    public RewriteResult Rewrite(string fragment)
    {
        var lines = fragment.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Trim().Length == 0)
            lines.RemoveAt(0);
        if (lines.Count == 0)
            return RewriteResult.Fail("fragment-empty");
        if (lines.Any(l => l.Contains('\t')))
            return RewriteResult.Fail("fragment-tabs");
        var baseIndent = lines.Where(l => l.Trim().Length > 0).Select(Indent).Min();
        if (Indent(lines[0]) != baseIndent)
            return RewriteResult.Fail("fragment-ambiguous-indentation", Head(fragment));
        var body = lines.Select(l => l.Trim().Length == 0 ? "" : l[baseIndent..]).ToList();

        RewriteResult? last = null;
        foreach (var shape in Shapes(body[0], body.Count))
        {
            var result = TryShape(shape, body);
            if (result.Ok)
            {
                var pad = new string(' ', baseIndent);
                var native = string.Join("\n", result.Native!.Split('\n').Select(l => l.Length == 0 ? l : pad + l));
                return new RewriteResult(native + "\n", null);
            }
            last = result;
        }
        return last ?? RewriteResult.Fail("fragment", Head(fragment));
    }

    private enum Shape { ModuleItems, ClassMembers, Statements, Expression }

    private static IEnumerable<Shape> Shapes(string first, int lineCount)
    {
        if (ModuleItemMarkers.Any(m => first.StartsWith(m, StringComparison.Ordinal)))
            yield return Shape.ModuleItems;
        else if (ClassMemberMarkers.Any(m => first.StartsWith(m, StringComparison.Ordinal)))
            yield return Shape.ClassMembers;
        else
        {
            yield return Shape.Statements;
            if (lineCount == 1 && !first.StartsWith('§'))
                yield return Shape.Expression;
        }
    }

    private RewriteResult TryShape(Shape shape, List<string> body)
    {
        var ownsEffects = shape == Shape.Statements && body[0].StartsWith("§E{", StringComparison.Ordinal);
        string legacy;
        int depth;
        switch (shape)
        {
            case Shape.ModuleItems:
                legacy = $"§M{{{ModuleName}}}\n" + Indented(body, 1);
                depth = 1;
                break;
            case Shape.ClassMembers:
                legacy = $"§M{{{ModuleName}}}\n  §CL{{{ClassName}:pub}}\n" + Indented(body, 2);
                depth = 2;
                break;
            case Shape.Statements:
                legacy = $"§M{{{ModuleName}}}\n  §F{{{FunctionName}:pub}} () -> void\n" + Indented(body, 2);
                depth = 2;
                break;
            default:
                legacy = $"§M{{{ModuleName}}}\n  §F{{{FunctionName}:pub}} () -> void\n    §R {body[0]}\n";
                depth = 2;
                break;
        }

        if (!_cache.TryGetValue(legacy, out var result))
            _cache[legacy] = result = Rewriter.Rewrite(legacy);
        if (!result.Ok)
            return result;

        // Unwrap, checking the exact wrapper shape.
        var native = result.Native!.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
        var header = new List<string> { $"module {ModuleName} {ModuleName} block" };
        if (shape == Shape.ClassMembers)
            header.Add($"  class {ClassName}");
        if (shape is Shape.Statements or Shape.Expression)
        {
            header.Add($"  func {FunctionName} () → void");
            if (!ownsEffects)
                header.Add("    !");
        }
        if (native.Count < header.Count || !header.SequenceEqual(native.Take(header.Count)))
            return RewriteResult.Fail("fragment-unwrap", native.FirstOrDefault(l => !header.Contains(l)));
        var pad = new string(' ', depth * 2);
        var rest = native.Skip(header.Count).ToList();
        if (rest.Any(l => l.Length > 0 && !l.StartsWith(pad, StringComparison.Ordinal)))
            return RewriteResult.Fail("fragment-unwrap", "body escapes the wrapper");
        var unwrapped = rest.Select(l => l.Length == 0 ? "" : l[pad.Length..]).ToList();
        while (unwrapped.Count > 0 && unwrapped[^1].Length == 0)
            unwrapped.RemoveAt(unwrapped.Count - 1);
        if (unwrapped.Count == 0)
            return RewriteResult.Fail("fragment-unwrap", "empty body");
        if (shape == Shape.Expression)
        {
            if (unwrapped.Count != 1 || !unwrapped[0].StartsWith("return ", StringComparison.Ordinal))
                return RewriteResult.Fail("fragment-unwrap", "expression shape");
            unwrapped[0] = unwrapped[0]["return ".Length..];
        }
        return new RewriteResult(string.Join("\n", unwrapped), null);
    }

    private static string Indented(List<string> body, int depth)
    {
        var pad = new string(' ', depth * 2);
        return string.Concat(body.Select(l => (l.Length == 0 ? "" : pad + l) + "\n"));
    }

    private static int Indent(string line) => line.TakeWhile(c => c == ' ').Count();

    private static string Head(string s)
    {
        var t = s.TrimStart();
        return t.Length > 80 ? t[..80] : t;
    }
}
