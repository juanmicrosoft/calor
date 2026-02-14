using System.Text.RegularExpressions;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Scores effect discipline for the benchmark.
/// Evaluates code based on bug prevention, functional correctness, and maintainability.
/// </summary>
public static class EffectDisciplineScorer
{
    /// <summary>
    /// Weight for functional correctness (does the code work?).
    /// </summary>
    public const double CorrectnessWeight = 0.40;

    /// <summary>
    /// Weight for bug prevention (would this code have the target bug?).
    /// </summary>
    public const double BugPreventionWeight = 0.40;

    /// <summary>
    /// Weight for maintainability (can another developer understand the constraints?).
    /// </summary>
    public const double MaintainabilityWeight = 0.20;

    /// <summary>
    /// Calculates the overall discipline score for a task result.
    /// </summary>
    /// <param name="correctness">Functional correctness score (0-1).</param>
    /// <param name="bugPrevention">Bug prevention score (0-1).</param>
    /// <param name="maintainability">Maintainability score (0-1).</param>
    /// <returns>Weighted overall score (0-1).</returns>
    public static double CalculateDisciplineScore(
        double correctness,
        double bugPrevention,
        double maintainability)
    {
        return (correctness * CorrectnessWeight) +
               (bugPrevention * BugPreventionWeight) +
               (maintainability * MaintainabilityWeight);
    }

