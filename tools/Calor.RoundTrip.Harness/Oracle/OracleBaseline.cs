using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: reviewed findings the gate tolerates while they are open. Each entry names one member of
/// one file and its exact verdict fingerprint. A listed member that behaves differently in any other way
/// fails (the fingerprint changed), and a listed member that now agrees fails too (the entry is stale),
/// so the baseline only ever shrinks. A file whose conversion records a loss never needs an entry.
/// </summary>
public sealed record OracleBaseline
{
    public const string Schema = "calor.member-oracle-baseline/1";

    [JsonPropertyName("schema")]
    public string SchemaId { get; init; } = Schema;

    [JsonPropertyName("entries")]
    public List<OracleBaselineEntry> Entries { get; init; } = [];

    public static string DefaultPath(string repoRoot) => Path.Combine(repoRoot, "eng", "member-oracle-baseline.json");

    public static OracleBaseline Load(string path)
    {
        if (!File.Exists(path))
            return new OracleBaseline();
        var baseline = JsonSerializer.Deserialize<OracleBaseline>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty");
        if (baseline.SchemaId != Schema)
            throw new InvalidOperationException($"{path}: schema '{baseline.SchemaId}', expected '{Schema}'");
        var duplicate = baseline.Entries.GroupBy(e => (e.Project, e.File, e.Member)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"{path}: duplicate entry {duplicate.Key}");
        if (baseline.Entries.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.Finding) || string.IsNullOrWhiteSpace(e.Fingerprint) || string.IsNullOrWhiteSpace(e.Witness)) is { } bare)
            throw new InvalidOperationException($"{path}: entry {bare.Project} {bare.File} {bare.Member} must cite a finding, a fingerprint and a witness");
        return baseline;
    }
}

public sealed record OracleBaselineEntry
{
    [JsonPropertyName("project")] public required string Project { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }

    /// <summary>The member key, <c>Type::Member</c>, as the report prints it.</summary>
    [JsonPropertyName("member")] public required string Member { get; init; }

    /// <summary>What is wrong, in a sentence, and where it is tracked.</summary>
    [JsonPropertyName("finding")] public required string Finding { get; init; }

    /// <summary>The member's verdict fingerprint (every differing input with both full observations), verbatim from the report.</summary>
    [JsonPropertyName("fingerprint")] public required string Fingerprint { get; init; }

    /// <summary>The member's first witness, for reviewers; the gate matches the fingerprint.</summary>
    [JsonPropertyName("witness")] public required string Witness { get; init; }
}
