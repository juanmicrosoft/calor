using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 7: Task Completion Calculator
/// Measures end-to-end success rate considering context efficiency.
/// Calor's compactness should enable better task completion within context limits.
/// </summary>
public class TaskCompletionCalculator : IMetricCalculator
{
    public string Category => "TaskCompletion";

    public string Description => "Measures end-to-end completeness and context efficiency";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var calorScore = CalculateCalorTaskCompletion(context);
        var csharpScore = CalculateCSharpTaskCompletion(context);

        var details = new Dictionary<string, object>
        {
            ["calorCompleteness"] = CalculateCompleteness(context.CalorSource, context.CalorCompilation.Success),
            ["csharpCompleteness"] = CalculateCompleteness(context.CSharpSource, context.CSharpCompilation.Success),
            ["calorContextEfficiency"] = CalculateContextEfficiency(context.CalorSource),
            ["csharpContextEfficiency"] = CalculateContextEfficiency(context.CSharpSource),
            ["calorCompilationSuccess"] = context.CalorCompilation.Success,
            ["csharpCompilationSuccess"] = context.CSharpCompilation.Success
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "OverallCompletion",
            calorScore,
            csharpScore,
            details));
    }

    private static double CalculateCalorTaskCompletion(EvaluationContext context)
    {
        var score = 0.0;

        // Compilation success is primary indicator
        if (context.CalorCompilation.Success)
        {
            score += 0.4;
        }
        else
        {
            // Partial credit for partial success
            var errorCount = context.CalorCompilation.Errors.Count;
            score += Math.Max(0, 0.2 - (errorCount * 0.02));
        }

        // Completeness score
        var completeness = CalculateCompleteness(context.CalorSource, context.CalorCompilation.Success);
        score += completeness * 0.3;

        // Context efficiency (compact code can fit more in context)
        var efficiency = CalculateContextEfficiency(context.CalorSource);
        score += efficiency * 0.3;

        return Math.Min(1.0, score);
    }

    private static double CalculateCSharpTaskCompletion(EvaluationContext context)
    {
        var score = 0.0;

        // Compilation success is primary indicator
        if (context.CSharpCompilation.Success)
        {
            score += 0.4;
        }
        else
        {
            // Partial credit for partial success
            var errorCount = context.CSharpCompilation.Errors.Count;
            score += Math.Max(0, 0.2 - (errorCount * 0.02));
        }

        // Completeness score
        var completeness = CalculateCompleteness(context.CSharpSource, context.CSharpCompilation.Success);
        score += completeness * 0.3;

        // Context efficiency
        var efficiency = CalculateContextEfficiency(context.CSharpSource);
        score += efficiency * 0.3;

        return Math.Min(1.0, score);
    }

    private static double CalculateCompleteness(string source, bool compilationSuccess)
    {
        if (string.IsNullOrWhiteSpace(source))
            return 0;

        var score = 0.0;

        // Has content
        if (source.Length > 10)
            score += 0.2;

        // Balanced braces indicate structural completeness
        var openBraces = source.Count(c => c == '{');
        var closeBraces = source.Count(c => c == '}');
        if (openBraces == closeBraces && openBraces > 0)
            score += 0.2;

        // Balanced parentheses
        var openParens = source.Count(c => c == '(');
        var closeParens = source.Count(c => c == ')');
        if (openParens == closeParens && openParens > 0)
            score += 0.2;

        // Has at least one complete statement
        if (source.Contains(";") || source.Contains("}"))
            score += 0.2;

        // Compilation success indicates full completeness
        if (compilationSuccess)
            score += 0.2;

        return Math.Min(1.0, score);
    }

    private static double CalculateContextEfficiency(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return 0;

        // Calculate information density relative to size
        var tokens = TokenizeSource(source).Count;
        var lines = source.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        if (tokens == 0 || lines == 0)
            return 0;

        // Tokens per line (higher is more efficient)
        var tokensPerLine = (double)tokens / lines;

        // Normalize to 0-1 (assuming 5-20 tokens per line is normal range)
        var efficiency = Math.Min(1.0, tokensPerLine / 15.0);

        // Penalty for excessive whitespace/comments ratio
        var whitespaceCount = source.Count(char.IsWhiteSpace);
        var whitespaceRatio = (double)whitespaceCount / source.Length;

        // Good code has 20-40% whitespace
        if (whitespaceRatio > 0.5)
        {
            efficiency *= (1.0 - (whitespaceRatio - 0.5));
        }

        return efficiency;
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
