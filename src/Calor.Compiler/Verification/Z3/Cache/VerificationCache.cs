using System.Text.Json;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Verification.Z3.Cache;

/// <summary>
/// Caches Z3 verification results to avoid redundant SMT solver invocations.
/// Thread-safe for concurrent access within a process.
/// </summary>
public sealed class VerificationCache : IDisposable
{
    private readonly VerificationCacheOptions _options;
    private readonly ContractHasher _hasher;
    private readonly string _cacheDirectory;
    private readonly string? _z3Version;
    private readonly object _lock = new();
    private bool _disposed;

    // Statistics
    private int _hits;
    private int _misses;
    private int _writes;
    private int _errors;
    private int _evictions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public VerificationCache(VerificationCacheOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hasher = new ContractHasher();
        _cacheDirectory = options.GetCacheDirectory();
        _z3Version = Z3ContextFactory.GetZ3Version();

        if (options.ClearBeforeVerification)
        {
            Clear();
        }
    }

    /// <summary>
    /// Whether caching is enabled.
    /// </summary>
    public bool IsEnabled => _options.Enabled;

    /// <summary>
    /// Tries to get a cached precondition result.
    /// </summary>
    public bool TryGetPreconditionResult(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        RequiresNode precondition,
        out ContractVerificationResult? result)
    {
        result = null;

        if (!_options.Enabled)
            return false;

        var hash = _hasher.HashPrecondition(parameters, precondition);
        return TryGetCachedResult(hash, out result);
    }

    /// <summary>
    /// Tries to get a cached postcondition result.
    /// </summary>
    public bool TryGetPostconditionResult(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        string? outputType,
        IReadOnlyList<RequiresNode> preconditions,
        EnsuresNode postcondition,
        out ContractVerificationResult? result)
    {
        result = null;

        if (!_options.Enabled)
            return false;

        var hash = _hasher.HashPostcondition(parameters, outputType, preconditions, postcondition);
        return TryGetCachedResult(hash, out result);
    }

    /// <summary>
    /// Caches a precondition result.
    /// </summary>
    public void CachePreconditionResult(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        RequiresNode precondition,
        ContractVerificationResult result)
    {
        if (!_options.Enabled)
            return;

        // Don't cache Unsupported or Skipped results - they may change with library updates
        if (result.Status == ContractVerificationStatus.Unsupported ||
            result.Status == ContractVerificationStatus.Skipped)
            return;

        var hash = _hasher.HashPrecondition(parameters, precondition);
        CacheResult(hash, result);
    }

    /// <summary>
    /// Caches a postcondition result.
    /// </summary>
    public void CachePostconditionResult(
        IReadOnlyList<(string Name, string TypeName)> parameters,
        string? outputType,
        IReadOnlyList<RequiresNode> preconditions,
        EnsuresNode postcondition,
        ContractVerificationResult result)
    {
        if (!_options.Enabled)
            return;

        // Don't cache Unsupported or Skipped results
        if (result.Status == ContractVerificationStatus.Unsupported ||
            result.Status == ContractVerificationStatus.Skipped)
            return;

        var hash = _hasher.HashPostcondition(parameters, outputType, preconditions, postcondition);
        CacheResult(hash, result);
    }

