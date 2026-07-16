using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Effects;

namespace Calor.Compiler.Incremental;

[JsonSerializable(typeof(BuildState))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BuildStateJsonContext : JsonSerializerContext { }

/// <summary>
/// Persisted incremental-build state (<c>.calor-build-state.json</c>). Shared by the
/// MSBuild task (<c>Calor.Tasks.CompileCalor</c>, state next to its output directory)
/// and the CLI compile path (<see cref="CompilationDriver"/>, state next to the
/// generated <c>.g.cs</c> outputs).
/// </summary>
internal sealed class BuildState
{
    // Bump when cache schema changes (e.g., added EffectSummary in 2.0).
    public string FormatVersion { get; set; } = "2.0";
    public string CompilerHash { get; set; } = "";
    public string OptionsHash { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public Dictionary<string, BuildFileEntry> Files { get; set; } = new();
}

internal sealed class BuildFileEntry
{
    public string ContentHash { get; set; } = "";
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }

    /// <summary>
    /// Serializable effect summary for cross-module enforcement on warm builds.
    /// Null for entries from older cache versions or files that failed to compile.
    /// </summary>
    public EffectSummary? EffectSummary { get; set; }

    /// <summary>
    /// Content hash of the generated output (.g.cs) as observed right after the
    /// compile that produced this entry. A warm build only trusts an output whose
    /// current hash matches — a corrupted or manually edited output is a cache
    /// miss, not "Up-to-date". Null (older caches, or the output was never
    /// observed) is also a miss. Populated by the CLI driver; the MSBuild task
    /// does not populate it yet and still trusts bare output existence
    /// (known limitation — follow-up).
    /// </summary>
    public string? OutputContentHash { get; set; }
}

internal static class BuildStateCache
{
    private const string FormatVersion = "2.0";
    private const string CacheFileName = ".calor-build-state.json";
    private const int MaxRetries = 3;
    private const int BaseRetryDelayMs = 50;

    public static string GetCachePath(string outputDirectory)
        => Path.Combine(outputDirectory, CacheFileName);

    public static BuildState? Load(string outputDirectory)
    {
        var cachePath = GetCachePath(outputDirectory);
        return RetryOnIOException(() =>
        {
            if (!File.Exists(cachePath))
                return null;

            var json = File.ReadAllText(cachePath);
            var state = JsonSerializer.Deserialize(json, BuildStateJsonContext.Default.BuildState);
            return state;
        });
    }

    public static void Save(BuildState state, string outputDirectory)
    {
        var cachePath = GetCachePath(outputDirectory);
        var dir = Path.GetDirectoryName(cachePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tmpPath = cachePath + ".tmp";
        RetryOnIOException(() =>
        {
            var json = JsonSerializer.Serialize(state, BuildStateJsonContext.Default.BuildState);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, cachePath, overwrite: true);
        });
    }

    /// <summary>
    /// Deletes the state file (best-effort). Used by <c>--clear-cache</c>.
    /// </summary>
    public static void Delete(string outputDirectory)
    {
        var cachePath = GetCachePath(outputDirectory);
        try
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
        }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    public static string ComputeFileHash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputePathHash(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// MSBuild-task compiler hash: the task assembly plus the Calor.Compiler.dll
    /// deployed next to it.
    /// </summary>
    public static string ComputeCompilerHash(string tasksAssemblyPath)
    {
        var tasksDir = Path.GetDirectoryName(tasksAssemblyPath)!;
        var compilerDllPath = Path.Combine(tasksDir, "Calor.Compiler.dll");
        return ComputeCompilerHash([tasksAssemblyPath, compilerDllPath]);
    }

    /// <summary>
    /// Compiler hash over an explicit set of assembly files (streamed; missing files
    /// are skipped so the hash degrades gracefully rather than throwing).
    /// </summary>
    public static string ComputeCompilerHash(IReadOnlyList<string> assemblyPaths)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // Stream hash to avoid allocating multi-MB arrays
        foreach (var path in assemblyPaths)
        {
            HashFileStreaming(sha, path);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>
    /// CLI compiler hash: the Calor.Compiler assembly itself. Falls back to the
    /// assembly version string when the on-disk location is unavailable
    /// (e.g., single-file publish), so upgrades still invalidate the cache.
    /// </summary>
    public static string ComputeCliCompilerHash()
    {
        var assembly = typeof(BuildStateCache).Assembly;
        var location = assembly.Location;
        if (!string.IsNullOrEmpty(location) && File.Exists(location))
        {
            return ComputeCompilerHash([location]);
        }

        var version = assembly.FullName ?? "calor";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(version)));
    }

    private static void HashFileStreaming(IncrementalHash hash, string filePath)
    {
        if (!File.Exists(filePath))
            return;

        Span<byte> buffer = stackalloc byte[8192];
        using var stream = File.OpenRead(filePath);
        int bytesRead;
        while ((bytesRead = stream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer[..bytesRead]);
        }
    }

    /// <summary>
    /// MSBuild-task options hash (kept bit-compatible with the historical format).
    /// </summary>
    public static string ComputeOptionsHash(bool enforceEffects = true)
        => ComputeOptionsHash($"enforceEffects:{enforceEffects}");

    /// <summary>
    /// Options hash over a caller-supplied canonical token. Diagnostics-affecting
    /// options must be folded into the token: an option flip between builds has to
    /// invalidate cached (skipped) files, otherwise violations that the new option
    /// set would report are silently missed on warm builds. (Options that only
    /// affect presentation — verbose, --format — must be excluded.)
    ///
    /// The set of EffectKind enum values is also folded in: cached EffectSummary entries
    /// reference kinds by name, and a kind added/removed/renamed in a compiler upgrade
    /// must force a cold rebuild so old summaries don't silently drop effects on parse.
    /// </summary>
    public static string ComputeOptionsHash(string optionsToken)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes("options:v1"));
        sha.AppendData(Encoding.UTF8.GetBytes($"|{optionsToken}"));
        sha.AppendData(Encoding.UTF8.GetBytes("|effectkinds:"));
        foreach (var kind in Enum.GetNames(typeof(EffectKind)))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(kind));
            sha.AppendData(Encoding.UTF8.GetBytes(","));
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    public static string ComputeManifestHash(string projectDirectory)
        => ComputeManifestHash([projectDirectory]);

    /// <summary>
    /// Manifest hash across one or more project directories (the CLI can compile
    /// files from several directories, each with its own effect manifests).
    /// </summary>
    public static string ComputeManifestHash(IReadOnlyList<string> projectDirectories)
    {
        var manifestFiles = new List<string>();
        var seen = new HashSet<string>(GetPathComparer());
        foreach (var dir in projectDirectories)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.GetFiles(dir, "*.calor-effects.json", SearchOption.TopDirectoryOnly))
            {
                if (seen.Add(Path.GetFullPath(file)))
                    manifestFiles.Add(file);
            }
        }

        if (manifestFiles.Count == 0)
            return "";

        manifestFiles.Sort(StringComparer.OrdinalIgnoreCase);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var manifestFile in manifestFiles)
        {
            sha.AppendData(File.ReadAllBytes(manifestFile));
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    public static bool IsFileUpToDate(BuildFileEntry? cached, string filePath)
    {
        if (cached == null)
            return false;

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            return false;

        // Level 1: stat gate — (mtime, size)
        if (fileInfo.LastWriteTimeUtc == cached.LastModified && fileInfo.Length == cached.FileSize)
            return true;

        // Level 2: content hash
        var currentHash = ComputeFileHash(filePath);
        return currentHash == cached.ContentHash;
    }

    public static BuildFileEntry CreateFileEntry(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return new BuildFileEntry
        {
            ContentHash = ComputeFileHash(filePath),
            LastModified = fileInfo.LastWriteTimeUtc,
            FileSize = fileInfo.Length
        };
    }

    /// <summary>
    /// File metadata snapshot taken eagerly (unlike <see cref="FileInfo"/>, whose
    /// stat is lazy and would otherwise happen after the source read).
    /// </summary>
    public readonly record struct FileStat(DateTime LastWriteTimeUtc, long Length);

    /// <summary>Stats <paramref name="filePath"/> now and returns the snapshot.</summary>
    public static FileStat StatFile(string filePath)
    {
        var info = new FileInfo(filePath);
        return new FileStat(info.LastWriteTimeUtc, info.Length);
    }

    /// <summary>
    /// Builds a cache entry from the exact bytes that were compiled, plus a stat
    /// snapshot taken <em>before</em> those bytes were read. Re-reading the file
    /// here (as <see cref="CreateFileEntry(string)"/> does) races with concurrent
    /// editors: an edit landing mid-compile would be hashed as if it had been
    /// compiled, so the next run would skip it and the new content would never
    /// be compiled. Stat-before-read matters for the same reason — a stale
    /// (mtime,size) fails the level-1 gate and falls through to the content hash,
    /// whereas a too-new (mtime,size) paired with an old hash could level-1-skip
    /// content that was never compiled.
    /// </summary>
    public static BuildFileEntry CreateFileEntry(FileStat statBeforeRead, byte[] compiledContent)
    {
        return new BuildFileEntry
        {
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(compiledContent)),
            LastModified = statBeforeRead.LastWriteTimeUtc,
            FileSize = statBeforeRead.Length
        };
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        // Replace backslashes with forward slashes for consistent keys across platforms
        return relativePath.Replace('\\', '/').TrimEnd('/');
    }

    public static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    public static (string relativePath, bool isOutOfProject) ComputeRelativePath(
        string inputPath, string projectDirectory)
    {
        return ComputeRelativePathFromFullProjectDir(inputPath, Path.GetFullPath(projectDirectory));
    }

    internal static (string relativePath, bool isOutOfProject) ComputeRelativePathFromFullProjectDir(
        string inputPath, string fullProjectDir)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        var relativePath = Path.GetRelativePath(fullProjectDir, fullInputPath);

        if (relativePath.StartsWith("..") || Path.IsPathRooted(relativePath))
        {
            // Out-of-project file — sanitize
            var pathHash = ComputePathHash(fullInputPath);
            var safePrefix = pathHash[..16]; // 64-bit collision space
            var sanitized = Path.Combine("_linked", safePrefix, Path.GetFileName(inputPath));
            return (NormalizeRelativePath(sanitized), true);
        }

        return (NormalizeRelativePath(relativePath), false);
    }

    /// <summary>
    /// Deepest directory containing all of <paramref name="files"/>. Files on
    /// disjoint roots (e.g., different drives) fall back to the first file's
    /// directory — out-of-project sanitization keeps the remaining keys stable.
    /// </summary>
    public static string ComputeCommonDirectory(IReadOnlyList<FileInfo> files)
        => ComputeCommonDirectoryOfDirs(
            files.Select(f => Path.GetDirectoryName(Path.GetFullPath(f.FullName))!).ToList());

    /// <summary>
    /// Deepest directory containing all of <paramref name="directories"/>. Directories
    /// on disjoint roots (e.g., different drives) fall back to the first directory —
    /// out-of-project sanitization keeps the remaining keys stable.
    /// </summary>
    public static string ComputeCommonDirectoryOfDirs(IReadOnlyList<string> directories)
    {
        var first = Path.GetFullPath(directories[0]);
        var common = first;
        foreach (var directory in directories)
        {
            var dir = Path.GetFullPath(directory);
            while (!IsWithin(dir, common))
            {
                var parent = Path.GetDirectoryName(common);
                if (parent == null)
                {
                    return first; // disjoint roots
                }
                common = parent;
            }
        }
        return common;
    }

    private static bool IsWithin(string path, string baseDir)
    {
        var relative = Path.GetRelativePath(baseDir, path);
        return relative == "."
            || (!relative.StartsWith("..") && !Path.IsPathRooted(relative));
    }

    public static bool IsGlobalInvalidation(BuildState? cached, string compilerHash,
        string optionsHash, string manifestHash, string outputDirectory)
    {
        if (cached == null)
            return true;
        if (cached.FormatVersion != FormatVersion)
            return true;
        if (cached.CompilerHash != compilerHash)
            return true;
        if (cached.OptionsHash != optionsHash)
            return true;
        if (cached.ManifestHash != manifestHash)
            return true;
        if (!GetPathComparer().Equals(
            NormalizeRelativePath(cached.OutputDirectory),
            NormalizeRelativePath(outputDirectory)))
            return true;
        return false;
    }

    private static T? RetryOnIOException<T>(Func<T?> action)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                Thread.Sleep(BaseRetryDelayMs * (1 << attempt));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    private static void RetryOnIOException(Action action)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                Thread.Sleep(BaseRetryDelayMs * (1 << attempt));
            }
        }
    }
}
