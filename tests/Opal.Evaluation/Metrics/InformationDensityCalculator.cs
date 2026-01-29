using Microsoft.CodeAnalysis.CSharp;
using Opal.Evaluation.Core;

namespace Opal.Evaluation.Metrics;

/// <summary>
/// Category 6: Information Density Calculator
/// Measures semantic information per token for OPAL vs C#.
/// OPAL is hypothesized to embed contracts/effects inline, increasing density.
/// </summary>
public class InformationDensityCalculator : IMetricCalculator
{
    public string Category => "InformationDensity";

    public string Description => "Measures semantic elements per token ratio";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Count semantic elements
        var opalSemantics = CountOpalSemanticElements(context);
        var csharpSemantics = CountCSharpSemanticElements(context);

        // Count tokens
        var opalTokens = CountTokens(context.OpalSource);
        var csharpTokens = CountTokens(context.CSharpSource);

        // Calculate density (semantic elements per token)
        var opalDensity = opalTokens > 0 ? (double)opalSemantics.Total / opalTokens : 0;
        var csharpDensity = csharpTokens > 0 ? (double)csharpSemantics.Total / csharpTokens : 0;

        var details = new Dictionary<string, object>
        {
            ["opalSemanticElements"] = opalSemantics,
            ["csharpSemanticElements"] = csharpSemantics,
            ["opalTokenCount"] = opalTokens,
            ["csharpTokenCount"] = csharpTokens,
            ["opalDensity"] = opalDensity,
            ["csharpDensity"] = csharpDensity
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "SemanticDensity",
            opalDensity,
            csharpDensity,
            details));
    }

    /// <summary>
    /// Calculates detailed density metrics.
    /// </summary>
    public List<MetricResult> CalculateDetailedMetrics(EvaluationContext context)
    {
        var results = new List<MetricResult>();

        var opalSemantics = CountOpalSemanticElements(context);
        var csharpSemantics = CountCSharpSemanticElements(context);
        var opalTokens = CountTokens(context.OpalSource);
        var csharpTokens = CountTokens(context.CSharpSource);

        // Overall density
        var opalDensity = opalTokens > 0 ? (double)opalSemantics.Total / opalTokens : 0;
        var csharpDensity = csharpTokens > 0 ? (double)csharpSemantics.Total / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "OverallDensity",
            opalDensity,
            csharpDensity));

        // Type information density
        var opalTypeDensity = opalTokens > 0 ? (double)opalSemantics.TypeAnnotations / opalTokens : 0;
        var csharpTypeDensity = csharpTokens > 0 ? (double)csharpSemantics.TypeAnnotations / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "TypeDensity",
            opalTypeDensity,
            csharpTypeDensity));

        // Contract density
        var opalContractDensity = opalTokens > 0 ? (double)opalSemantics.Contracts / opalTokens : 0;
        var csharpContractDensity = csharpTokens > 0 ? (double)csharpSemantics.Contracts / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "ContractDensity",
            opalContractDensity,
            csharpContractDensity));

        // Effect density
        var opalEffectDensity = opalTokens > 0 ? (double)opalSemantics.Effects / opalTokens : 0;
        var csharpEffectDensity = csharpTokens > 0 ? (double)csharpSemantics.Effects / csharpTokens : 0;
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "EffectDensity",
            opalEffectDensity,
            csharpEffectDensity));

        return results;
    }

    /// <summary>
    /// Counts semantic elements in OPAL source.
    /// </summary>
    private static SemanticElementCounts CountOpalSemanticElements(EvaluationContext context)
    {
        var source = context.OpalSource;
        var compilation = context.OpalCompilation;

        var counts = new SemanticElementCounts();

        // Count from source patterns (backup if compilation fails)
        counts.Modules = CountPattern(source, @"§M\[");
        counts.Functions = CountPattern(source, @"§F\[");
        counts.Variables = CountPattern(source, @"§V\[");
        counts.TypeAnnotations = CountPattern(source, @"§I\[") + CountPattern(source, @"§O\[");
        counts.Contracts = CountPattern(source, @"§REQ") + CountPattern(source, @"§ENS") + CountPattern(source, @"§INV");
        counts.Effects = CountPattern(source, @"§E\[");
        counts.ControlFlow = CountPattern(source, @"§IF") + CountPattern(source, @"§LOOP") + CountPattern(source, @"§MATCH");
        counts.Expressions = CountPattern(source, @"§C\[") + CountPattern(source, @"\([\+\-\*/]");

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

        counts.Functions = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Count();

        counts.Functions += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax>()
            .Count();

        counts.Variables = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax>()
            .Count();

        counts.Variables += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax>()
            .Count();

        counts.TypeAnnotations = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax>()
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

        counts.Expressions = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Count();

        counts.Expressions += root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax>()
            .Count();

        return counts;
    }

    /// <summary>
    /// Counts tokens in source code using simple tokenization.
    /// </summary>
    private static int CountTokens(string source)
    {
        var tokens = 0;
        var inToken = false;

        foreach (var ch in source)
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

    public int Total => Modules + Functions + Variables + TypeAnnotations +
                       Contracts + Effects + ControlFlow + Expressions;
}
