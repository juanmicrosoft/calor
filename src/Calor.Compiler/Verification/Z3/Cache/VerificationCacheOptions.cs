namespace Calor.Compiler.Verification.Z3.Cache;

/// <summary>
/// Configuration options for the verification cache.
/// </summary>
public sealed class VerificationCacheOptions
{
    /// <summary>
    /// Default maximum cache size in bytes (50 MB).
    /// </summary>
    public const long DefaultMaxCacheSizeBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Whether caching is enabled. Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether to clear the cache before verification. Default: false.
    /// </summary>
    public bool ClearBeforeVerification { get; init; }

    /// <summary>
    /// Project directory for project-level cache location.
    /// If null, user-level cache is used.
    /// </summary>
    public string? ProjectDirectory { get; init; }

    /// <summary>
    /// Custom cache directory override.
    /// If null, default locations are used.
    /// </summary>
    public string? CacheDirectory { get; init; }

    /// <summary>
    /// Maximum cache size in bytes. When exceeded, oldest entries are evicted.
    /// Default: 50 MB. Set to 0 for unlimited.
    /// </summary>
    public long MaxCacheSizeBytes { get; init; } = DefaultMaxCacheSizeBytes;

    /// <summary>
    /// Default cache options with caching enabled.
    /// </summary>
    public static VerificationCacheOptions Default { get; } = new();

    /// <summary>
    /// Cache options with caching disabled.
    /// </summary>
    public static VerificationCacheOptions Disabled { get; } = new() { Enabled = false };

    /// <summary>
    /// Gets the effective cache directory based on options.
    /// Priority: CacheDirectory override > Project-level > User-level
    /// </summary>
    public string GetCacheDirectory()
    {
        if (!string.IsNullOrEmpty(CacheDirectory))
            return CacheDirectory;

        // Try project-level cache first
        if (!string.IsNullOrEmpty(ProjectDirectory))
        {
            var projectCache = Path.Combine(ProjectDirectory, ".calor", "verification-cache");
            return projectCache;
        }

        // Fall back to user-level cache
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".calor", "cache", "z3", "v1");
    }
}
