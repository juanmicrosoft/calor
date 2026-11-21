using Microsoft.CodeAnalysis.CSharp;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 6: Information Density Calculator
/// Measures semantic information per token for Calor vs C#.
/// Calor is hypothesized to embed contracts/effects inline, increasing density.
/// </summary>
public class InformationDensityCalculator : IMetricCalculator
{
    public string Category => "InformationDensity";

    public string Description => "Measures semantic elements per token ratio";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Count semantic elements
        var calorSemantics = CountCalorSemanticElements(context);
        var csharpSemantics = CountCSharpSemanticElements(context);

        // Count tokens
        var calorTokens = CountTokens(context.CalorSource);
        var csharpTokens = CountTokens(context.CSharpSource);

        // Calculate density (semantic elements per token)
        var calorDensity = calorTokens > 0 ? (double)calorSemantics.Total / calorTokens : 0;
        var csharpDensity = csharpTokens > 0 ? (double)csharpSemantics.Total / csharpTokens : 0;

        var details = new Dictionary<string, object>
        {
            ["calorSemanticElements"] = calorSemantics,
            ["csharpSemanticElements"] = csharpSemantics,
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

    /// <summary>
    /// Calculates detailed density metrics.
    /// </summary>
    public List<MetricResult> CalculateDetailedMetrics(EvaluationContext context)
    {
        var results = new List<MetricResult>();

        var calorSemantics = CountCalorSemanticElements(context);
        var csharpSemantics = CountCSharpSemanticElements(context);
        var calorTokens = CountTokens(context.CalorSource);
        var csharpTokens = CountTokens(context.CSharpSource);

        // Overall density
        var calorDensity = calorTokens > 0 ? (double)calorSemantics.Total / calorTokens : 0;
        var csharpDensity = csharpTokens > 0 ? (double)csharpSemantics.Total / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "OverallDensity",
            calorDensity,
            csharpDensity));

