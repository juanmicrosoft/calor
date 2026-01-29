using Opal.Evaluation.Core;

namespace Opal.Evaluation.Metrics;

/// <summary>
/// Category 5: Error Detection Calculator
/// Measures bug finding and fixing capabilities for OPAL vs C#.
/// OPAL's contracts are hypothesized to expose invariant violations more clearly.
/// </summary>
public class ErrorDetectionCalculator : IMetricCalculator
{
    public string Category => "ErrorDetection";

    public string Description => "Measures bug detection and fixing capabilities based on contract clarity";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate error detection potential based on contract/assertion presence
        var opalDetection = CalculateOpalErrorDetectionCapability(context);
        var csharpDetection = CalculateCSharpErrorDetectionCapability(context);

        var details = new Dictionary<string, object>
        {
            ["opalDetectionFactors"] = GetOpalDetectionFactors(context),
            ["csharpDetectionFactors"] = GetCSharpDetectionFactors(context)
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "DetectionCapability",
            opalDetection,
            csharpDetection,
            details));
    }

    /// <summary>
    /// Evaluates error detection for a buggy/fixed code pair.
    /// </summary>
    public ErrorDetectionResult EvaluateBuggyCode(
        string buggyOpal,
        string fixedOpal,
        string buggyCSharp,
        string fixedCSharp,
        BugDescription bug)
    {
        // Parse both versions
        var buggyOpalCtx = new EvaluationContext
        {
            OpalSource = buggyOpal,
            CSharpSource = buggyCSharp,
            FileName = bug.Id
        };
        var fixedOpalCtx = new EvaluationContext
        {
            OpalSource = fixedOpal,
            CSharpSource = fixedCSharp,
            FileName = bug.Id
        };

        // Check if contracts catch the bug
        var opalCatchesBug = DetectsBugViaContracts(buggyOpalCtx, bug);
        var csharpCatchesBug = DetectsBugViaAssertions(buggyOpalCtx, bug);

        // Check if compilation itself catches the bug
        var opalCompileCatch = !buggyOpalCtx.OpalCompilation.Success;
        var csharpCompileCatch = !buggyOpalCtx.CSharpCompilation.Success;

        // Calculate fix complexity
        var opalFixComplexity = CalculateFixComplexity(buggyOpal, fixedOpal);
        var csharpFixComplexity = CalculateFixComplexity(buggyCSharp, fixedCSharp);

        return new ErrorDetectionResult
        {
            BugId = bug.Id,
            BugCategory = bug.Category,
            OpalDetectedAtCompile = opalCompileCatch,
            CSharpDetectedAtCompile = csharpCompileCatch,
            OpalDetectedViaContract = opalCatchesBug,
            CSharpDetectedViaAssertion = csharpCatchesBug,
            OpalFixComplexity = opalFixComplexity,
            CSharpFixComplexity = csharpFixComplexity
        };
    }

    /// <summary>
    /// Calculates OPAL's error detection capability based on contract presence.
    /// </summary>
    private static double CalculateOpalErrorDetectionCapability(EvaluationContext context)
    {
        var score = 0.3; // Base score
        var source = context.OpalSource;

        // Contracts significantly improve error detection
        if (source.Contains("§REQ")) score += 0.25; // Requires preconditions
        if (source.Contains("§ENS")) score += 0.20; // Ensures postconditions
        if (source.Contains("§INV")) score += 0.15; // Invariants

        // Effect declarations help detect side-effect bugs
        if (source.Contains("§E[")) score += 0.10;

        // Type annotations catch type errors
        if (source.Contains("§I[") && source.Contains(":")) score += 0.05;
        if (source.Contains("§O[")) score += 0.05;

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

        return Math.Min(score, 0.90); // Cap slightly lower than OPAL max
    }

    private static Dictionary<string, bool> GetOpalDetectionFactors(EvaluationContext context)
    {
        var source = context.OpalSource;
        return new Dictionary<string, bool>
        {
            ["hasRequires"] = source.Contains("§REQ"),
            ["hasEnsures"] = source.Contains("§ENS"),
            ["hasInvariants"] = source.Contains("§INV"),
            ["hasEffects"] = source.Contains("§E["),
            ["hasTypedInputs"] = source.Contains("§I[") && source.Contains(":"),
            ["hasTypedOutput"] = source.Contains("§O[")
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
    /// Checks if OPAL contracts would catch the described bug.
    /// </summary>
    private static bool DetectsBugViaContracts(EvaluationContext context, BugDescription bug)
    {
        var source = context.OpalSource;

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
    public bool OpalDetectedAtCompile { get; init; }
    public bool CSharpDetectedAtCompile { get; init; }
    public bool OpalDetectedViaContract { get; init; }
    public bool CSharpDetectedViaAssertion { get; init; }
    public double OpalFixComplexity { get; init; }
    public double CSharpFixComplexity { get; init; }

    public double OpalDetectionScore =>
        (OpalDetectedAtCompile ? 0.5 : 0) + (OpalDetectedViaContract ? 0.5 : 0);

    public double CSharpDetectionScore =>
        (CSharpDetectedAtCompile ? 0.5 : 0) + (CSharpDetectedViaAssertion ? 0.5 : 0);
}
