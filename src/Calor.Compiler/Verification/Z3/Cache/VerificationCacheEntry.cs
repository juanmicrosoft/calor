namespace Calor.Compiler.Verification.Z3.Cache;

/// <summary>
/// Represents a cached verification result entry.
/// </summary>
public sealed class VerificationCacheEntry
{
    /// <summary>
    /// Current cache format version.
    /// Increment this when the cache entry structure changes.
    /// </summary>
    public const string CurrentFormatVersion = "1.1";

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
        return new ContractVerificationResult(
            Status,
            CounterexampleDescription,
            TimeSpan.FromMilliseconds(OriginalDurationMs));
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
