using System.Text.Json;
using System.Text.Json.Nodes;

namespace Calor.Soundness.Sweep;

/// <summary>
/// Mechanical row status, findings, and controls from the retained case results (registration
/// caseResults.rowStatus, findingRecord, controls). Each baseline is computed only from its own
/// results; nothing is pooled.
/// </summary>
internal static class Report
{
    internal static readonly HashSet<string> FindingClasses =
    [
        "false-unconditional-proof", "false-proof-unconfirmed-vacuity", "required-demotion-absent", "guard-elided-without-proof",
        "vacuity-mislabel", "spurious-refutation", "spurious-model", "stale-cache-proof", "semantic-divergence",
        "control-mismatch", "candidate-unconfirmed",
    ];

    private static readonly HashSet<string> Blocking = ["harness-invalid", "flaky", "crashed", "unconfirmed-claim", "timed-out"];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static int Write(string repo, string outRoot)
    {
        var (registration, templates, cases, rows) = Program.Load(Path.GetFullPath(repo));
        var templateById = templates["templates"]!.AsArray().ToDictionary(t => t!["id"]!.GetValue<string>(), t => t!, StringComparer.Ordinal);
        var results = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        foreach (var b in new[] { "B1", "N1", "P845" })
        {
            var path = Path.Combine(outRoot, b, "case-results.jsonl");
            results[b] = File.Exists(path) ? File.ReadLines(path).Select(l => JsonNode.Parse(l)!.AsObject()).ToList() : [];
        }
        JsonObject? Final(string b, string caseId) => results[b].LastOrDefault(r => r["caseId"]!.GetValue<string>() == caseId);

        var controls = Controls(cases, templateById, Final);
        File.WriteAllText(Path.Combine(outRoot, "controls.json"), controls.ToJsonString(Indented));

        var summary = new JsonObject();
        foreach (var b in new[] { "B1", "N1" })
        {
            var other = b == "B1" ? "N1" : "B1";
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, $"docs/plans/evidence/r1-1419/manifest-{b}.json")))!;
            var pins = JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, b, "pins.json")))!;
            var rowsDir = Path.Combine(outRoot, b, "rows");
            var findingsDir = Path.Combine(outRoot, b, "findings");
            Directory.CreateDirectory(rowsDir);
            if (Directory.Exists(findingsDir)) Directory.Delete(findingsDir, true);
            Directory.CreateDirectory(findingsDir);
            var statusRows = new JsonArray();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var findingIndex = new JsonArray();
            var n = 0;
            var nonDiscriminating = controls["retro845"]![b]!["discriminates"]!.GetValue<bool>() == false;
            foreach (var mrow in manifest["rows"]!.AsArray())
            {
                var rowId = mrow!["row"]!.GetValue<string>();
                var row = rows[rowId];
                var lines = new List<string>();
                var classes = new Dictionary<string, int>(StringComparer.Ordinal);
                var unexecuted = 0;
                var findingClasses = new List<string>();
                foreach (var caseId in mrow["caseIds"]!.AsArray().Select(x => x!.GetValue<string>()))
                {
                    var r = Final(b, caseId);
                    if (r == null) { unexecuted++; lines.Add(new JsonObject { ["caseId"] = caseId, ["baseline"] = b, ["class"] = "not-investigated", ["reason"] = "unexecuted" }.ToJsonString()); continue; }
                    lines.Add(r.ToJsonString());
                    var cls = r["class"]!.GetValue<string>();
                    classes[cls] = classes.GetValueOrDefault(cls) + 1;
                    var all = new[] { cls }.Concat(r["addedFindings"]!.AsArray().Select(x => x!.GetValue<string>())).Where(FindingClasses.Contains).Distinct().ToList();
                    foreach (var fc in all)
                    {
                        findingClasses.Add(fc);
                        var id = $"F-{b}-{++n:D3}";
                        var record = Finding(id, b, other, fc, r, pins, registration, outRoot, Final(other, caseId), rowId);
                        File.WriteAllText(Path.Combine(findingsDir, id + ".json"), record.ToJsonString(Indented));
                        findingIndex.Add(new JsonObject { ["findingId"] = id, ["rowId"] = rowId, ["caseId"] = caseId, ["class"] = fc, ["token"] = r["token"]?.DeepClone(), ["o1"] = r["o1"]?.DeepClone() });
                    }
                }
                foreach (var mutation in controls["mutationPairs"]![b]!.AsArray().Where(p => p!["row"]!.GetValue<string>() == rowId && !p["oracleEqual"]!.GetValue<bool>()))
                    findingClasses.Add("control-mismatch");
                File.WriteAllText(Path.Combine(rowsDir, rowId + ".jsonl"), string.Concat(lines.Select(l => l + "\n")));
                string status;
                var reasons = new JsonArray();
                if (findingClasses.Contains("false-unconditional-proof")) status = "FALSE-PROOF";
                else if (findingClasses.Count > 0) status = "FINDING";
                else if (unexecuted > 0 || classes.Keys.Any(Blocking.Contains)) status = "INCOMPLETE";
                else status = "CLEAN-WITHIN-BUDGET";
                if (unexecuted > 0) reasons.Add($"{unexecuted} unexecuted");
                foreach (var k in classes.Keys.Where(Blocking.Contains)) reasons.Add($"{classes[k]} {k}");
                if (nonDiscriminating && rowId is "NUM-LITERAL-TYPED" or "NUM-ARITH-ADD")
                {
                    reasons.Add("#845 discriminating control did not discriminate on this baseline (registration controls.retrospective845.nonDiscriminatingConsequence)");
                    if (status == "CLEAN-WITHIN-BUDGET") status = "INCOMPLETE";
                }
                var guardedBy = controls["controlMismatches"]![b]!.AsArray().Where(m => m!["guards"]!.AsArray().Any(g => g!.GetValue<string>() == rowId)).Select(m => m!["caseId"]!.DeepClone()).ToArray();
                if (row.Classification == "not-investigated") status = "NOT-INVESTIGATED";
                counts[status] = counts.GetValueOrDefault(status) + 1;
                var exhaustive = lines.Count(l => JsonNode.Parse(l)!["exhaustive"]?.GetValue<bool>() == true);
                statusRows.Add(new JsonObject
                {
                    ["id"] = $"{b}:{rowId}", ["row"] = rowId, ["classification"] = row.Classification, ["releaseCritical"] = row.ReleaseCritical,
                    ["status"] = status, ["blockedAtTimebox"] = status == "INCOMPLETE" && row.ReleaseCritical,
                    ["allocated"] = mrow["caseIds"]!.AsArray().Count, ["unexecuted"] = unexecuted,
                    ["classCounts"] = JsonSerializer.SerializeToNode(classes.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary()),
                    ["findingClasses"] = new JsonArray(findingClasses.Distinct().Select(x => (JsonNode)x).ToArray()),
                    ["exhaustiveDomains"] = exhaustive, ["reasons"] = reasons,
                    ["controlMismatchesGuardingThisRow"] = new JsonArray(guardedBy),
                    ["resultPath"] = mrow["resultPath"]!.DeepClone(),
                });
            }
            var rowStatus = new JsonObject { ["baseline"] = b, ["counts"] = JsonSerializer.SerializeToNode(counts.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary()), ["rows"] = statusRows };
            File.WriteAllText(Path.Combine(outRoot, b, "row-status.json"), rowStatus.ToJsonString(Indented));
            File.WriteAllText(Path.Combine(outRoot, b, "findings-index.json"), findingIndex.ToJsonString(Indented));
            summary[b] = new JsonObject { ["rowStatusCounts"] = rowStatus["counts"]!.DeepClone(), ["findings"] = findingIndex.Count };
        }
        File.WriteAllText(Path.Combine(outRoot, "summary.json"), summary.ToJsonString(Indented));
        Console.WriteLine(summary.ToJsonString(Indented));
        return 0;
    }

    private static JsonObject Finding(string id, string b, string other, string cls, JsonObject r, JsonNode pins, JsonNode registration,
        string outRoot, JsonObject? otherResult, string rowId)
    {
        var lastAttempt = r["attempts"]!.AsArray()[^1]!["path"]!.GetValue<string>();
        var attempt = JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, b, lastAttempt)))!;
        var claim = attempt["claim"];
        var o1 = attempt["o1"];
        var witness = o1?["Witness"]?.GetValue<string>();
        var o2Run = attempt["o2"]?["runs"]?.AsArray().FirstOrDefault(x => witness != null && witness.StartsWith(x!["input"]!.GetValue<string>(), StringComparison.Ordinal));
        string otherResultText = otherResult == null ? "not-run"
            : (otherResult["class"]!.GetValue<string>() == cls || otherResult["addedFindings"]!.AsArray().Any(x => x!.GetValue<string>() == cls)) ? "reproduces" : "does-not-reproduce";
        return new JsonObject
        {
            ["findingId"] = id,
            ["baseline"] = b,
            ["registrationCommit"] = pins["registrationCommit"]!.DeepClone(),
            ["rowId"] = rowId,
            ["caseId"] = r["caseId"]!.DeepClone(),
            ["templateId"] = r["templateId"]!.DeepClone(),
            ["caseSha256"] = attempt["caseSha256"]?.DeepClone(),
            ["class"] = cls,
            ["claim"] = new JsonObject
            {
                ["site"] = attempt["claimSite"]?.DeepClone(),
                ["token"] = claim?["token"]?.DeepClone(),
                ["rawStatus"] = claim?["rawStatus"]?.DeepClone(),
                ["isVacuous"] = claim?["isVacuous"]?.DeepClone(),
                ["assumptions"] = claim?["assumptions"]?.DeepClone(),
                ["guardInElidedEmission"] = attempt["guards"]?["guardInElidedEmission"]?.DeepClone(),
                ["guardInForcedEmission"] = attempt["guards"]?["guardInForcedEmission"]?.DeepClone(),
            },
            ["witness"] = new JsonObject
            {
                ["inputs"] = witness ?? claim?["modelReplay"]?["input"]?.DeepClone()?.ToString(),
                ["o1"] = $"{o1?["Kind"]} {o1?["ViolationKind"]}".Trim(),
                ["o2"] = o2Run == null ? (attempt["o2"]?["status"]?.GetValue<string>() ?? "not-run") : o2Run["o2"]!.GetValue<string>() == "guard-threw" ? "guard threw" : "did not throw",
            },
            ["nonVacuityCheck"] = attempt["nonVacuityCheck"]?.DeepClone(),
            ["classReason"] = r["classReason"]?.DeepClone(),
            ["minimized"] = new JsonObject { ["calorSource"] = null, ["csharpReproduction"] = null, ["sha256"] = null, ["status"] = "pending" },
            ["environment"] = new JsonObject
            {
                ["calorDllSha256"] = pins["calorDllSha256"]!.DeepClone(),
                ["z3NativeSha256"] = pins["nativeZ3Sha256"]?.DeepClone(),
                ["dotnetVersion"] = pins["dotnetVersion"]!.DeepClone(),
                ["os"] = pins["os"]!.DeepClone(),
                ["translatorSemanticsVersion"] = pins["translatorSemanticsVersion"]?.DeepClone(),
            },
            ["attempts"] = r["attempts"]!.DeepClone(),
            ["otherBaseline"] = new JsonObject { ["id"] = other, ["result"] = otherResultText, ["source"] = "the other baseline's own execution of the same frozen case", ["otherClass"] = otherResult?["class"]?.DeepClone(), ["otherToken"] = otherResult?["token"]?.DeepClone() },
            ["affectedGuarantee"] = null,
            ["filedIssue"] = null,
            ["disposition"] = null,
        };
    }

    private static JsonObject Controls(IReadOnlyList<Calor.Compiler.Tests.SoundnessRegistration.SweepCaseGenerator.Case> cases,
        Dictionary<string, JsonNode> templateById, Func<string, string, JsonObject?> final)
    {
        var result = new JsonObject();
        var availability = new JsonObject();
        var mutation = new JsonObject();
        var retro = new JsonObject();
        var mismatches = new JsonObject();
        var controlCases = cases.Where(c => c.RowId.StartsWith("CTRL-", StringComparison.Ordinal)).ToList();
        foreach (var b in new[] { "B1", "N1", "P845" })
        {
            var avail = new JsonArray();
            foreach (var c in controlCases.Where(c => c.RowId is "CTRL-POSITIVE" or "CTRL-NEGATIVE"))
            {
                var r = final(b, c.Id);
                var expected = templateById[c.TemplateId]["expectedOutcome"]!.GetValue<string>();
                avail.Add(new JsonObject { ["caseId"] = c.Id, ["expected"] = expected, ["token"] = r?["token"]?.DeepClone(), ["ok"] = r?["token"]?.GetValue<string>() == expected });
            }
            availability[b] = new JsonObject { ["pass"] = avail.All(x => x!["ok"]!.GetValue<bool>()), ["cases"] = avail };
            if (b == "P845") continue;
            var pairs = new JsonArray();
            foreach (var g in controlCases.Where(c => c.RowId == "CTRL-MUTATION").GroupBy(c => templateById[c.TemplateId]["mutationPair"]!.GetValue<string>()))
            {
                var members = g.Select(c => final(b, c.Id)).ToList();
                pairs.Add(new JsonObject
                {
                    ["pair"] = g.Key, ["row"] = "CTRL-MUTATION",
                    ["cases"] = new JsonArray(g.Select(c => (JsonNode)c.Id).ToArray()),
                    ["o1"] = new JsonArray(members.Select(m => m?["o1"]?.DeepClone()).ToArray()),
                    ["tokens"] = new JsonArray(members.Select(m => m?["token"]?.DeepClone()).ToArray()),
                    ["oracleEqual"] = members.All(m => m != null) && members.Select(m => m!["o1"]?.ToJsonString()).Distinct().Count() == 1,
                });
            }
            mutation[b] = pairs;
            var mm = new JsonArray();
            foreach (var c in controlCases.Where(c => c.RowId is "CTRL-RETRO-845" or "CTRL-RETRO-FIXED"))
            {
                var r = final(b, c.Id);
                var t = templateById[c.TemplateId];
                if (r == null || r["token"]?.GetValue<string>() != t["expectedOutcome"]!.GetValue<string>())
                    mm.Add(new JsonObject { ["caseId"] = c.Id, ["expected"] = t["expectedOutcome"]!.DeepClone(), ["token"] = r?["token"]?.DeepClone(), ["class"] = r?["class"]?.DeepClone(), ["guards"] = t["guards"]!.DeepClone() });
            }
            mismatches[b] = mm;
            var width = new JsonArray();
            foreach (var c in controlCases.Where(c => c.RowId == "CTRL-RETRO-845"))
            {
                var t = templateById[c.TemplateId];
                var p = final("P845", c.Id);
                var r = final(b, c.Id);
                var p845Expected = t["retrospective"]!["p845ExpectedOutcome"]!.GetValue<string>();
                var isWidth = c.TemplateId.Contains("LONG", StringComparison.Ordinal) || c.TemplateId.Contains("UINT", StringComparison.Ordinal);
                width.Add(new JsonObject
                {
                    ["caseId"] = c.Id, ["template"] = c.TemplateId, ["widthCase"] = isWidth,
                    ["p845Expected"] = p845Expected, ["p845Token"] = p?["token"]?.DeepClone(),
                    ["expected"] = t["expectedOutcome"]!.DeepClone(), ["token"] = r?["token"]?.DeepClone(),
                    ["discriminates"] = isWidth && p?["token"]?.GetValue<string>() == p845Expected && r?["token"]?.GetValue<string>() == t["expectedOutcome"]!.GetValue<string>(),
                });
            }
            retro[b] = new JsonObject
            {
                ["p845AvailabilityPass"] = availability["P845"]!["pass"]!.DeepClone(),
                ["discriminates"] = availability["P845"]!["pass"]!.GetValue<bool>() && width.Any(w => w!["discriminates"]!.GetValue<bool>()),
                ["cases"] = width,
            };
        }
        result["solverAvailability"] = availability;
        result["mutationPairs"] = mutation;
        result["controlMismatches"] = mismatches;
        result["retro845"] = retro;
        return result;
    }
}
