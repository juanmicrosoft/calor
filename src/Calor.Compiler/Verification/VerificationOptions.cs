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
    /// Default verification options.
    /// </summary>
    public static VerificationOptions Default { get; } = new();
}
