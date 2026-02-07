using Calor.Evaluation.Core;

namespace Calor.Evaluation.Benchmarks;

/// <summary>
/// Adapts existing test data from the CSharpImport test suite for use in benchmarks.
/// </summary>
public class TestDataAdapter
{
    private readonly string _testDataPath;
    private readonly string _benchmarkPath;

    public TestDataAdapter(string testDataPath, string benchmarkPath)
    {
        _testDataPath = testDataPath;
        _benchmarkPath = benchmarkPath;
    }

    /// <summary>
    /// Gets the path to the TestData directory, searching relative to the assembly location.
    /// </summary>
    public static string GetTestDataPath()
    {
        // Try relative to assembly location first
        var assemblyLocation = Path.GetDirectoryName(typeof(TestDataAdapter).Assembly.Location);
        if (assemblyLocation != null)
        {
            var testDataPath = Path.Combine(assemblyLocation, "TestData");
            if (Directory.Exists(testDataPath))
                return testDataPath;
        }

        // Traverse up from current directory
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir, "tests", "TestData");
            if (Directory.Exists(candidate))
                return candidate;

            candidate = Path.Combine(currentDir, "TestData");
            if (Directory.Exists(candidate))
                return candidate;

            currentDir = Path.GetDirectoryName(currentDir);
        }

        throw new DirectoryNotFoundException("Could not locate TestData directory");
    }

    /// <summary>
    /// Gets the path to the Benchmarks directory.
    /// </summary>
    public static string GetBenchmarkPath()
    {
        return Path.Combine(GetTestDataPath(), "Benchmarks");
    }

    /// <summary>
    /// Creates an EvaluationContext from a benchmark entry.
    /// </summary>
    public async Task<EvaluationContext> CreateContextAsync(BenchmarkEntry entry)
    {
        var calorPath = Path.Combine(_benchmarkPath, entry.CalorFile);
        var csharpPath = Path.Combine(_benchmarkPath, entry.CSharpFile);

        // Try benchmark path first, then fall back to test data path
        if (!File.Exists(calorPath))
            calorPath = Path.Combine(_testDataPath, entry.CalorFile);
        if (!File.Exists(csharpPath))
            csharpPath = Path.Combine(_testDataPath, entry.CSharpFile);

        var calorSource = File.Exists(calorPath) ? await File.ReadAllTextAsync(calorPath) : "";
        var csharpSource = File.Exists(csharpPath) ? await File.ReadAllTextAsync(csharpPath) : "";

        return new EvaluationContext
        {
            CalorSource = calorSource,
            CSharpSource = csharpSource,
            FileName = entry.Id,
            Level = entry.Level,
            Features = entry.Features
        };
    }

    /// <summary>
    /// Creates an EvaluationContext from paired file paths.
    /// </summary>
    public static async Task<EvaluationContext> CreateContextFromFilesAsync(
        string calorPath,
        string csharpPath,
        int level = 1,
        List<string>? features = null)
    {
        var calorSource = await File.ReadAllTextAsync(calorPath);
        var csharpSource = await File.ReadAllTextAsync(csharpPath);

        return new EvaluationContext
        {
            CalorSource = calorSource,
            CSharpSource = csharpSource,
            FileName = Path.GetFileNameWithoutExtension(calorPath),
            Level = level,
            Features = features ?? new List<string>()
        };
    }

    /// <summary>
    /// Loads all benchmark entries from a manifest and creates contexts.
    /// </summary>
    public async Task<List<(BenchmarkEntry Entry, EvaluationContext Context)>> LoadAllBenchmarksAsync(
        BenchmarkManifest manifest)
    {
        var results = new List<(BenchmarkEntry, EvaluationContext)>();

        foreach (var entry in manifest.Benchmarks)
        {
            try
            {
                var context = await CreateContextAsync(entry);
                results.Add((entry, context));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to load benchmark {entry.Id}: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// Discovers paired Calor/C# files in a directory.
    /// </summary>
    public static async Task<BenchmarkManifest> DiscoverBenchmarksAsync(
        string directory,
        string category = "TokenEconomics")
    {
        var manifest = new BenchmarkManifest
        {
            Version = "1.0",
            Description = $"Auto-discovered benchmarks from {directory}"
        };

        var calorFiles = Directory.GetFiles(directory, "*.calr", SearchOption.AllDirectories);
        var counter = 1;

        foreach (var calorFile in calorFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(calorFile);
            var dir = Path.GetDirectoryName(calorFile)!;

            // Look for matching C# file
            var csharpFile = Path.Combine(dir, baseName + ".cs");
            if (!File.Exists(csharpFile))
            {
                csharpFile = Path.Combine(dir, baseName + ".g.cs");
                if (!File.Exists(csharpFile))
                    continue;
            }

            var relCalor = Path.GetRelativePath(directory, calorFile);
            var relCSharp = Path.GetRelativePath(directory, csharpFile);

            manifest.Benchmarks.Add(new BenchmarkEntry
            {
                Id = counter.ToString("D3"),
                Name = baseName,
                Category = category,
                CalorFile = relCalor,
                CSharpFile = relCSharp,
                Level = InferLevel(baseName),
                Features = await InferFeaturesAsync(calorFile),
                Notes = $"Auto-discovered from {baseName}"
            });

            counter++;
        }

        return manifest;
    }

    /// <summary>
    /// Infers complexity level from file name patterns.
    /// </summary>
    private static int InferLevel(string baseName)
    {
        var lower = baseName.ToLowerInvariant();

        if (lower.Contains("hello") || lower.Contains("simple"))
            return 1;
        if (lower.Contains("fizzbuzz") || lower.Contains("calc"))
            return 2;
        if (lower.Contains("type") || lower.Contains("class"))
            return 3;
        if (lower.Contains("generic") || lower.Contains("async"))
            return 4;
        if (lower.Contains("advanced") || lower.Contains("complex"))
            return 5;

        return 2; // Default to level 2
    }

    /// <summary>
    /// Infers features from Calor file content.
    /// </summary>
    private static async Task<List<string>> InferFeaturesAsync(string calorPath)
    {
        var features = new List<string>();
        var content = await File.ReadAllTextAsync(calorPath);

        if (content.Contains("§M{")) features.Add("module");
        if (content.Contains("§F{")) features.Add("function");
        if (content.Contains("§V{")) features.Add("variable");
        if (content.Contains("§I{")) features.Add("parameters");
        if (content.Contains("§O{")) features.Add("return_type");
        if (content.Contains("§REQ")) features.Add("requires");
        if (content.Contains("§ENS")) features.Add("ensures");
        if (content.Contains("§INV")) features.Add("invariant");
        if (content.Contains("§E{")) features.Add("effects");
        if (content.Contains("§IF")) features.Add("conditional");
        if (content.Contains("§LOOP")) features.Add("loop");
        if (content.Contains("§MATCH")) features.Add("pattern_matching");
        if (content.Contains("§C{")) features.Add("call");

        return features;
    }
}
