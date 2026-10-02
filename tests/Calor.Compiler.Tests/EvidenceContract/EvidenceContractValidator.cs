using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>One fail-closed finding: a stable code, the entry it concerns, and why.</summary>
internal sealed record ContractViolation(string Code, string Subject, string Message)
{
    public override string ToString() => $"{Code} [{Subject}] {Message}";
}

/// <summary>
/// Validator for the milestone 0.24 evidence contract (#1407):
/// <c>docs/plans/evidence/evidence-contract-1407/contract.json</c>, its artifact inventory, and
/// evidence rows that later gates (#1311, #1135, #1276, #1424, #1408) will produce against it.
///
/// <para>Every rule fails closed. Unknown tokens are errors rather than being mapped to the nearest
/// known value, and an absent field is a violation rather than a default. The rules encode the
/// contract's frozen sections; changing what they accept is a contract amendment.</para>
/// </summary>
internal static class EvidenceContractValidator
{
    /// <summary>The proof-outcome tokens frozen by #1407. Adding or removing one is an amendment.</summary>
    public static readonly IReadOnlyList<string> FrozenOutcomeTokens =
    [
        "Proven", "ProvenVacuous", "Discharged", "Assumed",
        "Unsupported", "TimeoutOrUnavailable", "Failed", "Boundary",
    ];

    /// <summary>The only outcomes allowed to remove a runtime guard or establish a claim.</summary>
    public static readonly IReadOnlyList<string> EstablishingOutcomeTokens = ["Proven", "Discharged"];

    /// <summary>The 0.24 children of epic #1409 other than #1407 itself.</summary>
    public static readonly IReadOnlyList<int> ExpectedChildren =
        [1419, 1311, 1413, 1420, 1421, 1135, 1241, 1417, 1276, 1422, 1410, 1423, 1424, 1408];

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    private static readonly string[] InventoryRequiredStrings =
        ["id", "kind", "classification", "reason", "producer", "sourceRevisions",
         "configuration", "seeds", "environment"];

    // ------------------------------------------------------------------
    // Contract
    // ------------------------------------------------------------------

