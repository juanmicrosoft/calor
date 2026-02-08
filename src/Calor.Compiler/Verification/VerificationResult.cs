namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Status of contract verification using Z3 SMT solver.
/// </summary>
public enum ContractVerificationStatus
{
    /// <summary>
    /// Contract was proven to always hold. Runtime check can be elided.
    /// </summary>
    Proven,

    /// <summary>
    /// Verification was inconclusive (timeout or too complex). Keep runtime check.
    /// </summary>
    Unproven,

    /// <summary>
    /// Contract was proven to be violable. Keep runtime check, emit warning.
    /// </summary>
    Disproven,

    /// <summary>
    /// Contract contains unsupported constructs. Keep runtime check silently.
    /// </summary>
    Unsupported,

    /// <summary>
    /// Verification was skipped (Z3 unavailable). Keep runtime check.
    /// </summary>
    Skipped
}

/// <summary>
/// Result of verifying a single contract.
/// </summary>
/// <param name="Status">The verification status.</param>
/// <param name="CounterexampleDescription">Description of counterexample if Disproven.</param>
/// <param name="Duration">Time taken to verify.</param>
public record ContractVerificationResult(
    ContractVerificationStatus Status,
    string? CounterexampleDescription = null,
    TimeSpan? Duration = null);

/// <summary>
/// Result of verifying all contracts in a function.
/// </summary>
/// <param name="FunctionId">The function's unique identifier.</param>
/// <param name="FunctionName">The function's name.</param>
/// <param name="PreconditionResults">Results for each precondition, keyed by condition index.</param>
/// <param name="PostconditionResults">Results for each postcondition, keyed by condition index.</param>
public record FunctionVerificationResult(
    string FunctionId,
    string FunctionName,
    IReadOnlyList<ContractVerificationResult> PreconditionResults,
    IReadOnlyList<ContractVerificationResult> PostconditionResults);

/// <summary>
/// Result of verifying all contracts in a module.
/// </summary>
/// <param name="Functions">Verification results for each function.</param>
public record ModuleVerificationResult(
    IReadOnlyList<FunctionVerificationResult> Functions)
{
    /// <summary>
    /// Gets the verification result for a specific function by ID.
    /// </summary>
    public FunctionVerificationResult? GetFunctionResult(string functionId)
    {
        return Functions.FirstOrDefault(f => f.FunctionId == functionId);
    }

    /// <summary>
    /// Gets the total count of contracts by status.
    /// </summary>
    public int CountByStatus(ContractVerificationStatus status)
    {
        return Functions.Sum(f =>
            f.PreconditionResults.Count(r => r.Status == status) +
            f.PostconditionResults.Count(r => r.Status == status));
    }

    /// <summary>
    /// Gets a summary of verification results.
    /// </summary>
    public VerificationSummary GetSummary()
    {
        return new VerificationSummary(
            Proven: CountByStatus(ContractVerificationStatus.Proven),
            Unproven: CountByStatus(ContractVerificationStatus.Unproven),
            Disproven: CountByStatus(ContractVerificationStatus.Disproven),
            Unsupported: CountByStatus(ContractVerificationStatus.Unsupported),
            Skipped: CountByStatus(ContractVerificationStatus.Skipped));
    }
}

/// <summary>
/// Summary statistics for verification results.
/// </summary>
public record VerificationSummary(
    int Proven,
    int Unproven,
    int Disproven,
    int Unsupported,
    int Skipped)
{
    /// <summary>
    /// Total number of contracts verified.
    /// </summary>
    public int Total => Proven + Unproven + Disproven + Unsupported + Skipped;
}
