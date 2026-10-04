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
        var userHome = UserHome.Resolve();
        return Path.Combine(userHome, ".calor", "cache", "z3", "v1");
    }
}

/// <summary>
/// The root of Calor's user-level state (<c>~/.calor</c>: the default verification cache and the
/// user effect manifests).
/// </summary>
/// <remarks>
/// On Windows, <see cref="System.Environment.SpecialFolder.UserProfile"/> comes from the shell's
/// known folder for the account and ignores the <c>USERPROFILE</c> variable, so a process cannot
/// be given its own user-level cache. NuGet reads the variable for the same reason, and so does
/// this resolver; when <c>USERPROFILE</c> is unset, empty, or not a fully qualified path, it
/// falls back to the known folder. On Linux and macOS it is
/// <see cref="System.Environment.SpecialFolder.UserProfile"/> unchanged, which already follows
/// <c>HOME</c>. In an ordinary Windows session the variable and the known folder are the same
/// directory, so only a process whose <c>USERPROFILE</c> was redirected (a test host isolating
/// its cache, #1135) sees a different root.
/// <para>
/// This file depends only on the BCL: the #1421 determinism protocol's environment check compiles
/// it as-is to confirm that the root follows the isolated home on every runner.
/// </para>
/// </remarks>
public static class UserHome
{
    /// <summary>The user-level root, or an empty string when none can be determined.</summary>
    public static string Resolve()
    {
        if (System.OperatingSystem.IsWindows())
        {
            var profile = System.Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile) && System.IO.Path.IsPathFullyQualified(profile))
                return profile;
        }

        return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
    }
}
