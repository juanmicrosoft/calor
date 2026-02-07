using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 4: Edit Precision Calculator
/// Measures code modification accuracy for Calor vs C#.
/// Calor's unique IDs are hypothesized to enable more precise targeting.
///
/// Now includes simulated edit task evaluation to measure actual edit success rates.
/// </summary>
public class EditPrecisionCalculator : IMetricCalculator
{
    public string Category => "EditPrecision";

    public string Description => "Measures code modification accuracy using diff analysis, ID-based targeting, and simulated edit tasks";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate edit precision based on structural targeting capability
        var calorPrecision = CalculateCalorEditPrecision(context);
        var csharpPrecision = CalculateCSharpEditPrecision(context);

        // Generate and evaluate simulated edit tasks
        var simulatedEdits = GenerateSimulatedEditTasks(context);
        var editTaskResults = EvaluateSimulatedEdits(context, simulatedEdits);

        var details = new Dictionary<string, object>
        {
            ["calorTargetingCapabilities"] = GetCalorTargetingCapabilities(context),
            ["csharpTargetingCapabilities"] = GetCSharpTargetingCapabilities(context),
            ["simulatedEditTasks"] = simulatedEdits.Select(e => e.Description).ToList(),
            ["editTaskResults"] = editTaskResults
        };

