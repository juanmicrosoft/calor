using System.Text.Json.Serialization;

namespace Calor.Compiler.Migration;

/// <summary>
/// Complete migration report for a file or project.
/// </summary>
public sealed class MigrationReport
{
    public required string ReportId { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public required MigrationDirection Direction { get; init; }
    public required MigrationSummary Summary { get; init; }
    public List<FileMigrationResult> FileResults { get; init; } = new();
    public BenchmarkSummary? Benchmark { get; init; }
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// Direction of the migration.
/// </summary>
public enum MigrationDirection
{
    CSharpToCalor,
    CalorToCSharp
}

/// <summary>
/// Summary of migration results.
/// </summary>
public sealed class MigrationSummary
{
    public int TotalFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int PartialFiles { get; set; }
    public int FailedFiles { get; set; }
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public double SuccessRate => TotalFiles > 0 ? (double)SuccessfulFiles / TotalFiles * 100 : 0;

    public List<string> MostCommonIssues { get; init; } = new();
    public List<string> UnsupportedFeatures { get; init; } = new();
}

/// <summary>
/// Result of migrating a single file.
/// </summary>
public sealed class FileMigrationResult
{
    public required string SourcePath { get; init; }
    public required string? OutputPath { get; init; }
    public required FileMigrationStatus Status { get; init; }
    public TimeSpan Duration { get; init; }
    public List<ConversionIssue> Issues { get; init; } = new();
    public FileMetrics? Metrics { get; init; }
}

/// <summary>
/// Status of a file migration.
/// </summary>
public enum FileMigrationStatus
{
    Success,
    Partial,
    Failed,
    Skipped
}

/// <summary>
/// Metrics for a migrated file.
/// </summary>
public sealed class FileMetrics
{
    public int OriginalLines { get; set; }
    public int OutputLines { get; set; }
    public int OriginalTokens { get; set; }
    public int OutputTokens { get; set; }
    public int OriginalCharacters { get; set; }
    public int OutputCharacters { get; set; }

    public double LineReduction => OriginalLines > 0
        ? (1.0 - (double)OutputLines / OriginalLines) * 100 : 0;
    public double TokenReduction => OriginalTokens > 0
        ? (1.0 - (double)OutputTokens / OriginalTokens) * 100 : 0;
    public double CharReduction => OriginalCharacters > 0
        ? (1.0 - (double)OutputCharacters / OriginalCharacters) * 100 : 0;
}

/// <summary>
/// Benchmark summary comparing original and converted code.
/// </summary>
public sealed class BenchmarkSummary
{
    public int TotalOriginalTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalOriginalLines { get; set; }
    public int TotalOutputLines { get; set; }

    public double TokenSavingsPercent => TotalOriginalTokens > 0
        ? (1.0 - (double)TotalOutputTokens / TotalOriginalTokens) * 100 : 0;
    public double LineSavingsPercent => TotalOriginalLines > 0
        ? (1.0 - (double)TotalOutputLines / TotalOriginalLines) * 100 : 0;
    public double OverallAdvantage => TotalOutputTokens > 0
        ? (double)TotalOriginalTokens / TotalOutputTokens : 1.0;

    public Dictionary<string, double> CategoryAdvantages { get; init; } = new();
}
