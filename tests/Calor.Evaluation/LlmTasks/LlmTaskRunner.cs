using System.Text.Json;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Execution;
using Calor.Evaluation.LlmTasks.Providers;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Runs LLM-based task completion tests.
/// Orchestrates code generation, compilation, execution, and scoring.
/// </summary>
public sealed class LlmTaskRunner : ILlmTaskRunner, IDisposable
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
    /// Creates a new LLM task runner.
    /// </summary>
    /// <param name="provider">The LLM provider to use.</param>
    /// <param name="cache">Optional response cache.</param>
    public LlmTaskRunner(ILlmProvider provider, LlmResponseCache? cache = null)
    {
        _provider = provider;
        _cache = cache ?? new LlmResponseCache();
        _executor = new CodeExecutor();
        _verifier = new OutputVerifier();
    }

    public async Task<LlmTaskRunResults> RunAllAsync(
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LlmTaskRunnerOptions();
        _budgetLimit = options.BudgetLimit;

        var results = new LlmTaskRunResults
        {
            Provider = _provider.Name,
            IsDryRun = options.DryRun
        };

        // Filter and sample tasks
        var tasks = FilterTasks(manifest.Tasks, options);

        if (options.Verbose)
        {
            Console.WriteLine($"Running {tasks.Count} tasks with provider '{_provider.Name}'");
            Console.WriteLine($"Budget: ${options.BudgetLimit:F2}");
        }

        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check budget
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
                var winner = taskResult.CalorWins ? "Calor" : taskResult.CSharpWins ? "C#" : "Tie";
                Console.WriteLine($"    Result: Calor={taskResult.CalorResult.Score:F2}, C#={taskResult.CSharpResult.Score:F2} ({winner})");
            }
        }

        // Calculate summary
        results = results with { Summary = CalculateSummary(results.Results) };

        return results;
    }

    public async Task<LlmTaskResult> RunTaskAsync(
        LlmTaskDefinition task,
        LlmTaskRunnerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LlmTaskRunnerOptions();

        // Generate code for both languages using language-appropriate prompts
        var calorResult = await GenerateAndEvaluateAsync(
            task, "calor", task.GetPrompt("calor"), options, cancellationToken);

        var csharpResult = await GenerateAndEvaluateAsync(
            task, "csharp", task.GetPrompt("csharp"), options, cancellationToken);

        return new LlmTaskResult
        {
            Task = task,
            CalorResult = calorResult,
            CSharpResult = csharpResult
        };
    }

    public LlmTaskCostEstimate EstimateCost(
        LlmTaskManifest manifest,
        LlmTaskRunnerOptions? options = null)
    {
        options ??= new LlmTaskRunnerOptions();
        var tasks = FilterTasks(manifest.Tasks, options);

        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var cachedCount = 0;
        var byCategory = new Dictionary<string, decimal>();

        foreach (var task in tasks)
        {
            // Estimate tokens for both prompts
            var calorPrompt = task.GetPrompt("calor");
            var csharpPrompt = task.GetPrompt("csharp");
            var calorInputTokens = _provider.EstimateTokenCount(calorPrompt);
            var csharpInputTokens = _provider.EstimateTokenCount(csharpPrompt);

            // Estimate output (assume ~500 tokens per generation)
            var estimatedOutputTokens = 500;

            // Check cache
            var calorCached = options.UseCache && !options.RefreshCache &&
                _cache.Contains(_provider.Name, calorPrompt, LlmGenerationOptions.Default);
            var csharpCached = options.UseCache && !options.RefreshCache &&
                _cache.Contains(_provider.Name, csharpPrompt, LlmGenerationOptions.Default);

            if (!calorCached)
            {
                totalInputTokens += calorInputTokens;
                totalOutputTokens += estimatedOutputTokens;
            }
            else
            {
                cachedCount++;
            }

            if (!csharpCached)
            {
                totalInputTokens += csharpInputTokens;
                totalOutputTokens += estimatedOutputTokens;
            }
            else
            {
                cachedCount++;
            }

            // Calculate per-category cost
            var taskCost = _provider.EstimateCost(
                calorInputTokens + csharpInputTokens,
                estimatedOutputTokens * 2);

            if (!byCategory.ContainsKey(task.Category))
                byCategory[task.Category] = 0;
            byCategory[task.Category] += taskCost;
        }

        var totalCost = _provider.EstimateCost(totalInputTokens, totalOutputTokens);
        var costWithCache = totalCost; // Already accounts for cache

        return new LlmTaskCostEstimate
        {
            TaskCount = tasks.Count,
            EstimatedInputTokens = totalInputTokens,
            EstimatedOutputTokens = totalOutputTokens,
            EstimatedCost = totalCost,
            CachedResponses = cachedCount,
            CostWithCache = costWithCache,
            ExceedsBudget = totalCost > options.BudgetLimit,
            BudgetLimit = options.BudgetLimit,
            ByCategory = byCategory
        };
    }

    private async Task<LlmTaskLanguageResult> GenerateAndEvaluateAsync(
        LlmTaskDefinition task,
        string language,
        string prompt,
        LlmTaskRunnerOptions options,
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
            // Dry run - don't actually call API
            genResult = new LlmGenerationResult
            {
                Success = true,
                GeneratedCode = $"// Dry run - no code generated for {language}",
                Provider = _provider.Name,
                Model = _provider.DefaultModel,
                InputTokens = _provider.EstimateTokenCount(prompt),
                OutputTokens = 100,
                Cost = _provider.EstimateCost(
                    _provider.EstimateTokenCount(prompt), 100),
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
                genResult = await GenerateWithRetry(prompt, language, genOptions, options, cancellationToken);
                if (genResult.Success)
                {
                    await _cache.SetAsync(_provider.Name, prompt, genOptions, genResult);
                }
            }
        }
        else
        {
            genResult = await GenerateWithRetry(prompt, language, genOptions, options, cancellationToken);
            if (genResult.Success && options.UseCache)
            {
                await _cache.SetAsync(_provider.Name, prompt, genOptions, genResult);
            }
        }

        // Track spend
        _currentSpend += genResult.Cost;

        if (!genResult.Success)
        {
            return new LlmTaskLanguageResult
            {
                Language = language,
                GeneratedCode = "",
                CompilationSuccess = false,
                CompilationErrors = new List<string> { genResult.Error ?? "Generation failed" },
                GenerationMetadata = CreateMetadata(genResult),
                Score = 0
            };
        }

        // Compile and execute
        return await EvaluateGeneratedCode(task, language, genResult, options);
    }

    private async Task<LlmGenerationResult> GenerateWithRetry(
        string prompt,
        string language,
        LlmGenerationOptions options,
        LlmTaskRunnerOptions runOptions,
        CancellationToken cancellationToken)
    {
        var attempts = runOptions.RetryAttempts + 1;
        LlmGenerationResult? lastResult = null;

        for (var i = 0; i < attempts; i++)
        {
            if (i > 0)
            {
                await Task.Delay(runOptions.RetryDelayMs, cancellationToken);
            }

            lastResult = await _provider.GenerateCodeAsync(prompt, language, options, cancellationToken);

            if (lastResult.Success)
                return lastResult;
        }

        return lastResult ?? LlmGenerationResult.Failed(_provider.Name, "No attempts made");
    }

    private async Task<LlmTaskLanguageResult> EvaluateGeneratedCode(
        LlmTaskDefinition task,
        string language,
        LlmGenerationResult genResult,
        LlmTaskRunnerOptions options)
    {
        var result = new LlmTaskLanguageResult
        {
            Language = language,
            GeneratedCode = genResult.GeneratedCode,
            GenerationMetadata = CreateMetadata(genResult)
        };

        // Compile
        List<string> compilationErrors;
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
                    Score = 0
                };
            }

            var csharpResult = _executor.CompileCSharp(calorResult.GeneratedCSharp!);
            if (!csharpResult.Success)
            {
                return result with
                {
                    CompilationSuccess = false,
                    CompilationErrors = csharpResult.Errors,
                    Score = task.Scoring.Compilation * 0.5 // Partial credit for Calor compilation
                };
            }

            assemblyBytes = csharpResult.AssemblyBytes;
            compilationErrors = new List<string>();
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
                    Score = 0
                };
            }

            assemblyBytes = csharpResult.AssemblyBytes;
            compilationErrors = new List<string>();
        }

        result = result with
        {
            CompilationSuccess = true,
            CompilationErrors = compilationErrors
        };

        // Execute test cases
        var testResults = new List<TestCaseResult>();
        var methodName = task.ExpectedSignature?.FunctionName ?? "compute";

        foreach (var (testCase, index) in task.TestCases.Select((tc, i) => (tc, i)))
        {
            var testResult = await ExecuteTestCase(
                assemblyBytes!, methodName, testCase, index, options);
            testResults.Add(testResult);
        }

        result = result with { TestResults = testResults };

        // For Calor, analyze contracts
        if (language.Equals("calor", StringComparison.OrdinalIgnoreCase))
        {
            var contractResults = AnalyzeContracts(genResult.GeneratedCode);
            result = result with { ContractResults = contractResults };
        }

        // Calculate score
        var score = CalculateScore(task, result);

        return result with { Score = score };
    }

    /// <summary>
    /// Analyzes contracts in Calor source code.
    /// Counts preconditions (§Q) and postconditions (§S) to give credit for contract usage.
    /// </summary>
    private static ContractVerificationResults AnalyzeContracts(string calorSource)
    {
        // Count contracts using simple pattern matching
        // §Q is precondition (requires), §S is postcondition (ensures)
        var preconditionCount = System.Text.RegularExpressions.Regex.Matches(
            calorSource, @"§Q\s*\(").Count;
        var postconditionCount = System.Text.RegularExpressions.Regex.Matches(
            calorSource, @"§S\s*\(").Count;

        var totalContracts = preconditionCount + postconditionCount;

        if (totalContracts == 0)
        {
            // No contracts found - return zero score
            return new ContractVerificationResults
            {
                Proven = 0,
                Disproven = 0,
                Unproven = 0
            };
        }

        // Since we're not doing full Z3 verification, we give credit for having contracts
        // Treat all contracts as "unproven" (neither proven nor disproven)
        // This gives 0.5 score per contract (see ContractVerificationResults.Score)
        return new ContractVerificationResults
        {
            Proven = 0,
            Disproven = 0,
            Unproven = totalContracts
        };
    }

    private Task<TestCaseResult> ExecuteTestCase(
        byte[] assemblyBytes,
        string methodName,
        TaskTestCase testCase,
        int index,
        LlmTaskRunnerOptions options)
    {
        try
        {
            // Convert JsonElement inputs to actual values
            var arguments = testCase.Input
                .Select(ConvertJsonElement)
                .ToArray();

            var execResult = _executor.Execute(assemblyBytes, methodName, arguments);

            if (execResult.TimedOut)
            {
                return Task.FromResult(new TestCaseResult
                {
                    Index = index,
                    Passed = false,
                    ErrorMessage = "Execution timed out",
                    TimedOut = true,
                    ExecutionTimeMs = execResult.DurationMs
                });
            }

            if (execResult.ContractViolation)
            {
                var expectedViolation = testCase.ExpectsContractViolation;
                return Task.FromResult(new TestCaseResult
                {
                    Index = index,
                    Passed = expectedViolation,
                    ErrorMessage = expectedViolation ? null : execResult.Error,
                    ContractViolation = true,
                    ExecutionTimeMs = execResult.DurationMs
                });
            }

            if (!execResult.Success)
            {
                return Task.FromResult(new TestCaseResult
                {
                    Index = index,
                    Passed = false,
                    ErrorMessage = execResult.Error,
                    ExecutionTimeMs = execResult.DurationMs
                });
            }

            // Verify output
            if (!testCase.Expected.HasValue)
            {
                // No expected value means test passes if execution succeeded
                return Task.FromResult(new TestCaseResult
                {
                    Index = index,
                    Passed = execResult.Success,
                    ErrorMessage = execResult.Success ? null : "No expected value and execution failed",
                    ExecutionTimeMs = execResult.DurationMs
                });
            }
            var verification = _verifier.Verify(execResult, testCase.Expected.Value);

            return Task.FromResult(new TestCaseResult
            {
                Index = index,
                Passed = verification.Passed,
                ActualOutput = verification.ActualValue,
                ExpectedOutput = verification.ExpectedValue,
                ErrorMessage = verification.Reason,
                ExecutionTimeMs = execResult.DurationMs
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new TestCaseResult
            {
                Index = index,
                Passed = false,
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

    private static double CalculateScore(LlmTaskDefinition task, LlmTaskLanguageResult result)
    {
        var score = 0.0;

        // Compilation score
        if (result.CompilationSuccess)
        {
            score += task.Scoring.Compilation;
        }

        // Test case score
        if (result.TotalTests > 0)
        {
            score += task.Scoring.TestCases * result.TestPassRate;
        }

        // Contract verification score (for Calor)
        if (result.ContractResults != null)
        {
            score += task.Scoring.Contracts * result.ContractResults.Score;
        }
        else if (result.Language.Equals("calor", StringComparison.OrdinalIgnoreCase))
        {
            // Give partial credit if Calor compiled but no explicit contract verification
            score += task.Scoring.Contracts * (result.CompilationSuccess ? 0.5 : 0);
        }

        return Math.Min(score, 1.0);
    }

    private static LlmGenerationMetadata CreateMetadata(LlmGenerationResult result)
    {
        return new LlmGenerationMetadata
        {
            Provider = result.Provider,
            Model = result.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            Cost = result.Cost,
            DurationMs = result.DurationMs,
            FromCache = result.FromCache
        };
    }

    private static string? _calorSkillsContent;
    private static string? _csharpSkillsContent;

    private static string? GetSkillsContent(string language)
    {
        var cacheRef = language == "calor" ? ref _calorSkillsContent : ref _csharpSkillsContent;

        if (cacheRef != null)
            return cacheRef;

        var fileName = language == "calor" ? "calor-language-skills.md" : "csharp-language-skills.md";

        // Try to load from various locations
        var possiblePaths = new[]
        {
            // Evaluation skills directory (new location)
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Skills", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "tests", "Calor.Evaluation", "Skills", fileName),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tests", "Calor.Evaluation", "Skills", fileName)),
            // Compiler skills directory (legacy location for Calor)
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "Calor.Compiler", "Resources", "Skills", "claude-calor-SKILL.md"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Calor.Compiler", "Resources", "Skills", "claude-calor-SKILL.md"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                cacheRef = File.ReadAllText(path);
                return cacheRef;
            }
        }

        // Fallback to basic prompt if skills file not found
        return null;
    }

    private static string? GetCalorSkillsContent() => GetSkillsContent("calor");
    private static string? GetCSharpSkillsContent() => GetSkillsContent("csharp");

    private static string GetSystemPrompt(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "calor" => GetCalorSystemPrompt(),
            "csharp" or "c#" => GetCSharpSystemPrompt(),
            _ => "You are an expert programmer. Generate only valid source code without any explanation."
        };
    }

    /// <summary>
    /// Gets the system prompt for a specific language (public for use by SafetyBenchmarkRunner).
    /// </summary>
    public static string GetSystemPromptForLanguage(string language) => GetSystemPrompt(language);

    private static string GetCSharpSystemPrompt()
    {
        var skillsContent = GetCSharpSkillsContent();

        if (skillsContent != null)
        {
            return $@"You are an expert C# programmer. Generate only valid C# code without any explanation.

{skillsContent}

IMPORTANT INSTRUCTIONS:
1. Output ONLY the C# code, no markdown code blocks, no explanation
2. Create a public static class named 'Functions' with the method
3. Use appropriate guard clauses/exceptions for input validation
4. Follow standard C# naming conventions";
        }

        // Fallback to basic prompt
        return @"You are an expert C# programmer. Generate only valid C# code without any explanation.
Create a public static class with a public static method. Output only the code, no markdown.";
    }

    private static string GetCalorSystemPrompt()
    {
        var skillsContent = GetCalorSkillsContent();

        if (skillsContent != null)
        {
            // Use the comprehensive skills file with instructions
            return $@"You are an expert Calor programmer. Generate only valid Calor code without any explanation.

{skillsContent}

IMPORTANT INSTRUCTIONS:
1. Output ONLY the Calor code, no markdown code blocks, no explanation
2. Always include the module wrapper §M{{id:ModuleName}} ... §/M{{id}}
3. Use test-style IDs like m001, f001, if1
4. For boolean logic, use (&&) not (and), (||) not (or), (!) not (not)
5. For negation of numbers, use (- 0 n) not (- n)";
        }

        // Fallback to basic prompt
        return @"You are an expert Calor programmer. Generate only valid Calor code without any explanation.

Calor syntax uses section markers with IDs:
- §M{id:ModuleName} starts a module, §/M{id} ends it
- §F{id:FunctionName:pub} starts a public function, §/F{id} ends it
- §I{type:paramName} declares an input parameter (types: i32, bool, string, void)
- §O{type} declares the output/return type
- §Q (condition) is a precondition (REQUIRES) using S-expression syntax
- §S (condition) is a postcondition (ENSURES) using S-expression syntax
- §R expression returns a value
- §IF{id} (cond) → body with §EI, §EL, and §/I{id} for conditionals
- §L{id:var:start:end:step} for loops, §/L{id} to end

Math uses S-expressions: (+ a b), (* a b), (- a b), (/ a b), (% a b)
Comparisons: (> a b), (< a b), (>= a b), (<= a b), (== a b), (!= a b)
Logic: (&& a b), (|| a b), (! a)

Example:
§M{m001:Math}
§F{f001:Factorial:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n 0)
  §S (>= result 1)
  §IF{if1} (<= n 1) → §R 1
  §EL → §R (* n (Factorial (- n 1)))
  §/I{if1}
§/F{f001}
§/M{m001}

Output only the code, no markdown or explanation.";
    }

    private static List<LlmTaskDefinition> FilterTasks(
        List<LlmTaskDefinition> tasks,
        LlmTaskRunnerOptions options)
    {
        var filtered = tasks.AsEnumerable();

        // Filter by task IDs
        if (options.TaskFilter != null && options.TaskFilter.Count > 0)
        {
            var filterSet = options.TaskFilter.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(t => filterSet.Contains(t.Id));
        }

        // Filter by category
        if (!string.IsNullOrEmpty(options.CategoryFilter))
        {
            filtered = filtered.Where(t =>
                t.Category.Equals(options.CategoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by difficulty
        filtered = filtered.Where(t => t.Difficulty <= options.MaxDifficulty);

        var result = filtered.ToList();

        // Sample if requested
        if (options.SampleSize.HasValue && options.SampleSize.Value < result.Count)
        {
            var random = options.SampleSeed.HasValue
                ? new Random(options.SampleSeed.Value)
                : new Random();

            result = result
                .OrderBy(_ => random.Next())
                .Take(options.SampleSize.Value)
                .ToList();
        }

        return result;
    }

    private static LlmTaskRunSummary CalculateSummary(List<LlmTaskResult> results)
    {
        if (results.Count == 0)
        {
            return new LlmTaskRunSummary();
        }

        var calorWins = results.Count(r => r.CalorWins);
        var csharpWins = results.Count(r => r.CSharpWins);
        var ties = results.Count(r => r.Tie);

        var avgCalorScore = results.Average(r => r.CalorResult.Score);
        var avgCsharpScore = results.Average(r => r.CSharpResult.Score);

        var calorCompileRate = results.Count(r => r.CalorResult.CompilationSuccess) / (double)results.Count;
        var csharpCompileRate = results.Count(r => r.CSharpResult.CompilationSuccess) / (double)results.Count;

        var calorTestPassRate = results
            .Where(r => r.CalorResult.TotalTests > 0)
            .Select(r => r.CalorResult.TestPassRate)
            .DefaultIfEmpty(0)
            .Average();

        var csharpTestPassRate = results
            .Where(r => r.CSharpResult.TotalTests > 0)
            .Select(r => r.CSharpResult.TestPassRate)
            .DefaultIfEmpty(0)
            .Average();

        // Calculate by category
        var byCategory = results
            .GroupBy(r => r.Task.Category)
            .ToDictionary(
                g => g.Key,
                g => new CategorySummary
                {
                    Category = g.Key,
                    TaskCount = g.Count(),
                    AverageCalorScore = g.Average(r => r.CalorResult.Score),
                    AverageCSharpScore = g.Average(r => r.CSharpResult.Score),
                    AdvantageRatio = g.Average(r => r.CSharpResult.Score) > 0
                        ? g.Average(r => r.CalorResult.Score) / g.Average(r => r.CSharpResult.Score)
                        : 1.0
                });

        return new LlmTaskRunSummary
        {
            TotalTasks = results.Count,
            CalorWins = calorWins,
            CSharpWins = csharpWins,
            Ties = ties,
            AverageCalorScore = avgCalorScore,
            AverageCSharpScore = avgCsharpScore,
            OverallAdvantageRatio = avgCsharpScore > 0 ? avgCalorScore / avgCsharpScore : 1.0,
            CalorCompilationRate = calorCompileRate,
            CSharpCompilationRate = csharpCompileRate,
            CalorTestPassRate = calorTestPassRate,
            CSharpTestPassRate = csharpTestPassRate,
            ByCategory = byCategory
        };
    }

    public void Dispose()
    {
        _executor.Dispose();
    }
}
