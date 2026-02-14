using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Execution;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Runs safety benchmark tests that measure contract enforcement quality.
/// Unlike the task completion benchmark, this focuses on error detection and quality.
/// </summary>
public sealed class SafetyBenchmarkRunner : IDisposable
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
    /// Creates a new safety benchmark runner.
    /// </summary>
    /// <param name="provider">The LLM provider to use.</param>
    /// <param name="cache">Optional response cache.</param>
    public SafetyBenchmarkRunner(ILlmProvider provider, LlmResponseCache? cache = null)
    {
        _provider = provider;
        _cache = cache ?? new LlmResponseCache();
        // Use Debug mode for contract enforcement - this is crucial for the safety benchmark
        _executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Debug);
        _verifier = new OutputVerifier();
    }

    /// <summary>
    /// Runs all safety benchmark tasks.
    /// </summary>
    public async Task<SafetyBenchmarkResults> RunAllAsync(
        LlmTaskManifest manifest,
        SafetyBenchmarkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SafetyBenchmarkOptions();
        _budgetLimit = options.BudgetLimit;

        var results = new SafetyBenchmarkResults
        {
            Provider = _provider.Name,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Filter tasks
        var tasks = FilterTasks(manifest.Tasks, options);

        if (options.Verbose)
        {
            Console.WriteLine($"Running {tasks.Count} safety benchmark tasks with provider '{_provider.Name}'");
            Console.WriteLine($"Budget: ${options.BudgetLimit:F2}");
            Console.WriteLine($"Contract mode: Debug (enforced)");
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
                var calorScore = taskResult.CalorResult.SafetyScore;
                var csharpScore = taskResult.CSharpResult.SafetyScore;
                var winner = calorScore > csharpScore ? "Calor" : csharpScore > calorScore ? "C#" : "Tie";
                Console.WriteLine($"    Safety: Calor={calorScore:F2}, C#={csharpScore:F2} ({winner})");
                Console.WriteLine($"    Error Quality: Calor={taskResult.CalorResult.AverageErrorQuality:F2}, C#={taskResult.CSharpResult.AverageErrorQuality:F2}");
            }
        }

        // Calculate summary
        results = results with { Summary = CalculateSummary(results.Results) };

        return results;
    }

    /// <summary>
    /// Runs a single safety benchmark task.
    /// </summary>
    public async Task<SafetyTaskResult> RunTaskAsync(
        LlmTaskDefinition task,
        SafetyBenchmarkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SafetyBenchmarkOptions();

        // Generate code for both languages
        var calorResult = await GenerateAndEvaluateSafetyAsync(
            task, "calor", task.GetPrompt("calor"), options, cancellationToken);

        var csharpResult = await GenerateAndEvaluateSafetyAsync(
            task, "csharp", task.GetPrompt("csharp"), options, cancellationToken);

        return new SafetyTaskResult
        {
            Task = task,
            CalorResult = calorResult,
            CSharpResult = csharpResult
        };
    }

    private async Task<SafetyLanguageResult> GenerateAndEvaluateSafetyAsync(
        LlmTaskDefinition task,
        string language,
        string prompt,
        SafetyBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        var genOptions = LlmGenerationOptions.Default with
        {
            SystemPrompt = GetSystemPrompt(language),
            Model = options.Model
        };

        // Check cache or generate
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
            return new SafetyLanguageResult
            {
                Language = language,
                GeneratedCode = "",
                CompilationSuccess = false,
                CompilationErrors = new List<string> { genResult.Error ?? "Generation failed" },
                SafetyScore = 0,
                TestResults = new List<SafetyTestCaseResult>()
            };
        }

        return await EvaluateSafetyAsync(task, language, genResult, options);
    }

    private async Task<SafetyLanguageResult> EvaluateSafetyAsync(
        LlmTaskDefinition task,
        string language,
        LlmGenerationResult genResult,
        SafetyBenchmarkOptions options)
    {
        var result = new SafetyLanguageResult
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
                return result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = calorResult.Errors,
                    SafetyScore = 0
                };
            }

            var csharpResult = _executor.CompileCSharp(calorResult.GeneratedCSharp!);
            if (!csharpResult.Success)
            {
                return result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = csharpResult.Errors,
                    SafetyScore = 0
                };
            }

            assemblyBytes = csharpResult.AssemblyBytes;
        }
        else
        {
            var csharpResult = _executor.CompileCSharp(genResult.GeneratedCode);
            if (!csharpResult.Success)
            {
                return result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = csharpResult.Errors,
                    SafetyScore = 0
                };
            }

            assemblyBytes = csharpResult.AssemblyBytes;
        }

        result = result with { CompilationSuccess = true };

        // Execute test cases and evaluate safety
        var testResults = new List<SafetyTestCaseResult>();
        var methodName = task.ExpectedSignature?.FunctionName ?? "compute";

        foreach (var (testCase, index) in task.TestCases.Select((tc, i) => (tc, i)))
        {
            var testResult = await ExecuteSafetyTestCase(
                assemblyBytes!, methodName, testCase, index, language, options);
            testResults.Add(testResult);
        }

        result = result with { TestResults = testResults };

        // Calculate safety metrics
        var normalTests = testResults.Where(t => !t.ExpectedViolation).ToList();
        var safetyTests = testResults.Where(t => t.ExpectedViolation).ToList();

        var normalCorrectness = normalTests.Count > 0
            ? normalTests.Count(t => t.NormalTestPassed) / (double)normalTests.Count
            : 1.0;

        var violationDetectionRate = safetyTests.Count > 0
            ? safetyTests.Count(t => t.ViolationDetected) / (double)safetyTests.Count
            : 1.0;

        var avgErrorQuality = safetyTests.Count > 0
            ? safetyTests.Average(t => t.ErrorQualityScore)
            : 0.0;

        // Calculate overall safety score using the scoring formula
        var safetyScore = SafetyScorer.CalculateSafetyScore(
            violationDetected: violationDetectionRate > 0.5,
            expectedViolation: true,
            errorQualityScore: avgErrorQuality,
            normalCorrectness: normalCorrectness);

        // Adjust: weight more precisely based on actual test composition
        var totalSafetyScore = (normalCorrectness * 0.30) +
                               (violationDetectionRate * 0.40) +
                               (avgErrorQuality * 0.30);

        return result with
        {
            SafetyScore = totalSafetyScore,
            ViolationDetectionRate = violationDetectionRate,
            AverageErrorQuality = avgErrorQuality,
            NormalCorrectness = normalCorrectness
        };
    }

    private Task<SafetyTestCaseResult> ExecuteSafetyTestCase(
        byte[] assemblyBytes,
        string methodName,
        TaskTestCase testCase,
        int index,
        string language,
        SafetyBenchmarkOptions options)
    {
        try
        {
            var arguments = testCase.Input.Select(ConvertJsonElement).ToArray();
            var execResult = _executor.Execute(assemblyBytes, methodName, arguments);

            var expectsViolation = testCase.ExpectsContractViolation;

            if (expectsViolation)
            {
                // Safety test case - we expect an exception
                var violationDetected = !execResult.Success &&
                    (execResult.ContractViolation || execResult.Exception != null);

                var errorQuality = SafetyScorer.ScoreErrorQuality(
                    execResult.Exception, language, expectedViolation: true);

                return Task.FromResult(new SafetyTestCaseResult
                {
                    Index = index,
                    ExpectedViolation = true,
                    ViolationDetected = violationDetected,
                    NormalTestPassed = false, // N/A for safety tests
                    ErrorQualityScore = errorQuality,
                    ExceptionAnalysis = execResult.SafetyAnalysis,
                    Description = testCase.Description,
                    ExecutionTimeMs = execResult.DurationMs
                });
            }
            else
            {
                // Normal test case - verify correct result
                var passed = execResult.Success;

                if (passed && testCase.Expected.HasValue)
                {
                    var verification = _verifier.Verify(execResult, testCase.Expected.Value);
                    passed = verification.Passed;
                }

                return Task.FromResult(new SafetyTestCaseResult
                {
                    Index = index,
                    ExpectedViolation = false,
                    ViolationDetected = !execResult.Success && execResult.ContractViolation,
                    NormalTestPassed = passed,
                    ErrorQualityScore = 0, // N/A for normal tests
                    ExecutionTimeMs = execResult.DurationMs
                });
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SafetyTestCaseResult
            {
                Index = index,
                ExpectedViolation = testCase.ExpectsContractViolation,
                ViolationDetected = false,
                NormalTestPassed = false,
                ErrorQualityScore = 0,
                ErrorMessage = $"Test execution error: {ex.Message}"
            });
        }
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

    private static List<LlmTaskDefinition> FilterTasks(
        List<LlmTaskDefinition> tasks,
        SafetyBenchmarkOptions options)
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
            var random = new Random();
            result = result.OrderBy(_ => random.Next()).Take(options.SampleSize.Value).ToList();
        }

        return result;
    }

    private static SafetyBenchmarkSummary CalculateSummary(List<SafetyTaskResult> results)
    {
        if (results.Count == 0)
        {
            return new SafetyBenchmarkSummary();
        }

        var calorScores = results.Select(r => r.CalorResult.SafetyScore).ToList();
        var csharpScores = results.Select(r => r.CSharpResult.SafetyScore).ToList();

        var calorViolationRates = results.Select(r => r.CalorResult.ViolationDetectionRate).ToList();
        var csharpViolationRates = results.Select(r => r.CSharpResult.ViolationDetectionRate).ToList();

        var calorErrorQualities = results.Select(r => r.CalorResult.AverageErrorQuality).ToList();
        var csharpErrorQualities = results.Select(r => r.CSharpResult.AverageErrorQuality).ToList();

        var calorNormalCorrectness = results.Select(r => r.CalorResult.NormalCorrectness).ToList();
        var csharpNormalCorrectness = results.Select(r => r.CSharpResult.NormalCorrectness).ToList();

        var avgCalorSafety = calorScores.Average();
        var avgCsharpSafety = csharpScores.Average();

        var byCategory = results
            .GroupBy(r => r.Task.Category)
            .ToDictionary(
                g => g.Key,
                g => new SafetyCategorySummary
                {
                    Category = g.Key,
                    TaskCount = g.Count(),
                    AverageCalorSafetyScore = g.Average(r => r.CalorResult.SafetyScore),
                    AverageCSharpSafetyScore = g.Average(r => r.CSharpResult.SafetyScore),
                    CalorViolationDetectionRate = g.Average(r => r.CalorResult.ViolationDetectionRate),
                    CSharpViolationDetectionRate = g.Average(r => r.CSharpResult.ViolationDetectionRate),
                    CalorErrorQuality = g.Average(r => r.CalorResult.AverageErrorQuality),
                    CSharpErrorQuality = g.Average(r => r.CSharpResult.AverageErrorQuality),
                    SafetyAdvantageRatio = g.Average(r => r.CSharpResult.SafetyScore) > 0
                        ? g.Average(r => r.CalorResult.SafetyScore) / g.Average(r => r.CSharpResult.SafetyScore)
                        : 1.0
                });

        return new SafetyBenchmarkSummary
        {
            TotalTasks = results.Count,
            CalorWins = results.Count(r => r.CalorResult.SafetyScore > r.CSharpResult.SafetyScore),
            CSharpWins = results.Count(r => r.CSharpResult.SafetyScore > r.CalorResult.SafetyScore),
            Ties = results.Count(r => Math.Abs(r.CalorResult.SafetyScore - r.CSharpResult.SafetyScore) < 0.01),
            AverageCalorSafetyScore = avgCalorSafety,
            AverageCSharpSafetyScore = avgCsharpSafety,
            SafetyAdvantageRatio = avgCsharpSafety > 0 ? avgCalorSafety / avgCsharpSafety : 1.0,
            CalorViolationDetectionRate = calorViolationRates.Average(),
            CSharpViolationDetectionRate = csharpViolationRates.Average(),
            CalorAverageErrorQuality = calorErrorQualities.Average(),
            CSharpAverageErrorQuality = csharpErrorQualities.Average(),
            CalorNormalCorrectness = calorNormalCorrectness.Average(),
            CSharpNormalCorrectness = csharpNormalCorrectness.Average(),
            ByCategory = byCategory
        };
    }

    public void Dispose()
    {
        _executor.Dispose();
    }
}

