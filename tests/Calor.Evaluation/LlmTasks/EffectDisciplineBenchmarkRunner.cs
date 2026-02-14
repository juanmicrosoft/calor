using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Execution;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Runs effect discipline benchmark tests that measure side effect management.
/// Evaluates how well code prevents real-world bugs related to:
/// - Flaky tests (non-determinism)
/// - Security boundaries (unauthorized I/O)
/// - Side effect transparency (hidden effects)
/// - Cache safety (memoization correctness)
/// </summary>
public sealed class EffectDisciplineBenchmarkRunner : IDisposable
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
    /// Creates a new effect discipline benchmark runner.
    /// </summary>
    /// <param name="provider">The LLM provider to use.</param>
    /// <param name="cache">Optional response cache.</param>
    public EffectDisciplineBenchmarkRunner(ILlmProvider provider, LlmResponseCache? cache = null)
    {
        _provider = provider;
        _cache = cache ?? new LlmResponseCache();
        // Use Release mode - effect discipline is about code quality, not runtime checks
        _executor = new CodeExecutor(timeoutMs: 5000, contractMode: EmitContractMode.Release);
        _verifier = new OutputVerifier();
    }

    /// <summary>
    /// Runs all effect discipline benchmark tasks.
    /// </summary>
    public async Task<EffectDisciplineResults> RunAllAsync(
        LlmTaskManifest manifest,
        EffectDisciplineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EffectDisciplineOptions();
        _budgetLimit = options.BudgetLimit;

        var results = new EffectDisciplineResults
        {
            Provider = _provider.Name,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Filter tasks
        var tasks = FilterTasks(manifest.Tasks, options);

        if (options.Verbose)
        {
            Console.WriteLine($"Running {tasks.Count} effect discipline tasks with provider '{_provider.Name}'");
            Console.WriteLine($"Budget: ${options.BudgetLimit:F2}");
            Console.WriteLine();
            Console.WriteLine("Categories:");
            foreach (var category in tasks.GroupBy(t => t.Category))
            {
                Console.WriteLine($"  - {category.Key}: {category.Count()} tasks");
            }
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
                var calorScore = taskResult.CalorResult.DisciplineScore;
                var csharpScore = taskResult.CSharpResult.DisciplineScore;
                var winner = calorScore > csharpScore ? "Calor" : csharpScore > calorScore ? "C#" : "Tie";
                Console.WriteLine($"    Discipline: Calor={calorScore:F2}, C#={csharpScore:F2} ({winner})");
                Console.WriteLine($"    Bug Prevention: Calor={taskResult.CalorResult.BugPreventionScore:F2}, C#={taskResult.CSharpResult.BugPreventionScore:F2}");
            }
        }

        // Calculate summary
        results = results with { Summary = CalculateSummary(results.Results) };

        return results;
    }

    /// <summary>
    /// Runs a single effect discipline task.
    /// </summary>
    public async Task<EffectDisciplineTaskResult> RunTaskAsync(
        LlmTaskDefinition task,
        EffectDisciplineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EffectDisciplineOptions();

        // Generate code for both languages
        var calorResult = await GenerateAndEvaluateAsync(
            task, "calor", task.GetPrompt("calor"), options, cancellationToken);

        var csharpResult = await GenerateAndEvaluateAsync(
            task, "csharp", task.GetPrompt("csharp"), options, cancellationToken);

        return new EffectDisciplineTaskResult
        {
            Task = task,
            CalorResult = calorResult,
            CSharpResult = csharpResult
        };
    }

    private async Task<EffectDisciplineLanguageResult> GenerateAndEvaluateAsync(
        LlmTaskDefinition task,
        string language,
        string prompt,
        EffectDisciplineOptions options,
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
            return new EffectDisciplineLanguageResult
            {
                Language = language,
                GeneratedCode = "",
                CompilationSuccess = false,
                CompilationErrors = new List<string> { genResult.Error ?? "Generation failed" },
                DisciplineScore = 0,
                TestResults = new List<EffectDisciplineTestResult>()
            };
        }

        return await EvaluateDisciplineAsync(task, language, genResult, options);
    }

    private async Task<EffectDisciplineLanguageResult> EvaluateDisciplineAsync(
        LlmTaskDefinition task,
        string language,
        LlmGenerationResult genResult,
        EffectDisciplineOptions options)
    {
        var result = new EffectDisciplineLanguageResult
        {
            Language = language,
            GeneratedCode = genResult.GeneratedCode,
            GenerationMetadata = new LlmGenerationMetadata
            {
                Provider = genResult.Provider,
                Model = genResult.Model,
                InputTokens = genResult.InputTokens,
                OutputTokens = genResult.OutputTokens,
                Cost = genResult.Cost,
                FromCache = genResult.FromCache
            }
        };

        // Compile
        byte[]? assemblyBytes;
        List<string> effectViolations = new();
        List<AnalyzerDiagnostic> analyzerDiagnostics = new();

        if (language.Equals("calor", StringComparison.OrdinalIgnoreCase))
        {
            var calorResult = _executor.CompileCalor(genResult.GeneratedCode);
            if (!calorResult.Success)
            {
                // Check if these are effect violations (which is actually good for the benchmark)
                effectViolations = calorResult.Errors
                    .Where(e => e.Contains("effect", StringComparison.OrdinalIgnoreCase) ||
                               e.Contains("pure", StringComparison.OrdinalIgnoreCase) ||
                               e.Contains("§E", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var earlyBugPreventionScore = EffectDisciplineScorer.ScoreCalorBugPrevention(
                    genResult.GeneratedCode, false, effectViolations, task.Category);

                return result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = calorResult.Errors,
                    EffectViolations = effectViolations,
                    BugPreventionScore = earlyBugPreventionScore,
                    // If compilation failed due to effect violation, that's actually good discipline
                    DisciplineScore = effectViolations.Count > 0 ? earlyBugPreventionScore * 0.8 : 0
                };
            }

            var csharpResult = _executor.CompileCSharp(calorResult.GeneratedCSharp!);
            if (!csharpResult.Success)
            {
                return result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = csharpResult.Errors,
                    DisciplineScore = 0
                };
            }

            assemblyBytes = csharpResult.AssemblyBytes;
        }
        else
        {
            // For C#, run effect discipline analyzers
            if (options.EnableAnalyzers)
            {
                var analysisResult = await EffectAnalysis.AnalyzeAsync(genResult.GeneratedCode, task.Category);
                analyzerDiagnostics = analysisResult.Diagnostics;
            }

            var csharpResult = _executor.CompileCSharp(genResult.GeneratedCode);
            if (!csharpResult.Success)
            {
                return result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = csharpResult.Errors,
                    AnalyzerDiagnostics = analyzerDiagnostics,
                    DisciplineScore = 0
                };
            }

            assemblyBytes = csharpResult.AssemblyBytes;
        }

        result = result with
        {
            CompilationSuccess = true,
            AnalyzerDiagnostics = analyzerDiagnostics
        };

        // Execute test cases
        var testResults = new List<EffectDisciplineTestResult>();
        var methodName = task.ExpectedSignature?.FunctionName ?? "compute";

        foreach (var (testCase, index) in task.TestCases.Select((tc, i) => (tc, i)))
        {
            var testResult = ExecuteTestCase(assemblyBytes!, methodName, testCase, index);
            testResults.Add(testResult);
        }

        result = result with { TestResults = testResults };

        // Calculate functional correctness
        var correctnessScore = testResults.Count > 0
            ? testResults.Count(t => t.Passed) / (double)testResults.Count
            : 0.0;

        // Calculate bug prevention score
        double bugPreventionScore;
        if (language.Equals("calor", StringComparison.OrdinalIgnoreCase))
        {
            bugPreventionScore = EffectDisciplineScorer.ScoreCalorBugPrevention(
                genResult.GeneratedCode, true, effectViolations, task.Category);
        }
        else
        {
            // For C#, use analyzers if enabled, otherwise heuristics
            bugPreventionScore = EffectDisciplineScorer.ScoreCSharpBugPrevention(
                genResult.GeneratedCode,
                true,
                analyzerDiagnostics.Count > 0 ? analyzerDiagnostics : null,
                task.Category);
        }

        // Calculate maintainability score
        var maintainabilityScore = EffectDisciplineScorer.ScoreMaintainability(
            genResult.GeneratedCode, language);

        // Calculate overall discipline score
        var disciplineScore = EffectDisciplineScorer.CalculateDisciplineScore(
            correctnessScore, bugPreventionScore, maintainabilityScore);

        return result with
        {
            CorrectnessScore = correctnessScore,
            BugPreventionScore = bugPreventionScore,
            MaintainabilityScore = maintainabilityScore,
            DisciplineScore = disciplineScore
        };
    }

    private EffectDisciplineTestResult ExecuteTestCase(
        byte[] assemblyBytes,
        string methodName,
        TaskTestCase testCase,
        int index)
    {
        try
        {
            var arguments = testCase.Input.Select(ConvertJsonElement).ToArray();
            var execResult = _executor.Execute(assemblyBytes, methodName, arguments);

            if (!execResult.Success)
            {
                return new EffectDisciplineTestResult
                {
                    Index = index,
                    Passed = false,
                    ErrorMessage = execResult.Error ?? "Execution failed",
                    ExecutionTimeMs = execResult.DurationMs
                };
            }

            // Verify output matches expected
            if (testCase.Expected.HasValue)
            {
                var verification = _verifier.Verify(execResult, testCase.Expected.Value);
                return new EffectDisciplineTestResult
                {
                    Index = index,
                    Passed = verification.Passed,
                    ActualOutput = execResult.ReturnValue?.ToString(),
                    ExpectedOutput = testCase.Expected.Value.ToString(),
                    ErrorMessage = verification.Passed ? null : verification.Reason,
                    ExecutionTimeMs = execResult.DurationMs
                };
            }

            return new EffectDisciplineTestResult
            {
                Index = index,
                Passed = true,
                ActualOutput = execResult.ReturnValue?.ToString(),
                ExecutionTimeMs = execResult.DurationMs
            };
        }
        catch (Exception ex)
        {
            return new EffectDisciplineTestResult
            {
                Index = index,
                Passed = false,
                ErrorMessage = $"Test execution error: {ex.Message}"
            };
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
        EffectDisciplineOptions options)
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

    private static EffectDisciplineSummary CalculateSummary(List<EffectDisciplineTaskResult> results)
    {
        if (results.Count == 0)
        {
            return new EffectDisciplineSummary();
        }

        var calorScores = results.Select(r => r.CalorResult.DisciplineScore).ToList();
        var csharpScores = results.Select(r => r.CSharpResult.DisciplineScore).ToList();

        var calorBugPrevention = results.Select(r => r.CalorResult.BugPreventionScore).ToList();
        var csharpBugPrevention = results.Select(r => r.CSharpResult.BugPreventionScore).ToList();

        var calorCorrectness = results.Select(r => r.CalorResult.CorrectnessScore).ToList();
        var csharpCorrectness = results.Select(r => r.CSharpResult.CorrectnessScore).ToList();

        var avgCalorDiscipline = calorScores.Average();
        var avgCsharpDiscipline = csharpScores.Average();

        var byCategory = results
            .GroupBy(r => r.Task.Category)
            .ToDictionary(
                g => g.Key,
                g => new EffectDisciplineCategorySummary
                {
                    Category = g.Key,
                    TaskCount = g.Count(),
                    AverageCalorDisciplineScore = g.Average(r => r.CalorResult.DisciplineScore),
                    AverageCSharpDisciplineScore = g.Average(r => r.CSharpResult.DisciplineScore),
                    CalorBugPreventionRate = g.Average(r => r.CalorResult.BugPreventionScore),
                    CSharpBugPreventionRate = g.Average(r => r.CSharpResult.BugPreventionScore),
                    CalorCorrectnessRate = g.Average(r => r.CalorResult.CorrectnessScore),
                    CSharpCorrectnessRate = g.Average(r => r.CSharpResult.CorrectnessScore),
                    DisciplineAdvantageRatio = g.Average(r => r.CSharpResult.DisciplineScore) > 0
                        ? g.Average(r => r.CalorResult.DisciplineScore) / g.Average(r => r.CSharpResult.DisciplineScore)
                        : 1.0
                });

        return new EffectDisciplineSummary
        {
            TotalTasks = results.Count,
            CalorWins = results.Count(r => r.CalorResult.DisciplineScore > r.CSharpResult.DisciplineScore),
            CSharpWins = results.Count(r => r.CSharpResult.DisciplineScore > r.CalorResult.DisciplineScore),
            Ties = results.Count(r => Math.Abs(r.CalorResult.DisciplineScore - r.CSharpResult.DisciplineScore) < 0.01),
            AverageCalorDisciplineScore = avgCalorDiscipline,
            AverageCSharpDisciplineScore = avgCsharpDiscipline,
            DisciplineAdvantageRatio = avgCsharpDiscipline > 0 ? avgCalorDiscipline / avgCsharpDiscipline : 1.0,
            CalorBugPreventionRate = calorBugPrevention.Average(),
            CSharpBugPreventionRate = csharpBugPrevention.Average(),
            CalorCorrectnessRate = calorCorrectness.Average(),
            CSharpCorrectnessRate = csharpCorrectness.Average(),
            ByCategory = byCategory
        };
    }

    public void Dispose()
    {
        _executor.Dispose();
    }
}

