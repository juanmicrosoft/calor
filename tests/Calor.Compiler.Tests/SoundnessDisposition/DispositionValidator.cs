using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Calor.Compiler.Tests.SoundnessDisposition;

/// <summary>
/// #1413 (0.24 S2): validator for the disposition record
/// (<c>docs/plans/evidence/s2-1413/dispositions.json</c>). Each registered S1 row and each S1
/// finding, on B1 and on N1 separately, must get exactly one disposition from the frozen
/// vocabulary (contract.json <c>findingDispositions</c>), consistent with the registration's
/// disposition handoff. Every rule fails closed: a missing field is a violation, never a
/// default. <paramref name="closing"/> selects the closure rules: a record that claims S2 is
/// closed must have every repair merged and nothing left undecided.
/// </summary>
internal static class DispositionValidator
{
    internal sealed record Violation(string Code, string Message);

    private static readonly string[] Baselines = ["B1", "N1"];
    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

    internal static IReadOnlyList<Violation> Validate(
        JsonNode record,
        JsonNode contract,
        JsonNode s1Findings,
        JsonNode s1Run1Findings,
        JsonNode s1RowStatus,
        bool closing)
    {
        var v = new List<Violation>();
        void Add(string code, string message) => v.Add(new Violation(code, message));

        // D001 vocabulary: exactly the frozen set, in the record and as used.
        var frozen = contract["findingDispositions"]?.AsArray().Select(x => x!.GetValue<string>()).ToArray() ?? [];
        var vocabulary = record["vocabulary"]?.AsArray().Select(x => x?.GetValue<string>()).ToArray() ?? [];
        if (frozen.Length == 0 || !frozen.SequenceEqual(vocabulary))
            Add("D001", "vocabulary differs from contract.json findingDispositions");
        bool Known(string? disposition) => disposition != null && frozen.Contains(disposition);

        // D009 capacity.
        var repairs = record["repairs"]?.AsArray() ?? new JsonArray();
        var maxRepairs = record["capacity"]?["maxRepairs"]?.GetValue<int>();
        var maxLines = record["capacity"]?["maxNonTestChangedLinesPerRepair"]?.GetValue<int>();
        if (maxRepairs != 6 || maxLines != 600)
            Add("D009", "capacity must state the contract's s2-repairs ceilings (6 repairs, 600 non-test lines)");
        if (repairs.Count > 6)
            Add("D009", $"{repairs.Count} repairs exceed the 6-repair ceiling");

        // D008 repair fields, D010 closure.
        var repairIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var repair in repairs)
        {
            var id = Str(repair, "id");
            if (id == null || !repairIds.Add(id))
            {
                Add("D008", $"repair id missing or duplicated: {id}");
                continue;
            }
            var kind = Str(repair, "kind");
            if (kind is not ("FIX-IN-0.24" or "DEMOTE-IN-0.24"))
                Add("D008", $"{id}: kind must be FIX-IN-0.24 or DEMOTE-IN-0.24");
            foreach (var field in new[] { "rootCause", "change", "owner", "reviewer" })
                if (string.IsNullOrWhiteSpace(Str(repair, field)))
                    Add("D008", $"{id}: {field} is required");
            if (repair!["nonTestChangedLines"] is not JsonValue lines || lines.GetValue<int>() is < 0 or > 600)
                Add("D009", $"{id}: nonTestChangedLines missing or above 600");
            if (repair["blocks"]?.AsArray().Any(x => x?.GetValue<int>() == 1423) != true)
                Add("D008", $"{id}: must block the candidate freeze #1423");

            var status = Str(repair, "status");
            int? pr = repair["pr"] is JsonValue prValue && prValue.TryGetValue<int>(out var prNumber) ? prNumber : null;
            var witnesses = repair["regressionWitness"]?.AsArray().Count ?? 0;
            if (status == "decision-required")
            {
                if (repair["decisionOptions"]?.AsArray().Count is not > 0)
                    Add("D008", $"{id}: a decision-required repair must list its decision options");
                if (closing)
                    Add("D010", $"{id}: undecided at closure");
                continue;
            }
            if (status is not ("open" or "merged"))
                Add("D008", $"{id}: status must be open, merged, or decision-required");
            if (pr is not > 0 || string.IsNullOrWhiteSpace(Str(repair, "branch")) || witnesses == 0)
                Add("D008", $"{id}: an open or merged repair needs a PR, a branch, and a regression witness");
            if (status == "merged" && (Str(repair, "mergeCommit") is not { } sha || !FullSha.IsMatch(sha)))
                Add("D008", $"{id}: a merged repair needs its full merge commit SHA");
            if (closing && status != "merged")
                Add("D010", $"{id}: not merged at closure");
        }
        if (closing && Str(record["closure"], "status") != "CLOSED")
            Add("D010", "closure.status must be CLOSED when validated for closure");
        if (!closing && Str(record["closure"], "status") != "OPEN")
            Add("D010", "a record that is not closing must say closure.status OPEN");

