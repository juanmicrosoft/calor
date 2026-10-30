using System.Text.RegularExpressions;
using Calor.Evaluation.Core;
using Calor.Compiler.Tests;

namespace Calor.Evaluation.Benchmarks;

/// <summary>
/// Loads current-compiler inputs, resolving explicitly registered active Calor copies.
/// This is not a replay of a historical producer or its original measured input bytes.
/// </summary>
public class TestDataAdapter
{
    private readonly string _testDataPath;
    private readonly string _benchmarkPath;
    private readonly string _repositoryRoot;

    public TestDataAdapter(string testDataPath, string benchmarkPath)
        : this(testDataPath, benchmarkPath, ActiveSyntaxInputs.RepositoryRoot)
    {
    }

    internal TestDataAdapter(string testDataPath, string benchmarkPath, string repositoryRoot)
    {
        _testDataPath = testDataPath;
        _benchmarkPath = benchmarkPath;
        _repositoryRoot = repositoryRoot;
    }

    /// <summary>
    /// Gets the benchmark data path.
    /// </summary>
    public string BenchmarkPath => _benchmarkPath;

    /// <summary>
    /// Gets canonical repository inputs first, so registered mirrors are not bypassed by output copies.
    /// </summary>
    public static string GetTestDataPath()
    {
        var currentInputs = Path.Combine(ActiveSyntaxInputs.RepositoryRoot, "tests", "TestData");
        if (Directory.Exists(currentInputs))
            return currentInputs;

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
    /// Creates a current-compiler context without changing the manifest's logical entry.
    /// Metadata retains the original and resolved Calor paths and the unchanged paired C# path.
    /// Both source files are required; missing inputs fail the load instead of becoming empty code.
    /// </summary>
    public async Task<EvaluationContext> CreateContextAsync(BenchmarkEntry entry)
    {
        var calorPath = Path.Combine(_benchmarkPath, entry.CalorFile);
        var csharpPath = Path.Combine(_benchmarkPath, entry.CSharpFile);
        var resolvedCalorPath = ResolveCurrentCalorPath(_repositoryRoot, calorPath);

        // Try benchmark path first, then fall back to test data path
        if (!File.Exists(resolvedCalorPath))
        {
            calorPath = Path.Combine(_testDataPath, entry.CalorFile);
            resolvedCalorPath = ResolveCurrentCalorPath(_repositoryRoot, calorPath);
        }
        if (!File.Exists(csharpPath))
            csharpPath = Path.Combine(_testDataPath, entry.CSharpFile);

        var calorSource = await File.ReadAllTextAsync(resolvedCalorPath);
        var csharpSource = await File.ReadAllTextAsync(csharpPath);

        return new EvaluationContext
        {
            CalorSource = calorSource,
            CSharpSource = csharpSource,
            FileName = entry.Id,
            Level = entry.Level,
            Features = entry.Features,
            Metadata = InputMetadata(calorPath, resolvedCalorPath, csharpPath)
        };
    }

    /// <summary>
    /// Creates a current-compiler context from paired file paths. Registered repository
    /// Calor inputs use required active copies; external inputs remain literal.
    /// </summary>
    public static async Task<EvaluationContext> CreateContextFromFilesAsync(
        string calorPath,
        string csharpPath,
        int level = 1,
        List<string>? features = null)
    {
        var resolvedCalorPath = ResolveCurrentCalorPath(ActiveSyntaxInputs.RepositoryRoot, calorPath);
        var calorSource = await File.ReadAllTextAsync(resolvedCalorPath);
        var csharpSource = await File.ReadAllTextAsync(csharpPath);

        return new EvaluationContext
        {
            CalorSource = calorSource,
            CSharpSource = csharpSource,
            FileName = Path.GetFileNameWithoutExtension(calorPath),
            Level = level,
            Features = features ?? new List<string>(),
            Metadata = InputMetadata(calorPath, resolvedCalorPath, csharpPath)
        };
    }

    private static string ResolveCurrentCalorPath(string root, string path)
    {
        var original = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, original).Replace('\\', '/');
        return Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)
            ? original
            : ActiveSyntaxInputs.ForCurrentCompiler(root, original);
    }

    private static Dictionary<string, object> InputMetadata(string original, string resolved, string csharp)
        => new()
        {
            ["inputContract"] = "current-compiler-active-syntax",
            ["originalCalorPath"] = Path.GetFullPath(original),
            ["resolvedCalorPath"] = Path.GetFullPath(resolved),
            ["csharpPath"] = Path.GetFullPath(csharp),
            ["usesActiveSyntax"] = !string.Equals(Path.GetFullPath(original), Path.GetFullPath(resolved), StringComparison.Ordinal)
        };

    /// <summary>
    /// Loads all benchmark entries from a manifest and creates contexts.
    /// </summary>
    public async Task<List<(BenchmarkEntry Entry, EvaluationContext Context)>> LoadAllBenchmarksAsync(
        BenchmarkManifest manifest)
    {
        var results = new List<(BenchmarkEntry, EvaluationContext)>();

        foreach (var entry in manifest.Benchmarks)
        {
            var context = await CreateContextAsync(entry);
            results.Add((entry, context));
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
        if (content.Contains("§Q")) features.Add("requires");
        if (Regex.IsMatch(content, @"§S[\s(]")) features.Add("ensures");
        if (content.Contains("§INV")) features.Add("invariant");
        if (content.Contains("§E{")) features.Add("effects");
        if (content.Contains("§IF")) features.Add("conditional");
        if (content.Contains("§LOOP")) features.Add("loop");
        if (content.Contains("§MATCH")) features.Add("pattern_matching");
        if (content.Contains("§C{")) features.Add("call");

        return features;
    }
}
