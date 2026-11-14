using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;

namespace Calor.Compiler.LegacySyntax;

/// <summary>The outcome of one rewrite attempt. <see cref="Native"/> is set only when the oracle passed.</summary>
public sealed record RewriteResult(string? Native, string? Reason, string? Detail = null)
{
    /// <summary>Diagnostics only: the rejected candidate and the two C# texts the oracle compared.</summary>
    public string? Candidate { get; init; }
    public string? CsLegacy { get; init; }
    public string? CsNative { get; init; }

    public bool Ok => Native != null;
    public static RewriteResult Fail(string reason, string? detail = null) => new(null, reason, detail);
}

/// <summary>
/// legacy text -> pre-cutover parser -> today's AST -> today's CalorEmitter -> native text,
/// accepted only when the oracle holds:
///   1. the native text parses with today's lexer/parser with zero diagnostics, and
///   2. today's CSharpEmitter produces identical C# for the legacy AST and the native AST.
/// </summary>
public static partial class Rewriter
{
    public static RewriteResult Rewrite(string legacy)
    {
        // 1. Legacy parse (must be clean).
        if (!TryLegacyParse(legacy, out var legacyAst, out var legacyError))
            return RewriteResult.Fail("legacy-parse", legacyError);

        // 2. Native print: first of the normalized AST (module namespace made explicit),
        //    then of the AST as parsed. 3. Candidate spellings of each, judged by the full
        //    oracle; the first that passes wins.
        RewriteResult? last = null;
        var tried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var normalize in new[] { true, false })
        {
            TryLegacyParse(legacy, out var ast, out _);
            string native;
            try
            {
                native = new CalorEmitter().Emit(normalize ? NormalizeForNativePrinting(ast!) : ast!);
            }
            catch (Exception ex)
            {
                last ??= RewriteResult.Fail("calor-emit-crash", ex.GetType().Name + ": " + FirstLine(ex.Message));
                continue;
            }
            foreach (var candidate in Candidates(native))
            {
                if (!tried.Add(candidate))
                    continue;
                var result = Check(legacy, candidate);
                if (result.Ok)
                    return result;
                if (last == null || last.Reason == "calor-emit-crash" || normalize)
                    last = result with { Candidate = candidate };
            }
        }
        return last!;
    }

    /// <summary>
    /// Textual variants of the printed native text, each with runs of blank lines collapsed
    /// first. The printer spells a legacy print
    /// statement (§P) as System.Console.WriteLine(...), which today's emitter keeps
    /// qualified, while the legacy C# was Console.WriteLine(...) under `using System;`.
    /// The unqualified variant is tried second; the oracle decides.
    /// </summary>
    private static IEnumerable<string> Candidates(string native)
    {
        var unqualified = native
            .Replace("System.Console.WriteLine(", "Console.WriteLine(", StringComparison.Ordinal)
            .Replace("System.Console.Write(", "Console.Write(", StringComparison.Ordinal)
            .Replace("new System.Collections.Generic.", "new ", StringComparison.Ordinal);
        foreach (var text in unqualified == native ? [native] : new[] { native, unqualified })
        {
            var collapsed = CollapseBlankLines(text);
            if (collapsed != text)
                yield return collapsed;
            yield return text;
        }
    }

    /// <summary>Cosmetic: the printer leaves runs of blank lines where legacy closers were.</summary>
    internal static string CollapseBlankLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>();
        foreach (var line in lines)
        {
            var blank = line.Trim().Length == 0;
            if (blank && (output.Count == 0 || output[^1].Length == 0))
                continue;
            output.Add(blank ? "" : line);
        }
        while (output.Count > 0 && output[^1].Length == 0)
            output.RemoveAt(output.Count - 1);
        return string.Join("\n", output) + "\n";
    }

    /// <summary>
    /// Legacy modules carry no namespace metadata: the legacy C# emitter wraps the module
    /// in a block namespace named after the module (unless the name is empty or _global).
    /// Native headers spell that as `module Name Name block`; give the printer the scope.
    /// </summary>
    internal static ModuleNode NormalizeForNativePrinting(ModuleNode module)
    {
        if (module.NamespaceScopes.Count > 0 || module.NamespaceIdentity != null)
            return module;
        if (string.IsNullOrEmpty(module.Name) || module.Name == "_global")
            return module;
        var scope = new NamespaceScopeInfo("ns_legacy", module.Name, module.Name, null, false, module.Span);
        return module.With(update => update.NamespaceScopes = [scope]);
    }

    /// <summary>The oracle for one candidate native text.</summary>
    private static RewriteResult Check(string legacy, string native)
    {
        // Native text must parse with zero diagnostics.
        var nativeBag = new DiagnosticBag();
        ModuleNode nativeAst;
        try
        {
            var tokens = new Parsing.Lexer(native, nativeBag).TokenizeAllForParser();
            nativeAst = new Parsing.Parser(tokens, nativeBag).Parse();
            Parsing.RawCSharpFrame.ValidateInteropReasons(tokens, nativeBag);
        }
        catch (Exception ex)
        {
            return RewriteResult.Fail("native-parse-crash", ex.GetType().Name + ": " + FirstLine(ex.Message));
        }
        if (nativeBag.Count > 0)
        {
            var d = nativeBag.First();
            return RewriteResult.Fail("native-parse-diagnostics", $"{d.Code}: {FirstLine(d.Message)}");
        }

        // C# equivalence through today's emitter. A fresh, un-normalized legacy AST feeds
        // the legacy side so neither the normalization nor anything an emitter caches on
        // nodes can leak into the comparison.
        if (!TryLegacyParse(legacy, out var legacyAst, out _))
            return RewriteResult.Fail("legacy-parse-nondeterministic");
        string csLegacy, csNative;
        try
        {
            csLegacy = new CSharpEmitter().Emit(legacyAst!);
        }
        catch (Exception ex)
        {
            return RewriteResult.Fail("csharp-emit-crash-legacy", ex.GetType().Name + ": " + FirstLine(ex.Message));
        }
        try
        {
            csNative = new CSharpEmitter().Emit(nativeAst);
        }
        catch (Exception ex)
        {
            return RewriteResult.Fail("csharp-emit-crash-native", ex.GetType().Name + ": " + FirstLine(ex.Message));
        }
        if (!CSharpEquivalence.Equivalent(csLegacy, csNative))
            return RewriteResult.Fail("csharp-mismatch", FirstDifference(csLegacy, csNative))
                with { CsLegacy = csLegacy, CsNative = csNative };

        // Effect rows are not in the C#; compare them on fresh trees.
        TryLegacyParse(legacy, out var legacyForEffects, out _);
        var nativeForEffects = ParseNative(native);
        var effectDifference = EffectEquivalence.Difference(legacyForEffects!, nativeForEffects);
        if (effectDifference != null)
            return RewriteResult.Fail("effect-mismatch", effectDifference);

        // Plain // comments are not in the AST; refuse to drop them silently.
        foreach (var comment in PlainComments(legacy))
        {
            if (!native.Contains(comment, StringComparison.Ordinal))
                return RewriteResult.Fail("comment-loss", comment);
        }

        return new RewriteResult(native, null);
    }

    private static ModuleNode ParseNative(string native)
    {
        var bag = new DiagnosticBag();
        return new Parsing.Parser(new Parsing.Lexer(native, bag).TokenizeAllForParser(), bag).Parse();
    }

    public static bool TryLegacyParse(string legacy, out ModuleNode? module, out string? error)
    {
        module = null;
        error = null;
        var bag = new DiagnosticBag();
        try
        {
            var tokens = new Lexer(legacy, bag).TokenizeAllForParser();
            module = new Parser(tokens, bag).Parse();
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + FirstLine(ex.Message);
            return false;
        }
        if (bag.HasErrors)
        {
            var d = bag.Errors[0];
            error = $"{d.Code}: {FirstLine(d.Message)}";
            return false;
        }
        return module != null;
    }

    /// <summary>
    /// Plain (non-doc) line comments in legacy text, outside string literals. Approximate
    /// string tracking is enough: a false positive only makes the rewrite more conservative.
    /// </summary>
    internal static IEnumerable<string> PlainComments(string source)
    {
        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var inString = false;
            for (var i = 0; i < line.Length - 1; i++)
            {
                var c = line[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (!inString && c == '/' && line[i + 1] == '/')
                {
                    if (i + 2 < line.Length && line[i + 2] == '/')
                        break; // doc comment: carried on the AST
                    var text = line[(i + 2)..].Trim();
                    if (text.Length > 0)
                        yield return text;
                    break;
                }
            }
        }
    }

    internal static string FirstLine(string s)
    {
        var i = s.IndexOfAny(['\r', '\n']);
        return i < 0 ? s : s[..i];
    }

    internal static string FirstDifference(string a, string b)
    {
        var la = a.Split('\n');
        var lb = b.Split('\n');
        for (var i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            var x = i < la.Length ? la[i].Trim() : "<eof>";
            var y = i < lb.Length ? lb[i].Trim() : "<eof>";
            if (x != y)
                return $"line {i + 1}: legacy `{Clip(x)}` vs native `{Clip(y)}`";
        }
        return "whitespace only";
    }

    private static string Clip(string s) => s.Length > 140 ? s[..140] + "…" : s;
}
