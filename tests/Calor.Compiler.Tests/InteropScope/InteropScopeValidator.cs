using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.EvidenceContract;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1426 (0.25 R0): validates the interoperability scope and baseline packet
/// (<c>docs/plans/evidence/v0.25-r0-1426/scope.json</c>) against the observed baseline results.
/// It fails closed on a family missing from the denominator, a case removed or reclassified
/// without an amendment, fixture drift, a READY record without its commit, tests, examples,
/// reviews, approver or resolved blockers, and a gate marked MET while a condition is open.
/// It checks that evidence is present and consistent; it does not re-run the converter.
/// </summary>
internal static class InteropScopeValidator
{
    public const string PacketDir = "docs/plans/evidence/v0.25-r0-1426";

    /// <summary>The six families the epic (#1425) retains, keyed by family id.</summary>
    public static readonly IReadOnlyDictionary<string, int> RequiredFamilies = new Dictionary<string, int>
    {
        ["F1"] = 943, ["F2"] = 906, ["F3"] = 847, ["F4"] = 1139, ["F5"] = 1132, ["F6"] = 1144,
    };

    private const int CandidateFreezeIssue = 1423;
    private const int RefOutDesignIssue = 1427;
    private const int ScopeGateIssue = 1426;

    /// <summary>Gate conditions registered at 1.0.0; none may be dropped to reach MET.</summary>
    public static readonly IReadOnlyList<string> RequiredGateConditions =
        ["blocker-1413", "capacity-accepted", "independence-deviation-accepted", "blockers-mapped-after-1413",
         "interaction-and-doc-rows-registered"];
    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex SemVer = new(@"^(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    public static IReadOnlyList<ContractViolation> Validate(
        JsonNode scope, JsonNode results, Func<string, byte[]?> readRepoFile, IReadOnlyCollection<string> fixtureFiles)
    {
        var v = new List<ContractViolation>();
        var vocab = scope["vocabularies"]!;
        var families = Arr(scope["families"]).ToList();
        var cases = Arr(scope["cases"]).ToList();
        var amendments = Arr(scope["amendments"]).ToList();
        var removedCases = amendments.SelectMany(a => Arr(a["removedCases"])).ToList();
        var removedFamilies = amendments.SelectMany(a => Arr(a["removedFamilies"])).ToList();
        bool Amended(string key, string id) =>
            amendments.Any(a => Arr(a[key]).Any(x => Str(x) == id || Str(x?["id"]) == id));

        // S001: the six-family denominator.
        foreach (var (id, issue) in RequiredFamilies)
        {
            var matches = families.Where(f => Str(f["id"]) == id).ToList();
            var removed = removedFamilies.Any(r => Str(r["id"]) == id && Str(r["lastStatus"]) is { Length: > 0 });
            if (matches.Count == 0 && !removed)
                v.Add(new("S001", id, $"family {id} (#{issue}) is missing from the denominator without an amendment that retains it"));
            if (matches.Count > 1)
                v.Add(new("S001", id, "duplicate family id"));
            if (matches.Count == 1 && Int(matches[0]["issue"]) != issue)
                v.Add(new("S001", id, $"family {id} must track #{issue}"));
        }
        foreach (var f in families.Where(f => !RequiredFamilies.ContainsKey(Str(f["id"]) ?? "")))
            v.Add(new("S001", Str(f["id"]) ?? "?", "unknown family"));

        // S012: every family waits for the repaired 0.24 candidate; F1 also waits for the ref/out design.
        foreach (var f in families)
        {
            var blockers = Arr(f["blockers"]).Select(b => Int(b)).ToHashSet();
            if (!blockers.Contains(CandidateFreezeIssue))
                v.Add(new("S012", Str(f["id"]) ?? "?", $"family blockers must include #{CandidateFreezeIssue}"));
            if (Str(f["id"]) == "F1" && !blockers.Contains(RefOutDesignIssue))
                v.Add(new("S012", "F1", $"F1 blockers must include #{RefOutDesignIssue}"));
            if (Str(f["id"]) != "F1" && !blockers.Contains(ScopeGateIssue))
                v.Add(new("S012", Str(f["id"]) ?? "?", $"family blockers must include #{ScopeGateIssue}"));
        }

        // S002: case shape and vocabularies.
        var seen = new HashSet<string>();
        foreach (var c in cases)
        {
            var id = Str(c["id"]) ?? "";
            if (id.Length == 0 || !seen.Add(id))
                v.Add(new("S002", id, "case id empty or duplicated"));
            if (!families.Any(f => Str(f["id"]) == Str(c["family"])))
                v.Add(new("S002", id, "case belongs to no registered family"));
            foreach (var (field, list) in new[] { ("role", "role"), ("expected", "expected"), ("baselineStatus", "baselineStatus") })
                if (!In(vocab[list], Str(c[field])))
                    v.Add(new("S002", id, $"{field} '{Str(c[field])}' is not in the frozen vocabulary"));
            var hasBaseline = c["baseline"] is JsonObject b && b.Count > 0 || Arr(c["observations"]).Any();
            if (Str(c["baselineStatus"]) != "not-measured" && !hasBaseline)
                v.Add(new("S002", id, "a measured status needs a baseline or observations"));
        }
        foreach (var f in families.Where(f => !cases.Any(c => Str(c["family"]) == Str(f["id"]))))
            v.Add(new("S002", Str(f["id"]) ?? "?", "family has no registered case"));

        // S003: fixture integrity and registration.
        var registered = cases.Concat(Arr(scope["discovered"])).Where(c => Str(c["fixture"]) is not null).ToList();
        foreach (var c in registered)
        {
            var bytes = readRepoFile(Str(c["fixture"])!);
            if (bytes is null || Sha256(bytes) != Str(c["fixtureSha256"]))
                v.Add(new("S003", Str(c["id"]) ?? "?", "fixture missing or its LF-normalized SHA-256 differs from the registered hash"));
        }
        var known = registered.Concat(Arr(scope["denominatorV1"])).Select(c => Str(c["fixture"])).ToHashSet();
        foreach (var file in fixtureFiles.Where(f => !known.Contains(f)))
            v.Add(new("S003", file, "fixture file is not registered by any case"));

        // S004: no silent scope reduction, reclassification or rehash against the frozen 1.0.0 denominator.
        foreach (var d in Arr(scope["denominatorV1"]))
        {
            var id = Str(d["id"])!;
            var current = cases.FirstOrDefault(c => Str(c["id"]) == id);
            if (current is null)
            {
                if (!removedCases.Any(r => Str(r["id"]) == id && Str(r["lastStatus"]) is { Length: > 0 }))
                    v.Add(new("S004", id, "registered case removed without an amendment that retains it with its last status"));
                continue;
            }
            if (Str(current["family"]) != Str(d["family"]))
                v.Add(new("S004", id, "case moved to another family"));
            foreach (var field in new[] { "expected", "role", "baselineStatus" })
                if (Str(current[field]) != Str(d[field]) && !Amended("reclassifiedCases", id))
                    v.Add(new("S004", id, $"{field} changed without an amendment"));
            if (Str(current["fixtureSha256"]) != Str(d["fixtureSha256"]) && !Amended("rehashedCases", id))
                v.Add(new("S004", id, "fixture hash changed without an amendment"));
        }

        // S005: amendment records.
        var previous = (1, 0, 0);
        foreach (var a in amendments)
        {
            var version = Str(a["version"]);
            var m = version is null ? null : SemVer.Match(version);
            var parsed = m is { Success: true } ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : (0, 0, 0);
            if (m is not { Success: true } || parsed.CompareTo(previous) <= 0)
                v.Add(new("S005", version ?? "?", "amendment version must be MAJOR.MINOR.PATCH and strictly increasing from 1.0.0"));
            previous = parsed;
            if (Str(a["dateUtc"]) is not { } date || !date.EndsWith('Z') || Int(a["pr"]) <= 0
                || Str(a["justification"]) is not { Length: > 0 } || a["afterInspection"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                v.Add(new("S005", version ?? "?", "amendment needs a UTC date, PR, justification and a boolean afterInspection"));
        }
        var latest = amendments.Count == 0 ? "1.0.0" : Str(amendments[^1]["version"]);
        if (Str(scope["scopeVersion"]) != latest)
            v.Add(new("S005", "scopeVersion", "scopeVersion must equal the latest amendment version"));

        // S006-S008, S011: readiness records.
        var deviation = scope["independence"]?["deviation"]?.GetValue<bool>() ?? true;
        var recordValue = Str(scope["independence"]?["recordValue"]);
        if (deviation && (recordValue != "reduced" || Str(scope["independence"]?["statement"]) is not { Length: > 0 }))
            v.Add(new("S011", "independence", "the reduced-independence deviation must be recorded as 'reduced' with its statement"));
        // Lifting the deviation is itself a decision: it names the reviewer and the amendment that did it.
        if (!deviation && (Str(scope["independence"]?["reviewer"]) is not { Length: > 0 }
            || !amendments.Any(a => Str(a["version"]) == Str(scope["independence"]?["liftedBy"]))))
            v.Add(new("S011", "independence", "lifting the deviation needs a named reviewer and the amendment that lifted it"));
        foreach (var f in families)
        {
            var fid = Str(f["id"]) ?? "?";
            var r = f["readiness"];
            var status = Str(r?["status"]);
            if (!In(vocab["readiness"], status))
                v.Add(new("S006", fid, $"readiness status '{status}' is not NOT_STARTED, IN_PROGRESS, READY or BLOCKED"));
            if (status == "READY")
                ValidateReady(v, fid, r!, f, cases.Where(c => Str(c["family"]) == fid).ToList(), vocab, deviation, recordValue);
            if (status == "BLOCKED" && (Str(r?["reason"]) is not { Length: > 0 } || !Arr(r?["blockedBy"]).Any()))
                v.Add(new("S008", fid, "a BLOCKED record needs a reason and blockedBy"));
            if (status == "IN_PROGRESS" && Str(r?["pr"]) is null && Str(r?["branch"]) is null)
                v.Add(new("S008", fid, "an IN_PROGRESS record needs a PR or branch"));
        }

        // S009-S010: lifecycle and capacity.
        var lifecycle = Str(scope["status"]);
        var gate = Str(scope["gateStatus"]);
        var capacity = scope["capacity"];
        var ceilings = Arr(capacity?["ceilings"]).ToList();
        if (lifecycle is not ("PROPOSED" or "FROZEN") || gate is not ("MET" or "NOT-MET"))
            v.Add(new("S009", "status", "status must be PROPOSED or FROZEN and gateStatus MET or NOT-MET"));
        if (lifecycle == "PROPOSED" && (gate != "NOT-MET" || scope["acceptance"] is not null
            || Str(capacity?["status"]) != "PROPOSED" || ceilings.Any(c => Str(c["status"]) != "PROPOSED")))
            v.Add(new("S009", "status", "a PROPOSED packet is NOT-MET, has no acceptance record, and every ceiling is PROPOSED"));
        if (lifecycle == "FROZEN" && (!IsFullSha(Str(scope["acceptance"]?["mergeCommit"]))
            || Str(scope["acceptance"]?["mergedAtUtc"]) is not { } at || !at.EndsWith('Z') || Int(scope["acceptance"]?["pr"]) <= 0
            || Str(capacity?["status"]) != "ACCEPTED" || ceilings.Any(c => Str(c["status"]) != "ACCEPTED")))
            v.Add(new("S009", "status", "a FROZEN packet needs a full acceptance record and every ceiling ACCEPTED"));
        if (gate == "MET" && (lifecycle != "FROZEN"
            || Arr(scope["gateConditions"]).Any(g => Str(g["state"]) != "SATISFIED" || Str(g["evidence"]) is not { Length: > 0 })))
            v.Add(new("S009", "gateStatus", "R0 is MET only when FROZEN and every gate condition is SATISFIED with evidence"));
        foreach (var id in RequiredGateConditions.Where(id => !Arr(scope["gateConditions"]).Any(g => Str(g["id"]) == id)))
            v.Add(new("S009", id, "a registered gate condition was removed"));
        if (!Arr(scope["gateConditions"]).Any(g => Str(g["id"]) == "blocker-1413" && Int(g["issue"]) == 1413))
            v.Add(new("S009", "blocker-1413", "the #1413 closure condition must name #1413"));
        var ceilingIds = new HashSet<string>();
        foreach (var c in ceilings)
            if (!ceilingIds.Add(Str(c["id"]) ?? "") || c["value"]?.GetValueKind() != JsonValueKind.Number
                || Str(c["unit"]) is not { Length: > 0 } || Str(c["status"]) is not ("PROPOSED" or "ACCEPTED"))
                v.Add(new("S010", Str(c["id"]) ?? "?", "ceiling needs a unique id, numeric value, unit and PROPOSED/ACCEPTED status"));
        if (!ceilings.Any(c => Str(c["id"]) == "paid-spend" && c["value"]?.ToJsonString() == "0"))
            v.Add(new("S010", "paid-spend", "the paid-spend ceiling must be registered at 0"));

        // S013: every recorded baseline is derived from the committed results.
        if (Str(results["srcTree"]) != Str(scope["identities"]?["srcTree"]) || !IsFullSha(Str(results["srcTree"]))
            || !IsFullSha(Str(results["measuredCommit"])))
            v.Add(new("S013", "srcTree", "results must come from a full commit whose src/ tree is the registered compiler source tree"));
        var byCase = Arr(results["cases"]).ToDictionary(c => Str(c["case"])!, c => c);
        var byCalr = Arr(results["calorCases"]).ToDictionary(c => Str(c["case"])!, c => c);
        foreach (var c in cases.Concat(Arr(scope["discovered"])))
        {
            var id = Str(c["id"])!;
            foreach (var (surface, value) in (c["baseline"] as JsonObject) ?? new JsonObject())
            {
                string? derived = byCase.TryGetValue(id, out var rc) ? Derive(rc["surfaces"]?[surface])
                    : byCalr.TryGetValue(id, out var cc) && surface == "compile" ? DeriveCompile(cc["compile"]) : null;
                if (derived != Str(value))
                    v.Add(new("S013", $"{id}/{surface}", $"recorded baseline '{Str(value)}' differs from the results ('{derived}')"));
            }
            foreach (var o in Arr(c["observations"]))
            {
                var surface = byCase.GetValueOrDefault(Str(o["case"]) ?? "")?["surfaces"]?[Str(o["surface"]) ?? ""] as JsonObject;
                var path = Str(o["path"]) ?? "";
                if (surface is null || !surface.ContainsKey(path) || !JsonNode.DeepEquals(surface[path], o["equals"]))
                    v.Add(new("S013", id, $"observation {Str(o["case"])}/{Str(o["surface"])}/{Str(o["path"])} does not match the results"));
            }
        }
        return v;
    }

    private static void ValidateReady(List<ContractViolation> v, string fid, JsonNode r, JsonNode family,
        List<JsonNode> familyCases, JsonNode vocab, bool deviation, string? recordValue)
    {
        void Fail(string what) => v.Add(new("S007", fid, $"READY record: {what}"));
        if (!IsFullSha(Str(r["candidate"]?["commit"])))
            Fail("candidate.commit must be a full 40-hex commit");
        var recorded = Arr(r["cases"]).ToList();
        foreach (var c in familyCases)
        {
            var row = recorded.FirstOrDefault(x => Str(x["id"]) == Str(c["id"]));
            if (row is null || !In(vocab["caseResult"], Str(row["result"])))
                Fail($"case {Str(c["id"])} has no recorded result");
            else if (Str(row["result"]) != "passed")
                Fail($"case {Str(c["id"])} is {Str(row["result"])}, not passed");
        }
        var tests = Arr(r["tests"]).ToList();
        if (tests.Count == 0 || tests.Any(t => Str(t["project"]) is null || Int(t["total"]) <= 0 || Int(t["failed"]) != 0))
            Fail("tests must name each project with a positive total and zero failures");
        if (!Arr(r["websiteExamples"]).Any())
            Fail("websiteExamples must list the family's checked website examples");
        if (!Arr(r["reviews"]).Any() || Arr(r["reviews"]).Any(x => Str(x["reviewer"]) is null || Str(x["record"]) is null))
            Fail("reviews must name each reviewer and its retained record");
        if (Str(r["approver"]?["login"]) is null || Str(r["approver"]?["approvalUrl"]) is null)
            Fail("approver needs a login and an approval URL");
        if (deviation && Str(r["independence"]) != recordValue)
            Fail($"independence must be '{recordValue}' while the deviation stands");
        foreach (var b in Arr(family["blockers"]))
            if (!Arr(r["blockersResolved"]).Any(x => Int(x["issue"]) == Int(b) && Str(x["reference"]) is { Length: > 0 }))
                Fail($"blocker #{Int(b)} has no recorded resolution");
    }

    /// <summary>One surface's outcome: native/preserved with match/mismatch, or the compile error codes.</summary>
    public static string? Derive(JsonNode? s)
    {
        if (s is null) return null;
        if (s["execution"] is { } e)
        {
            var interop = Int(s["interopPreservationCount"]) > 0 || Int(s["interopPreservations"]) > 0
                || Int(s["csharpInteropBlocks"]) > 0 || Int(s["inlineInteropExpressions"]) > 0;
            var match = e["matchesOriginal"]?.GetValue<bool>() == true;
            return (interop ? "preserved" : "native") + (match ? "-match" : "-mismatch")
                + (Str(e["compileMode"]) == "permissive-effects" ? "@permissive-effects" : "");
        }
        if (s["compile"] is { } k)
            return "compile-error:" + Codes(k["errorCodes"])
                + (s["compilePermissiveEffects"] is { } p ? ";permissive-effects:" + Codes(p["errorCodes"]) : "");
        return "no-output";
    }

    public static string DeriveCompile(JsonNode? k) => Int(k?["exit"]) == 0
        ? "compiled;warnings:" + Codes(k?["warningCodes"])
        : "compile-error:" + Codes(k?["errorCodes"]) + "@line" + Int(k?["firstErrorLine"]);

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"))));

    private static string Codes(JsonNode? n) => string.Join(",", Arr(n).Select(Str));
    private static bool In(JsonNode? list, string? value) => value is not null && Arr(list).Any(x => Str(x) == value);
    private static bool IsFullSha(string? s) => s is not null && FullSha.IsMatch(s);
    private static IEnumerable<JsonNode> Arr(JsonNode? n) => n is JsonArray a ? a.OfType<JsonNode>() : [];
    private static string? Str(JsonNode? n) => n is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
    private static int Int(JsonNode? n) => n is JsonValue jv && jv.TryGetValue<int>(out var i) ? i : 0;
}
