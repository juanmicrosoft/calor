using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.EvidenceContract;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1426 (0.25 R0): validates the interoperability scope and baseline packet
/// (<c>docs/plans/evidence/v0.25-r0-1426/scope.json</c>) against the sealed baseline evidence.
/// It fails closed on a family missing from the denominator, a case added, removed, reclassified
/// or rehashed without a matching amendment, fixture or evidence drift, a READY record without
/// typed, resolvable commit/tests/examples/reviews/approver/blocker evidence or made stale by a
/// later amendment, and a gate marked MET without its amendment and retained evidence.
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

    /// <summary>Frozen vocabularies. The packet must repeat them exactly; it cannot extend them.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Vocabularies = new Dictionary<string, string[]>
    {
        ["readiness"] = ["NOT_STARTED", "IN_PROGRESS", "READY", "BLOCKED"],
        ["expected"] = ["native", "preserved", "rejected", "native-or-rejected", "native-or-preserved", "preserved-or-rejected"],
        ["role"] = ["control", "residual", "decision", "interaction", "doc-example"],
        ["baselineStatus"] = ["reproduces", "partially-reproduces", "no-longer-reproduces", "control-passes", "not-measured"],
        ["caseResult"] = ["passed", "failed", "skipped", "reverted"],
    };

    /// <summary>The surfaces every C# fixture is measured on.</summary>
    public static readonly IReadOnlyList<string> Surfaces = ["cli-default", "cli-passthrough", "mcp-default", "mcp-passthroughOnError"];

    /// <summary>Gate conditions registered at 1.0.0; none may be dropped to reach MET.</summary>
    public static readonly IReadOnlyList<string> RequiredGateConditions =
        ["blocker-1413", "capacity-accepted", "independence-deviation-accepted", "blockers-mapped-after-1413",
         "interaction-and-doc-rows-registered"];

    private static readonly string[] TrackedFields = ["family", "role", "expected", "baselineStatus", "fixtureSha256"];
    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.Compiled);
    private static readonly Regex SemVer = new(@"^(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    public static IReadOnlyList<ContractViolation> Validate(JsonNode scope, JsonNode results,
        Func<string, byte[]?> readRepoFile, IReadOnlyCollection<string> fixtureFiles, IReadOnlyCollection<string> generatedFiles)
    {
        var v = new List<ContractViolation>();
        var families = Arr(scope["families"]).ToList();
        var cases = Arr(scope["cases"]).ToList();
        var amendments = Arr(scope["amendments"]).ToList();
        var removedFamilies = amendments.SelectMany(a => Arr(a["removedFamilies"])).ToList();
        bool Exists(string? path) => path is { Length: > 0 } && readRepoFile(path.Split('#')[0]) is not null;

        // S001: the six-family denominator.
        foreach (var (id, issue) in RequiredFamilies)
        {
            var matches = families.Where(f => Str(f["id"]) == id).ToList();
            if (matches.Count == 0 && !removedFamilies.Any(r => Str(r["id"]) == id && NonBlank(r["lastStatus"])))
                v.Add(new("S001", id, $"family {id} (#{issue}) is missing from the denominator without an amendment that retains it"));
            if (matches.Count > 1)
                v.Add(new("S001", id, "duplicate family id"));
            if (matches.Count == 1 && Int(matches[0]["issue"]) != issue)
                v.Add(new("S001", id, $"family {id} must track #{issue}"));
        }
        foreach (var f in families.Where(f => !RequiredFamilies.ContainsKey(Str(f["id"]) ?? "")))
            v.Add(new("S001", Str(f["id"]) ?? "?", "unknown family"));

        // S012: every family waits for the repaired 0.24 candidate and the scope gate (F1 through #1427).
        foreach (var f in families)
        {
            var fid = Str(f["id"]) ?? "?";
            var blockers = Arr(f["blockers"]).Select(b => Int(b)).ToHashSet();
            foreach (var need in fid == "F1" ? new[] { 1423, 1427 } : new[] { 1423, 1426 })
                if (!blockers.Contains(need))
                    v.Add(new("S012", fid, $"family blockers must include #{need}"));
        }

        // S002: case shape and the frozen vocabularies.
        foreach (var (name, values) in Vocabularies)
            if (!Arr(scope["vocabularies"]?[name]).Select(Str).SequenceEqual(values))
                v.Add(new(name == "readiness" ? "S006" : "S002", name, "the packet vocabulary differs from the frozen vocabulary"));
        var seen = new HashSet<string>();
        foreach (var c in cases)
        {
            var id = Str(c["id"]) ?? "";
            if (id.Length == 0 || !seen.Add(id))
                v.Add(new("S002", id, "case id empty or duplicated"));
            if (!families.Any(f => Str(f["id"]) == Str(c["family"])))
                v.Add(new("S002", id, "case belongs to no registered family"));
            foreach (var field in new[] { "role", "expected", "baselineStatus" })
                if (!Vocabularies[field].Contains(Str(c[field])))
                    v.Add(new("S002", id, $"{field} '{Str(c[field])}' is not in the frozen vocabulary"));
            var hasBaseline = c["baseline"] is JsonObject { Count: > 0 } || Arr(c["observations"]).Any();
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

        // S005: amendment records.
        var previous = (1, 0, 0);
        foreach (var a in amendments)
        {
            var version = Str(a["version"]);
            var parsed = Version(version);
            if (parsed is null || parsed.Value.CompareTo(previous) <= 0)
                v.Add(new("S005", version ?? "?", "amendment version must be MAJOR.MINOR.PATCH and strictly increasing from 1.0.0"));
            previous = parsed ?? previous;
            if (Str(a["dateUtc"]) is not { } date || !date.EndsWith('Z') || Int(a["pr"]) <= 0 || !NonBlank(a["justification"])
                || a["afterInspection"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                v.Add(new("S005", version ?? "?", "amendment needs a UTC date, PR, justification and a boolean afterInspection"));
        }
        var latest = amendments.Count == 0 ? "1.0.0" : Str(amendments[^1]["version"]);
        if (Str(scope["scopeVersion"]) != latest)
            v.Add(new("S005", "scopeVersion", "scopeVersion must equal the latest amendment version"));

        // S004: every registered case (1.0.0 or added by amendment) stays, or is removed with its last
        // status; every tracked field equals its registered value after the amendments' exact
        // from -> to changes, applied in order. A case nobody registered fails.
        var registry = Arr(scope["denominatorV1"]).ToDictionary(d => Str(d["id"])!, d => (JsonNode)d.DeepClone());
        var touched = new Dictionary<string, string>(); // case id -> latest amendment version that touched it
        foreach (var a in amendments)
        {
            var version = Str(a["version"]) ?? "?";
            foreach (var added in Arr(a["addedCases"]))
                if (Str(added["id"]) is { } aid && registry.TryAdd(aid, added.DeepClone()))
                    touched[aid] = version;
            foreach (var ch in Arr(a["changes"]))
            {
                var cid = Str(ch["id"]) ?? "";
                var field = Str(ch["field"]) ?? "";
                if (!registry.TryGetValue(cid, out var entry) || !TrackedFields.Contains(field) || Str(entry[field]) != Str(ch["from"]))
                    v.Add(new("S004", cid, $"amendment {version} changes {field} from a value that is not the registered one"));
                else
                {
                    entry[field] = Str(ch["to"]);
                    touched[cid] = version;
                }
            }
            foreach (var r in Arr(a["removedCases"]))
                if (Str(r["id"]) is { } rid)
                    touched[rid] = version;
        }
        var removed = amendments.SelectMany(a => Arr(a["removedCases"]))
            .Where(r => NonBlank(r["lastStatus"])).Select(r => Str(r["id"])).ToHashSet();
        foreach (var (id, entry) in registry)
        {
            var current = cases.FirstOrDefault(c => Str(c["id"]) == id);
            if (current is null)
            {
                if (!removed.Contains(id))
                    v.Add(new("S004", id, "registered case removed without an amendment that retains it with its last status"));
                continue;
            }
            foreach (var field in TrackedFields.Where(fl => Str(current[fl]) != Str(entry[fl])))
                v.Add(new("S004", id, $"{field} differs from its registered value without a matching amendment change"));
        }
        foreach (var c in cases.Where(c => !registry.ContainsKey(Str(c["id"]) ?? "")))
            v.Add(new("S004", Str(c["id"]) ?? "?", "case added without an amendment that registers it"));

        // S006-S008, S011: readiness records.
        var deviation = scope["independence"]?["deviation"]?.GetValue<bool>() ?? true;
        var recordValue = Str(scope["independence"]?["recordValue"]);
        if (deviation && (recordValue != "reduced" || !NonBlank(scope["independence"]?["statement"])))
            v.Add(new("S011", "independence", "the reduced-independence deviation must be recorded as 'reduced' with its statement"));
        if (!deviation && (!NonBlank(scope["independence"]?["reviewer"])
            || !amendments.Any(a => Str(a["version"]) == Str(scope["independence"]?["liftedBy"]))))
            v.Add(new("S011", "independence", "lifting the deviation needs a named reviewer and the amendment that lifted it"));
        foreach (var f in families)
        {
            var fid = Str(f["id"]) ?? "?";
            var r = f["readiness"];
            var status = Str(r?["status"]);
            if (!Vocabularies["readiness"].Contains(status))
                v.Add(new("S006", fid, $"readiness status '{status}' is not NOT_STARTED, IN_PROGRESS, READY or BLOCKED"));
            if (status == "READY")
            {
                var familyCases = cases.Where(c => Str(c["family"]) == fid).ToList();
                ValidateReady(v, fid, r!, f, familyCases, Exists, recordValue, deviation);
                var assessed = Version(Str(r!["scopeVersion"]));
                var stale = familyCases.Select(c => Str(c["id"])!).Concat(removed.Where(x => registry.TryGetValue(x!, out var e) && Str(e["family"]) == fid)!)
                    .Where(id => touched.TryGetValue(id!, out var ver) && assessed is not null && Version(ver)!.Value.CompareTo(assessed.Value) > 0);
                if (assessed is null || (Str(r["scopeVersion"]) != "1.0.0" && !amendments.Any(a => Str(a["version"]) == Str(r["scopeVersion"]))))
                    v.Add(new("S007", fid, "READY record: scopeVersion must name the registered scope version it was assessed under"));
                foreach (var id in stale)
                    v.Add(new("S007", fid, $"READY record is stale: case {id} changed in a later amendment"));
            }
            if (status == "BLOCKED" && (!NonBlank(r?["reason"]) || !Arr(r?["blockedBy"]).Any()))
                v.Add(new("S008", fid, "a BLOCKED record needs a reason and blockedBy"));
            if (status == "IN_PROGRESS" && !NonBlank(r?["pr"]) && !NonBlank(r?["branch"]))
                v.Add(new("S008", fid, "an IN_PROGRESS record needs a PR or branch"));
        }

        // S009-S010: lifecycle, gate and capacity.
        var lifecycle = Str(scope["status"]);
        var gate = Str(scope["gateStatus"]);
        var capacity = scope["capacity"];
        var ceilings = Arr(capacity?["ceilings"]).ToList();
        var conditions = Arr(scope["gateConditions"]).ToList();
        if (lifecycle is not ("PROPOSED" or "FROZEN") || gate is not ("MET" or "NOT-MET"))
            v.Add(new("S009", "status", "status must be PROPOSED or FROZEN and gateStatus MET or NOT-MET"));
        if (lifecycle == "PROPOSED" && (gate != "NOT-MET" || scope["acceptance"] is not null
            || Str(capacity?["status"]) != "PROPOSED" || ceilings.Any(c => Str(c["status"]) != "PROPOSED")))
            v.Add(new("S009", "status", "a PROPOSED packet is NOT-MET, has no acceptance record, and every ceiling is PROPOSED"));
        if (lifecycle == "FROZEN" && (!IsFullSha(Str(scope["acceptance"]?["mergeCommit"]))
            || Str(scope["acceptance"]?["mergedAtUtc"]) is not { } at || !at.EndsWith('Z') || Int(scope["acceptance"]?["pr"]) <= 0
            || Str(capacity?["status"]) != "ACCEPTED" || ceilings.Any(c => Str(c["status"]) != "ACCEPTED")))
            v.Add(new("S009", "status", "a FROZEN packet needs a full acceptance record and every ceiling ACCEPTED"));
        foreach (var id in RequiredGateConditions.Where(id => !conditions.Any(g => Str(g["id"]) == id)))
            v.Add(new("S009", id, "a registered gate condition was removed"));
        if (!conditions.Any(g => Str(g["id"]) == "blocker-1413" && Int(g["issue"]) == 1413))
            v.Add(new("S009", "blocker-1413", "the #1413 closure condition must name #1413"));
        if (gate == "MET")
        {
            if (lifecycle != "FROZEN" || !amendments.Any(a => Str(a["gateDecision"]) == "R0-MET"))
                v.Add(new("S009", "gateStatus", "R0 is MET only in a FROZEN packet, by an amendment recording gateDecision R0-MET"));
            foreach (var g in conditions.Where(g => Str(g["state"]) != "SATISFIED" || !Exists(Str(g["evidence"]))))
                v.Add(new("S009", Str(g["id"]) ?? "?", "MET needs every gate condition SATISFIED with a retained evidence file"));
            if (!conditions.Any(g => Str(g["id"]) == "blocker-1413" && Str(g["closedAtUtc"]) is { } cl && cl.EndsWith('Z')))
                v.Add(new("S009", "blocker-1413", "MET needs the #1413 closure time"));
            foreach (var role in new[] { "interaction", "doc-example" }.Where(role => !cases.Any(c => Str(c["role"]) == role)))
                v.Add(new("S009", role, $"MET needs registered {role} rows"));
        }
        var ceilingIds = new HashSet<string>();
        foreach (var c in ceilings)
            if (!ceilingIds.Add(Str(c["id"]) ?? "") || c["value"]?.GetValueKind() != JsonValueKind.Number
                || !NonBlank(c["unit"]) || Str(c["status"]) is not ("PROPOSED" or "ACCEPTED"))
                v.Add(new("S010", Str(c["id"]) ?? "?", "ceiling needs a unique id, numeric value, unit and PROPOSED/ACCEPTED status"));
        if (!ceilings.Any(c => Str(c["id"]) == "paid-spend" && c["value"]?.ToJsonString() == "0"))
            v.Add(new("S010", "paid-spend", "the paid-spend ceiling must be registered at 0"));

        // S013: the baseline evidence is sealed, and every recorded baseline is recomputed from it.
        var seal = scope["evidenceSeal"];
        if (Str(seal?["baselineResults"]) != Sha256(readRepoFile(PacketDir + "/baseline-results.json") ?? []))
            v.Add(new("S013", "baselineResults", "baseline-results.json differs from its sealed hash"));
        if (Str(seal?["generated"]) != SealFiles(generatedFiles, readRepoFile))
            v.Add(new("S013", "generated", "the retained converter outputs differ from their sealed hash"));
        if (Str(results["srcTree"]) != Str(scope["identities"]?["srcTree"]) || !IsFullSha(Str(results["srcTree"]))
            || !IsFullSha(Str(results["measuredCommit"])))
            v.Add(new("S013", "srcTree", "results must come from a full commit whose src/ tree is the registered compiler source tree"));
        var byCase = Arr(results["cases"]).ToDictionary(c => Str(c["case"])!, c => c);
        var byCalr = Arr(results["calorCases"]).ToDictionary(c => Str(c["case"])!, c => c);
        foreach (var c in cases.Concat(Arr(scope["discovered"])))
        {
            var id = Str(c["id"])!;
            var baseline = (c["baseline"] as JsonObject) ?? new JsonObject();
            var rc = byCase.GetValueOrDefault(id);
            var cc = byCalr.GetValueOrDefault(id);
            if ((rc ?? cc) is { } r && (Str(r["fixture"]) != Str(c["fixture"]) || Str(r["fixtureSha256"]) != Str(c["fixtureSha256"])))
                v.Add(new("S013", id, "results were produced from a different fixture"));
            if (rc is not null && Surfaces.Any(s => !baseline.ContainsKey(s)))
                v.Add(new("S013", id, "a C# fixture must record all four surfaces"));
            foreach (var (surface, value) in baseline)
            {
                var derived = rc is not null ? Derive(rc["surfaces"]?[surface], rc["original"])
                    : cc is not null && surface == "compile" ? DeriveCompile(cc["compile"]) : null;
                if (derived != Str(value))
                    v.Add(new("S013", $"{id}/{surface}", $"recorded baseline '{Str(value)}' differs from the results ('{derived}')"));
            }
            foreach (var o in Arr(c["observations"]))
            {
                var surface = byCase.GetValueOrDefault(Str(o["case"]) ?? "")?["surfaces"]?[Str(o["surface"]) ?? ""] as JsonObject;
                var path = Str(o["path"]) ?? "";
                if (surface is null || !surface.ContainsKey(path) || !JsonNode.DeepEquals(surface[path], o["equals"]))
                    v.Add(new("S013", id, $"observation {Str(o["case"])}/{Str(o["surface"])}/{path} does not match the results"));
            }
        }
        return v;
    }

    private static void ValidateReady(List<ContractViolation> v, string fid, JsonNode r, JsonNode family,
        List<JsonNode> familyCases, Func<string?, bool> exists, string? recordValue, bool deviation)
    {
        void Fail(string what) => v.Add(new("S007", fid, $"READY record: {what}"));
        if (!IsFullSha(Str(r["candidate"]?["commit"])))
            Fail("candidate.commit must be a full 40-hex commit");
        var recorded = Arr(r["cases"]).ToList();
        foreach (var c in familyCases)
        {
            var row = recorded.FirstOrDefault(x => Str(x["id"]) == Str(c["id"]));
            if (row is null || !Vocabularies["caseResult"].Contains(Str(row["result"])))
                Fail($"case {Str(c["id"])} has no recorded result");
            else if (Str(row["result"]) != "passed")
                Fail($"case {Str(c["id"])} is {Str(row["result"])}, not passed");
        }
        var tests = Arr(r["tests"]).ToList();
        if (tests.Count == 0 || tests.Any(t => !NonBlank(t["project"]) || !IsInt(t["total"]) || Int(t["total"]) <= 0
            || !IsInt(t["failed"]) || Int(t["failed"]) != 0))
            Fail("tests must name each project with a numeric positive total and a numeric zero failed");
        var examples = Arr(r["websiteExamples"]).ToList();
        if (examples.Count == 0 || examples.Any(e => !exists(Str(e))))
            Fail("websiteExamples must list checked website pages that exist in the repository");
        var reviews = Arr(r["reviews"]).ToList();
        if (reviews.Count == 0 || reviews.Any(x => !NonBlank(x["reviewer"]) || !exists(Str(x["record"]))))
            Fail("reviews must name each reviewer and a retained record that exists in the repository");
        if (!NonBlank(r["approver"]?["login"]) || Str(r["approver"]?["approvalUrl"]) is not { } url
            || !url.StartsWith("https://github.com/", StringComparison.Ordinal))
            Fail("approver needs a login and a GitHub approval URL");
        if (deviation && Str(r["independence"]) != recordValue)
            Fail($"independence must be '{recordValue}' while the deviation stands");
        foreach (var b in Arr(family["blockers"]))
            if (!Arr(r["blockersResolved"]).Any(x => Int(x["issue"]) == Int(b) && NonBlank(x["reference"])))
                Fail($"blocker #{Int(b)} has no recorded resolution");
    }

    /// <summary>One surface's outcome. Behavioral equality is recomputed from the original run.</summary>
    public static string? Derive(JsonNode? s, JsonNode? original)
    {
        if (s is null) return null;
        if (s["execution"] is { } e)
        {
            var interop = Int(s["interopPreservationCount"]) > 0 || Int(s["interopPreservations"]) > 0
                || Int(s["csharpInteropBlocks"]) > 0 || Int(s["inlineInteropExpressions"]) > 0;
            var match = e["compiled"]?.GetValueKind() == JsonValueKind.True && original?["compiled"]?.GetValueKind() == JsonValueKind.True
                && JsonNode.DeepEquals(e["result"], original["result"]) && JsonNode.DeepEquals(e["exception"], original["exception"]);
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

    /// <summary>SHA-256 over the sorted "path sha" lines of the given files.</summary>
    public static string SealFiles(IEnumerable<string> files, Func<string, byte[]?> read) => Sha256(Encoding.UTF8.GetBytes(
        string.Join("\n", files.OrderBy(f => f, StringComparer.Ordinal).Select(f => f + " " + Sha256(read(f) ?? [])))));

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"))));

    private static (int, int, int)? Version(string? s) => s is not null && SemVer.Match(s) is { Success: true } m
        ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    private static string Codes(JsonNode? n) => string.Join(",", Arr(n).Select(Str));
    private static bool NonBlank(JsonNode? n) => !string.IsNullOrWhiteSpace(Str(n));
    private static bool IsInt(JsonNode? n) => n is JsonValue jv && jv.GetValueKind() == JsonValueKind.Number && jv.TryGetValue<int>(out _);
    private static bool IsFullSha(string? s) => s is not null && FullSha.IsMatch(s);
    private static IEnumerable<JsonNode> Arr(JsonNode? n) => n is JsonArray a ? a.OfType<JsonNode>() : [];
    private static string? Str(JsonNode? n) => n is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
    private static int Int(JsonNode? n) => n is JsonValue jv && jv.TryGetValue<int>(out var i) ? i : 0;
}
