using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.SoundnessRegistration;
using static Calor.Soundness.Sweep.Program;

namespace Calor.Soundness.Sweep;

// Mechanical row status, findings, and controls (registration rowStatus, findingRecord, controls); each baseline uses only its own results.
internal static class Report
{
    private static readonly HashSet<string> FindingClasses =
    [
        "false-unconditional-proof", "false-proof-unconfirmed-vacuity", "required-demotion-absent", "guard-elided-without-proof", "vacuity-mislabel",
        "spurious-refutation", "spurious-model", "stale-cache-proof", "semantic-divergence", "control-mismatch", "candidate-unconfirmed",
    ];

    private static readonly HashSet<string> Blocking = ["harness-invalid", "flaky", "crashed", "unconfirmed-claim", "timed-out"];

    private static List<JsonObject> Lines(string path) => File.Exists(path) ? File.ReadLines(path).Select(l => JsonNode.Parse(l)!.AsObject()).ToList() : [];

    private static JsonNode Counts(Dictionary<string, int> d) => JsonSerializer.SerializeToNode(d.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary())!;

    public static int Write(string repo, string outRoot)
    {
        var (registration, templates, cases, rows) = Load(Path.GetFullPath(repo));
        var results = new[] { "B1", "N1", "P845" }.ToDictionary(b => b, b => Lines(Path.Combine(outRoot, b, "case-results.jsonl")));
        JsonObject? Final(string b, string caseId) => results[b].LastOrDefault(r => S(r["caseId"]) == caseId);
        var controls = Controls(cases, templates, Final);
        File.WriteAllText(Path.Combine(outRoot, "controls.json"), controls.ToJsonString(Indented));
        var summary = new JsonObject();
        foreach (var b in new[] { "B1", "N1" })
        {
            var other = b == "B1" ? "N1" : "B1";
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, $"docs/plans/evidence/r1-1419/manifest-{b}.json")))!;
            var pins = JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, b, "pins.json")))!;
            var (rowsDir, findingsDir) = (Path.Combine(outRoot, b, "rows"), Path.Combine(outRoot, b, "findings"));
            Directory.CreateDirectory(rowsDir);
            if (Directory.Exists(findingsDir)) Directory.Delete(findingsDir, true);
            Directory.CreateDirectory(findingsDir);
            var (statusRows, findingIndex, counts, n) = (new JsonArray(), new JsonArray(), new Dictionary<string, int>(StringComparer.Ordinal), 0);
            var nonDiscriminating = !controls["retro845"]![b]!["discriminates"]!.GetValue<bool>();
            foreach (var mrow in manifest["rows"]!.AsArray())
            {
                var rowId = S(mrow!["row"]);
                var row = rows[rowId];
                var (lines, classes, findingClasses, unexecuted) = (new List<JsonObject>(), new Dictionary<string, int>(StringComparer.Ordinal), new List<string>(), 0);
                foreach (var caseId in mrow["caseIds"]!.AsArray().Select(S))
                {
                    if (Final(b, caseId) is not { } r) { unexecuted++; lines.Add(new JsonObject { ["caseId"] = caseId, ["baseline"] = b, ["class"] = "not-investigated", ["reason"] = "unexecuted" }); continue; }
                    lines.Add(r);
                    var cls = S(r["class"]);
                    classes[cls] = classes.GetValueOrDefault(cls) + 1;
                    foreach (var fc in r["addedFindings"]!.AsArray().Select(S).Prepend(cls).Where(FindingClasses.Contains).Distinct())
                    {
                        findingClasses.Add(fc);
                        var id = $"F-{b}-{++n:D3}";
                        File.WriteAllText(Path.Combine(findingsDir, id + ".json"), Finding(id, b, other, fc, r, pins, registration, outRoot, Final(other, caseId), rowId, cases.First(x => x.Id == caseId)).ToJsonString(Indented));
                        findingIndex.Add(new JsonObject { ["findingId"] = id, ["rowId"] = rowId, ["caseId"] = caseId, ["class"] = fc, ["token"] = r["token"]?.DeepClone(), ["o1"] = r["o1"]?.DeepClone() });
                    }
                }
                if (controls["mutationPairs"]![b]!.AsArray().Any(p => S(p!["row"]) == rowId && !p["oracleEqual"]!.GetValue<bool>())) findingClasses.Add("control-mismatch");
                File.WriteAllText(Path.Combine(rowsDir, rowId + ".jsonl"), string.Concat(lines.Select(l => l.ToJsonString() + "\n")));
                var reasons = new JsonArray();
                if (unexecuted > 0) reasons.Add($"{unexecuted} unexecuted");
                foreach (var k in classes.Keys.Where(Blocking.Contains)) reasons.Add($"{classes[k]} {k}");
                var status = findingClasses.Contains("false-unconditional-proof") ? "FALSE-PROOF" : findingClasses.Count > 0 ? "FINDING"
                    : unexecuted > 0 || classes.Keys.Any(Blocking.Contains) ? "INCOMPLETE" : "CLEAN-WITHIN-BUDGET";
                if (nonDiscriminating && rowId is "NUM-LITERAL-TYPED" or "NUM-ARITH-ADD")
                {
                    reasons.Add("#845 discriminating control did not discriminate on this baseline (registration controls.retrospective845.nonDiscriminatingConsequence)");
                    if (status == "CLEAN-WITHIN-BUDGET") status = "INCOMPLETE";
                }
                if (row.Classification == "not-investigated") status = "NOT-INVESTIGATED";
                counts[status] = counts.GetValueOrDefault(status) + 1;
                statusRows.Add(new JsonObject
                {
                    ["id"] = $"{b}:{rowId}", ["row"] = rowId, ["classification"] = row.Classification, ["releaseCritical"] = row.ReleaseCritical, ["status"] = status,
                    ["blockedAtTimebox"] = status == "INCOMPLETE" && row.ReleaseCritical, ["allocated"] = mrow["caseIds"]!.AsArray().Count, ["unexecuted"] = unexecuted,
                    ["classCounts"] = Counts(classes), ["findingClasses"] = new JsonArray(findingClasses.Distinct().Select(x => (JsonNode)x).ToArray()),
                    ["exhaustiveDomains"] = lines.Count(l => l["exhaustive"]?.GetValue<bool>() == true), ["reasons"] = reasons,
                    ["controlMismatchesGuardingThisRow"] = new JsonArray(controls["controlMismatches"]![b]!.AsArray().Where(m => m!["guards"]!.AsArray().Any(g => S(g) == rowId)).Select(m => m!["caseId"]!.DeepClone()).ToArray()),
                    ["resultPath"] = mrow["resultPath"]!.DeepClone(),
                });
            }
            File.WriteAllText(Path.Combine(outRoot, b, "row-status.json"), new JsonObject { ["baseline"] = b, ["counts"] = Counts(counts), ["rows"] = statusRows }.ToJsonString(Indented));
            File.WriteAllText(Path.Combine(outRoot, b, "findings-index.json"), findingIndex.ToJsonString(Indented));
            summary[b] = new JsonObject { ["rowStatusCounts"] = Counts(counts), ["findings"] = findingIndex.Count, ["runValidity"] = File.Exists(Path.Combine(outRoot, "invalid-run.jsonl")) ? "INVALID: see invalid-run.jsonl (statuses are not eligible; candidate false proofs stay findings)" : Native(pins, S(pins["binaryDirectory"])) != null ? "pinned" : "provenance-unconfirmed: native solver image not pinned in-run" };
        }
        File.WriteAllText(Path.Combine(outRoot, "summary.json"), summary.ToJsonString(Indented));
        Console.WriteLine(summary.ToJsonString(Indented));
        return 0;
    }

    private static JsonObject Finding(string id, string b, string other, string cls, JsonObject r, JsonNode pins, JsonNode registration, string outRoot, JsonObject? otherResult, string rowId, SweepCaseGenerator.Case c)
    {
        var (reproSource, reproOutput) = Reproduction(c.OracleSource);
        var observations = r["attempts"]!.AsArray().Select(a => JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, b, S(a!["path"]))))! is var raw && raw["lateObservation"] is JsonObject late ? late : raw).ToList();
        var attempt = observations.LastOrDefault(x => x["class"]?.GetValue<string>() == cls || (x["addedFindings"]?.AsArray().Any(y => S(y) == cls) ?? false)) ?? observations[^1];
        var (claim, o1, guards) = (cls == "false-unconditional-proof" && attempt["claim"]?["token"]?.GetValue<string>() is not ("Proven" or "Discharged") ? attempt["forcedClaim"] : attempt["claim"], attempt["o1"], attempt["guards"]);
        // The in-run pin did not capture the native image; the post-run native-check (same process layout) records what each baseline maps.
        var nativePath = Path.Combine(outRoot, "native-z3-check.json");
        var native = (File.Exists(nativePath) ? JsonNode.Parse(File.ReadAllText(nativePath)) : null)?[b]?["processLoadedZ3"]?.AsArray().FirstOrDefault(x => S(x!["path"]).StartsWith(S(pins["binaryDirectory"]), StringComparison.Ordinal));
        static string Repro(JsonObject? x, string cls) => x == null || (x["status"] is JsonNode st && S(st) != "executed") ? "not-run"
            : x["class"]?.GetValue<string>() == cls || (x["addedFindings"]?.AsArray().Any(y => S(y) == cls) ?? false) ? "reproduces" : "does-not-reproduce";
        var cross = Lines(Path.Combine(outRoot, other, "cross-baseline.jsonl")).LastOrDefault(x => S(x["caseId"]) == S(r["caseId"]));
        var replay = claim?["modelReplay"];
        var useModel = cls == "spurious-refutation" && replay?["input"] is JsonValue; // the defect witness of a spurious refutation is the solver model
        var witnessInput = useModel ? S(replay!["input"]) : o1?["Witness"]?.GetValue<string>() ?? replay?["input"]?.ToString();
        var o2For = attempt["o2"]?["runs"]?.AsArray().FirstOrDefault(x => witnessInput?.StartsWith(S(x!["input"]), StringComparison.Ordinal) == true);
        return new JsonObject
        {
            ["findingId"] = id, ["baseline"] = b, ["registrationCommit"] = pins["registrationCommit"]!.DeepClone(), ["rowId"] = rowId, ["caseId"] = r["caseId"]!.DeepClone(),
            ["templateId"] = r["templateId"]!.DeepClone(), ["caseSha256"] = attempt["caseSha256"]?.DeepClone(), ["class"] = cls,
            ["claim"] = new JsonObject
            {
                ["site"] = attempt["claimSite"]?.DeepClone(), ["token"] = claim?["token"]?.DeepClone(), ["rawStatus"] = claim?["rawStatus"]?.DeepClone(),
                ["isVacuous"] = claim?["isVacuous"]?.DeepClone(), ["assumptions"] = claim?["assumptions"]?.DeepClone(),
                ["guardInElidedEmission"] = guards?["guardInElidedEmission"]?.DeepClone(), ["guardInForcedEmission"] = guards?["guardInForcedEmission"]?.DeepClone(),
            },
            ["witness"] = new JsonObject
            {
                ["inputs"] = witnessInput, ["source"] = useModel ? "the solver counterexample model, replayed under O1" : "the O1 witness",
                ["o1"] = useModel ? $"model input: {replay!["o1"]}; case verdict: {o1?["Kind"]}" : $"{o1?["Kind"]} {o1?["ViolationKind"]}".Trim(),
                ["o2"] = o2For == null ? "not-run" : S(o2For["o2"]) switch { "guard-threw" => "guard threw", "returned" or "o2-other-exception" => "did not throw", _ => "not-run" },
                ["o2Detail"] = o2For?.DeepClone(),
            },
            ["nonVacuityCheck"] = attempt["nonVacuityCheck"]?.DeepClone(), ["classReason"] = r["classReason"]?.DeepClone(),
            ["minimized"] = new JsonObject
            {
                ["status"] = "registered-case: a single-function probe generated from one template; not reduced further", ["calorSource"] = c.CalorSource,
                ["calorPrimeSource"] = c.CalorPrimeSource, ["csharpReproduction"] = reproSource, ["reproductionOutput"] = reproOutput, ["sha256"] = Hashing.Sha256Text(c.CalorSource),
            },
            ["environment"] = new JsonObject
            {
                ["calorDllSha256"] = pins["calorDllSha256"]!.DeepClone(), ["z3NativeSha256"] = native?["sha256"]?.DeepClone(), ["z3NativeSource"] = native == null ? "not captured" : "post-run native-check.json (same process layout)", ["dotnetVersion"] = pins["dotnetVersion"]!.DeepClone(),
                ["os"] = pins["os"]!.DeepClone(), ["translatorSemanticsVersion"] = pins["translatorSemanticsVersion"]?.DeepClone(),
            },
            ["attempts"] = r["attempts"]!.DeepClone(),
            ["otherBaseline"] = new JsonObject
            {
                ["id"] = other, ["result"] = Repro(cross, cls), ["rerun"] = cross?.DeepClone(),
                ["otherSweepResult"] = new JsonObject { ["token"] = otherResult?["token"]?.DeepClone(), ["class"] = otherResult?["class"]?.DeepClone(), ["result"] = Repro(otherResult, cls) },
            },
            ["affectedGuarantee"] = Affected(rowId, attempt["claimSite"]?.GetValue<string>(), registration), ["filedIssue"] = null, ["disposition"] = null,
        };
    }

    private static string Affected(string rowId, string? site, JsonNode registration)
    {
        var title = S(registration["denominator"]!["rows"]!.AsArray().First(x => S(x!["id"]) == rowId)!["title"]);
        var path = site switch
        {
            "obligation" => "a Discharged obligation drops its emitted runtime guard under the default ObligationPolicy (Discharged = Ignore) with ElideProvenGuards on (CSharpEmitter obligation sites)",
            "implication" => "no emitted guard; a Proven implication makes ContractInheritanceChecker accept the implementer contract (no LSP-violation error)",
            "precondition" => "none (precondition satisfiability removes no guard)",
            _ when rowId.StartsWith("CACHE-", StringComparison.Ordinal) => "a cached (warm) Proven postcondition is elided from the emitted C# by default (verification cache, then CSharpEmitter postcondition site)",
            _ => "a non-vacuous Proven postcondition is elided from the emitted C# by default (ElideProvenGuards, CSharpEmitter postcondition site)",
        };
        var surfaces = site switch
        {
            "obligation" => "website/content/syntax-reference/refinement-types.mdx, website/content/guides/verification-guarantees.mdx",
            "implication" => "website/content/syntax-reference/inheritance.mdx, website/content/cli/verify.mdx",
            _ => "docs/verification-modeled-forms.md, website/content/guides/verification-guarantees.mdx, website/content/cli/compile.mdx",
        };
        return $"Form: {title} (row {rowId}). Guard path: {path}. Public surfaces that describe this form: {surfaces} (located by search; their wording was not audited by S1). "
            + "No experiment relying on this form was identified by S1; S2 (#1413) confirms.";
    }

    // Standalone C# reproduction (no Calor): the registered oracle program plus a Main that enumerates its domain and prints the first input where the property
    // is false or throws (or, for an exists claim, the first witness). BCL only; executed here.
    private static (string Source, string Output) Reproduction(string oracleSource)
    {
        const string Main = """

public static class Repro
{
    public static int Main()
    {
        int reached = 0;
        foreach (var a in R1Oracle.Inputs())
        {
            bool hyp; object? r = null; bool ok; string why;
            try { hyp = R1Oracle.HypO(a); } catch { continue; }
            if (!hyp) continue;
            if (R1Oracle.HasBody) { try { r = R1Oracle.BodyO(a); } catch { continue; } }
            reached++;
            try { ok = R1Oracle.PropO(a, r); why = "false"; } catch (Exception e) { ok = false; why = "throws " + e.GetType().Name; }
            var shown = "(" + string.Join(", ", a.Select(v => v is null ? "null" : v is Array arr ? "[" + string.Join(", ", arr.Cast<object>()) + "]" : v is string s ? "\"" + s + "\"" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))) + ")";
            if (R1Oracle.Claim == "exists") { if (ok) { Console.WriteLine("witness " + shown); return 0; } continue; }
            if (!ok) { Console.WriteLine("property " + why + " at " + shown + (R1Oracle.HasBody ? " result=" + Convert.ToString(r, System.Globalization.CultureInfo.InvariantCulture) : "")); return 1; }
        }
        Console.WriteLine((R1Oracle.Claim == "exists" ? "no witness" : "no violation") + " in the registered domain; reached inputs: " + reached);
        return 0;
    }
}
""";
        var source = oracleSource + Main;
        var (image, errors) = Roslyn.Emit(source, [.. IndependentOracle.BclReferences, Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)], Roslyn.Checked(oracleSource));
        if (image == null) return (source, "compile error: " + errors);
        var context = new System.Runtime.Loader.AssemblyLoadContext("Repro", isCollectible: true);
        var previousOut = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            var exit = OracleProgram.WithCulture(() => context.LoadFromStream(image).GetType("Repro")!.GetMethod("Main")!.Invoke(null, null));
            return (source, writer.ToString().Trim() + $" (exit {exit})");
        }
        finally { Console.SetOut(previousOut); context.Unload(); }
    }

    private static JsonObject Controls(IReadOnlyList<SweepCaseGenerator.Case> cases, Dictionary<string, JsonNode> templates, Func<string, string, JsonObject?> final)
    {
        var (availability, mutation, retro, mismatches) = (new JsonObject(), new JsonObject(), new JsonObject(), new JsonObject());
        var ctrl = cases.Where(c => c.RowId.StartsWith("CTRL-", StringComparison.Ordinal)).ToList();
        string? Token(string b, string caseId) => final(b, caseId)?["token"]?.GetValue<string>();
        foreach (var b in new[] { "P845", "B1", "N1" })
        {
            var avail = new JsonArray(ctrl.Where(c => c.RowId is "CTRL-POSITIVE" or "CTRL-NEGATIVE").Select(c => (JsonNode)new JsonObject
            {
                ["caseId"] = c.Id, ["expected"] = S(templates[c.TemplateId]["expectedOutcome"]), ["token"] = Token(b, c.Id), ["ok"] = Token(b, c.Id) == S(templates[c.TemplateId]["expectedOutcome"]),
            }).ToArray());
            availability[b] = new JsonObject { ["pass"] = avail.All(x => x!["ok"]!.GetValue<bool>()), ["cases"] = avail };
            if (b == "P845") continue;
            mutation[b] = new JsonArray(ctrl.Where(c => c.RowId == "CTRL-MUTATION").GroupBy(c => S(templates[c.TemplateId]["mutationPair"])).Select(g =>
            {
                var members = g.Select(c => final(b, c.Id)).ToList();
                return (JsonNode)new JsonObject
                {
                    ["pair"] = g.Key, ["row"] = "CTRL-MUTATION", ["cases"] = new JsonArray(g.Select(c => (JsonNode)c.Id).ToArray()),
                    ["o1"] = new JsonArray(members.Select(m => m?["o1"]?.DeepClone()).ToArray()), ["tokens"] = new JsonArray(members.Select(m => m?["token"]?.DeepClone()).ToArray()),
                    ["oracleEqual"] = members.All(m => m != null) && members.Select(m => m!["o1"]?.ToJsonString()).Distinct().Count() == 1,
                };
            }).ToArray());
            mismatches[b] = new JsonArray(ctrl.Where(c => c.RowId is "CTRL-RETRO-845" or "CTRL-RETRO-FIXED" && Token(b, c.Id) != S(templates[c.TemplateId]["expectedOutcome"])).Select(c => (JsonNode)new JsonObject
            {
                ["caseId"] = c.Id, ["expected"] = templates[c.TemplateId]["expectedOutcome"]!.DeepClone(), ["token"] = Token(b, c.Id), ["class"] = final(b, c.Id)?["class"]?.DeepClone(),
                ["guards"] = templates[c.TemplateId]["guards"]!.DeepClone(),
            }).ToArray());
            var width = new JsonArray(ctrl.Where(c => c.RowId == "CTRL-RETRO-845").Select(c =>
            {
                var t = templates[c.TemplateId];
                var p845Expected = S(t["retrospective"]!["p845ExpectedOutcome"]);
                var isWidth = c.TemplateId.Contains("LONG", StringComparison.Ordinal) || c.TemplateId.Contains("UINT", StringComparison.Ordinal);
                return (JsonNode)new JsonObject
                {
                    ["caseId"] = c.Id, ["template"] = c.TemplateId, ["widthCase"] = isWidth, ["p845Expected"] = p845Expected, ["p845Token"] = Token("P845", c.Id),
                    ["expected"] = t["expectedOutcome"]!.DeepClone(), ["token"] = Token(b, c.Id),
                    ["discriminates"] = isWidth && Token("P845", c.Id) == p845Expected && Token(b, c.Id) == S(t["expectedOutcome"]),
                };
            }).ToArray());
            var p845Pass = availability["P845"]!["pass"]!.GetValue<bool>();
            retro[b] = new JsonObject { ["p845AvailabilityPass"] = p845Pass, ["discriminates"] = p845Pass && width.Any(w => w!["discriminates"]!.GetValue<bool>()), ["cases"] = width };
        }
        return new JsonObject { ["solverAvailability"] = availability, ["mutationPairs"] = mutation, ["controlMismatches"] = mismatches, ["retro845"] = retro };
    }
}
