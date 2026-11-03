namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Options for running LLM tasks.
/// </summary>
public record LlmTaskRunnerOptions
{
    /// <summary>
    /// Reported-spend stopping threshold in USD for RunAllAsync.
    /// Checked between paired tasks, so an in-flight task can exceed it.
    /// </summary>
    public decimal BudgetLimit { get; init; } = 5.00m;

    /// <summary>
    /// Whether to use cached responses when available.
    /// </summary>
    public bool UseCache { get; init; } = true;

    /// <summary>
    /// Whether to refresh the cache for all tasks.
    /// </summary>
    public bool RefreshCache { get; init; }

    /// <summary>
    /// Specific task IDs to run (null means all tasks).
    /// </summary>
    public List<string>? TaskFilter { get; init; }

    /// <summary>
    /// Category filter (null means all categories).
    /// </summary>
    public string? CategoryFilter { get; init; }

    /// <summary>
    /// Maximum difficulty level to include.
    /// </summary>
    public int MaxDifficulty { get; init; } = 5;

    /// <summary>
    /// Number of tasks to sample (null means all tasks).
    /// </summary>
    public int? SampleSize { get; init; }

    /// <summary>
    /// Random seed for sampling (for reproducibility).
    /// </summary>
    public int? SampleSeed { get; init; }

    /// <summary>
    /// Whether this is a dry run (no actual API calls).
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Whether to enable verbose output.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Timeout for code execution in milliseconds.
    /// </summary>
    public int ExecutionTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Number of retry attempts for failed API calls.
    /// </summary>
    public int RetryAttempts { get; init; } = 2;

    /// <summary>
    /// Delay between retries in milliseconds.
    /// </summary>
    public int RetryDelayMs { get; init; } = 1000;

    /// <summary>
    /// Whether to run tasks in parallel.
    /// </summary>
    public bool Parallel { get; init; } = false;

    /// <summary>
    /// Maximum parallelism degree.
    /// </summary>
    public int MaxParallelism { get; init; } = 4;

    /// <summary>
    /// Specific model to use (null means provider default).
    /// </summary>
    public string? Model { get; init; }
}

/// <summary>
/// Interface for running LLM-based task completion tests.
/// </summary>
public interface ILlmTaskRunner
{
    /// <summary>
    /// Runs all tasks from a manifest.
    /// </summary>
    /// <param name="manifest">The task manifest.</param>
    /// <param name="options">Run options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated results.</returns>
    Task<LlmTaskRunResults> RunAllAsync(
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a single task.
    /// </summary>
    /// <param name="task">The task definition.</param>
    /// <param name="options">Run options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task result.</returns>
    Task<LlmTaskResult> RunTaskAsync(
        LlmTaskDefinition task,
        LlmTaskRunnerOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the cost to run all tasks.
    /// </summary>
    /// <param name="manifest">The task manifest.</param>
    /// <param name="options">Run options.</param>
    /// <returns>Cost estimate.</returns>
    LlmTaskCostEstimate EstimateCost(
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null);

    /// <summary>
    /// Gets the current budget usage.
    /// </summary>
    decimal CurrentSpend { get; }

    /// <summary>
    /// Gets the remaining budget.
    /// </summary>
    decimal RemainingBudget { get; }
}

/// <summary>
/// Cost estimate for running LLM tasks.
/// </summary>
public record LlmTaskCostEstimate
{
    /// <summary>
    /// Number of tasks to run.
    /// </summary>
    public int TaskCount { get; init; }

    /// <summary>
    /// Estimated total input tokens.
    /// </summary>
    public int EstimatedInputTokens { get; init; }

    /// <summary>
    /// Estimated total output tokens.
    /// </summary>
    public int EstimatedOutputTokens { get; init; }

    /// <summary>
    /// Estimated cost in USD.
    /// </summary>
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Number of cached responses available.
    /// </summary>
    public int CachedResponses { get; init; }

    /// <summary>
    /// Cost after accounting for cache hits.
    /// </summary>
    public decimal CostWithCache { get; init; }

    /// <summary>
    /// Whether the estimated cost exceeds the budget.
    /// </summary>
    public bool ExceedsBudget { get; init; }

    /// <summary>
    /// The budget limit.
    /// </summary>
    public decimal BudgetLimit { get; init; }

    /// <summary>
    /// Breakdown by category.
    /// </summary>
    public Dictionary<string, decimal> ByCategory { get; init; } = new();
}
