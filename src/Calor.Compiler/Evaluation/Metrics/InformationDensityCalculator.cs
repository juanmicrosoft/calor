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

        var calorTokens = CountTokens(context.CalorSource);
        var csharpTokens = CountTokens(context.CSharpSource);

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
        var tokenCount = CountTokens(source);

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
        var tokenCount = CountTokens(source);

        if (tokenCount == 0)
            return 0;

        var semanticElements = CountCSharpSemanticElements(source);
        var totalElements = semanticElements.Values.Sum();

        // Semantic elements per token, normalized
        return totalElements / (double)tokenCount * 10.0; // Scale factor
    }

    private static Dictionary<string, int> CountCalorSemanticElements(string source)
    {
        // Native (0.30) spellings of the constructs the legacy counter found by section marker.
        var elements = new Dictionary<string, int>
        {
            // Structural elements
            ["modules"] = NativeCalorSyntax.Module.Matches(source).Count,
            ["functions"] = NativeCalorSyntax.Function.Matches(source).Count,
            ["variables"] = NativeCalorSyntax.Binding.Matches(source).Count,
            ["parameters"] = NativeCalorSyntax.CountTypedParameters(source) +
                             NativeCalorSyntax.TypedOutput.Matches(source).Count,

            // Contract elements (high semantic value - weight 2x)
            ["requires"] = NativeCalorSyntax.Precondition.Matches(source).Count * 2,
            ["ensures"] = NativeCalorSyntax.Ensures.Matches(source).Count * 2,
            ["invariants"] = NativeCalorSyntax.Invariant.Matches(source).Count * 2,

            // Effect declarations (high semantic value - weight 2x)
            ["effects"] = NativeCalorSyntax.EffectRow.Matches(source).Count * 2,

            // Control flow
            ["conditionals"] = NativeCalorSyntax.If.Matches(source).Count +
                               NativeCalorSyntax.Else.Matches(source).Count +
                               NativeCalorSyntax.ElseIf.Matches(source).Count,
            ["loops"] = NativeCalorSyntax.Loop.Matches(source).Count,
            ["matches"] = NativeCalorSyntax.Match.Matches(source).Count * 2, // Pattern matching is semantic-rich

            // Returns
            ["returns"] = NativeCalorSyntax.Return.Matches(source).Count,

            // Calls
            ["calls"] = NativeCalorSyntax.Call.Matches(source).Count,

            // Type annotations - built-in type names. Typed parameters and return types are
            // already counted once under "parameters" and are not added again here.
            ["types"] = NativeCalorSyntax.TypeName.Matches(source).Count,

            // Native source has no closing tags: blocks end by indentation.
            ["closingTags"] = 0,

            // Mutability markers (unique to Calor): `var` bindings
            ["mutability"] = NativeCalorSyntax.MutableBinding.Matches(source).Count
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

            // Returns
            ["returns"] = CountOccurrences(source, "return ") + CountOccurrences(source, "return;"),

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

    /// <summary>
    /// Counts tokens in source code using simple tokenization: words, and each punctuation
    /// or symbol character. Native Calor keywords are ordinary words, so both languages
    /// use the same rule.
    /// </summary>
    private static int CountTokens(string source)
    {
        var processed = source;

        var tokens = new List<string>();
        var currentToken = "";

        foreach (var ch in processed)
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

        return tokens.Count;
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
