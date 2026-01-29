using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opal.Evaluation.Benchmarks;

/// <summary>
/// Loads and manages benchmark metadata from manifest files.
/// </summary>
public class BenchmarkManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Version of the manifest format.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Description of this benchmark suite.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// List of benchmark entries.
    /// </summary>
    public List<BenchmarkEntry> Benchmarks { get; set; } = new();

    /// <summary>
    /// Comprehension questions organized by file ID.
    /// </summary>
    public Dictionary<string, List<QuestionEntry>> ComprehensionQuestions { get; set; } = new();

    /// <summary>
    /// Edit task definitions.
    /// </summary>
    public List<EditTaskEntry> EditTasks { get; set; } = new();

    /// <summary>
    /// Bug detection scenarios.
    /// </summary>
    public List<BugEntry> BugScenarios { get; set; } = new();

    /// <summary>
    /// Loads a manifest from a JSON file.
    /// </summary>
    public static async Task<BenchmarkManifest> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<BenchmarkManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize manifest from {path}");
    }

    /// <summary>
    /// Saves the manifest to a JSON file.
    /// </summary>
    public async Task SaveAsync(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    /// <summary>
    /// Gets benchmarks filtered by category.
    /// </summary>
    public IEnumerable<BenchmarkEntry> GetByCategory(string category)
    {
        return Benchmarks.Where(b =>
            string.Equals(b.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets benchmarks filtered by level.
    /// </summary>
    public IEnumerable<BenchmarkEntry> GetByLevel(int level)
    {
        return Benchmarks.Where(b => b.Level == level);
    }

    /// <summary>
    /// Gets benchmarks filtered by feature.
    /// </summary>
    public IEnumerable<BenchmarkEntry> GetByFeature(string feature)
    {
        return Benchmarks.Where(b =>
            b.Features.Contains(feature, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// A single benchmark entry in the manifest.
/// </summary>
public class BenchmarkEntry
{
    /// <summary>
    /// Unique identifier for this benchmark.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Display name for the benchmark.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Category (TokenEconomics, Comprehension, EditPrecision, ErrorDetection).
    /// </summary>
    public string Category { get; set; } = "TokenEconomics";

    /// <summary>
    /// Relative path to the OPAL file.
    /// </summary>
    public required string OpalFile { get; set; }

    /// <summary>
    /// Relative path to the C# file.
    /// </summary>
    public required string CSharpFile { get; set; }

    /// <summary>
    /// Complexity level (1-5).
    /// </summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// Feature tags for filtering.
    /// </summary>
    public List<string> Features { get; set; } = new();

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string Notes { get; set; } = "";

    /// <summary>
    /// Expected OPAL advantage ratio (for validation).
    /// </summary>
    public double? ExpectedAdvantage { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// Display name combining ID and name.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Name) ? Id : $"[{Id}] {Name}";
}

/// <summary>
/// A comprehension question entry.
/// </summary>
public class QuestionEntry
{
    /// <summary>
    /// Unique question identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The question text.
    /// </summary>
    public required string Question { get; set; }

    /// <summary>
    /// Expected answer.
    /// </summary>
    public required string Answer { get; set; }

    /// <summary>
    /// Question category (semantics, behavior, structure).
    /// </summary>
    public string Category { get; set; } = "semantics";

    /// <summary>
    /// Difficulty level.
    /// </summary>
    public int Difficulty { get; set; } = 1;
}

/// <summary>
/// An edit task entry.
/// </summary>
public class EditTaskEntry
{
    /// <summary>
    /// Unique task identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Description of the edit to perform.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Path to OPAL before file.
    /// </summary>
    public required string OpalBefore { get; set; }

    /// <summary>
    /// Path to OPAL expected after file.
    /// </summary>
    public required string OpalAfter { get; set; }

    /// <summary>
    /// Path to C# before file.
    /// </summary>
    public required string CSharpBefore { get; set; }

    /// <summary>
    /// Path to C# expected after file.
    /// </summary>
    public required string CSharpAfter { get; set; }

    /// <summary>
    /// Category of edit (add, modify, delete, refactor).
    /// </summary>
    public string Category { get; set; } = "modify";

    /// <summary>
    /// Difficulty level.
    /// </summary>
    public int Level { get; set; } = 1;
}

/// <summary>
/// A bug scenario entry for error detection.
/// </summary>
public class BugEntry
{
    /// <summary>
    /// Unique bug identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Description of the bug.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Bug category (null_reference, bounds_check, contract_violation, etc).
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Path to buggy OPAL file.
    /// </summary>
    public required string OpalBuggy { get; set; }

    /// <summary>
    /// Path to fixed OPAL file.
    /// </summary>
    public required string OpalFixed { get; set; }

    /// <summary>
    /// Path to buggy C# file.
    /// </summary>
    public required string CSharpBuggy { get; set; }

    /// <summary>
    /// Path to fixed C# file.
    /// </summary>
    public required string CSharpFixed { get; set; }

    /// <summary>
    /// Expected error message (if applicable).
    /// </summary>
    public string? ExpectedError { get; set; }

    /// <summary>
    /// Difficulty level.
    /// </summary>
    public int Level { get; set; } = 1;
}
