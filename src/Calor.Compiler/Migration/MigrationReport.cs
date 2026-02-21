using System.Text.Json.Serialization;
using Calor.Compiler.Analysis;
using Calor.Compiler.Verification.Z3;

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
    public AnalysisSummaryReport? Analysis { get; init; }
    public VerificationSummaryReport? Verification { get; init; }
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
    public FileAnalysisResult? Analysis { get; init; }
    public FileVerificationSummary? Verification { get; set; }
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

/// <summary>
/// Aggregated analysis summary for the migration report.
/// </summary>
public sealed class AnalysisSummaryReport
{
    public int FilesAnalyzed { get; init; }
    public double AverageScore { get; init; }
    public Dictionary<MigrationPriority, int> PriorityBreakdown { get; init; } = new();
    public Dictionary<ScoreDimension, double> DimensionAverages { get; init; } = new();
    public TimeSpan Duration { get; init; }
    public List<FileAnalysisResult> FileResults { get; init; } = new();
}

/// <summary>
/// Per-file analysis result included in the migration report.
/// </summary>
public sealed class FileAnalysisResult
{
    public required string FilePath { get; init; }
    public double Score { get; init; }
    public MigrationPriority Priority { get; init; }
    public Dictionary<ScoreDimension, double> DimensionScores { get; init; } = new();
    public List<UnsupportedConstruct> UnsupportedConstructs { get; init; } = new();
}

/// <summary>
/// Aggregated verification summary for the migration report.
/// </summary>
public sealed class VerificationSummaryReport
{
    public int FilesVerified { get; init; }
    public int FilesSkipped { get; init; }
    public int TotalContracts { get; init; }
    public int Proven { get; init; }
    public int Unproven { get; init; }
    public int Disproven { get; init; }
    public int Unsupported { get; init; }
    public int ContractsSkipped { get; init; }
    public bool Z3Available { get; init; }
    public TimeSpan Duration { get; init; }
    public List<FileVerificationSummary> FileResults { get; init; } = new();

    public double ProvenRate => TotalContracts > 0 ? (double)Proven / TotalContracts * 100 : 0;
}

/// <summary>
/// Per-file verification summary included in the migration report.
/// </summary>
public sealed class FileVerificationSummary
{
    public required string CalorPath { get; init; }
    public int TotalContracts { get; init; }
    public int Proven { get; init; }
    public int Unproven { get; init; }
    public int Disproven { get; init; }
    public List<string> DisprovenDetails { get; init; } = new();

    public double ProvenRate => TotalContracts > 0 ? (double)Proven / TotalContracts * 100 : 0;
}
