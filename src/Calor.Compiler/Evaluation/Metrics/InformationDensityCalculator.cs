using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 6: Information Density Calculator
/// Measures semantic elements per token ratio.
/// Calor carries more semantic information inline (effects, contracts).
/// </summary>
public class InformationDensityCalculator : IMetricCalculator
{
    public string Category => "InformationDensity";

    public string Description => "Measures semantic information per token ratio";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var calorDensity = CalculateCalorDensity(context);
        var csharpDensity = CalculateCSharpDensity(context);

        var calorTokens = TokenizeSource(context.CalorSource).Count;
        var csharpTokens = TokenizeSource(context.CSharpSource).Count;

        var details = new Dictionary<string, object>
        {
            ["calorSemanticElements"] = CountCalorSemanticElements(context.CalorSource),
            ["csharpSemanticElements"] = CountCSharpSemanticElements(context.CSharpSource),
            ["calorTokenCount"] = calorTokens,
            ["csharpTokenCount"] = csharpTokens,
            ["calorDensity"] = calorDensity,
            ["csharpDensity"] = csharpDensity
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "SemanticDensity",
            calorDensity,
            csharpDensity,
            details));
    }

    private static double CalculateCalorDensity(EvaluationContext context)
    {
        var source = context.CalorSource;
        var tokenCount = TokenizeSource(source).Count;

        if (tokenCount == 0)
            return 0;

        var semanticElements = CountCalorSemanticElements(source);
        var totalElements = semanticElements.Values.Sum();

        // Semantic elements per token, normalized
        return totalElements / (double)tokenCount * 10.0; // Scale factor
    }

    private static double CalculateCSharpDensity(EvaluationContext context)
    {
        var source = context.CSharpSource;
        var tokenCount = TokenizeSource(source).Count;

        if (tokenCount == 0)
            return 0;

        var semanticElements = CountCSharpSemanticElements(source);
        var totalElements = semanticElements.Values.Sum();

        // Semantic elements per token, normalized
        return totalElements / (double)tokenCount * 10.0; // Scale factor
    }

    private static Dictionary<string, int> CountCalorSemanticElements(string source)
    {
        var elements = new Dictionary<string, int>
        {
            // Structural elements
            ["modules"] = CountOccurrences(source, "§M["),
            ["functions"] = CountOccurrences(source, "§F["),
            ["variables"] = CountOccurrences(source, "§V["),
            ["parameters"] = CountOccurrences(source, "§I[") + CountOccurrences(source, "§O["),

            // Contract elements (high semantic value)
            ["requires"] = CountOccurrences(source, "§REQ") * 2, // Weight contracts higher
            ["ensures"] = CountOccurrences(source, "§ENS") * 2,
            ["invariants"] = CountOccurrences(source, "§INV") * 2,

            // Effect declarations (high semantic value)
            ["effects"] = CountOccurrences(source, "§E[") * 2,

            // Control flow
            ["conditionals"] = CountOccurrences(source, "§IF") + CountOccurrences(source, "§ELSE"),
            ["loops"] = CountOccurrences(source, "§LOOP") + CountOccurrences(source, "§FOR") +
                       CountOccurrences(source, "§WHILE"),
            ["matches"] = CountOccurrences(source, "§MATCH") * 2, // Pattern matching is semantic-rich

            // Calls and returns
            ["calls"] = CountOccurrences(source, "§C["),
            ["returns"] = CountOccurrences(source, "§RET"),

            // Type annotations
            ["types"] = CountOccurrences(source, ":") // Approximate type annotations
        };

        return elements;
    }

    private static Dictionary<string, int> CountCSharpSemanticElements(string source)
    {
        var elements = new Dictionary<string, int>
        {
            // Structural elements
            ["classes"] = CountOccurrences(source, "class "),
            ["structs"] = CountOccurrences(source, "struct "),
            ["interfaces"] = CountOccurrences(source, "interface "),
            ["methods"] = CountMethodCount(source),
            ["properties"] = CountOccurrences(source, " get;") + CountOccurrences(source, " set;"),

            // Control flow
            ["conditionals"] = CountOccurrences(source, "if (") + CountOccurrences(source, "if(") +
                              CountOccurrences(source, "else"),
            ["loops"] = CountOccurrences(source, "for (") + CountOccurrences(source, "for(") +
                       CountOccurrences(source, "while (") + CountOccurrences(source, "while(") +
                       CountOccurrences(source, "foreach (") + CountOccurrences(source, "foreach("),
            ["switches"] = CountOccurrences(source, "switch (") + CountOccurrences(source, "switch("),

            // Error handling
            ["exceptions"] = CountOccurrences(source, "throw ") + CountOccurrences(source, "try") +
                            CountOccurrences(source, "catch"),

            // LINQ (semantic-rich)
            ["linq"] = CountOccurrences(source, ".Select(") + CountOccurrences(source, ".Where(") +
                      CountOccurrences(source, ".OrderBy(") + CountOccurrences(source, ".GroupBy("),

            // Async
            ["async"] = CountOccurrences(source, "async ") + CountOccurrences(source, "await "),

            // Attributes
            ["attributes"] = CountAttributeCount(source)
        };

        return elements;
    }

    private static int CountMethodCount(string source)
    {
        var count = 0;
        var lines = source.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if ((trimmed.Contains("public ") || trimmed.Contains("private ") ||
                 trimmed.Contains("protected ") || trimmed.Contains("internal ") ||
                 trimmed.Contains("static ")) &&
                trimmed.Contains("(") && !trimmed.StartsWith("//") &&
                !trimmed.Contains("="))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountAttributeCount(string source)
    {
        var count = 0;
        var inAttribute = false;

        foreach (var ch in source)
        {
            if (ch == '[' && !inAttribute)
            {
                inAttribute = true;
            }
            else if (ch == ']' && inAttribute)
            {
                count++;
                inAttribute = false;
            }
        }

        return count;
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

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
