using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Execution;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Runs code correctness benchmark tests that measure bug prevention through edge case handling.
/// This benchmark uses pure pass/fail scoring - no language bias, just "does the code work?"
/// </summary>
public sealed class CorrectnessBenchmarkRunner : IDisposable
{
    private readonly ILlmProvider _provider;
    private readonly LlmResponseCache _cache;
    private readonly CodeExecutor _executor;
    private readonly OutputVerifier _verifier;
    private decimal _currentSpend;
    private decimal _budgetLimit;

    public decimal CurrentSpend => _currentSpend;
    public decimal RemainingBudget => _budgetLimit - _currentSpend;

    /// <summary>
    /// Creates a new correctness benchmark runner.
    /// </summary>
    public CorrectnessBenchmarkRunner(ILlmProvider provider, LlmResponseCache? cache = null)
    {
        _provider = provider;
        _cache = cache ?? new LlmResponseCache();
        // Enable contract enforcement for fair comparison - Calor's contracts should
        // be enforced at runtime, just like C#'s ArgumentException guard clauses
        _executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Debug);
        _verifier = new OutputVerifier();
    }

    /// <summary>
    /// Runs all correctness benchmark tasks.
    /// </summary>
    public async Task<CorrectnessBenchmarkResults> RunAllAsync(
        LlmTaskManifest manifest,
        CorrectnessBenchmarkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CorrectnessBenchmarkOptions();
        _budgetLimit = options.BudgetLimit;

        var results = new CorrectnessBenchmarkResults
        {
            Provider = _provider.Name,
            Timestamp = DateTimeOffset.UtcNow
        };

        var tasks = FilterTasks(manifest.Tasks, options);

        if (options.Verbose)
        {
            Console.WriteLine($"Running {tasks.Count} correctness benchmark tasks with provider '{_provider.Name}'");
            Console.WriteLine($"Budget: ${options.BudgetLimit:F2}");
            Console.WriteLine();
        }

        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_currentSpend >= options.BudgetLimit)
            {
                if (options.Verbose)
                {
                    Console.WriteLine($"Budget exceeded (${_currentSpend:F2}/${options.BudgetLimit:F2}), stopping");
                }
                break;
            }

            if (options.Verbose)
            {
                Console.WriteLine($"  Running task: {task.Id} - {task.Name}");
            }

            var taskResult = await RunTaskAsync(task, options, cancellationToken);
            results.Results.Add(taskResult);

            if (options.Verbose)
            {
                var calorScore = taskResult.CalorResult.CorrectnessScore;
                var csharpScore = taskResult.CSharpResult.CorrectnessScore;
                var winner = calorScore > csharpScore ? "Calor" :
                             csharpScore > calorScore ? "C#" : "Tie";
                Console.WriteLine($"    Correctness: Calor={calorScore:P0}, C#={csharpScore:P0} ({winner})");
                Console.WriteLine($"    Edge Cases: Calor={taskResult.CalorResult.EdgeCaseScore:P0}, C#={taskResult.CSharpResult.EdgeCaseScore:P0}");
            }
        }

        results = results with { Summary = CalculateSummary(results.Results) };

        return results;
    }

    /// <summary>
    /// Runs a single correctness benchmark task.
    /// </summary>
    public async Task<CorrectnessTaskResult> RunTaskAsync(
        LlmTaskDefinition task,
        CorrectnessBenchmarkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CorrectnessBenchmarkOptions();

        var calorResult = await GenerateAndEvaluateAsync(
            task, "calor", task.GetPrompt("calor"), options, cancellationToken);

        var csharpResult = await GenerateAndEvaluateAsync(
            task, "csharp", task.GetPrompt("csharp"), options, cancellationToken);

        return new CorrectnessTaskResult
        {
            Task = task,
            CalorResult = calorResult,
            CSharpResult = csharpResult
        };
    }

    private async Task<CorrectnessLanguageResult> GenerateAndEvaluateAsync(
        LlmTaskDefinition task,
        string language,
        string prompt,
        CorrectnessBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        var genOptions = LlmGenerationOptions.Default with
        {
            SystemPrompt = GetSystemPrompt(language),
            Model = options.Model
        };

        LlmGenerationResult genResult;

        if (options.DryRun)
        {
            genResult = new LlmGenerationResult
            {
                Success = true,
                GeneratedCode = $"// Dry run - no code generated for {language}",
                Provider = _provider.Name,
                Model = _provider.DefaultModel,
                InputTokens = _provider.EstimateTokenCount(prompt),
                OutputTokens = 100,
                Cost = _provider.EstimateCost(_provider.EstimateTokenCount(prompt), 100),
                FromCache = false
            };
        }
        else if (options.UseCache && !options.RefreshCache)
        {
            var cached = await _cache.GetAsync(_provider.Name, prompt, genOptions);
            if (cached != null)
            {
                genResult = cached;
            }
            else
            {
                genResult = await _provider.GenerateCodeAsync(prompt, language, genOptions, cancellationToken);
                if (genResult.Success)
                {
                    await _cache.SetAsync(_provider.Name, prompt, genOptions, genResult);
                }
            }
        }
        else
        {
            genResult = await _provider.GenerateCodeAsync(prompt, language, genOptions, cancellationToken);
            if (genResult.Success && options.UseCache)
            {
                await _cache.SetAsync(_provider.Name, prompt, genOptions, genResult);
            }
        }

        _currentSpend += genResult.Cost;

        if (!genResult.Success)
        {
            return new CorrectnessLanguageResult
            {
                Language = language,
                GeneratedCode = "",
                CompilationSuccess = false,
                CompilationErrors = new List<string> { genResult.Error ?? "Generation failed" },
                CorrectnessScore = 0,
                EdgeCaseScore = 0,
                TestResults = new List<CorrectnessTestResult>()
            };
        }

        return await EvaluateCorrectnessAsync(task, language, genResult, options);
    }

    private Task<CorrectnessLanguageResult> EvaluateCorrectnessAsync(
        LlmTaskDefinition task,
        string language,
        LlmGenerationResult genResult,
        CorrectnessBenchmarkOptions options)
    {
        var result = new CorrectnessLanguageResult
        {
            Language = language,
            GeneratedCode = genResult.GeneratedCode
        };

        // Compile
        byte[]? assemblyBytes;

        if (language.Equals("calor", StringComparison.OrdinalIgnoreCase))
        {
            var calorResult = _executor.CompileCalor(genResult.GeneratedCode);
            if (!calorResult.Success)
            {
                return Task.FromResult(result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = calorResult.Errors,
                    CorrectnessScore = 0,
                    EdgeCaseScore = 0
                });
            }

            var csharpResult = _executor.CompileCSharp(calorResult.GeneratedCSharp!);
            if (!csharpResult.Success)
            {
                return Task.FromResult(result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = csharpResult.Errors,
                    CorrectnessScore = 0,
                    EdgeCaseScore = 0
                });
            }

            assemblyBytes = csharpResult.AssemblyBytes;
        }
        else
        {
            var csharpResult = _executor.CompileCSharp(genResult.GeneratedCode);
            if (!csharpResult.Success)
            {
                return Task.FromResult(result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = csharpResult.Errors,
                    CorrectnessScore = 0,
                    EdgeCaseScore = 0
                });
            }

            assemblyBytes = csharpResult.AssemblyBytes;
        }

        result = result with { CompilationSuccess = true };

        // Execute test cases
        var testResults = new List<CorrectnessTestResult>();
        var methodName = task.ExpectedSignature?.FunctionName ?? "compute";

        foreach (var (testCase, index) in task.TestCases.Select((tc, i) => (tc, i)))
        {
            var testResult = ExecuteTestCase(assemblyBytes!, methodName, testCase, index);
            testResults.Add(testResult);
        }

        result = result with { TestResults = testResults };

        // Calculate scores - pure pass/fail
        var allTests = testResults;
        var edgeCaseTests = testResults.Where(t => t.IsEdgeCase).ToList();
        var normalTests = testResults.Where(t => !t.IsEdgeCase).ToList();

        var overallScore = allTests.Count > 0
            ? (double)allTests.Count(t => t.Passed) / allTests.Count
            : 0.0;

        var edgeCaseScore = edgeCaseTests.Count > 0
            ? (double)edgeCaseTests.Count(t => t.Passed) / edgeCaseTests.Count
            : 1.0; // No edge cases = perfect edge case score

        var normalScore = normalTests.Count > 0
            ? (double)normalTests.Count(t => t.Passed) / normalTests.Count
            : 0.0;

        return Task.FromResult(result with
        {
            CorrectnessScore = overallScore,
            EdgeCaseScore = edgeCaseScore,
            NormalCaseScore = normalScore,
            TestsPassed = allTests.Count(t => t.Passed),
            TestsTotal = allTests.Count
        });
    }

    private CorrectnessTestResult ExecuteTestCase(
        byte[] assemblyBytes,
        string methodName,
        TaskTestCase testCase,
        int index)
    {
        var isEdgeCase = testCase.IsEdgeCase;

        try
        {
            var arguments = testCase.Input.Select(ConvertJsonElement).ToArray();
            var execResult = _executor.Execute(assemblyBytes, methodName, arguments);

            if (!execResult.Success)
            {
                // If we expected a contract violation, check if we got one
                // Fair comparison: Accept BOTH Calor's ContractViolationException
                // AND C#'s ArgumentException/ArgumentOutOfRangeException as valid contract enforcement
                if (testCase.ExpectsContractViolation)
                {
                    var isValidContractEnforcement = IsContractEnforcementException(execResult.Exception);
                    return new CorrectnessTestResult
                    {
                        Index = index,
                        IsEdgeCase = isEdgeCase,
                        Passed = isValidContractEnforcement,
                        ErrorMessage = isValidContractEnforcement
                            ? null
                            : $"Expected contract enforcement but got: {execResult.Exception?.GetType().Name}",
                        ExecutionTimeMs = execResult.DurationMs
                    };
                }

                return new CorrectnessTestResult
                {
                    Index = index,
                    IsEdgeCase = isEdgeCase,
                    Passed = false,
                    ErrorMessage = execResult.Exception?.Message ?? "Execution failed",
                    ExecutionTimeMs = execResult.DurationMs
                };
            }

            // If we expected a contract violation but execution succeeded, that's a failure
            if (testCase.ExpectsContractViolation)
            {
                return new CorrectnessTestResult
                {
                    Index = index,
                    IsEdgeCase = isEdgeCase,
                    Passed = false,
                    ErrorMessage = "Expected contract violation but execution succeeded",
                    ExecutionTimeMs = execResult.DurationMs
                };
            }

            // Verify output
            if (testCase.Expected.HasValue)
            {
                var verification = _verifier.Verify(execResult, testCase.Expected.Value);
                return new CorrectnessTestResult
                {
                    Index = index,
                    IsEdgeCase = isEdgeCase,
                    Passed = verification.Passed,
                    ActualValue = execResult.ReturnValue?.ToString(),
                    ExpectedValue = testCase.Expected.Value.ToString(),
                    ErrorMessage = verification.Passed ? null : verification.Reason,
                    ExecutionTimeMs = execResult.DurationMs
                };
            }

            return new CorrectnessTestResult
            {
                Index = index,
                IsEdgeCase = isEdgeCase,
                Passed = true,
                ActualValue = execResult.ReturnValue?.ToString(),
                ExecutionTimeMs = execResult.DurationMs
            };
        }
        catch (Exception ex)
        {
            // If we expected a contract violation and got an exception, check if it's valid
            if (testCase.ExpectsContractViolation && IsContractEnforcementException(ex))
            {
                return new CorrectnessTestResult
                {
                    Index = index,
                    IsEdgeCase = isEdgeCase,
                    Passed = true,
                    ErrorMessage = null,
                    ExecutionTimeMs = 0
                };
            }

            return new CorrectnessTestResult
            {
                Index = index,
                IsEdgeCase = isEdgeCase,
                Passed = false,
                ErrorMessage = $"Test execution error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Determines if an exception represents valid contract enforcement.
    /// Fair comparison: accepts both Calor's ContractViolationException AND
    /// C#'s standard argument validation exceptions (ArgumentException, etc.)
    /// </summary>
    private static bool IsContractEnforcementException(Exception? exception)
    {
        if (exception == null) return false;

        var exceptionType = exception.GetType();
        var typeName = exceptionType.Name;

        // Calor's contract violation
        if (typeName.Contains("ContractViolation")) return true;

        // C#'s standard argument validation (fair comparison)
        if (exception is ArgumentException) return true;  // Includes ArgumentNullException, ArgumentOutOfRangeException
        if (typeName == "ArgumentException") return true;
        if (typeName == "ArgumentNullException") return true;
        if (typeName == "ArgumentOutOfRangeException") return true;

        // Also accept InvalidOperationException as it's often used for precondition violations
        if (exception is InvalidOperationException) return true;

        return false;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToArray(),
            _ => element.GetRawText()
        };
    }

    private static string GetSystemPrompt(string language) =>
        LlmTaskRunner.GetSystemPromptForLanguage(language);

    internal static List<LlmTaskDefinition> FilterTasks(
        List<LlmTaskDefinition> tasks,
        CorrectnessBenchmarkOptions options)
    {
        var filtered = tasks.AsEnumerable();

        if (options.TaskFilter != null && options.TaskFilter.Count > 0)
        {
            var filterSet = options.TaskFilter.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => filterSet.Contains(t.Id));
        }

        if (!string.IsNullOrEmpty(options.CategoryFilter))
        {
            filtered = filtered.Where(t =>
                t.Category.Equals(options.CategoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        var result = filtered.ToList();

        if (options.SampleSize.HasValue && options.SampleSize.Value < result.Count)
        {
            // Seed the shuffle Random so two developers with the same seed observe
            // the same corpus order. See docs/plans/2026-08-18-test-suite-audit.md (R11).
            var random = options.SampleSeed.HasValue
                ? new Random(options.SampleSeed.Value)
                : new Random();
            result = result.OrderBy(_ => random.Next()).Take(options.SampleSize.Value).ToList();
        }

        return result;
    }

    private static CorrectnessBenchmarkSummary CalculateSummary(List<CorrectnessTaskResult> results)
    {
        if (results.Count == 0)
        {
            return new CorrectnessBenchmarkSummary();
        }

        var calorScores = results.Select(r => r.CalorResult.CorrectnessScore).ToList();
        var csharpScores = results.Select(r => r.CSharpResult.CorrectnessScore).ToList();

        var calorEdgeScores = results.Select(r => r.CalorResult.EdgeCaseScore).ToList();
        var csharpEdgeScores = results.Select(r => r.CSharpResult.EdgeCaseScore).ToList();

        var avgCalorCorrectness = calorScores.Average();
        var avgCsharpCorrectness = csharpScores.Average();

        var byCategory = results
            .GroupBy(r => r.Task.Category)
            .ToDictionary(
                g => g.Key,
                g => new CorrectnessCategorySummary
                {
                    Category = g.Key,
                    TaskCount = g.Count(),
                    AverageCalorScore = g.Average(r => r.CalorResult.CorrectnessScore),
                    AverageCSharpScore = g.Average(r => r.CSharpResult.CorrectnessScore),
                    CalorEdgeCaseScore = g.Average(r => r.CalorResult.EdgeCaseScore),
                    CSharpEdgeCaseScore = g.Average(r => r.CSharpResult.EdgeCaseScore),
                    AdvantageRatio = g.Average(r => r.CSharpResult.CorrectnessScore) > 0
                        ? g.Average(r => r.CalorResult.CorrectnessScore) / g.Average(r => r.CSharpResult.CorrectnessScore)
                        : 1.0
                });

        return new CorrectnessBenchmarkSummary
        {
            TotalTasks = results.Count,
            CalorWins = results.Count(r => r.CalorResult.CorrectnessScore > r.CSharpResult.CorrectnessScore + 0.001),
            CSharpWins = results.Count(r => r.CSharpResult.CorrectnessScore > r.CalorResult.CorrectnessScore + 0.001),
            Ties = results.Count(r => Math.Abs(r.CalorResult.CorrectnessScore - r.CSharpResult.CorrectnessScore) <= 0.001),
            AverageCalorScore = avgCalorCorrectness,
            AverageCSharpScore = avgCsharpCorrectness,
            AdvantageRatio = avgCsharpCorrectness > 0 ? avgCalorCorrectness / avgCsharpCorrectness : 1.0,
            CalorEdgeCaseScore = calorEdgeScores.Average(),
            CSharpEdgeCaseScore = csharpEdgeScores.Average(),
            TotalTestsPassed = results.Sum(r => r.CalorResult.TestsPassed + r.CSharpResult.TestsPassed),
            TotalTests = results.Sum(r => r.CalorResult.TestsTotal + r.CSharpResult.TestsTotal),
            ByCategory = byCategory
        };
    }

    public void Dispose()
    {
        _executor.Dispose();
    }
}

/// <summary>
/// Options for running correctness benchmarks.
/// </summary>
public record CorrectnessBenchmarkOptions
{
    public decimal BudgetLimit { get; init; } = 5.00m;
    public bool UseCache { get; init; } = true;
    public bool RefreshCache { get; init; } = false;
    public bool DryRun { get; init; } = false;
    public bool Verbose { get; init; } = false;
    public List<string>? TaskFilter { get; init; }
    public string? CategoryFilter { get; init; }
    public int? SampleSize { get; init; }

    /// <summary>
    /// Random seed used to shuffle the corpus when sampling. Null means
    /// non-deterministic (system time). See docs/plans/2026-08-18-test-suite-audit.md (R11).
    /// </summary>
    public int? SampleSeed { get; init; }
    public string? Model { get; init; }
}

/// <summary>
/// Results from running correctness benchmarks.
/// </summary>
public record CorrectnessBenchmarkResults
{
    public List<CorrectnessTaskResult> Results { get; init; } = new();
    public CorrectnessBenchmarkSummary Summary { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
    public string? Provider { get; init; }
}

/// <summary>
/// Summary statistics for correctness benchmark.
/// </summary>
public record CorrectnessBenchmarkSummary
{
    public int TotalTasks { get; init; }
    public int CalorWins { get; init; }
    public int CSharpWins { get; init; }
    public int Ties { get; init; }
    public double AverageCalorScore { get; init; }
    public double AverageCSharpScore { get; init; }
    public double AdvantageRatio { get; init; }
    public double CalorEdgeCaseScore { get; init; }
    public double CSharpEdgeCaseScore { get; init; }
    public int TotalTestsPassed { get; init; }
    public int TotalTests { get; init; }
    public Dictionary<string, CorrectnessCategorySummary> ByCategory { get; init; } = new();
}

/// <summary>
/// Summary for a correctness benchmark category.
/// </summary>
public record CorrectnessCategorySummary
{
    public required string Category { get; init; }
    public int TaskCount { get; init; }
    public double AverageCalorScore { get; init; }
    public double AverageCSharpScore { get; init; }
    public double CalorEdgeCaseScore { get; init; }
    public double CSharpEdgeCaseScore { get; init; }
    public double AdvantageRatio { get; init; }
}

/// <summary>
/// Result for a single correctness benchmark task.
/// </summary>
public record CorrectnessTaskResult
{
    public required LlmTaskDefinition Task { get; init; }
    public required CorrectnessLanguageResult CalorResult { get; init; }
    public required CorrectnessLanguageResult CSharpResult { get; init; }

    public double AdvantageRatio =>
        CSharpResult.CorrectnessScore > 0
            ? CalorResult.CorrectnessScore / CSharpResult.CorrectnessScore
            : 1.0;
}

/// <summary>
/// Correctness benchmark result for a single language.
/// </summary>
public record CorrectnessLanguageResult
{
    public required string Language { get; init; }
    public required string GeneratedCode { get; init; }
    public bool CompilationSuccess { get; init; }
    public List<string> CompilationErrors { get; init; } = new();
    public List<CorrectnessTestResult> TestResults { get; init; } = new();
    public double CorrectnessScore { get; init; }
    public double EdgeCaseScore { get; init; }
    public double NormalCaseScore { get; init; }
    public int TestsPassed { get; init; }
    public int TestsTotal { get; init; }
}

/// <summary>
/// Result of a single correctness test case.
/// </summary>
public record CorrectnessTestResult
{
    public int Index { get; init; }
    public bool IsEdgeCase { get; init; }
    public bool Passed { get; init; }
    public string? ActualValue { get; init; }
    public string? ExpectedValue { get; init; }
    public string? ErrorMessage { get; init; }
    public double ExecutionTimeMs { get; init; }
}