        // Type information density
        var calorTypeDensity = calorTokens > 0 ? (double)calorSemantics.TypeAnnotations / calorTokens : 0;
        var csharpTypeDensity = csharpTokens > 0 ? (double)csharpSemantics.TypeAnnotations / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "TypeDensity",
            calorTypeDensity,
            csharpTypeDensity));

        // Contract density
        var calorContractDensity = calorTokens > 0 ? (double)calorSemantics.Contracts / calorTokens : 0;
        var csharpContractDensity = csharpTokens > 0 ? (double)csharpSemantics.Contracts / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "ContractDensity",
            calorContractDensity,
            csharpContractDensity));

        // Effect density
        var calorEffectDensity = calorTokens > 0 ? (double)calorSemantics.Effects / calorTokens : 0;
        var csharpEffectDensity = csharpTokens > 0 ? (double)csharpSemantics.Effects / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "EffectDensity",
            calorEffectDensity,
            csharpEffectDensity));

        return results;
    }

    /// <summary>
    /// Counts semantic elements in Calor source.
    /// </summary>
    private static SemanticElementCounts CountCalorSemanticElements(EvaluationContext context)
    {
        var source = context.CalorSource;
        var compilation = context.CalorCompilation;

        var counts = new SemanticElementCounts();

        // Count from source patterns (backup if compilation fails); see CalorSyntax.
        counts.Modules = CalorSyntax.Module.Matches(source).Count;
        counts.Functions = CalorSyntax.Function.Matches(source).Count;
        counts.Variables = CalorSyntax.Binding.Matches(source).Count;

        // Type annotations: typed parameters and return types AND actual type names for parity
        // with C# TypeSyntax counting
        counts.TypeAnnotations = CalorSyntax.CountTypedParameters(source) + CalorSyntax.TypedOutput.Matches(source).Count;
        counts.TypeAnnotations += CountPattern(source, @"\b(int|long|float|double|string|bool|void)\b");

        // Contracts (weight higher - unique to Calor)
        counts.Contracts = (CalorSyntax.Precondition.Matches(source).Count + CalorSyntax.Ensures.Matches(source).Count) * 2;

        // Effects (weight higher - unique to Calor)
        counts.Effects = CalorSyntax.EffectRow.Matches(source).Count * 2;

        // Control flow: include return statements, else branches, else-if branches
        counts.ControlFlow = CalorSyntax.If.Matches(source).Count + CalorSyntax.Loop.Matches(source).Count;
        counts.ControlFlow += CalorSyntax.Return.Matches(source).Count;
        counts.ControlFlow += CalorSyntax.Else.Matches(source).Count;
        counts.ControlFlow += CalorSyntax.ElseIf.Matches(source).Count;

        // Calls and binary arithmetic
        counts.Expressions = CalorSyntax.Call.Matches(source).Count + CountPattern(source, @"\s[\+\-\*/]\s");

        // Native source has no closing tags: blocks end by indentation.
        counts.ClosingTags = 0;

        // Mutable binding markers (unique to Calor - explicit mutability intent)
        counts.MutabilityMarkers = CalorSyntax.MutableBinding.Matches(source).Count;

        // If compilation succeeded, use AST for more accurate counts
        if (compilation.Success && compilation.Module != null)
        {
            counts.Modules = 1;
            counts.Functions = compilation.Module.Functions.Count;
        }

        return counts;
    }

    /// <summary>
    /// Counts semantic elements in C# source using Roslyn.
    /// </summary>
    private static SemanticElementCounts CountCSharpSemanticElements(EvaluationContext context)
    {
        var compilation = context.CSharpCompilation;
        var counts = new SemanticElementCounts();

        if (!compilation.Success || compilation.Root == null)
        {
            // Fallback to pattern counting
            var src = context.CSharpSource;
            counts.Modules = CountPattern(src, @"namespace\s+\w+");
            counts.Functions = CountPattern(src, @"(public|private|protected|internal)\s+(static\s+)?\w+\s+\w+\s*\(");
            counts.TypeAnnotations = CountPattern(src, @"(int|string|bool|double|float|void|var)\s+\w+");
            return counts;
        }

        var root = compilation.Root;

        // Count using Roslyn
        counts.Modules = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax>()
            .Count();

        var methodCount = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Count();

        var constructorCount = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax>()
            .Count();

        counts.Functions = methodCount + constructorCount;

        counts.Variables = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax>()
            .Count();

        var parameterCount = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax>()
            .Count();

        counts.Variables += parameterCount;

        // Count only meaningful type annotations (not ALL TypeSyntax nodes):
        // - Method return types (one per method)
        // - Parameter types (one per parameter)
        // - Field types
        // - Property types
        // This is more fair than counting every TypeSyntax (which includes generics, casts, etc.)
        counts.TypeAnnotations = methodCount; // Return type per method
        counts.TypeAnnotations += parameterCount; // One type per parameter

        counts.TypeAnnotations += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax>()
            .Count();

        counts.TypeAnnotations += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax>()
            .Count();

        // C# doesn't have built-in contracts - count assertions as approximation
        var source = context.CSharpSource;
        counts.Contracts = CountPattern(source, @"Contract\.(Requires|Ensures|Invariant)")
            + CountPattern(source, @"Debug\.Assert");

        // C# doesn't have explicit effects
        counts.Effects = 0;

        counts.ControlFlow = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax>()
            .Count();

        counts.ControlFlow += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ForStatementSyntax>()
            .Count();

        counts.ControlFlow += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.WhileStatementSyntax>()
            .Count();

        counts.ControlFlow += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.SwitchStatementSyntax>()
            .Count();

        // Add return statements and else branches for parity with Calor counting
        counts.ControlFlow += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax>()
            .Count();

        counts.ControlFlow += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ElseClauseSyntax>()
            .Count();

        counts.Expressions = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Count();

        counts.Expressions += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax>()
            .Count();

        return counts;
    }

    /// <summary>
    /// Counts tokens in source code using simple tokenization: words, and each punctuation
    /// or symbol character. Native Calor keywords are ordinary words, so both languages
    /// use the same rule.
    /// </summary>
    private static int CountTokens(string source)
    {
        var processed = source;
        var tokens = 0;
        var inToken = false;

        foreach (var ch in processed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (inToken)
                {
                    tokens++;
                    inToken = false;
                }
            }
            else if (char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (inToken)
                {
                    tokens++;
                    inToken = false;
                }
                tokens++; // Punctuation is its own token
            }
            else
            {
                inToken = true;
            }
        }

        if (inToken)
            tokens++;

        return tokens;
    }

    private static int CountPattern(string source, string pattern)
    {
        return System.Text.RegularExpressions.Regex.Matches(source, pattern).Count;
    }
}

/// <summary>
/// Counts of semantic elements in source code.
/// </summary>
public class SemanticElementCounts
{
    public int Modules { get; set; }
    public int Functions { get; set; }
    public int Variables { get; set; }
    public int TypeAnnotations { get; set; }
    public int Contracts { get; set; }
    public int Effects { get; set; }
    public int ControlFlow { get; set; }
    public int Expressions { get; set; }
    public int ClosingTags { get; set; }
    public int MutabilityMarkers { get; set; }

    public int Total => Modules + Functions + Variables + TypeAnnotations +
                       Contracts + Effects + ControlFlow + Expressions +
                       ClosingTags + MutabilityMarkers;
}
