using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;

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
    public string FormatVersion { get; set; } = BuildStateCache.CurrentFormatVersion;
    public string CompilerSemanticsVersion { get; set; } =
        BuildStateCache.CurrentCompilerSemanticsVersion;

    /// <summary>
    /// Fingerprint of the cross-module function map the outputs were emitted
    /// under (G3/#809): emission qualifies cross-module calls from this map, so
    /// a changed map (module added/removed/renamed, ambiguity introduced) must
    /// invalidate every warm skip — a cached .g.cs may carry stale
    /// qualification. Null/empty when single-module.
    /// </summary>
    public string? CrossModuleMapHash { get; set; }
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
    /// observed) is also a miss.
    /// </summary>
    public string? OutputContentHash { get; set; }

    /// <summary>
    /// File-local diagnostics replayed by the MSBuild task on a warm hit. Paths
    /// are intentionally not persisted; the current source path is supplied at
    /// replay time to keep cache state portable and privacy-aware.
    /// </summary>
    public List<CachedDiagnostic>? Diagnostics { get; set; }
}

internal sealed class CachedDiagnostic
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

internal enum CacheLoadStatus
{
    Loaded,
    Missing,
    CorruptOrPartial,
    Unreadable
}

internal sealed record CacheLoadResult(BuildState? State, CacheLoadStatus Status);

internal enum GlobalCacheInvalidationReason
{
    MissingCache,
    CorruptOrPartialCache,
    UnreadableCache,
    SchemaVersionChanged,
    CompilerSemanticsChanged,
    CompilerChanged,
    OptionsOrInputsChanged,
    ManifestChanged,
    OutputDirectoryChanged,
    CrossModuleMapChanged
}

internal enum FileCacheMissReason
{
    EntryMissing,
    EffectSummaryMissing,
    DiagnosticsMissing,
    OutputMissing,
    OutputHashMissing,
    OutputContentChanged,
    SourceMissing,
    SourceContentChanged,
    CacheValidationFailed
}

internal static class BuildStateCache
{
    // 3.0 adds an explicit compiler-semantics surface and canonical MSBuild input
    // fingerprinting. Older state is intentionally rebuilt and overwritten.
    public const string CurrentFormatVersion = "3.0";
    public const string CurrentCompilerSemanticsVersion = "calor-compile-semantics-v1";
    public const string CurrentOptionsSerializerVersion = "compile-inputs-v3";
    private const string CacheFileName = ".calor-build-state.json";
    private const int MaxRetries = 3;
    private const int BaseRetryDelayMs = 50;

    public static string GetCachePath(string outputDirectory)
        => Path.Combine(outputDirectory, CacheFileName);

    public static BuildState? Load(string outputDirectory)
        => LoadWithStatus(outputDirectory).State;

