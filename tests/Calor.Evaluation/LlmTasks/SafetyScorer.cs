using Calor.Runtime;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Scores error quality for the safety benchmark.
/// Measures how informative and precise exceptions are when contracts are violated.
/// </summary>
public static class SafetyScorer
{
    /// <summary>
    /// Scores the quality of an error/exception for safety benchmarking.
    /// </summary>
    /// <param name="exception">The exception that was thrown (or null if none).</param>
    /// <param name="language">The source language ("calor" or "csharp").</param>
    /// <param name="expectedViolation">Whether a violation was expected.</param>
    /// <returns>Error quality score from 0.0 to 1.0.</returns>
    public static double ScoreErrorQuality(Exception? exception, string language, bool expectedViolation)
    {
        // If no exception but one was expected = complete failure
        if (exception == null)
        {
            return expectedViolation ? 0.0 : 1.0;
        }

        // If exception but none was expected = also a failure (different issue)
        if (!expectedViolation)
        {
            return 0.0;
        }

        // Score based on exception quality
        return language.ToLowerInvariant() == "calor"
            ? ScoreCalorException(exception)
            : ScoreCSharpException(exception);
    }

    /// <summary>
    /// Scores a Calor ContractViolationException.
    /// Full diagnostics (location + condition) = 1.0
    /// Partial diagnostics = 0.7-0.9
    /// Basic exception = 0.5
    /// </summary>
    private static double ScoreCalorException(Exception exception)
    {
        if (exception is ContractViolationException cve)
        {
            var score = 0.5; // Base score for catching the violation

            // Add points for having location info
            if (cve.Line > 0 && cve.Column > 0)
            {
                score += 0.2;
            }

            // Add points for having condition text
            if (!string.IsNullOrEmpty(cve.Condition))
            {
                score += 0.2;
            }

            // Add points for having function ID
            if (!string.IsNullOrEmpty(cve.FunctionId))
            {
                score += 0.1;
            }

            return Math.Min(score, 1.0);
        }

        // Check if it's a contract-like exception by type name
        if (exception.GetType().Name.Contains("Contract"))
        {
            return 0.6;
        }

        // Generic exception with contract-related message
        if (exception.Message.Contains("Precondition") ||
            exception.Message.Contains("Postcondition") ||
            exception.Message.Contains("Contract"))
        {
            return 0.5;
        }

        // Some other exception was thrown
        return ScoreGenericException(exception);
    }

    /// <summary>
    /// Scores a C# exception for error quality.
    /// ArgumentException with good message = 0.4-0.5
    /// Generic exception = 0.1-0.3
    /// Runtime exceptions (DivideByZero) = 0.2
    /// </summary>
    private static double ScoreCSharpException(Exception exception)
    {
        // ArgumentException with meaningful message
        if (exception is ArgumentException argEx)
        {
            var score = 0.3; // Base score

            // Add points for having parameter name
            if (!string.IsNullOrEmpty(argEx.ParamName))
            {
                score += 0.1;
            }

            // Add points for descriptive message
            if (!string.IsNullOrEmpty(argEx.Message) && argEx.Message.Length > 10)
            {
                score += 0.1;
            }

            return score;
        }

        // ArgumentOutOfRangeException is slightly better
        if (exception is ArgumentOutOfRangeException)
        {
            return 0.45;
        }

        // InvalidOperationException with message
        if (exception is InvalidOperationException)
        {
            return 0.35;
        }

        return ScoreGenericException(exception);
    }

    /// <summary>
    /// Scores generic exceptions based on type and message.
    /// </summary>
    private static double ScoreGenericException(Exception exception)
    {
        // Runtime exceptions that indicate the issue was caught
        if (exception is DivideByZeroException)
        {
            return 0.2; // Caught by runtime, not by code
        }

        if (exception is OverflowException)
        {
            return 0.25; // Caught by runtime with checked context
        }

        if (exception is IndexOutOfRangeException)
        {
            return 0.2; // Caught by runtime
        }

        if (exception is NullReferenceException)
        {
            return 0.1; // Worst - indicates missing validation
        }

        // Any other exception with a message
        if (!string.IsNullOrEmpty(exception.Message))
        {
            return 0.15;
        }

        // Bare exception
        return 0.1;
    }

    /// <summary>
    /// Calculates the overall safety score for a test case.
    /// </summary>
    /// <param name="violationDetected">Whether a violation/exception was detected.</param>
    /// <param name="expectedViolation">Whether a violation was expected.</param>
    /// <param name="errorQualityScore">The error quality score (0-1).</param>
    /// <param name="normalCorrectness">Whether normal test cases passed (0-1).</param>
    /// <returns>Weighted safety score.</returns>
    public static double CalculateSafetyScore(
        bool violationDetected,
        bool expectedViolation,
        double errorQualityScore,
        double normalCorrectness)
    {
        // Weights from the plan
        const double violationWeight = 0.40;
        const double qualityWeight = 0.30;
        const double correctnessWeight = 0.30;

        // Violation detection score
        var violationScore = (violationDetected == expectedViolation) ? 1.0 : 0.0;

        // Calculate weighted score
        return (violationScore * violationWeight) +
               (errorQualityScore * qualityWeight) +
               (normalCorrectness * correctnessWeight);
    }

