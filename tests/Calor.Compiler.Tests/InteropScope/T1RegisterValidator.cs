using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.EvidenceContract;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #1428 (0.25 T1): validates the per-family readiness register and shared-control registry
/// (<c>docs/plans/evidence/v0.25-t1-1428/register.json</c>) against the R0 scope packet and the
/// committed evidence, failing closed (codes T001-T009, documented in the register's README).
/// It compares manifests mechanically: a removed, added or reclassified case, a requirement digest
/// the scope did not register, or an evidence or results file whose hash the register did not
/// approve, fails.
/// </summary>
internal static class T1RegisterValidator
{
    public const string RegisterPath = SharedInteropControlsTests.Dir + "/register.json";
    public const string ScopePath = InteropScopeValidator.PacketDir + "/scope.json";

    /// <summary>The #1428 mandatory shared controls; each needs at least one registered control.</summary>
    public static readonly string[] MandatoryCovers =
    [
        "refout-nullable-locals", "local-function-interpolation", "local-function-calls", "array-arguments", "array-returns",
        "array-conditional-branches", "iterator-accessor-fallback", "rescue-combinations", "interpolation-string",
        "interpolation-formattablestring", "interpolation-iformattable", "interpolation-competing-overloads",
        "interpolation-custom-handler", "interpolation-format-culture",
    ];

    /// <summary>Surfaces #1428 names that have no conversion entry point; each needs a recorded reason.</summary>
    public static readonly string[] UnmeasuredSurfaces = ["sdk", "msbuild", "lsp"];

    /// <summary>Where F2 recorded the F-2/corpus counts; it must also be one of F2's approved evidence files.</summary>
    public const string F2CorpusSource = "docs/plans/evidence/v0.25-f2-906/rows.json";

    /// <summary>Where the 0.25 re-proof keeps F2's per-case candidate records (D-READY-EVIDENCE-DIRS).</summary>
    public const string F2ReadyDir = "docs/plans/evidence/v0.25-f2-906/ready/";

    private static readonly string[] CorpusKeys = ["incompleteDiagnostics", "inRepoExpressionsBound", "conversionVisits", "opaqueBoundaries", "opaqueExpressions"];