    public static CacheLoadResult LoadWithStatus(string outputDirectory)
    {
        var cachePath = GetCachePath(outputDirectory);
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                if (!File.Exists(cachePath))
                    return new CacheLoadResult(null, CacheLoadStatus.Missing);

                var json = File.ReadAllText(cachePath);
                using var document = JsonDocument.Parse(json);
                if (!HasRequiredCacheProperties(document.RootElement))
                    return new CacheLoadResult(null, CacheLoadStatus.CorruptOrPartial);
                var state = JsonSerializer.Deserialize(json, BuildStateJsonContext.Default.BuildState);
                if (state == null
                    || state.Files == null
                    || string.IsNullOrEmpty(state.CompilerHash)
                    || string.IsNullOrEmpty(state.OptionsHash)
                    || string.IsNullOrEmpty(state.FormatVersion))
                {
                    return new CacheLoadResult(null, CacheLoadStatus.CorruptOrPartial);
                }
                return new CacheLoadResult(state, CacheLoadStatus.Loaded);
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                Thread.Sleep(BaseRetryDelayMs * (1 << attempt));
            }
            catch (JsonException)
            {
                return new CacheLoadResult(null, CacheLoadStatus.CorruptOrPartial);
            }
            catch (NotSupportedException)
            {
                return new CacheLoadResult(null, CacheLoadStatus.CorruptOrPartial);
            }
            catch (IOException)
            {
                return new CacheLoadResult(null, CacheLoadStatus.Unreadable);
            }
            catch (UnauthorizedAccessException)
            {
                return new CacheLoadResult(null, CacheLoadStatus.Unreadable);
            }
        }
        return new CacheLoadResult(null, CacheLoadStatus.Unreadable);
    }

    private static bool HasRequiredCacheProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("formatVersion", out var format)
            || format.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("compilerHash", out var compilerHash)
            || compilerHash.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("optionsHash", out var optionsHash)
            || optionsHash.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("manifestHash", out var manifestHash)
            || manifestHash.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("outputDirectory", out var outputDirectory)
            || outputDirectory.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return format.GetString() != CurrentFormatVersion
            || (root.TryGetProperty("compilerSemanticsVersion", out var semantics)
                && semantics.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(semantics.GetString()));
    }

    public static void Save(BuildState state, string outputDirectory)
    {
        var cachePath = GetCachePath(outputDirectory);
        var dir = Path.GetDirectoryName(cachePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tmpPath = cachePath + $".tmp.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.{Guid.NewGuid():N}";
        try
        {
            RetryOnIOException(() =>
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
                var json = JsonSerializer.Serialize(state, BuildStateJsonContext.Default.BuildState);
                using (var stream = new FileStream(
                    tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tmpPath, cachePath, overwrite: true);
            });
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Deletes the state file (best-effort). Used by <c>--clear-cache</c>.
    /// </summary>
    public static bool Delete(string outputDirectory)
    {
        var cachePath = GetCachePath(outputDirectory);
        try
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
            return !File.Exists(cachePath);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static string ComputeFileHash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return ComputeContentHash(bytes);
    }

    public static string ComputeContentHash(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string ComputePathHash(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Compiler hash over an explicit set of assembly files. Missing members are
    /// represented explicitly so removing a dependency invalidates the cache.
    /// </summary>
    public static string ComputeCompilerHash(IReadOnlyList<string> assemblyPaths)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var path in assemblyPaths.OrderBy(
                     path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            AppendLengthPrefixed(sha, name);
            if (File.Exists(path))
            {
                AppendLengthPrefixed(sha, "present");
                HashFileStreaming(sha, path);
            }
            else
            {
                AppendLengthPrefixed(sha, "missing");
            }
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>
    /// CLI compiler hash: the Calor.Compiler assembly itself. Falls back to the
    /// assembly version string when the on-disk location is unavailable
    /// (e.g., single-file publish), so upgrades still invalidate the cache.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Assembly.Location is checked for empty string; falls back to the version string in single-file mode.")]
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

    private static void AppendLengthPrefixed(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    /// <summary>
    /// Legacy convenience overload for callers with only effect enforcement.
    /// </summary>
    public static string ComputeOptionsHash(bool enforceEffects = true)
        => ComputeOptionsHash($"enforceEffects:{enforceEffects}");

    /// <summary>
    /// Options hash over a caller-supplied canonical token. Diagnostics-affecting
    /// options must be folded into the token: an option flip between builds has to
    /// invalidate cached (skipped) files, otherwise violations that the new option
    /// set would report are silently missed on warm builds. Presentation-only
    /// options are excluded unless they alter diagnostics; the MSBuild task's
    /// Verbose setting does, so its canonical record includes that value.
    ///
    /// The set of EffectKind enum values is also folded in: cached EffectSummary entries
    /// reference kinds by name, and a kind added/removed/renamed in a compiler upgrade
    /// must force a cold rebuild so old summaries don't silently drop effects on parse.
    /// </summary>
    public static string ComputeOptionsHash(string optionsToken)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes($"options:{CurrentOptionsSerializerVersion}"));
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
        => ComputeManifestHash(projectDirectories, ManifestLoader.GetUserManifestsDirectory());

    internal static string ComputeManifestHash(
        IReadOnlyList<string> projectDirectories,
        string? userManifestsDirectory)
    {
        var roots = projectDirectories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select((directory, index) => (
                Scope: $"project-{index}",
                Directory: Path.GetFullPath(directory)))
            .ToList();
        if (!string.IsNullOrEmpty(userManifestsDirectory))
        {
            roots.Add(("user", Path.GetFullPath(userManifestsDirectory)));
        }

        var manifestFiles = new List<(string Scope, string Path)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Directory))
                continue;
            foreach (var file in Directory.GetFiles(
                         root.Directory, "*.calor-effects.json", SearchOption.TopDirectoryOnly))
            {
                if (seen.Add(Path.GetFullPath(file)))
                    manifestFiles.Add((root.Scope, file));
            }
        }

        if (manifestFiles.Count == 0)
            return "";

        manifestFiles.Sort((left, right) =>
        {
            var scopeComparison = StringComparer.Ordinal.Compare(left.Scope, right.Scope);
            return scopeComparison != 0
                ? scopeComparison
                : StringComparer.Ordinal.Compare(
                    NormalizePathForOrdering(left.Path),
                    NormalizePathForOrdering(right.Path));
        });

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var manifestFile in manifestFiles)
        {
            AppendLengthPrefixed(sha, manifestFile.Scope);
            AppendLengthPrefixed(sha, Path.GetFileName(manifestFile.Path));
            AppendLengthPrefixed(sha, new FileInfo(manifestFile.Path).Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            HashFileStreaming(sha, manifestFile.Path);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static string NormalizePathForOrdering(string path)
        => Path.GetFullPath(path).Replace('\\', '/');

    public static bool IsFileUpToDate(BuildFileEntry? cached, string filePath)
    {
        if (cached == null)
            return false;

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            return false;

        // Always verify bytes. Metadata is retained as the pre-read snapshot for
        // race safety and observability, but mtime/size alone cannot prove that
        // content is unchanged (both can be preserved by editors or restored).
        return ComputeFileHash(filePath) == cached.ContentHash;
    }

    /// <summary>
    /// Evaluates a warm-cache entry in a fixed order so verbose logs and tests get
    /// one deterministic, actionable miss reason.
    /// </summary>
    public static FileCacheMissReason? GetFileCacheMissReason(
        BuildFileEntry? cached, string sourcePath, string outputPath)
    {
        if (cached == null)
            return FileCacheMissReason.EntryMissing;
        if (cached.EffectSummary == null)
            return FileCacheMissReason.EffectSummaryMissing;
        if (cached.Diagnostics == null)
            return FileCacheMissReason.DiagnosticsMissing;
        if (cached.Diagnostics.Any(diagnostic =>
                diagnostic.Severity is not ("warning" or "info")
                || string.IsNullOrEmpty(diagnostic.Code)
                || diagnostic.Line < 0
                || diagnostic.Column < 0))
        {
            return FileCacheMissReason.CacheValidationFailed;
        }
        if (!File.Exists(outputPath))
            return FileCacheMissReason.OutputMissing;
        if (cached.OutputContentHash == null)
            return FileCacheMissReason.OutputHashMissing;

        try
        {
            if (ComputeFileHash(outputPath) != cached.OutputContentHash)
                return FileCacheMissReason.OutputContentChanged;

            var info = new FileInfo(sourcePath);
            if (!info.Exists)
                return FileCacheMissReason.SourceMissing;
            return ComputeFileHash(sourcePath) == cached.ContentHash
                ? null
                : FileCacheMissReason.SourceContentChanged;
        }
        catch (IOException)
        {
            return FileCacheMissReason.CacheValidationFailed;
        }
        catch (UnauthorizedAccessException)
        {
            return FileCacheMissReason.CacheValidationFailed;
        }
    }

    public static string GetReasonCode(GlobalCacheInvalidationReason reason) => reason switch
    {
        GlobalCacheInvalidationReason.MissingCache => "missing-cache",
        GlobalCacheInvalidationReason.CorruptOrPartialCache => "corrupt-or-partial-cache",
        GlobalCacheInvalidationReason.UnreadableCache => "unreadable-cache",
        GlobalCacheInvalidationReason.SchemaVersionChanged => "schema-version-changed",
        GlobalCacheInvalidationReason.CompilerSemanticsChanged => "compiler-semantics-changed",
        GlobalCacheInvalidationReason.CompilerChanged => "compiler-changed",
        GlobalCacheInvalidationReason.OptionsOrInputsChanged => "options-or-inputs-changed",
        GlobalCacheInvalidationReason.ManifestChanged => "manifest-changed",
        GlobalCacheInvalidationReason.OutputDirectoryChanged => "output-directory-changed",
        GlobalCacheInvalidationReason.CrossModuleMapChanged => "cross-module-map-changed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    public static string GetReasonCode(FileCacheMissReason reason) => reason switch
    {
        FileCacheMissReason.EntryMissing => "entry-missing",
        FileCacheMissReason.EffectSummaryMissing => "effect-summary-missing",
        FileCacheMissReason.DiagnosticsMissing => "diagnostics-missing",
        FileCacheMissReason.OutputMissing => "output-missing",
        FileCacheMissReason.OutputHashMissing => "output-hash-missing",
        FileCacheMissReason.OutputContentChanged => "output-content-changed",
        FileCacheMissReason.SourceMissing => "source-missing",
        FileCacheMissReason.SourceContentChanged => "source-content-changed",
        FileCacheMissReason.CacheValidationFailed => "cache-validation-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

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
    /// be compiled. The snapshot is retained for observability and migration
    /// even though warm validation hashes bytes unconditionally.
    /// </summary>
    public static BuildFileEntry CreateFileEntry(FileStat statBeforeRead, byte[] compiledContent)
    {
        return new BuildFileEntry
        {
            ContentHash = ComputeContentHash(compiledContent),
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
        => GetGlobalInvalidationReasons(
            cached, compilerHash, optionsHash, manifestHash, outputDirectory).Count != 0;

    public static IReadOnlyList<GlobalCacheInvalidationReason> GetGlobalInvalidationReasons(
        BuildState? cached, string compilerHash, string optionsHash,
        string manifestHash, string outputDirectory)
    {
        var reasons = new List<GlobalCacheInvalidationReason>();
        if (cached == null)
        {
            reasons.Add(GlobalCacheInvalidationReason.MissingCache);
            return reasons;
        }
        if (cached.FormatVersion != CurrentFormatVersion)
            reasons.Add(GlobalCacheInvalidationReason.SchemaVersionChanged);
        if (cached.CompilerSemanticsVersion != CurrentCompilerSemanticsVersion)
            reasons.Add(GlobalCacheInvalidationReason.CompilerSemanticsChanged);
        if (cached.CompilerHash != compilerHash)
            reasons.Add(GlobalCacheInvalidationReason.CompilerChanged);
        if (cached.OptionsHash != optionsHash)
            reasons.Add(GlobalCacheInvalidationReason.OptionsOrInputsChanged);
        if (cached.ManifestHash != manifestHash)
            reasons.Add(GlobalCacheInvalidationReason.ManifestChanged);
        if (!GetPathComparer().Equals(
            NormalizeRelativePath(cached.OutputDirectory),
            NormalizeRelativePath(outputDirectory)))
            reasons.Add(GlobalCacheInvalidationReason.OutputDirectoryChanged);
        return reasons;
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
