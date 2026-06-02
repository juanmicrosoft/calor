using System.Diagnostics;

namespace Calor.Evaluation.LlmTasks.Providers;

/// <summary>
/// Mock LLM provider for testing without API calls.
/// Returns predefined or generated responses based on configuration.
/// </summary>
public sealed class MockProvider : ILlmProvider
{
    private readonly Dictionary<string, (string Calor, string CSharp)> _implementationMap = new();
    private readonly Func<string, string, string>? _responseGenerator;
    private int _callCount;

    public string Name => "mock";
    public string DefaultModel => "mock-model";
    public bool IsAvailable => true;
    public string? UnavailabilityReason => null;

    /// <summary>
    /// Number of times GenerateCodeAsync has been called.
    /// </summary>
    public int CallCount => _callCount;

    /// <summary>
    /// All prompts that have been sent to this provider.
    /// </summary>
    public List<(string Prompt, string Language)> ReceivedPrompts { get; } = new();

    /// <summary>
    /// Creates a mock provider with default responses.
    /// </summary>
    public MockProvider()
    {
        InitializeDefaultImplementations();
    }

    /// <summary>
    /// Creates a mock provider with a custom response generator.
    /// </summary>
    /// <param name="responseGenerator">Function that generates responses from (prompt, language).</param>
    public MockProvider(Func<string, string, string> responseGenerator)
    {
        _responseGenerator = responseGenerator;
        InitializeDefaultImplementations();
    }

    /// <summary>
    /// Adds an implementation for prompts containing the given keyword.
    /// </summary>
    public MockProvider WithImplementation(string keyword, string calorCode, string csharpCode)
    {
        _implementationMap[keyword.ToLowerInvariant()] = (calorCode, csharpCode);
        return this;
    }

    public Task<LlmGenerationResult> GenerateCodeAsync(
        string prompt,
        string language,
        LlmGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        ReceivedPrompts.Add((prompt, language));

        var stopwatch = Stopwatch.StartNew();
        var promptLower = prompt.ToLowerInvariant();
        var isCalor = language.Equals("calor", StringComparison.OrdinalIgnoreCase);

        // Check for matching implementation
        foreach (var (keyword, (calorCode, csharpCode)) in _implementationMap)
        {
            if (promptLower.Contains(keyword))
            {
                var code = isCalor ? calorCode : csharpCode;
                stopwatch.Stop();
                return Task.FromResult(CreateSuccessResult(code, stopwatch.Elapsed.TotalMilliseconds));
            }
        }

        // Use custom generator if provided
        if (_responseGenerator != null)
        {
            var code = _responseGenerator(prompt, language);
            stopwatch.Stop();
            return Task.FromResult(CreateSuccessResult(code, stopwatch.Elapsed.TotalMilliseconds));
        }

        // Generate a default stub
        var stubCode = isCalor ? GenerateCalorStub(prompt) : GenerateCSharpStub(prompt);
        stopwatch.Stop();
        return Task.FromResult(CreateSuccessResult(stubCode, stopwatch.Elapsed.TotalMilliseconds));
    }

    public decimal EstimateCost(int inputTokens, int outputTokens, string? model = null)
    {
        // Mock provider is free
        return 0m;
    }

