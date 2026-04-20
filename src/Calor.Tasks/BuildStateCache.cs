using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Tasks;

[JsonSerializable(typeof(BuildState))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BuildStateJsonContext : JsonSerializerContext { }

internal sealed class BuildState
{
    public string FormatVersion { get; set; } = "1.0";
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
}

internal static class BuildStateCache
{
    private const string FormatVersion = "1.0";
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

    public static string ComputeCompilerHash(string tasksAssemblyPath)
    {
        var tasksDir = Path.GetDirectoryName(tasksAssemblyPath)!;
        var compilerDllPath = Path.Combine(tasksDir, "Calor.Compiler.dll");

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // Stream hash to avoid allocating multi-MB arrays
        HashFileStreaming(sha, tasksAssemblyPath);
        HashFileStreaming(sha, compilerDllPath);

        return Convert.ToHexStringLower(sha.GetHashAndReset());
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

    // Cached since options don't change within a process lifetime.
    // When codegen-affecting properties are added, this must take them as explicit parameters.
    private static readonly string CachedOptionsHash = ComputeOptionsHashCore();

    public static string ComputeOptionsHash() => CachedOptionsHash;

    private static string ComputeOptionsHashCore()
    {
        // Currently the task only passes Verbose (excluded — doesn't affect output or diagnostics).
        // The hash exists for forward compatibility. When properties like ContractMode,
        // EnforceEffects, StrictEffects, RequireDocs, StrictApi are added to the task,
        // they MUST be added here as explicit parameters.
        var bytes = Encoding.UTF8.GetBytes("options:v1");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeManifestHash(string projectDirectory)
    {
        var manifestFiles = Directory.Exists(projectDirectory)
            ? Directory.GetFiles(projectDirectory, "*.calor-effects.json", SearchOption.TopDirectoryOnly)
            : [];

        if (manifestFiles.Length == 0)
            return "";

        Array.Sort(manifestFiles, StringComparer.OrdinalIgnoreCase);

        using var sha = SHA256.Create();
        foreach (var manifestFile in manifestFiles)
        {
            var content = File.ReadAllBytes(manifestFile);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
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
