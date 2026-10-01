using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests.Plans;

/// <summary>
/// Machine check for the 0.23 M0 non-authorization boundary (#1371 acceptance
/// criterion 3; rules in docs/plans/safe-delegation-m0/v0.23/r0-authorization.md
/// Section 9 and amendment-001-public-proxy.md Section 6). The authoritative
/// gate values live in v0.23/gate-state.json; Markdown records under v0.23/
/// are scanned only for narrow declaration forms so prose that merely names a
/// prohibition does not trip the check.
/// </summary>
public sealed class SafeDelegationV023BoundaryTests
{
    internal const string RequiredLabel = "AI-adjudicated, public-proxy domain";

    private static readonly string[] ProcessStates =
        ["MET", "UNAVAILABLE", "EXPIRED", "REVOKED", "NOT_AUTHORIZED", "NOT_REACHED", "INVALIDATED"];

    private static readonly string[] FormalClassifications =
        ["FEASIBLE AS PROPOSED", "REQUIRES SEPARATE APPROVAL", "NOT FEASIBLE", "INSUFFICIENT INFORMATION"];

    private static readonly string[] MaintainerActions =
        ["STOP", "DEFER", "ALLOW A SEPARATE AUTHORIZATION REQUEST"];

    private static readonly string[] RequiredBoundaryFlags =
    [
        "participantEnrollment", "taskExecution", "paidExperimentCollection",
        "acceptanceServiceImplementation", "reuseBy1254Or1259", "reopening1284To1309",
        "humanContact", "restrictedDataAccess",
    ];

    /// <summary>Original-gate values that amendment 001 records and no later record may change.</summary>
    private static readonly (string Id, string Value)[] ImmutableHistory =
    [
        ("R1-original", "UNAVAILABLE"),
        ("R2A-original", "UNAVAILABLE"),
        ("R3-original", "NOT_REACHED"),
        ("R2B-original", "NOT_REACHED"),
        ("R4-original", "NOT_REACHED"),
    ];

    private static readonly Regex ClassificationDeclaration = new(
        @"scientific classification\**\s*[:|][\s*`]*(FEASIBLE AS PROPOSED|REQUIRES SEPARATE APPROVAL|NOT FEASIBLE|INSUFFICIENT INFORMATION)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProhibitedDeclaration = new(
        @"^\s*[-*|]?\s*\**\s*(participant enrollment|participant recruitment|task execution|paid (experiment )?collection|acceptance[- ]service implementation)\s*\**\s*[:|][\s*`]*(authorized|approved|permitted|yes|true|started|in progress|performed|completed|occurred|MET)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReviewFileName = new(
        @"^round-(?<n>[0-9]+)-[a-z0-9]+(-[a-z0-9]+)*\.md$", RegexOptions.Compiled);

    private static string V023Directory()
        => Path.Combine(CliTestHarness.FindRepoRoot(), "docs", "plans", "safe-delegation-m0", "v0.23");

    [Fact]
    public void GateState_SatisfiesBoundary()
    {
        var json = File.ReadAllText(Path.Combine(V023Directory(), "gate-state.json"));
        Assert.Empty(ValidateGateState(json));
    }

