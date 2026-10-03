using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.SoundnessRegistration;

namespace Calor.Soundness.Sweep;

internal sealed record RowInfo(string Id, string Classification, bool ReleaseCritical, string Channel, string[]? RefusalCodes, int Order);

/// <summary>
/// One case execution on one baseline: every channel of the case's row, both emissions, the O1
/// verdict, O2 replay, guard observations, and the mechanical classification of registration
/// caseResults.classificationTable. Returns one attempt record; retries are scheduled by the caller.
/// </summary>
internal static partial class CaseExecutor
{
    public static readonly string[] ClaimTokens =
        ["Proven", "ProvenVacuous", "Discharged", "Assumed", "Unsupported", "TimeoutOrUnavailable", "Failed", "Boundary"];

    public static JsonObject Execute(BaselineHost host, SweepCaseGenerator.Case c, RowInfo row, JsonNode template, string scratch)
    {
        var record = new JsonObject
        {
            ["baseline"] = host.Id,
            ["caseId"] = c.Id,
            ["rowId"] = c.RowId,
            ["templateId"] = c.TemplateId,
            ["caseSha256"] = new JsonObject { ["calor"] = c.CalorSha256, ["oracle"] = c.OracleSha256, ["prime"] = c.CalorPrimeSource == null ? null : SweepCaseGenerator.Sha256(c.CalorPrimeSource) },
        };
        var claimSite = template["claimSite"]!.GetValue<string>();
        var obligationKind = template["obligationKind"]?.GetValue<string>();
        record["claimSite"] = claimSite;
        record["obligationKind"] = obligationKind;

        // O1: the registered independent oracle, unchanged.
        var o1 = OracleProgram.WithCulture(() => IndependentOracle.Evaluate(c.OracleSource));
        record["o1"] = JsonSerializer.SerializeToNode(o1);

        // Channels.
        JsonObject elided, forced;
        var channels = new JsonObject();
        record["channels"] = channels;
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

        // O2 replay on the forced emission (an observation; claim-independent).
        record["o2"] = O2(host, c, template, o1, forced, claimSite, obligationKind);
        Adjudicate(record, c, row, template, o1, elided, forced);
        return record;
    }

    /// <summary>
    /// Claim mapping, guard observation, and classification from retained observations only (no
    /// compiler call), so a corrected mapping can be re-applied to retained attempts without
    /// re-executing a case.
    /// </summary>
    public static void Adjudicate(JsonObject record, SweepCaseGenerator.Case c, RowInfo row, JsonNode template,
        IndependentOracle.Verdict o1, JsonObject elided, JsonObject forced)
    {
        var claimSite = template["claimSite"]!.GetValue<string>();
        var obligationKind = template["obligationKind"]?.GetValue<string>();
        var claim = Claim(elided, claimSite, obligationKind, c.CalorSource);
        var forcedClaim = Claim(forced, claimSite, obligationKind, c.CalorSource);
        record["claim"] = claim;
        record["forcedClaim"] = forcedClaim;
        record["guards"] = Guards(elided, forced, claimSite, obligationKind, claim["token"]?.GetValue<string>());
        Classify(record, c, row, template, o1, claim, forcedClaim);
    }

    /// <summary>The (elided, forced) compile records of a retained attempt.</summary>
    public static (JsonObject Elided, JsonObject Forced) Emissions(JsonObject record)
    {
        var channels = record["channels"]!.AsObject();
        if (channels["CH-CACHE"] is JsonObject cache)
            return ((JsonObject)cache["warm"]!, (JsonObject)cache["warmForced"]!);
        var ch = (JsonObject)channels.First().Value!;
        return ((JsonObject)ch["elided"]!, (JsonObject)ch["forced"]!);
    }

