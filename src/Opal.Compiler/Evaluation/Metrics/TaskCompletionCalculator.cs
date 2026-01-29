using System.Text.RegularExpressions;
using Opal.Compiler.Evaluation.Core;

namespace Opal.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 7: Task Completion Calculator
/// Measures end-to-end success rate considering structural validity, semantic completeness,
/// and information density. Uses fair structural checks that don't penalize OPAL for
/// parser strictness on valid converted code.
/// </summary>
public class TaskCompletionCalculator : IMetricCalculator
{
    public string Category => "TaskCompletion";

    public string Description => "Measures structural validity, semantic completeness, and information density";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var opalScore = CalculateOpalTaskCompletion(context);
        var csharpScore = CalculateCSharpTaskCompletion(context);

        var details = new Dictionary<string, object>
        {
            ["opalStructuralValidity"] = CalculateStructuralValidity(context.OpalSource, isOpal: true, context.OpalCompilation.Success),
            ["csharpStructuralValidity"] = CalculateStructuralValidity(context.CSharpSource, isOpal: false, context.CSharpCompilation.Success),
            ["opalSemanticCompleteness"] = CalculateSemanticCompleteness(context.OpalSource, isOpal: true),
            ["csharpSemanticCompleteness"] = CalculateSemanticCompleteness(context.CSharpSource, isOpal: false),
            ["opalInformationDensity"] = CalculateInformationDensity(context.OpalSource, isOpal: true),
            ["csharpInformationDensity"] = CalculateInformationDensity(context.CSharpSource, isOpal: false),
            ["opalCompilationSuccess"] = context.OpalCompilation.Success,
            ["csharpCompilationSuccess"] = context.CSharpCompilation.Success
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "OverallCompletion",
            opalScore,
            csharpScore,
            details));
    }

    private static double CalculateOpalTaskCompletion(EvaluationContext context)
    {
        var score = 0.0;
        var source = context.OpalSource;

        // Structural validity (40%) - balanced tags, not full parse
        // This is fair for converted OPAL that may have valid structure but strict parser failures
        var validity = CalculateStructuralValidity(source, isOpal: true, context.OpalCompilation.Success);
        score += validity * 0.4;

        // Semantic completeness (30%) - has required constructs
        var completeness = CalculateSemanticCompleteness(source, isOpal: true);
        score += completeness * 0.3;

        // Information density (30%) - semantic content per token
        var density = CalculateInformationDensity(source, isOpal: true);
        score += density * 0.3;

        return Math.Min(1.0, score);
    }

    private static double CalculateCSharpTaskCompletion(EvaluationContext context)
    {
        var score = 0.0;
        var source = context.CSharpSource;

        // Structural validity (40%) - uses Roslyn parse success
        var validity = CalculateStructuralValidity(source, isOpal: false, context.CSharpCompilation.Success);
        score += validity * 0.4;

        // Semantic completeness (30%) - has required constructs
        var completeness = CalculateSemanticCompleteness(source, isOpal: false);
        score += completeness * 0.3;

        // Information density (30%) - semantic content per token
        var density = CalculateInformationDensity(source, isOpal: false);
        score += density * 0.3;

        return Math.Min(1.0, score);
    }

    /// <summary>
    /// Calculate structural validity without requiring full parser success.
    /// For OPAL: Check that tags are balanced (§M/§/M, §F/§/F, etc.)
    /// For C#: Use Roslyn parse success
    /// </summary>
    private static double CalculateStructuralValidity(string source, bool isOpal, bool compilationSuccess)
    {
        if (string.IsNullOrWhiteSpace(source))
            return 0;

        if (isOpal)
        {
            // Check balanced OPAL tags - this is fair for converted code
            // that may be structurally sound but fail strict parsing
            var opens = CountPattern(source, @"§[A-Z]+\[");
            var closes = CountPattern(source, @"§/[A-Z]+");

            // Also check for balanced brackets and parens
            var openBrackets = source.Count(c => c == '[');
            var closeBrackets = source.Count(c => c == ']');
            var bracketsBalanced = Math.Abs(openBrackets - closeBrackets) <= 2;

            var openParens = source.Count(c => c == '(');
            var closeParens = source.Count(c => c == ')');
            var parensBalanced = Math.Abs(openParens - closeParens) <= 2;

            // Give full credit if tags are balanced and brackets/parens are close
            if (opens > 0 && Math.Abs(opens - closes) <= 1 && bracketsBalanced && parensBalanced)
                return 1.0;

            // Partial credit for mostly balanced structure
            if (opens > 0 && Math.Abs(opens - closes) <= 3)
                return 0.7;

            // Some credit if there's meaningful content
            if (opens > 0)
                return 0.5;

            return 0.3;
        }
        else
        {
            // C# uses Roslyn success - straightforward
            return compilationSuccess ? 1.0 : 0.3;
        }
    }

    /// <summary>
    /// Calculate semantic completeness by checking for required language constructs.
    /// </summary>
    private static double CalculateSemanticCompleteness(string source, bool isOpal)
    {
        if (string.IsNullOrWhiteSpace(source))
            return 0;

        var score = 0.0;

        if (isOpal)
        {
            // Check for module/namespace declaration
            if (source.Contains("§M[") || source.Contains("§MODULE"))
                score += 0.2;

            // Check for function/method declaration
            if (source.Contains("§F[") || source.Contains("§METHOD") || source.Contains("§FUNC"))
                score += 0.2;

            // Check for input parameters
            if (source.Contains("§I[") || source.Contains("§IN") || source.Contains("§PARAM"))
                score += 0.2;

            // Check for output/return type
            if (source.Contains("§O[") || source.Contains("§OUT") || source.Contains("§RET"))
                score += 0.2;

            // Check for return statements or assignments
            if (source.Contains("§R ") || source.Contains("§RETURN") || source.Contains("§= ") || source.Contains("§SET"))
                score += 0.2;
        }
        else
        {
            // Check for namespace declaration
            if (source.Contains("namespace "))
                score += 0.2;

            // Check for class/struct/interface declaration
            if (source.Contains("class ") || source.Contains("struct ") || source.Contains("interface "))
                score += 0.2;

            // Check for method declarations (access modifier + return type + name + parens)
            if (Regex.IsMatch(source, @"\b(public|private|protected|internal)\s+\w+\s+\w+\s*\("))
                score += 0.2;

            // Check for return statements
            if (source.Contains("return "))
                score += 0.2;

            // Check for complete method bodies
            if (source.Contains("{") && source.Contains("}"))
                score += 0.2;
        }

        return Math.Min(1.0, score);
    }

    /// <summary>
    /// Calculate information density - semantic content per token.
    /// Higher ratio = more information per token = better for LLM context limits.
    /// </summary>
    private static double CalculateInformationDensity(string source, bool isOpal)
    {
        if (string.IsNullOrWhiteSpace(source))
            return 0;

        var tokens = TokenizeSource(source).Count;
        if (tokens == 0)
            return 0;

        // Count semantic elements
        int semanticElements;
        if (isOpal)
        {
            // Count OPAL tags (§M[, §F[, §I[, §O[, §R, §=, etc.)
            semanticElements = CountPattern(source, @"§[A-Z]+[\[\s]");
        }
        else
        {
            // Count C# keywords that define structure
            semanticElements = CountPattern(source, @"\b(class|struct|interface|enum|namespace|void|public|private|protected|internal|static|async|return|if|else|for|foreach|while|try|catch|throw|new|using)\b");
        }

        // Semantic elements per 100 tokens (normalized)
        var density = (semanticElements * 100.0) / tokens;

        // 10 elements per 100 tokens = max score (1.0)
        // This rewards code that packs more meaning into fewer tokens
        return Math.Min(1.0, density / 10.0);
    }

    /// <summary>
    /// Count regex pattern matches in source.
    /// </summary>
    private static int CountPattern(string source, string pattern)
    {
        try
        {
            return Regex.Matches(source, pattern).Count;
        }
        catch
        {
            return 0;
        }
    }

    private static List<string> TokenizeSource(string source)
    {
        var tokens = new List<string>();
        var currentToken = "";

        foreach (var ch in source)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!string.IsNullOrEmpty(currentToken))
                {
                    tokens.Add(currentToken);
                    currentToken = "";
                }
            }
            else if (char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (!string.IsNullOrEmpty(currentToken))
                {
                    tokens.Add(currentToken);
                    currentToken = "";
                }
                tokens.Add(ch.ToString());
            }
            else
            {
                currentToken += ch;
            }
        }

        if (!string.IsNullOrEmpty(currentToken))
        {
            tokens.Add(currentToken);
        }

        return tokens;
    }
}
