using System.Text.RegularExpressions;
using Calor.Compiler.Mcp.Tools;

namespace Calor.Compiler.Migration;

/// <summary>
/// Describes a single fix applied by the PostConversionFixer.
/// </summary>
public sealed class AppliedFix
{
    public required string Rule { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Result of running the PostConversionFixer.
/// </summary>
public sealed class FixResult
{
    public required string FixedSource { get; init; }
    public required bool WasModified { get; init; }
    public required List<AppliedFix> AppliedFixes { get; init; }
}

/// <summary>
/// Auto-fixes known invalid patterns in converter output.
/// Applies ordered regex-based rules and re-parses after each pass.
/// </summary>
public sealed class PostConversionFixer
{
    private const int MaxPasses = 3;

    private static readonly List<(string Name, string Description, Func<string, (string Result, bool Changed)> Apply)> Rules = new()
    {
        ("OrphanedClosingTag", "Remove unmatched §/NEW or §/C closing tags at statement level",
            FixOrphanedClosingTags),

        ("UnmatchedParentheses", "Remove extra closing parentheses or add missing opening parentheses",
            FixUnmatchedParentheses),

        ("CommaLeaks", "Strip raw commas from Lisp expression positions",
            FixCommaLeaks),

        ("GenericInLisp", "Extract generic type arguments from Lisp call position to binding",
            FixGenericInLisp),

        ("InlineErrLam", "Extract §ERR/§LAM from inside Lisp call arguments to preceding binding",
            FixInlineErrLam),

        ("IfExpressionArrow", "Insert missing → after IF condition in expression context",
            FixIfExpressionArrow),
    };

    /// <summary>
    /// Attempts to fix known invalid patterns in the given Calor source.
    /// Re-parses after each pass and returns immediately if valid.
    /// </summary>
    public FixResult Fix(string calorSource)
    {
        var allFixes = new List<AppliedFix>();
        var current = calorSource;

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var passModified = false;

            foreach (var (name, description, apply) in Rules)
            {
                var (result, changed) = apply(current);
                if (changed)
                {
                    current = result;
                    passModified = true;
                    allFixes.Add(new AppliedFix { Rule = name, Description = description });
                }
            }

            if (!passModified)
                break;

            // Re-parse to check if source is now valid
            var parseResult = CalorSourceHelper.Parse(current, "post-fix-check.calr");
            if (parseResult.IsSuccess)
            {
                return new FixResult
                {
                    FixedSource = current,
                    WasModified = true,
                    AppliedFixes = allFixes
                };
            }
        }

        return new FixResult
        {
            FixedSource = current,
            WasModified = allFixes.Count > 0,
            AppliedFixes = allFixes
        };
    }

    /// <summary>
    /// Rule A: Remove orphaned §/NEW or §/C closing tags that appear at statement level
    /// without a matching opener on the same line.
    /// </summary>
    private static (string Result, bool Changed) FixOrphanedClosingTags(string source)
    {
        // Match lines that consist solely of an orphaned closing tag (with optional whitespace)
        // Note: \r? needed for Windows line endings where $ matches before \n but after \r
        var pattern = @"^[ \t]*§/(?:NEW|C)\{[^}]*\}[ \t]*\r?$";
        var result = Regex.Replace(source, pattern, "", RegexOptions.Multiline);

        // Clean up resulting blank lines (collapse multiple blank lines to one)
        result = Regex.Replace(result, @"(\r?\n){3,}", "\n\n");

        return (result, result != source);
    }

    /// <summary>
    /// Rule B: Fix unmatched parentheses — remove trailing extra ')' on lines,
    /// or balance simple cases.
    /// </summary>
    private static (string Result, bool Changed) FixUnmatchedParentheses(string source)
    {
        var lines = source.Split('\n');
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var depth = 0;
            foreach (var ch in line)
            {
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
            }

            if (depth < 0)
            {
                // More ')' than '(' — remove excess from end
                var excess = -depth;
                var chars = line.ToCharArray();
                for (var j = chars.Length - 1; j >= 0 && excess > 0; j--)
                {
                    if (chars[j] == ')')
                    {
                        chars[j] = ' ';
                        excess--;
                        changed = true;
                    }
                }
                lines[i] = new string(chars).TrimEnd();
            }
        }

        return (string.Join("\n", lines), changed);
    }

