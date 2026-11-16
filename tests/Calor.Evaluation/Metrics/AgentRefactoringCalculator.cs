using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 12: Agent Refactoring Success Calculator
/// Measures AI agent success rates when performing refactoring tasks on Calor vs C#.
///
/// This calculator integrates with the E2E agent task framework to run refactoring
/// benchmarks and compare success rates between languages. Calor is expected to
/// demonstrate advantages due to:
/// - First-class contracts that propagate during refactoring
/// - Effect tracking that survives code transformations
/// - Stable unique IDs that enable reliable references
/// </summary>
public partial class AgentRefactoringCalculator : IMetricCalculator
{
    public string Category => "AgentRefactoring";

    public string Description => "AI agent success rates on refactoring tasks (Calor vs C#)";

    private readonly string _agentTasksDir;
    private readonly bool _quickMode;

    public AgentRefactoringCalculator(string? agentTasksDir = null, bool quickMode = true)
    {
        // Default to relative path from test assembly
        _agentTasksDir = agentTasksDir ?? FindAgentTasksDir();
        _quickMode = quickMode;
    }

    public async Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // This calculator runs agent tasks rather than analyzing static code
        // The context is used for configuration but actual metrics come from agent runs

        try
        {
            var calorResults = await RunAgentTasksAsync("calor");
            var csharpResults = await RunAgentTasksAsync("csharp");

            var calorScore = CalculateSuccessScore(calorResults);
            var csharpScore = CalculateSuccessScore(csharpResults);

            var details = new Dictionary<string, object>
            {
                ["calorResults"] = calorResults,
                ["csharpResults"] = csharpResults,
                ["advantageRatio"] = csharpScore > 0 ? calorScore / csharpScore : 1.0,
                ["quickMode"] = _quickMode,
                ["agentTasksDir"] = _agentTasksDir
            };

            return MetricResult.CreateHigherIsBetter(
                Category,
                "RefactoringSuccessRate",
                calorScore,
                csharpScore,
                details);
        }
        catch (Exception ex)
        {
            return MetricResult.CreateHigherIsBetter(
                Category,
                "RefactoringSuccessRate",
                0.0,
                0.0,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["stackTrace"] = ex.StackTrace ?? ""
                });
        }
    }

    /// <summary>
    /// Runs agent refactoring tasks for a specific language.
    /// </summary>
    private async Task<AgentTaskResults> RunAgentTasksAsync(string language)
    {
        var scriptPath = Path.Combine(_agentTasksDir, "run-agent-tests.sh");

        if (!File.Exists(scriptPath))
        {
            return new AgentTaskResults
            {
                Language = language,
                Error = $"Script not found: {scriptPath}"
            };
        }

        var args = new List<string>
        {
            "--category", "refactoring-benchmark",
            "--filter", language
        };

        if (_quickMode)
        {
            args.Add("--single-run");
        }

        try
        {
            var result = await RunBashScriptAsync(scriptPath, args);
            return ParseAgentResults(result, language);
        }
        catch (Exception ex)
        {
            return new AgentTaskResults
            {
                Language = language,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Runs a bash script and captures output.
    /// </summary>
    private static async Task<string> RunBashScriptAsync(string scriptPath, List<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\" {string.Join(" ", args)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return output + error;
    }

    /// <summary>
    /// Parses agent test output to extract results.
    /// </summary>
    private static AgentTaskResults ParseAgentResults(string output, string language)
    {
        var results = new AgentTaskResults { Language = language };

        // Parse "Passed: X" and "Failed: Y" from output
        var passedMatch = PassedRegex().Match(output);
        var failedMatch = FailedRegex().Match(output);

        if (passedMatch.Success)
        {
            results.Passed = int.Parse(passedMatch.Groups[1].Value);
        }

        if (failedMatch.Success)
        {
            results.Failed = int.Parse(failedMatch.Groups[1].Value);
        }

        // Parse individual task results
        var taskMatches = TaskResultRegex().Matches(output);
        foreach (Match match in taskMatches)
        {
            var taskId = match.Groups[1].Value;
            var status = match.Groups[2].Value.Contains("passed", StringComparison.OrdinalIgnoreCase);
            results.TaskResults[taskId] = status;
        }

        results.RawOutput = output;
        return results;
    }

    /// <summary>
    /// Calculates a success score from agent results.
    /// </summary>
    private static double CalculateSuccessScore(AgentTaskResults results)
    {
        if (results.Error != null)
        {
            return 0.0;
        }

        var total = results.Passed + results.Failed;
        if (total == 0)
        {
            return 0.0;
        }

        return (double)results.Passed / total;
    }

    /// <summary>
    /// Finds the agent-tasks directory using multiple strategies.
    /// </summary>
    private static string FindAgentTasksDir()
    {
        // Strategy 1: Check environment variable (useful for CI)
        var envPath = Environment.GetEnvironmentVariable("CALOR_AGENT_TASKS_DIR");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }

        // Strategy 2: Walk up from assembly location to find repo root
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(baseDir);

        while (current != null)
        {
            // Look for agent-tasks relative to tests directory
            var agentTasksPath = Path.Combine(current.FullName, "tests", "E2E", "agent-tasks");
            if (Directory.Exists(agentTasksPath))
            {
                return agentTasksPath;
            }

            // Also check if we're directly in tests directory
            var directPath = Path.Combine(current.FullName, "E2E", "agent-tasks");
            if (Directory.Exists(directPath))
            {
                return directPath;
            }

            // Check for .git directory as repo root marker
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                var fromGitRoot = Path.Combine(current.FullName, "tests", "E2E", "agent-tasks");
                if (Directory.Exists(fromGitRoot))
                {
                    return fromGitRoot;
                }
            }

            current = current.Parent;
        }

        // Strategy 3: Try relative paths from assembly location
        var relativePaths = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "E2E", "agent-tasks"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "tests", "E2E", "agent-tasks"),
            Path.Combine(baseDir, "..", "..", "..", "E2E", "agent-tasks"),
        };

        foreach (var path in relativePaths)
        {
            var normalized = Path.GetFullPath(path);
            if (Directory.Exists(normalized))
            {
                return normalized;
            }
        }

        // Last resort: return a sensible default that will fail clearly
        throw new DirectoryNotFoundException(
            $"Could not find agent-tasks directory. Set CALOR_AGENT_TASKS_DIR environment variable or ensure you're running from within the Calor repository. Base directory: {baseDir}");
    }

    /// <summary>
    /// Evaluates a specific refactoring scenario with pre/post code analysis.
    /// </summary>
    public RefactoringEvaluationResult EvaluateRefactoring(
        string scenarioId,
        string calorBefore,
        string calorAfter,
        string csharpBefore,
        string csharpAfter)
    {
        return new RefactoringEvaluationResult
        {
            ScenarioId = scenarioId,
            CalorContractsPreserved = AreContractsPreserved(calorBefore, calorAfter),
            CalorEffectsPreserved = AreEffectsPreserved(calorBefore, calorAfter),
            CalorIdsPreserved = AreIdsPreserved(calorBefore, calorAfter),
            CSharpCommentsPreserved = AreCommentsPreserved(csharpBefore, csharpAfter),
            CalorDiffSize = CalculateDiffSize(calorBefore, calorAfter),
            CSharpDiffSize = CalculateDiffSize(csharpBefore, csharpAfter)
        };
    }

    private static bool AreContractsPreserved(string before, string after)
    {
        var beforeCount = CalorSyntax.Requires.Matches(before).Count + CalorSyntax.Ensures.Matches(before).Count;
        var afterCount = CalorSyntax.Requires.Matches(after).Count + CalorSyntax.Ensures.Matches(after).Count;
        return afterCount >= beforeCount;
    }

    private static bool AreEffectsPreserved(string before, string after)
    {
        var beforeCount = CalorSyntax.EffectRow.Matches(before).Count;
        var afterCount = CalorSyntax.EffectRow.Matches(after).Count;
        return afterCount >= beforeCount;
    }

    private static bool AreIdsPreserved(string before, string after)
    {
        var beforeIds = ExtractIds(before);
        var afterIds = ExtractIds(after);
        return beforeIds.All(id => afterIds.Contains(id));
    }

    private static bool AreCommentsPreserved(string before, string after)
    {
        // Check if contract comments are preserved in C#
        var beforeComments = CountPattern(before, @"//\s*(Pre|Post)condition:");
        var afterComments = CountPattern(after, @"//\s*(Pre|Post)condition:");
        return afterComments >= beforeComments;
    }

    private static HashSet<string> ExtractIds(string source)
    {
        var matches = IdRegex().Matches(source);
        return matches.Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static int CountPattern(string source, string pattern)
    {
        return Regex.Matches(source, pattern).Count;
    }

    private static int CalculateDiffSize(string before, string after)
    {
        // Simple line-based diff count
        var beforeLines = before.Split('\n').ToHashSet();
        var afterLines = after.Split('\n').ToHashSet();

        var added = afterLines.Except(beforeLines).Count();
        var removed = beforeLines.Except(afterLines).Count();

        return added + removed;
    }

    [GeneratedRegex(@"Passed:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PassedRegex();

    [GeneratedRegex(@"Failed:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex FailedRegex();

    [GeneratedRegex(@"(refactor-\S+):\s*(\d+/\d+\s+passed|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex TaskResultRegex();

    [GeneratedRegex(@"§[FMV]\{([^:]+):")]
    private static partial Regex IdRegex();
}

/// <summary>
/// Results from running agent refactoring tasks.
/// </summary>
public class AgentTaskResults
{
    public string Language { get; set; } = "";
    public int Passed { get; set; }
    public int Failed { get; set; }
    public Dictionary<string, bool> TaskResults { get; set; } = new();
    public string? Error { get; set; }
    public string RawOutput { get; set; } = "";

    public double PassRate => Passed + Failed > 0 ? (double)Passed / (Passed + Failed) : 0.0;
}

/// <summary>
/// Result of evaluating a specific refactoring operation.
/// </summary>
public class RefactoringEvaluationResult
{
    public string ScenarioId { get; init; } = "";
    public bool CalorContractsPreserved { get; init; }
    public bool CalorEffectsPreserved { get; init; }
    public bool CalorIdsPreserved { get; init; }
    public bool CSharpCommentsPreserved { get; init; }
    public int CalorDiffSize { get; init; }
    public int CSharpDiffSize { get; init; }

    public bool CalorFullyPreserved => CalorContractsPreserved && CalorEffectsPreserved && CalorIdsPreserved;
    public double CalorPreservationScore => (
        (CalorContractsPreserved ? 1.0 : 0.0) +
        (CalorEffectsPreserved ? 1.0 : 0.0) +
        (CalorIdsPreserved ? 1.0 : 0.0)) / 3.0;
}
