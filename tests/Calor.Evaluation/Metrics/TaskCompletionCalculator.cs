using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 7: Task Completion Calculator
/// Measures end-to-end success rates for Calor vs C#.
/// Less context usage means more room for work in LLM context windows.
/// </summary>
public class TaskCompletionCalculator : IMetricCalculator
{
    public string Category => "TaskCompletion";

    public string Description => "Measures end-to-end task success rates and efficiency";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate task completion potential based on code characteristics
        var calorPotential = CalculateCalorCompletionPotential(context);
        var csharpPotential = CalculateCSharpCompletionPotential(context);

        var details = new Dictionary<string, object>
        {
            ["calorFactors"] = GetCalorCompletionFactors(context),
            ["csharpFactors"] = GetCSharpCompletionFactors(context)
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "CompletionPotential",
            calorPotential,
            csharpPotential,
            details));
    }

    /// <summary>
    /// Evaluates a specific task completion scenario.
    /// </summary>
    public TaskCompletionResult EvaluateTask(
        TaskDefinition task,
        string calorOutput,
        string csharpOutput,
        int calorTokensUsed,
        int csharpTokensUsed)
    {
        // Verify outputs against expected results
        var calorCorrect = VerifyTaskOutput(task, calorOutput, isCalor: true);
        var csharpCorrect = VerifyTaskOutput(task, csharpOutput, isCalor: false);

        // Calculate efficiency (correctness per token used)
        var calorEfficiency = calorTokensUsed > 0 ? (calorCorrect ? 1.0 : 0.0) / calorTokensUsed * 1000 : 0;
        var csharpEfficiency = csharpTokensUsed > 0 ? (csharpCorrect ? 1.0 : 0.0) / csharpTokensUsed * 1000 : 0;

        return new TaskCompletionResult
        {
            TaskId = task.Id,
            TaskCategory = task.Category,
            CalorSucceeded = calorCorrect,
            CSharpSucceeded = csharpCorrect,
            CalorTokensUsed = calorTokensUsed,
            CSharpTokensUsed = csharpTokensUsed,
            CalorEfficiency = calorEfficiency,
            CSharpEfficiency = csharpEfficiency
        };
    }

    /// <summary>
    /// Calculates completion potential for Calor based on code characteristics.
    /// </summary>
    private static double CalculateCalorCompletionPotential(EvaluationContext context)
    {
        var score = 0.5; // Base score

        // Compactness improves context efficiency
        var tokenCount = CountTokens(context.CalorSource);
        if (tokenCount < 50) score += 0.15;
        else if (tokenCount < 100) score += 0.10;
        else if (tokenCount < 200) score += 0.05;

        // Compilation success is crucial
        if (context.CalorCompilation.Success) score += 0.20;

        // Structural completeness
        var source = context.CalorSource;
        if (source.Contains("§M[") && source.Contains("§/M[")) score += 0.05;
        if (source.Contains("§F[") && source.Contains("§/F[")) score += 0.05;

        // Contracts enable verification
        if (source.Contains("§REQ") || source.Contains("§ENS")) score += 0.05;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates completion potential for C# based on code characteristics.
    /// </summary>
    private static double CalculateCSharpCompletionPotential(EvaluationContext context)
    {
        var score = 0.5; // Base score

        // Token count (C# typically needs more)
        var tokenCount = CountTokens(context.CSharpSource);
        if (tokenCount < 100) score += 0.10;
        else if (tokenCount < 200) score += 0.05;

        // Compilation success
        if (context.CSharpCompilation.Success) score += 0.20;

        // Structural completeness
        var source = context.CSharpSource;
        if (source.Contains("namespace")) score += 0.05;
        if (source.Contains("class")) score += 0.05;
        if (source.Contains("return")) score += 0.05;

        return Math.Min(score, 0.95); // Cap slightly lower to reflect verbosity overhead
    }

    private static Dictionary<string, object> GetCalorCompletionFactors(EvaluationContext context)
    {
        var tokenCount = CountTokens(context.CalorSource);
        return new Dictionary<string, object>
        {
            ["tokenCount"] = tokenCount,
            ["compilesSuccessfully"] = context.CalorCompilation.Success,
            ["hasCompleteStructure"] = context.CalorSource.Contains("§/M["),
            ["hasContracts"] = context.CalorSource.Contains("§REQ") || context.CalorSource.Contains("§ENS"),
            ["estimatedContextUsage"] = tokenCount / 4096.0 // Fraction of typical context
        };
    }

    private static Dictionary<string, object> GetCSharpCompletionFactors(EvaluationContext context)
    {
        var tokenCount = CountTokens(context.CSharpSource);
        return new Dictionary<string, object>
        {
            ["tokenCount"] = tokenCount,
            ["compilesSuccessfully"] = context.CSharpCompilation.Success,
            ["hasNamespace"] = context.CSharpSource.Contains("namespace"),
            ["hasClass"] = context.CSharpSource.Contains("class"),
            ["estimatedContextUsage"] = tokenCount / 4096.0
        };
    }

    /// <summary>
    /// Verifies task output against expected results.
    /// </summary>
    private static bool VerifyTaskOutput(TaskDefinition task, string output, bool isCalor)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        // Check for required patterns
        foreach (var pattern in task.RequiredPatterns)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(output, pattern))
                return false;
        }

        // Check for forbidden patterns (should not be present)
        foreach (var pattern in task.ForbiddenPatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(output, pattern))
                return false;
        }

        // Language-specific verification
        if (isCalor)
        {
            // Calor should have proper structure
            if (task.RequiresCompilation)
            {
                var ctx = new EvaluationContext
                {
                    CalorSource = output,
                    CSharpSource = "",
                    FileName = task.Id
                };
                return ctx.CalorCompilation.Success;
            }
        }
        else
        {
            // C# should compile
            if (task.RequiresCompilation)
            {
                var ctx = new EvaluationContext
                {
                    CalorSource = "",
                    CSharpSource = output,
                    FileName = task.Id
                };
                return ctx.CSharpCompilation.Success;
            }
        }

        return true;
    }

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
                tokens++;
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
}

/// <summary>
/// Definition of a task for completion evaluation.
/// </summary>
public class TaskDefinition
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public List<string> RequiredPatterns { get; init; } = new();
    public List<string> ForbiddenPatterns { get; init; } = new();
    public bool RequiresCompilation { get; init; } = true;
    public int MaxTokenBudget { get; init; } = 1000;
}

/// <summary>
/// Result of a task completion evaluation.
/// </summary>
public class TaskCompletionResult
{
    public required string TaskId { get; init; }
    public required string TaskCategory { get; init; }
    public bool CalorSucceeded { get; init; }
    public bool CSharpSucceeded { get; init; }
    public int CalorTokensUsed { get; init; }
    public int CSharpTokensUsed { get; init; }
    public double CalorEfficiency { get; init; }
    public double CSharpEfficiency { get; init; }

    public MetricResult ToMetricResult()
    {
        return MetricResult.CreateHigherIsBetter(
            "TaskCompletion",
            $"Task_{TaskId}",
            CalorSucceeded ? 1.0 : 0.0,
            CSharpSucceeded ? 1.0 : 0.0,
            new Dictionary<string, object>
            {
                ["calorTokensUsed"] = CalorTokensUsed,
                ["csharpTokensUsed"] = CSharpTokensUsed,
                ["calorEfficiency"] = CalorEfficiency,
                ["csharpEfficiency"] = CSharpEfficiency
            });
    }
}