/// <summary>
/// Options for running effect discipline benchmarks.
/// </summary>
public record EffectDisciplineOptions
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
    public bool EnableAnalyzers { get; init; } = false;
}

/// <summary>
/// Results from running effect discipline benchmarks.
/// </summary>
public record EffectDisciplineResults
{
    public List<EffectDisciplineTaskResult> Results { get; init; } = new();
    public EffectDisciplineSummary Summary { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
    public string? Provider { get; init; }
    public decimal TotalCost => Results.Sum(r =>
        (r.CalorResult.GenerationMetadata?.Cost ?? 0) +
        (r.CSharpResult.GenerationMetadata?.Cost ?? 0));
}

/// <summary>
/// Summary statistics for effect discipline benchmark.
/// </summary>
public record EffectDisciplineSummary
{
    public int TotalTasks { get; init; }
    public int CalorWins { get; init; }
    public int CSharpWins { get; init; }
    public int Ties { get; init; }
    public double AverageCalorDisciplineScore { get; init; }
    public double AverageCSharpDisciplineScore { get; init; }
    public double DisciplineAdvantageRatio { get; init; }
    public double CalorBugPreventionRate { get; init; }
    public double CSharpBugPreventionRate { get; init; }
    public double CalorCorrectnessRate { get; init; }
    public double CSharpCorrectnessRate { get; init; }
    public Dictionary<string, EffectDisciplineCategorySummary> ByCategory { get; init; } = new();
}

/// <summary>
/// Summary for an effect discipline category.
/// </summary>
public record EffectDisciplineCategorySummary
{
    public required string Category { get; init; }
    public int TaskCount { get; init; }
    public double AverageCalorDisciplineScore { get; init; }
    public double AverageCSharpDisciplineScore { get; init; }
    public double CalorBugPreventionRate { get; init; }
    public double CSharpBugPreventionRate { get; init; }
    public double CalorCorrectnessRate { get; init; }
    public double CSharpCorrectnessRate { get; init; }
    public double DisciplineAdvantageRatio { get; init; }
}

/// <summary>
/// Result for a single effect discipline task.
/// </summary>
public record EffectDisciplineTaskResult
{
    public required LlmTaskDefinition Task { get; init; }
    public required EffectDisciplineLanguageResult CalorResult { get; init; }
    public required EffectDisciplineLanguageResult CSharpResult { get; init; }

