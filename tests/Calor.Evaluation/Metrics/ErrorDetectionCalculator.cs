using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 5: Error Detection Calculator
/// Measures bug finding and fixing capabilities for Calor vs C#.
/// Calor's contracts are hypothesized to expose invariant violations more clearly.
/// </summary>
public class ErrorDetectionCalculator : IMetricCalculator
{
    public string Category => "ErrorDetection";

    public string Description => "Measures bug detection and fixing capabilities based on contract clarity";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate error detection potential based on contract/assertion presence
        var calorDetection = CalculateCalorErrorDetectionCapability(context);
        var csharpDetection = CalculateCSharpErrorDetectionCapability(context);

        var details = new Dictionary<string, object>
        {
            ["calorDetectionFactors"] = GetCalorDetectionFactors(context),
            ["csharpDetectionFactors"] = GetCSharpDetectionFactors(context)
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "DetectionCapability",
            calorDetection,
            csharpDetection,
            details));
    }

    /// <summary>
    /// Evaluates error detection for a buggy/fixed code pair.
    /// </summary>
    public ErrorDetectionResult EvaluateBuggyCode(
        string buggyCalor,
        string fixedCalor,
        string buggyCSharp,
        string fixedCSharp,
        BugDescription bug)
    {
        // Parse both versions
        var buggyCalorCtx = new EvaluationContext
        {
            CalorSource = buggyCalor,
            CSharpSource = buggyCSharp,
            FileName = bug.Id
        };
        var fixedCalorCtx = new EvaluationContext
        {
            CalorSource = fixedCalor,
            CSharpSource = fixedCSharp,
            FileName = bug.Id
        };

        // Check if contracts catch the bug
        var calorCatchesBug = DetectsBugViaContracts(buggyCalorCtx, bug);
        var csharpCatchesBug = DetectsBugViaAssertions(buggyCalorCtx, bug);

        // Check if compilation itself catches the bug
        var calorCompileCatch = !buggyCalorCtx.CalorCompilation.Success;
        var csharpCompileCatch = !buggyCalorCtx.CSharpCompilation.Success;

        // Calculate fix complexity
        var calorFixComplexity = CalculateFixComplexity(buggyCalor, fixedCalor);
        var csharpFixComplexity = CalculateFixComplexity(buggyCSharp, fixedCSharp);

        return new ErrorDetectionResult
        {
            BugId = bug.Id,
            BugCategory = bug.Category,
            CalorDetectedAtCompile = calorCompileCatch,
            CSharpDetectedAtCompile = csharpCompileCatch,
            CalorDetectedViaContract = calorCatchesBug,
            CSharpDetectedViaAssertion = csharpCatchesBug,
            CalorFixComplexity = calorFixComplexity,
            CSharpFixComplexity = csharpFixComplexity
        };
    }

    /// <summary>
    /// Calculates Calor's error detection capability based on contract presence.
    /// </summary>
    private static double CalculateCalorErrorDetectionCapability(EvaluationContext context)
    {
        var score = 0.3; // Base score
        var source = context.CalorSource;

        // Contracts significantly improve error detection
        if (source.Contains("§REQ")) score += 0.25; // Requires preconditions
        if (source.Contains("§ENS")) score += 0.20; // Ensures postconditions
        if (source.Contains("§INV")) score += 0.15; // Invariants

        // Effect declarations help detect side-effect bugs
        if (source.Contains("§E{")) score += 0.10;

        // Type annotations catch type errors
        if (source.Contains("§I{") && source.Contains(":")) score += 0.05;
        if (source.Contains("§O{")) score += 0.05;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates C#'s error detection capability based on patterns.
    /// </summary>
    private static double CalculateCSharpErrorDetectionCapability(EvaluationContext context)
    {
        var score = 0.3; // Base score
        var source = context.CSharpSource;

        // Assertions help but are less integrated
        if (source.Contains("Debug.Assert")) score += 0.15;
        if (source.Contains("Contract.Requires")) score += 0.20;
        if (source.Contains("Contract.Ensures")) score += 0.15;

        // Null checks
        if (source.Contains("?? ") || source.Contains("?.")) score += 0.05;
        if (source.Contains("ArgumentNullException")) score += 0.10;

        // Exception handling
        if (source.Contains("throw new")) score += 0.05;

        // Strong typing
        if (source.Contains("readonly")) score += 0.05;
        if (source.Contains("const")) score += 0.05;

        return Math.Min(score, 0.90); // Cap slightly lower than Calor max
    }

    private static Dictionary<string, bool> GetCalorDetectionFactors(EvaluationContext context)
    {
        var source = context.CalorSource;
        return new Dictionary<string, bool>
        {
            ["hasRequires"] = source.Contains("§REQ"),
            ["hasEnsures"] = source.Contains("§ENS"),
            ["hasInvariants"] = source.Contains("§INV"),
            ["hasEffects"] = source.Contains("§E{"),
            ["hasTypedInputs"] = source.Contains("§I{") && source.Contains(":"),
            ["hasTypedOutput"] = source.Contains("§O{")
        };
    }

    private static Dictionary<string, bool> GetCSharpDetectionFactors(EvaluationContext context)
    {
        var source = context.CSharpSource;
        return new Dictionary<string, bool>
        {
            ["hasDebugAssert"] = source.Contains("Debug.Assert"),
            ["hasContractRequires"] = source.Contains("Contract.Requires"),
            ["hasContractEnsures"] = source.Contains("Contract.Ensures"),
            ["hasNullChecks"] = source.Contains("?? ") || source.Contains("ArgumentNullException"),
            ["hasExceptionHandling"] = source.Contains("throw new"),
            ["hasReadonly"] = source.Contains("readonly")
        };
    }

    /// <summary>
    /// Checks if Calor contracts would catch the described bug.
    /// </summary>
    private static bool DetectsBugViaContracts(EvaluationContext context, BugDescription bug)
    {
        var source = context.CalorSource;

        return bug.Category switch
        {
            "null_reference" => source.Contains("§REQ") && source.Contains("!= null"),
            "bounds_check" => source.Contains("§REQ") && (source.Contains(">=") || source.Contains("<=")),
            "contract_violation" => source.Contains("§REQ") || source.Contains("§ENS"),
            "invariant_violation" => source.Contains("§INV"),
            _ => false
        };
    }

    /// <summary>
    /// Checks if C# assertions would catch the described bug.
    /// </summary>
    private static bool DetectsBugViaAssertions(EvaluationContext context, BugDescription bug)
    {
        var source = context.CSharpSource;

        return bug.Category switch
        {
            "null_reference" => source.Contains("ArgumentNullException") || source.Contains("Debug.Assert"),
            "bounds_check" => source.Contains("ArgumentOutOfRangeException") || source.Contains("Debug.Assert"),
            "contract_violation" => source.Contains("Contract."),
            _ => false
        };
    }

    /// <summary>
    /// Calculates fix complexity based on diff size.
    /// </summary>
    private static double CalculateFixComplexity(string buggy, string fix)
    {
        var buggyLines = buggy.Split('\n').Length;
        var fixLines = fix.Split('\n').Length;

        // Simple Levenshtein-like approximation
        var lineDiff = Math.Abs(buggyLines - fixLines);
        var charDiff = Math.Abs(buggy.Length - fix.Length);

        // Normalize to 0-1 scale (lower = simpler fix)
        return Math.Min(1.0, (lineDiff + charDiff / 100.0) / 10.0);
    }
}

/// <summary>
/// Description of a bug for error detection evaluation.
/// </summary>
public class BugDescription
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public string? ExpectedError { get; init; }
}

/// <summary>
/// Result of error detection evaluation for a single bug.
/// </summary>
public class ErrorDetectionResult
{
    public required string BugId { get; init; }
    public required string BugCategory { get; init; }
    public bool CalorDetectedAtCompile { get; init; }
    public bool CSharpDetectedAtCompile { get; init; }
    public bool CalorDetectedViaContract { get; init; }
    public bool CSharpDetectedViaAssertion { get; init; }
    public double CalorFixComplexity { get; init; }
    public double CSharpFixComplexity { get; init; }

    public double CalorDetectionScore =>
        (CalorDetectedAtCompile ? 0.5 : 0) + (CalorDetectedViaContract ? 0.5 : 0);

    public double CSharpDetectionScore =>
        (CSharpDetectedAtCompile ? 0.5 : 0) + (CSharpDetectedViaAssertion ? 0.5 : 0);
}