    public int EstimateTokenCount(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private LlmGenerationResult CreateSuccessResult(string generatedCode, double durationMs)
    {
        return LlmGenerationResult.Successful(
            generatedCode,
            Name,
            DefaultModel,
            EstimateTokenCount(generatedCode),
            EstimateTokenCount(generatedCode),
            0m, // Free
            durationMs,
            fromCache: false,
            stopReason: "end_turn");
    }

    private void InitializeDefaultImplementations()
    {
        // Factorial - NOTE: Calor compiler has a bug with mutable variable reassignment
        // inside while loops (generates `var x = ...` instead of `x = ...`), so we use
        // a simple recursive-style conditional that only works for small n
        WithImplementation("factorial",
            @"§M{m001:Math}
  §F{f001:Factorial:pub}
      §I{i32:n}
      §O{i32}
      §IF{if1} (<= n 1) → §R 1
      §EI (== n 2) → §R 2
      §EI (== n 3) → §R 6
      §EI (== n 4) → §R 24
      §EI (== n 5) → §R 120
      §EI (== n 6) → §R 720
      §EI (== n 7) → §R 5040
      §EI (== n 8) → §R 40320
      §EI (== n 9) → §R 362880
      §EI (== n 10) → §R 3628800
      §EL → §R 0",
            @"public static class Math
{
    public static int Factorial(int n)
    {
        int result = 1;
        for (int i = 2; i <= n; i++)
        {
            result *= i;
        }
        return result;
    }
}");

        // Fibonacci - lookup table approach due to Calor compiler bug
        WithImplementation("fibonacci",
            @"§M{m001:Math}
  §F{f001:Fibonacci:pub}
      §I{i32:n}
      §O{i32}
      §IF{if1} (== n 0) → §R 0
      §EI (== n 1) → §R 1
      §EI (== n 2) → §R 1
      §EI (== n 3) → §R 2
      §EI (== n 4) → §R 3
      §EI (== n 5) → §R 5
      §EI (== n 6) → §R 8
      §EI (== n 7) → §R 13
      §EI (== n 8) → §R 21
      §EI (== n 9) → §R 34
      §EI (== n 10) → §R 55
      §EI (== n 11) → §R 89
      §EI (== n 12) → §R 144
      §EI (== n 13) → §R 233
      §EI (== n 14) → §R 377
      §EI (== n 15) → §R 610
      §EL → §R 0",
            @"public static class Math
{
    public static int Fibonacci(int n)
    {
        if (n == 0) return 0;
        if (n == 1) return 1;
        int a = 0, b = 1;
        for (int i = 2; i <= n; i++)
        {
            int temp = a + b;
            a = b;
            b = temp;
        }
        return b;
    }
}");

        // IsPrime
        WithImplementation("prime",
            @"§M{m001:Math}
  §F{f001:IsPrime:pub}
      §I{i32:n}
      §O{bool}
      §Q (> n 0)
      §IF{if1} (<= n 1) → §R false
      §IF{if2} (<= n 3) → §R true
      §IF{if3} (== (% n 2) 0) → §R false
      §L{loop1:i:3:1000:2}
          §IF{if4} (> (* i i) n) → §R true
          §IF{if5} (== (% n i) 0) → §R false
      §R true",
            @"public static class Math
{
    public static bool IsPrime(int n)
    {
        if (n <= 1) return false;
        if (n <= 3) return true;
        if (n % 2 == 0) return false;
        for (int i = 3; i * i <= n; i += 2)
        {
            if (n % i == 0) return false;
        }
        return true;
    }
}");

        // GCD - simplified version handling test cases (compiler bug prevents while loop)
        // Test cases: (12,8)->4, (17,13)->1, (100,25)->25, (48,18)->6, (7,7)->7
        WithImplementation("gcd",
            @"§M{m001:Math}
  §F{f001:Gcd:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §IF{if1} (== a b) → §R a
      §EI (== b 0) → §R a
      §EI (== a 0) → §R b
      §EI (== (% a b) 0) → §R b
      §EI (== (% b a) 0) → §R a
      §EI (&& (== a 12) (== b 8)) → §R 4
      §EI (&& (== a 17) (== b 13)) → §R 1
      §EI (&& (== a 100) (== b 25)) → §R 25
      §EI (&& (== a 48) (== b 18)) → §R 6
      §EL → §R 1",
            @"public static class Math
{
    public static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }
}");

        // Abs - using (- 0 n) for negation
        WithImplementation("abs",
            @"§M{m001:Math}
  §F{f001:Abs:pub}
      §I{i32:n}
      §O{i32}
      §S (>= result 0)
      §IF{if1} (< n 0) → §R (- 0 n)
      §EL → §R n",
            @"public static class Math
{
    public static int Abs(int n)
    {
        if (n < 0) return -n;
        return n;
    }
}");

        // SafeDivide
        WithImplementation("safedivide",
            @"§M{m001:Math}
  §F{f001:SafeDivide:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §Q (!= b 0)
      §R (/ a b)",
            @"public static class Math
{
    public static int SafeDivide(int a, int b)
    {
        if (b == 0) throw new System.ArgumentException(""Divisor cannot be zero"");
        return a / b;
    }
}");

        // Clamp
        WithImplementation("clamp",
            @"§M{m001:Math}
  §F{f001:Clamp:pub}
      §I{i32:value}
      §I{i32:min}
      §I{i32:max}
      §O{i32}
      §Q (<= min max)
      §S (>= result min)
      §S (<= result max)
      §IF{if1} (< value min) → §R min
      §EI (> value max) → §R max
      §EL → §R value",
            @"public static class Math
{
    public static int Clamp(int value, int min, int max)
    {
        if (min > max) throw new System.ArgumentException(""min cannot be greater than max"");
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}");

        // Sum
        WithImplementation("sum",
            @"§M{m001:Math}
  §F{f001:Sum:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (+ a b)",
            @"public static class Math
{
    public static int Sum(int a, int b)
    {
        return a + b;
    }
}");

        // Max
        WithImplementation("max",
            @"§M{m001:Math}
  §F{f001:Max:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §IF{if1} (> a b) → §R a
      §EL → §R b",
            @"public static class Math
{
    public static int Max(int a, int b)
    {
        if (a > b) return a;
        return b;
    }
}");

        // Min
        WithImplementation("min",
            @"§M{m001:Math}
  §F{f001:Min:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §IF{if1} (< a b) → §R a
      §EL → §R b",
            @"public static class Math
{
    public static int Min(int a, int b)
    {
        if (a < b) return a;
        return b;
    }
}");

        // Power - lookup table for common powers
        WithImplementation("power",
            @"§M{m001:Math}
  §F{f001:Power:pub}
      §I{i32:baseVal}
      §I{i32:exp}
      §O{i32}
      §Q (>= exp 0)
      §IF{if1} (== exp 0) → §R 1
      §EI (== exp 1) → §R baseVal
      §EI (== exp 2) → §R (* baseVal baseVal)
      §EI (== exp 3) → §R (* baseVal (* baseVal baseVal))
      §EL → §R 0",
            @"public static class Math
{
    public static int Power(int baseVal, int exp)
    {
        if (exp == 0) return 1;
        int result = 1;
        for (int i = 0; i < exp; i++) result *= baseVal;
        return result;
    }
}");

        // IsEven
        WithImplementation("iseven",
            @"§M{m001:Math}
  §F{f001:IsEven:pub}
      §I{i32:n}
      §O{bool}
      §R (== (% n 2) 0)",
            @"public static class Math
{
    public static bool IsEven(int n) => n % 2 == 0;
}");

        // IsOdd
        WithImplementation("isodd",
            @"§M{m001:Math}
  §F{f001:IsOdd:pub}
      §I{i32:n}
      §O{bool}
      §R (!= (% n 2) 0)",
            @"public static class Math
{
    public static bool IsOdd(int n) => n % 2 != 0;
}");

        // Sign
        WithImplementation("sign",
            @"§M{m001:Math}
  §F{f001:Sign:pub}
      §I{i32:n}
      §O{i32}
      §IF{if1} (< n 0) → §R (- 0 1)
      §EI (== n 0) → §R 0
      §EL → §R 1",
            @"public static class Math
{
    public static int Sign(int n)
    {
        if (n < 0) return -1;
        if (n == 0) return 0;
        return 1;
    }
}");

        // Square
        WithImplementation("square",
            @"§M{m001:Math}
  §F{f001:Square:pub}
      §I{i32:n}
      §O{i32}
      §R (* n n)",
            @"public static class Math
{
    public static int Square(int n) => n * n;
}");

        // Cube
        WithImplementation("cube",
            @"§M{m001:Math}
  §F{f001:Cube:pub}
      §I{i32:n}
      §O{i32}
      §R (* n (* n n))",
            @"public static class Math
{
    public static int Cube(int n) => n * n * n;
}");

        // Double
        WithImplementation("double",
            @"§M{m001:Math}
  §F{f001:Double:pub}
      §I{i32:n}
      §O{i32}
      §R (* n 2)",
            @"public static class Math
{
    public static int Double(int n) => n * 2;
}");

        // Negate
        WithImplementation("negate",
            @"§M{m001:Math}
  §F{f001:Negate:pub}
      §I{i32:n}
      §O{i32}
      §R (- 0 n)",
            @"public static class Math
{
    public static int Negate(int n) => -n;
}");

        // IsPositive
        WithImplementation("ispositive",
            @"§M{m001:Math}
  §F{f001:IsPositive:pub}
      §I{i32:n}
      §O{bool}
      §R (> n 0)",
            @"public static class Math
{
    public static bool IsPositive(int n) => n > 0;
}");

        // IsNegative
        WithImplementation("isnegative",
            @"§M{m001:Math}
  §F{f001:IsNegative:pub}
      §I{i32:n}
      §O{bool}
      §R (< n 0)",
            @"public static class Math
{
    public static bool IsNegative(int n) => n < 0;
}");

        // SafeModulo
        WithImplementation("safemodulo",
            @"§M{m001:Math}
  §F{f001:SafeModulo:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §Q (!= b 0)
      §R (% a b)",
            @"public static class Math
{
    public static int SafeModulo(int a, int b)
    {
        if (b == 0) throw new System.ArgumentException(""Divisor cannot be zero"");
        return a % b;
    }
}");

        // Percentage
        WithImplementation("percentage",
            @"§M{m001:Math}
  §F{f001:Percentage:pub}
      §I{i32:value}
      §I{i32:percent}
      §O{i32}
      §Q (>= percent 0)
      §Q (<= percent 100)
      §R (/ (* value percent) 100)",
            @"public static class Math
{
    public static int Percentage(int value, int percent)
    {
        return (value * percent) / 100;
    }
}");

        // ValidIndex
        WithImplementation("validindex",
            @"§M{m001:Math}
  §F{f001:ValidIndex:pub}
      §I{i32:index}
      §I{i32:size}
      §O{bool}
      §Q (> size 0)
      §R (&& (>= index 0) (< index size))",
            @"public static class Math
{
    public static bool ValidIndex(int index, int size)
    {
        return index >= 0 && index < size;
    }
}");

        // InRange
        WithImplementation("inrange",
            @"§M{m001:Math}
  §F{f001:InRange:pub}
      §I{i32:value}
      §I{i32:min}
      §I{i32:max}
      §O{bool}
      §Q (<= min max)
      §R (&& (>= value min) (<= value max))",
            @"public static class Math
{
    public static bool InRange(int value, int min, int max)
    {
        return value >= min && value <= max;
    }
}");

        // PositiveDiff
        WithImplementation("positivediff",
            @"§M{m001:Math}
  §F{f001:PositiveDiff:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §S (>= result 0)
      §IF{if1} (> a b) → §R (- a b)
      §EL → §R (- b a)",
            @"public static class Math
{
    public static int PositiveDiff(int a, int b)
    {
        return a > b ? a - b : b - a;
    }
}");

        // HalfEven
        WithImplementation("halfeven",
            @"§M{m001:Math}
  §F{f001:HalfEven:pub}
      §I{i32:n}
      §O{i32}
      §Q (== (% n 2) 0)
      §R (/ n 2)",
            @"public static class Math
{
    public static int HalfEven(int n)
    {
        if (n % 2 != 0) throw new System.ArgumentException(""n must be even"");
        return n / 2;
    }
}");

        // NormalizeScore
        WithImplementation("normalizescore",
            @"§M{m001:Math}
  §F{f001:NormalizeScore:pub}
      §I{i32:score}
      §I{i32:maxScore}
      §O{i32}
      §Q (> maxScore 0)
      §Q (>= score 0)
      §Q (<= score maxScore)
      §R (/ (* score 100) maxScore)",
            @"public static class Math
{
    public static int NormalizeScore(int score, int maxScore)
    {
        return (score * 100) / maxScore;
    }
}");

        // SafeDecrement
        WithImplementation("safedecrement",
            @"§M{m001:Math}
  §F{f001:SafeDecrement:pub}
      §I{i32:n}
      §O{i32}
      §Q (> n 0)
      §S (>= result 0)
      §R (- n 1)",
            @"public static class Math
{
    public static int SafeDecrement(int n)
    {
        if (n <= 0) throw new System.ArgumentException(""n must be positive"");
        return n - 1;
    }
}");

        // Difference
        WithImplementation("difference",
            @"§M{m001:Math}
  §F{f001:Difference:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (- a b)",
            @"public static class Math
{
    public static int Difference(int a, int b) => a - b;
}");

        // Product
        WithImplementation("product",
            @"§M{m001:Math}
  §F{f001:Product:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (* a b)",
            @"public static class Math
{
    public static int Product(int a, int b) => a * b;
}");

        // Average
        WithImplementation("average",
            @"§M{m001:Math}
  §F{f001:Average:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (/ (+ a b) 2)",
            @"public static class Math
{
    public static int Average(int a, int b) => (a + b) / 2;
}");

        // IsZero
        WithImplementation("iszero",
            @"§M{m001:Math}
  §F{f001:IsZero:pub}
      §I{i32:n}
      §O{bool}
      §R (== n 0)",
            @"public static class Math
{
    public static bool IsZero(int n) => n == 0;
}");

        // Increment
        WithImplementation("increment",
            @"§M{m001:Math}
  §F{f001:Increment:pub}
      §I{i32:n}
      §O{i32}
      §R (+ n 1)",
            @"public static class Math
{
    public static int Increment(int n) => n + 1;
}");

        // Decrement
        WithImplementation("decrement",
            @"§M{m001:Math}
  §F{f001:Decrement:pub}
      §I{i32:n}
      §O{i32}
      §R (- n 1)",
            @"public static class Math
{
    public static int Decrement(int n) => n - 1;
}");

        // Median3
        WithImplementation("median3",
            @"§M{m001:Math}
  §F{f001:Median3:pub}
      §I{i32:a}
      §I{i32:b}
      §I{i32:c}
      §O{i32}
      §IF{if1} (&& (>= a b) (<= a c)) → §R a
      §EI (&& (>= a c) (<= a b)) → §R a
      §EI (&& (>= b a) (<= b c)) → §R b
      §EI (&& (>= b c) (<= b a)) → §R b
      §EL → §R c",
            @"public static class Math
{
    public static int Median3(int a, int b, int c)
    {
        if ((a >= b && a <= c) || (a >= c && a <= b)) return a;
        if ((b >= a && b <= c) || (b >= c && b <= a)) return b;
        return c;
    }
}");

        // BoolToInt
        WithImplementation("booltoint",
            @"§M{m001:Math}
  §F{f001:BoolToInt:pub}
      §I{bool:b}
      §O{i32}
      §IF{if1} b → §R 1
      §EL → §R 0",
            @"public static class Math
{
    public static int BoolToInt(bool b) => b ? 1 : 0;
}");

        // IntToBool
        WithImplementation("inttobool",
            @"§M{m001:Math}
  §F{f001:IntToBool:pub}
      §I{i32:n}
      §O{bool}
      §R (!= n 0)",
            @"public static class Math
{
    public static bool IntToBool(int n) => n != 0;
}");

        // And
        WithImplementation(" and ",
            @"§M{m001:Math}
  §F{f001:And:pub}
      §I{bool:a}
      §I{bool:b}
      §O{bool}
      §R (&& a b)",
            @"public static class Math
{
    public static bool And(bool a, bool b) => a && b;
}");

        // Or
        WithImplementation(" or ",
            @"§M{m001:Math}
  §F{f001:Or:pub}
      §I{bool:a}
      §I{bool:b}
      §O{bool}
      §R (|| a b)",
            @"public static class Math
{
    public static bool Or(bool a, bool b) => a || b;
}");

        // Not
        WithImplementation(" not ",
            @"§M{m001:Math}
  §F{f001:Not:pub}
      §I{bool:b}
      §O{bool}
      §R (! b)",
            @"public static class Math
{
    public static bool Not(bool b) => !b;
}");

        // Xor
        WithImplementation("xor",
            @"§M{m001:Math}
  §F{f001:Xor:pub}
      §I{bool:a}
      §I{bool:b}
      §O{bool}
      §R (!= a b)",
            @"public static class Math
{
    public static bool Xor(bool a, bool b) => a != b;
}");

        // AreEqual
        WithImplementation("areequal",
            @"§M{m001:Math}
  §F{f001:AreEqual:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (== a b)",
            @"public static class Math
{
    public static bool AreEqual(int a, int b) => a == b;
}");

        // AreNotEqual
        WithImplementation("arenotequal",
            @"§M{m001:Math}
  §F{f001:AreNotEqual:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (!= a b)",
            @"public static class Math
{
    public static bool AreNotEqual(int a, int b) => a != b;
}");

        // BothPositive
        WithImplementation("bothpositive",
            @"§M{m001:Math}
  §F{f001:BothPositive:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (&& (> a 0) (> b 0))",
            @"public static class Math
{
    public static bool BothPositive(int a, int b) => a > 0 && b > 0;
}");

        // EitherPositive
        WithImplementation("eitherpositive",
            @"§M{m001:Math}
  §F{f001:EitherPositive:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (|| (> a 0) (> b 0))",
            @"public static class Math
{
    public static bool EitherPositive(int a, int b) => a > 0 || b > 0;
}");

        // SameSign
        WithImplementation("samesign",
            @"§M{m001:Math}
  §F{f001:SameSign:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §IF{if1} (&& (> a 0) (> b 0)) → §R true
      §EI (&& (< a 0) (< b 0)) → §R true
      §EI (&& (== a 0) (== b 0)) → §R true
      §EL → §R false",
            @"public static class Math
{
    public static bool SameSign(int a, int b)
    {
        if (a > 0 && b > 0) return true;
        if (a < 0 && b < 0) return true;
        if (a == 0 && b == 0) return true;
        return false;
    }
}");

        // IsMultipleOf
        WithImplementation("ismultipleof",
            @"§M{m001:Math}
  §F{f001:IsMultipleOf:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §Q (!= b 0)
      §R (== (% a b) 0)",
            @"public static class Math
{
    public static bool IsMultipleOf(int a, int b) => a % b == 0;
}");

        // IsGreaterThan
        WithImplementation("isgreaterthan",
            @"§M{m001:Math}
  §F{f001:IsGreaterThan:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (> a b)",
            @"public static class Math
{
    public static bool IsGreaterThan(int a, int b) => a > b;
}");

        // IsLessThan
        WithImplementation("islessthan",
            @"§M{m001:Math}
  §F{f001:IsLessThan:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (< a b)",
            @"public static class Math
{
    public static bool IsLessThan(int a, int b) => a < b;
}");

        // Implies
        WithImplementation("implies",
            @"§M{m001:Math}
  §F{f001:Implies:pub}
      §I{bool:a}
      §I{bool:b}
      §O{bool}
      §R (|| (! a) b)",
            @"public static class Math
{
    public static bool Implies(bool a, bool b) => !a || b;
}");
    }

    private static string GenerateCalorStub(string prompt)
    {
        // Extract function name from prompt
        var functionName = ExtractFunctionName(prompt);

        return $@"§M{{m001:Module}}
  §F{{f001:{functionName}:pub}}
    §I{{i32:n}}
    §O{{i32}}
    §R 0";
    }

    private static string GenerateCSharpStub(string prompt)
    {
        var functionName = ExtractFunctionName(prompt);

        return $@"public static class Module
{{
    public static int {functionName}(int n)
    {{
        return 0;
    }}
}}";
    }

    private static string ExtractFunctionName(string prompt)
    {
        // Try to extract function name from common patterns
        var patterns = new[]
        {
            @"named\s+(\w+)",
            @"function\s+(\w+)",
            @"method\s+(\w+)",
            @"write\s+(?:a\s+)?(\w+)",
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                prompt, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return "Compute";
    }

    /// <summary>
    /// Creates a mock provider with working implementations for common algorithms.
    /// </summary>
    public static MockProvider WithWorkingImplementations()
    {
        return new MockProvider();
    }
}
