using System.Text;

namespace Calor.Compiler.Migration;

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
    public static string Strip(string source)
    {
        var lines = source.Split('\n');
        var result = new StringBuilder();
        // Stack tracks whether each nesting level is currently emitting lines
        var activeStack = new Stack<bool>();
        activeStack.Push(true); // top-level is always active

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();
            if (trimmed.StartsWith("#if ") || trimmed.StartsWith("#if\t") || trimmed == "#if")
            {
                // Push: first branch is active only if parent is active
                activeStack.Push(activeStack.Peek());
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
                }
                continue;
            }

            if (trimmed.StartsWith("#endif") && (trimmed.Length == 6 || char.IsWhiteSpace(trimmed[6]) || trimmed[6] == '/' || trimmed[6] == '\r'))
            {
                if (activeStack.Count > 1)
                {
                    activeStack.Pop();
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
        }

        // Remove trailing newline that we always append
        if (result.Length > 0 && result[result.Length - 1] == '\n')
        {
            result.Length--;
        }

        return result.ToString();
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