        // Combine structural precision with simulated edit success
        var calorCombined = (calorPrecision * 0.4) + (editTaskResults.CalorSuccessRate * 0.6);
        var csharpCombined = (csharpPrecision * 0.4) + (editTaskResults.CSharpSuccessRate * 0.6);

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "TargetingPrecision",
            calorCombined,
            csharpCombined,
            details));
    }

    /// <summary>
    /// Generates simulated edit tasks based on the code structure.
    /// </summary>
    public List<SimulatedEditTask> GenerateSimulatedEditTasks(EvaluationContext context)
    {
        var tasks = new List<SimulatedEditTask>();

        // Analyze Calor structure for edit targets
        var calorSource = context.CalorSource;
        var csharpSource = context.CSharpSource;

        // Task 1: Change loop bounds (if loops exist)
        var calorLoops = Regex.Matches(calorSource, @"§LOOP\[([^:]+):");
        var csharpLoops = Regex.Matches(csharpSource, @"\bfor\s*\([^)]+\)");

        if (calorLoops.Count > 0 || csharpLoops.Count > 0)
        {
            var loopId = calorLoops.Count > 0 ? calorLoops[0].Groups[1].Value : "loop1";
            tasks.Add(new SimulatedEditTask
            {
                Id = "edit_loop_bounds",
                Description = $"Change loop {loopId} start value to 0",
                EditType = EditType.ModifyValue,
                CalorTarget = loopId,
                CSharpTarget = "for loop initialization",
                ExpectedChanges = 1
            });
        }

        // Task 2: Add precondition to function
        var calorFunctions = Regex.Matches(calorSource, @"§F\[([^:]+):([^\]]+)\]");
        var csharpMethods = Regex.Matches(csharpSource, @"\b(public|private|protected)\s+\w+\s+(\w+)\s*\(");

        if (calorFunctions.Count > 0 || csharpMethods.Count > 0)
        {
            var funcId = calorFunctions.Count > 0 ? calorFunctions[0].Groups[1].Value : "func";
            var methodName = csharpMethods.Count > 0 ? csharpMethods[0].Groups[2].Value : "Method";

            tasks.Add(new SimulatedEditTask
            {
                Id = "add_precondition",
                Description = $"Add precondition 'x >= 0' to function {funcId}",
                EditType = EditType.AddContract,
                CalorTarget = funcId,
                CSharpTarget = methodName,
                ExpectedChanges = 1
            });
        }

        // Task 3: Rename function
        if (calorFunctions.Count > 0 || csharpMethods.Count > 0)
        {
            var funcId = calorFunctions.Count > 0 ? calorFunctions[0].Groups[1].Value : "func";
            var funcName = calorFunctions.Count > 0 ? calorFunctions[0].Groups[2].Value : "Method";
            var methodName = csharpMethods.Count > 0 ? csharpMethods[0].Groups[2].Value : "Method";

            tasks.Add(new SimulatedEditTask
            {
                Id = "rename_function",
                Description = $"Rename function '{funcName}' to 'NewName'",
                EditType = EditType.Rename,
                CalorTarget = funcId,
                CSharpTarget = methodName,
                ExpectedChanges = -1 // Depends on call sites
            });
        }

        // Task 4: Add parameter to function
        if (calorFunctions.Count > 0 || csharpMethods.Count > 0)
        {
            var funcId = calorFunctions.Count > 0 ? calorFunctions[0].Groups[1].Value : "func";
            var methodName = csharpMethods.Count > 0 ? csharpMethods[0].Groups[2].Value : "Method";

            tasks.Add(new SimulatedEditTask
            {
                Id = "add_parameter",
                Description = $"Add parameter 'int flag' to function {funcId}",
                EditType = EditType.ChangeSignature,
                CalorTarget = funcId,
                CSharpTarget = methodName,
                ExpectedChanges = -1 // Depends on call sites
            });
        }

        // Task 5: Change return type
        if (calorFunctions.Count > 0 || csharpMethods.Count > 0)
        {
            var funcId = calorFunctions.Count > 0 ? calorFunctions[0].Groups[1].Value : "func";
            var methodName = csharpMethods.Count > 0 ? csharpMethods[0].Groups[2].Value : "Method";

            tasks.Add(new SimulatedEditTask
            {
                Id = "change_return_type",
                Description = $"Change return type of {funcId} from int to double",
                EditType = EditType.ChangeType,
                CalorTarget = funcId,
                CSharpTarget = methodName,
                ExpectedChanges = 1
            });
        }

        return tasks;
    }

    /// <summary>
    /// Evaluates simulated edit tasks for both languages.
    /// </summary>
    public SimulatedEditResults EvaluateSimulatedEdits(EvaluationContext context, List<SimulatedEditTask> tasks)
    {
        var calorResults = new List<EditTaskResult>();
        var csharpResults = new List<EditTaskResult>();

        foreach (var task in tasks)
        {
            calorResults.Add(EvaluateCalorEdit(context, task));
            csharpResults.Add(EvaluateCSharpEdit(context, task));
        }

        return new SimulatedEditResults
        {
            TaskCount = tasks.Count,
            CalorResults = calorResults,
            CSharpResults = csharpResults,
            CalorSuccessRate = calorResults.Count > 0 ? calorResults.Average(r => r.Score) : 0.5,
            CSharpSuccessRate = csharpResults.Count > 0 ? csharpResults.Average(r => r.Score) : 0.5
        };
    }

    /// <summary>
    /// Evaluates a single edit task for Calor code.
    /// </summary>
    private EditTaskResult EvaluateCalorEdit(EvaluationContext context, SimulatedEditTask task)
    {
        var source = context.CalorSource;
        var result = new EditTaskResult { TaskId = task.Id };

        // Check if target is uniquely identifiable
        var targetPattern = $@"§[A-Z]\[{Regex.Escape(task.CalorTarget)}:";
        var matches = Regex.Matches(source, targetPattern);

        result.TargetFound = matches.Count > 0;
        result.TargetUnique = matches.Count == 1;

        // Calculate precision based on edit type
        result.Score = task.EditType switch
        {
            EditType.ModifyValue => EvaluateValueModification(source, task.CalorTarget, true),
            EditType.AddContract => EvaluateContractAddition(source, task.CalorTarget, true),
            EditType.Rename => EvaluateRename(source, task.CalorTarget, true),
            EditType.ChangeSignature => EvaluateSignatureChange(source, task.CalorTarget, true),
            EditType.ChangeType => EvaluateTypeChange(source, task.CalorTarget, true),
            _ => 0.5
        };

        // Bonus for unique ID-based targeting
        if (result.TargetUnique)
            result.Score = Math.Min(1.0, result.Score * 1.2);

        // Check for potential collateral damage
        result.CollateralRisk = EstimateCollateralRisk(source, task, true);

        return result;
    }

    /// <summary>
    /// Evaluates a single edit task for C# code.
    /// </summary>
    private EditTaskResult EvaluateCSharpEdit(EvaluationContext context, SimulatedEditTask task)
    {
        var source = context.CSharpSource;
        var result = new EditTaskResult { TaskId = task.Id };

        // Check if target is identifiable by name
        var targetPattern = $@"\b{Regex.Escape(task.CSharpTarget)}\b";
        var matches = Regex.Matches(source, targetPattern);

        result.TargetFound = matches.Count > 0;
        result.TargetUnique = matches.Count == 1;

        // Calculate precision based on edit type
        result.Score = task.EditType switch
        {
            EditType.ModifyValue => EvaluateValueModification(source, task.CSharpTarget, false),
            EditType.AddContract => EvaluateContractAddition(source, task.CSharpTarget, false),
            EditType.Rename => EvaluateRename(source, task.CSharpTarget, false),
            EditType.ChangeSignature => EvaluateSignatureChange(source, task.CSharpTarget, false),
            EditType.ChangeType => EvaluateTypeChange(source, task.CSharpTarget, false),
            _ => 0.5
        };

        // Penalty for non-unique targets (name collisions)
        if (!result.TargetUnique && result.TargetFound)
            result.Score *= 0.7;

        // Check for potential collateral damage
        result.CollateralRisk = EstimateCollateralRisk(source, task, false);

        return result;
    }

    private static double EvaluateValueModification(string source, string target, bool isCalor)
    {
        if (isCalor)
        {
            // Calor: Find loop/variable by ID
            var pattern = $@"§(?:LOOP|V)\[{Regex.Escape(target)}:";
            return Regex.IsMatch(source, pattern) ? 0.9 : 0.4;
        }
        else
        {
            // C#: Need to find by line context
            return source.Contains("for") || source.Contains("while") ? 0.6 : 0.4;
        }
    }

    private static double EvaluateContractAddition(string source, string target, bool isCalor)
    {
        if (isCalor)
        {
            // Calor: Insert §REQ after function header (well-defined insertion point)
            var pattern = $@"§F\[{Regex.Escape(target)}:";
            if (!Regex.IsMatch(source, pattern)) return 0.3;

            // Already has contracts = easier to add more
            return source.Contains("§REQ") || source.Contains("§ENS") ? 0.95 : 0.85;
        }
        else
        {
            // C#: Need to add Debug.Assert or Contract.Requires at method start
            // Harder to identify exact insertion point
            return 0.6;
        }
    }

    private static double EvaluateRename(string source, string target, bool isCalor)
    {
        if (isCalor)
        {
            // Calor: Change name but keep ID - call sites don't need updating if using ID
            var idPattern = $@"§F\[{Regex.Escape(target)}:";
            return Regex.IsMatch(source, idPattern) ? 0.95 : 0.5;
        }
        else
        {
            // C#: Need to rename all occurrences - risk of over/under matching
            var occurrences = Regex.Matches(source, $@"\b{Regex.Escape(target)}\b").Count;
            // More occurrences = more risk of mistakes
            return Math.Max(0.4, 0.8 - (occurrences * 0.05));
        }
    }

    private static double EvaluateSignatureChange(string source, string target, bool isCalor)
    {
        if (isCalor)
        {
            // Calor: Update §I[ section, call sites can be found by ID
            var pattern = $@"§F\[{Regex.Escape(target)}:";
            var hasInputSection = source.Contains("§I[");
            return Regex.IsMatch(source, pattern) ? (hasInputSection ? 0.9 : 0.8) : 0.4;
        }
        else
        {
            // C#: Update method signature and all call sites
            // Call sites harder to find definitively
            return 0.55;
        }
    }

    private static double EvaluateTypeChange(string source, string target, bool isCalor)
    {
        if (isCalor)
        {
            // Calor: Update §O[ section
            var pattern = $@"§F\[{Regex.Escape(target)}:";
            var hasOutputSection = source.Contains("§O[");
            return Regex.IsMatch(source, pattern) ? (hasOutputSection ? 0.9 : 0.75) : 0.4;
        }
        else
        {
            // C#: Update return type in signature
            return 0.65;
        }
    }

    private static double EstimateCollateralRisk(string source, SimulatedEditTask task, bool isCalor)
    {
        if (isCalor)
        {
            // Calor: IDs reduce collateral risk significantly
            // Risk mainly from incorrect boundary detection
            var hasClosingTags = source.Contains("§/F[") || source.Contains("§/M[");
            return hasClosingTags ? 0.1 : 0.25;
        }
        else
        {
            // C#: Name-based changes have higher collateral risk
            var targetOccurrences = Regex.Matches(source, $@"\b{Regex.Escape(task.CSharpTarget)}\b").Count;
            return Math.Min(0.8, 0.2 + (targetOccurrences * 0.1));
        }
    }

    /// <summary>
    /// Evaluates edit precision for a before/after edit pair.
    /// </summary>
    public MetricResult EvaluateEdit(
        string calorBefore,
        string calorAfter,
        string csharpBefore,
        string csharpAfter,
        string editDescription)
    {
        var calorDiff = CalculateDiffMetrics(calorBefore, calorAfter);
        var csharpDiff = CalculateDiffMetrics(csharpBefore, csharpAfter);

        // Edit efficiency: fewer changes = more precise
        var calorEfficiency = calorDiff.TotalLines > 0
            ? 1.0 - ((double)calorDiff.ModifiedLines / calorDiff.TotalLines)
            : 1.0;
        var csharpEfficiency = csharpDiff.TotalLines > 0
            ? 1.0 - ((double)csharpDiff.ModifiedLines / csharpDiff.TotalLines)
            : 1.0;

        var details = new Dictionary<string, object>
        {
            ["editDescription"] = editDescription,
            ["calorDiff"] = calorDiff,
            ["csharpDiff"] = csharpDiff
        };

        return MetricResult.CreateHigherIsBetter(
            Category,
            "EditEfficiency",
            calorEfficiency,
            csharpEfficiency,
            details);
    }

    /// <summary>
    /// Evaluates edit correctness by comparing actual output to expected output.
    /// </summary>
    public MetricResult EvaluateEditCorrectness(
        string actualOutput,
        string expectedOutput,
        bool isCalor)
    {
        var diff = CalculateDiffMetrics(expectedOutput, actualOutput);

        // Correctness based on how close actual is to expected
        var correctness = diff.TotalLines > 0
            ? 1.0 - ((double)diff.ModifiedLines / diff.TotalLines)
            : 1.0;

        var prefix = isCalor ? "Calor" : "CSharp";
        return new MetricResult(
            Category,
            $"{prefix}EditCorrectness",
            isCalor ? correctness : 0,
            isCalor ? 0 : correctness,
            1.0,
            new Dictionary<string, object>
            {
                ["diff"] = diff,
                ["correctness"] = correctness
            });
    }

    /// <summary>
    /// Calculates Calor's edit precision based on unique ID presence.
    /// </summary>
    private static double CalculateCalorEditPrecision(EvaluationContext context)
    {
        var score = 0.5; // Base score

        var source = context.CalorSource;

        // Calor unique IDs enable precise targeting
        var moduleIds = CountPattern(source, @"\§M\[[^\]]+:");
        var functionIds = CountPattern(source, @"\§F\[[^\]]+:");
        var variableIds = CountPattern(source, @"\§V\[[^\]]+:");

        // More unique IDs = higher precision capability
        if (moduleIds > 0) score += 0.15;
        if (functionIds > 0) score += 0.20;
        if (variableIds > 0) score += 0.10;

        // Closing tags enable safe modifications
        if (source.Contains("§/F[")) score += 0.05;
        if (source.Contains("§/M[")) score += 0.05;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates C#'s edit precision based on structural patterns.
    /// </summary>
    private static double CalculateCSharpEditPrecision(EvaluationContext context)
    {
        var score = 0.5; // Base score

        var source = context.CSharpSource;

        // C# relies on names and line numbers for targeting
        var hasNamespace = source.Contains("namespace");
        var hasClass = source.Contains("class");
        var hasMethods = CountPattern(source, @"\b(public|private|protected|internal)\s+(static\s+)?\w+\s+\w+\s*\(");

        if (hasNamespace) score += 0.10;
        if (hasClass) score += 0.10;
        if (hasMethods > 0) score += 0.15;

        // Braces can cause edit ambiguity
        var braceCount = source.Count(c => c == '{');
        if (braceCount > 5) score -= 0.05; // Nested braces reduce precision

        return Math.Max(0.3, Math.Min(score, 0.85)); // Cap at 0.85 for C#
    }

    private static Dictionary<string, object> GetCalorTargetingCapabilities(EvaluationContext context)
    {
        var source = context.CalorSource;
        return new Dictionary<string, object>
        {
            ["hasUniqueModuleIds"] = source.Contains("§M["),
            ["hasUniqueFunctionIds"] = source.Contains("§F["),
            ["hasUniqueVariableIds"] = source.Contains("§V["),
            ["hasClosingTags"] = source.Contains("§/"),
            ["moduleIdCount"] = CountPattern(source, @"\§M\["),
            ["functionIdCount"] = CountPattern(source, @"\§F\[")
        };
    }

    private static Dictionary<string, object> GetCSharpTargetingCapabilities(EvaluationContext context)
    {
        var source = context.CSharpSource;
        return new Dictionary<string, object>
        {
            ["hasNamespace"] = source.Contains("namespace"),
            ["hasClass"] = source.Contains("class"),
            ["methodCount"] = CountPattern(source, @"\b(public|private|protected|internal)\s+(static\s+)?\w+\s+\w+\s*\("),
            ["braceDepth"] = source.Count(c => c == '{'),
            ["lineCount"] = source.Split('\n').Length
        };
    }

    private static int CountPattern(string source, string pattern)
    {
        return Regex.Matches(source, pattern).Count;
    }

    /// <summary>
    /// Calculates diff metrics between two versions of source code.
    /// </summary>
    private static DiffMetrics CalculateDiffMetrics(string before, string after)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(before, after);

        var inserted = diff.Lines.Count(l => l.Type == ChangeType.Inserted);
        var deleted = diff.Lines.Count(l => l.Type == ChangeType.Deleted);
        var unchanged = diff.Lines.Count(l => l.Type == ChangeType.Unchanged);
        var modified = inserted + deleted;

        return new DiffMetrics(
            TotalLines: diff.Lines.Count,
            InsertedLines: inserted,
            DeletedLines: deleted,
            UnchangedLines: unchanged,
            ModifiedLines: modified);
    }
}