    /// <summary>
    /// Rule C: Strip raw commas that leaked into Lisp expression positions.
    /// Only targets commas inside Lisp-style calls that start with an operator,
    /// e.g., (+ a, b) → (+ a b). Does NOT strip commas from inline signatures
    /// like (i32:a, i32:b) → i32 which are legitimate.
    /// </summary>
    private static (string Result, bool Changed) FixCommaLeaks(string source)
    {
        // Only match commas that appear after a Lisp operator call start:
        // (operator arg1, arg2) where operator is +, -, *, /, %, ==, !=, <, >, etc.
        // The lookbehind matches "(operator " followed by any non-paren chars, then comma.
        var pattern = @"(?<=\((?:\+|-|\*|/|%|==|!=|<=?|>=?|&&|\|\||!|and|or|not|len|concat|fmt|str|lower|upper|contains|substr|char-at)\s[^)]*),(?=\s)";
        var result = Regex.Replace(source, pattern, "");

        return (result, result != source);
    }

    /// <summary>
    /// Rule D: Fix generic type arguments in Lisp call position.
    /// Converts patterns like (SomeType&lt;T&gt; args) by extracting the generic portion.
    /// </summary>
    private static (string Result, bool Changed) FixGenericInLisp(string source)
    {
        // Match cases where a generic type <T> appears directly inside a Lisp expression
        // e.g., (§C{Method} §A §C{Option<int>.Some} ... )
        // Replace <...> with {of:...} comment notation to avoid parser confusion
        var pattern = @"(?<=§C\{[^}]*)<([^>]+)>(?=[^}]*\})";
        var result = Regex.Replace(source, pattern, "{of:$1}");

        if (result != source)
            return (result, true);

        // Also handle bare <T> in expression args: (.Method<int> arg) → (.Method{of:int} arg)
        var pattern2 = @"(?<=\.\w+)<(\w+(?:,\s*\w+)*)>(?=[\s)])";
        result = Regex.Replace(source, pattern2, "{of:$1}");

        return (result, result != source);
    }

    /// <summary>
    /// Rule E: Extract §ERR or §LAM tags from inside Lisp call arguments to preceding bindings.
    /// When §ERR{...} or §LAM{...} appears as a Lisp call argument, it causes parse errors.
    /// This rule extracts them into §B{autoN} bindings on the line before.
    /// </summary>
    private static (string Result, bool Changed) FixInlineErrLam(string source)
    {
        var lines = source.Split('\n');
        var changed = false;
        var autoCounter = 0;
        var outputLines = new List<string>(lines.Length + 10);

        // Pattern: §A followed by §ERR{...} content, inside a call line
        var errInCallPattern = new Regex(@"(§A\s+)§ERR\{([^}]*)\}\s+([^\n§]*)");
        // Pattern: §A followed by §LAM{...}...§/LAM{...}, inside a call line
        var lamInCallPattern = new Regex(@"(§A\s+)(§LAM\{[^}]*\}[^\n]*§/LAM\{[^}]*\})");

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Check for inline §ERR inside a call argument
            var errMatch = errInCallPattern.Match(line);
            if (errMatch.Success && line.Contains("§C{"))
            {
                autoCounter++;
                var bindingName = $"__autoErr{autoCounter}";
                var indent = GetIndent(line);
                var errTag = errMatch.Groups[2].Value;
                var errArg = errMatch.Groups[3].Value.Trim();

                // Emit binding before this line
                var bindingLine = string.IsNullOrEmpty(errArg)
                    ? $"{indent}§B{{{bindingName}}} §ERR{{{errTag}}}"
                    : $"{indent}§B{{{bindingName}}} §ERR{{{errTag}}} {errArg}";
                outputLines.Add(bindingLine);

                // Replace inline §ERR with binding reference
                line = errInCallPattern.Replace(line, $"$1{bindingName}", 1);
                changed = true;
            }

            // Check for inline §LAM inside a call argument
            var lamMatch = lamInCallPattern.Match(line);
            if (lamMatch.Success && line.Contains("§C{"))
            {
                autoCounter++;
                var bindingName = $"__autoLam{autoCounter}";
                var indent = GetIndent(line);
                var lamBlock = lamMatch.Groups[2].Value;

                // Emit binding before this line
                outputLines.Add($"{indent}§B{{{bindingName}}} {lamBlock}");

                // Replace inline §LAM with binding reference
                line = lamInCallPattern.Replace(line, $"$1{bindingName}", 1);
                changed = true;
            }

            outputLines.Add(line);
        }

        return (string.Join("\n", outputLines), changed);
    }

    private static string GetIndent(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }

    /// <summary>
    /// Rule F: Insert missing → after IF condition when used in expression context.
    /// Targets: §IF{id} (condition) expression → should be §IF{id} (condition) → expression
    /// </summary>
    private static (string Result, bool Changed) FixIfExpressionArrow(string source)
    {
        // Match §IF or §EI followed by a parenthesized condition, then NOT followed by →
        // but followed by §R (return), which indicates expression context
        var pattern = @"(§(?:IF|EI)\{[^}]*\}\s*\([^)]*\))\s+(?!→)(§R\s)";
        var result = Regex.Replace(source, pattern, "$1 → $2");

        return (result, result != source);
    }
}
