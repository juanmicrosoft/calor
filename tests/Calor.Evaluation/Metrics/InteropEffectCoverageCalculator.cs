using Calor.Evaluation.Core;
using Calor.Compiler.Ast;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Measures BCL manifest resolution coverage for .NET interop.
/// This is a Calor-only metric as C# has no equivalent effect manifest system.
/// </summary>
public class InteropEffectCoverageCalculator : IMetricCalculator
{
    public string Category => "InteropEffectCoverage";
    public string Description => "BCL manifest resolution coverage for .NET interop";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        const double csharpScore = 0.0;

        if (!context.CalorCompilation.Success || context.CalorCompilation.Module == null)
        {
            return Task.FromResult(MetricResult.CreateHigherIsBetter(
                Category, "ManifestCoverage", 0.0, csharpScore,
                new Dictionary<string, object> { ["error"] = "Compilation failed", ["isCalorOnly"] = true }));
        }

        var loader = new ManifestLoader();
        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        // Collect external calls from AST using shared collector
        var allCalls = ExternalCallCollector.Collect(context.CalorCompilation.Module);

        var resolved = 0;
        var unknown = 0;
        var unknownCalls = new List<string>();

        foreach (var call in allCalls)
        {
            var resolution = resolver.Resolve(call.TypeName, call.MethodName);
            if (resolution.Status == EffectResolutionStatus.Unknown)
            {
                unknown++;
                unknownCalls.Add($"{call.TypeName}.{call.MethodName}");
            }
            else
            {
                resolved++;
            }
        }

        var total = resolved + unknown;
        var calorScore = total > 0 ? (double)resolved / total : 1.0;

        var details = new Dictionary<string, object>
        {
            ["resolved"] = resolved,
            ["unknown"] = unknown,
            ["total"] = total,
            ["coveragePercent"] = calorScore * 100,
            ["isCalorOnly"] = true
        };

        // Include up to 10 unknown calls for debugging
        if (unknownCalls.Count > 0)
        {
            details["unknownCalls"] = unknownCalls.Take(10).ToList();
        }

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category, "ManifestCoverage", calorScore, csharpScore, details));
    }

    // External call collection delegated to shared ExternalCallCollector
    // in Calor.Compiler.Effects namespace (covers class methods, constructors,
    // and resolves variable types via §NEW initializer scanning).
}
