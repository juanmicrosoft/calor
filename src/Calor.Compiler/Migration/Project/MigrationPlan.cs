using Calor.Compiler.Analysis;

namespace Calor.Compiler.Migration.Project;

/// <summary>
/// Represents a plan for migrating a project or set of files.
/// </summary>
public sealed class MigrationPlan
{
    public required string ProjectPath { get; init; }
    public required MigrationDirection Direction { get; init; }
    public required List<MigrationPlanEntry> Entries { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public MigrationPlanOptions Options { get; init; } = new();

    public int TotalFiles => Entries.Count;
    public int ConvertibleFiles => Entries.Count(e => e.Convertibility == FileConvertibility.Full);
    public int PartialFiles => Entries.Count(e => e.Convertibility == FileConvertibility.Partial);
    public int SkippedFiles => Entries.Count(e => e.Convertibility == FileConvertibility.Skip);

    /// <summary>
    /// Gets estimated issues based on feature analysis.
    /// </summary>
    public int EstimatedIssues => Entries.Sum(e => e.EstimatedIssues);
}

/// <summary>
/// An entry in the migration plan representing a single file.
/// </summary>
public sealed class MigrationPlanEntry
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public required FileConvertibility Convertibility { get; init; }
    public List<string> DetectedFeatures { get; init; } = new();
    public List<string> PotentialIssues { get; init; } = new();
    public int EstimatedIssues { get; init; }
    public long FileSizeBytes { get; init; }
    public string? SkipReason { get; init; }

    /// <summary>
    /// Analysis score from MigrationAnalyzer, populated during the analyze phase.
    /// </summary>
    public FileMigrationScore? AnalysisScore { get; set; }
}

/// <summary>
/// Convertibility level for a file.
/// </summary>
public enum FileConvertibility
{
    /// <summary>File can be fully converted.</summary>
    Full,
    /// <summary>File can be partially converted, needs review.</summary>
    Partial,
    /// <summary>File should be skipped.</summary>
    Skip
}

/// <summary>
/// Options for migration plan creation.
/// </summary>
public sealed class MigrationPlanOptions
{
    /// <summary>Whether to include test files.</summary>
    public bool IncludeTests { get; set; } = true;

    /// <summary>Whether to include generated files (*.g.cs, *.Designer.cs).</summary>
    public bool IncludeGenerated { get; set; } = false;

    /// <summary>File patterns to exclude.</summary>
    public List<string> ExcludePatterns { get; init; } = new()
    {
        "*.g.cs",
        "*.Designer.cs",
        "GlobalUsings.cs",
        "AssemblyInfo.cs"
    };

    /// <summary>Maximum file size to process (bytes).</summary>
    public long MaxFileSizeBytes { get; set; } = 1_000_000; // 1MB

    /// <summary>Whether to run in parallel.</summary>
    public bool Parallel { get; set; } = true;

    /// <summary>Maximum degree of parallelism.</summary>
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>Whether to include benchmark metrics.</summary>
    public bool IncludeBenchmark { get; set; } = false;

    /// <summary>Whether to skip the migration analysis phase.</summary>
    public bool SkipAnalyze { get; set; } = false;

    /// <summary>Whether to skip the Z3 contract verification phase.</summary>
    public bool SkipVerify { get; set; } = false;

    /// <summary>Z3 verification timeout in milliseconds.</summary>
    public uint VerificationTimeoutMs { get; set; } = Verification.Z3.VerificationOptions.DefaultTimeoutMs;
}