    /// <summary>
    /// Scores Calor code for effect discipline.
    /// Calor's effect system provides compile-time enforcement.
    /// </summary>
    /// <param name="code">The Calor source code.</param>
    /// <param name="compilationSuccess">Whether the code compiled.</param>
    /// <param name="effectViolations">List of effect violations from compiler.</param>
    /// <param name="category">The task category for context.</param>
    /// <returns>Bug prevention score (0-1).</returns>
    public static double ScoreCalorBugPrevention(
        string code,
        bool compilationSuccess,
        IReadOnlyList<string>? effectViolations,
        string category)
    {
        // If code doesn't compile, it might be due to effect violations (good!) or other errors (bad)
        if (!compilationSuccess)
        {
            // Check if failure was due to effect violation (this is actually good - caught at compile time)
            if (effectViolations != null && effectViolations.Any(v =>
                v.Contains("effect", StringComparison.OrdinalIgnoreCase) ||
                v.Contains("pure", StringComparison.OrdinalIgnoreCase) ||
                v.Contains("§E", StringComparison.OrdinalIgnoreCase)))
            {
                // Compiler caught an effect violation - this is the intended behavior
                return 1.0;
            }

            // Failed for other reasons - partial credit
            return 0.3;
        }

        // Code compiled - check if it uses proper effect annotations
        var score = 0.5; // Base score for compiling

        // Check for explicit effect declarations
        if (HasEffectAnnotations(code))
        {
            score += 0.3;
        }

        // Check for pure function markers based on category
        if (IsPureByCategory(category) && CodeAppearsPure(code))
        {
            score += 0.2;
        }

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Scores C# code for effect discipline based on best practices.
    /// C# relies on conventions, patterns, and analyzers rather than compile-time enforcement.
    /// </summary>
    /// <param name="code">The C# source code.</param>
    /// <param name="compilationSuccess">Whether the code compiled.</param>
    /// <param name="analyzerDiagnostics">Diagnostics from Roslyn analyzers (ED001-ED007).</param>
    /// <param name="category">The task category for context.</param>
    /// <returns>Bug prevention score (0-1).</returns>
    public static double ScoreCSharpBugPrevention(
        string code,
        bool compilationSuccess,
        IReadOnlyList<AnalyzerDiagnostic>? analyzerDiagnostics,
        string category)
    {
        if (!compilationSuccess)
        {
            return 0.0;
        }

        // Start with base score
        var score = 0.5;

        // Deduct for analyzer violations
        if (analyzerDiagnostics != null && analyzerDiagnostics.Count > 0)
        {
            var errorCount = analyzerDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warningCount = analyzerDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

            // Errors are critical - bug would reach production
            if (errorCount > 0)
            {
                return 0.0;
            }

            // Warnings reduce score
            score -= warningCount * 0.1;
            score = Math.Max(score, 0.3);
        }
        else
        {
            // No analyzer ran - use heuristic analysis
            score = ScoreCSharpByHeuristics(code, category);
        }

        // Bonus for best practices
        score += ScoreCSharpBestPractices(code, category);

        return Math.Min(Math.Max(score, 0.0), 1.0);
    }

    /// <summary>
    /// Heuristic analysis of C# code when analyzers aren't available.
    /// </summary>
    private static double ScoreCSharpByHeuristics(string code, string category)
    {
        var score = 0.5;
        var violations = new List<string>();

        // Check for time-related violations (flaky tests)
        if (category == "flaky-test-prevention" || category == "cache-safety")
        {
            if (ContainsDateTimeNow(code))
            {
                violations.Add("DateTime.Now usage");
                score -= 0.3;
            }

            if (ContainsUnsafeRandom(code))
            {
                violations.Add("Unseeded Random usage");
                score -= 0.3;
            }

            if (ContainsGuidNewGuid(code))
            {
                violations.Add("Guid.NewGuid() usage");
                score -= 0.3;
            }
        }

        // Check for network/IO violations (security boundaries)
        if (category == "security-boundaries")
        {
            if (ContainsNetworkCalls(code))
            {
                violations.Add("Network access");
                score -= 0.5;
            }

            if (ContainsFileOperations(code))
            {
                violations.Add("File I/O");
                score -= 0.3;
            }
        }

        // Check for side effect violations (transparency)
        if (category == "side-effect-transparency")
        {
            if (ContainsConsoleOutput(code))
            {
                violations.Add("Console output");
                score -= 0.3;
            }

            if (ContainsLogging(code))
            {
                violations.Add("Logging");
                score -= 0.3;
            }
        }

        return Math.Max(score, 0.0);
    }

    /// <summary>
    /// Scores C# code for following best practices.
    /// </summary>
    private static double ScoreCSharpBestPractices(string code, string category)
    {
        var bonus = 0.0;

        // Check for [Pure] attribute
        if (HasPureAttribute(code))
        {
            bonus += 0.15;
        }

        // Check for static method (suggests no instance state)
        if (IsStaticMethod(code))
        {
            bonus += 0.1;
        }

        // Check for readonly/immutable patterns
        if (UsesImmutablePatterns(code))
        {
            bonus += 0.1;
        }

        // Category-specific bonuses
        if (category == "flaky-test-prevention")
        {
            // Bonus for DI pattern (accepting dependencies as parameters)
            if (UsesDependencyInjection(code))
            {
                bonus += 0.15;
            }
        }

        return bonus;
    }

    /// <summary>
    /// Scores maintainability based on code clarity and documentation.
    /// </summary>
    /// <param name="code">The source code.</param>
    /// <param name="language">The language ("calor" or "csharp").</param>
    /// <returns>Maintainability score (0-1).</returns>
    public static double ScoreMaintainability(string code, string language)
    {
        var score = 0.5; // Base score

        // Check for self-documenting code
        if (HasDescriptiveNames(code))
        {
            score += 0.2;
        }

        // Check for appropriate documentation
        if (language == "calor")
        {
            // Effect annotations document behavior
            if (HasEffectAnnotations(code))
            {
                score += 0.3;
            }
        }
        else
        {
            // XML docs or [Pure] attribute
            if (HasXmlDocs(code) || HasPureAttribute(code))
            {
                score += 0.3;
            }
        }

        return Math.Min(score, 1.0);
    }

    #region Code Analysis Helpers

    private static bool HasEffectAnnotations(string code) =>
        code.Contains("§E{") ||
        code.Contains("§E[") ||
        Regex.IsMatch(code, @"\bpure\b", RegexOptions.IgnoreCase);

    private static bool IsPureByCategory(string category) =>
        category is "flaky-test-prevention" or "cache-safety" or "side-effect-transparency";

    private static bool CodeAppearsPure(string code) =>
        !ContainsDateTimeNow(code) &&
        !ContainsUnsafeRandom(code) &&
        !ContainsNetworkCalls(code) &&
        !ContainsFileOperations(code) &&
        !ContainsConsoleOutput(code);

    private static bool ContainsDateTimeNow(string code) =>
        Regex.IsMatch(code, @"DateTime\.(Now|UtcNow|Today)", RegexOptions.IgnoreCase);

    private static bool ContainsUnsafeRandom(string code) =>
        Regex.IsMatch(code, @"new\s+Random\s*\(\s*\)");

    private static bool ContainsGuidNewGuid(string code) =>
        Regex.IsMatch(code, @"Guid\.NewGuid\s*\(\s*\)");

    private static bool ContainsNetworkCalls(string code) =>
        Regex.IsMatch(code, @"\b(HttpClient|WebRequest|WebClient|Socket|TcpClient|UdpClient)\b") ||
        Regex.IsMatch(code, @"\.(GetAsync|PostAsync|SendAsync|DownloadString)\b");

    private static bool ContainsFileOperations(string code) =>
        Regex.IsMatch(code, @"\bFile\.(Read|Write|Open|Create|Delete|Exists|Copy|Move)") ||
        Regex.IsMatch(code, @"\bDirectory\.(Create|Delete|Exists|GetFiles)") ||
        Regex.IsMatch(code, @"\b(StreamReader|StreamWriter|FileStream)\b");

    private static bool ContainsConsoleOutput(string code) =>
        Regex.IsMatch(code, @"Console\.(Write|WriteLine|Error)");

    private static bool ContainsLogging(string code) =>
        Regex.IsMatch(code, @"\b(Logger|Log|Logging|ILogger)\b", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(code, @"\.(LogInformation|LogWarning|LogError|LogDebug|Log)\b");

    private static bool HasPureAttribute(string code) =>
        code.Contains("[Pure]") ||
        code.Contains("[System.Diagnostics.Contracts.Pure]");

    private static bool IsStaticMethod(string code) =>
        Regex.IsMatch(code, @"\bpublic\s+static\b");

    private static bool UsesImmutablePatterns(string code) =>
        code.Contains("readonly") ||
        code.Contains("ImmutableArray") ||
        code.Contains("ImmutableList") ||
        code.Contains("record ") ||
        code.Contains("init;");

    private static bool UsesDependencyInjection(string code) =>
        Regex.IsMatch(code, @"(ITimeProvider|IClock|IDateTimeProvider|IRandomGenerator)", RegexOptions.IgnoreCase) ||
        code.Contains("Func<DateTime>") ||
        code.Contains("Func<int>");

    private static bool HasDescriptiveNames(string code)
    {
        // Simple heuristic: check for PascalCase names with multiple words
        var matches = Regex.Matches(code, @"\b([A-Z][a-z]+){2,}\b");
        return matches.Count >= 2;
    }

    private static bool HasXmlDocs(string code) =>
        code.Contains("/// <summary>") ||
        code.Contains("/// <param") ||
        code.Contains("/// <returns");

    #endregion
}

/// <summary>
/// Represents a diagnostic from a Roslyn analyzer.
/// </summary>
public record AnalyzerDiagnostic
{
    /// <summary>
    /// The diagnostic rule ID (e.g., "ED001").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The diagnostic message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The severity level.
    /// </summary>
    public DiagnosticSeverity Severity { get; init; }

    /// <summary>
    /// The line number where the issue was found.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// The column number where the issue was found.
    /// </summary>
    public int Column { get; init; }
}

/// <summary>
/// Severity levels for analyzer diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Hidden diagnostic (not shown by default).
    /// </summary>
    Hidden = 0,

    /// <summary>
    /// Informational diagnostic.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Warning diagnostic.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Error diagnostic - indicates bug would reach production.
    /// </summary>
    Error = 3
}

/// <summary>
/// Detailed analysis of effect discipline for a task.
/// </summary>
public record EffectDisciplineAnalysis
{
    /// <summary>
    /// Whether effect discipline was properly maintained.
    /// </summary>
    public bool DisciplineMaintained { get; init; }

    /// <summary>
    /// List of detected violations.
    /// </summary>
    public List<string> Violations { get; init; } = new();

    /// <summary>
    /// List of best practices that were followed.
    /// </summary>
    public List<string> BestPractices { get; init; } = new();

    /// <summary>
    /// The overall discipline score.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// The discipline quality level.
    /// </summary>
    public DisciplineQualityLevel QualityLevel { get; init; }
}

/// <summary>
/// Qualitative levels for effect discipline.
/// </summary>
public enum DisciplineQualityLevel
{
    /// <summary>
    /// Excellent: No violations, follows all best practices.
    /// </summary>
    Excellent,

    /// <summary>
    /// Good: Minor issues but bug unlikely to reach production.
    /// </summary>
    Good,

    /// <summary>
    /// Adequate: Some concerns but functionally correct.
    /// </summary>
    Adequate,

    /// <summary>
    /// Poor: Likely to have the target bug in production.
    /// </summary>
    Poor,

    /// <summary>
    /// Fail: Definitely has the target bug.
    /// </summary>
    Fail
}