    private static (JsonObject Elided, JsonObject Forced) RunCache(BaselineHost host, SweepCaseGenerator.Case c, JsonObject channels, string scratch)
    {
        string Fresh() { var d = Path.Combine(scratch, "cache-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
        var cache = new JsonObject();
        channels["CH-CACHE"] = cache;
        if (c.RowId == "CACHE-SEMANTICS-VERSION")
        {
            // Variant A: tamper-reachability control (outcome -> proven only); Proven required.
            var dirA = Fresh();
            cache["primeA"] = host.Compile(c.CalorPrimeSource!, false, true, dirA);
            cache["tamperA"] = Tamper(dirA, staleVersion: false);
            cache["warmA"] = host.Compile(c.CalorSource, false, true, dirA);
            // Variant B (adjudicated): outcome -> proven AND translator semantics version -> r1-stale-version.
            var dirB = Fresh();
            cache["primeB"] = host.Compile(c.CalorPrimeSource!, false, true, dirB);
            cache["tamperB"] = Tamper(dirB, staleVersion: true);
            cache["warm"] = host.Compile(c.CalorSource, false, true, dirB);
            cache["warmForced"] = host.Compile(c.CalorSource, false, false, dirB);
        }
        else
        {
            var dir = Fresh();
            cache["prime"] = host.Compile(c.CalorPrimeSource!, false, true, dir);
            cache["warm"] = host.Compile(c.CalorSource, false, true, dir);
            cache["warmForced"] = host.Compile(c.CalorSource, false, false, dir);
        }
        cache["cold"] = host.Compile(c.CalorSource, false, true, Fresh());
        return ((JsonObject)cache["warm"]!, (JsonObject)cache["warmForced"]!);
    }

    /// <summary>Rewrites every primed cache entry's outcome to proven (and, for variant B, its translator semantics version).</summary>
    private static JsonArray Tamper(string projectDir, bool staleVersion)
    {
        var changed = new JsonArray();
        var root = Path.Combine(projectDir, ".calor", "verification-cache");
        if (!Directory.Exists(root))
            return changed;
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var entry = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var before = entry.ToJsonString();
            // The baseline serializes camelCase with a numeric ContractVerificationStatus (Proven = 0).
            var statusIsNumber = entry["status"] is JsonValue v && v.TryGetValue<int>(out _);
            entry["status"] = statusIsNumber ? 0 : "Proven";
            entry["proofStatus"] = "proven";
            entry["proofVacuous"] = false;
            entry["counterexampleBindings"] = null;
            entry["counterexampleDescription"] = null;
            entry["proofReason"] = null;
            if (staleVersion)
            {
                var sv = entry["semanticsVersion"]?.GetValue<string>() ?? "";
                var bar = sv.LastIndexOf('|');
                entry["semanticsVersion"] = (bar >= 0 ? sv[..(bar + 1)] : "") + "r1-stale-version";
            }
            File.WriteAllText(file, entry.ToJsonString());
            changed.Add(new JsonObject { ["file"] = Path.GetRelativePath(projectDir, file), ["before"] = before, ["after"] = entry.ToJsonString() });
        }
        return changed;
    }

    /// <summary>Maps the case's one claim to exactly one contract section 4 token (from ProofOutcome, never a legacy enum).</summary>
    internal static JsonObject Claim(JsonObject compile, string claimSite, string? obligationKind, string calorSource)
    {
        var result = new JsonObject();
        var errors = compile["diagnostics"]!.AsArray().Where(d => d!["severity"]!.GetValue<string>() == "Error")
            .Select(d => d!["code"]!.GetValue<string>()).Distinct().Order(StringComparer.Ordinal).ToArray();
        result["compileErrors"] = new JsonArray(errors.Select(e => (JsonNode)e).ToArray());
        switch (claimSite)
        {
            case "postcondition":
            case "precondition":
            {
                var key = claimSite == "postcondition" ? "postconditions" : "preconditions";
                var functions = compile["contracts"]?.AsArray();
                if (functions == null) { result["claimFound"] = false; break; }
                var all = functions.SelectMany(f => f![key]!.AsArray().Select(r => (Fn: f!["functionName"]!.GetValue<string>(), R: r!))).ToList();
                if (all.Count != 1)
                    all = all.Where(x => x.Fn == "Probe").ToList();
                if (all.Count != 1) { result["claimFound"] = false; result["claimError"] = $"{all.Count} {key} results"; break; }
                var outcome = all[0].R["outcome"]!;
                result["claimFound"] = true;
                result["rawStatus"] = outcome["status"]!.GetValue<string>();
                result["legacyStatus"] = all[0].R["legacyStatus"]!.GetValue<string>();
                result["isVacuous"] = outcome["isVacuous"]!.GetValue<bool>();
                result["assumptions"] = outcome["assumptions"]!.DeepClone();
                result["counterexample"] = outcome["counterexample"]?.DeepClone();
                result["translatorSemanticsVersion"] = all[0].R["translatorSemanticsVersion"]?.DeepClone();
                result["token"] = FromProofStatus(outcome["status"]!.GetValue<string>(), outcome["isVacuous"]!.GetValue<bool>());
                break;
            }
            case "obligation":
            {
                var obligations = compile["obligations"]?.AsArray();
                if (obligations == null) { result["claimFound"] = false; break; }
                var matching = obligations.Where(o => o!["kind"]!.GetValue<string>() == obligationKind).ToList();
                if (matching.Count != 1) { result["claimFound"] = false; result["claimError"] = $"{matching.Count} obligations of kind {obligationKind}"; break; }
                var o = matching[0]!;
                var status = o["status"]!.GetValue<string>();
                var outcome = o["outcome"];
                result["claimFound"] = true;
                result["rawStatus"] = status;
                result["outcomeStatus"] = outcome?["status"]?.DeepClone();
                result["assumptions"] = outcome?["assumptions"]?.DeepClone();
                result["counterexample"] = outcome?["counterexample"]?.DeepClone() ?? ParseCounterexample(o["counterexampleDescription"]?.GetValue<string>());
                result["token"] = (status, outcome?["status"]?.GetValue<string>()) switch
                {
                    ("Discharged", null or "Proven") => "Discharged",
                    ("Failed", null or "Refuted") => "Failed",
                    ("Boundary", _) => "Boundary",
                    ("Pending", _) => "TimeoutOrUnavailable",
                    ("Unsupported", null or "Unsupported") => "Unsupported",
                    ("Timeout", "Assumed") => "Assumed",
                    ("Timeout", "Timeout" or "Unknown" or "Unavailable") => "TimeoutOrUnavailable",
                    _ => "UNMAPPED:" + status + "/" + outcome?["status"],
                };
                break;
            }
            case "implication":
            {
                // CH-IMPLICATION: each template carries contracts in one direction; only that direction's
                // Calor0815 text (Proven), Calor0816 (TimeoutOrUnavailable), or LSP-violation error
                // (Failed) is adjudicated; none of these means the heuristic fallback ran (Unsupported).
                // Any other Calor0815 (e.g. the vacuous other direction) is archived, not adjudicated.
                var post = calorSource.Contains("§S (", StringComparison.Ordinal);
                var dir = post ? "Postcondition" : "Precondition";
                var proven = post ? "Postcondition strengthening proven" : "Precondition weakening proven";
                var unknown = post ? "postcondition strengthening is valid" : "precondition weakening is valid";
                var diags = compile["diagnostics"]!.AsArray().Select(d => d!.AsObject()).ToList();
                bool Has(JsonObject d, string text) => d["message"]!.GetValue<string>().Contains(text, StringComparison.Ordinal);
                var mine = diags.Where(d => Has(d, "'Impl.Run'")).ToList();
                result["direction"] = dir;
                result["implicationDiagnostics"] = new JsonArray(mine.Select(d => d.DeepClone()).ToArray());
                var reachedChecker = diags.All(d => d["severity"]!.GetValue<string>() != "Error" || Has(d, "LSP violation"));
                if (!reachedChecker && !mine.Any()) { result["claimFound"] = false; result["claimError"] = "rejected before the inheritance checker"; break; }
                result["claimFound"] = true;
                var lsp = mine.FirstOrDefault(d => d["severity"]!.GetValue<string>() == "Error"
                    && (Has(d, $"LSP violation: {dir} in") || Has(d, $"LSP violation: Could not prove that {dir.ToLowerInvariant()} in")));
                result["token"] = mine.Any(d => d["code"]!.GetValue<string>() == "Calor0815" && Has(d, proven)) ? "Proven"
                    : mine.Any(d => d["code"]!.GetValue<string>() == "Calor0816" && Has(d, unknown)) ? "TimeoutOrUnavailable"
                    : lsp != null ? "Failed" : "Unsupported";
                result["rawStatus"] = result["token"]!.GetValue<string>();
                result["isVacuous"] = false;
                result["counterexample"] = lsp == null ? null : ParseCounterexample(lsp["message"]!.GetValue<string>());
                break;
            }
            case "guard-emission":
                result["claimFound"] = true;
                result["token"] = null;
                break;
        }
        return result;
    }

    private static string FromProofStatus(string status, bool vacuous) => status switch
    {
        "Proven" => vacuous ? "ProvenVacuous" : "Proven",
        "Refuted" => "Failed",
        "Assumed" => "Assumed",
        "Unsupported" => "Unsupported",
        "Timeout" or "Unknown" or "Unavailable" => "TimeoutOrUnavailable",
        _ => "UNMAPPED:" + status,
    };

    [GeneratedRegex(@"Counterexample:\s*(?<b>.*)$")]
    private static partial Regex CounterexampleText();

    private static JsonArray? ParseCounterexample(string? text)
    {
        if (text == null) return null;
        var m = CounterexampleText().Match(text);
        if (!m.Success) return null;
        var array = new JsonArray();
        foreach (var part in m.Groups["b"].Value.TrimEnd('.').Split(", "))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) array.Add(new JsonObject { ["name"] = part[..eq].Trim(), ["value"] = part[(eq + 1)..].Trim() });
        }
        return array;
    }

    private static int Count(string text, string marker)
    {
        var n = 0;
        for (var i = text.IndexOf(marker, StringComparison.Ordinal); i >= 0; i = text.IndexOf(marker, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    private static JsonObject Guards(JsonObject elided, JsonObject forced, string claimSite, string? obligationKind, string? token)
    {
        var marker = claimSite switch
        {
            "postcondition" or "guard-emission" => "Calor.Runtime.ContractKind.Ensures",
            "obligation" => obligationKind switch
            {
                "ProofObligation" => "Proof obligation [",
                "IndexBounds" => "\"Indexed-type bound violated\"",
                "RefinementEntry" => "\"Violation of refinement type|\"Violation of inline refinement",
                "RefinementReturn" => "\"Return value violates refinement type",
                "Subtype" => "\"Value violates refinement type",
                _ => null,
            },
            _ => null,
        };
        var g = new JsonObject { ["marker"] = marker };
        if (marker == null) { g["applicable"] = false; return g; }
        var e = elided["emitted"]!.GetValue<string>();
        var f = forced["emitted"]!.GetValue<string>();
        g["applicable"] = true;
        if (e.Length == 0 || f.Length == 0) { g["observed"] = false; g["reason"] = "no-emission (compile rejected)"; return g; }
        var ec = marker.Split('|').Sum(m => Count(e, m));
        var fc = marker.Split('|').Sum(m => Count(f, m));
        g["observed"] = true;
        g["elidedCount"] = ec;
        g["forcedCount"] = fc;
        g["guardInForcedEmission"] = fc > 0;
        g["guardInElidedEmission"] = ec >= fc && fc > 0;
        var mayElide = token is "Proven" or "Discharged";
        g["elidedWithoutProof"] = fc == 0 || (ec < fc && !mayElide);
        return g;
    }

    private static JsonObject O2(BaselineHost host, SweepCaseGenerator.Case c, JsonNode template, IndependentOracle.Verdict o1,
        JsonObject forced, string claimSite, string? obligationKind)
    {
        if (claimSite is "implication" or "precondition")
            return new JsonObject { ["status"] = "not-applicable" };
        if (o1.Kind == "oracle-invalid")
            return new JsonObject { ["status"] = "not-run-oracle-invalid" };
        var emitted = forced["emitted"]!.GetValue<string>();
        if (emitted.Length == 0)
            return new JsonObject { ["status"] = "not-run-no-emission" };
        using var program = new OracleProgram(c.OracleSource);
        var wanted = new List<string>();
        if (o1.Kind == "violated" && o1.Witness != null)
        {
            var w = o1.Witness;
            var arrow = w.IndexOf(" -> result=", StringComparison.Ordinal);
            wanted.Add(arrow >= 0 ? w[..arrow] : w);
        }
        wanted.AddRange(o1.ReachedSample ?? []);
        wanted = wanted.Distinct().ToList();
        var recovered = program.Recover(wanted);
        var inputs = wanted.Where(recovered.ContainsKey).Select(r => (r, recovered[r], program.PointVerdict(recovered[r]))).ToList();
        var record = EmittedReplay.Run(emitted, Path.Combine(host.Directory, "Calor.Runtime.dll"), program.Parameters,
            template["oracle"]!["replay"]?.GetValue<string>(), inputs, claimSite, obligationKind);
        record["unrecovered"] = new JsonArray(wanted.Where(w => !recovered.ContainsKey(w)).Select(w => (JsonNode)w).ToArray());
        return record;
    }

    /// <summary>registration caseResults.classificationTable, applied mechanically (one primary class plus added finding records).</summary>
    private static void Classify(JsonObject record, SweepCaseGenerator.Case c, RowInfo row, JsonNode template,
        IndependentOracle.Verdict o1, JsonObject claim, JsonObject forcedClaim)
    {
        var token = claim["token"]?.GetValue<string>();
        var claimSite = template["claimSite"]!.GetValue<string>();
        var vacuousByConstruction = template["oracle"]!["vacuousByConstruction"]?.GetValue<bool>() ?? false;
        var added = new JsonArray();
        string primary;
        string? reason = null;
        var nonVacuity = token is "Proven" or "Discharged" ? (o1.Reached > 0 ? "passed" : "failed") : null;

        if (o1.Kind == "oracle-invalid")
            (primary, reason) = ("harness-invalid", "oracle-invalid: " + o1.Error);
        else if (token != null && token.StartsWith("UNMAPPED", StringComparison.Ordinal))
            (primary, reason) = ("harness-invalid", "unmappable status " + token);
        else if (claim["claimFound"]?.GetValue<bool>() != true)
            (primary, reason) = Rejection(row, claim);
        else if (claimSite == "guard-emission")
            (primary, reason) = ("no-claim", "guard-emission claim: no proof token; judged on the inherited guard only");
        else if (token != forcedClaim["token"]?.GetValue<string>())
            (primary, reason) = ("flaky", $"elided and forced compiles disagree ({token} vs {forcedClaim["token"]})");
        else if (token == "TimeoutOrUnavailable")
            (primary, reason) = ("timed-out", "TimeoutOrUnavailable");
        else if (c.Claim == "exists")
            primary = token switch
            {
                "Proven" => o1.Kind switch
                {
                    "witness-found" => "validated-proof",
                    "no-witness-exhaustive" => "false-unconditional-proof",
                    _ => "unconfirmed-claim",
                },
                "Failed" => o1.Kind == "witness-found" ? "spurious-refutation" : "no-claim",
                _ => Demotion(row, token!),
            };
        else if (token is "Proven" or "Discharged")
        {
            if (o1.Kind == "violated")
                primary = "false-unconditional-proof";
            else if (row.Classification != "modeled")
                primary = "required-demotion-absent";
            else if (o1.Reached > 0)
                primary = "validated-proof";
            else
                primary = o1.Kind == "vacuous-in-domain" && (c.Exhaustive || vacuousByConstruction) ? "vacuity-mislabel" : "unconfirmed-claim";
        }
        else if (token == "ProvenVacuous")
            primary = o1.Reached > 0 ? "vacuity-mislabel" : "refusal-validated";
        else if (token == "Failed")
            (primary, reason) = Refutation(c, o1, claim);
        else
            primary = Demotion(row, token!);

        // Cache rows: the warm token must equal the cold token (stale-cache-proof); variant A must be Proven.
        if (c.RowId.StartsWith("CACHE-", StringComparison.Ordinal) && record["channels"]!["CH-CACHE"] is JsonObject cache)
        {
            var cold = Claim((JsonObject)cache["cold"]!, claimSite, null, c.CalorSource)["token"]?.GetValue<string>();
            record["coldToken"] = cold;
            if (cache["warmA"] is JsonObject warmA)
            {
                var a = Claim(warmA, claimSite, null, c.CalorSource)["token"]?.GetValue<string>();
                record["variantAToken"] = a;
                if (a != "Proven")
                    (primary, reason) = ("harness-invalid", $"tamper-reachability control (variant A) produced {a}, Proven required");
            }
            if (primary != "harness-invalid" && cold != token)
                added.Add("stale-cache-proof");
            if (primary != "harness-invalid" && cold == "Proven" && o1.Kind == "violated" && primary != "false-unconditional-proof")
                added.Add("false-unconditional-proof");
        }

        if (record["guards"] is JsonObject g && g["elidedWithoutProof"]?.GetValue<bool>() == true && primary != "harness-invalid")
            added.Add("guard-elided-without-proof");
        if (record["o2"]?["runs"] is JsonArray runs && runs.Any(r => r!["divergence"]!.GetValue<bool>()))
            added.Add("semantic-divergence");

        // Verifier controls other than solver availability: a token that differs from expectedOutcome.
        var expected = template["expectedOutcome"]?.GetValue<string>();
        if (expected != null && ClaimTokens.Contains(expected) && row.Id is not ("CTRL-POSITIVE" or "CTRL-NEGATIVE")
            && record["baseline"]!.GetValue<string>() != "P845"
            && token != expected && primary != "false-unconditional-proof" && primary != "harness-invalid")
            added.Add("control-mismatch");

        record["status"] = "executed";
        record["class"] = primary;
        record["classReason"] = reason;
        record["addedFindings"] = added;
        record["nonVacuityCheck"] = nonVacuity == null ? null : new JsonObject { ["id"] = "R1-O1-reached", ["result"] = nonVacuity };
    }

    private static (string, string?) Rejection(RowInfo row, JsonObject claim)
    {
        var codes = claim["compileErrors"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        if (codes.Length == 0)
            return ("harness-invalid", "no claim-bearing outcome and no rejection: " + claim["claimError"]);
        if (row.Classification == "unsupported-refused" && row.RefusalCodes is { Length: > 0 } allowed)
        {
            var ok = allowed.SequenceEqual(["*"])
                ? codes.All(code => int.TryParse(code.AsSpan(5), out var n) && n >= 200)
                : codes.All(allowed.Contains);
            if (ok)
                return ("refusal-validated", "front-end rejection with registered codes [" + string.Join(",", codes) + "]");
        }
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
        var bindings = claim["counterexample"]?.AsArray()
            .ToDictionary(b => b!["name"]!.GetValue<string>(), b => b!["value"]!.GetValue<string>(), StringComparer.Ordinal);
        if (bindings is { Count: > 0 })
        {
            using var program = new OracleProgram(c.OracleSource);
            var input = program.FromModel(bindings);
            if (input != null)
            {
                var point = program.PointVerdict(input);
                claim["modelReplay"] = new JsonObject { ["input"] = OracleProgram.Render(input), ["o1"] = point };
                return point == "violated" ? ("no-claim", "Failed with a genuine counterexample (model replayed under O1)")
                    : ("spurious-refutation", $"model {OracleProgram.Render(input)} does not violate the property under O1 ({point})");
            }
        }
        claim["modelReplay"] = new JsonObject { ["input"] = null, ["o1"] = "no replayable model" };
        return o1.Kind == "holds-exhaustive" ? ("spurious-refutation", "Failed with no replayable model while O1 is holds-exhaustive")
            : ("no-claim", "Failed with no replayable model on a " + (c.Exhaustive ? "exhaustive" : "sampled") + " domain");
    }

    internal static long Elapsed(Stopwatch sw) => sw.ElapsedMilliseconds;
}
