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
    /// 1.3: added ProofVacuous (guarantees plan D-G1.3) and body-aware
    /// postcondition keys (D-G1.1) — the bump invalidates every pre-G1 entry,
    /// which is required: old entries were keyed without the function body and
    /// may hold result-unbound verdicts.
    /// 1.4: signed-modulo translation fixed (bvsmod → bvsrem, C# remainder
    /// semantics) — the bump evicts every pre-fix entry, which is required:
    /// a warm cache holding a bvsmod-based Proven verdict would keep eliding
    /// a runtime guard the fixed verifier refutes (G1 re-verification C-cache).
    /// Translation-semantics changes MUST bump this version: the Z3-binary
    /// check does not cover our own translation layer.
    /// </summary>
    public const string CurrentFormatVersion = "1.4";

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
    /// True when the proof is vacuous (unsatisfiable precondition set). Must survive
    /// the cache round-trip: a rehydrated vacuous proof must still never elide checks.
    /// </summary>
    public bool ProofVacuous { get; set; }

    /// <summary>
    /// Named assumption set for Assumed outcomes (guarantees plan D-G2.1). Additive
    /// within format 1.3: no pre-existing 1.3 entry can hold an Assumed status, so
    /// a missing field (older writer) never mispresents a real assumption set.
    /// </summary>
    public List<string>? ProofAssumptions { get; set; }

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
            ? Verification.ProofOutcome.Rehydrate(ProofStatus, CounterexampleBindings, ProofReason, ProofVacuous, ProofAssumptions)
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
            ProofVacuous = result.Outcome?.IsVacuous ?? false,
            ProofAssumptions = result.Outcome is { Assumptions.Count: > 0 } o ? o.Assumptions.ToList() : null,
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