    [Fact]
    public void MarkdownRecords_DeclareNoProhibitedStateOrUnlabeledClassification()
    {
        var reviews = Path.Combine(V023Directory(), "reviews") + Path.DirectorySeparatorChar;
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(V023Directory(), "*.md", SearchOption.AllDirectories))
        {
            // Review transcripts quote reviewer output verbatim; they are not records of state.
            if (file.StartsWith(reviews, StringComparison.Ordinal))
                continue;
            violations.AddRange(ValidateMarkdown(Path.GetFileName(file), File.ReadAllText(file)));
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ReviewFiles_FollowProtocolNamingAndRoundLimit()
    {
        var reviews = Path.Combine(V023Directory(), "reviews");
        if (!Directory.Exists(reviews))
            return;

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(reviews, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var match = ReviewFileName.Match(name);
            if (!match.Success)
                violations.Add($"{file}: name must be round-<N>-<reviewer>.md");
            else if (!int.TryParse(match.Groups["n"].Value, out var n) || n is < 1 or > 5)
                violations.Add($"{file}: round must be 1-5");
        }

        Assert.Empty(violations);
    }

    // ---- Negative cases: the validators reject each prohibited state. ----

    [Theory]
    [InlineData("taskExecution")]
    [InlineData("participantEnrollment")]
    [InlineData("paidExperimentCollection")]
    public void Validator_RejectsBoundaryFlagSetTrue(string flag)
    {
        var json = Mutate(root => root["boundary"]!.AsObject()[flag] = true);
        Assert.Contains(ValidateGateState(json), v => v.Contains(flag, StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsMetGateWithNonMetPrerequisite()
    {
        var json = Mutate(root => Gate(root, "R3")["value"] = "MET");
        Assert.Contains(ValidateGateState(json), v => v.Contains("R3", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsMetDescendantOfRevokedR0()
    {
        var json = Mutate(root =>
        {
            Gate(root, "R0")["value"] = "REVOKED";
            Gate(root, "R2A'")["value"] = "MET";
        });
        Assert.Contains(ValidateGateState(json), v => v.Contains("R2A'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsUnknownProcessState()
    {
        var json = Mutate(root => Gate(root, "R3")["value"] = "APPROVED");
        Assert.Contains(ValidateGateState(json), v => v.Contains("APPROVED", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsOverwrittenHistory()
    {
        var json = Mutate(root =>
        {
            foreach (var entry in root["history"]!.AsArray())
            {
                if ((string?)entry!["id"] == "R2A-original")
                    entry["value"] = "MET";
            }
        });
        Assert.Contains(ValidateGateState(json), v => v.Contains("R2A-original", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsUnlabeledR5Classification()
    {
        var json = Mutate(root => Gate(root, "R5")["scientificClassification"] = "NOT FEASIBLE");
        Assert.Contains(ValidateGateState(json), v => v.Contains(RequiredLabel, StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownValidator_RejectsDeclarations_AndIgnoresProse()
    {
        Assert.NotEmpty(ValidateMarkdown("x.md", "| Task execution | authorized |"));
        Assert.NotEmpty(ValidateMarkdown("x.md", "**Participant enrollment:** started"));
        Assert.NotEmpty(ValidateMarkdown("x.md", "**Scientific classification:** `NOT FEASIBLE`"));
        Assert.Empty(ValidateMarkdown("x.md",
            "**Scientific classification:** `NOT FEASIBLE` (" + RequiredLabel + ")"));
        Assert.Empty(ValidateMarkdown("x.md", "- participant enrollment or recruitment;"));
        Assert.Empty(ValidateMarkdown("x.md", "No task execution is authorized."));
    }

    // ---- Validators ----

    internal static List<string> ValidateGateState(string json)
    {
        var violations = new List<string>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var boundary = root.GetProperty("boundary");
        foreach (var flag in RequiredBoundaryFlags)
        {
            if (!boundary.TryGetProperty(flag, out var value) || value.ValueKind != JsonValueKind.False)
                violations.Add($"boundary.{flag} must be false");
        }

        foreach (var prop in boundary.EnumerateObject())
        {
            if (!RequiredBoundaryFlags.Contains(prop.Name) && prop.Value.ValueKind != JsonValueKind.False)
                violations.Add($"boundary.{prop.Name} must be false");
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var gates = root.GetProperty("gates").EnumerateArray().ToList();
        foreach (var gate in gates)
        {
            var id = gate.GetProperty("id").GetString()!;
            var value = gate.GetProperty("value").ValueKind == JsonValueKind.Null
                ? null
                : gate.GetProperty("value").GetString();
            if (value is not null && !ProcessStates.Contains(value))
                violations.Add($"gate {id}: value {value} is not a #1370 process state");
            if (!values.TryAdd(id, value))
                violations.Add($"gate {id}: duplicate id");
        }

        foreach (var gate in gates)
        {
            var id = gate.GetProperty("id").GetString()!;
            if (values[id] != "MET")
                continue;
            foreach (var pre in gate.GetProperty("prerequisites").EnumerateArray())
            {
                var preId = pre.GetString()!;
                if (!values.TryGetValue(preId, out var preValue))
                    violations.Add($"gate {id}: unknown prerequisite {preId}");
                else if (preValue != "MET")
                    violations.Add($"gate {id}: MET while prerequisite {preId} is {preValue ?? "open"}");
            }
        }

        var r5 = gates.SingleOrDefault(g => g.GetProperty("id").GetString() == "R5");
        if (r5.ValueKind == JsonValueKind.Object)
        {
            var classification = OptionalString(r5, "scientificClassification");
            if (classification is not null && classification != "UNADJUDICATED")
            {
                if (!FormalClassifications.Contains(classification))
                    violations.Add($"R5: unknown classification {classification}");
                if (OptionalString(r5, "classificationLabel") != RequiredLabel)
                    violations.Add($"R5: classification must carry the label \"{RequiredLabel}\"");
            }

            var action = OptionalString(r5, "maintainerAction");
            if (action is not null && !MaintainerActions.Contains(action))
                violations.Add($"R5: unknown maintainer action {action}");
            if (values.GetValueOrDefault("R5") == "MET"
                && (classification is null || classification == "UNADJUDICATED" || action is null))
                violations.Add("R5: MET requires a formal classification and a separate maintainer action");
        }

        var history = root.GetProperty("history").EnumerateArray()
            .ToDictionary(h => h.GetProperty("id").GetString()!, h => h.GetProperty("value").GetString());
        foreach (var (id, expected) in ImmutableHistory)
        {
            if (!history.TryGetValue(id, out var actual) || actual != expected)
                violations.Add($"history {id}: must remain {expected} (amendment 001 Section 2)");
        }

        return violations;
    }

    internal static List<string> ValidateMarkdown(string name, string text)
    {
        var violations = new List<string>();
        var lineNumber = 0;
        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            if (ClassificationDeclaration.IsMatch(line) && !line.Contains(RequiredLabel, StringComparison.Ordinal))
                violations.Add($"{name}:{lineNumber}: classification without \"{RequiredLabel}\"");
            if (ProhibitedDeclaration.IsMatch(line))
                violations.Add($"{name}:{lineNumber}: declares a prohibited activity as authorized or performed");
        }

        return violations;
    }

    private static string? OptionalString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Mutate(Action<System.Text.Json.Nodes.JsonObject> change)
    {
        var json = File.ReadAllText(Path.Combine(V023Directory(), "gate-state.json"));
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        change(root);
        return root.ToJsonString();
    }

    private static System.Text.Json.Nodes.JsonObject Gate(System.Text.Json.Nodes.JsonObject root, string id)
        => root["gates"]!.AsArray().Single(g => (string?)g!["id"] == id)!.AsObject();
}