    /// <summary>
    /// Analyzes exception metadata for detailed reporting.
    /// </summary>
    public static SafetyExceptionAnalysis AnalyzeException(Exception? exception, string language)
    {
        if (exception == null)
        {
            return new SafetyExceptionAnalysis
            {
                ExceptionDetected = false,
                ExceptionType = null,
                ExceptionMessage = null,
                HasLocation = false,
                HasCondition = false,
                HasParameterName = false,
                ErrorQualityScore = 0.0,
                QualityLevel = ErrorQualityLevel.Fail
            };
        }

        var errorQualityScore = ScoreErrorQuality(exception, language, expectedViolation: true);
        var qualityLevel = GetQualityLevel(errorQualityScore);

        if (exception is ContractViolationException cve)
        {
            return new SafetyExceptionAnalysis
            {
                ExceptionDetected = true,
                ExceptionType = exception.GetType().Name,
                ExceptionMessage = exception.Message,
                HasLocation = cve.Line > 0,
                HasCondition = !string.IsNullOrEmpty(cve.Condition),
                HasParameterName = false,
                FunctionId = cve.FunctionId,
                Line = cve.Line,
                Column = cve.Column,
                Condition = cve.Condition,
                ContractKind = cve.Kind.ToString(),
                ErrorQualityScore = errorQualityScore,
                QualityLevel = qualityLevel
            };
        }
        else if (exception is ArgumentException argEx)
        {
            return new SafetyExceptionAnalysis
            {
                ExceptionDetected = true,
                ExceptionType = exception.GetType().Name,
                ExceptionMessage = exception.Message,
                HasLocation = false,
                HasCondition = false,
                HasParameterName = !string.IsNullOrEmpty(argEx.ParamName),
                ParameterName = argEx.ParamName,
                ErrorQualityScore = errorQualityScore,
                QualityLevel = qualityLevel
            };
        }
        else
        {
            return new SafetyExceptionAnalysis
            {
                ExceptionDetected = true,
                ExceptionType = exception.GetType().Name,
                ExceptionMessage = exception.Message,
                HasLocation = false,
                HasCondition = false,
                HasParameterName = false,
                ErrorQualityScore = errorQualityScore,
                QualityLevel = qualityLevel
            };
        }
    }

    /// <summary>
    /// Maps a numeric quality score to a quality level.
    /// </summary>
    private static ErrorQualityLevel GetQualityLevel(double score)
    {
        return score switch
        {
            >= 0.9 => ErrorQualityLevel.Excellent,
            >= 0.6 => ErrorQualityLevel.Good,
            >= 0.3 => ErrorQualityLevel.Adequate,
            >= 0.1 => ErrorQualityLevel.Poor,
            _ => ErrorQualityLevel.Fail
        };
    }
}

/// <summary>
/// Detailed analysis of an exception for safety benchmarking.
/// </summary>
public record SafetyExceptionAnalysis
{
    /// <summary>
    /// Whether an exception was detected.
    /// </summary>
    public bool ExceptionDetected { get; init; }

    /// <summary>
    /// Type name of the exception.
    /// </summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Exception message.
    /// </summary>
    public string? ExceptionMessage { get; init; }

    /// <summary>
    /// Whether location information is available.
    /// </summary>
    public bool HasLocation { get; init; }

    /// <summary>
    /// Whether condition text is available.
    /// </summary>
    public bool HasCondition { get; init; }

    /// <summary>
    /// Whether parameter name is available (for ArgumentException).
    /// </summary>
    public bool HasParameterName { get; init; }

    /// <summary>
    /// Function ID (Calor only).
    /// </summary>
    public string? FunctionId { get; init; }

    /// <summary>
    /// Source line number (Calor only).
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// Source column number (Calor only).
    /// </summary>
    public int Column { get; init; }

    /// <summary>
    /// Contract condition text (Calor only).
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Contract kind (Requires/Ensures/Invariant).
    /// </summary>
    public string? ContractKind { get; init; }

    /// <summary>
    /// Parameter name (C# ArgumentException only).
    /// </summary>
    public string? ParameterName { get; init; }

    /// <summary>
    /// Calculated error quality score (0-1).
    /// </summary>
    public double ErrorQualityScore { get; init; }

    /// <summary>
    /// Qualitative error quality level.
    /// </summary>
    public ErrorQualityLevel QualityLevel { get; init; }
}

/// <summary>
/// Qualitative levels for error quality.
/// </summary>
public enum ErrorQualityLevel
{
    /// <summary>
    /// Excellent: Specific exception type + precise location + condition shown (score >= 0.9)
    /// </summary>
    Excellent,

    /// <summary>
    /// Good: Specific exception type + meaningful message (score >= 0.6)
    /// </summary>
    Good,

    /// <summary>
    /// Adequate: Any exception thrown with some message (score >= 0.3)
    /// </summary>
    Adequate,

    /// <summary>
    /// Poor: Exception thrown but generic/unhelpful (score >= 0.1)
    /// </summary>
    Poor,

    /// <summary>
    /// Fail: No exception (silent failure or wrong result) (score < 0.1)
    /// </summary>
    Fail
}
