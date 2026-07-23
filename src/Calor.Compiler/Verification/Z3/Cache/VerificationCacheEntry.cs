namespace Calor.Compiler.Verification.Z3.Cache;

/// <summary>
/// Represents a cached verification result entry.
/// </summary>
public sealed class VerificationCacheEntry
{
    /// <summary>
    /// Current cache format version.
    /// Increment this when the cache entry structure changes.
    /// 1.2: added choke-point proof outcome fields (ProofStatus, ProofReason,
    /// CounterexampleBindings) so cache hits keep the five-status vocabulary
    /// and structured counterexamples (loop plan D1.2).
    /// </summary>
    public const string CurrentFormatVersion = "1.2";

    /// <summary>
    /// Cache format version for invalidation on format changes.
    /// </summary>
    public string Version { get; set; } = CurrentFormatVersion;

    /// <summary>
    /// Z3 library version that produced this result.
    /// Results are invalidated when Z3 version changes.
    /// </summary>
    public string? Z3Version { get; set; }

    /// <summary>
    /// The verification status.
    /// </summary>
    public ContractVerificationStatus Status { get; set; }

    /// <summary>
    /// Description of counterexample if Disproven.
    /// </summary>
    public string? CounterexampleDescription { get; set; }

    /// <summary>
    /// Choke-point proof status wire name (proven|refuted|unknown|timeout|unsupported).
    /// </summary>
    public string? ProofStatus { get; set; }

    /// <summary>
    /// Choke-point outcome reason detail (unsupported diagnosis, solver error, unknown-reason).
    /// </summary>
    public string? ProofReason { get; set; }

    /// <summary>
    /// Structured counterexample bindings when the contract was refuted with a model.
    /// </summary>
    public List<Verification.CounterexampleBinding>? CounterexampleBindings { get; set; }

    /// <summary>
    /// Original verification duration in milliseconds.
    /// </summary>
    public double OriginalDurationMs { get; set; }

    /// <summary>
    /// When this cache entry was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// SHA256 hash of the contract expression for integrity verification.
    /// </summary>
    public string ContractHash { get; set; } = "";

    /// <summary>
    /// Creates a ContractVerificationResult from this cache entry.
    /// </summary>
    public ContractVerificationResult ToResult()
    {
        var outcome = ProofStatus != null
            ? Verification.ProofOutcome.Rehydrate(ProofStatus, CounterexampleBindings, ProofReason)
            : null;

        return new ContractVerificationResult(
            Status,
            CounterexampleDescription: CounterexampleDescription,
            Warnings: null, // Warnings are not cached
            Duration: TimeSpan.FromMilliseconds(OriginalDurationMs),
            Outcome: outcome);
    }

    /// <summary>
    /// Creates a cache entry from a verification result.
    /// </summary>
    public static VerificationCacheEntry FromResult(
        ContractVerificationResult result,
        string contractHash,
        string? z3Version)
    {
        return new VerificationCacheEntry
        {
            Version = CurrentFormatVersion,
            Z3Version = z3Version,
            Status = result.Status,
            CounterexampleDescription = result.CounterexampleDescription,
            ProofStatus = result.Outcome?.StatusName,
            ProofReason = result.Outcome?.Reason,
            CounterexampleBindings = result.Outcome?.Counterexample?.Bindings.ToList(),
            OriginalDurationMs = result.Duration?.TotalMilliseconds ?? 0,
            CreatedAt = DateTime.UtcNow,
            ContractHash = contractHash
        };
    }

    /// <summary>
    /// Checks if this cache entry is valid for the given Z3 version.
    /// </summary>
    public bool IsValidFor(string? currentZ3Version)
    {
        // Format version must match
        if (Version != CurrentFormatVersion)
            return false;

        // Z3 version must match (both null is OK for tests)
        if (Z3Version != currentZ3Version)
            return false;

        return true;
    }
}
