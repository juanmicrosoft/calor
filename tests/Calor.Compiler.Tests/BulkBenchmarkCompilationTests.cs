using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests;

/// <summary>
/// Bulk compilation test that verifies all 202 benchmark .calr files parse and compile without errors.
/// </summary>
public class BulkBenchmarkCompilationTests
{
    private readonly ITestOutputHelper _output;

    public BulkBenchmarkCompilationTests(ITestOutputHelper output) => _output = output;

    private static string GetBenchmarksDir()
    {
        var testDir = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        return Path.Combine(projectRoot, "tests", "TestData", "Benchmarks");
    }

    private record ManifestEntry(string id, string name, string category, string calorFile, string csharpFile);
    private record Manifest(List<ManifestEntry> benchmarks);

    private static List<ManifestEntry> LoadManifest()
    {
        var benchmarksDir = GetBenchmarksDir();
        var manifestPath = Path.Combine(benchmarksDir, "manifest.json");
        var json = File.ReadAllText(manifestPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<Manifest>(json, options)!;
        return manifest.benchmarks;
    }

    public static IEnumerable<object[]> AllBenchmarks()
    {
        foreach (var entry in LoadManifest())
        {
            yield return new object[] { entry.id, entry.name, entry.category, entry.calorFile };
        }
    }

    [Theory]
    [MemberData(nameof(AllBenchmarks))]
    public void BenchmarkFile_Compiles(string id, string name, string category, string calorFile)
    {
        var benchmarksDir = GetBenchmarksDir();
        var fullPath = Path.Combine(benchmarksDir, calorFile);

        Assert.True(File.Exists(fullPath), $"Missing .calr file: {calorFile}");

        var source = File.ReadAllText(fullPath);
        Assert.False(string.IsNullOrWhiteSpace(source), $"Empty .calr file: {calorFile}");

        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnforceEffects = false // Don't fail on effect issues for benchmarks
        };

        var result = Program.Compile(source, fullPath, options);

        if (result.HasErrors)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == Calor.Compiler.Diagnostics.DiagnosticSeverity.Error)
                .Select(d => $"  [{d.Code}] {d.Message}");
            var errorMsg = string.Join(Environment.NewLine, errors);
            _output.WriteLine($"FAIL [{id}] {category}/{name}:");
            _output.WriteLine(errorMsg);
            Assert.Fail($"Compilation failed for {calorFile}:\n{errorMsg}");
        }

        Assert.False(string.IsNullOrWhiteSpace(result.GeneratedCode),
            $"No C# code generated for {calorFile}");

        _output.WriteLine($"OK [{id}] {category}/{name} → {result.GeneratedCode.Length} chars C#");
    }

    /// <summary>
    /// Regenerates .g.cs files for TokenEconomics programs only when the .calr source is newer.
    /// Use: dotnet test --filter "Category=Generation" to run explicitly.
    /// </summary>
    [Fact]
    [Trait("Category", "Generation")]
    public void GenerateGcsFiles_ForTokenEconomics()
    {
        var benchmarksDir = GetBenchmarksDir();
        var entries = LoadManifest().Where(e => e.category == "TokenEconomics").ToList();
        int generated = 0, skipped = 0;

        foreach (var entry in entries)
        {
            var calrPath = Path.Combine(benchmarksDir, entry.calorFile);
            var gcsPath = calrPath.Replace(".calr", ".g.cs");

            // Skip if .g.cs already exists and is newer than .calr source
            if (File.Exists(gcsPath) && File.GetLastWriteTimeUtc(gcsPath) >= File.GetLastWriteTimeUtc(calrPath))
            {
                skipped++;
                continue;
            }

            var source = File.ReadAllText(calrPath);
            var options = new CompilationOptions
            {
                ContractMode = ContractMode.Debug,
                EnforceEffects = false
            };
            var result = Program.Compile(source, calrPath, options);

            if (!result.HasErrors && !string.IsNullOrWhiteSpace(result.GeneratedCode))
            {
                File.WriteAllText(gcsPath, result.GeneratedCode);
                generated++;
            }
        }

        _output.WriteLine($"TokenEconomics .g.cs: {generated} generated, {skipped} up-to-date (total {entries.Count})");
        Assert.True(generated + skipped == entries.Count,
            $"Only processed {generated + skipped}/{entries.Count} files");
    }

    [Fact]
    public void AllBenchmarkFiles_Exist_And_Have_CSharpReference()
    {
        var benchmarksDir = GetBenchmarksDir();
        var entries = LoadManifest();

        Assert.True(entries.Count >= 200, $"Expected at least 200 benchmarks, got {entries.Count}");

        var missing = new List<string>();
        foreach (var entry in entries)
        {
            var calrPath = Path.Combine(benchmarksDir, entry.calorFile);
            var csPath = Path.Combine(benchmarksDir, entry.csharpFile);
            if (!File.Exists(calrPath)) missing.Add($"MISSING .calr: {entry.calorFile}");
            if (!File.Exists(csPath)) missing.Add($"MISSING .cs: {entry.csharpFile}");
        }

        Assert.True(missing.Count == 0,
            $"Missing files:\n{string.Join(Environment.NewLine, missing)}");
    }
}