/// <summary>
/// Options for running safety benchmarks.
/// </summary>
public record SafetyBenchmarkOptions
{
    public decimal BudgetLimit { get; init; } = 5.00m;
    public bool UseCache { get; init; } = true;
    public bool RefreshCache { get; init; } = false;
    public bool DryRun { get; init; } = false;
    public bool Verbose { get; init; } = false;
    public List<string>? TaskFilter { get; init; }
    public string? CategoryFilter { get; init; }
    public int? SampleSize { get; init; }
    public string? Model { get; init; }
}

/// <summary>
/// Results from running safety benchmarks.
/// </summary>
public record SafetyBenchmarkResults
{
    public List<SafetyTaskResult> Results { get; init; } = new();
    public SafetyBenchmarkSummary Summary { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
    public string? Provider { get; init; }
    public decimal TotalCost => Results.Sum(r =>
        (r.CalorResult.GenerationMetadata?.Cost ?? 0) +
        (r.CSharpResult.GenerationMetadata?.Cost ?? 0));
}

/// <summary>
/// Summary statistics for safety benchmark.
/// </summary>
public record SafetyBenchmarkSummary
{
    public int TotalTasks { get; init; }
    public int CalorWins { get; init; }
    public int CSharpWins { get; init; }
    public int Ties { get; init; }
    public double AverageCalorSafetyScore { get; init; }
    public double AverageCSharpSafetyScore { get; init; }
    public double SafetyAdvantageRatio { get; init; }
    public double CalorViolationDetectionRate { get; init; }
    public double CSharpViolationDetectionRate { get; init; }
    public double CalorAverageErrorQuality { get; init; }
    public double CSharpAverageErrorQuality { get; init; }
    public double CalorNormalCorrectness { get; init; }
    public double CSharpNormalCorrectness { get; init; }
    public Dictionary<string, SafetyCategorySummary> ByCategory { get; init; } = new();
}

/// <summary>
/// Summary for a safety benchmark category.
/// </summary>
public record SafetyCategorySummary
{
    public required string Category { get; init; }
    public int TaskCount { get; init; }
    public double AverageCalorSafetyScore { get; init; }
    public double AverageCSharpSafetyScore { get; init; }
    public double CalorViolationDetectionRate { get; init; }
    public double CSharpViolationDetectionRate { get; init; }
    public double CalorErrorQuality { get; init; }
    public double CSharpErrorQuality { get; init; }
    public double SafetyAdvantageRatio { get; init; }
}

/// <summary>
/// Result for a single safety benchmark task.
/// </summary>
public record SafetyTaskResult
{
    public required LlmTaskDefinition Task { get; init; }
    public required SafetyLanguageResult CalorResult { get; init; }
    public required SafetyLanguageResult CSharpResult { get; init; }

