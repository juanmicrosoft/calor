namespace Calor.LanguageServer.Utilities;

/// <summary>
/// The native call that encloses a cursor: the callee as written (for example <c>Add</c> or
/// <c>Math.Max</c>) and the zero-based index of the argument the cursor is in.
/// </summary>
internal sealed record NativeCallContext(string Callee, int ArgumentIndex)
{
    /// <summary>The callee's last name segment (<c>Max</c> for <c>Math.Max</c>).</summary>
    public string Name => Callee[(Callee.LastIndexOf('.') + 1)..];

    // Words that can sit directly before '(' without being a call.
    private static readonly HashSet<string> NotCallees = new(StringComparer.Ordinal)
    {
        "if", "while", "for", "each", "do", "match", "case", "when", "requires", "proof",
        "invariant", "return", "yield", "throw", "lock", "using", "set", "let", "var", "in",
        "else", "not", "and", "or", "is", "as", "await", "func", "module", "class", "new",
    };

    /// <summary>
    /// Finds the innermost unclosed call around <paramref name="offset"/>. Native calls are
    /// written <c>name(args)</c> with no space before the parenthesis, so a parenthesis after
    /// whitespace (an <c>if (...)</c> condition or a <c>func Name (...)</c> header) or after a
    /// keyword is grouping, not a call. String and character literals and line comments are
    /// skipped. Returns null when the cursor is not inside a call's argument list.
    /// </summary>
    public static NativeCallContext? Find(string source, int offset)
    {
        offset = Math.Clamp(offset, 0, source.Length);
        var frames = new Stack<(char Open, string? Callee, int Commas)>();

        for (var i = 0; i < offset; i++)
        {
            var c = source[i];
            switch (c)
            {
                case '/' when i + 1 < source.Length && source[i + 1] == '/':
                    i = SkipTo(source, i, '\n', offset) - 1;
                    break;
                case '"' or '\'':
                    i = SkipLiteral(source, i, c, offset);
                    break;
                case '(':
                    frames.Push(('(', CalleeBefore(source, i), 0));
                    break;
                case '[' or '{':
                    frames.Push((c, null, 0));
                    break;
                case ')' or ']' or '}':
                    var open = c switch { ')' => '(', ']' => '[', _ => '{' };
                    // Pop to the matching opener; an unbalanced closer is left alone.
                    if (frames.Any(frame => frame.Open == open))
                    {
                        while (frames.Pop().Open != open) { }
                    }
                    break;
                case ',' when frames.Count > 0 && frames.Peek().Open == '(':
                    var top = frames.Pop();
                    frames.Push(top with { Commas = top.Commas + 1 });
                    break;
            }
        }

        foreach (var frame in frames)
        {
            if (frame.Callee != null)
                return new NativeCallContext(frame.Callee, frame.Commas);
        }
        return null;
    }

    private static int SkipTo(string source, int start, char end, int limit)
    {
        var index = source.IndexOf(end, start);
        return index < 0 || index > limit ? limit : index;
    }

    /// <summary>Returns the index of the literal's closing quote (or the limit when unclosed).</summary>
    private static int SkipLiteral(string source, int start, char quote, int limit)
    {
        for (var i = start + 1; i < limit; i++)
        {
            if (source[i] == '\\')
                i++;
            else if (source[i] == quote || source[i] == '\n')
                return i;
        }
        return limit;
    }

    /// <summary>The callee written directly before the '(' at <paramref name="paren"/>, if any.</summary>
    private static string? CalleeBefore(string source, int paren)
    {
        var end = paren;
        // Explicit generic arguments: Name<T, U>(...)
        if (end > 0 && source[end - 1] == '>')
        {
            var depth = 0;
            var i = end - 1;
            for (; i >= 0; i--)
            {
                if (source[i] == '>') depth++;
                else if (source[i] == '<' && --depth == 0) break;
                else if (source[i] is '\n' or '(' or ')') return null;
            }
            if (i < 0) return null;
            end = i;
        }

        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] is '_' or '.' or '@'
                   || (source[start - 1] == '?' && source[start] == '.')))
            start--;
        if (start == end)
            return null;

        var callee = source[start..end].Replace("@", "").Replace("?.", ".");
        if (callee.StartsWith('.') || callee.EndsWith('.') || char.IsDigit(callee[0]) || NotCallees.Contains(callee))
            return null;
        return callee;
    }
}
