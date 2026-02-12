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
        var elements = new Dictionary<string, int>
        {
            // Structural elements (using correct curly brace syntax)
            ["modules"] = CountOccurrences(source, "§M{"),
            ["functions"] = CountOccurrences(source, "§F{") + CountOccurrences(source, "§AF{") +
                           CountOccurrences(source, "§MT{") + CountOccurrences(source, "§AMT{"),
            ["variables"] = CountOccurrences(source, "§B{"),
            ["parameters"] = CountOccurrences(source, "§I{") + CountOccurrences(source, "§O{"),

            // Contract elements (high semantic value - weight 2x)
            // Using correct syntax: §Q and §S followed by space
            ["requires"] = CountOccurrences(source, "§Q ") * 2,
            ["ensures"] = CountOccurrences(source, "§S ") * 2,
            ["invariants"] = CountOccurrences(source, "§INV{") * 2,

            // Effect declarations (high semantic value - weight 2x)
            ["effects"] = CountOccurrences(source, "§E{") * 2,

            // Control flow
            ["conditionals"] = CountOccurrences(source, "§IF") + CountOccurrences(source, "§EL ") +
                              CountOccurrences(source, "§EI{"),
            ["loops"] = CountOccurrences(source, "§L{") + CountOccurrences(source, "§WH{") +
                       CountOccurrences(source, "§W{"),
            ["matches"] = CountOccurrences(source, "§MA{") * 2, // Pattern matching is semantic-rich

            // Returns
            ["returns"] = CountOccurrences(source, "§R "),

            // Calls
            ["calls"] = CountOccurrences(source, "§C{"),

            // Type annotations - count actual type names for parity with C# TypeSyntax counting
            ["types"] = CountTypeNames(source),

            // Closing tags as scope markers
            ["closingTags"] = CountOccurrences(source, "§/"),

            // Mutability markers (unique to Calor)
            ["mutability"] = CountOccurrences(source, "§B{~")
        };

        return elements;
    }

    private static int CountTypeNames(string source)
    {
        // Count input/output markers
        var count = CountOccurrences(source, "§I{") + CountOccurrences(source, "§O{");

        // Count actual type name occurrences
        var typePatterns = new[] { "i32", "i64", "f32", "f64", "str", "bool", "unit" };
        foreach (var pattern in typePatterns)
        {
            count += CountWordOccurrences(source, pattern);
        }

        return count;
    }

    private static int CountWordOccurrences(string source, string word)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(word, index, StringComparison.Ordinal)) != -1)
        {
            // Check word boundaries
            var before = index > 0 ? source[index - 1] : ' ';
            var after = index + word.Length < source.Length ? source[index + word.Length] : ' ';

            if (!char.IsLetterOrDigit(before) && before != '_' &&
                !char.IsLetterOrDigit(after) && after != '_')
            {
                count++;
            }
            index += word.Length;
        }
        return count;
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
    /// Counts tokens in source code using simple tokenization.
    /// For Calor, treats section markers (§M{, §F{, §/F{, etc.) as single tokens
    /// rather than counting each character separately.
    /// </summary>
    private static int CountTokens(string source)
    {
        // Pre-process Calor: Replace section markers with single-token placeholders
        // This prevents §M{ from being counted as 3 tokens (§, M, {)
        var processed = System.Text.RegularExpressions.Regex.Replace(
            source,
            @"§/?[A-Z]+\{",
            " _MARKER_ ");

        // Also handle markers without braces (§Q, §S, §R, §EL followed by space)
        processed = System.Text.RegularExpressions.Regex.Replace(
            processed,
            @"§[A-Z]+\s",
            " _MARKER_ ");

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