    public static IReadOnlyList<ContractViolation> ValidateContract(JsonNode contract)
    {
        var v = new List<ContractViolation>();

        var cutoff = contract["cutoff"];
        if (!IsFullSha(Str(cutoff?["commit"])))
            v.Add(new("C001", "cutoff", "cutoff.commit must be a full 40-hex commit SHA"));
        if (!IsUtcTimestamp(Str(cutoff?["timestampUtc"])))
            v.Add(new("C001", "cutoff", "cutoff.timestampUtc must be an ISO-8601 UTC timestamp ending in Z"));

        foreach (var baseline in Array(contract["baselines"]))
        {
            if (!IsFullSha(Str(baseline?["commit"])))
                v.Add(new("C002", Str(baseline?["id"]) ?? "?", "baseline commit must be a full 40-hex SHA; a branch name or short SHA is not a baseline"));
        }
        if (!IsFullSha(Str(contract["candidateStart"]?["commit"])))
            v.Add(new("C002", "candidateStart", "candidate starting commit must be a full 40-hex SHA"));

        var outcomes = Array(contract["outcomeVocabulary"]?["outcomes"]);
        var tokens = outcomes.Select(o => Str(o?["token"]) ?? "").ToList();
        if (!tokens.OrderBy(t => t, StringComparer.Ordinal)
                .SequenceEqual(FrozenOutcomeTokens.OrderBy(t => t, StringComparer.Ordinal)))
        {
            v.Add(new("C003", "outcomeVocabulary",
                $"outcome tokens [{string.Join(", ", tokens)}] differ from the frozen set [{string.Join(", ", FrozenOutcomeTokens)}]"));
        }
        foreach (var outcome in outcomes)
        {
            var token = Str(outcome?["token"]) ?? "?";
            var establishing = EstablishingOutcomeTokens.Contains(token);
            if (Bool(outcome?["mayEstablishClaim"]) != establishing)
                v.Add(new("C004", token, $"mayEstablishClaim must be {establishing.ToString().ToLowerInvariant()}"));
            if (Bool(outcome?["mayRemoveGuard"]) != establishing)
                v.Add(new("C004", token, $"mayRemoveGuard must be {establishing.ToString().ToLowerInvariant()}"));
        }

        var sections = Array(contract["sections"]).Select(s => Str(s)).OfType<string>().ToHashSet();
        var children = Array(contract["children"]);
        var childIssues = new HashSet<int>();
        foreach (var child in children)
        {
            var issue = Int(child?["issue"]);
            var subject = issue is null ? "child ?" : $"#{issue}";
            if (issue is not null) childIssues.Add(issue.Value);

            var consumes = Array(child?["consumes"]).Select(s => Str(s)).ToList();
            if (consumes.Count == 0)
                v.Add(new("C005", subject, "child names no frozen section it consumes"));
            foreach (var section in consumes.Where(s => s is null || !sections.Contains(s)))
                v.Add(new("C005", subject, $"consumes unknown section '{section}'"));
            if (string.IsNullOrWhiteSpace(Str(child?["closureEvidence"])))
                v.Add(new("C005", subject, "child names no closure evidence"));
        }

        var missing = ExpectedChildren.Where(i => !childIssues.Contains(i)).ToList();
        if (missing.Count > 0)
            v.Add(new("C007", "children", $"epic children missing from the contract: {string.Join(", ", missing.Select(i => "#" + i))}"));

        v.AddRange(ValidateGraph(children, childIssues));

        var capacity = contract["authorityCapacity"]?["capacity"];
        var ceilings = Array(capacity?["ceilings"]);
        if (ceilings.Count == 0)
            v.Add(new("C011", "capacity", "no capacity ceilings recorded"));
        foreach (var ceiling in ceilings)
        {
            var id = Str(ceiling?["id"]) ?? "?";
            if (ceiling?["value"] is not JsonValue value || !value.TryGetValue<double>(out var number) || number < 0)
                v.Add(new("C011", id, "ceiling needs a non-negative numeric value"));
            if (string.IsNullOrWhiteSpace(Str(ceiling?["unit"])))
                v.Add(new("C011", id, "ceiling needs a unit"));
            var status = Str(ceiling?["status"]);
            if (status is not ("PROPOSED" or "ACCEPTED"))
                v.Add(new("C011", id, $"ceiling status '{status}' is not PROPOSED or ACCEPTED"));
        }
        var amendmentVersions = Array(contract["amendmentLog"]).Select(a => Str(a?["version"])).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var exception in Array(capacity?["exceptions"]))
        {
            // A per-PR raise (stopping rule 1) is valid only exactly as an amendment registered it,
            // in an amendment that is in the log: a different PR, value, or ceiling needs a new
            // amendment and a validator change, which review sees.
            var ceilingId = Str(exception?["ceiling"]);
            var pr = Int(exception?["pr"]);
            var subject = $"exception {ceilingId ?? "?"} #{pr?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
            double? raised = exception?["value"] is JsonValue rv && rv.TryGetValue<double>(out var r) ? r : null;
            var recordedIn = Str(exception?["amendment"]);
            var registered = RegisteredCeilingExceptions.Any(e =>
                e.Ceiling == ceilingId && e.Pr == pr && e.Value == raised && e.Amendment == recordedIn);
            if (!registered || recordedIn is null || !amendmentVersions.Contains(recordedIn))
                v.Add(new("C011", subject, "ceiling exception is not one registered by a logged amendment (1.1.0: pr-size, #1473, 1520)"));
            if (string.IsNullOrWhiteSpace(Str(exception?["justification"])))
                v.Add(new("C011", subject, "exception needs a justification"));
        }
        var anyProposed = Str(capacity?["status"]) == "PROPOSED"
            || ceilings.Any(c => Str(c?["status"]) == "PROPOSED");
        if (Str(contract["gateStatus"]) == "MET" && (anyProposed || Str(contract["status"]) == "PROPOSED"))
            v.Add(new("C008", "gateStatus", "R0 cannot be MET while the contract or any capacity ceiling is still PROPOSED"));
        if (Str(contract["gateStatus"]) is not ("MET" or "NOT-MET"))
            v.Add(new("C008", "gateStatus", "gateStatus must be MET or NOT-MET"));

        v.AddRange(ValidateLifecycle(contract, capacity, ceilings));

        var independence = contract["authorityCapacity"]?["independence"];
        var deviation = Bool(independence?["deviation"]);
        if (deviation == true)
        {
            if (Str(independence?["maxAdjudicationUnderDeviation"]) != "BOUNDED")
                v.Add(new("C009", "independence", "a recorded independence deviation must cap adjudication at BOUNDED"));
            if (Array(independence?["consequences"]).Count == 0)
                v.Add(new("C009", "independence", "a recorded independence deviation must state its consequences"));
        }
        else if (deviation is null)
        {
            v.Add(new("C009", "independence", "independence.deviation must be recorded explicitly as a boolean"));
        }
        foreach (var role in new[] { "decisionAuthority", "amendmentAuthority", "implementationOwners", "reviewAndMerge", "adjudicator" })
        {
            if (string.IsNullOrWhiteSpace(Str(contract["authorityCapacity"]?[role])))
                v.Add(new("C009", role, "authority or owner is not named"));
        }

        v.AddRange(ValidateAmendments(contract));
        v.AddRange(ValidateDocsDeployRule(contract));
        return v;
    }

    /// <summary>
    /// C013 (amendment 1.1.0): the documentation-only deploy rule, when present, may only be as strict
    /// as or stricter than the rule amendment 1.1.0 registered: its allowlist a subset of the
    /// registered one, its denylist a superset, the website and workflow still diffed, the
    /// pre-contract base and attestation unchanged. Widening it is a validator change, which review sees.
    /// </summary>
    private static IEnumerable<ContractViolation> ValidateDocsDeployRule(JsonNode contract)
    {
        var rule = contract["releasePath"]?["documentationOnlyDeploy"];
        if (rule is null)
            yield break;
        var allowed = Array(rule["allowedPaths"]).Select(Str).ToList();
        if (allowed.Count == 0)
            yield return new("C013", "documentationOnlyDeploy", "the documentation-only deploy rule needs a non-empty allowlist");
        foreach (var pattern in allowed.Where(p => p is null || !RegisteredDocsAllowedPaths.Contains(p)))
            yield return new("C013", "documentationOnlyDeploy", $"allowed pattern '{pattern}' is not in the allowlist registered by amendment 1.1.0");
        var denied = Array(rule["deniedPaths"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var pattern in RegisteredDocsDeniedPaths.Where(p => !denied.Contains(p)))
            yield return new("C013", "documentationOnlyDeploy", $"registered denial '{pattern}' was removed");
        var inputs = Array(rule["buildInputs"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (!inputs.Contains("website/"))
            yield return new("C013", "documentationOnlyDeploy", "buildInputs must include website/");
        if (!Array(rule["machinery"]?["paths"]).Select(Str).Contains(".github/workflows/nextjs-gh-pages.yml"))
            yield return new("C013", "documentationOnlyDeploy", "the deploying workflow must stay machinery");
        if (Str(rule["base"]?["preContractDeploy"]?["commit"]) != RegisteredPreContractDeployCommit
            || Long(rule["base"]?["preContractDeploy"]?["runId"]) != RegisteredPreContractDeployRun)
            yield return new("C013", "documentationOnlyDeploy", "the pre-contract deploy base is run 34999741475 at 72a0a855");
        if (Str(rule["auditRecord"]?["attestationStatement"]) != RegisteredDocsAttestation)
            yield return new("C013", "documentationOnlyDeploy", "the attestation statement is the one registered by amendment 1.1.0");
    }

    /// <summary>Per-PR ceiling raises registered by amendments (stopping rule 1).</summary>
    private static readonly (string Ceiling, int Pr, double Value, string Amendment)[] RegisteredCeilingExceptions =
    [
        ("pr-size", 1473, 1520, "1.1.0"),
    ];

    private static readonly HashSet<string> RegisteredDocsAllowedPaths = new(StringComparer.Ordinal)
    {
        "website/content/cli/**/*.mdx", "website/content/getting-started/**/*.mdx",
        "website/content/syntax-reference/**/*.mdx", "website/content/semantics/**/*.mdx",
        "website/content/guides/**/*.mdx", "website/content/contributing/**/*.mdx",
    };

    private static readonly string[] RegisteredDocsDeniedPaths =
    [
        "website/public/data/**", "website/content/benchmarking/**", "website/content/philosophy/**",
        "website/content/changelog.mdx", "website/content/index.mdx",
        "website/content/guides/verification-guarantees.mdx", "website/content/contributing/adding-benchmarks.mdx",
        "website/content/cli/benchmark.mdx", "website/content/cli/evaluation.mdx",
    ];

    private const string RegisteredPreContractDeployCommit = "72a0a855d8cd7f1e85c5bcd99474b83d1556d4e1";
    private const long RegisteredPreContractDeployRun = 34999741475;
    private const string RegisteredDocsAttestation =
        "This deploy changes no claim or number about 0.24 evidence, verification results, or a Calor advantage.";

    private static IEnumerable<ContractViolation> ValidateGraph(List<JsonNode?> children, HashSet<int> childIssues)
    {
        var edges = new Dictionary<int, List<int>>();
        foreach (var child in children)
        {
            var issue = Int(child?["issue"]);
            if (issue is null) continue;
            var deps = Array(child?["dependsOn"]).Select(Int).ToList();
            if (deps.Count == 0)
                yield return new("C006", $"#{issue}", "child has no recorded dependency");
            foreach (var dep in deps)
            {
                if (dep is null || (dep != 1407 && !childIssues.Contains(dep.Value)))
                    yield return new("C006", $"#{issue}", $"depends on unknown issue {dep}");
            }
            edges[issue.Value] = deps.OfType<int>().Where(d => d != 1407).ToList();
        }

        // Depth-first cycle detection; a cycle makes the gate order meaningless.
        var state = new Dictionary<int, int>();
        foreach (var start in edges.Keys)
        {
            if (HasCycle(start, edges, state, out var at))
            {
                yield return new("C006", $"#{at}", "dependency graph contains a cycle");
                yield break;
            }
        }
    }

    private static bool HasCycle(int node, Dictionary<int, List<int>> edges, Dictionary<int, int> state, out int at)
    {
        at = node;
        if (state.TryGetValue(node, out var s))
            return s == 1;
        state[node] = 1;
        foreach (var next in edges.GetValueOrDefault(node, []))
        {
            if (HasCycle(next, edges, state, out at))
                return true;
        }
        state[node] = 2;
        return false;
    }

    /// <summary>
    /// C012: the packet is either PROPOSED (as committed in the R0 PR and its merge commit) or FROZEN
    /// (after the acceptance write-back). FROZEN needs every acceptance field; nothing in between.
    /// </summary>
    private static IEnumerable<ContractViolation> ValidateLifecycle(
        JsonNode contract, JsonNode? capacity, List<JsonNode?> ceilings)
    {
        var status = Str(contract["status"]);
        var acceptance = contract["acceptance"];
        if (status == "PROPOSED")
        {
            if (acceptance is not null)
                yield return new("C012", "acceptance", "a PROPOSED contract cannot carry an acceptance record");
            if (Str(capacity?["status"]) != "PROPOSED" || ceilings.Any(c => Str(c?["status"]) != "PROPOSED"))
                yield return new("C012", "capacity", "a PROPOSED contract keeps capacity and every ceiling PROPOSED until the acceptance write-back");
            yield break;
        }
        if (status != "FROZEN")
        {
            yield return new("C012", "status", $"status '{status}' is not PROPOSED or FROZEN");
            yield break;
        }
        if (Str(contract["gateStatus"]) != "MET")
            yield return new("C012", "gateStatus", "a FROZEN contract records R0 as MET");
        if (Str(capacity?["status"]) != "ACCEPTED" || ceilings.Any(c => Str(c?["status"]) != "ACCEPTED"))
            yield return new("C012", "capacity", "a FROZEN contract needs capacity and every ceiling ACCEPTED");
        if (!IsFullSha(Str(acceptance?["mergeCommit"])))
            yield return new("C012", "acceptance", "acceptance.mergeCommit must be the full SHA of the R0 merge on main");
        if (!IsUtcTimestamp(Str(acceptance?["mergedAtUtc"])))
            yield return new("C012", "acceptance", "acceptance.mergedAtUtc must be a UTC timestamp");
        if (Int(acceptance?["pr"]) is null)
            yield return new("C012", "acceptance", "acceptance.pr must name the merged R0 pull request");
    }

    private static IEnumerable<ContractViolation> ValidateAmendments(JsonNode contract)
    {
        var log = Array(contract["amendmentLog"]);
        var expectedVersion = "1.0.0";
        var previous = new Version(1, 0, 0);
        foreach (var entry in log)
        {
            var version = Str(entry?["version"]);
            var subject = $"amendment {version ?? "?"}";
            if (string.IsNullOrWhiteSpace(version))
                yield return new("C010", subject, "amendment has no version");
            else if (!TryParseSemVer(version, out var parsed) || parsed <= previous)
                yield return new("C010", subject, $"amendment version must be a MAJOR.MINOR.PATCH strictly greater than {previous}");
            else
                previous = parsed;
            if (string.IsNullOrWhiteSpace(Str(entry?["justification"])))
                yield return new("C010", subject, "amendment has no justification");
            if (Int(entry?["reviewedInPr"]) is not > 0)
                yield return new("C010", subject, "amendment names no reviewed PR");
            if (!IsUtcTimestamp(Str(entry?["timestampUtc"])))
                yield return new("C010", subject, "amendment has no UTC timestamp");
            if (Bool(entry?["afterDecisionBearingInspection"]) is null)
                yield return new("C010", subject, "amendment must state as a boolean whether it follows decision-bearing inspection");
            foreach (var removed in Array(entry?["removedRows"]))
            {
                if (string.IsNullOrWhiteSpace(Str(removed?["lastStatus"])))
                    yield return new("C010", subject, "a removed row must stay reported with its last status");
            }
            if (version is not null) expectedVersion = version;
        }
        if (Str(contract["contractVersion"]) != expectedVersion)
            yield return new("C010", "contractVersion",
                $"contractVersion '{Str(contract["contractVersion"])}' must equal the latest amendment version '{expectedVersion}'");
    }

    // ------------------------------------------------------------------
    // Inventory
    // ------------------------------------------------------------------

    public static IReadOnlyList<ContractViolation> ValidateInventory(JsonNode contract, JsonNode inventory)
    {
        var v = new List<ContractViolation>();

        if (Str(inventory["cutoffCommit"]) != Str(contract["cutoff"]?["commit"]))
            v.Add(new("I012", "inventory", "inventory cutoffCommit differs from the contract cutoff"));
        if (Str(inventory["contractVersion"]) != Str(contract["contractVersion"]))
            v.Add(new("I012", "inventory", "inventory contractVersion differs from the contract"));

        var classifications = Keys(contract["classifications"]);
        var durable = Array(contract["durableIdentityKinds"]).Select(Str).OfType<string>().ToHashSet();
        var nonDurable = Array(contract["nonDurableIdentityKinds"]).Select(Str).OfType<string>().ToHashSet();
        var knownIssues = Array(contract["children"]).Select(c => Int(c?["issue"])).OfType<int>().Append(1407).ToHashSet();

        var artifacts = Array(inventory["artifacts"]);
        var byId = new Dictionary<string, JsonNode>();
        foreach (var artifact in artifacts)
        {
            var id = Str(artifact?["id"]) ?? "?";
            if (artifact is not null && !byId.TryAdd(id, artifact))
                v.Add(new("I003", id, "duplicate artifact id"));
        }

        foreach (var artifact in artifacts)
        {
            if (artifact is null) continue;
            var id = Str(artifact["id"]) ?? "?";

            foreach (var field in InventoryRequiredStrings)
            {
                if (string.IsNullOrWhiteSpace(Str(artifact[field])))
                    v.Add(new("I001", id, $"missing required field '{field}'"));
            }
            foreach (var field in new[] { "paths", "consumers" })
            {
                if (Array(artifact[field]).Count == 0)
                    v.Add(new("I001", id, $"'{field}' must list at least one entry"));
            }

            var classification = Str(artifact["classification"]);
            if (classification is null || !classifications.Contains(classification))
                v.Add(new("I002", id, $"unknown classification '{classification}'"));

            var command = Str(artifact["regeneration"]?["command"]);
            var note = Str(artifact["regeneration"]?["note"]);
            if (classification is "authoritative" or "derived" && string.IsNullOrWhiteSpace(command))
                v.Add(new("I004", id, $"{classification} artifact has no regeneration command"));
            if (classification is "historical-only" or "stale"
                && string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(note))
                v.Add(new("I005", id, "artifact gives neither a regeneration command nor a note why none is valid"));

            var identity = Str(artifact["provenance"]?["identity"]);
            if (identity is null || (!durable.Contains(identity) && !nonDurable.Contains(identity))
                || string.IsNullOrWhiteSpace(Str(artifact["provenance"]?["detail"])))
                v.Add(new("I006", id, $"missing or unknown provenance identity '{identity}'"));
            else if (classification == "authoritative" && !durable.Contains(identity))
                v.Add(new("I007", id, $"authoritative artifact has non-durable identity '{identity}'"));
            if (classification == "authoritative" && Str(artifact["storage"]?["kind"]) == "actions-artifact")
                v.Add(new("I007", id, "authoritative artifact stored only as an expiring Actions artifact"));

            var derivedFrom = Array(artifact["derivedFrom"]).Select(Str).ToList();
            if (classification == "derived" && derivedFrom.Count == 0)
                v.Add(new("I008", id, "derived artifact names no inventoried input"));
            foreach (var input in derivedFrom)
            {
                if (input is null || !byId.TryGetValue(input, out var source))
                {
                    v.Add(new("I009", id, $"derivedFrom references unknown artifact '{input}'"));
                    continue;
                }
                if (Str(source["classification"]) == "stale" && classification is "authoritative" or "derived")
                    v.Add(new("I010", id, $"{classification} artifact consumes stale input '{input}'; staleness propagates"));
            }

            var owners = Array(artifact["owningIssues"]).Select(Int).ToList();
            if (owners.Count == 0)
                v.Add(new("I011", id, "artifact has no owning issue"));
            foreach (var owner in owners.Where(o => o is null || !knownIssues.Contains(o.Value)))
                v.Add(new("I011", id, $"owning issue {owner} is not a 0.24 child"));
        }

        v.AddRange(ValidatePendingUpdates(contract, inventory, byId, classifications));
        return v;
    }

    /// <summary>
    /// I013 (amendment 1.1.0): a pending inventory update names a known artifact, the open PR whose
    /// repair motivates it, the amendment that recorded it, a known target classification, and a
    /// resolving PR for each defect it would resolve. It is never applied by the validator: rows are
    /// checked against the artifacts as committed.
    /// </summary>
    private static IEnumerable<ContractViolation> ValidatePendingUpdates(
        JsonNode contract, JsonNode inventory, Dictionary<string, JsonNode> byId, HashSet<string> classifications)
    {
        var amendments = Array(contract["amendmentLog"]).Select(a => Str(a?["version"])).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (inventory["pendingUpdates"] is { } present && present is not JsonArray)
            yield return new("I013", "pendingUpdates", "pendingUpdates must be an array");
        foreach (var update in Array(inventory["pendingUpdates"]))
        {
            var id = Str(update?["id"]) ?? "?";
            if (!seen.Add(id))
                yield return new("I013", id, "duplicate pending update id");
            var artifact = Str(update?["artifact"]);
            if (artifact is null || !byId.ContainsKey(artifact))
                yield return new("I013", id, $"pending update names unknown artifact '{artifact}'");
            if (Int(update?["repairingPr"]) is not > 0)
                yield return new("I013", id, "pending update must name the repairing PR");
            var amendment = Str(update?["recordedInAmendment"]);
            if (amendment is null || !amendments.Contains(amendment))
                yield return new("I013", id, $"pending update names no recorded amendment ('{amendment}')");
            if (Str(update?["status"]) != "pending-merge")
                yield return new("I013", id, "a pending update has status 'pending-merge'; an applied update is removed by its write-back");
            var after = Str(update?["classificationAfter"]);
            if (after is null || !classifications.Contains(after))
                yield return new("I013", id, $"unknown classificationAfter '{after}'");
            if (update?["changes"] is not JsonObject)
                yield return new("I013", id, "pending update needs a changes object (empty when only defects resolve)");
            var cutoffDefects = artifact is not null && byId.TryGetValue(artifact, out var record)
                ? Array(record["openDefects"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal)
                : [];
            if (update?["defectResolutions"] is not JsonArray)
                yield return new("I013", id, "defectResolutions must be an array (empty when no defect resolves)");
            foreach (var resolution in Array(update?["defectResolutions"]))
            {
                if (string.IsNullOrWhiteSpace(Str(resolution?["defect"])) || Int(resolution?["resolvedByPr"]) is not > 0
                    || string.IsNullOrWhiteSpace(Str(resolution?["scope"])))
                    yield return new("I013", id, "a defect resolution needs the defect text, a resolving PR, and its scope");
                else if (!cutoffDefects.Contains(Str(resolution?["defect"])!))
                    yield return new("I013", id, "a defect resolution must quote one of the artifact's cutoff openDefects verbatim");
            }
        }
    }

    // ------------------------------------------------------------------
    // Evidence rows
    // ------------------------------------------------------------------

    /// <summary>
    /// Validates evidence rows against the frozen contract. <paramref name="candidateCommit"/> is the
    /// #1423 frozen candidate; null means no candidate is frozen, so nothing can be established.
    /// <paramref name="candidateSemanticsVersion"/> is that candidate's
    /// <c>ContractTranslator.SemanticsVersion</c>; null means it is unknown, so nothing can be established.
    /// </summary>
    public static IReadOnlyList<ContractViolation> ValidateEvidenceRows(
        JsonNode contract, JsonNode inventory, JsonArray rows, string? candidateCommit,
        string? candidateSemanticsVersion)
    {
        var v = new List<ContractViolation>();

        var outcomes = Array(contract["outcomeVocabulary"]?["outcomes"])
            .Where(o => o is not null)
            .ToDictionary(o => Str(o!["token"]) ?? "", o => o!, StringComparer.Ordinal);
        var rowStatuses = Array(contract["outcomeVocabulary"]?["nonOutcomeRowStatuses"])
            .Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var artifacts = Array(inventory["artifacts"]).Where(a => a is not null)
            .ToDictionary(a => Str(a!["id"]) ?? "", a => a!, StringComparer.Ordinal);
        var adjudications = Array(contract["adjudicationOutcomes"]).Select(Str).OfType<string>().ToHashSet();
        var pairDispositions = Array(contract["benchmarkEquivalence"]?["pairDispositions"]).Select(Str).OfType<string>().ToHashSet();
        var keyFields = Array(contract["benchmarkEquivalence"]?["comparabilityKey"]).Select(Str).OfType<string>().ToList();
        var pairFields = Array(contract["benchmarkEquivalence"]?["pairRowRequiredFields"]).Select(Str).OfType<string>().ToList();
        var stringFields = Array(contract["benchmarkEquivalence"]?["pairRowStringFields"]).Select(Str).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var samplingUnit = Str(contract["benchmarkEquivalence"]?["samplingUnit"]);
        var cutoff = ParseUtc(Str(contract["cutoff"]?["timestampUtc"]));
        // Fail closed: anything other than an explicit false is treated as a standing deviation.
        var reducedIndependence = Bool(contract["authorityCapacity"]?["independence"]?["deviation"]) != false;

        foreach (var row in rows)
        {
            if (row is null) continue;
            var id = Str(row["id"]) ?? "?";
            var outcome = Str(row["outcome"]);
            var counted = Str(row["counted"]);

            if (counted is not ("established" or "not-established"))
                v.Add(new("E001", id, $"counted must be 'established' or 'not-established', not '{counted}'"));

            var isOutcome = outcome is not null && outcomes.ContainsKey(outcome);
            var isRowStatus = outcome is not null && rowStatuses.Contains(outcome);
            if (!isOutcome && !isRowStatus)
                v.Add(new("E001", id, $"unknown outcome '{outcome}'; it is not mapped to any frozen token"));

            if (!IsFullSha(Str(row["sourceCommit"])))
                v.Add(new("E012", id, "sourceCommit must be a full 40-hex SHA"));

            var adjudication = Str(row["adjudication"]);
            if (adjudication is not null)
            {
                if (!adjudications.Contains(adjudication))
                    v.Add(new("E011", id, $"unknown adjudication outcome '{adjudication}'"));
                else if (adjudication == "SUPPORTED" && reducedIndependence)
                    v.Add(new("E011", id, "SUPPORTED is unavailable under the recorded independence deviation; the cap is BOUNDED"));
            }

            v.AddRange(ValidateBenchmark(row, id, keyFields, pairFields, stringFields, pairDispositions, samplingUnit));

            // A benchmark row is decision-bearing (it can feed a headline, delta, or adjudication)
            // unless it is explicitly recorded as historical. Decision-bearing rows meet the same
            // artifact, candidate, and freshness rules as established proofs, whatever 'counted' says.
            var established = counted == "established";
            var decisionBearingBenchmark = row["benchmark"] is not null && outcome != "historical";
            if (established || decisionBearingBenchmark)
                v.AddRange(ValidateProvenance(row, id, artifacts, candidateCommit, cutoff));

            if (!established)
                continue;

            if (!isOutcome || Bool(outcomes[outcome!]["mayEstablishClaim"]) != true)
                v.Add(new("E002", id, $"outcome '{outcome}' cannot be counted as established"));
            if (outcome is "Proven" or "Discharged")
            {
                var vacuous = Bool(row["vacuous"]);
                if (vacuous is null)
                    v.Add(new("E003", id, "an established proof must state vacuous=false explicitly"));
                else if (vacuous.Value)
                    v.Add(new("E003", id, "a vacuous proof is never established"));
            }
            if (outcome == "Discharged"
                && (string.IsNullOrWhiteSpace(Str(row["nonVacuityCheck"]?["id"]))
                    || Str(row["nonVacuityCheck"]?["result"]) != "passed"))
                v.Add(new("E003", id, "a Discharged obligation establishes only after a registered non-vacuity check {id, result: passed}"));

            v.AddRange(ValidateEstablishmentPrerequisites(row, id, candidateSemanticsVersion));
        }

        return v;
    }

    /// <summary>E004–E007, E014: the cited artifact, the frozen candidate, and freshness.</summary>
    private static IEnumerable<ContractViolation> ValidateProvenance(
        JsonNode row, string id, Dictionary<string, JsonNode> artifacts, string? candidateCommit, DateTimeOffset? cutoff)
    {
        var artifactId = Str(row["artifact"]);
        if (artifactId is null || !artifacts.TryGetValue(artifactId, out var artifact))
        {
            yield return new("E004", id, $"row cites unknown artifact '{artifactId}'");
        }
        else
        {
            if (Str(artifact["classification"]) != "authoritative")
                yield return new("E005", id, $"artifact '{artifactId}' is {Str(artifact["classification"])}, not authoritative");
            var openDefects = Array(artifact["openDefects"]).Count;
            var resolvedBy = Array(row["openDefectsResolvedBy"]).Select(Int).ToList();
            if (resolvedBy.Any(pr => pr is null) || resolvedBy.Count < openDefects)
                yield return new("E014", id,
                    $"artifact '{artifactId}' has {openDefects} open defect(s) at the cutoff; the row names {resolvedBy.Count} resolving PR(s)");
        }

        if (candidateCommit is null)
            yield return new("E006", id, "no frozen candidate (#1423); nothing can be established or decided yet");
        else if (!string.Equals(Str(row["sourceCommit"]), candidateCommit, StringComparison.Ordinal))
            yield return new("E006", id, "row was not produced on the frozen candidate");

        var recorded = ParseUtc(Str(row["recordedAtUtc"]));
        if (recorded is null || cutoff is null)
            yield return new("E007", id, "row needs a UTC recordedAtUtc");
        else if (recorded <= cutoff)
            yield return new("E007", id, "row predates the cutoff; retrospective evidence cannot be confirmatory");
    }

    // ------------------------------------------------------------------
    // Terminal record (#1408)
    // ------------------------------------------------------------------

    /// <summary>
    /// T001–T003: the machine-checkable part of the terminal-success predicate
    /// (<c>rules.terminalSuccess</c>). The required subjects and their criticality come from the
    /// contract and inventory, never from the record: every inventory artifact and every child gate
    /// other than #1408 is adjudicated exactly once, every subject is release-critical, and
    /// HISTORICAL-ONLY is valid only for an artifact the inventory classifies historical-only.
    /// MILESTONE-SUCCEEDED needs every subject present and none BLOCKED and, under the recorded
    /// independence deviation, the reduced-independence statement and limitation.
    /// </summary>
    public static IReadOnlyList<ContractViolation> ValidateTerminalRecord(
        JsonNode contract, JsonNode inventory, IReadOnlyCollection<string> registeredClaims, JsonNode record)
    {
        var v = new List<ContractViolation>();
        var terminal = Array(contract["terminalOutcomes"]).Select(Str).OfType<string>().ToHashSet();
        var adjudicationOutcomes = Array(contract["adjudicationOutcomes"]).Select(Str).OfType<string>().ToHashSet();
        var classification = Array(inventory["artifacts"]).Where(a => a is not null)
            .ToDictionary(a => Str(a!["id"]) ?? "", a => Str(a!["classification"]), StringComparer.Ordinal);
        foreach (var claim in registeredClaims.Where(c => !IsClaimSubject(c)))
            v.Add(new("T003", claim, "a registered claim id must have the form 'claim:<id>'"));
        var required = classification.Keys
            .Concat(Array(contract["children"]).Select(c => Int(c?["issue"])).OfType<int>()
                .Where(i => i != 1408).Select(i => $"gate:#{i}"))
            .Concat(registeredClaims.Where(IsClaimSubject))
            .ToHashSet(StringComparer.Ordinal);

        var outcome = Str(record["outcome"]);
        if (outcome is null || !terminal.Contains(outcome))
            v.Add(new("T003", "terminal", $"unknown terminal outcome '{outcome}'"));

        var rows = Array(record["adjudications"]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var subject = Str(row?["subject"]);
            var name = subject ?? "?";
            if (subject is null || !required.Contains(subject))
                v.Add(new("T003", name, "unknown or unregistered subject; subjects are inventory artifacts, 'gate:#<issue>', and registered 'claim:<id>' ids"));
            else if (!seen.Add(subject))
                v.Add(new("T003", name, "subject adjudicated more than once"));

            var cls = subject is not null && classification.TryGetValue(subject, out var c) ? c : null;
            var adjudication = Str(row?["outcome"]);
            if (adjudication is null || !adjudicationOutcomes.Contains(adjudication))
                v.Add(new("T003", name, $"unknown adjudication outcome '{adjudication}'"));
            else if (adjudication == "HISTORICAL-ONLY" && cls != "historical-only")
                v.Add(new("T003", name, "HISTORICAL-ONLY is valid only for an artifact the inventory classifies historical-only"));
            else if (cls == "stale" && adjudication != "BLOCKED")
                v.Add(new("T003", name, "an artifact still classified stale can only be BLOCKED; repair and reclassify it by amendment first"));
        }

        if (outcome != "MILESTONE-SUCCEEDED")
            return v;

        foreach (var missing in required.Where(s => !seen.Contains(s)).OrderBy(s => s, StringComparer.Ordinal))
            v.Add(new("T001", missing, "required subject is not adjudicated; missing evidence cannot succeed"));
        foreach (var row in rows.Where(r => Str(r?["outcome"]) == "BLOCKED"))
            v.Add(new("T001", Str(row?["subject"]) ?? "?", "a BLOCKED adjudication makes the milestone fail; every subject is release-critical"));

        var independence = contract["authorityCapacity"]?["independence"];
        if (Bool(independence?["deviation"]) != false)
        {
            if (Str(record["adjudicationIndependence"]) != "reduced")
                v.Add(new("T002", "terminal", "under the independence deviation a success record carries adjudicationIndependence = reduced"));
            var limitation = Str(independence?["publishedLimitation"]);
            if (limitation is null || Str(record["limitation"]) != limitation)
                v.Add(new("T002", "terminal", "a success record carries the published independence limitation verbatim"));
            if (Bool(record["epicIndependentAdjudicationMet"]) != false)
                v.Add(new("T002", "terminal", "a success record states epicIndependentAdjudicationMet = false"));
            foreach (var row in rows.Where(r => Str(r?["outcome"]) == "SUPPORTED"))
                v.Add(new("T002", Str(row?["subject"]) ?? "?", "SUPPORTED is unavailable under the independence deviation"));
            foreach (var row in rows.Where(r => Str(r?["independence"]) != "reduced-maintainer-adjudicated"))
                v.Add(new("T002", Str(row?["subject"]) ?? "?", "every adjudication row records independence = reduced-maintainer-adjudicated"));
        }
        return v;
    }

    private static bool IsClaimSubject(string subject) =>
        subject.StartsWith("claim:", StringComparison.Ordinal) && subject.Length > "claim:".Length;

    /// <summary>E013: the §4 establishment conditions the row itself must evidence.</summary>
    private static IEnumerable<ContractViolation> ValidateEstablishmentPrerequisites(
        JsonNode row, string id, string? candidateSemanticsVersion)
    {
        if (string.IsNullOrWhiteSpace(Str(row["registrationId"])))
            yield return new("E013", id, "established row names no registration entry");
        if (string.IsNullOrWhiteSpace(Str(row["producer"])))
            yield return new("E013", id, "established row names no registered producer");
        if (string.IsNullOrWhiteSpace(Str(row["oracle"]?["id"])))
            yield return new("E013", id, "established row names no independent oracle");
        else if (Bool(row["oracle"]?["agrees"]) != true)
            yield return new("E013", id, "the independent oracle does not agree (or its verdict is absent)");
        if (candidateSemanticsVersion is null)
            yield return new("E013", id, "the candidate's translator semantics version is unknown");
        else if (!string.Equals(Str(row["translatorSemanticsVersion"]), candidateSemanticsVersion, StringComparison.Ordinal))
            yield return new("E013", id,
                $"translatorSemanticsVersion '{Str(row["translatorSemanticsVersion"])}' differs from the candidate's '{candidateSemanticsVersion}'");
        if (Bool(row["unresolvedFalseProof"]) != false)
            yield return new("E013", id, "the row must state unresolvedFalseProof=false; an unresolved or unstated false proof blocks establishment");
    }

    private static IEnumerable<ContractViolation> ValidateBenchmark(
        JsonNode row, string id, List<string> keyFields, List<string> pairFields,
        HashSet<string> stringFields, HashSet<string> dispositions, string? samplingUnit)
    {
        var bench = row["benchmark"];
        if (bench is null)
            yield break;

        if (samplingUnit is null || Str(bench["samplingUnit"]) != samplingUnit)
            yield return new("E010", id, $"sampling unit '{Str(bench["samplingUnit"])}' is not the frozen '{samplingUnit}'");
        // Every representation of the sampling unit must be the frozen one, not merely self-consistent.
        if (bench["comparability"]?["samplingUnit"] is not null && Str(bench["comparability"]?["samplingUnit"]) != samplingUnit)
            yield return new("E010", id, $"comparability samplingUnit '{Str(bench["comparability"]?["samplingUnit"])}' is not the frozen '{samplingUnit}'");

        var pairs = Array(bench["pairs"]);
        if (pairs.Count == 0)
            yield return new("E009", id, "benchmark row has an empty pair denominator");
        foreach (var pair in pairs)
        {
            var pairId = Str(pair?["pairId"]) ?? "?";
            foreach (var field in pairFields.Where(f => IsBlank(pair?[f])))
                yield return new("E009", id, $"pair {pairId} is missing required field '{field}'");
            foreach (var field in pairFields.Where(f => !IsBlank(pair?[f])))
            {
                var node = pair![field];
                var isString = node is JsonValue sv && sv.TryGetValue<string>(out _);
                if (stringFields.Contains(field) ? !isString : node is JsonValue && !isString)
                    yield return new("E009", id, $"pair {pairId} field '{field}' has the wrong JSON type");
            }
            foreach (var field in pairFields.Where(f => IsHashField(f) && !IsBlank(pair?[f]) && !IsSha256(Str(pair?[f]))))
                yield return new("E009", id, $"pair {pairId} field '{field}' is not a 64-hex SHA-256");
            var included = Bool(pair?["included"]);
            if (included is null)
                yield return new("E009", id, $"pair {pairId} must state included as a boolean");
            var disposition = Str(pair?["disposition"]);
            if (disposition is null || !dispositions.Contains(disposition))
                yield return new("E009", id, $"pair {pairId} has unknown disposition '{disposition}'");
            else if (included == true && disposition != "EQUIVALENT")
                yield return new("E009", id, $"pair {pairId} is {disposition} but included; only EQUIVALENT pairs enter metrics");
        }

        // The key is required on every benchmark row, whether or not it is compared to an earlier one.
        var key = bench["comparability"];
        foreach (var field in keyFields.Where(f => IsBlank(key?[f])))
            yield return new("E008", id, $"comparability field '{field}' is missing; incomparable");
        foreach (var field in keyFields.Where(f => IsHashField(f) && !IsBlank(key?[f]) && !IsSha256(Str(key?[f]))))
            yield return new("E008", id, $"comparability field '{field}' is not a 64-hex SHA-256; incomparable");

        var comparedTo = bench["comparedTo"];
        if (comparedTo is null)
            yield break;
        foreach (var field in keyFields)
        {
            var mine = key?[field]?.ToJsonString();
            var theirs = comparedTo[field]?.ToJsonString();
            if (theirs is null)
                yield return new("E008", id, $"compared-to result lacks comparability field '{field}'; incomparable");
            else if (mine is not null && mine != theirs)
                yield return new("E008", id, $"comparability field '{field}' differs ({theirs} -> {mine}); incomparable");
        }
    }

    // ------------------------------------------------------------------
    // Documentation-only website deploy audit record (amendment 1.1.0)
    // ------------------------------------------------------------------

    private static readonly string[] DocsDeployPublishFlags =
        ["package", "githubRelease", "tag", "benchmarkData", "releaseNotes"];

    private static readonly string[] DocsDeployBaseKinds =
        ["pre-contract-deploy", "audited-docs-deploy", "adjudicated-deploy"];

    /// <summary>
    /// D001–D006: one audit record of a documentation-only website deploy
    /// (<c>releasePath.documentationOnlyDeploy</c>). The record's own verdicts are not trusted: every
    /// changed path is re-classified here against the contract's allow and deny patterns and the
    /// inventory's artifact paths. Recomputing the diff from git is the #1410 verifier's job.
    /// </summary>
    public static IReadOnlyList<ContractViolation> ValidateDocsDeployAudit(JsonNode contract, JsonNode inventory, JsonNode record)
    {
        var v = new List<ContractViolation>();
        var rule = contract["releasePath"]?["documentationOnlyDeploy"];
        if (rule is null)
        {
            v.Add(new("D001", "documentationOnlyDeploy", "the contract defines no documentation-only deploy path; every deploy needs an adjudication identity"));
            return v;
        }
        var subject = $"deploy of {Str(record["deployedCommit"]) ?? "?"}";

        // D001: identity of the record and the run. The record is committed before the deploy job
        // publishes, and it names the run that will publish.
        if (Str(record["schema"]) != Str(rule["auditRecord"]?["schema"]))
            v.Add(new("D001", subject, $"schema must be '{Str(rule["auditRecord"]?["schema"])}'"));
        if (Long(record["runId"]) is not > 0)
            v.Add(new("D001", subject, "runId must be the deploying run id"));
        if (Str(record["workflow"]) != ".github/workflows/nextjs-gh-pages.yml")
            v.Add(new("D001", subject, "workflow must be .github/workflows/nextjs-gh-pages.yml"));
        // The record's contract version must be one the log actually records, at or after the
        // amendment that introduced the path, and that amendment must itself be in the log.
        var logged = Array(contract["amendmentLog"]).Select(a => Str(a?["version"])).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var introducedBy = Str(rule["amendment"]);
        var recordVersionText = Str(record["contractVersion"]);
        if (introducedBy is null || !logged.Contains(introducedBy) || !TryParseSemVer(introducedBy, out var introduced)
            || recordVersionText is null || !logged.Contains(recordVersionText)
            || !TryParseSemVer(recordVersionText, out var recordVersion) || recordVersion < introduced)
            v.Add(new("D001", subject, $"contractVersion must be a logged amendment version at or after the one that introduced this path ({introducedBy})"));
        if (!IsUtcTimestamp(Str(record["recordedAtUtc"])))
            v.Add(new("D001", subject, "recordedAtUtc must be a UTC timestamp"));

        // D002: deployed commit and diff base.
        if (!IsFullSha(Str(record["deployedCommit"])))
            v.Add(new("D002", subject, "deployedCommit must be a full 40-hex SHA"));
        if (!IsFullSha(Str(record["baseCommit"])))
            v.Add(new("D002", subject, "baseCommit must be a full 40-hex SHA"));
        if (Long(record["baseRunId"]) is not > 0)
            v.Add(new("D002", subject, "baseRunId must name the base deployment's run"));
        var baseKind = Str(record["baseKind"]);
        if (baseKind is null || !DocsDeployBaseKinds.Contains(baseKind))
            v.Add(new("D002", subject, $"unknown baseKind '{baseKind}'"));
        else if (baseKind == "pre-contract-deploy")
        {
            var pre = rule["base"]?["preContractDeploy"];
            if (Str(record["baseCommit"]) != Str(pre?["commit"]) || Long(record["baseRunId"]) != Long(pre?["runId"]))
                v.Add(new("D002", subject, "a pre-contract-deploy base must be the contract's recorded pre-contract deployment"));
        }
        if (Long(record["baseRunId"]) is { } baseRun && baseRun == Long(record["runId"]))
            v.Add(new("D002", subject, "a deploy cannot be its own base"));

        // D003: every changed path is documentation-only, re-classified here.
        var allowed = Array(rule["allowedPaths"]).Select(Str).OfType<string>().Select(GlobRegex).ToList();
        var denied = Array(rule["deniedPaths"]).Select(Str).OfType<string>().Select(GlobRegex).ToList();
        var inventoryPaths = Array(inventory["artifacts"])
            .SelectMany(a => Array(a?["paths"]).Select(Str).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        if (record["changedPaths"] is not JsonArray changed)
            v.Add(new("D003", subject, "changedPaths must list every changed build-input path (empty when none changed)"));
        else
        {
            foreach (var node in changed)
            {
                var path = Str(node);
                if (path is null || !IsDocumentationOnly(path, allowed, denied, inventoryPaths))
                    v.Add(new("D003", subject, $"changed path '{path}' is not documentation-only"));
            }
        }
        // Machinery (the deploying workflow and what it invokes) is never documentation: each change
        // since the base names the merged PR that brought it to main, and no website/ path is machinery.
        if (record["machineryChanges"] is not JsonArray machinery)
            v.Add(new("D003", subject, "machineryChanges must list every changed machinery path (empty when none changed)"));
        else
        {
            foreach (var change in machinery)
            {
                var path = Str(change?["path"]);
                if (string.IsNullOrWhiteSpace(path) || path.StartsWith("website/", StringComparison.Ordinal)
                    || path.Split('/').Any(segment => segment is ".." or "." or "") || Int(change?["pr"]) is not > 0)
                    v.Add(new("D003", subject, $"machinery change '{path}' needs a path outside website/ and the merged PR that changed it"));
            }
        }

        // D004: the recorded checks passed and the maintainer attested the semantic condition.
        if (Str(record["allowlistCheck"]?["result"]) != "passed"
            || record["allowlistCheck"]?["disallowedPaths"] is not JsonArray { Count: 0 })
            v.Add(new("D004", subject, "allowlistCheck must have passed with an empty disallowedPaths list"));
        foreach (var check in new[] { "fileModeCheck", "mdxCheck", "wordingCheck" })
        {
            if (Str(record[check]?["result"]) != "passed")
                v.Add(new("D004", subject, $"{check} must have passed"));
        }
        if (string.IsNullOrWhiteSpace(Str(record["attestation"]?["by"]))
            || Str(record["attestation"]?["statement"]) != Str(rule["auditRecord"]?["attestationStatement"]))
            v.Add(new("D004", subject, "attestation needs the dispatching maintainer and the contract's attestation statement verbatim"));

        // D005: nothing else is published.
        foreach (var flag in DocsDeployPublishFlags)
        {
            if (Bool(record["publishes"]?[flag]) != false)
                v.Add(new("D005", subject, $"publishes.{flag} must be recorded explicitly as false"));
        }

        // D006: the window is open only before #1408 records MILESTONE-SUCCEEDED.
        if (Bool(record["terminalSuccessRecordPresent"]) != false)
            v.Add(new("D006", subject, "a documentation-only deploy is valid only while no MILESTONE-SUCCEEDED terminal record exists (state false explicitly)"));
        return v;
    }

    private static bool IsDocumentationOnly(string path, List<Regex> allowed, List<Regex> denied, HashSet<string> inventoryPaths)
        => path.Length > 0
           && !path.StartsWith('/')
           && !path.Split('/').Any(segment => segment is ".." or "." or "")
           && allowed.Any(r => r.IsMatch(path))
           && !denied.Any(r => r.IsMatch(path))
           && !inventoryPaths.Contains(path);

    /// <summary>A case-sensitive glob: '**/' matches zero or more directories, '*' stays within one segment.</summary>
    internal static Regex GlobRegex(string glob)
    {
        var pattern = new System.Text.StringBuilder(@"\A");
        for (var i = 0; i < glob.Length; i++)
        {
            if (glob[i] == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                if (i + 2 < glob.Length && glob[i + 2] == '/')
                {
                    pattern.Append("(?:[^/]+/)*");
                    i += 2;
                }
                else
                {
                    pattern.Append(".*");
                    i += 1;
                }
            }
            else if (glob[i] == '*')
                pattern.Append("[^/]*");
            else
                pattern.Append(Regex.Escape(glob[i].ToString()));
        }
        return new Regex(pattern.Append(@"\z").ToString(), RegexOptions.CultureInvariant);
    }

    // ------------------------------------------------------------------
    // Packet hash manifest
    // ------------------------------------------------------------------

    /// <summary>H001: sha256.json must cover the contract document, contract.json, and the inventory by name.</summary>
    public static IReadOnlyList<ContractViolation> ValidatePacketManifest(JsonNode contract, JsonNode manifest, string contractPath)
    {
        var v = new List<ContractViolation>();
        var files = Keys(manifest["files"]);
        foreach (var required in new[] { Str(contract["document"]), contractPath, Str(contract["inventory"]) })
        {
            if (required is null || !files.Contains(required))
                v.Add(new("H001", "sha256.json", $"hash manifest does not cover required packet file '{required}'"));
        }
        return v;
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

    private static bool TryParseSemVer(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        var parts = text.Split('.');
        var numbers = new int[3];
        if (parts.Length != 3)
            return false;
        for (var i = 0; i < 3; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)
                || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }
        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static int? Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var i) ? i : null;

    private static long? Long(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var l) ? l : null;

    private static List<JsonNode?> Array(JsonNode? node) =>
        node is JsonArray array ? array.ToList() : [];

    private static HashSet<string> Keys(JsonNode? node) =>
        node is JsonObject obj ? obj.Select(p => p.Key).ToHashSet(StringComparer.Ordinal) : [];

    private static bool IsFullSha(string? sha) => sha is not null && FullSha.IsMatch(sha);

    private static bool IsUtcTimestamp(string? text) => text is not null && text.EndsWith('Z') && ParseUtc(text) is not null;

    private static DateTimeOffset? ParseUtc(string? text) =>
        text is not null && text.EndsWith('Z')
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;
}
