using System.Text.Json;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Execution;
using Calor.Evaluation.LlmTasks.Providers;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Tests for the LLM task completion infrastructure.
/// </summary>
public class LlmTaskTests
{
    #region MockProvider Tests

    [Fact]
    public void MockProvider_IsAvailable_ReturnsTrue()
    {
        var provider = new MockProvider();
        Assert.True(provider.IsAvailable);
        Assert.Null(provider.UnavailabilityReason);
    }

    [Fact]
    public void MockProvider_Name_ReturnsMock()
    {
        var provider = new MockProvider();
        Assert.Equal("mock", provider.Name);
    }

    [Fact]
    public async Task MockProvider_GenerateFactorial_ReturnsCalorCode()
    {
        var provider = new MockProvider();
        var result = await provider.GenerateCodeAsync(
            "Write a factorial function", "calor");

        Assert.True(result.Success);
        Assert.Contains("§M{", result.GeneratedCode);
        Assert.Contains("§F{", result.GeneratedCode);
        Assert.Contains("Factorial", result.GeneratedCode);
    }

    [Fact]
    public async Task MockProvider_GenerateFactorial_ReturnsCSharpCode()
    {
        var provider = new MockProvider();
        var result = await provider.GenerateCodeAsync(
            "Write a factorial function", "csharp");

        Assert.True(result.Success);
        Assert.Contains("public static", result.GeneratedCode);
        Assert.Contains("Factorial", result.GeneratedCode);
    }

