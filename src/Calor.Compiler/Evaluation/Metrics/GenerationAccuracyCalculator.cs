using Calor.Compiler.Evaluation.Core;

namespace Calor.Compiler.Evaluation.Metrics;

/// <summary>
/// Category 2: Generation Accuracy Calculator
/// Measures compilation success and AST structure validity.
/// Calor's explicit structure markers should improve accuracy.
/// </summary>
public class GenerationAccuracyCalculator : IMetricCalculator
{
    public string Category => "GenerationAccuracy";

    public string Description => "Measures compilation success and structural validity";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        var calorSuccess = context.CalorCompilation.Success;
        var csharpSuccess = context.CSharpCompilation.Success;

        // Calculate scores based on compilation success and error count
        var calorErrorCount = context.CalorCompilation.Errors.Count;
        var csharpErrorCount = context.CSharpCompilation.Errors.Count;

        // Score: 1.0 for success, reduced by error count
        var calorScore = calorSuccess ? 1.0 : Math.Max(0, 1.0 - (calorErrorCount * 0.1));
        var csharpScore = csharpSuccess ? 1.0 : Math.Max(0, 1.0 - (csharpErrorCount * 0.1));

        // Additional structural metrics for Calor
        var calorStructuralScore = CalculateCalorStructuralScore(context);
        var csharpStructuralScore = CalculateCSharpStructuralScore(context);

        // Combine compilation and structural scores
        var calorCombined = (calorScore + calorStructuralScore) / 2.0;
        var csharpCombined = (csharpScore + csharpStructuralScore) / 2.0;

        var details = new Dictionary<string, object>
        {
            ["calorSuccess"] = calorSuccess,
            ["csharpSuccess"] = csharpSuccess,
            ["calorErrorCount"] = calorErrorCount,
            ["csharpErrorCount"] = csharpErrorCount,
            ["calorStructuralScore"] = calorStructuralScore,
            ["csharpStructuralScore"] = csharpStructuralScore,
            ["calorErrors"] = context.CalorCompilation.Errors.Take(5).ToList(),
            ["csharpErrors"] = context.CSharpCompilation.Errors.Take(5).ToList()
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "CompilationAccuracy",
            calorCombined,
            csharpCombined,
            details));
    }

    private static double CalculateCalorStructuralScore(EvaluationContext context)
    {
        var source = context.CalorSource;
        var score = 0.0;
        var checks = 0;

        // Check for proper module structure
        if (source.Contains("§M["))
        {
            score += 1.0;
            checks++;
        }

        // Check for function definitions
        if (source.Contains("§F["))
        {
            score += 1.0;
            checks++;
        }

        // Check for proper block structure
        var openBraces = source.Count(c => c == '{');
        var closeBraces = source.Count(c => c == '}');
        if (openBraces == closeBraces)
        {
            score += 1.0;
            checks++;
        }

        // Check for proper parameter definitions
        if (source.Contains("§I[") || source.Contains("§O["))
        {
            score += 1.0;
            checks++;
        }

        return checks > 0 ? score / checks : 0.5;
    }

    private static double CalculateCSharpStructuralScore(EvaluationContext context)
    {
        if (!context.CSharpCompilation.Success || context.CSharpCompilation.Root == null)
            return 0.0;

        var root = context.CSharpCompilation.Root;
        var score = 0.0;
        var checks = 0;

        // Check for namespace or type declarations
        if (root.Members.Any())
        {
            score += 1.0;
            checks++;
        }

        // Check for using directives
        if (root.Usings.Any())
        {
            score += 1.0;
            checks++;
        }

        // Check for proper semicolons (basic structural validity)
        var source = context.CSharpSource;
        var semicolons = source.Count(c => c == ';');
        if (semicolons > 0)
        {
            score += 1.0;
            checks++;
        }

        // Check balanced braces
        var openBraces = source.Count(c => c == '{');
        var closeBraces = source.Count(c => c == '}');
        if (openBraces == closeBraces && openBraces > 0)
        {
            score += 1.0;
            checks++;
        }

        return checks > 0 ? score / checks : 0.5;
    }
}
