namespace Calor.Compiler.Analysis;

/// <summary>
/// Dimensions used to score C# code for Calor migration potential.
/// </summary>
public enum ScoreDimension
{
    /// <summary>
    /// Argument validation, range checks, assertions → §Q/§S contracts.
    /// </summary>
    ContractPotential,

    /// <summary>
    /// File I/O, network, database, console calls → §E effect declarations.
    /// </summary>
    EffectPotential,

    /// <summary>
    /// Nullable types, null checks, ??, ?. → Option&lt;T&gt;.
    /// </summary>
    NullSafetyPotential,

    /// <summary>
    /// Try/catch blocks, throw statements → Result&lt;T,E&gt;.
    /// </summary>
    ErrorHandlingPotential,

    /// <summary>
    /// Switch statements/expressions → Exhaustiveness checking.
    /// </summary>
    PatternMatchPotential,

    /// <summary>
    /// Public APIs, undocumented methods → Calor metadata.
    /// </summary>
    ApiComplexityPotential,

    /// <summary>
    /// async/await patterns, Task&lt;T&gt; returns → Calor has different async model.
    /// </summary>
    AsyncPotential,

    /// <summary>
    /// LINQ methods (Where, Select, OrderBy) → Calor uses different collection patterns.
    /// </summary>
    LinqPotential
}

/// <summary>
/// Score for a single dimension.
/// </summary>
public sealed class DimensionScore
{
    public ScoreDimension Dimension { get; init; }
    public double RawScore { get; init; }
    public double Weight { get; init; }
    public double WeightedScore => RawScore * Weight;
    public int PatternCount { get; init; }
    public List<string> Examples { get; init; } = new();

    public static double GetWeight(ScoreDimension dimension) => dimension switch
    {
        ScoreDimension.ContractPotential => 0.18,
        ScoreDimension.EffectPotential => 0.13,
        ScoreDimension.NullSafetyPotential => 0.18,
        ScoreDimension.ErrorHandlingPotential => 0.18,
        ScoreDimension.PatternMatchPotential => 0.08,
        ScoreDimension.ApiComplexityPotential => 0.13,
        ScoreDimension.AsyncPotential => 0.06,
        ScoreDimension.LinqPotential => 0.06,
        _ => 0.0
    };
}

/// <summary>
/// Priority band for migration urgency.
/// </summary>
public enum MigrationPriority
{
    /// <summary>Score 0-25: Minimal benefit from migration.</summary>
    Low,

    /// <summary>Score 26-50: Some benefit from migration.</summary>
    Medium,

    /// <summary>Score 51-75: Good migration candidate.</summary>
    High,

    /// <summary>Score 76-100: Excellent migration candidate.</summary>
    Critical
}

/// <summary>
/// Represents a C# construct that the Calor converter doesn't yet support.
/// </summary>
public sealed class UnsupportedConstruct
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public int Count { get; init; }
    public List<string> Examples { get; init; } = new();
}

/// <summary>
/// Migration analysis score for a single file.
/// </summary>
public sealed class FileMigrationScore
{
    public required string FilePath { get; init; }
    public required string RelativePath { get; init; }
    public double TotalScore { get; init; }
    public MigrationPriority Priority { get; init; }
    public Dictionary<ScoreDimension, DimensionScore> Dimensions { get; init; } = new();
    public int LineCount { get; init; }
    public int MethodCount { get; init; }
    public int TypeCount { get; init; }
    public bool WasSkipped { get; init; }
    public string? SkipReason { get; init; }

    /// <summary>
    /// C# constructs found in this file that aren't yet supported by the Calor converter.
    /// Files with unsupported constructs receive a significant score penalty.
    /// </summary>
    public List<UnsupportedConstruct> UnsupportedConstructs { get; init; } = new();

    /// <summary>
    /// True if the file contains constructs that can't yet be converted to Calor.
    /// </summary>
    public bool HasUnsupportedConstructs => UnsupportedConstructs.Count > 0;

    public static MigrationPriority GetPriority(double score) => score switch
    {
        >= 76 => MigrationPriority.Critical,
        >= 51 => MigrationPriority.High,
        >= 26 => MigrationPriority.Medium,
        _ => MigrationPriority.Low
    };

    public static string GetPriorityLabel(MigrationPriority priority) => priority switch
    {
        MigrationPriority.Critical => "Critical",
        MigrationPriority.High => "High",
        MigrationPriority.Medium => "Medium",
        MigrationPriority.Low => "Low",
        _ => "Unknown"
    };
}

/// <summary>
/// Aggregated analysis result for an entire project/directory.
/// </summary>
public sealed class ProjectAnalysisResult
{
    public required string RootPath { get; init; }
    public DateTime AnalyzedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
    public List<FileMigrationScore> Files { get; init; } = new();
    public List<FileMigrationScore> SkippedFiles { get; init; } = new();

    public int TotalFilesAnalyzed => Files.Count;
    public int TotalFilesSkipped => SkippedFiles.Count;

    public double AverageScore => Files.Count > 0
        ? Files.Average(f => f.TotalScore)
        : 0;

    public Dictionary<MigrationPriority, int> PriorityBreakdown => Files
        .GroupBy(f => f.Priority)
        .ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<ScoreDimension, double> AverageScoresByDimension
    {
        get
        {
            if (Files.Count == 0) return new();

            return Enum.GetValues<ScoreDimension>()
                .ToDictionary(
                    d => d,
                    d => Files.Where(f => f.Dimensions.ContainsKey(d))
                              .Select(f => f.Dimensions[d].RawScore)
                              .DefaultIfEmpty(0)
                              .Average()
                );
        }
    }

    public IEnumerable<FileMigrationScore> GetTopFiles(int count = 20) =>
        Files.OrderByDescending(f => f.TotalScore).Take(count);

    public IEnumerable<FileMigrationScore> GetFilesAboveThreshold(int threshold) =>
        Files.Where(f => f.TotalScore >= threshold).OrderByDescending(f => f.TotalScore);

    public bool HasHighPriorityFiles =>
        Files.Any(f => f.Priority == MigrationPriority.High || f.Priority == MigrationPriority.Critical);
}

/// <summary>
/// Configuration thresholds for analysis.
/// </summary>
public sealed class AnalysisThresholds
{
    /// <summary>
    /// Minimum score to include in output (0-100).
    /// </summary>
    public int MinimumScore { get; init; } = 0;

    /// <summary>
    /// Maximum number of top files to display.
    /// </summary>
    public int TopFilesCount { get; init; } = 20;

    /// <summary>
    /// Score thresholds for priority bands.
    /// </summary>
    public int CriticalThreshold { get; init; } = 76;
    public int HighThreshold { get; init; } = 51;
    public int MediumThreshold { get; init; } = 26;
}