    public static IReadOnlyList<ContractViolation> Validate(JsonNode register, JsonNode scope, Func<string, byte[]?> read)
    {
        var v = new List<ContractViolation>();
        void Add(string code, string subject, string message) => v.Add(new(code, subject, message));
        JsonNode? Json(string? path)
        {
            try { return path is { Length: > 0 } && !path.Split('/').Contains("..") && read(path) is { } b ? JsonNode.Parse(b) : null; }
            catch (JsonException) { return null; }
        }
        string? Hash(string? path) => path is { Length: > 0 } && !path.Split('/').Contains("..") && read(path) is { } b ? InteropScopeValidator.Sha256(b) : null;

        // T001: the register consumes the current scope version.
        if (Str(register["consumes"]?["scope"]) != ScopePath || Str(register["consumes"]?["scopeVersion"]) != Str(scope["scopeVersion"]))
            Add("T001", "consumes", $"the register must consume {ScopePath} at its current scopeVersion ({Str(scope["scopeVersion"])})");

        // T002-T004: families mirror the scope (all six, readiness, blockers), with their registered cases and approved evidence.
        var families = Arr(register["families"]).ToList();
        var scopeFamilies = Arr(scope["families"]).ToList();
        if (!families.Select(f => Str(f["id"])).SequenceEqual(scopeFamilies.Select(f => Str(f["id"]))))
            Add("T002", "families", "the register must list exactly the scope's families, in scope order");
        var scopeCases = Arr(scope["cases"]).ToList();
        foreach (var f in families)
        {
            var fid = Str(f["id"]) ?? "?";
            var sf = scopeFamilies.FirstOrDefault(x => Str(x["id"]) == fid);
            if (sf is null)
                continue;
            if (Int(f["issue"]) != Int(sf["issue"]) || Str(f["readiness"]) != Str(sf["readiness"]?["status"])
                || !Arr(f["blockers"]).Select(Int).SequenceEqual(Arr(sf["blockers"]).Select(Int)))
                Add("T002", fid, "issue, readiness status and blockers must equal the scope's (READY is recorded only in scope.json)");
            if (!Arr(f["readyNeeds"]).Any() && Str(f["readiness"]) != "READY")
                Add("T002", fid, "a family that is not READY must say what READY still needs");
            var expected = scopeCases.Where(c => Str(c["family"]) == fid).ToList();
            var listed = Arr(f["cases"]).ToList();
            foreach (var c in expected)
            {
                var id = Str(c["id"]);
                var row = listed.FirstOrDefault(x => Str(x["id"]) == id);
                if (row is null)
                    Add("T003", id ?? "?", $"registered case is missing from family {fid}");
                else if (Str(row["role"]) != Str(c["role"]) || Str(row["expected"]) != Str(c["expected"]))
                    Add("T003", id!, "case reclassified: role or expected differs from the scope");
                else if (Str(row["requirementSha256"]) != InteropScopeValidator.RequirementDigest(c))
                    Add("T003", id!, "requirement digest is not the one the scope registers");
            }
            foreach (var extra in listed.Where(x => !expected.Any(c => Str(c["id"]) == Str(x["id"]))))
                Add("T003", Str(extra["id"]) ?? "?", $"case is not registered to family {fid} by the scope");
            if (listed.Select(x => Str(x["id"])).Distinct().Count() != listed.Count)
                Add("T003", fid, "duplicate case id");
            var evidence = Arr(f["evidence"]).ToList();
            if (evidence.Count == 0)
                Add("T004", fid, "a family needs at least one evidence file");
            var own = $"docs/plans/evidence/v0.25-{fid.ToLowerInvariant()}-{Int(f["issue"])}/";
            foreach (var e in evidence)
                if (Str(e["path"])?.StartsWith(own, StringComparison.Ordinal) != true || Hash(Str(e["path"])) is not { } h || h != Str(e["sha256"]))
                    Add("T004", Str(e["path"]) ?? fid, $"evidence file must be the family's own (under {own}) at its approved hash");
        }

        // T005: the F-2/corpus before and after counts are preserved exactly as F2 recorded them. Amendment 1.0.3
        // (D-F2-INTERP-09-RECANDIDATE) re-registers 'after' to the 0.25 candidate's measurement: then 'after' equals that
        // retained measurement (F2's own ready/ record, at its approved hash), 'before' and the recorded previous 'after'
        // equal F2's, and before plus every PR's delta gives after.
        var corpus = register["corpusCounts"];
        var source = Json(Str(corpus?["source"]));
        var f2Evidence = families.FirstOrDefault(f => Str(f["id"]) == "F2") is { } f2
            ? Arr(f2["evidence"]).Select(e => Str(e["path"])).ToHashSet() : [];
        if (Str(corpus?["case"]) != "F2-INTERP-09" || Str(corpus?["source"]) != F2CorpusSource || !f2Evidence.Contains(F2CorpusSource)
            || source?["corpusCounts"] is not JsonObject recorded)
            Add("T005", "corpusCounts", $"the F2-INTERP-09 corpus counts must cite F2's approved {F2CorpusSource}");
        else
        {
            var re = corpus!["reRegistration"];
            JsonNode? candidateCounts = null;
            var perPr = Arr(re?["perPr"]).ToList();
            if (re is not null)
            {
                var path = Str(re["measurement"]?["path"]);
                var file = path?.StartsWith(F2ReadyDir, StringComparison.Ordinal) == true ? Json(path) : null;
                candidateCounts = Arr(file?["measurements"]).FirstOrDefault(m => Str(m["kind"]) == "corpus-counts")?["candidate"];
                if (Str(re["amendment"]) is not { } version || !Arr(register["amendments"]).Any(a => Str(a["version"]) == version)
                    || Hash(path) is not { } mh || mh != Str(re["measurement"]?["sha256"]) || Str(file?["caseId"]) != "F2-INTERP-09"
                    || !InteropScopeValidator.IsFullSha(Str(re["measuredOn"])) || Str(file?["candidate"]) != Str(re["measuredOn"]) || candidateCounts is null
                    || perPr.Count == 0 || perPr.Any(p => Int(p["pr"]) <= 0 || !InteropScopeValidator.IsFullSha(Str(p["mergeCommit"]))))
                    Add("T005", "reRegistration", $"a re-registration needs its register amendment, F2's measurement under {F2ReadyDir} at its hash, the candidate and per-PR deltas");
            }
            foreach (var key in CorpusKeys)
            {
                var mine = corpus!["counts"]?[key];
                var after = re is null ? recorded[key]?["after"] : candidateCounts?[key];
                // Every count is a JSON integer before it is compared or added (a string or boolean never matches).
                if (!IsInteger(mine?["before"]) || !IsInteger(mine?["after"]) || !IsInteger(after)
                    || !JsonNode.DeepEquals(mine!["before"], recorded[key]?["before"]) || !JsonNode.DeepEquals(mine["after"], after)
                    || (re is not null && (!JsonNode.DeepEquals(re["previousAfter"]?[key], recorded[key]?["after"])
                        || perPr.Any(p => !IsInteger(p["delta"]?[key]))
                        || Int(mine["before"]) + perPr.Sum(p => Int(p["delta"]?[key])) != Int(mine["after"]))))
                    Add("T005", key, "before/after count differs from F2's recorded count (or, re-registered, from the candidate's measurement and the per-PR deltas) or is missing");
            }
        }

        // T006: controls cover every mandatory shape and every registered interaction row, with approved fixtures.
        var controls = Arr(register["controls"]).ToList();
        var ids = new HashSet<string>();
        var vocab = Arr(scope["vocabularies"]?["expected"]).Select(Str).ToHashSet();
        foreach (var c in controls)
        {
            var id = Str(c["id"]) ?? "";
            if (!ids.Add(id))
                Add("T006", id, "control id empty or duplicated");
            if (!vocab.Contains(Str(c["expected"])) || !Arr(c["covers"]).Any()
                || Arr(c["families"]).Select(Str).Any(x => !InteropScopeValidator.RequiredFamilies.ContainsKey(x ?? "")))
                Add("T006", id, "a control needs covers, an expected outcome from the scope vocabulary and families among the six");
            if (Hash(Str(c["fixture"])) is not { } fh || fh != Str(c["fixtureSha256"]))
                Add("T006", id, "control fixture missing or its hash is not the approved one");
        }
        var covered = controls.SelectMany(c => Arr(c["covers"]).Select(Str)).ToHashSet();
        foreach (var shape in MandatoryCovers.Where(s => !covered.Contains(s)))
            Add("T006", shape, "mandatory shared control has no registered control");
        foreach (var row in scopeCases.Where(c => Str(c["role"]) == "interaction"))
        {
            var id = Str(row["id"]);
            var ctl = controls.FirstOrDefault(c => Arr(c["covers"]).Select(Str).Contains(id));
            if (ctl is null || Str(ctl["expected"]) != Str(row["expected"])
                || !Arr(ctl["families"]).Select(Str).OrderBy(x => x).SequenceEqual(Arr(row["families"]).Select(Str).OrderBy(x => x)))
                Add("T006", id ?? "?", "interaction row needs a control with the scope's expected outcome and families");
        }

        // T007: the committed results are approved by hash and every cell is an allowed, behavior-preserving outcome.
        var resultsPath = Str(register["results"]?["path"]);
        if (resultsPath != SharedInteropControlsTests.ResultsPath || Hash(resultsPath) is not { } rh || rh != Str(register["results"]?["sha256"]))
            Add("T007", "results", "controls-results.json missing or its hash is not the approved one");
        var results = Json(resultsPath)?["controls"];
        var measured = Arr(register["surfaces"]).Where(s => s["measured"]?.GetValueKind() == JsonValueKind.True).Select(s => Str(s["id"])).ToList();
        foreach (var c in controls)
        {
            var id = Str(c["id"]) ?? "?";
            var entry = results?[id];
            var allowed = (Str(c["expected"]) ?? "").Split("-or-");
            if (entry is null || !NonBlank(entry["originalTrace"]))
            {
                Add("T007", id, "control has no committed observation");
                continue;
            }
            foreach (var s in measured)
                if (Str(entry["surfaces"]?[s!]?["outcome"]) is not { } outcome || !allowed.Contains(outcome))
                    Add("T007", $"{id}/{s}", "cell missing, or its outcome is not one the control allows");
        }

        // T008: the surface list is the measured set plus the named entry points without conversion.
        if (!measured.SequenceEqual(SharedInteropControlsTests.Surfaces))
            Add("T008", "surfaces", "measured surfaces must be exactly the surfaces the controls run on");
        foreach (var s in Arr(register["surfaces"]))
        {
            var sid = Str(s["id"]) ?? "?";
            if (s["measured"]?.GetValueKind() == JsonValueKind.True
                && (s["defaults"]?["rescueUnusableMembers"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)
                    || s["defaults"]?["passthroughOnError"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)))
                Add("T008", sid, "a measured surface records its actual rescue and passthrough defaults");
            if (s["measured"]?.GetValueKind() != JsonValueKind.True && !NonBlank(s["reason"]))
                Add("T008", sid, "an unmeasured surface needs a reason");
        }
        foreach (var u in UnmeasuredSurfaces.Where(u => !Arr(register["surfaces"]).Any(s => Str(s["id"]) == u)))
            Add("T008", u, "the surface #1428 names must be recorded, measured or with a reason");

        // T009: register versioning.
        var amendments = Arr(register["amendments"]).ToList();
        var previous = new Version(1, 0, 0);
        foreach (var a in amendments)
        {
            var ok = System.Version.TryParse(Str(a["version"]), out var ver) && ver > previous;
            if (!ok || Int(a["pr"]) <= 0 || !NonBlank(a["justification"]) || Str(a["dateUtc"])?.EndsWith('Z') != true)
                Add("T009", Str(a["version"]) ?? "?", "a register amendment needs a strictly increasing version, UTC date, PR and justification");
            previous = ok ? ver! : previous;
        }
        if (Str(register["registerVersion"]) != (amendments.Count == 0 ? "1.0.0" : Str(amendments[^1]["version"])))
            Add("T009", "registerVersion", "registerVersion must equal the latest amendment version (1.0.0 with none)");
        // Every approved path and hash is sealed; changing one needs a register amendment recording the new seal
        // (with no amendment, the 1.0.0 seal, which T1RegisterTests also pins).
        var seal = ApprovalSeal(register);
        var approved = amendments.Count == 0 ? Str(register["approvalSeal"]) : Str(amendments[^1]["approvalSeal"]);
        if (seal != approved)
            Add("T009", "approvalSeal", "an approved evidence, fixture or results hash changed without a register amendment recording the new approval seal");
        if (Str(register["independence"]) != "reduced")
            Add("T009", "independence", "the register records the reduced-independence deviation");
        return v;
    }