        var repairsById = repairs.Where(r => Str(r, "id") != null)
            .GroupBy(r => Str(r, "id")!).ToDictionary(g => g.Key, g => g.First()!);

        foreach (var baseline in Baselines)
        {
            var b = record["baselines"]?[baseline];
            if (b == null)
            {
                Add("D002", $"baseline {baseline} missing");
                continue;
            }

            // D013 published artifacts are immutable; the disposition is candidate-side.
            if (b["artifact"]?["immutable"]?.GetValue<bool>() != true
                || string.IsNullOrWhiteSpace(Str(b["artifact"], "dispositionScope")))
                Add("D013", $"{baseline}: artifact must be recorded immutable with a candidate-side disposition scope");
            if (baseline == "N1" && string.IsNullOrWhiteSpace(Str(b["artifact"], "publishedArtifactObligation")))
                Add("D013", "N1: the published package's obligation (release-notes advisory) must be recorded");

            // D002 finding coverage: exactly the S1 findings, once each, matching row/case/class.
            var expected = (s1Findings[baseline]?.AsArray() ?? new JsonArray())
                .ToDictionary(f => Str(f, "findingId")!, f => f!);
            var run1 = (s1Run1Findings[baseline]?.AsArray() ?? new JsonArray())
                .ToDictionary(f => Str(f, "findingId")!, f => f!);
            if (run1.Count != expected.Count || run1.Any(kv => !expected.TryGetValue(kv.Key, out var e)
                    || Str(e, "caseId") != Str(kv.Value, "caseId") || Str(e, "class") != Str(kv.Value, "class")))
                Add("D002", $"{baseline}: run-1 and run-2 finding sets differ; both must be dispositioned");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dispositionsByRow = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var f in b["findings"]?.AsArray() ?? new JsonArray())
            {
                var id = Str(f, "findingId");
                if (id == null || !seen.Add(id))
                {
                    Add("D002", $"{baseline}: finding {id} missing an id or duplicated");
                    continue;
                }
                if (!expected.TryGetValue(id, out var source))
                {
                    Add("D002", $"{baseline}: {id} is not an S1 finding");
                    continue;
                }
                if (Str(f, "rowId") != Str(source, "rowId") || Str(f, "caseId") != Str(source, "caseId")
                    || Str(f, "class") != Str(source, "class"))
                    Add("D002", $"{baseline}: {id} row, case, or class differs from S1");

                var disposition = Str(f, "disposition");
                if (!Known(disposition))
                {
                    Add("D001", $"{baseline}: {id} has unknown disposition '{disposition}'");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(Str(f, "reason")))
                    Add("D011", $"{baseline}: {id} has no reason");
                // D005 a finding is never VALIDATED or NOT-INVESTIGATED.
                if (disposition is "VALIDATED" or "NOT-INVESTIGATED")
                    Add("D005", $"{baseline}: {id} is a finding and cannot be {disposition}");
                // D006 a false unconditional proof must be fixed, demoted, or fail the milestone.
                if (Str(source, "class") == "false-unconditional-proof"
                    && disposition is not ("FIX-IN-0.24" or "DEMOTE-IN-0.24" or "MILESTONE-FAILED"))
                    Add("D006", $"{baseline}: false proof {id} must be FIX, DEMOTE, or MILESTONE-FAILED");
                // D007 a FIX/DEMOTE names its repair, and the repair claims the finding's row.
                if (disposition is "FIX-IN-0.24" or "DEMOTE-IN-0.24")
                {
                    var repairId = Str(f, "repair");
                    if (repairId == null || !repairsById.TryGetValue(repairId, out var repair))
                        Add("D007", $"{baseline}: {id} names no known repair");
                    else if (repair["rows"]?.AsArray().Any(r => r?.GetValue<string>() == Str(source, "rowId")) != true)
                        Add("D007", $"{baseline}: repair {repairId} does not list row {Str(source, "rowId")}");
                }
                dispositionsByRow.TryAdd(Str(source, "rowId")!, []);
                dispositionsByRow[Str(source, "rowId")!].Add(disposition!);
            }
            foreach (var missing in expected.Keys.Where(k => !seen.Contains(k)))
                Add("D002", $"{baseline}: S1 finding {missing} has no disposition");

