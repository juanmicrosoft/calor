using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Configuration options for contract verification.
/// </summary>
public sealed class VerificationOptions
{
    /// <summary>
    /// Default timeout per function in milliseconds.
    /// </summary>
    public const uint DefaultTimeoutMs = 5000;

    /// <summary>
    /// Timeout per function in milliseconds.
    /// Default: 5000ms (5 seconds).
    /// </summary>
    public uint TimeoutMs { get; init; } = DefaultTimeoutMs;

    /// <summary>
    /// Whether to emit verbose diagnostic output during verification.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Mirrors <see cref="Calor.Compiler.CompilationOptions"/>' elision setting (default
    /// on since v0.15) so verbose diagnostics can state the check's actual disposition
    /// (kept vs elided) truthfully. Verification itself never acts on this — only the
    /// emitter does.
    /// </summary>
    public bool ElideProvenGuards { get; init; } = true;

    /// <summary>
    /// Cache options for verification results.
    /// Default: caching enabled.
    /// </summary>
    public VerificationCacheOptions CacheOptions { get; init; } = VerificationCacheOptions.Default;

    /// <summary>
    /// Cancellation token for aborting verification between contracts/functions.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Default verification options.
    /// </summary>
    public static VerificationOptions Default { get; } = new();
}
