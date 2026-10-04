using System.Text.Json.Serialization;
using Calor.Compiler.Ast;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

internal enum ContractPosition
{
    Precondition,
    Postcondition,
    Obligation
}

internal enum CasePolarity
{
    Provable,
    Refutable
}

internal enum RuntimeVerdict
{
    Completed,
    GuardFailed,
    RuntimeError
}

internal sealed record DifferentialForm(
    string Id,
    string Category,
    bool MatrixApplicable,
    string? ExclusionReason,
    IReadOnlyList<string> AllowedAssumptions,
    Func<CasePolarity, FormExpression> Build,
    Func<ExpressionNode, bool> ContainsTarget);

internal sealed record FormExpression(
    ExpressionNode Condition,
    IReadOnlyList<ParameterNode> Parameters);

internal sealed record DifferentialCase(
    string Id,
    string FormId,
    string FormCategory,
    ContractPosition Position,
    int NestingDepth,
    CasePolarity Polarity,
    FunctionNode Function,
    string? ProofId,
    IReadOnlyList<string> AllowedAssumptions);

internal sealed record CaseResult(
    string Id,
    string FormId,
    string Category,
    string Position,
    int NestingDepth,
    string Polarity,
    string SolverStatus,
    string RuntimeVerdict,
    bool GuardForced,
    bool ElidedWhenEnabled,
    bool SolverHandled,
    bool Mismatch,
    string? Detail);

internal sealed record FormCoverage(
    string Id,
    string Category,
    bool Applicable,
    string? ExclusionReason,
    IReadOnlyList<string> AllowedAssumptions,
    bool SolverHandled,
    int Cases,
    int ProvableCases,
    int RefutableCases,
    int PreconditionCases,
    int PostconditionCases,
    int ObligationCases,
    bool Elides,
    int Mismatches,
    IReadOnlyDictionary<string, int> Statuses);

internal sealed record FailSafeControl(
    string Scenario,
    string Channel,
    string Status,
    bool GuardRetained,
    string RuntimeVerdict,
    bool Passed);

internal sealed record CoverageMetrics(
    int FormsWhitelisted,
    int FormsApplicable,
    int FormsCovered,
    int FormsEliding,
    double FormCoverageFraction,
    double ElisionCoverageFraction,
    int MatrixCellsRegistered,
    int MatrixCellsApplicable,
    int MatrixCellsCovered,
    double MatrixCoverageFraction,
    int CasesGenerated,
    int Mismatches);

internal sealed record DifferentialReport(
    string SchemaVersion,
    string Registration,
    string WhitelistSha256,
    int MaximumNestingDepth,
    IReadOnlyList<string> Positions,
    IReadOnlyList<string> Polarities,
    CoverageMetrics Coverage,
    IReadOnlyDictionary<string, int> OutcomeCounts,
    IReadOnlyDictionary<string, string> EncodingNotes,
    IReadOnlyList<FormCoverage> Forms,
    IReadOnlyList<FailSafeControl> FailSafeControls,
    IReadOnlyList<CaseResult> Mismatches)
{
    [JsonIgnore]
    public bool Passed =>
        Coverage.Mismatches == 0
        && Coverage.FormsCovered == Coverage.FormsApplicable
        && Coverage.MatrixCellsCovered == Coverage.MatrixCellsApplicable
        && FailSafeControls.All(control => control.Passed);
}
