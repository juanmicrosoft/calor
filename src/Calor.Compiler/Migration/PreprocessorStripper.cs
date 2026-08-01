using System.Text;

namespace Calor.Compiler.Migration;

/// <summary>
/// One stripped conditional-compilation directive, with its 1-based source line
/// and, for branch-opening directives, how many content lines were dropped
/// because the branch was inactive. Feeds the #770 loss accounting.
/// </summary>
public sealed record StrippedDirective(int Line, string Directive, int DroppedLines);

/// <summary>
/// Result of preprocessor stripping: the stripped source plus the semantic
/// directives that were removed (conditional compilation only — cosmetic
/// directives like #region/#pragma are not reported as losses).
/// </summary>
public sealed record PreprocessorStripResult(string Source, IReadOnlyList<StrippedDirective> ConditionalDirectives);

/// <summary>
/// Strips C# preprocessor directives from source code before conversion.
/// Keeps the primary (#if true / first) branch of conditional compilation blocks.
/// </summary>
public static class PreprocessorStripper
{
    /// <summary>
    /// Strips preprocessor directives, keeping the first branch of #if/#else/#endif blocks.
    /// Also strips #region, #endregion, #pragma, #nullable, #warning, #error, #line directives.
    /// </summary>
    public static string Strip(string source) => StripWithReport(source).Source;

    /// <summary>
    /// Strips preprocessor directives and reports every stripped conditional
    /// directive with its location and dropped-branch line counts, so the
    /// caller can record structured losses (#770 item 8): keeping an
    /// unevaluated first branch and deleting alternates is a semantic loss,
    /// not a cosmetic cleanup.
    /// </summary>
    public static PreprocessorStripResult StripWithReport(string source)
    {
        var lines = source.Split('\n');
        var result = new StringBuilder();
        var stripped = new List<StrippedDirective>();
        // Stack tracks whether each nesting level is currently emitting lines
        var activeStack = new Stack<bool>();
        activeStack.Push(true); // top-level is always active

        // Index into `stripped` of the directive owning the currently-dropping
        // branch at each nesting level (-1 = branch is active, nothing dropping).
        var droppingDirective = new Stack<int>();
        droppingDirective.Push(-1);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            var trimmed = rawLine.TrimStart();
            if (trimmed.StartsWith("#if ") || trimmed.StartsWith("#if\t") || trimmed == "#if")
            {
                // Push: first branch is active only if parent is active
                stripped.Add(new StrippedDirective(lineIndex + 1, trimmed.TrimEnd('\r').TrimEnd(), 0));
                activeStack.Push(activeStack.Peek());
                droppingDirective.Push(-1);
                continue;
            }

            if (trimmed.StartsWith("#elif ") || trimmed.StartsWith("#elif\t") || trimmed == "#elif" ||
                trimmed.StartsWith("#else") && (trimmed.Length == 5 || char.IsWhiteSpace(trimmed[5]) || trimmed[5] == '/' || trimmed[5] == '\r'))
            {
                // Switch to inactive for alternate branches
                if (activeStack.Count > 1)
                {
                    activeStack.Pop();
                    activeStack.Push(false);
                    droppingDirective.Pop();
                    stripped.Add(new StrippedDirective(lineIndex + 1, trimmed.TrimEnd('\r').TrimEnd(), 0));
                    droppingDirective.Push(stripped.Count - 1);
                }
                continue;
            }

            if (trimmed.StartsWith("#endif") && (trimmed.Length == 6 || char.IsWhiteSpace(trimmed[6]) || trimmed[6] == '/' || trimmed[6] == '\r'))
            {
                if (activeStack.Count > 1)
                {
                    activeStack.Pop();
                    droppingDirective.Pop();
                }
                continue;
            }

            // Strip standalone directives
            if (IsStandaloneDirective(trimmed))
            {
                continue;
            }

            // Emit line only if all nesting levels are active
            if (activeStack.Peek())
            {
                result.Append(rawLine);
                result.Append('\n');
            }
            else if (droppingDirective.Peek() >= 0 && !string.IsNullOrWhiteSpace(trimmed))
            {
                // Content line dropped from an inactive branch — attribute it to
                // the #elif/#else directive that opened the branch.
                var idx = droppingDirective.Peek();
                stripped[idx] = stripped[idx] with { DroppedLines = stripped[idx].DroppedLines + 1 };
            }
        }

        // Remove trailing newline that we always append
        if (result.Length > 0 && result[result.Length - 1] == '\n')
        {
            result.Length--;
        }

        return new PreprocessorStripResult(result.ToString(), stripped);
    }

    private static bool IsStandaloneDirective(string trimmed)
    {
        return trimmed.StartsWith("#region") ||
               trimmed.StartsWith("#endregion") ||
               trimmed.StartsWith("#pragma") ||
               trimmed.StartsWith("#nullable") ||
               trimmed.StartsWith("#warning") ||
               trimmed.StartsWith("#error") ||
               trimmed.StartsWith("#line");
    }
}
