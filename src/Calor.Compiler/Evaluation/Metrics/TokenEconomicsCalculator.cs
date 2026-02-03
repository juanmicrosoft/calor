using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 1: Token Economics Calculator
/// Measures the token count and character count comparison between Calor and C#.
/// Calor is hypothesized to be ~40-60% more compact.
/// </summary>
public class TokenEconomicsCalculator : IMetricCalculator
{
    public string Category => "TokenEconomics";

    public string Description => "Measures token and character counts to compare code compactness";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate token counts using simple tokenization
        var calorTokens = TokenizeSource(context.CalorSource);
        var csharpTokens = TokenizeSource(context.CSharpSource);

        var calorTokenCount = calorTokens.Count;
        var csharpTokenCount = csharpTokens.Count;

        // Character counts (excluding whitespace for fair comparison)
        var calorCharCount = context.CalorSource.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "").Length;
        var csharpCharCount = context.CSharpSource.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "").Length;

        // Line counts
        var calorLineCount = context.CalorSource.Split('\n').Length;
        var csharpLineCount = context.CSharpSource.Split('\n').Length;

        // Calculate ratios (lower is better for Calor, so C#/Calor gives advantage ratio)
        var tokenRatio = calorTokenCount > 0 ? (double)csharpTokenCount / calorTokenCount : 1.0;
        var charRatio = calorCharCount > 0 ? (double)csharpCharCount / calorCharCount : 1.0;
        var lineRatio = calorLineCount > 0 ? (double)csharpLineCount / calorLineCount : 1.0;

        // Composite advantage (geometric mean of ratios)
        var compositeAdvantage = Math.Pow(tokenRatio * charRatio * lineRatio, 1.0 / 3.0);

        var details = new Dictionary<string, object>
        {
            ["calorTokenCount"] = calorTokenCount,
            ["csharpTokenCount"] = csharpTokenCount,
            ["tokenRatio"] = tokenRatio,
            ["calorCharCount"] = calorCharCount,
            ["csharpCharCount"] = csharpCharCount,
            ["charRatio"] = charRatio,
            ["calorLineCount"] = calorLineCount,
            ["csharpLineCount"] = csharpLineCount,
            ["lineRatio"] = lineRatio,
            ["calorTokens"] = calorTokens.Take(50).ToList(),
            ["csharpTokens"] = csharpTokens.Take(50).ToList()
        };

        return Task.FromResult(MetricResult.CreateLowerIsBetter(
            Category,
            "CompositeTokenEconomics",
            calorTokenCount,
            csharpTokenCount,
            details));
    }

    /// <summary>
    /// Simple tokenizer that splits source code into tokens.
    /// Approximates LLM tokenization by splitting on whitespace and punctuation.
    /// </summary>
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
