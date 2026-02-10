using Calor.Evaluation.Core;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Measures Z3 static contract verification rates.
/// This is a Calor-only metric as C# has no equivalent static verification feature.
/// </summary>
public class ContractVerificationCalculator : IMetricCalculator
{
    public string Category => "ContractVerification";
    public string Description => "Z3 static contract verification rates";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        const double csharpScore = 0.0; // C# has no equivalent

        if (!context.CalorCompilation.Success || context.CalorCompilation.Module == null)
        {
            return Task.FromResult(MetricResult.CreateHigherIsBetter(
                Category, "VerificationScore", 0.0, csharpScore,
                new Dictionary<string, object> { ["error"] = "Compilation failed", ["isCalorOnly"] = true }));
        }

        // Check Z3 availability
        if (!Z3ContextFactory.IsAvailable)
        {
            return Task.FromResult(MetricResult.CreateHigherIsBetter(
                Category, "VerificationScore", 0.0, csharpScore,
                new Dictionary<string, object> { ["skipped"] = "Z3 unavailable", ["isCalorOnly"] = true }));
        }

        var diagnostics = new DiagnosticBag();
        var verificationPass = new ContractVerificationPass(diagnostics, new VerificationOptions());
        var result = verificationPass.Verify(context.CalorCompilation.Module);
        var summary = result.GetSummary();

        if (summary.Total == 0)
        {
            return Task.FromResult(MetricResult.CreateHigherIsBetter(
                Category, "VerificationScore", 0.0, csharpScore,
                new Dictionary<string, object> { ["noContracts"] = true, ["isCalorOnly"] = true }));
        }

        // Score: Proven=1.0, Unproven=0.5, Disproven=0.0
        var calorScore = (summary.Proven * 1.0 + summary.Unproven * 0.5) / summary.Total;

        var details = new Dictionary<string, object>
        {
            ["proven"] = summary.Proven,
            ["disproven"] = summary.Disproven,
            ["unproven"] = summary.Unproven,
            ["unsupported"] = summary.Unsupported,
            ["skipped"] = summary.Skipped,
            ["total"] = summary.Total,
            ["provenRate"] = (double)summary.Proven / summary.Total,
            ["disprovenRate"] = (double)summary.Disproven / summary.Total,
            ["isCalorOnly"] = true
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category, "VerificationScore", calorScore, csharpScore, details));
    }
}
