using Opal.Evaluation.Core;

namespace Opal.Evaluation.Metrics;

/// <summary>
/// Category 2: Generation Accuracy Calculator
/// Measures how accurately code can be generated in OPAL vs C#.
/// Explicit structure in OPAL is hypothesized to reduce errors.
/// </summary>
public class GenerationAccuracyCalculator : IMetricCalculator
{
    public string Category => "GenerationAccuracy";

    public string Description => "Measures code generation correctness via compilation success and structural matching";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var opalCompilation = context.OpalCompilation;
        var csharpCompilation = context.CSharpCompilation;

        // Compilation success score (1.0 = success, 0.0 = failure)
        var opalCompileScore = opalCompilation.Success ? 1.0 : 0.0;
        var csharpCompileScore = csharpCompilation.Success ? 1.0 : 0.0;

        // Structural completeness (based on AST node presence)
        var opalStructureScore = CalculateOpalStructureScore(opalCompilation);
        var csharpStructureScore = CalculateCSharpStructureScore(csharpCompilation);

        // Error count (lower is better)
        var opalErrorCount = opalCompilation.Errors.Count;
        var csharpErrorCount = csharpCompilation.Errors.Count;

        // Composite score (weighted average)
        var opalScore = (opalCompileScore * 0.5) + (opalStructureScore * 0.3) + (opalErrorCount == 0 ? 0.2 : 0.0);
        var csharpScore = (csharpCompileScore * 0.5) + (csharpStructureScore * 0.3) + (csharpErrorCount == 0 ? 0.2 : 0.0);

        var details = new Dictionary<string, object>
        {
            ["opalCompileSuccess"] = opalCompilation.Success,
            ["csharpCompileSuccess"] = csharpCompilation.Success,
            ["opalStructureScore"] = opalStructureScore,
            ["csharpStructureScore"] = csharpStructureScore,
            ["opalErrorCount"] = opalErrorCount,
            ["csharpErrorCount"] = csharpErrorCount,
            ["opalErrors"] = opalCompilation.Errors,
            ["csharpErrors"] = csharpCompilation.Errors
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "CompositeAccuracy",
            opalScore,
            csharpScore,
            details));
    }

    /// <summary>
    /// Calculates detailed accuracy metrics with all sub-metrics.
    /// </summary>
    public List<MetricResult> CalculateDetailedMetrics(EvaluationContext context)
    {
        var results = new List<MetricResult>();
        var opalCompilation = context.OpalCompilation;
        var csharpCompilation = context.CSharpCompilation;

        // Compilation success
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "CompilationSuccess",
            opalCompilation.Success ? 1.0 : 0.0,
            csharpCompilation.Success ? 1.0 : 0.0));

        // Error count (inverted for lower-is-better)
        results.Add(MetricResult.CreateLowerIsBetter(
            Category,
            "ErrorCount",
            opalCompilation.Errors.Count,
            csharpCompilation.Errors.Count));

        // Structure score
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "StructureCompleteness",
            CalculateOpalStructureScore(opalCompilation),
            CalculateCSharpStructureScore(csharpCompilation)));

        return results;
    }

    /// <summary>
    /// Calculates a structural completeness score for OPAL based on AST node types present.
    /// </summary>
    private static double CalculateOpalStructureScore(OpalCompilationResult compilation)
    {
        if (!compilation.Success || compilation.Module == null)
            return 0.0;

        var score = 0.0;
        var module = compilation.Module;

        // Check for key structural elements
        if (module.Name != null) score += 0.2;
        if (module.Functions.Count > 0) score += 0.3;

        // Check function completeness
        foreach (var func in module.Functions)
        {
            if (func.Name != null) score += 0.1;
            if (func.Output != null) score += 0.1;
            if (func.Body != null && func.Body.Count > 0) score += 0.1;
            break; // Just check first function for now
        }

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates a structural completeness score for C# based on syntax nodes present.
    /// </summary>
    private static double CalculateCSharpStructureScore(CSharpCompilationResult compilation)
    {
        if (!compilation.Success || compilation.Root == null)
            return 0.0;

        var score = 0.0;
        var root = compilation.Root;

        // Check for key structural elements
        var hasUsings = root.Usings.Count > 0;
        var hasNamespace = root.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax>().Any();
        var hasClass = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().Any();
        var hasMethod = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Any();

        if (hasUsings) score += 0.1;
        if (hasNamespace) score += 0.3;
        if (hasClass) score += 0.3;
        if (hasMethod) score += 0.3;

        return Math.Min(score, 1.0);
    }
}
