using Calor.Evaluation.Core;
using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Tests for the proportional counting and contract-depth scoring in ComprehensionCalculator.
/// </summary>
public class ComprehensionProportionalTests
{
    #region Proportional Counting Tests

    [Fact]
    public async Task ProportionalCounting_MoreMarkers_ProducesHigherScore()
    {
        var calculator = new ComprehensionCalculator();

        // Simple program: 1 function, no contracts
        var simpleContext = CreateContext(
            calor: "§M{m001:Simple} §F{f001:Add:pub} §I{i32:a} §I{i32:b} §O{i32} §R (+ a b) §/F{f001} §/M{m001}",
            csharp: "namespace Simple { public class M { public int Add(int a, int b) { return a + b; } } }");

        // Complex program: multiple functions, contracts, effects
        var complexContext = CreateContext(
            calor: @"§M{m001:Complex}
§F{f001:Divide:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §S (== result (/ a b))
  §E{cw}
  §R (/ a b)
§/F{f001}
§F{f002:Abs:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x -2147483648)
  §S (>= result 0)
  §R x
§/F{f002}
§F{f003:Max:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §S (>= result a)
  §S (>= result b)
  §R a
§/F{f003}
§/M{m001}",
            csharp: @"namespace Complex { public class M {
    public int Divide(int a, int b) { return a / b; }
    public int Abs(int x) { return x; }
    public int Max(int a, int b) { return a; }
} }");

        var simpleResult = await calculator.CalculateAsync(simpleContext);
        var complexResult = await calculator.CalculateAsync(complexContext);

        Assert.True(complexResult.CalorScore > simpleResult.CalorScore,
            $"Complex Calor ({complexResult.CalorScore:F3}) should score higher than simple ({simpleResult.CalorScore:F3}) due to proportional counting");
    }

    [Fact]
    public async Task ProportionalCounting_CSharpAlsoScalesWithDensity()
    {
        var calculator = new ComprehensionCalculator();

        // Simple C# with no documentation
        var simpleContext = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §O{i32} §R 1 §/F{f001} §/M{m001}",
            csharp: "class T { int F() { return 1; } }");

        // C# with documentation, contracts, multiple return types
        var richContext = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §O{i32} §R 1 §/F{f001} §/M{m001}",
            csharp: @"namespace Rich {
    /// <summary>First method</summary>
    /// <param name=""x"">The input</param>
    public class T {
        /// <summary>Second method</summary>
        public int F() { return 1; }
        /// <summary>Third method</summary>
        public string G() { return """"; }
        public int H(int x) { Debug.Assert(x > 0); return x; }
    }
}");

        var simpleResult = await calculator.CalculateAsync(simpleContext);
        var richResult = await calculator.CalculateAsync(richContext);

        Assert.True(richResult.CSharpScore > simpleResult.CSharpScore,
            $"Rich C# ({richResult.CSharpScore:F3}) should score higher than simple ({simpleResult.CSharpScore:F3})");
    }

    [Fact]
    public async Task ProportionalCounting_EmptySource_ScoresZero()
    {
        var calculator = new ComprehensionCalculator();
        var context = CreateContext(calor: "", csharp: "");

        var result = await calculator.CalculateAsync(context);

        Assert.Equal(0.0, result.CalorScore);
        Assert.Equal(0.0, result.CSharpScore);
    }

    #endregion

    #region Contract Depth Tests

    [Fact]
    public async Task ContractCompleteness_BothPreAndPost_GetsBonus()
    {
        var calculator = new ComprehensionCalculator();

        // Pre-only
        var preOnlyContext = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §I{i32:x} §O{i32} §Q (> x 0) §R x §/F{f001} §/M{m001}",
            csharp: "class T { int F(int x) { return x; } }");

        // Pre + Post (contract completeness bonus)
        var bothContext = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §I{i32:x} §O{i32} §Q (> x 0) §S (>= result 0) §R x §/F{f001} §/M{m001}",
            csharp: "class T { int F(int x) { return x; } }");

        var preOnly = await calculator.CalculateAsync(preOnlyContext);
        var both = await calculator.CalculateAsync(bothContext);

        Assert.True(both.CalorScore > preOnly.CalorScore,
            $"Both pre+post ({both.CalorScore:F3}) should score higher than pre-only ({preOnly.CalorScore:F3})");
    }

    [Fact]
    public async Task IdConsistency_MatchedPairs_GetsBonus()
    {
        var calculator = new ComprehensionCalculator();

        // Matched pairs (open == close)
        var matchedContext = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §O{i32} §R 1 §/F{f001} §/M{m001}",
            csharp: "class T { int F() { return 1; } }");

        var result = await calculator.CalculateAsync(matchedContext);

        // Verify the factors report matched pairs
        var factors = result.Details["calorClarityFactors"];
        Assert.NotNull(factors);
    }

    #endregion

    #region Score Cap Tests

    [Fact]
    public async Task Scores_NeverExceedCap()
    {
        var calculator = new ComprehensionCalculator();

        // Maximally rich Calor program
        var richContext = CreateContext(
            calor: @"§M{m001:T}
§F{f001:A:pub} §I{i32:x} §I{i32:y} §O{i32} §Q (> x 0) §Q (> y 0) §S (> result 0) §E{cw,db:rw} §R (+ x y) §/F{f001}
§F{f002:B:pub} §I{i32:x} §O{i32} §Q (>= x 0) §S (>= result 0) §E{fs:rw} §R x §/F{f002}
§F{f003:C:pub} §I{i32:a} §I{i32:b} §O{i32} §Q (!= b 0) §S (== result (/ a b)) §R (/ a b) §/F{f003}
§/M{m001}",
            csharp: @"namespace T {
    /// <summary>A</summary>
    /// <param name=""x"">x</param>
    public class M {
        /// <summary>B</summary>
        /// <param name=""x"">x</param>
        public int A(int x, int y) { Debug.Assert(x > 0); Contract.Requires(y > 0); return x + y; }
        public int B(int x) { Contract.Ensures(x >= 0); return x; }
    }
}");

        var result = await calculator.CalculateAsync(richContext);

        Assert.True(result.CalorScore <= 1.5, $"Calor score {result.CalorScore} should not exceed 1.5");
        Assert.True(result.CSharpScore <= 1.5, $"C# score {result.CSharpScore} should not exceed 1.5");
    }

    #endregion

    #region Clarity Factors Tests

    [Fact]
    public async Task ClarityFactors_ReturnCounts_NotBooleans()
    {
        var calculator = new ComprehensionCalculator();

        var context = CreateContext(
            calor: @"§M{m001:T}
§F{f001:A:pub} §I{i32:x} §O{i32} §Q (> x 0) §S (>= result 0) §R x §/F{f001}
§F{f002:B:pub} §I{i32:y} §O{i32} §Q (> y 0) §R y §/F{f002}
§/M{m001}",
            csharp: "namespace T { public class M { public int A(int x) { return x; } public int B(int y) { return y; } } }");

        var result = await calculator.CalculateAsync(context);

        var calorFactors = result.Details["calorClarityFactors"] as Dictionary<string, object>;
        Assert.NotNull(calorFactors);

        // Should have counts, not booleans
        Assert.True(calorFactors!.ContainsKey("functionCount"));
        Assert.True(calorFactors.ContainsKey("requiresCount"));

        // Counts should be > 1 for this multi-function program
        var funcCount = Convert.ToInt32(calorFactors["functionCount"]);
        Assert.Equal(2, funcCount);

        var reqCount = Convert.ToInt32(calorFactors["requiresCount"]);
        Assert.Equal(2, reqCount);
    }

    [Fact]
    public async Task CSharpClarityFactors_ReturnCounts()
    {
        var calculator = new ComprehensionCalculator();

        var context = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §O{i32} §R 1 §/F{f001} §/M{m001}",
            csharp: @"namespace T {
    /// <summary>First</summary>
    /// <summary>Second</summary>
    public class M {
        public int F() { return 1; }
    }
}");

        var result = await calculator.CalculateAsync(context);

        var csharpFactors = result.Details["csharpClarityFactors"] as Dictionary<string, object>;
        Assert.NotNull(csharpFactors);
        Assert.True(csharpFactors!.ContainsKey("docCommentCount"));

        var docCount = Convert.ToInt32(csharpFactors["docCommentCount"]);
        Assert.True(docCount >= 2, $"Should count multiple doc comments, got {docCount}");
    }

    #endregion

    #region EvaluateQuestionsAsync Tests

    [Fact]
    public async Task EvaluateQuestionsAsync_WithLlmJudge_UsesJudge()
    {
        var calculator = new ComprehensionCalculator();
        var context = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §O{i32} §R 1 §/F{f001} §/M{m001}",
            csharp: "class T { int F() { return 1; } }");

        var questions = new List<ComprehensionQuestion>
        {
            new() { Id = "q1", Question = "What does this return?", ExpectedAnswer = "1" }
        };

        var judgeCallCount = 0;

        var results = await calculator.EvaluateQuestionsAsync(
            context,
            questions,
            answerGenerator: (code, question) => Task.FromResult("Returns 1"),
            answerScorer: (question, actual, expected) =>
            {
                judgeCallCount++;
                return Task.FromResult(0.9);
            });

        Assert.Single(results);
        Assert.Equal(2, judgeCallCount); // Called for both Calor and C#
        Assert.Equal(0.9, results[0].CalorScore);
        Assert.Equal(0.9, results[0].CSharpScore);
    }

    [Fact]
    public async Task EvaluateQuestionsAsync_WithoutJudge_UsesHeuristic()
    {
        var calculator = new ComprehensionCalculator();
        var context = CreateContext(
            calor: "§M{m001:T} §F{f001:F:pub} §O{i32} §R 1 §/F{f001} §/M{m001}",
            csharp: "class T { int F() { return 1; } }");

        var questions = new List<ComprehensionQuestion>
        {
            new() { Id = "q1", Question = "What does this return?", ExpectedAnswer = "1" }
        };

        var results = await calculator.EvaluateQuestionsAsync(
            context,
            questions,
            answerGenerator: (code, question) => Task.FromResult("1"));

        Assert.Single(results);
        Assert.Equal(1.0, results[0].CalorScore); // Exact match
    }

    #endregion

    #region Helper Methods

    private static EvaluationContext CreateContext(string calor, string csharp)
    {
        return new EvaluationContext
        {
            CalorSource = calor,
            CSharpSource = csharp,
            FileName = "test",
            Level = 1,
            Features = new List<string>()
        };
    }

    #endregion
}