    /// <summary>
    /// Clears all cached verification results.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    Directory.Delete(_cacheDirectory, recursive: true);
                }
            }
            catch
            {
                // Ignore errors during cache clear
            }
        }
    }

    /// <summary>
    /// Gets statistics about cache usage.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics(
            Interlocked.CompareExchange(ref _hits, 0, 0),
            Interlocked.CompareExchange(ref _misses, 0, 0),
            Interlocked.CompareExchange(ref _writes, 0, 0),
            Interlocked.CompareExchange(ref _errors, 0, 0),
            Interlocked.CompareExchange(ref _evictions, 0, 0));
    }

    private bool TryGetCachedResult(string hash, out ContractVerificationResult? result)
    {
        result = null;

        try
        {
            var filePath = GetCacheFilePath(hash);

            if (!File.Exists(filePath))
            {
                Interlocked.Increment(ref _misses);
                return false;
            }

            string json;
            lock (_lock)
            {
                if (!File.Exists(filePath))
                {
                    Interlocked.Increment(ref _misses);
                    return false;
                }
                json = File.ReadAllText(filePath);
            }

            var entry = JsonSerializer.Deserialize<VerificationCacheEntry>(json, JsonOptions);

            if (entry == null || entry.ContractHash != hash || !entry.IsValidFor(_z3Version))
            {
                // Cache entry is invalid, version mismatch, or Z3 version changed
                Interlocked.Increment(ref _misses);
                TryDeleteFile(filePath);
                return false;
            }

            // Update file access time for LRU tracking
            try
            {
                File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow);
            }
            catch
            {
                // Ignore - not critical for functionality
            }

            result = entry.ToResult();
            Interlocked.Increment(ref _hits);
            return true;
        }
        catch
        {
            Interlocked.Increment(ref _errors);
            return false;
        }
    }

    private void CacheResult(string hash, ContractVerificationResult result)
    {
        try
        {
            var filePath = GetCacheFilePath(hash);
            var directory = Path.GetDirectoryName(filePath)!;

            lock (_lock)
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Enforce cache size limit before writing
                EnforceCacheSizeLimit();

                var entry = VerificationCacheEntry.FromResult(result, hash, _z3Version);
                var json = JsonSerializer.Serialize(entry, JsonOptions);

                // Write to a temp file first, then move for atomicity
                var tempPath = filePath + $".{Environment.ProcessId}.tmp";
                try
                {
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                finally
                {
                    // Clean up temp file if it still exists
                    TryDeleteFile(tempPath);
                }
            }

            Interlocked.Increment(ref _writes);
        }
        catch
        {
            Interlocked.Increment(ref _errors);
        }
    }

    /// <summary>
    /// Enforces the maximum cache size by evicting oldest entries (LRU).
    /// Must be called while holding the lock.
    /// </summary>
    private void EnforceCacheSizeLimit()
    {
        if (_options.MaxCacheSizeBytes <= 0)
            return; // No limit

        if (!Directory.Exists(_cacheDirectory))
            return;

        try
        {
            var files = Directory.GetFiles(_cacheDirectory, "*.json", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList();

            var totalSize = files.Sum(f => f.Length);

            if (totalSize <= _options.MaxCacheSizeBytes)
                return; // Under limit

            // Sort by last access time (oldest first) for LRU eviction
            var sortedFiles = files
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            // Evict oldest files until we're under 80% of the limit (to avoid frequent evictions)
            var targetSize = (long)(_options.MaxCacheSizeBytes * 0.8);
            foreach (var file in sortedFiles)
            {
                if (totalSize <= targetSize)
                    break;

                try
                {
                    var fileSize = file.Length;
                    file.Delete();
                    totalSize -= fileSize;
                    Interlocked.Increment(ref _evictions);
                }
                catch
                {
                    // Ignore individual file deletion errors
                }
            }
        }
        catch
        {
            // Ignore errors during eviction - cache will still work, just might grow larger
        }
    }

    private string GetCacheFilePath(string hash)
    {
        // Use first 2 characters as subdirectory for better filesystem performance
        var prefix = hash[..2];
        return Path.Combine(_cacheDirectory, prefix, hash + ".json");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore deletion errors
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}

/// <summary>
/// Statistics about verification cache usage.
/// </summary>
public record CacheStatistics(int Hits, int Misses, int Writes, int Errors, int Evictions)
{
    /// <summary>
    /// Total cache lookups.
    /// </summary>
    public int TotalLookups => Hits + Misses;

    /// <summary>
    /// Cache hit rate as a percentage.
    /// </summary>
    public double HitRate => TotalLookups > 0 ? (double)Hits / TotalLookups * 100 : 0;
}
