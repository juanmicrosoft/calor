using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.SoundnessRegistration;
using static Calor.Soundness.Sweep.Program;

namespace Calor.Soundness.Sweep;

internal sealed record RowInfo(string Id, string Classification, bool ReleaseCritical, string Channel, string[]? RefusalCodes, int Order);

// One case execution on one baseline: every channel of the case's row, both emissions, the O1 verdict, O2 replay, guard observations, and the mechanical
// classification of registration caseResults.classificationTable. Returns one attempt record; retries are scheduled by the caller.
internal static partial class CaseExecutor
{
    public static readonly string[] ClaimTokens = ["Proven", "ProvenVacuous", "Discharged", "Assumed", "Unsupported", "TimeoutOrUnavailable", "Failed", "Boundary"];

    private static string? Str(JsonNode? n) => n?.GetValue<string>();

    public static JsonObject Execute(BaselineHost host, SweepCaseGenerator.Case c, RowInfo row, JsonNode template, string scratch)
    {
        var claimSite = S(template["claimSite"]);
        var obligationKind = Str(template["obligationKind"]);
        var o1 = OracleProgram.WithCulture(() => IndependentOracle.Evaluate(c.OracleSource)); // O1: the registered oracle, unchanged
        var channels = new JsonObject();
        var record = new JsonObject
        {
            ["baseline"] = host.Id, ["caseId"] = c.Id, ["rowId"] = c.RowId, ["templateId"] = c.TemplateId,
            ["caseSha256"] = new JsonObject { ["calor"] = c.CalorSha256, ["oracle"] = c.OracleSha256, ["prime"] = c.CalorPrimeSource == null ? null : SweepCaseGenerator.Sha256(c.CalorPrimeSource) },
            ["claimSite"] = claimSite, ["obligationKind"] = obligationKind, ["o1"] = JsonSerializer.SerializeToNode(o1), ["channels"] = channels,
        };
        JsonObject elided, forced;
        try
        {
            if (c.RowId.StartsWith("CACHE-", StringComparison.Ordinal))
                (elided, forced) = RunCache(host, c, channels, scratch);
            else
            {
                var refinements = claimSite == "obligation";
                elided = host.Compile(c.CalorSource, refinements, elide: true, cacheDirectory: null);
                forced = host.Compile(c.CalorSource, refinements, elide: false, cacheDirectory: null);
                channels[refinements ? "CH-OBLIGATION" : "CH-CONTRACT"] = new JsonObject { ["elided"] = elided, ["forced"] = forced };
            }
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            record["status"] = "crashed";
            record["crash"] = ex.InnerException?.ToString() ?? ex.ToString();
            return record;
        }
        record["o2"] = Replay(c, template, o1, S(forced["emitted"]), Path.Combine(host.Directory, "Calor.Runtime.dll"), claimSite, obligationKind);
        Adjudicate(record, c, row, template, o1, elided, forced);
        return record;
    }

    // Claim mapping, guard observation, and classification from observations only (no compiler call).
    public static void Adjudicate(JsonObject record, SweepCaseGenerator.Case c, RowInfo row, JsonNode template, IndependentOracle.Verdict o1, JsonObject elided, JsonObject forced)
    {
        var claimSite = S(template["claimSite"]);
        var obligationKind = Str(template["obligationKind"]);
        var claim = Claim(elided, claimSite, obligationKind, c.CalorSource);
        var forcedClaim = Claim(forced, claimSite, obligationKind, c.CalorSource);
        record["claim"] = claim;
        record["forcedClaim"] = forcedClaim;
        record["guards"] = Guards(elided, forced, claimSite, obligationKind, Str(claim["token"]));
        Classify(record, c, row, template, o1, claim, forcedClaim);
    }

    // The (elided, forced) compile records of a retained attempt.
    public static (JsonObject Elided, JsonObject Forced) Emissions(JsonObject record)
    {
        var channels = record["channels"]!.AsObject();
        if (channels["CH-CACHE"] is JsonObject cache) return ((JsonObject)cache["warm"]!, (JsonObject)cache["warmForced"]!);
        var ch = (JsonObject)channels.First().Value!;
        return ((JsonObject)ch["elided"]!, (JsonObject)ch["forced"]!);
    }

