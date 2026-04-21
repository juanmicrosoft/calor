using System.Text.Json.Serialization;

namespace Calor.Compiler.Experiments;

/// <summary>
/// One entry in <c>docs/experiments/registry.json</c>. The registry is append-only —
/// edits to existing entries are rejected by CI (see <c>docs/plans/calor-native-type-system-v2.md</c>
/// §5.0f). Lineage through supersedes chains carries hypothesis state across lifecycle
/// transitions (Stage 1 → Stage 2 → behind-flag → promoted/held/dropped).
///
/// Field names use snake_case on the wire to match the plan's YAML examples and so
/// humans who edit the JSON can match the plan 1:1.
/// </summary>
public sealed class RegistryEntry
{
    /// <summary>
    /// Unique identifier, e.g., <c>TIER1A-flow-option-tracking</c> or <c>TIER1A-flow-option-tracking-stage2</c>.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Engineering domain tag: <c>Dataflow</c>, <c>Pattern</c>, <c>Elaboration</c>,
    /// <c>TypeSystem</c>, or <c>Codegen</c>. Used for tuple-based hypothesis identity.
    /// </summary>
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    /// <summary>
    /// Plain-English hypothesis claim.
    /// </summary>
    [JsonPropertyName("hypothesis")]
    public string Hypothesis { get; set; } = "";

    /// <summary>
    /// Target code class for tuple-based hypothesis identity — what category of code
    /// the hypothesis affects (e.g., <c>unwrap-sites</c>, <c>option-match-sites</c>).
    /// Part of the two-kill-rule identity tuple.
    /// </summary>
    [JsonPropertyName("tuple_code_class")]
    public string TupleCodeClass { get; set; } = "";

    /// <summary>
    /// Expected direction of effect on the primary metric: <c>up</c> or <c>down</c>.
    /// Part of the two-kill-rule identity tuple.
    /// </summary>
    [JsonPropertyName("tuple_effect_direction")]
    public string TupleEffectDirection { get; set; } = "";

    /// <summary>
    /// Lifecycle status:
    /// <c>pre-registered-stage-1</c> | <c>pre-registered-stage-2</c> | <c>behind-flag</c>
    /// | <c>promoted</c> | <c>held</c> | <c>dropped</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>
    /// ID of the predecessor entry this supersedes, or null for origin entries.
    /// Chain walks back to null.
    /// </summary>
    [JsonPropertyName("supersedes")]
    public string? Supersedes { get; set; }

    /// <summary>
    /// For held entries: the engineer responsible for the quarterly review.
    /// Null or empty means the entry has no owner — the §4.4 auto-drop rule applies.
    /// </summary>
    [JsonPropertyName("hold_owner")]
    public string? HoldOwner { get; set; }

    /// <summary>
    /// For held entries: ISO-8601 date when the entry entered Held state.
    /// Null outside Held.
    /// </summary>
    [JsonPropertyName("hold_started")]
    public string? HoldStarted { get; set; }

    /// <summary>
    /// For held entries: ISO-8601 date when the next quarterly review is due.
    /// The §4.4 stale-holds rule fires when the date is in the past.
    /// </summary>
    [JsonPropertyName("quarterly_review_due")]
    public string? QuarterlyReviewDue { get; set; }

    /// <summary>
    /// For re-proposals (§4.5 anti-evasion): rationale for why the original hypothesis'
    /// metric was wrong. Required on metric-substitution re-proposals.
    /// </summary>
    [JsonPropertyName("metric_change_rationale")]
    public string? MetricChangeRationale { get; set; }

    /// <summary>
    /// CI-filled: git commit SHA of the PR that introduced this entry.
    /// Not author-controlled (prevents <c>--date</c> backdating).
    /// </summary>
    [JsonPropertyName("commit_sha")]
    public string? CommitSha { get; set; }

    /// <summary>
    /// CI-filled: ISO-8601 timestamp from the GitHub API at PR merge.
    /// Not author-controlled. Used for audit-trail chronological ordering.
    /// </summary>
    [JsonPropertyName("merged_at")]
    public string? MergedAt { get; set; }

    /// <summary>
    /// Optional: link to the decision memo PR for entries that reached a terminal state.
    /// </summary>
    [JsonPropertyName("decision_memo_url")]
    public string? DecisionMemoUrl { get; set; }

    /// <summary>
    /// Optional: for human-override cases where a reviewer chose a different action than
    /// the agent recommended — the rationale is tracked here for later calibration analysis.
    /// </summary>
    [JsonPropertyName("override_rationale")]
    public string? OverrideRationale { get; set; }
}

/// <summary>
/// Root document for <c>registry.json</c>. A flat array of entries, append-only.
/// </summary>
public sealed class RegistryDocument
{
    [JsonPropertyName("entries")]
    public List<RegistryEntry> Entries { get; set; } = new();
}
