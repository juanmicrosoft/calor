using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests.Plans;

/// <summary>
/// Machine check for the 0.23 M0 non-authorization boundary (#1371 acceptance
/// criterion 3; rules in docs/plans/safe-delegation-m0/v0.23/r0-authorization.md
/// Section 9 and amendment-001-public-proxy.md Section 6). The authoritative
/// gate values live in v0.23/gate-state.json and are validated against a fixed
/// copy of the amended graph held here. Markdown records under v0.23/ are
/// scanned heuristically for declaration forms; prose that names a prohibition
/// under a negation does not trip the check.
/// </summary>
public sealed class SafeDelegationV023BoundaryTests
{
    internal const string RequiredLabel = "AI-adjudicated, public-proxy domain";

    private const decimal CashCapUsd = 200m;
    private const int MaxRounds = 5;
    private const decimal MaintainerHoursCap = 20m;
    private const decimal AgentSessionHoursCap = 60m;

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

    /// <summary>
    /// The amended dependency graph (amendment-001 Section 4). Held here, not read
    /// from gate-state.json, so editing the JSON cannot loosen a prerequisite.
    /// </summary>
    private static readonly Dictionary<string, string[]> AmendedGraph = new(StringComparer.Ordinal)
    {
        ["R0"] = [],
        ["R1"] = ["R0"],
        ["R2A'"] = ["R0"],
        ["R3"] = ["R1", "R2A'"],
        ["R2B'"] = ["R1", "R2A'", "R3"],
        ["R4"] = ["R1", "R2A'", "R3", "R2B'"],
        ["R5"] = ["R4"],
    };

    /// <summary>Original-gate values that amendment 001 records and no later record may change.</summary>
    private static readonly (string Id, string Value)[] ImmutableHistory =
    [
        ("R1-original", "UNAVAILABLE"),
        ("R2A-original", "UNAVAILABLE"),
        ("R3-original", "NOT_REACHED"),
        ("R2B-original", "NOT_REACHED"),
        ("R4-original", "NOT_REACHED"),
    ];

    private const string Activity =
        @"(participant enrollment|participant recruitment|task execution|paid (experiment )?collection|acceptance[- ]service implementation)";

    private const string StateWord =
        @"(authorized|approved|permitted|yes|true|started|underway|in progress|performed|completed|occurred|MET)";