    private static (JsonObject Elided, JsonObject Forced) RunCache(BaselineHost host, SweepCaseGenerator.Case c, JsonObject channels, string scratch)
    {
        string Fresh() { var d = Path.Combine(scratch, "cache-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
        var cache = new JsonObject();
        channels["CH-CACHE"] = cache;
        var dir = Fresh();
        if (c.RowId == "CACHE-SEMANTICS-VERSION")
        {
            // Variant A: the tamper-reachability control (outcome -> proven only; Proven required). Variant B (adjudicated): also a stale version.
            cache["primeA"] = host.Compile(c.CalorPrimeSource!, false, true, dir);
            cache["tamperA"] = Tamper(dir, staleVersion: false);
            cache["warmA"] = host.Compile(c.CalorSource, false, true, dir);
            dir = Fresh();
            cache["primeB"] = host.Compile(c.CalorPrimeSource!, false, true, dir);
            cache["tamperB"] = Tamper(dir, staleVersion: true);
        }
        else
            cache["prime"] = host.Compile(c.CalorPrimeSource!, false, true, dir);
        cache["warm"] = host.Compile(c.CalorSource, false, true, dir);
        cache["warmForced"] = host.Compile(c.CalorSource, false, false, dir);
        cache["cold"] = host.Compile(c.CalorSource, false, true, Fresh());
        return ((JsonObject)cache["warm"]!, (JsonObject)cache["warmForced"]!);
    }

    // Rewrites every primed cache entry's outcome to proven (and, for variant B, its translator semantics version).
    private static JsonArray Tamper(string projectDir, bool staleVersion)
    {
        var changed = new JsonArray();
        var root = Path.Combine(projectDir, ".calor", "verification-cache");
        if (!Directory.Exists(root)) return changed;
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var entry = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var before = entry.ToJsonString();
            // The baseline serializes camelCase with a numeric ContractVerificationStatus (Proven = 0).
            entry["status"] = entry["status"] is JsonValue v && v.TryGetValue<int>(out _) ? 0 : "Proven";
            entry["proofStatus"] = "proven";
            entry["proofVacuous"] = false;
            entry["counterexampleBindings"] = entry["counterexampleDescription"] = entry["proofReason"] = null;
            if (staleVersion) { var sv = Str(entry["semanticsVersion"]) ?? ""; entry["semanticsVersion"] = sv[..(sv.LastIndexOf('|') + 1)] + "r1-stale-version"; }
            File.WriteAllText(file, entry.ToJsonString());
            changed.Add(new JsonObject { ["file"] = Path.GetRelativePath(projectDir, file), ["before"] = before, ["after"] = entry.ToJsonString() });
        }
        return changed;
    }

    // Maps the case's one claim to exactly one contract section 4 token (from ProofOutcome, never a legacy enum).
    internal static JsonObject Claim(JsonObject compile, string claimSite, string? obligationKind, string calorSource)
    {
        var diags = compile["diagnostics"]!.AsArray().Select(d => d!.AsObject()).ToList();
        var result = new JsonObject { ["compileErrors"] = new JsonArray(diags.Where(d => S(d["severity"]) == "Error").Select(d => S(d["code"])).Distinct().Order(StringComparer.Ordinal).Select(e => (JsonNode)e).ToArray()) };
        JsonObject NotFound(string? error) { result["claimFound"] = false; if (error != null) result["claimError"] = error; return result; }
        switch (claimSite)
        {
            case "postcondition" or "precondition":
            {
                var key = claimSite == "postcondition" ? "postconditions" : "preconditions";
                if (compile["contracts"]?.AsArray() is not { } functions) return NotFound(null);
                var all = functions.SelectMany(f => f![key]!.AsArray().Select(r => (Fn: S(f["functionName"]), R: r!))).ToList();
                if (all.Count != 1) all = all.Where(x => x.Fn == "Probe").ToList();
                if (all.Count != 1) return NotFound($"{all.Count} {key} results");
                var outcome = all[0].R["outcome"]!;
                var (status, vacuous) = (S(outcome["status"]), outcome["isVacuous"]!.GetValue<bool>());
                (result["claimFound"], result["rawStatus"], result["legacyStatus"], result["isVacuous"]) = (true, status, Str(all[0].R["legacyStatus"]), vacuous);
                (result["assumptions"], result["counterexample"]) = (outcome["assumptions"]!.DeepClone(), outcome["counterexample"]?.DeepClone());
                result["translatorSemanticsVersion"] = all[0].R["translatorSemanticsVersion"]?.DeepClone();
                result["token"] = status switch
                {
                    "Proven" => vacuous ? "ProvenVacuous" : "Proven", "Refuted" => "Failed", "Assumed" => "Assumed", "Unsupported" => "Unsupported",
                    "Timeout" or "Unknown" or "Unavailable" => "TimeoutOrUnavailable", _ => "UNMAPPED:" + status,
                };
                return result;
            }
            case "obligation":
            {
                if (compile["obligations"]?.AsArray() is not { } obligations) return NotFound(null);
                var matching = obligations.Where(o => S(o!["kind"]) == obligationKind).ToList();
                if (matching.Count != 1) return NotFound($"{matching.Count} obligations of kind {obligationKind}");
                var o = matching[0]!;
                var (status, outcome) = (S(o["status"]), o["outcome"]);
                (result["claimFound"], result["rawStatus"], result["outcomeStatus"], result["assumptions"]) = (true, status, outcome?["status"]?.DeepClone(), outcome?["assumptions"]?.DeepClone());
                result["counterexample"] = outcome?["counterexample"]?.DeepClone() ?? ParseCounterexample(Str(o["counterexampleDescription"]));
                result["token"] = (status, Str(outcome?["status"])) switch
                {
                    ("Discharged", null or "Proven") => "Discharged", ("Failed", null or "Refuted") => "Failed", ("Boundary", _) => "Boundary",
                    ("Pending", _) => "TimeoutOrUnavailable", ("Unsupported", null or "Unsupported") => "Unsupported", ("Timeout", "Assumed") => "Assumed",
                    ("Timeout", "Timeout" or "Unknown" or "Unavailable") => "TimeoutOrUnavailable", _ => "UNMAPPED:" + status + "/" + outcome?["status"],
                };
                return result;
            }
            case "implication":
            {
                // CH-IMPLICATION: only the template's own direction is adjudicated: its Calor0815 text (Proven), its
                // Calor0816 (TimeoutOrUnavailable), or its LSP-violation error (Failed); none means the heuristic ran
                // (Unsupported). Any other Calor0815 (e.g. the vacuous other direction) is archived, not adjudicated.
                var post = calorSource.Contains("§S (", StringComparison.Ordinal);
                var dir = post ? "Postcondition" : "Precondition";
                bool Has(JsonObject d, string text) => S(d["message"]).Contains(text, StringComparison.Ordinal);
                var mine = diags.Where(d => Has(d, "'Impl.Run'")).ToList();
                result["direction"] = dir;
                result["implicationDiagnostics"] = new JsonArray(mine.Select(d => d.DeepClone()).ToArray());
                if (!mine.Any() && !diags.All(d => S(d["severity"]) != "Error" || Has(d, "LSP violation"))) return NotFound("rejected before the inheritance checker");
                var lsp = mine.FirstOrDefault(d => S(d["severity"]) == "Error" && (Has(d, $"LSP violation: {dir} in") || Has(d, $"LSP violation: Could not prove that {dir.ToLowerInvariant()} in")));
                result["claimFound"] = true;
                result["token"] = mine.Any(d => S(d["code"]) == "Calor0815" && Has(d, post ? "Postcondition strengthening proven" : "Precondition weakening proven")) ? "Proven"
                    : mine.Any(d => S(d["code"]) == "Calor0816" && Has(d, post ? "postcondition strengthening is valid" : "precondition weakening is valid")) ? "TimeoutOrUnavailable"
                    : lsp != null ? "Failed" : "Unsupported";
                result["rawStatus"] = Str(result["token"]);
                result["isVacuous"] = false;
                result["counterexample"] = lsp == null ? null : ParseCounterexample(S(lsp["message"]));
                return result;
            }
            default: // guard-emission
                result["claimFound"] = true;
                result["token"] = null;
                return result;
        }
    }

    [GeneratedRegex(@"Counterexample:\s*(?<b>.*)$")]
    private static partial Regex CounterexampleText();

    private static JsonArray? ParseCounterexample(string? text) => text != null && CounterexampleText().Match(text) is { Success: true } m
        ? new JsonArray(m.Groups["b"].Value.TrimEnd('.').Split(", ").Where(p => p.IndexOf('=') > 0)
            .Select(p => (JsonNode)new JsonObject { ["name"] = p[..p.IndexOf('=')].Trim(), ["value"] = p[(p.IndexOf('=') + 1)..].Trim() }).ToArray())
        : null;

    private static int Count(string text, string marker) => text.Split(marker).Length - 1;

    private static JsonObject Guards(JsonObject elided, JsonObject forced, string claimSite, string? obligationKind, string? token)
    {
        var marker = claimSite switch
        {
            "postcondition" or "guard-emission" => "Calor.Runtime.ContractKind.Ensures",
            "obligation" => obligationKind switch
            {
                "ProofObligation" => "Proof obligation [", "IndexBounds" => "\"Indexed-type bound violated\"",
                "RefinementEntry" => "\"Violation of refinement type|\"Violation of inline refinement",
                "RefinementReturn" => "\"Return value violates refinement type", "Subtype" => "\"Value violates refinement type", _ => null,
            },
            _ => null,
        };
        var g = new JsonObject { ["marker"] = marker };
        if (marker == null) { g["applicable"] = false; return g; }
        var (e, f) = (S(elided["emitted"]), S(forced["emitted"]));
        g["applicable"] = true;
        if (e.Length == 0 || f.Length == 0) { g["observed"] = false; g["reason"] = "no-emission (compile rejected)"; return g; }
        var ec = marker.Split('|').Sum(m => Count(e, m));
        var fc = marker.Split('|').Sum(m => Count(f, m));
        (g["observed"], g["elidedCount"], g["forcedCount"], g["guardInForcedEmission"], g["guardInElidedEmission"]) = (true, ec, fc, fc > 0, ec >= fc && fc > 0);
        g["elidedWithoutProof"] = fc == 0 || (ec < fc && token is not ("Proven" or "Discharged"));
        return g;
    }

    // R1-O2 from a forced emission (live or retained) and the baseline's own Calor.Runtime.dll.
    internal static JsonObject Replay(SweepCaseGenerator.Case c, JsonNode template, IndependentOracle.Verdict o1, string emitted, string runtimeDll, string claimSite, string? obligationKind)
    {
        if (claimSite is "implication" or "precondition") return new JsonObject { ["status"] = "not-applicable" };
        if (o1.Kind == "oracle-invalid") return new JsonObject { ["status"] = "not-run-oracle-invalid" };
        if (emitted.Length == 0) return new JsonObject { ["status"] = "not-run-no-emission" };
        using var program = new OracleProgram(c.OracleSource);
        var wanted = (o1.Kind == "violated" && o1.Witness != null ? [o1.Witness.Split(" -> result=")[0]] : new List<string>())
            .Concat(o1.ReachedSample ?? []).Distinct().ToList();
        // Two independent recoveries: the O1 point verdict runs the oracle body, which may mutate (aliased)
        // inputs, so O2 receives its own untouched entry values.
        var verdicts = program.Recover(wanted).ToDictionary(kv => kv.Key, kv => program.PointVerdict(kv.Value));
        var forReplay = program.Recover(wanted);
        var record = EmittedReplay.Run(emitted, runtimeDll, program.Parameters, Str(template["oracle"]!["replay"]),
            wanted.Where(forReplay.ContainsKey).Select(r => (r, forReplay[r], verdicts[r])).ToList(), claimSite, obligationKind);
        record["unrecovered"] = new JsonArray(wanted.Where(w => !forReplay.ContainsKey(w)).Select(w => (JsonNode)w).ToArray());
        return record;
    }

    // registration caseResults.classificationTable, applied mechanically (one primary class plus added finding records).
    private static void Classify(JsonObject record, SweepCaseGenerator.Case c, RowInfo row, JsonNode template, IndependentOracle.Verdict o1, JsonObject claim, JsonObject forcedClaim)
    {
        var (token, forcedToken) = (Str(claim["token"]), Str(forcedClaim["token"]));
        var claimSite = S(template["claimSite"]);
        var added = new JsonArray();
        string? reason = null;
        static bool Proof(string? t) => t is "Proven" or "Discharged";
        string primary;
        if (o1.Kind == "oracle-invalid") (primary, reason) = ("harness-invalid", "oracle-invalid: " + o1.Error);
        else if (token?.StartsWith("UNMAPPED", StringComparison.Ordinal) == true) (primary, reason) = ("harness-invalid", "unmappable status " + token);
        else if (claim["claimFound"]?.GetValue<bool>() != true && !Proof(forcedToken)) (primary, reason) = Rejection(row, claim);
        else if (claimSite == "guard-emission") (primary, reason) = ("no-claim", "guard-emission claim: no proof token; judged on the inherited guard only");
        else if (token != forcedToken)
            (primary, reason) = o1.Kind == (c.Claim == "exists" ? "no-witness-exhaustive" : "violated") && (Proof(token) || Proof(forcedToken))
                ? ("false-unconditional-proof", $"elided and forced compiles disagree ({token} vs {forcedToken}); a proof with an O1 violation is a finding regardless")
                : ("flaky", $"elided and forced compiles disagree ({token} vs {forcedToken})");
        else if (token == "TimeoutOrUnavailable") (primary, reason) = ("timed-out", "TimeoutOrUnavailable");
        else if (c.Claim == "exists")
            primary = token switch
            {
                "Proven" => o1.Kind switch { "witness-found" => "validated-proof", "no-witness-exhaustive" => "false-unconditional-proof", _ => "unconfirmed-claim" },
                "Failed" => o1.Kind == "witness-found" ? "spurious-refutation" : "no-claim",
                _ => Demotion(row, token!),
            };
        else if (Proof(token))
            primary = o1.Kind == "violated" ? "false-unconditional-proof"
                : row.Classification != "modeled" ? "required-demotion-absent"
                : o1.Reached > 0 ? "validated-proof"
                : o1.Kind == "vacuous-in-domain" && (c.Exhaustive || (template["oracle"]!["vacuousByConstruction"]?.GetValue<bool>() ?? false)) ? "vacuity-mislabel" : "unconfirmed-claim";
        else if (token == "ProvenVacuous") primary = o1.Reached > 0 ? "vacuity-mislabel" : "refusal-validated";
        else if (token == "Failed") (primary, reason) = Refutation(c, o1, claim);
        else primary = Demotion(row, token!);
        // Cache rows: the warm token must equal the cold token (stale-cache-proof); variant A must be Proven.
        if (c.RowId.StartsWith("CACHE-", StringComparison.Ordinal) && record["channels"]!["CH-CACHE"] is JsonObject cache)
        {
            var cold = Str(Claim((JsonObject)cache["cold"]!, claimSite, null, c.CalorSource)["token"]);
            record["coldToken"] = cold;
            if (cache["warmA"] is JsonObject warmA)
            {
                var a = Str(Claim(warmA, claimSite, null, c.CalorSource)["token"]);
                record["variantAToken"] = a;
                if (a != "Proven") (primary, reason) = ("harness-invalid", $"tamper-reachability control (variant A) produced {a}, Proven required");
            }
            if (primary != "harness-invalid" && cold != token) added.Add("stale-cache-proof");
            if (primary != "harness-invalid" && cold == "Proven" && o1.Kind == "violated" && primary != "false-unconditional-proof") added.Add("false-unconditional-proof");
        }
        if (record["guards"]?["elidedWithoutProof"]?.GetValue<bool>() == true && primary != "harness-invalid") added.Add("guard-elided-without-proof");
        if (record["o2"]?["runs"] is JsonArray runs && runs.Any(r => r!["divergence"]!.GetValue<bool>())) added.Add("semantic-divergence");
        // Verifier controls other than solver availability: a token that differs from expectedOutcome (P845 is judged separately).
        var expected = Str(template["expectedOutcome"]);
        if (expected != null && ClaimTokens.Contains(expected) && row.Id is not ("CTRL-POSITIVE" or "CTRL-NEGATIVE") && S(record["baseline"]) != "P845"
            && token != expected && primary is not ("false-unconditional-proof" or "harness-invalid"))
            added.Add("control-mismatch");
        record["status"] = "executed";
        record["class"] = primary;
        record["classReason"] = reason;
        record["addedFindings"] = added;
        record["nonVacuityCheck"] = Proof(token) ? new JsonObject { ["id"] = "R1-O1-reached", ["result"] = o1.Reached > 0 ? "passed" : "failed" } : null;
    }

    private static (string, string?) Rejection(RowInfo row, JsonObject claim)
    {
        var codes = claim["compileErrors"]!.AsArray().Select(S).ToArray();
        if (codes.Length == 0) return ("harness-invalid", "no claim-bearing outcome and no rejection: " + claim["claimError"]);
        if (row.Classification == "unsupported-refused" && row.RefusalCodes is { Length: > 0 } allowed
            && (allowed.SequenceEqual(["*"]) ? codes.All(code => int.TryParse(code.AsSpan(5), out var n) && n >= 200) : codes.All(allowed.Contains)))
            return ("refusal-validated", "front-end rejection with registered codes [" + string.Join(",", codes) + "]");
        return ("harness-invalid", "compile-rejected [" + string.Join(",", codes) + "] outside the row's frontEndRefusalCodes");
    }

    private static string Demotion(RowInfo row, string token) => token switch
    {
        "Assumed" => row.Classification == "assumed" ? "refusal-validated" : "no-claim",
        "Unsupported" => row.Classification is "unsupported-refused" or "assumed" ? "refusal-validated" : "no-claim",
        "Boundary" => "no-claim",
        _ => throw new InvalidOperationException($"unmatched classification combination: {row.Classification}/{token}"),
    };

    private static (string, string?) Refutation(SweepCaseGenerator.Case c, IndependentOracle.Verdict o1, JsonObject claim)
    {
        var bindings = claim["counterexample"]?.AsArray().ToDictionary(b => S(b!["name"]), b => S(b!["value"]), StringComparer.Ordinal);
        if (bindings is { Count: > 0 })
        {
            using var program = new OracleProgram(c.OracleSource);
            if (program.FromModel(bindings) is { } input)
            {
                var rendered = OracleProgram.Render(input);
                var point = program.PointVerdict(input);
                claim["modelReplay"] = new JsonObject { ["input"] = rendered, ["o1"] = point };
                return point == "violated" ? ("no-claim", "Failed with a genuine counterexample (model replayed under O1)")
                    : ("spurious-refutation", $"model {rendered} does not violate the property under O1 ({point})");
            }
        }
        claim["modelReplay"] = new JsonObject { ["input"] = null, ["o1"] = "no replayable model" };
        return o1.Kind == "holds-exhaustive" ? ("spurious-refutation", "Failed with no replayable model while O1 is holds-exhaustive")
            : ("no-claim", "Failed with no replayable model on a " + (c.Exhaustive ? "exhaustive" : "sampled") + " domain");
    }
}
