using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Evaluation.LlmTasks;

/// <summary>
/// Defines a programming task for LLM-based evaluation.
/// Each task includes prompts for both Calor and C# code generation,
/// along with test cases to verify correctness.
/// </summary>
public record LlmTaskDefinition
{
    /// <summary>
    /// Unique identifier for this task (e.g., "algo-001").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name (e.g., "Factorial Function").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Task category for grouping (e.g., "basic-algorithms", "contracts", "data-structures").
    /// </summary>
    public string Category { get; init; } = "general";

    /// <summary>
    /// Difficulty level (1-5, where 1 is easiest).
    /// </summary>
    public int Difficulty { get; init; } = 1;

    /// <summary>
    /// Language-neutral prompt for code generation (v2 format).
    /// When set, this is used for both languages, with the system prompt
    /// providing language-specific guidance via skills files.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>
    /// Language-specific prompts for code generation (v1 format).
    /// Used when Prompt is not set for backward compatibility.
    /// </summary>
    public TaskPrompts? Prompts { get; init; }

    /// <summary>
    /// Gets the prompt for a specific language.
    /// Uses the neutral Prompt if available, otherwise falls back to language-specific prompts.
    /// </summary>
    public string GetPrompt(string language)
    {
        // Prefer neutral prompt (v2 format)
        if (!string.IsNullOrEmpty(Prompt))
        {
            return Prompt;
        }

        // Fall back to language-specific prompts (v1 format)
        if (Prompts != null)
        {
            return language.ToLowerInvariant() switch
            {
                "calor" => Prompts.Calor,
                "csharp" or "c#" => Prompts.CSharp,
                _ => Prompts.CSharp // Default to C#
            };
        }

        throw new InvalidOperationException($"Task {Id} has no prompt defined");
    }

    /// <summary>
    /// Whether this task uses neutral prompts (v2 format).
    /// </summary>
    public bool UsesNeutralPrompt => !string.IsNullOrEmpty(Prompt);

    /// <summary>
    /// Test cases to verify the generated code.
    /// </summary>
    public List<TaskTestCase> TestCases { get; init; } = new();

    /// <summary>
    /// Scoring weights for different aspects.
    /// </summary>
    public TaskScoring Scoring { get; init; } = new();

    /// <summary>
    /// Optional hints or context for the task.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Expected function/method signature for validation.
    /// </summary>
    public TaskSignature? ExpectedSignature { get; init; }

    /// <summary>
    /// Tags for filtering and categorization.
    /// </summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>
    /// Bug prevention documentation for effect discipline benchmarks.
    /// Describes the real-world bug this task prevents and how each language addresses it.
    /// </summary>
    public BugPrevention? BugPrevention { get; init; }
}

/// <summary>
/// Documents how a task prevents real-world bugs and compares approaches between languages.
/// Used for fair comparison in effect discipline benchmarks.
/// </summary>
public record BugPrevention
{
    /// <summary>
    /// Description of the actual real-world bug this task is designed to prevent.
    /// </summary>
    public required string RealWorldBug { get; init; }

    /// <summary>
    /// How Calor's effect system prevents this bug.
    /// </summary>
    public required string CalorApproach { get; init; }

    /// <summary>
    /// How C# best practices (DI, [Pure], interfaces) can prevent this bug.
    /// </summary>
    public required string CsharpApproach { get; init; }
}

/// <summary>
/// Language-specific prompts for code generation.
/// </summary>
public record TaskPrompts
{
    /// <summary>
    /// Prompt for generating Calor code.
    /// </summary>
    public required string Calor { get; init; }

    /// <summary>
    /// Prompt for generating C# code.
    /// </summary>
    public required string CSharp { get; init; }
}

/// <summary>
/// A single test case for verifying generated code.
/// </summary>
public record TaskTestCase
{
    /// <summary>
    /// Input values for the function.
    /// </summary>
    public required JsonElement[] Input { get; init; }

    /// <summary>
    /// Expected output value. Optional for test cases that expect contract violations.
    /// </summary>
    public JsonElement? Expected { get; init; }

    /// <summary>
    /// Optional description of what this test case verifies.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this test case should trigger a contract violation (for contract testing).
    /// </summary>
    public bool ExpectsContractViolation { get; init; }
}

/// <summary>
/// Scoring weights for task evaluation.
/// </summary>
public record TaskScoring
{
    /// <summary>
    /// Weight for successful compilation (0-1).
    /// </summary>
    public double Compilation { get; init; } = 0.3;

    /// <summary>
    /// Weight for passing test cases (0-1).
    /// </summary>
    public double TestCases { get; init; } = 0.5;

    /// <summary>
    /// Weight for contract verification (Calor only, 0-1).
    /// </summary>
    public double Contracts { get; init; } = 0.2;

    /// <summary>
    /// Validates that weights sum to 1.0.
    /// </summary>
    public bool IsValid => Math.Abs(Compilation + TestCases + Contracts - 1.0) < 0.001;
}

/// <summary>
/// Expected function signature for validation.
/// </summary>
public record TaskSignature
{
    /// <summary>
    /// Expected function name.
    /// </summary>
    public required string FunctionName { get; init; }

    /// <summary>
    /// Expected parameter types (e.g., ["int", "int"]).
    /// </summary>
    public List<string> ParameterTypes { get; init; } = new();

    /// <summary>
    /// Expected return type (e.g., "int").
    /// </summary>
    public required string ReturnType { get; init; }
}

/// <summary>
/// Manifest containing all task definitions.
/// </summary>
public record LlmTaskManifest
{
    /// <summary>
    /// Manifest version.
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Description of this manifest.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// All task definitions.
    /// </summary>
    public List<LlmTaskDefinition> Tasks { get; init; } = new();

    /// <summary>
    /// Loads a manifest from a JSON file.
    /// </summary>
    public static async Task<LlmTaskManifest> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonSerializer.Deserialize<LlmTaskManifest>(json, options)
            ?? throw new InvalidOperationException($"Failed to deserialize manifest from {path}");
    }

    /// <summary>
    /// Saves this manifest to a JSON file.
    /// </summary>
    public async Task SaveAsync(string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(this, options);
        await File.WriteAllTextAsync(path, json);
    }

    /// <summary>
    /// Gets tasks filtered by category.
    /// </summary>
    public IEnumerable<LlmTaskDefinition> GetByCategory(string category) =>
        Tasks.Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets tasks filtered by difficulty.
    /// </summary>
    public IEnumerable<LlmTaskDefinition> GetByDifficulty(int difficulty) =>
        Tasks.Where(t => t.Difficulty == difficulty);

    /// <summary>
    /// Gets tasks filtered by tag.
    /// </summary>
    public IEnumerable<LlmTaskDefinition> GetByTag(string tag) =>
        Tasks.Where(t => t.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
}
