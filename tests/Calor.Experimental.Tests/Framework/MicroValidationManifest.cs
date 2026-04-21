using System.Text.Json.Serialization;

namespace Calor.Experimental.Tests.Framework;

/// <summary>
/// Per-feature metadata describing the micro-validation test set for one experimental
/// hypothesis (§5.0e of <c>docs/plans/calor-native-type-system-v2.md</c>).
///
/// The manifest lives at <c>tests/Calor.Experimental.Tests/TIERxY-name/manifest.json</c>
/// alongside <c>positive/</c>, <c>negative/</c>, and <c>edge/</c> subdirectories
/// containing the actual <c>.calr</c> programs.
///
/// Field names use snake_case on the wire to match the plan's conventions and keep
/// the JSON easy to edit by hand.
/// </summary>
public sealed class MicroValidationManifest
{
    /// <summary>
    /// The hypothesis ID this micro-validation set belongs to, matching an entry in
    /// <c>docs/experiments/registry.json</c>. Example: <c>TIER1A-flow-option-tracking</c>.
    /// </summary>
    [JsonPropertyName("hypothesis_id")]
    public string HypothesisId { get; set; } = "";

    /// <summary>
    /// The experimental feature flag gating this hypothesis (matches the flag name
    /// passed to <c>calor --experimental &lt;name&gt;</c>).
    /// </summary>
    [JsonPropertyName("experimental_flag")]
    public string ExperimentalFlag { get; set; } = "";

    /// <summary>
    /// The diagnostic code the feature emits when it detects a positive case
    /// (e.g., <c>Calor1200</c>). Used by the micro-validation runner to distinguish
    /// true-positive from false-positive behavior.
    /// </summary>
    [JsonPropertyName("expected_diagnostic_code")]
    public string ExpectedDiagnosticCode { get; set; } = "";

    /// <summary>
    /// One-line description of what the positive cases are designed to exercise.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}
