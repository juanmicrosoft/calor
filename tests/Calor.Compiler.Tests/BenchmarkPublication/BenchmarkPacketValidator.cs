using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests.BenchmarkPublication;

/// <summary>One fail-closed finding: a stable code, the entry it concerns, and why.</summary>
internal sealed record PacketViolation(string Code, string Subject, string Message)
{
    public override string ToString() => $"{Code} [{Subject}] {Message}";
}

/// <summary>
/// Validator for the B1 (#1276) benchmark packets under <c>eng/benchmark-publication/b1-1276/</c>, which the
/// #1422 publication gate (<c>scripts/benchmark_publication_gate.py</c>) projects into the website headline.
/// This part holds the pair-row and comparability-key rules shared by the registration and the results
/// (E008-E010). They were read from the 0.24 evidence contract's <c>benchmarkEquivalence</c> section, which
/// #1584 archived; the values below are that section's, unchanged.
///
/// <para>Every rule fails closed. Unknown tokens are errors rather than being mapped to the nearest known
/// value, and an absent field is a violation rather than a default.</para>
/// </summary>
internal static partial class BenchmarkPacketValidator
{
    public static readonly IReadOnlyList<string> ComparabilityKeyFields =
        ["pairManifestSha256", "metricSetSha256", "metricImplementationVersion", "aggregationMethod", "samplingUnit",
         "runCount", "generatorVersion", "exclusionsSha256"];

    public static readonly IReadOnlyList<string> PairRowRequiredFields =
        ["pairId", "calorPath", "calorSha256", "csharpPath", "csharpSha256", "taskStatementSha256", "inputSet",
         "expectedOutputs", "failureBehavior", "disposition", "equivalenceEvidence", "reviewer"];

    public static readonly IReadOnlySet<string> PairRowStringFields = new HashSet<string>(StringComparer.Ordinal)
        { "pairId", "calorPath", "calorSha256", "csharpPath", "csharpSha256", "taskStatementSha256", "failureBehavior", "disposition", "reviewer" };

    public static readonly IReadOnlySet<string> PairDispositions = new HashSet<string>(StringComparer.Ordinal)
        { "EQUIVALENT", "NOT-EQUIVALENT", "UNCLASSIFIED", "EXCLUDED-PRE-REGISTERED" };

    public const string SamplingUnit = "program-pair";

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    /// <summary>E008-E010: the comparability key, the pair rows, and the sampling unit of one benchmark row.</summary>
    private static IEnumerable<PacketViolation> ValidateBenchmark(JsonNode row, string id)
    {
        var bench = row["benchmark"];
        if (bench is null)
            yield break;

        if (Str(bench["samplingUnit"]) != SamplingUnit)
            yield return new("E010", id, $"sampling unit '{Str(bench["samplingUnit"])}' is not the registered '{SamplingUnit}'");
        // Every representation of the sampling unit must be the registered one, not merely self-consistent.
        if (bench["comparability"]?["samplingUnit"] is not null && Str(bench["comparability"]?["samplingUnit"]) != SamplingUnit)
            yield return new("E010", id, $"comparability samplingUnit '{Str(bench["comparability"]?["samplingUnit"])}' is not the registered '{SamplingUnit}'");

        var pairs = Array(bench["pairs"]);
        if (pairs.Count == 0)
            yield return new("E009", id, "benchmark row has an empty pair denominator");
        foreach (var pair in pairs)
        {
            var pairId = Str(pair?["pairId"]) ?? "?";
            foreach (var field in PairRowRequiredFields.Where(f => IsBlank(pair?[f])))
                yield return new("E009", id, $"pair {pairId} is missing required field '{field}'");
            foreach (var field in PairRowRequiredFields.Where(f => !IsBlank(pair?[f])))
            {
                var node = pair![field];
                var isString = node is JsonValue sv && sv.TryGetValue<string>(out _);
                if (PairRowStringFields.Contains(field) ? !isString : node is JsonValue && !isString)
                    yield return new("E009", id, $"pair {pairId} field '{field}' has the wrong JSON type");
            }
            foreach (var field in PairRowRequiredFields.Where(f => IsHashField(f) && !IsBlank(pair?[f]) && !IsSha256(Str(pair?[f]))))
                yield return new("E009", id, $"pair {pairId} field '{field}' is not a 64-hex SHA-256");
            var included = Bool(pair?["included"]);
            if (included is null)
                yield return new("E009", id, $"pair {pairId} must state included as a boolean");
            var disposition = Str(pair?["disposition"]);
            if (disposition is null || !PairDispositions.Contains(disposition))
                yield return new("E009", id, $"pair {pairId} has unknown disposition '{disposition}'");
            else if (included == true && disposition != "EQUIVALENT")
                yield return new("E009", id, $"pair {pairId} is {disposition} but included; only EQUIVALENT pairs enter metrics");
        }

        // The key is required on every benchmark row, whether or not it is compared to an earlier one.
        var key = bench["comparability"];
        foreach (var field in ComparabilityKeyFields.Where(f => IsBlank(key?[f])))
            yield return new("E008", id, $"comparability field '{field}' is missing; incomparable");
        foreach (var field in ComparabilityKeyFields.Where(f => IsHashField(f) && !IsBlank(key?[f]) && !IsSha256(Str(key?[f]))))
            yield return new("E008", id, $"comparability field '{field}' is not a 64-hex SHA-256; incomparable");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A JSON boolean, or null for anything else (absent, string, number): never a default.</summary>
    private static bool? Bool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;

    private static bool IsBlank(JsonNode? node) => node switch
    {
        null => true,
        JsonValue value => value.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s),
        JsonObject obj => obj.Count == 0,
        JsonArray array => array.Count == 0,
        _ => true,
    };

    private static bool IsHashField(string field) => field.EndsWith("Sha256", StringComparison.Ordinal);

    private static bool IsSha256(string? text) => text is not null && Sha256Hex.IsMatch(text);

    private static int? Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var i) ? i : null;

    private static List<JsonNode?> Array(JsonNode? node) =>
        node is JsonArray array ? array.ToList() : [];

    private static bool IsFullSha(string? sha) => sha is not null && FullSha.IsMatch(sha);
}

/// <summary>Assertions shared by the B1 packet tests.</summary>
internal static class PacketAssert
{
    internal static void Violation(IReadOnlyList<PacketViolation> violations, string code, params string[] alsoAllowed)
    {
        Assert.True(violations.Any(v => v.Code == code),
            $"expected a {code} violation; got:{Environment.NewLine}{Describe(violations)}");
        var unexpected = violations.Where(v => v.Code != code && !alsoAllowed.Contains(v.Code)).ToList();
        Assert.True(unexpected.Count == 0,
            $"expected only {code}; also got:{Environment.NewLine}{Describe(unexpected)}");
    }

    internal static string Describe(IReadOnlyList<PacketViolation> violations)
        => violations.Count == 0 ? "(none)" : string.Join(Environment.NewLine, violations);
}
