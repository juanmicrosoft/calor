using System.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Analysis;

/// <summary>
/// A blocker that prevents or hinders conversion from C# to Calor.
/// </summary>
public sealed record ConvertibilityBlocker(
    string Name,
    string Description,
    int Count);

/// <summary>
/// Result of a convertibility analysis on a single C# file.
/// </summary>
public sealed record ConvertibilityResult(
    string FilePath,
    int Score,
    string Summary,
    bool ConversionAttempted,
    bool ConversionSucceeded,
    bool CompilationSucceeded,
    double ConversionRate,
    List<ConvertibilityBlocker> Blockers,
    int TotalBlockerInstances,
    TimeSpan Duration);

/// <summary>
/// Aggregated convertibility results for a directory of C# files.
/// </summary>
public sealed class DirectoryConvertibilityResult
{
    public required string DirectoryPath { get; init; }
    public required List<ConvertibilityResult> FileResults { get; init; }
    public TimeSpan Duration { get; init; }

    public int TotalFiles => FileResults.Count;
    public double AverageScore => FileResults.Count > 0 ? FileResults.Average(r => r.Score) : 0;

    public int HighCount => FileResults.Count(r => r.Score >= 90);
    public int MediumCount => FileResults.Count(r => r.Score >= 70 && r.Score < 90);
    public int LowCount => FileResults.Count(r => r.Score >= 40 && r.Score < 70);
    public int BlockedCount => FileResults.Count(r => r.Score < 40);

    /// <summary>
    /// Gets blockers aggregated across all files, sorted by total instance count.
    /// </summary>
    public List<(string Name, int TotalInstances, int FileCount)> GetAggregatedBlockers()
    {
        return FileResults
            .SelectMany(r => r.Blockers)
            .GroupBy(b => b.Name)
            .Select(g => (
                Name: g.Key,
                TotalInstances: g.Sum(b => b.Count),
                FileCount: g.Count()))
            .OrderByDescending(x => x.TotalInstances)
            .ToList();
    }
}

/// <summary>
/// Analyzes C# source code to determine how likely it is to successfully convert to Calor.
/// Combines static analysis of unsupported constructs with an actual conversion attempt.
/// </summary>
public sealed class ConvertibilityAnalyzer
{
    private static readonly string[] SkippedDirectories = { "obj", "bin", ".git", "node_modules" };

    /// <summary>
    /// Full analysis: static analysis + conversion attempt + optional compilation check.
    /// </summary>
    public ConvertibilityResult Analyze(string csharpSource, string filePath = "input.cs")
    {
        var sw = Stopwatch.StartNew();

        // Stage 1: Static analysis for unsupported constructs
        var migrationAnalyzer = new MigrationAnalyzer();
        var migrationScore = migrationAnalyzer.AnalyzeSource(csharpSource, filePath, filePath);

        if (migrationScore.WasSkipped)
        {
            sw.Stop();
            return new ConvertibilityResult(
                FilePath: filePath,
                Score: 0,
                Summary: $"Not convertible: {migrationScore.SkipReason}",
                ConversionAttempted: false,
                ConversionSucceeded: false,
                CompilationSucceeded: false,
                ConversionRate: 0,
                Blockers: new List<ConvertibilityBlocker>(),
                TotalBlockerInstances: 0,
                Duration: sw.Elapsed);
        }

        var blockers = BuildBlockers(migrationScore.UnsupportedConstructs);
        var totalBlockerInstances = blockers.Sum(b => b.Count);
        var constructTypes = blockers.Count;

        // Stage 2: Actual conversion attempt
        bool conversionAttempted = true;
        bool conversionSucceeded = false;
        bool compilationSucceeded = false;
        double conversionRate = 0;
        int conversionErrors = 0;

        try
        {
            var converter = new CSharpToCalorConverter(new ConversionOptions
            {
                GracefulFallback = true,
                Explain = true
            });
            var conversionResult = converter.Convert(csharpSource, filePath);

            conversionSucceeded = conversionResult.Success;
            conversionRate = conversionResult.Context.Stats.ConversionRate;
            conversionErrors = conversionResult.Issues
                .Count(i => i.Severity == ConversionIssueSeverity.Error);

            // Add blockers from conversion explanation
            var explanation = conversionResult.Context.GetExplanation();
            foreach (var (featureName, instances) in explanation.UnsupportedFeatures)
            {
                if (!blockers.Any(b => b.Name == featureName) && instances.Count > 0)
                {
                    blockers.Add(new ConvertibilityBlocker(featureName, $"{featureName} not supported", instances.Count));
                    totalBlockerInstances += instances.Count;
                    constructTypes++;
                }
            }

            // If conversion succeeded, attempt compilation for round-trip check
            if (conversionSucceeded && !string.IsNullOrEmpty(conversionResult.CalorSource))
            {
                try
                {
                    var compileOptions = new CompilationOptions
                    {
                        EnforceEffects = false,
                        UnknownCallPolicy = UnknownCallPolicy.Permissive
                    };
                    var compileResult = Program.Compile(conversionResult.CalorSource, filePath, compileOptions);
                    compilationSucceeded = !compileResult.HasErrors;
                }
                catch
                {
                    compilationSucceeded = false;
                }
            }
        }
        catch
        {
            // Conversion crashed entirely
            conversionAttempted = true;
            conversionSucceeded = false;
            conversionRate = 0;
            conversionErrors = 1;
        }

        // Score formula
        // constructPenalty uses per-type weight (10) and per-instance weight (2),
        // capped at 50 to allow differentiation between moderately and heavily blocked files.
        // The compilation bonus (+5) rewards files where the round-trip actually succeeds.
        int score;
        if (!conversionSucceeded && conversionRate == 0)
        {
            score = 0;
        }
        else
        {
            var baseScore = conversionRate;
            var errorPenalty = Math.Min(50, conversionErrors * 10);
            var constructPenalty = Math.Min(50, constructTypes * 10 + totalBlockerInstances * 2);
            score = (int)Math.Max(0, baseScore - errorPenalty - constructPenalty);

            // Bonus for successful compilation
            if (compilationSucceeded)
            {
                score = Math.Min(100, score + 5);
            }
        }

        sw.Stop();

        var summary = BuildSummary(score, blockers);

        return new ConvertibilityResult(
            FilePath: filePath,
            Score: score,
            Summary: summary,
            ConversionAttempted: conversionAttempted,
            ConversionSucceeded: conversionSucceeded,
            CompilationSucceeded: compilationSucceeded,
            ConversionRate: Math.Round(conversionRate, 1),
            Blockers: blockers,
            TotalBlockerInstances: totalBlockerInstances,
            Duration: sw.Elapsed);
    }