    public double DisciplineAdvantageRatio =>
        CSharpResult.DisciplineScore > 0 ? CalorResult.DisciplineScore / CSharpResult.DisciplineScore : 1.0;
}

/// <summary>
/// Effect discipline result for a single language.
/// </summary>
public record EffectDisciplineLanguageResult
{
    public required string Language { get; init; }
    public required string GeneratedCode { get; init; }
    public bool CompilationSuccess { get; init; }
    public List<string> CompilationErrors { get; init; } = new();
    public List<string> EffectViolations { get; init; } = new();
    public List<AnalyzerDiagnostic> AnalyzerDiagnostics { get; init; } = new();
    public List<EffectDisciplineTestResult> TestResults { get; init; } = new();
    public LlmGenerationMetadata? GenerationMetadata { get; init; }
    public double DisciplineScore { get; init; }
    public double CorrectnessScore { get; init; }
    public double BugPreventionScore { get; init; }
    public double MaintainabilityScore { get; init; }
}

/// <summary>
/// Result of a single test case in effect discipline benchmark.
/// </summary>
public record EffectDisciplineTestResult
{
    public int Index { get; init; }
    public bool Passed { get; init; }
    public string? ActualOutput { get; init; }
    public string? ExpectedOutput { get; init; }
    public string? ErrorMessage { get; init; }
    public double ExecutionTimeMs { get; init; }
}
