using Opal.Evaluation.Core;

namespace Opal.Evaluation.Metrics;

/// <summary>
/// Category 1: Token Economics Calculator
/// Measures the token count and character count comparison between OPAL and C#.
/// OPAL is hypothesized to be ~40-60% more compact.
/// </summary>
public class TokenEconomicsCalculator : IMetricCalculator
{
    public string Category => "TokenEconomics";

    public string Description => "Measures token and character counts to compare code compactness";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate token counts using simple tokenization
        var opalTokens = TokenizeSource(context.OpalSource);
        var csharpTokens = TokenizeSource(context.CSharpSource);

        var opalTokenCount = opalTokens.Count;
        var csharpTokenCount = csharpTokens.Count;

        // Character counts (excluding whitespace for fair comparison)
        var opalCharCount = context.OpalSource.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "").Length;
        var csharpCharCount = context.CSharpSource.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "").Length;

        // Line counts
        var opalLineCount = context.OpalSource.Split('\n').Length;
        var csharpLineCount = context.CSharpSource.Split('\n').Length;

        // Calculate ratios (lower is better for OPAL, so C#/OPAL gives advantage ratio)
        var tokenRatio = opalTokenCount > 0 ? (double)csharpTokenCount / opalTokenCount : 1.0;
        var charRatio = opalCharCount > 0 ? (double)csharpCharCount / opalCharCount : 1.0;
        var lineRatio = opalLineCount > 0 ? (double)csharpLineCount / opalLineCount : 1.0;

        // Composite advantage (geometric mean of ratios)
        var compositeAdvantage = Math.Pow(tokenRatio * charRatio * lineRatio, 1.0 / 3.0);

        var details = new Dictionary<string, object>
        {
            ["opalTokenCount"] = opalTokenCount,
            ["csharpTokenCount"] = csharpTokenCount,
            ["tokenRatio"] = tokenRatio,
            ["opalCharCount"] = opalCharCount,
            ["csharpCharCount"] = csharpCharCount,
            ["charRatio"] = charRatio,
            ["opalLineCount"] = opalLineCount,
            ["csharpLineCount"] = csharpLineCount,
            ["lineRatio"] = lineRatio,
            ["opalTokens"] = opalTokens.Take(50).ToList(), // Sample of tokens
            ["csharpTokens"] = csharpTokens.Take(50).ToList()
        };

        return Task.FromResult(MetricResult.CreateLowerIsBetter(
            Category,
            "CompositeTokenEconomics",
            opalTokenCount,
            csharpTokenCount,
            details));
    }

    /// <summary>
    /// Calculates detailed token economics with all sub-metrics.
    /// </summary>
    public List<MetricResult> CalculateDetailedMetrics(EvaluationContext context)
    {
        var results = new List<MetricResult>();

        var opalTokens = TokenizeSource(context.OpalSource);
        var csharpTokens = TokenizeSource(context.CSharpSource);

        // Token count
        results.Add(MetricResult.CreateLowerIsBetter(
            Category,
            "TokenCount",
            opalTokens.Count,
            csharpTokens.Count));

        // Character count (excluding whitespace)
        var opalChars = context.OpalSource.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "").Length;
        var csharpChars = context.CSharpSource.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "").Length;
        results.Add(MetricResult.CreateLowerIsBetter(
            Category,
            "CharacterCount",
            opalChars,
            csharpChars));

        // Line count
        var opalLines = context.OpalSource.Split('\n').Length;
        var csharpLines = context.CSharpSource.Split('\n').Length;
        results.Add(MetricResult.CreateLowerIsBetter(
            Category,
            "LineCount",
            opalLines,
            csharpLines));

        // Average token length
        var opalAvgLen = opalTokens.Count > 0 ? opalTokens.Average(t => t.Length) : 0;
        var csharpAvgLen = csharpTokens.Count > 0 ? csharpTokens.Average(t => t.Length) : 0;
        results.Add(new MetricResult(
            Category,
            "AverageTokenLength",
            opalAvgLen,
            csharpAvgLen,
            1.0, // Neutral - just informational
            new Dictionary<string, object>()));

        return results;
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
