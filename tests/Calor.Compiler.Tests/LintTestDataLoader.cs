using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Tests;

/// <summary>
/// Loads test data files and manifest for the Lint regression tests.
/// </summary>
public static class LintTestDataLoader
{
    private static readonly string TestDataRoot = GetTestDataPath();
    private static LintManifestFile? _cachedManifest;

    /// <summary>
    /// Gets the path to the TestData/LintScenarios directory.
    /// </summary>
    public static string GetTestDataPath()
    {
        // Try to find the TestData directory relative to the test assembly
        var assemblyLocation = AppDomain.CurrentDomain.BaseDirectory;
        var testDataPath = Path.Combine(assemblyLocation, "TestData", "LintScenarios");

        if (Directory.Exists(testDataPath))
        {
            return testDataPath;
        }

        // Fallback: walk up from the assembly location to find the tests directory
        var current = new DirectoryInfo(assemblyLocation);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "TestData", "LintScenarios");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find TestData/LintScenarios directory. Searched from: {assemblyLocation}");
    }

    /// <summary>
    /// Loads the manifest.json file containing metadata for all test files.
    /// </summary>
    public static LintManifestFile LoadManifest()
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

        _cachedManifest = JsonSerializer.Deserialize<LintManifestFile>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize manifest.json");

        return _cachedManifest;
    }

    /// <summary>
    /// Gets all test file info from the manifest.
    /// </summary>
    public static IEnumerable<LintTestFileInfo> GetAllTestFiles()
    {
        var manifest = LoadManifest();
        return manifest.Files;
    }

    /// <summary>
    /// Gets test files filtered by category.
    /// </summary>
    public static IEnumerable<LintTestFileInfo> GetTestFilesByCategory(string category)
    {
        return GetAllTestFiles().Where(f => f.Category == category);
    }

    /// <summary>
    /// Gets test files that are expected to have auto-fixable issues.
    /// </summary>
    public static IEnumerable<LintTestFileInfo> GetAutoFixableFiles()
    {
        return GetAllTestFiles().Where(f => f.CanAutoFix);
    }

    /// <summary>
    /// Gets test files that should have zero lint issues.
    /// </summary>
    public static IEnumerable<LintTestFileInfo> GetCleanFiles()
    {
        return GetAllTestFiles().Where(f => f.ExpectedIssues == 0);
    }

    /// <summary>
    /// Gets test files that are expected to fail parsing.
    /// </summary>
    public static IEnumerable<LintTestFileInfo> GetErrorCaseFiles()
    {
        return GetAllTestFiles().Where(f => f.ExpectedIssues == -1);
    }

    /// <summary>
    /// Gets test files that have lint issues but aren't error cases.
    /// </summary>
    public static IEnumerable<LintTestFileInfo> GetFilesWithIssues()
    {
        return GetAllTestFiles().Where(f => f.ExpectedIssues > 0);
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
    /// Loads the content of a test file by its LintTestFileInfo.
    /// </summary>
    public static string LoadTestFile(LintTestFileInfo fileInfo)
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
    public static string GetFullPath(LintTestFileInfo fileInfo)
    {
        return GetFullPath(fileInfo.File);
    }

    /// <summary>
    /// Gets test data for Theory tests (file path and expected issue count).
    /// </summary>
    public static IEnumerable<object[]> GetIdAbbreviationTestData()
    {
        return GetTestFilesByCategory("01_id_abbreviation")
            .Select(f => new object[] { f.File, f.ExpectedIssues });
    }

    /// <summary>
    /// Gets test data for whitespace tests.
    /// </summary>
    public static IEnumerable<object[]> GetWhitespaceTestData()
    {
        return GetTestFilesByCategory("02_whitespace")
            .Select(f => new object[] { f.File, f.ExpectedIssues });
    }

    /// <summary>
    /// Gets test data for round-trip tests.
    /// </summary>
    public static IEnumerable<object[]> GetRoundTripTestData()
    {
        return GetTestFilesByCategory("07_round_trip")
            .Select(f => new object[] { f.File });
    }

    /// <summary>
    /// Gets test data for idempotency tests.
    /// </summary>
    public static IEnumerable<object[]> GetIdempotencyTestData()
    {
        return GetTestFilesByCategory("08_idempotency")
            .Select(f => new object[] { f.File });
    }

    /// <summary>
    /// Gets test data for error cases.
    /// </summary>
    public static IEnumerable<object[]> GetErrorCaseTestData()
    {
        return GetTestFilesByCategory("10_error_cases")
            .Select(f => new object[] { f.File });
    }
}

/// <summary>
/// Represents the manifest.json file structure for lint tests.
/// </summary>
public class LintManifestFile
{
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public List<LintCategoryInfo> Categories { get; set; } = new();
    public List<LintTestFileInfo> Files { get; set; } = new();
}

/// <summary>
/// Represents a category in the manifest.
/// </summary>
public class LintCategoryInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Represents a single test file entry in the manifest.
/// </summary>
public class LintTestFileInfo
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public string Category { get; set; } = "";
    public int ExpectedIssues { get; set; }
    public List<string> IssueTypes { get; set; } = new();
    public bool CanAutoFix { get; set; }
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