    /// <summary>SHA-256 over every approved (path, hash) pair: family evidence, control fixtures and the results file.</summary>
    public static string ApprovalSeal(JsonNode register)
    {
        var lines = Arr(register["families"]).SelectMany(f => Arr(f["evidence"]).Select(e => $"evidence {Str(f["id"])} {Str(e["path"])} {Str(e["sha256"])}"))
            .Concat(Arr(register["controls"]).Select(c => $"fixture {Str(c["id"])} {Str(c["fixture"])} {Str(c["fixtureSha256"])}"))
            .Append($"results {Str(register["results"]?["path"])} {Str(register["results"]?["sha256"])}")
            .Append($"corpus {Str(register["corpusCounts"]?["source"])} {register["corpusCounts"]?["counts"]?.ToJsonString()}"
                + (register["corpusCounts"]?["reRegistration"] is { } re ? $" reRegistration {re.ToJsonString()}" : ""))
            .OrderBy(l => l, StringComparer.Ordinal);
        return InteropScopeValidator.Sha256(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    private static IEnumerable<JsonNode> Arr(JsonNode? n) => n is JsonArray a ? a.OfType<JsonNode>() : [];
    private static string? Str(JsonNode? n) => n is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
    private static int Int(JsonNode? n) => n is JsonValue jv && jv.TryGetValue<int>(out var i) ? i : 0;
    private static bool IsInteger(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out _);

    private static bool NonBlank(JsonNode? n) => !string.IsNullOrWhiteSpace(Str(n));
}
