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
        return v;
    }

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
            if (Int(entry?["reviewedInPr"]) is null)
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

        return v;
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

            v.AddRange(ValidateBenchmark(row, id, keyFields, pairFields, pairDispositions, samplingUnit));

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
    /// T001–T003: the machine-checkable part of the terminal-success predicate. MILESTONE-SUCCEEDED
    /// needs no BLOCKED adjudication of any release-critical artifact, gate, or claim, and, under the
    /// recorded independence deviation, adjudicationIndependence = reduced with no SUPPORTED row.
    /// </summary>
    public static IReadOnlyList<ContractViolation> ValidateTerminalRecord(JsonNode contract, JsonNode record)
    {
        var v = new List<ContractViolation>();
        var terminal = Array(contract["terminalOutcomes"]).Select(Str).OfType<string>().ToHashSet();
        var adjudicationOutcomes = Array(contract["adjudicationOutcomes"]).Select(Str).OfType<string>().ToHashSet();
        var outcome = Str(record["outcome"]);
        if (outcome is null || !terminal.Contains(outcome))
            v.Add(new("T003", "terminal", $"unknown terminal outcome '{outcome}'"));

        var rows = Array(record["adjudications"]);
        foreach (var row in rows)
        {
            var subject = Str(row?["subject"]) ?? "?";
            var adjudication = Str(row?["outcome"]);
            if (adjudication is null || !adjudicationOutcomes.Contains(adjudication))
                v.Add(new("T003", subject, $"unknown adjudication outcome '{adjudication}'"));
            if (Bool(row?["releaseCritical"]) is null)
                v.Add(new("T003", subject, "adjudication row must state releaseCritical as a boolean"));
        }

        if (outcome != "MILESTONE-SUCCEEDED")
            return v;

        if (rows.Count == 0)
            v.Add(new("T001", "terminal", "a success record with no adjudication rows establishes nothing"));
        foreach (var row in rows.Where(r => Str(r?["outcome"]) == "BLOCKED" && Bool(r?["releaseCritical"]) != false))
            v.Add(new("T001", Str(row?["subject"]) ?? "?", "a release-critical BLOCKED adjudication makes the milestone fail"));

        var deviation = Bool(contract["authorityCapacity"]?["independence"]?["deviation"]) != false;
        if (deviation)
        {
            if (Str(record["adjudicationIndependence"]) != "reduced")
                v.Add(new("T002", "terminal", "under the independence deviation a success record carries adjudicationIndependence = reduced"));
            foreach (var row in rows.Where(r => Str(r?["outcome"]) == "SUPPORTED"))
                v.Add(new("T002", Str(row?["subject"]) ?? "?", "SUPPORTED is unavailable under the independence deviation"));
            foreach (var row in rows.Where(r => Str(r?["independence"]) != "reduced-maintainer-adjudicated"))
                v.Add(new("T002", Str(row?["subject"]) ?? "?", "every adjudication row records independence = reduced-maintainer-adjudicated"));
        }
        return v;
    }

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
        HashSet<string> dispositions, string? samplingUnit)
    {
        var bench = row["benchmark"];
        if (bench is null)
            yield break;

        if (Str(bench["samplingUnit"]) != samplingUnit)
            yield return new("E010", id, $"sampling unit '{Str(bench["samplingUnit"])}' is not the frozen '{samplingUnit}'");

        var pairs = Array(bench["pairs"]);
        if (pairs.Count == 0)
            yield return new("E009", id, "benchmark row has an empty pair denominator");
        foreach (var pair in pairs)
        {
            var pairId = Str(pair?["pairId"]) ?? "?";
            foreach (var field in pairFields.Where(f => IsBlank(pair?[f])))
                yield return new("E009", id, $"pair {pairId} is missing required field '{field}'");
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