    /// <summary>
    /// Quick analysis: static analysis only, no conversion attempt.
    /// </summary>
    public ConvertibilityResult AnalyzeQuick(string csharpSource, string filePath = "input.cs")
    {
        var sw = Stopwatch.StartNew();

        var migrationAnalyzer = new MigrationAnalyzer();
        var migrationScore = migrationAnalyzer.AnalyzeSource(csharpSource, filePath, filePath);

        if (migrationScore.WasSkipped)
        {
            sw.Stop();
            return new ConvertibilityResult(
                FilePath: filePath,
                Score: 0,
                Summary: $"Not convertible: {migrationScore.SkipReason}",
                ConversionAttempted: false,
                ConversionSucceeded: false,
                CompilationSucceeded: false,
                ConversionRate: 0,
                Blockers: new List<ConvertibilityBlocker>(),
                TotalBlockerInstances: 0,
                Duration: sw.Elapsed);
        }

        var blockers = BuildBlockers(migrationScore.UnsupportedConstructs);
        var totalBlockerInstances = blockers.Sum(b => b.Count);
        var constructTypes = blockers.Count;

        // Estimate score based on blockers alone (no conversion data).
        // Uses same per-unit weights as full mode (types*10, instances*2) for proportionality,
        // but with a higher cap (60) since there's no conversion data to refine the estimate.
        var constructPenalty = Math.Min(60, constructTypes * 10 + totalBlockerInstances * 2);
        var score = Math.Max(0, 100 - constructPenalty);

        sw.Stop();

        var summary = BuildSummary(score, blockers);

        return new ConvertibilityResult(
            FilePath: filePath,
            Score: score,
            Summary: summary,
            ConversionAttempted: false,
            ConversionSucceeded: false,
            CompilationSucceeded: false,
            ConversionRate: 0,
            Blockers: blockers,
            TotalBlockerInstances: totalBlockerInstances,
            Duration: sw.Elapsed);
    }

    /// <summary>
    /// Analyzes all C# files in a directory.
    /// </summary>
    public async Task<DirectoryConvertibilityResult> AnalyzeDirectoryAsync(
        string directoryPath, bool quick = false)
    {
        var sw = Stopwatch.StartNew();
        var absolutePath = Path.GetFullPath(directoryPath);
        var files = GetCSharpFiles(absolutePath);
        var results = new List<ConvertibilityResult>();

        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file);
            var relativePath = Path.GetRelativePath(absolutePath, file);
            var result = quick
                ? AnalyzeQuick(source, relativePath)
                : Analyze(source, relativePath);
            results.Add(result);
        }

        sw.Stop();

        return new DirectoryConvertibilityResult
        {
            DirectoryPath = absolutePath,
            FileResults = results.OrderByDescending(r => r.Score).ToList(),
            Duration = sw.Elapsed
        };
    }

    private static List<ConvertibilityBlocker> BuildBlockers(List<UnsupportedConstruct> unsupported)
    {
        return unsupported
            .Where(c => c.Count > 0)
            .Select(c => new ConvertibilityBlocker(c.Name, c.Description, c.Count))
            .OrderByDescending(b => b.Count)
            .ToList();
    }

    private static string BuildSummary(int score, List<ConvertibilityBlocker> blockers)
    {
        if (score == 0 && blockers.Count > 0)
        {
            return $"Not convertible: {blockers.Count} unsupported construct type{(blockers.Count != 1 ? "s" : "")} detected.";
        }

        if (blockers.Count == 0)
        {
            return $"{score}% convertible, no significant blockers detected.";
        }

        var topBlockers = blockers.Take(3).ToList();
        var parts = topBlockers.Select(b =>
        {
            var name = b.Name.Replace("-", " ");
            return $"{b.Count} {name} usage{(b.Count != 1 ? "s" : "")}";
        });

        return $"{score}% convertible, blocked by {string.Join(" and ", parts)}.";
    }

    private static List<string> GetCSharpFiles(string directory)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(directory, "*.cs"))
            {
                files.Add(file);
            }
            foreach (var subDir in Directory.GetDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir);
                if (!SkippedDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                {
                    files.AddRange(GetCSharpFiles(subDir));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories
        }
        return files;
    }
}
