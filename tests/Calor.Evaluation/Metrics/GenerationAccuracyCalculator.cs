using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 2: Generation Accuracy Calculator
/// Measures how accurately code can be generated in Calor vs C#.
/// Explicit structure in Calor is hypothesized to reduce errors.
/// </summary>
public class GenerationAccuracyCalculator : IMetricCalculator
{
    public string Category => "GenerationAccuracy";

    public string Description => "Measures code generation correctness via compilation success and structural matching";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var calorCompilation = context.CalorCompilation;
        var csharpCompilation = context.CSharpCompilation;

        // Compilation success score (1.0 = success, 0.0 = failure)
        var calorCompileScore = calorCompilation.Success ? 1.0 : 0.0;
        var csharpCompileScore = csharpCompilation.Success ? 1.0 : 0.0;

        // Structural completeness (based on AST node presence)
        var calorStructureScore = CalculateCalorStructureScore(calorCompilation);
        var csharpStructureScore = CalculateCSharpStructureScore(csharpCompilation);

        // Error count (lower is better)
        var calorErrorCount = calorCompilation.Errors.Count;
        var csharpErrorCount = csharpCompilation.Errors.Count;

        // Composite score (weighted average)
        var calorScore = (calorCompileScore * 0.5) + (calorStructureScore * 0.3) + (calorErrorCount == 0 ? 0.2 : 0.0);
        var csharpScore = (csharpCompileScore * 0.5) + (csharpStructureScore * 0.3) + (csharpErrorCount == 0 ? 0.2 : 0.0);

        var details = new Dictionary<string, object>
        {
            ["calorCompileSuccess"] = calorCompilation.Success,
            ["csharpCompileSuccess"] = csharpCompilation.Success,
            ["calorStructureScore"] = calorStructureScore,
            ["csharpStructureScore"] = csharpStructureScore,
            ["calorErrorCount"] = calorErrorCount,
            ["csharpErrorCount"] = csharpErrorCount,
            ["calorErrors"] = calorCompilation.Errors,
            ["csharpErrors"] = csharpCompilation.Errors
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "CompositeAccuracy",
            calorScore,
            csharpScore,
            details));
    }

    /// <summary>
    /// Calculates detailed accuracy metrics with all sub-metrics.
    /// </summary>
    public List<MetricResult> CalculateDetailedMetrics(EvaluationContext context)
    {
        var results = new List<MetricResult>();
        var calorCompilation = context.CalorCompilation;
        var csharpCompilation = context.CSharpCompilation;

        // Compilation success
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "CompilationSuccess",
            calorCompilation.Success ? 1.0 : 0.0,
            csharpCompilation.Success ? 1.0 : 0.0));

        // Error count (inverted for lower-is-better)
        results.Add(MetricResult.CreateLowerIsBetter(
            Category,
            "ErrorCount",
            calorCompilation.Errors.Count,
            csharpCompilation.Errors.Count));

        // Structure score
        results.Add(MetricResult.CreateHigherIsBetter(
            Category,
            "StructureCompleteness",
            CalculateCalorStructureScore(calorCompilation),
            CalculateCSharpStructureScore(csharpCompilation)));

        return results;
    }

    /// <summary>
    /// Calculates a structural completeness score for Calor based on AST node types present.
    /// </summary>
    private static double CalculateCalorStructureScore(CalorCompilationResult compilation)
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