    private static readonly Regex ClassificationDeclaration = new(
        @"classification\**\s*[:|][\s*`]*(FEASIBLE AS PROPOSED|REQUIRES SEPARATE APPROVAL|NOT FEASIBLE|INSUFFICIENT INFORMATION)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Task execution: started", "| Task execution | authorized |"
    private static readonly Regex ProhibitedFieldDeclaration = new(
        @"\b" + Activity + @"\s*\**\s*[:|][\s*`]*" + StateWord + @"\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Task execution is authorized", "paid collection has been completed"
    private static readonly Regex ProhibitedSentenceDeclaration = new(
        @"\b" + Activity + @"\b[^.;!?|]{0,40}?\b(is|was|are|were|has been|have been)\s+(now\s+)?" + StateWord + @"\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Negation = new(
        @"\b(no|not|never|nor|none|without|prohibit\w*|forbid\w*)\b",
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
            // Correctly named review transcripts quote reviewer output verbatim; they
            // are not records of state. Anything else under reviews/ is scanned.
            if (file.StartsWith(reviews, StringComparison.Ordinal)
                && ReviewFileName.IsMatch(Path.GetFileName(file)))
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
            var match = ReviewFileName.Match(Path.GetFileName(file));
            if (!match.Success)
                violations.Add($"{file}: name must be round-<N>-<reviewer>.md");
            else if (!int.TryParse(match.Groups["n"].Value, out var n) || n is < 1 or > MaxRounds)
                violations.Add($"{file}: round must be 1-{MaxRounds}");
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
        Assert.Contains(ValidateGateState(json), v => v.Contains("R3: MET while prerequisite R1", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsMetDescendantOfRevokedR0()
    {
        var json = Mutate(root =>
        {
            Gate(root, "R0")["value"] = "REVOKED";
            Gate(root, "R2A'")["value"] = "MET";
        });
        Assert.Contains(ValidateGateState(json), v => v.Contains("R2A': MET while prerequisite R0", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsClearedPrerequisites()
    {
        var json = Mutate(root =>
        {
            Gate(root, "R3")["value"] = "MET";
            Gate(root, "R3")["prerequisites"] = new JsonArray();
        });
        var violations = ValidateGateState(json);
        Assert.Contains(violations, v => v.Contains("R3: prerequisites must be", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("R3: MET while prerequisite R1", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsDeletedGate()
    {
        var json = Mutate(root =>
        {
            var gates = root["gates"]!.AsArray();
            gates.Remove(gates.Single(g => (string?)g!["id"] == "R5"));
        });
        Assert.Contains(ValidateGateState(json), v => v.Contains("R5: missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsUnknownProcessState()
    {
        var json = Mutate(root => Gate(root, "R3")["value"] = "APPROVED");
        Assert.Contains(ValidateGateState(json), v => v.Contains("APPROVED", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsRaisedCaps()
    {
        var json = Mutate(root =>
        {
            root["limits"]!["cashCapUsd"] = 500;
            root["limits"]!["maxReviewRoundsPerArtifact"] = 10;
            root["limits"]!["agentSessionHoursCap"] = 600;
        });
        var violations = ValidateGateState(json);
        Assert.Contains(violations, v => v.Contains("agentSessionHoursCap", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("cashCapUsd", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("maxReviewRoundsPerArtifact", StringComparison.Ordinal));
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
    public void Validator_RejectsR5MetWithoutSeparateAction()
    {
        var json = Mutate(root =>
        {
            foreach (var gate in root["gates"]!.AsArray())
                gate!["value"] = "MET";
            Gate(root, "R5")["scientificClassification"] = "NOT FEASIBLE";
            Gate(root, "R5")["classificationLabel"] = RequiredLabel;
            Gate(root, "R5")["maintainerAction"] = null;
        });
        Assert.Contains(ValidateGateState(json), v => v.Contains("maintainer action", StringComparison.Ordinal));
    }

    // ---- Terminal-state invariants (0.23 close-out). ----

    [Fact]
    public void Validator_RejectsDescendantOfUnavailableGateThatIsNotNotReached()
    {
        var json = Mutate(root =>
        {
            Gate(root, "R1")["value"] = "UNAVAILABLE";
            Gate(root, "R4")["value"] = "UNAVAILABLE";
        });
        Assert.Contains(ValidateGateState(json), v => v.Contains("R4: must be NOT_REACHED while ancestor R1", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsOpenDescendantOfUnavailableGate()
    {
        var json = Mutate(root =>
        {
            Gate(root, "R1")["value"] = "UNAVAILABLE";
            Gate(root, "R1")["reason"] = "round cap reached";
            Gate(root, "R1")["decided"] = "2026-10-01";
            foreach (var id in new[] { "R2A'", "R3", "R2B'", "R4", "R5" })
                Gate(root, id)["value"] = null;
            Gate(root, "R5")["scientificClassification"] = null;
            Gate(root, "R5")["maintainerAction"] = null;
        });
        Assert.Empty(ValidateGateState(json));
    }

    [Theory]
    [InlineData("reason", "reason")]
    [InlineData("decided", "decided date")]
    public void Validator_RejectsTerminalGateWithoutDatedReason(string field, string expected)
    {
        var json = Mutate(root => Gate(root, "R5").Remove(field));
        Assert.Contains(ValidateGateState(json), v => v.Contains("R5", StringComparison.Ordinal) && v.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsUnadjudicatedCloseoutWithoutMaintainerAction()
    {
        var json = Mutate(root => Gate(root, "R5")["maintainerAction"] = null);
        Assert.Contains(ValidateGateState(json), v => v.Contains("separate maintainer action", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsFormalClassificationOnNonMetR5()
    {
        var json = Mutate(root =>
        {
            Gate(root, "R5")["scientificClassification"] = "INSUFFICIENT INFORMATION";
            Gate(root, "R5")["classificationLabel"] = RequiredLabel;
        });
        Assert.Contains(ValidateGateState(json), v => v.Contains("preserve UNADJUDICATED", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsInvalidatedR5AfterLaterRevocation()
    {
        var json = Mutate(root =>
        {
            Gate(root, "R0")["value"] = "REVOKED";
            Gate(root, "R0")["reason"] = "authority withdrawn";
            Gate(root, "R0")["decided"] = "2026-10-15";
            Gate(root, "R5")["value"] = "INVALIDATED";
        });
        Assert.Empty(ValidateGateState(json));
    }

    [Fact]
    public void Validator_RejectsR5ValueOutsideCloseoutPaths()
    {
        var json = Mutate(root => Gate(root, "R5")["value"] = "NOT_REACHED");
        Assert.Contains(ValidateGateState(json), v => v.Contains("not a closeout or lifecycle value", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("| Task execution | authorized |")]
    [InlineData("**Participant enrollment:** started")]
    [InlineData("Participant enrollment:\nstarted")]
    [InlineData("Task execution is authorized.")]
    [InlineData("Paid collection has been completed for R2B.")]
    [InlineData("**Scientific classification:** `NOT FEASIBLE`")]
    [InlineData("Classification: FEASIBLE AS PROPOSED")]
    [InlineData("| R5 classification | INSUFFICIENT INFORMATION |")]
    public void MarkdownValidator_RejectsDeclarations(string text)
        => Assert.NotEmpty(ValidateMarkdown("x.md", text));

    [Theory]
    [InlineData("**Scientific classification:** `NOT FEASIBLE` (" + RequiredLabel + ")")]
    [InlineData("- participant enrollment or recruitment;")]
    [InlineData("No task execution is authorized.")]
    [InlineData("Task execution is not authorized.")]
    [InlineData("This record prohibits paid collection, which is never permitted.")]
    [InlineData("| Task execution | Not authorized |")]
    public void MarkdownValidator_IgnoresNegatedProse(string text)
        => Assert.Empty(ValidateMarkdown("x.md", text));

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

        var limits = root.GetProperty("limits");
        if (!limits.TryGetProperty("cashCapUsd", out var cap) || cap.GetDecimal() > CashCapUsd)
            violations.Add($"limits.cashCapUsd must be at most {CashCapUsd} (R0 Section 5)");
        if (!limits.TryGetProperty("maxReviewRoundsPerArtifact", out var rounds) || rounds.GetInt32() > MaxRounds)
            violations.Add($"limits.maxReviewRoundsPerArtifact must be at most {MaxRounds} (R0 Section 5)");
        if (!limits.TryGetProperty("maintainerHoursCap", out var maintainerHours) || maintainerHours.GetDecimal() > MaintainerHoursCap)
            violations.Add($"limits.maintainerHoursCap must be at most {MaintainerHoursCap} (R0 Section 5)");
        if (!limits.TryGetProperty("agentSessionHoursCap", out var agentHours) || agentHours.GetDecimal() > AgentSessionHoursCap)
            violations.Add($"limits.agentSessionHoursCap must be at most {AgentSessionHoursCap} (R0 Section 5)");

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var gates = root.GetProperty("gates").EnumerateArray().ToList();
        foreach (var gate in gates)
        {
            var id = gate.GetProperty("id").GetString()!;
            var value = OptionalString(gate, "value");
            if (value is not null && !ProcessStates.Contains(value))
                violations.Add($"gate {id}: value {value} is not a #1370 process state");
            if (!values.TryAdd(id, value))
                violations.Add($"gate {id}: duplicate id");
        }

        foreach (var expected in AmendedGraph.Keys.Where(k => !values.ContainsKey(k)))
            violations.Add($"gate {expected}: missing (amended graph requires it)");

        foreach (var gate in gates)
        {
            var id = gate.GetProperty("id").GetString()!;
            if (!AmendedGraph.TryGetValue(id, out var required))
            {
                violations.Add($"gate {id}: not in the amended graph");
                continue;
            }

            var declared = gate.GetProperty("prerequisites").EnumerateArray().Select(p => p.GetString()!).ToHashSet();
            if (!declared.SetEquals(required))
                violations.Add($"gate {id}: prerequisites must be [{string.Join(", ", required)}]");

            // Checked against the fixed graph, never the declared list.
            if (values[id] != "MET")
                continue;
            foreach (var preId in required)
            {
                var preValue = values.GetValueOrDefault(preId);
                if (preValue != "MET")
                    violations.Add($"gate {id}: MET while prerequisite {preId} is {preValue ?? "open"}");
            }
        }

        // #1370 gate propagation: a descendant of a prerequisite that terminally failed to
        // reach MET (UNAVAILABLE, NOT_AUTHORIZED, NOT_REACHED) closes NOT_REACHED or stays
        // open. R5 is the closeout exception and is checked separately below.
        foreach (var (id, _) in AmendedGraph)
        {
            if (id == "R5" || !values.TryGetValue(id, out var value) || value is null or "NOT_REACHED")
                continue;
            foreach (var ancestor in Ancestors(id))
            {
                var ancestorValue = values.GetValueOrDefault(ancestor);
                if (ancestorValue is "UNAVAILABLE" or "NOT_AUTHORIZED" or "NOT_REACHED")
                    violations.Add($"gate {id}: must be NOT_REACHED while ancestor {ancestor} is {ancestorValue}");
            }
        }

        // Every dispositioned non-MET gate carries a dated reason (#1370 completion path 2).
        foreach (var gate in gates)
        {
            var id = gate.GetProperty("id").GetString()!;
            var value = values.GetValueOrDefault(id);
            if (value is null or "MET")
                continue;
            if (string.IsNullOrWhiteSpace(OptionalString(gate, "reason")))
                violations.Add($"gate {id}: {value} requires a non-empty reason");
            if (!IsIsoDate(OptionalString(gate, "decided")))
                violations.Add($"gate {id}: {value} requires a decided date (yyyy-MM-dd)");
        }

        var r5 = gates.SingleOrDefault(g => g.GetProperty("id").GetString() == "R5");
        if (r5.ValueKind == JsonValueKind.Object)
        {
            var r5Value = values.GetValueOrDefault("R5");
            if (r5Value is not null and not "MET")
            {
                // Amendment 001 Section 5 and R0 Section 10: the administrative closeout ends
                // UNAVAILABLE or EXPIRED; a later expiry, revocation, or withdrawal makes a
                // finished R5 INVALIDATED. Every non-MET R5 preserves UNADJUDICATED and still
                // records a separate maintainer action.
                if (r5Value is not ("UNAVAILABLE" or "EXPIRED" or "INVALIDATED"))
                    violations.Add($"R5: {r5Value} is not a closeout or lifecycle value (UNAVAILABLE, EXPIRED, or INVALIDATED)");
                if (OptionalString(r5, "scientificClassification") != "UNADJUDICATED")
                    violations.Add("R5: a non-MET R5 must preserve UNADJUDICATED");
                if (OptionalString(r5, "maintainerAction") is null)
                    violations.Add("R5: UNADJUDICATED closeout requires a separate maintainer action");
            }

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
        foreach (var (line, unit) in ScanUnits(text))
        {
            if (ClassificationDeclaration.IsMatch(unit) && !unit.Contains(RequiredLabel, StringComparison.Ordinal))
                violations.Add($"{name}:{line}: classification without \"{RequiredLabel}\"");

            var declared = ProhibitedFieldDeclaration.Matches(unit)
                .Concat(ProhibitedSentenceDeclaration.Matches(unit))
                .Any(m => !Negation.IsMatch(SentencePrefix(unit, m)));
            if (declared)
                violations.Add($"{name}:{line}: declares a prohibited activity as authorized or performed");
        }

        return violations;
    }

    /// <summary>
    /// Splits Markdown into scan units: each table row on its own, and each other
    /// paragraph with its wrapped lines joined, so a declaration split across
    /// lines is still seen.
    /// </summary>
    private static IEnumerable<(int Line, string Text)> ScanUnits(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var buffer = new List<string>();
        var start = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isRow = line.TrimStart().StartsWith('|');
            if ((isRow || string.IsNullOrWhiteSpace(line)) && buffer.Count > 0)
            {
                yield return (start + 1, string.Join(' ', buffer));
                buffer.Clear();
            }

            if (isRow)
            {
                yield return (i + 1, line);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                if (buffer.Count == 0)
                    start = i;
                buffer.Add(line.Trim());
            }
        }

        if (buffer.Count > 0)
            yield return (start + 1, string.Join(' ', buffer));
    }

    /// <summary>The text from the start of the match's sentence or table cell through the end of the match.</summary>
    private static string SentencePrefix(string unit, Match match)
    {
        var begin = match.Index == 0
            ? 0
            : unit.LastIndexOfAny(['.', ';', '!', '?', '|'], match.Index - 1) + 1;
        return unit[begin..(match.Index + match.Length)];
    }

    /// <summary>Transitive prerequisites of a gate in the fixed amended graph.</summary>
    private static HashSet<string> Ancestors(string id)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(AmendedGraph[id]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (result.Add(next))
            {
                foreach (var pre in AmendedGraph[next])
                    pending.Push(pre);
            }
        }

        return result;
    }

    private static bool IsIsoDate(string? text)
        => text is not null
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _);

    private static string? OptionalString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Mutate(Action<JsonObject> change)
    {
        var json = File.ReadAllText(Path.Combine(V023Directory(), "gate-state.json"));
        var root = JsonNode.Parse(json)!.AsObject();
        change(root);
        return root.ToJsonString();
    }

    private static JsonObject Gate(JsonObject root, string id)
        => root["gates"]!.AsArray().Single(g => (string?)g!["id"] == id)!.AsObject();
}
