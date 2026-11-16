using System.Text.RegularExpressions;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Scores effect discipline for the benchmark using outcome-based scoring.
/// Both languages are evaluated equally based on whether they produce correct, deterministic results.
///
/// Scoring Philosophy:
/// - Bug prevention is measured by TEST OUTCOMES, not syntax analysis
/// - If tests pass → BugPrevention = 1.0 for BOTH languages
/// - If tests fail → BugPrevention = 0.0
/// - No bonus for language-specific syntax (effect rows, [Pure], etc.)
/// </summary>
public static class EffectDisciplineScorer
{
    /// <summary>
    /// Weight for functional correctness (does the code work?).
    /// </summary>
    public const double CorrectnessWeight = 0.50;

    /// <summary>
    /// Weight for bug prevention (did the code produce deterministic results?).
    /// </summary>
    public const double BugPreventionWeight = 0.50;

    /// <summary>
    /// Calculates the overall discipline score for a task result.
    /// Simplified to focus on outcomes: correctness + determinism.
    /// </summary>
    /// <param name="correctness">Functional correctness score (0-1).</param>
    /// <param name="bugPrevention">Bug prevention score (0-1) - based on test outcomes.</param>
    /// <param name="maintainability">Maintainability score (ignored for fairness).</param>
    /// <returns>Weighted overall score (0-1).</returns>
    public static double CalculateDisciplineScore(
        double correctness,
        double bugPrevention,
        double maintainability)
    {
        // Simplified scoring: 50% correctness (tests pass) + 50% bug prevention (determinism)
        // Maintainability is ignored because it unfairly rewarded Calor syntax
        return (correctness * CorrectnessWeight) +
               (bugPrevention * BugPreventionWeight);
    }

    /// <summary>
    /// Scores bug prevention based on test outcomes.
    /// This is the FAIR scoring method used for both languages.
    /// </summary>
    /// <param name="testsPass">Whether all tests passed.</param>
    /// <param name="isDeterministic">Whether the code is deterministic (no known-bad patterns).</param>
    /// <returns>Bug prevention score (0-1).</returns>
    public static double ScoreBugPreventionByOutcome(bool testsPass, bool isDeterministic)
    {
        // Pure outcome-based scoring: tests pass = 1.0, tests fail = 0.0
        // Both languages can achieve 1.0 equally
        if (!testsPass)
        {
            return 0.0;
        }

        // If tests pass but code uses non-deterministic patterns, it's a warning
        // but we still give full credit since tests passed (outcome-based)
        return 1.0;
    }

    /// <summary>
    /// Scores Calor code for effect discipline using outcome-based scoring.
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
        // Outcome-based scoring: compilation failed = 0.0
        // No special treatment for effect violations - same as any compilation failure
        // This ensures fair comparison with C#
        if (!compilationSuccess)
        {
            return 0.0;
        }

        // Code compiled - actual score determined by test outcomes
        // Return 1.0 as placeholder; actual scoring uses test results
        return 1.0;
    }

    /// <summary>
    /// Scores C# code for effect discipline using outcome-based scoring.
    /// C# can achieve the same score as Calor when tests pass.
    /// </summary>
    /// <param name="code">The C# source code.</param>
    /// <param name="compilationSuccess">Whether the code compiled.</param>
    /// <param name="analyzerDiagnostics">Diagnostics from Roslyn analyzers (for warnings only).</param>
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

        // Code compiled - actual score determined by test outcomes
        // Return 1.0 as placeholder; actual scoring uses test results
        // No penalties for syntax - we only care about outcomes
        return 1.0;
    }

    /// <summary>
    /// Detects non-deterministic patterns in code (for warnings only, not scoring).
    /// Returns a list of detected patterns that could cause flaky behavior.
    /// </summary>
    /// <param name="code">The source code to analyze.</param>
    /// <param name="category">The task category for context.</param>
    /// <returns>List of detected non-deterministic patterns.</returns>
    public static List<string> DetectNonDeterministicPatterns(string code, string category)
    {
        var warnings = new List<string>();

        // Time-related patterns (flaky tests, cache safety)
        if (category is "flaky-test-prevention" or "cache-safety")
        {
            if (ContainsDateTimeNow(code))
                warnings.Add("DateTime.Now/UtcNow usage detected");
            if (ContainsUnsafeRandom(code))
                warnings.Add("Unseeded Random() usage detected");
            if (ContainsGuidNewGuid(code))
                warnings.Add("Guid.NewGuid() usage detected");
        }

        // Network/IO patterns (security boundaries)
        if (category == "security-boundaries")
        {
            if (ContainsNetworkCalls(code))
                warnings.Add("Network call detected");
            if (ContainsFileOperations(code))
                warnings.Add("File I/O detected");
        }

        // Side effect patterns (transparency)
        if (category == "side-effect-transparency")
        {
            if (ContainsConsoleOutput(code))
                warnings.Add("Console output detected");
            if (ContainsLogging(code))
                warnings.Add("Logging detected");
        }

        return warnings;
    }

    /// <summary>
    /// Scores maintainability based on code clarity.
    /// Simplified to not favor either language's syntax.
    /// </summary>
    /// <param name="code">The source code.</param>
    /// <param name="language">The language ("calor" or "csharp").</param>
    /// <returns>Maintainability score (0-1).</returns>
    public static double ScoreMaintainability(string code, string language)
    {
        // For fair scoring, give both languages the same base maintainability score
        // Actual maintainability is subjective and language-specific syntax
        // should not be rewarded
        var score = 0.7; // Base score for readable code

        // Only reward language-neutral good practices
        if (HasDescriptiveNames(code))
        {
            score += 0.15;
        }

        // Documentation is good in any language
        if (HasXmlDocs(code) || HasComments(code))
        {
            score += 0.15;
        }

        return Math.Min(score, 1.0);
    }

    private static bool HasComments(string code) =>
        code.Contains("//") || code.Contains("/*");

    #region Code Analysis Helpers

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