    [Fact]
    public async Task MockProvider_TracksCallCount()
    {
        var provider = new MockProvider();
        Assert.Equal(0, provider.CallCount);

        await provider.GenerateCodeAsync("test", "calor");
        Assert.Equal(1, provider.CallCount);

        await provider.GenerateCodeAsync("test", "csharp");
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task MockProvider_RecordsPrompts()
    {
        var provider = new MockProvider();

        await provider.GenerateCodeAsync("prompt1", "calor");
        await provider.GenerateCodeAsync("prompt2", "csharp");

        Assert.Equal(2, provider.ReceivedPrompts.Count);
        Assert.Equal(("prompt1", "calor"), provider.ReceivedPrompts[0]);
        Assert.Equal(("prompt2", "csharp"), provider.ReceivedPrompts[1]);
    }

    [Fact]
    public void MockProvider_EstimateCost_ReturnsZero()
    {
        var provider = new MockProvider();
        Assert.Equal(0m, provider.EstimateCost(1000, 500));
    }

    [Fact]
    public void MockProvider_EstimateTokenCount_ApproximatesFourCharsPerToken()
    {
        var provider = new MockProvider();
        // 20 characters / 4 = 5 tokens
        Assert.Equal(5, provider.EstimateTokenCount("12345678901234567890"));
    }

    [Fact]
    public async Task MockProvider_WithCustomGenerator_UsesGenerator()
    {
        var provider = new MockProvider((prompt, lang) => $"Custom: {lang}");

        var result = await provider.GenerateCodeAsync("anything", "test-lang");

        Assert.True(result.Success);
        Assert.Equal("Custom: test-lang", result.GeneratedCode);
    }

    [Fact]
    public async Task MockProvider_WithImplementation_MatchesKeyword()
    {
        var provider = new MockProvider()
            .WithImplementation("special", "calor-code", "csharp-code");

        var calorResult = await provider.GenerateCodeAsync(
            "Write a special function", "calor");
        var csharpResult = await provider.GenerateCodeAsync(
            "Write a special function", "csharp");

        Assert.Equal("calor-code", calorResult.GeneratedCode);
        Assert.Equal("csharp-code", csharpResult.GeneratedCode);
    }

    #endregion

    #region CodeExecutor Tests

    [Fact]
    public void CodeExecutor_CompileCSharp_SimpleClass_Succeeds()
    {
        using var executor = new CodeExecutor();
        var result = executor.CompileCSharp(@"
public static class Test
{
    public static int Add(int a, int b) => a + b;
}");

        Assert.True(result.Success);
        Assert.NotNull(result.AssemblyBytes);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CodeExecutor_CompileCSharp_InvalidCode_ReturnsErrors()
    {
        using var executor = new CodeExecutor();
        var result = executor.CompileCSharp("this is not valid C#");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void CodeExecutor_Execute_SimpleMethod_ReturnsCorrectValue()
    {
        using var executor = new CodeExecutor();
        var compileResult = executor.CompileCSharp(@"
public static class Math
{
    public static int Add(int a, int b) => a + b;
}");

        var execResult = executor.Execute(
            compileResult.AssemblyBytes!,
            "Add",
            new object[] { 3, 4 });

        Assert.True(execResult.Success);
        Assert.Equal(7, execResult.ReturnValue);
    }

    [Fact]
    public void CodeExecutor_Execute_MethodNotFound_ReturnsError()
    {
        using var executor = new CodeExecutor();
        var compileResult = executor.CompileCSharp(@"
public static class Test
{
    public static int Foo() => 42;
}");

        var execResult = executor.Execute(
            compileResult.AssemblyBytes!,
            "NonExistentMethod");

        Assert.False(execResult.Success);
        Assert.Contains("not found", execResult.Error);
    }

    [Fact]
    public void CodeExecutor_Execute_ThrowsException_CapturesError()
    {
        using var executor = new CodeExecutor();
        var compileResult = executor.CompileCSharp(@"
public static class Test
{
    public static int Throw() => throw new System.InvalidOperationException(""Test error"");
}");

        var execResult = executor.Execute(compileResult.AssemblyBytes!, "Throw");

        Assert.False(execResult.Success);
        Assert.Contains("Test error", execResult.Error);
        Assert.Equal("InvalidOperationException", execResult.ExceptionType);
    }

    [Fact]
    public void CodeExecutor_CompileCalor_SimpleFunction_Succeeds()
    {
        using var executor = new CodeExecutor();
        var result = executor.CompileCalor(@"§M{m001:Math}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}");

        Assert.True(result.Success);
        Assert.NotNull(result.GeneratedCSharp);
        Assert.Contains("public static int Add", result.GeneratedCSharp);
    }

    [Fact]
    public void CodeExecutor_CompileCalor_InvalidSyntax_ReturnsErrors()
    {
        using var executor = new CodeExecutor();
        var result = executor.CompileCalor("this is not valid Calor");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void CodeExecutor_CompileAndExecuteCalor_Works()
    {
        using var executor = new CodeExecutor();
        var result = executor.CompileAndExecuteCalor(@"§M{m001:Math}
§F{f001:Multiply:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (* a b)
§/F{f001}
§/M{m001}",
            "Multiply",
            new object[] { 6, 7 });

        Assert.True(result.Success);
        Assert.Equal(42, result.ReturnValue);
    }

    [Fact]
    public void CodeExecutor_CompileAndExecuteCSharp_Works()
    {
        using var executor = new CodeExecutor();
        var result = executor.CompileAndExecuteCSharp(@"
public static class Math
{
    public static int Square(int x) => x * x;
}",
            "Square",
            new object[] { 5 });

        Assert.True(result.Success);
        Assert.Equal(25, result.ReturnValue);
    }

    #endregion

    #region OutputVerifier Tests

    [Fact]
    public void OutputVerifier_VerifyValue_MatchingInt_ReturnsTrue()
    {
        var verifier = new OutputVerifier();
        var expected = JsonDocument.Parse("42").RootElement;

        var result = verifier.VerifyValue(42, expected);
        Assert.True(result.Passed);
    }

    [Fact]
    public void OutputVerifier_VerifyValue_MismatchedInt_ReturnsFalse()
    {
        var verifier = new OutputVerifier();
        var expected = JsonDocument.Parse("42").RootElement;

        var result = verifier.VerifyValue(41, expected);
        Assert.False(result.Passed);
    }

    [Fact]
    public void OutputVerifier_VerifyValue_MatchingBool_ReturnsTrue()
    {
        var verifier = new OutputVerifier();

        Assert.True(verifier.VerifyValue(true, JsonDocument.Parse("true").RootElement).Passed);
        Assert.True(verifier.VerifyValue(false, JsonDocument.Parse("false").RootElement).Passed);
    }

    [Fact]
    public void OutputVerifier_VerifyValue_MatchingString_ReturnsTrue()
    {
        var verifier = new OutputVerifier();
        var expected = JsonDocument.Parse("\"hello\"").RootElement;

        var result = verifier.VerifyValue("hello", expected);
        Assert.True(result.Passed);
    }

    [Fact]
    public void OutputVerifier_VerifyValue_NullValues_ReturnsTrue()
    {
        var verifier = new OutputVerifier();
        var expected = JsonDocument.Parse("null").RootElement;

        var result = verifier.VerifyValue(null, expected);
        Assert.True(result.Passed);
    }

    [Fact]
    public void OutputVerifier_Verify_WithExecutionResult_Works()
    {
        var verifier = new OutputVerifier();
        var execResult = new ExecutionResult
        {
            Success = true,
            ReturnValue = 42
        };
        var expected = JsonDocument.Parse("42").RootElement;

        var result = verifier.Verify(execResult, expected);
        Assert.True(result.Passed);
    }

    [Fact]
    public void OutputVerifier_Verify_FailedExecution_ReturnsFalse()
    {
        var verifier = new OutputVerifier();
        var execResult = new ExecutionResult
        {
            Success = false,
            Error = "Execution failed"
        };
        var expected = JsonDocument.Parse("42").RootElement;

        var result = verifier.Verify(execResult, expected);
        Assert.False(result.Passed);
        Assert.Contains("Execution failed", result.Reason);
    }

    #endregion

    #region LlmTaskDefinition Tests

    [Fact]
    public void LlmTaskManifest_LoadFromJson_ParsesCorrectly()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""description"": ""Test manifest"",
            ""tasks"": [
                {
                    ""id"": ""test-001"",
                    ""name"": ""Test Task"",
                    ""category"": ""test"",
                    ""difficulty"": 1,
                    ""description"": ""A test task"",
                    ""prompts"": {
                        ""calor"": ""Write Calor code"",
                        ""csharp"": ""Write C# code""
                    },
                    ""testCases"": [
                        { ""input"": [1, 2], ""expected"": 3 }
                    ],
                    ""scoring"": {
                        ""compilation"": 0.3,
                        ""testCases"": 0.5,
                        ""contracts"": 0.2
                    },
                    ""expectedSignature"": {
                        ""functionName"": ""Add"",
                        ""parameterTypes"": [""int"", ""int""],
                        ""returnType"": ""int""
                    },
                    ""tags"": [""math""]
                }
            ]
        }";

        var manifest = JsonSerializer.Deserialize<LlmTaskManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Equal("1.0", manifest.Version);
        Assert.Single(manifest.Tasks);

        var task = manifest.Tasks[0];
        Assert.Equal("test-001", task.Id);
        Assert.Equal("Test Task", task.Name);
        Assert.Equal("test", task.Category);
        Assert.Equal(1, task.Difficulty);
        Assert.Equal("Write Calor code", task.Prompts!.Calor);
        Assert.Equal("Write C# code", task.Prompts.CSharp);
        Assert.Single(task.TestCases);
        Assert.Equal(0.3, task.Scoring.Compilation);
        Assert.Equal("Add", task.ExpectedSignature?.FunctionName);
        Assert.Contains("math", task.Tags);
    }

    [Fact]
    public void TaskScoring_IsValid_ReturnsTrueForValidWeights()
    {
        var scoring = new TaskScoring
        {
            Compilation = 0.3,
            TestCases = 0.5,
            Contracts = 0.2
        };

        Assert.True(scoring.IsValid);
    }

    [Fact]
    public void TaskScoring_IsValid_ReturnsFalseForInvalidWeights()
    {
        var scoring = new TaskScoring
        {
            Compilation = 0.5,
            TestCases = 0.5,
            Contracts = 0.5
        };

        Assert.False(scoring.IsValid);
    }

    #endregion

    #region LlmTaskRunner Tests

    [Fact]
    public async Task LlmTaskRunner_RunTask_ReturnsResults()
    {
        var provider = new MockProvider();
        var runner = new LlmTaskRunner(provider);

        var task = new LlmTaskDefinition
        {
            Id = "test-001",
            Name = "Sum",
            Category = "test",
            Difficulty = 1,
            Description = "Add two numbers",
            Prompts = new TaskPrompts
            {
                Calor = "Write a Calor sum function",
                CSharp = "Write a C# sum function"
            },
            TestCases = new List<TaskTestCase>
            {
                new()
                {
                    Input = new[]
                    {
                        JsonDocument.Parse("1").RootElement,
                        JsonDocument.Parse("2").RootElement
                    },
                    Expected = JsonDocument.Parse("3").RootElement
                }
            },
            Scoring = new TaskScoring { Compilation = 0.3, TestCases = 0.5, Contracts = 0.2 },
            ExpectedSignature = new TaskSignature
            {
                FunctionName = "Sum",
                ParameterTypes = new List<string> { "int", "int" },
                ReturnType = "int"
            }
        };

        var result = await runner.RunTaskAsync(task);

        Assert.NotNull(result);
        Assert.NotNull(result.CalorResult);
        Assert.NotNull(result.CSharpResult);
        Assert.Equal("calor", result.CalorResult.Language);
        Assert.Equal("csharp", result.CSharpResult.Language);
    }

    [Fact]
    public async Task LlmTaskRunner_DryRun_DoesNotCompile()
    {
        var provider = new MockProvider();
        var runner = new LlmTaskRunner(provider);

        var task = new LlmTaskDefinition
        {
            Id = "test-001",
            Name = "Test",
            Category = "test",
            Difficulty = 1,
            Description = "Test",
            Prompts = new TaskPrompts { Calor = "test", CSharp = "test" },
            TestCases = new List<TaskTestCase>(),
            Scoring = new TaskScoring { Compilation = 0.3, TestCases = 0.5, Contracts = 0.2 }
        };

        var options = new LlmTaskRunnerOptions { DryRun = true };
        var result = await runner.RunTaskAsync(task, options);

        // Dry run returns stub code, won't actually compile
        Assert.Contains("Dry run", result.CalorResult.GeneratedCode);
        Assert.Contains("Dry run", result.CSharpResult.GeneratedCode);
    }

    [Fact]
    public void LlmTaskRunner_EstimateCost_ReturnsEstimate()
    {
        var provider = new MockProvider();
        var runner = new LlmTaskRunner(provider);

        var manifest = new LlmTaskManifest
        {
            Version = "1.0",
            Description = "Test",
            Tasks = new List<LlmTaskDefinition>
            {
                new()
                {
                    Id = "test-001",
                    Name = "Test",
                    Category = "test",
                    Difficulty = 1,
                    Description = "Test",
                    Prompts = new TaskPrompts { Calor = "Write code", CSharp = "Write code" },
                    TestCases = new List<TaskTestCase>(),
                    Scoring = new TaskScoring()
                }
            }
        };

        var estimate = runner.EstimateCost(manifest);

        Assert.NotNull(estimate);
        Assert.Equal(1, estimate.TaskCount);
        // Mock provider returns 0 cost
        Assert.Equal(0m, estimate.EstimatedCost);
    }

    #endregion

    #region LlmResponseCache Tests

    [Fact]
    public async Task LlmResponseCache_SetAndGet_RoundTrips()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"test-cache-{Guid.NewGuid():N}");
        try
        {
            var cache = new LlmResponseCache(cacheDir);

            var result = new LlmGenerationResult
            {
                Success = true,
                GeneratedCode = "test code",
                Provider = "test",
                Model = "test-model",
                InputTokens = 100,
                OutputTokens = 50,
                Cost = 0.01m
            };

            await cache.SetAsync("provider", "prompt", LlmGenerationOptions.Default, result);
            var retrieved = await cache.GetAsync("provider", "prompt", LlmGenerationOptions.Default);

            Assert.NotNull(retrieved);
            Assert.Equal("test code", retrieved.GeneratedCode);
            Assert.True(retrieved.FromCache);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        }
    }

    [Fact]
    public async Task LlmResponseCache_GetMissing_ReturnsNull()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"test-cache-{Guid.NewGuid():N}");
        try
        {
            var cache = new LlmResponseCache(cacheDir);
            var result = await cache.GetAsync("provider", "nonexistent", LlmGenerationOptions.Default);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        }
    }

    [Fact]
    public async Task LlmResponseCache_Contains_ReturnsTrueForCached()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"test-cache-{Guid.NewGuid():N}");
        try
        {
            var cache = new LlmResponseCache(cacheDir);

            var result = new LlmGenerationResult
            {
                Success = true,
                GeneratedCode = "code",
                Provider = "test",
                Model = "model"
            };

            await cache.SetAsync("provider", "prompt", LlmGenerationOptions.Default, result);

            Assert.True(cache.Contains("provider", "prompt", LlmGenerationOptions.Default));
            Assert.False(cache.Contains("provider", "other", LlmGenerationOptions.Default));
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        }
    }

    [Fact]
    public async Task LlmResponseCache_GetStatistics_ReturnsCorrectCounts()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"test-cache-{Guid.NewGuid():N}");
        try
        {
            var cache = new LlmResponseCache(cacheDir);

            var result = new LlmGenerationResult
            {
                Success = true,
                GeneratedCode = "code",
                Provider = "test",
                Model = "model"
            };

            await cache.SetAsync("p1", "prompt1", LlmGenerationOptions.Default, result);
            await cache.SetAsync("p1", "prompt2", LlmGenerationOptions.Default, result);

            var stats = cache.GetStatistics();

            Assert.Equal(2, stats.EntryCount);
            Assert.True(stats.TotalSizeBytes > 0);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        }
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_FullTaskRunWithMockProvider_ProducesValidResults()
    {
        var provider = new MockProvider();
        var runner = new LlmTaskRunner(provider);

        // Create a task that matches MockProvider's built-in implementations
        var task = new LlmTaskDefinition
        {
            Id = "int-001",
            Name = "Sum",
            Category = "math",
            Difficulty = 1,
            Description = "Sum two numbers",
            Prompts = new TaskPrompts
            {
                Calor = "Write a Calor function named Sum that returns the sum of two integers",
                CSharp = "Write a C# method named Sum that returns the sum of two integers"
            },
            TestCases = new List<TaskTestCase>
            {
                new()
                {
                    Input = new[]
                    {
                        JsonDocument.Parse("1").RootElement,
                        JsonDocument.Parse("2").RootElement
                    },
                    Expected = JsonDocument.Parse("3").RootElement
                },
                new()
                {
                    Input = new[]
                    {
                        JsonDocument.Parse("10").RootElement,
                        JsonDocument.Parse("20").RootElement
                    },
                    Expected = JsonDocument.Parse("30").RootElement
                }
            },
            Scoring = new TaskScoring
            {
                Compilation = 0.3,
                TestCases = 0.5,
                Contracts = 0.2
            },
            ExpectedSignature = new TaskSignature
            {
                FunctionName = "Sum",
                ParameterTypes = new List<string> { "int", "int" },
                ReturnType = "int"
            }
        };

        var result = await runner.RunTaskAsync(task);

        // Both should compile
        Assert.True(result.CalorResult.CompilationSuccess);
        Assert.True(result.CSharpResult.CompilationSuccess);

        // Both should pass tests
        Assert.True(result.CalorResult.TestResults.All(r => r.Passed));
        Assert.True(result.CSharpResult.TestResults.All(r => r.Passed));

        // Scores should be positive
        Assert.True(result.CalorResult.Score > 0);
        Assert.True(result.CSharpResult.Score > 0);
    }

    #endregion
}
