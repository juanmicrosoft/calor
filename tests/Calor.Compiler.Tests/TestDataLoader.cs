using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Tests;

/// <summary>
/// Loads test data files and manifest for the C# Import tests.
/// </summary>
public static class TestDataLoader
{
    private static readonly string TestDataRoot = GetTestDataPath();
    private static ManifestFile? _cachedManifest;

    /// <summary>
    /// Gets the path to the TestData/CSharpImport directory.
    /// </summary>
    public static string GetTestDataPath()
    {
        // Try to find the TestData directory relative to the test assembly
        var assemblyLocation = AppDomain.CurrentDomain.BaseDirectory;
        var testDataPath = Path.Combine(assemblyLocation, "TestData", "CSharpImport");

        if (Directory.Exists(testDataPath))
        {
            return testDataPath;
        }

        // Fallback: walk up from the assembly location to find the tests directory
        var current = new DirectoryInfo(assemblyLocation);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "TestData", "CSharpImport");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find TestData/CSharpImport directory. Searched from: {assemblyLocation}");
    }

    /// <summary>
    /// Loads the manifest.json file containing metadata for all test files.
    /// </summary>
    public static ManifestFile LoadManifest()
    {
        if (_cachedManifest != null)
        {
            return _cachedManifest;
        }

        var manifestPath = Path.Combine(TestDataRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Manifest file not found: {manifestPath}");
        }

        var json = File.ReadAllText(manifestPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _cachedManifest = JsonSerializer.Deserialize<ManifestFile>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize manifest.json");

        return _cachedManifest;
    }

    /// <summary>
    /// Gets all test file info from the manifest.
    /// </summary>
    public static IEnumerable<TestFileInfo> GetAllTestFiles()
    {
        var manifest = LoadManifest();
        return manifest.Files;
    }

    /// <summary>
    /// Gets test files filtered by level.
    /// </summary>
    public static IEnumerable<TestFileInfo> GetTestFilesByLevel(int level)
    {
        return GetAllTestFiles().Where(f => f.Level == level);
    }

    /// <summary>
    /// Gets test files filtered by expected result.
    /// </summary>
    public static IEnumerable<TestFileInfo> GetTestFilesByExpectedResult(string expectedResult)
    {
        return GetAllTestFiles().Where(f => f.ExpectedResult == expectedResult);
    }

    /// <summary>
    /// Gets test files that have a specific feature.
    /// </summary>
    public static IEnumerable<TestFileInfo> GetTestFilesByFeature(string feature)
    {
        return GetAllTestFiles().Where(f => f.Features.Contains(feature));
    }

    /// <summary>
    /// Loads the content of a test file by its relative path.
    /// </summary>
    public static string LoadTestFile(string relativePath)
    {
        var fullPath = Path.Combine(TestDataRoot, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Test file not found: {fullPath}");
        }

        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Loads the content of a test file by its TestFileInfo.
    /// </summary>
    public static string LoadTestFile(TestFileInfo fileInfo)
    {
        return LoadTestFile(fileInfo.File);
    }

    /// <summary>
    /// Gets the full path to a test file.
    /// </summary>
    public static string GetFullPath(string relativePath)
    {
        return Path.Combine(TestDataRoot, relativePath);
    }

    /// <summary>
    /// Gets the full path to a test file.
    /// </summary>
    public static string GetFullPath(TestFileInfo fileInfo)
    {
        return GetFullPath(fileInfo.File);
    }
}

/// <summary>
/// Represents the manifest.json file structure.
/// </summary>
public class ManifestFile
{
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public List<TestFileInfo> Files { get; set; } = new();
}

/// <summary>
/// Represents a single test file entry in the manifest.
/// </summary>
public class TestFileInfo
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public int Level { get; set; }
    public List<string> Features { get; set; } = new();
    public string ExpectedResult { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>
    /// Gets the file name without path for display purposes.
    /// </summary>
    [JsonIgnore]
    public string FileName => Path.GetFileName(File);

    /// <summary>
    /// Gets a display-friendly name for the test.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => $"[{Id}] {FileName}";

    public override string ToString() => DisplayName;
}