    public double SafetyAdvantageRatio =>
        CSharpResult.SafetyScore > 0 ? CalorResult.SafetyScore / CSharpResult.SafetyScore : 1.0;
}

/// <summary>
/// Safety benchmark result for a single language.
/// </summary>
public record SafetyLanguageResult
{
    public required string Language { get; init; }
    public required string GeneratedCode { get; init; }
    public bool CompilationSuccess { get; init; }
    public List<string> CompilationErrors { get; init; } = new();
    public List<SafetyTestCaseResult> TestResults { get; init; } = new();
    public LlmGenerationMetadata? GenerationMetadata { get; init; }
    public double SafetyScore { get; init; }
    public double ViolationDetectionRate { get; init; }
    public double AverageErrorQuality { get; init; }
    public double NormalCorrectness { get; init; }
}

/// <summary>
/// Result of a single safety test case.
/// </summary>
public record SafetyTestCaseResult
{
    public int Index { get; init; }
    public bool ExpectedViolation { get; init; }
    public bool ViolationDetected { get; init; }
    public bool NormalTestPassed { get; init; }
    public double ErrorQualityScore { get; init; }
    public SafetyExceptionAnalysis? ExceptionAnalysis { get; init; }
    public string? Description { get; init; }
    public string? ErrorMessage { get; init; }
    public double ExecutionTimeMs { get; init; }
}
