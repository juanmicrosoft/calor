using Calor.Evaluation.Core;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Measures effect declaration correctness via strict enforcement.
/// This is a Calor-only metric as C# has no equivalent effect system.
/// </summary>
public class EffectSoundnessCalculator : IMetricCalculator
{
    public string Category => "EffectSoundness";
    public string Description => "Effect declaration correctness via strict enforcement";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        const double csharpScore = 0.0;

        if (!context.CalorCompilation.Success || context.CalorCompilation.Module == null)
        {
            return Task.FromResult(MetricResult.CreateHigherIsBetter(
                Category, "EffectCorrectness", 0.0, csharpScore,
                new Dictionary<string, object> { ["error"] = "Compilation failed", ["isCalorOnly"] = true }));
        }

        var diagnostics = new DiagnosticBag();
        var catalog = EffectsCatalog.CreateDefault();
        var enforcementPass = new EffectEnforcementPass(
            diagnostics,
            catalog,
            policy: UnknownCallPolicy.Strict,
            resolver: null,
            strictEffects: true);

        enforcementPass.Enforce(context.CalorCompilation.Module);

        var forbiddenErrors = diagnostics.Count(d => d.Code == DiagnosticCode.ForbiddenEffect);
        var unknownErrors = diagnostics.Count(d => d.Code == DiagnosticCode.UnknownExternalCall);
        var totalFunctions = context.CalorCompilation.Module.Functions.Count;

        // Score: 1.0 if no errors, decreases with errors
        var totalErrors = forbiddenErrors + unknownErrors;
        var calorScore = totalFunctions > 0 && totalErrors == 0 ? 1.0
            : totalFunctions > 0 ? Math.Max(0, 1.0 - (double)totalErrors / totalFunctions)
            : 0.0;

        var details = new Dictionary<string, object>
        {
            ["forbiddenEffectErrors"] = forbiddenErrors,
            ["unknownCallErrors"] = unknownErrors,
            ["totalFunctions"] = totalFunctions,
            ["isCalorOnly"] = true
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category, "EffectCorrectness", calorScore, csharpScore, details));
    }
}