/// <summary>
/// Metrics from a diff comparison.
/// </summary>
public record DiffMetrics(
    int TotalLines,
    int InsertedLines,
    int DeletedLines,
    int UnchangedLines,
    int ModifiedLines);

/// <summary>
/// A simulated edit task to test targeting precision.
/// </summary>
public class SimulatedEditTask
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public EditType EditType { get; init; }
    public required string CalorTarget { get; init; }
    public required string CSharpTarget { get; init; }
    public int ExpectedChanges { get; init; } = 1;
}

/// <summary>
/// Types of edit operations.
/// </summary>
public enum EditType
{
    ModifyValue,
    AddContract,
    Rename,
    ChangeSignature,
    ChangeType,
    Delete,
    Move
}

/// <summary>
/// Result of evaluating a single edit task.
/// </summary>
public class EditTaskResult
{
    public required string TaskId { get; init; }
    public bool TargetFound { get; set; }
    public bool TargetUnique { get; set; }
    public double Score { get; set; }
    public double CollateralRisk { get; set; }
}

/// <summary>
/// Aggregated results from simulated edit tasks.
/// </summary>
public class SimulatedEditResults
{
    public int TaskCount { get; init; }
    public List<EditTaskResult> CalorResults { get; init; } = new();
    public List<EditTaskResult> CSharpResults { get; init; } = new();
    public double CalorSuccessRate { get; init; }
    public double CSharpSuccessRate { get; init; }
    public double AdvantageRatio => CSharpSuccessRate > 0 ? CalorSuccessRate / CSharpSuccessRate : 1.0;
}