            // D003 row coverage and D004/D012 row rules.
            var rowsExpected = (s1RowStatus["baselines"]?[baseline]?["rows"]?.AsArray() ?? new JsonArray())
                .ToDictionary(r => Str(r, "row")!, r => Str(r, "combined")!);
            var rowsSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in b["rows"]?.AsArray() ?? new JsonArray())
            {
                var rowId = Str(row, "rowId");
                if (rowId == null || !rowsSeen.Add(rowId) || !rowsExpected.TryGetValue(rowId, out var status))
                {
                    Add("D003", $"{baseline}: row {rowId} unknown, unnamed, or duplicated");
                    continue;
                }
                if (Str(row, "status") != status)
                    Add("D003", $"{baseline}: row {rowId} status differs from the S1 combined status");
                var disposition = Str(row, "disposition");
                if (!Known(disposition))
                {
                    Add("D001", $"{baseline}: row {rowId} has unknown disposition '{disposition}'");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(Str(row, "reason")))
                    Add("D011", $"{baseline}: row {rowId} has no reason");
                if ((disposition == "VALIDATED") != (status == "CLEAN-WITHIN-BUDGET"))
                    Add("D004", $"{baseline}: row {rowId} ({status}) cannot be {disposition}; VALIDATED is exactly the CLEAN-WITHIN-BUDGET rows");
                if ((disposition == "NOT-INVESTIGATED") != (status == "NOT-INVESTIGATED"))
                    Add("D004", $"{baseline}: row {rowId} ({status}) cannot be {disposition}; NOT-INVESTIGATED is exactly the excluded rows");
                var findingDispositions = dispositionsByRow.GetValueOrDefault(rowId) ?? [];
                if (status is "FINDING" or "FALSE-PROOF" && findingDispositions.Count == 0)
                    Add("D012", $"{baseline}: row {rowId} has status {status} but no dispositioned finding");
                if (findingDispositions.Count > 0)
                {
                    var expectedRow = findingDispositions.Contains("MILESTONE-FAILED") ? "MILESTONE-FAILED"
                        : findingDispositions.Contains("DEMOTE-IN-0.24") ? "DEMOTE-IN-0.24"
                        : "FIX-IN-0.24";
                    if (disposition != expectedRow)
                        Add("D012", $"{baseline}: row {rowId} must be {expectedRow}, the most conservative disposition of its findings");
                }
            }
            foreach (var missing in rowsExpected.Keys.Where(k => !rowsSeen.Contains(k)))
                Add("D003", $"{baseline}: registered row {missing} has no disposition");
        }

        // D014 discovery findings (outside the registered sweep).
        foreach (var d in record["discoveryFindings"]?.AsArray() ?? new JsonArray())
        {
            var id = Str(d, "id");
            if (d!["issue"] is not JsonValue || d["registered"]?.GetValue<bool>() != false
                || string.IsNullOrWhiteSpace(Str(d, "source")) || string.IsNullOrWhiteSpace(Str(d, "reason")))
                Add("D014", $"discovery {id}: needs an issue, registered=false, a source, and a reason");
            var disposition = Str(d, "disposition");
            if (!Known(disposition) || disposition is "VALIDATED" or "NOT-INVESTIGATED")
                Add("D014", $"discovery {id}: disposition '{disposition}' is not allowed");
            if (disposition is "FIX-IN-0.24" or "DEMOTE-IN-0.24"
                && (Str(d, "repair") is not { } repairId || !repairsById.TryGetValue(repairId, out var repair)
                    || repair["discoveries"]?.AsArray().Any(x => x?.GetValue<string>() == id) != true))
                Add("D014", $"discovery {id}: must name a repair that lists it");
        }

        return v;
    }

    private static string? Str(JsonNode? node, string name)
        => node?[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
