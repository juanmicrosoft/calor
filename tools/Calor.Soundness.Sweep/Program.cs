using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.SoundnessRegistration;

namespace Calor.Soundness.Sweep;

/// <summary>
/// #1311 sweep driver. Commands:
///   run    --repo R --out O --b1 DIR --n1 DIR --p845 DIR --registration-commit SHA
///   report --repo R --out O
/// Execution order, retries, budget, and invalid-run checks follow registration.json "execution" and "budget".
/// </summary>
internal static partial class Program
{
    private const int CaseWallClockSeconds = 120;
    private const int Reserve = 74;
    private const int ExecutionCeiling = 1500;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i + 1 < args.Length; i += 2)
            opts[args[i].TrimStart('-')] = args[i + 1];
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return args.FirstOrDefault() switch
        {
            "run" => Run(opts),
            "report" => Report.Write(opts["repo"], opts["out"]),
            "selftest" => SelfTest(opts),
            "reclassify" => Reclassify(opts),
            "native-check" => NativeCheck(opts),
            "crossrun" => CrossRun(opts),
            _ => Usage(),
        };
    }

    /// <summary>
    /// noPooling.crossBaselineUse: each finding case is re-run on the other baseline from the reserve
    /// and recorded as that baseline's own result, in priority order (false proofs first) until the
    /// 1,500-execution ceiling. These executions never enter row status (cross-baseline.jsonl).
    /// </summary>
    private static int CrossRun(Dictionary<string, string> opts)
    {
        var repo = Path.GetFullPath(opts["repo"]);
        var outRoot = Path.GetFullPath(opts["out"]);
        var (_, templates, cases, rows) = Load(repo);
        var templateById = templates["templates"]!.AsArray().ToDictionary(t => t!["id"]!.GetValue<string>(), t => t!, StringComparer.Ordinal);
        var caseById = cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var hosts = new[] { "B1", "N1" }.ToDictionary(id => id, id => new BaselineHost(id, opts[id.ToLowerInvariant()]));
        var ledger = new Ledger(Path.Combine(outRoot, "ledger.jsonl"));
        var scratch = Path.Combine(Path.GetTempPath(), "r1-cross-" + Environment.ProcessId);
        Directory.CreateDirectory(scratch);
        string[] priority = ["false-unconditional-proof", "stale-cache-proof", "required-demotion-absent", "spurious-refutation"];
        var work = new List<(int Rank, string Target, string CaseId)>();
        foreach (var source in new[] { "B1", "N1" })
            foreach (var g in JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, source, "findings-index.json")))!.AsArray().GroupBy(f => f!["caseId"]!.GetValue<string>()))
                work.Add((g.Min(f => Array.IndexOf(priority, f!["class"]!.GetValue<string>()) is var i and >= 0 ? i : priority.Length), source == "B1" ? "N1" : "B1", g.Key));
        foreach (var (_, target, caseId) in work.OrderBy(w => w.Rank).ThenBy(w => w.CaseId, StringComparer.Ordinal).ThenBy(w => w.Target, StringComparer.Ordinal))
        {
            var path = Path.Combine(outRoot, target, "cross-baseline.jsonl");
            if (File.Exists(path) && File.ReadLines(path).Any(l => JsonNode.Parse(l)!["caseId"]!.GetValue<string>() == caseId))
                continue;
            var c = caseById[caseId];
            JsonObject line;
            if (ledger.Total >= ExecutionCeiling)
                line = new JsonObject { ["caseId"] = caseId, ["baseline"] = target, ["status"] = "not-run", ["reason"] = "1,500-execution ceiling reached" };
            else
            {
                var a = Attempt(hosts[target], c, rows[c.RowId], templateById[c.TemplateId], "reserve-cross-baseline", 1, ledger, outRoot, scratch);
                line = new JsonObject
                {
                    ["caseId"] = caseId, ["baseline"] = target, ["status"] = a["status"]!.DeepClone(), ["token"] = a["claim"]?["token"]?.DeepClone(),
                    ["class"] = a["class"]?.DeepClone(), ["addedFindings"] = a["addedFindings"]?.DeepClone(),
                    ["path"] = $"attempts/{caseId}/reserve-cross-baseline-attempt-1.json",
                };
            }
            File.AppendAllText(path, line.ToJsonString() + "\n");
            Console.WriteLine($"{target} {caseId}: {line["status"]} {line["token"]} {line["class"]}");
        }
        Console.WriteLine($"ledger {ledger.Total}, reserve used {ledger.ReserveUsed}");
        return 0;
    }

    /// <summary>
    /// Loads the three baselines in the same order and process layout as "run", forces the solver to
    /// load, and records every libz3 image the process actually mapped (no case is executed).
    /// </summary>
    private static int NativeCheck(Dictionary<string, string> opts)
    {
        var result = new JsonObject { ["recordedUtc"] = DateTime.UtcNow.ToString("O") };
        foreach (var id in new[] { "B1", "N1", "P845" })
        {
            var host = new BaselineHost(id, opts[id.ToLowerInvariant()]);
            host.Compile("§M{m1:R1Native}\n  §F{f1:Probe:pub} (i32:x) -> i32\n    §E{}\n    §S (== x x)\n    §R INT:0\n", false, true, null);
            result[id] = Pins(host, opts["registration-commit"]);
        }
        File.WriteAllText(opts["out-file"], result.ToJsonString(Indented));
        Console.WriteLine(result.ToJsonString(Indented));
        return 0;
    }

    /// <summary>
    /// Re-applies the (corrected) claim mapping, guard observation, and classification to every
    /// retained attempt without calling any compiler: observations (diagnostics, outcomes, emitted
    /// code, O1, O2) are unchanged. The run-time classification is preserved in each attempt under
    /// "runtimeClassification", and case-results.jsonl is rebuilt from the attempts.
    /// </summary>
    private static int Reclassify(Dictionary<string, string> opts)
    {
        var repo = Path.GetFullPath(opts["repo"]);
        var outRoot = Path.GetFullPath(opts["out"]);
        var (_, templates, cases, rows) = Load(repo);
        var templateById = templates["templates"]!.AsArray().ToDictionary(t => t!["id"]!.GetValue<string>(), t => t!, StringComparer.Ordinal);
        var caseById = cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var b in new[] { "B1", "N1", "P845" })
        {
            var path = Path.Combine(outRoot, b, "case-results.jsonl");
            var rebuilt = new List<string>();
            foreach (var line in File.ReadLines(path))
            {
                var old = JsonNode.Parse(line)!.AsObject();
                var c = caseById[old["caseId"]!.GetValue<string>()];
                var attempts = new List<JsonObject>();
                foreach (var a in old["attempts"]!.AsArray())
                {
                    var file = Path.Combine(outRoot, b, a!["path"]!.GetValue<string>());
                    var record = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                    if (record["status"]?.GetValue<string>() == "executed")
                    {
                        record["runtimeClassification"] ??= new JsonObject
                        {
                            ["harnessCommit"] = opts["runtime-harness"],
                            ["claim"] = record["claim"]?.DeepClone(), ["forcedClaim"] = record["forcedClaim"]?.DeepClone(),
                            ["guards"] = record["guards"]?.DeepClone(), ["class"] = record["class"]?.DeepClone(),
                            ["classReason"] = record["classReason"]?.DeepClone(), ["addedFindings"] = record["addedFindings"]?.DeepClone(),
                            ["nonVacuityCheck"] = record["nonVacuityCheck"]?.DeepClone(), ["coldToken"] = record["coldToken"]?.DeepClone(),
                        };
                        var o1 = JsonSerializer.Deserialize<IndependentOracle.Verdict>(record["o1"]!.ToJsonString())!;
                        var (elided, forced) = CaseExecutor.Emissions(record);
                        if (opts.ContainsKey("rereplay"))
                        {
                            // R1-O2 re-run from the retained forced emission and the pinned Calor.Runtime.dll
                            // (no compiler call); the run-time O2 record is kept as runtimeO2.
                            record["runtimeO2"] ??= record["o2"]?.DeepClone();
                            var t = templateById[c.TemplateId];
                            var runtimeDll = Path.Combine(opts[b.ToLowerInvariant()], "Calor.Runtime.dll");
                            record["o2"] = CaseExecutor.Replay(c, t, o1, forced["emitted"]!.GetValue<string>(), runtimeDll,
                                t["claimSite"]!.GetValue<string>(), t["obligationKind"]?.GetValue<string>());
                            record["o2ReplayedBy"] = opts["harness"];
                        }
                        CaseExecutor.Adjudicate(record, c, rows[c.RowId], templateById[c.TemplateId], o1, elided, forced);
                        record["reclassifiedBy"] = opts["harness"];
                        File.WriteAllText(file, record.ToJsonString(Indented));
                    }
                    attempts.Add(record);
                }
                var final = Combine(c, attempts);
                final["bucket"] = old["bucket"]!.DeepClone();
                final["retryNotRun"] = old["retryNotRun"]?.DeepClone();
                if (old["class"]!.GetValue<string>() != final["class"]!.GetValue<string>()
                    || old["addedFindings"]!.ToJsonString() != final["addedFindings"]!.ToJsonString())
                    Console.WriteLine($"{b} {c.Id}: {old["class"]} {old["addedFindings"]!.ToJsonString()} -> {final["class"]} {final["addedFindings"]!.ToJsonString()}");
                rebuilt.Add(final.ToJsonString());
            }
            File.WriteAllText(path, string.Concat(rebuilt.Select(l => l + "\n")));
        }
        return 0;
    }

    /// <summary>
    /// Harness self-test on executor-written synthetic inputs (selftest-templates.json): never a
    /// registered case, never counted as a case execution, never used for any row status.
    /// </summary>
    private static int SelfTest(Dictionary<string, string> opts)
    {
        var outDir = Path.GetFullPath(opts["out"]);
        Directory.CreateDirectory(outDir);
        var scratch = Path.Combine(Path.GetTempPath(), "r1-selftest-" + Environment.ProcessId);
        Directory.CreateDirectory(scratch);
        var file = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "selftest-templates.json")))!;
        var hosts = opts.Where(kv => kv.Key is "b1" or "n1" or "p845").Select(kv => new BaselineHost(kv.Key.ToUpperInvariant(), kv.Value)).ToList();
        foreach (var t in file["templates"]!.AsArray())
        {
            var c = SweepCaseGenerator.Expand(t!, "SELFTEST-" + t!["id"]!.GetValue<string>(), 0, new SweepCaseGenerator.SplitMix64(1311));
            var row = new RowInfo(t!["row"]!.GetValue<string>(), t["classification"]!.GetValue<string>(), false, "selftest", null, 0);
            foreach (var host in hosts)
            {
                var record = CaseExecutor.Execute(host, c, row, t, scratch);
                if (record["status"]?.GetValue<string>() == "executed" && t["claimSite"]!.GetValue<string>() is "postcondition" && !c.RowId.StartsWith("CACHE", StringComparison.Ordinal))
                    record["cliCrosscheck"] = CliCrosscheck.Run(host, c, record, scratch);
                File.WriteAllText(Path.Combine(outDir, $"{host.Id}-{c.Id}.json"), record.ToJsonString(Indented));
                Console.WriteLine($"{host.Id} {c.Id}: token={record["claim"]?["token"]} o1={record["o1"]?["Kind"]} class={record["class"]} added=[{string.Join(",", record["addedFindings"]?.AsArray() ?? [])}] reason={record["classReason"]} guards={record["guards"]?.ToJsonString()} o2={record["o2"]?["status"]} cli={record["cliCrosscheck"]?["contradiction"]}");
            }
        }
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: run --repo R --out O --b1 DIR --n1 DIR --p845 DIR --registration-commit SHA | report --repo R --out O");
        return 2;
    }

    internal static (JsonNode Registration, JsonNode Templates, IReadOnlyList<SweepCaseGenerator.Case> Cases, Dictionary<string, RowInfo> Rows) Load(string repo)
    {
        var packet = Path.Combine(repo, "docs/plans/evidence/r1-1419");
        var registration = JsonNode.Parse(File.ReadAllText(Path.Combine(packet, "registration.json")))!;
        var templates = JsonNode.Parse(File.ReadAllText(Path.Combine(packet, "templates.json")))!;
        var cases = SweepCaseGenerator.Generate(registration, templates);
        var rows = registration["denominator"]!["rows"]!.AsArray().Select((r, i) => new RowInfo(
                r!["id"]!.GetValue<string>(), r["classification"]!.GetValue<string>(), r["releaseCritical"]!.GetValue<bool>(),
                r["channel"]!.GetValue<string>(), r["frontEndRefusalCodes"]?.AsArray().Select(x => x!.GetValue<string>()).ToArray(), i))
            .ToDictionary(r => r.Id, StringComparer.Ordinal);
        return (registration, templates, cases, rows);
    }

    private static int Run(Dictionary<string, string> opts)
    {
        var repo = Path.GetFullPath(opts["repo"]);
        var outRoot = Path.GetFullPath(opts["out"]);
        var (registration, templates, cases, rows) = Load(repo);
        var templateById = templates["templates"]!.AsArray().ToDictionary(t => t!["id"]!.GetValue<string>(), t => t!, StringComparer.Ordinal);
        Directory.CreateDirectory(outRoot);
        var scratch = Path.Combine(Path.GetTempPath(), "r1-sweep-" + Environment.ProcessId);
        Directory.CreateDirectory(scratch);

        // Preflight 1: the generator reproduces the frozen case index byte for byte.
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "docs/plans/evidence/r1-1419/cases-index.json")))!["cases"]!.AsArray();
        var mismatched = cases.Zip(index).Where(p => p.First.Id != p.Second!["id"]!.GetValue<string>()
            || p.First.CalorSha256 != p.Second["calorSha256"]!.GetValue<string>()
            || p.First.OracleSha256 != p.Second["oracleSha256"]!.GetValue<string>()).Select(p => p.First.Id).ToList();
        if (cases.Count != index.Count || mismatched.Count > 0)
        {
            Console.Error.WriteLine($"PREFLIGHT FAIL: case index mismatch ({mismatched.Count}): {string.Join(",", mismatched.Take(5))}");
            return 3;
        }

        // Preflight 2: the 13 oracle controls produce their registered verdicts on this machine.
        var oracleControls = OracleControls(templates);
        File.WriteAllText(Path.Combine(outRoot, "oracle-controls.json"), oracleControls.ToJsonString(Indented));
        if (oracleControls["pass"]!.GetValue<bool>() != true)
        {
            Console.Error.WriteLine("PREFLIGHT FAIL: oracle control mismatch (invalid run)");
            return 3;
        }

        // Pins and hosts.
        var hosts = new Dictionary<string, BaselineHost>(StringComparer.Ordinal);
        foreach (var id in new[] { "B1", "N1", "P845" })
        {
            hosts[id] = new BaselineHost(id, opts[id.ToLowerInvariant()]);
            Directory.CreateDirectory(Path.Combine(outRoot, id));
        }
        var ledger = new Ledger(Path.Combine(outRoot, "ledger.jsonl"));
        var pinsPath = (string id) => Path.Combine(outRoot, id, "pins.json");
        foreach (var (id, host) in hosts)
        {
            var pins = Pins(host, opts["registration-commit"]);
            if (File.Exists(pinsPath(id)))
            {
                var previous = JsonNode.Parse(File.ReadAllText(pinsPath(id)))!;
                foreach (var key in new[] { "calorDllSha256", "calorRuntimeDllSha256", "microsoftZ3DllSha256", "dotnetVersion", "os" })
                    if (previous[key]?.ToJsonString() != pins[key]?.ToJsonString())
                    {
                        Console.Error.WriteLine($"INVALID RUN: {id} pin {key} changed ({previous[key]} -> {pins[key]})");
                        return 4;
                    }
            }
            else
                File.WriteAllText(pinsPath(id), pins.ToJsonString(Indented));
        }

        foreach (var id in hosts.Keys)
            if (JsonNode.Parse(File.ReadAllText(pinsPath(id)))!["processLoadedZ3"] is not JsonArray { Count: > 0 })
            {
                Console.Error.WriteLine($"INVALID RUN: {id} native solver image not pinned before the first case");
                return 4;
            }

        var byRow = cases.GroupBy(c => c.RowId).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var sw = Stopwatch.StartNew();
        CaseResult Exec(string baseline, SweepCaseGenerator.Case c, string bucket) =>
            ExecuteWithRetry(hosts[baseline], c, rows[c.RowId], templateById[c.TemplateId], bucket, ledger, outRoot, scratch);

        // 1. Solver availability (CTRL-POSITIVE, CTRL-NEGATIVE) on B1 and N1, alternating per case.
        var availability = byRow["CTRL-POSITIVE"].Concat(byRow["CTRL-NEGATIVE"]).ToList();
        foreach (var c in availability)
            foreach (var b in new[] { "B1", "N1" })
                Exec(b, c, "controls");
        foreach (var b in new[] { "B1", "N1" })
            if (!AvailabilityHolds(outRoot, b, availability, templateById))
            {
                Console.Error.WriteLine($"INVALID RUN: {b} solver-availability control mismatch");
                return 5;
            }

        // 2. The other verifier controls, alternating per case.
        foreach (var rowId in new[] { "CTRL-MUTATION", "CTRL-RETRO-845", "CTRL-RETRO-FIXED" })
            foreach (var c in byRow[rowId])
                foreach (var b in new[] { "B1", "N1" })
                    Exec(b, c, "controls");

        // CH-CLI-CROSSCHECK: a contradiction on any control makes the run invalid (harness defect, not a finding).
        foreach (var b in new[] { "B1", "N1" })
            if (File.ReadLines(Path.Combine(outRoot, b, "case-results.jsonl")).Any(l => JsonNode.Parse(l)!["cliContradiction"]!.GetValue<bool>()))
            {
                Console.Error.WriteLine($"INVALID RUN: {b} CLI cross-check contradiction on a control");
                return 6;
            }

        // 3. P845: availability check (charged to the reserve), then the six #845 cases.
        foreach (var c in availability)
            Exec("P845", c, "reserve-p845-availability");
        if (!AvailabilityHolds(outRoot, "P845", availability, templateById))
            Console.Error.WriteLine("P845 solver-availability mismatch: P845 run invalid; the #845 control cannot discriminate");
        else
            foreach (var c in byRow["CTRL-RETRO-845"])
                Exec("P845", c, "p845");

        // 4. Sweep: round-robin by case index across rows (registration row order), B1 and N1 alternating.
        var sweepRows = rows.Values.OrderBy(r => r.Order).Where(r => !r.Id.StartsWith("CTRL-", StringComparison.Ordinal) && byRow.ContainsKey(r.Id)).ToList();
        var depth = sweepRows.Max(r => byRow[r.Id].Count);
        for (var k = 0; k < depth; k++)
            foreach (var r in sweepRows.Where(r => byRow[r.Id].Count > k))
                foreach (var b in new[] { "B1", "N1" })
                {
                    if (ledger.Total >= ExecutionCeiling)
                    {
                        Console.Error.WriteLine("STOP: case-execution ceiling reached");
                        goto done;
                    }
                    Exec(b, byRow[r.Id][k], "sweep");
                }
        done:

        // Pins re-checked after the last case: the binaries are immutable for the whole sweep.
        foreach (var (id, host) in hosts)
        {
            var after = Pins(host, opts["registration-commit"]);
            var before = JsonNode.Parse(File.ReadAllText(pinsPath(id)))!;
            foreach (var key in new[] { "calorDllSha256", "calorRuntimeDllSha256", "microsoftZ3DllSha256" })
                if (before[key]?.ToJsonString() != after[key]?.ToJsonString() || before[key] == null)
                {
                    Console.Error.WriteLine($"INVALID RUN: {id} pin {key} missing or changed after the run");
                    return 4;
                }
        }
        File.AppendAllText(Path.Combine(outRoot, "sessions.jsonl"), new JsonObject
        {
            ["endedUtc"] = DateTime.UtcNow.ToString("O"),
            ["wallClockSeconds"] = Math.Round(sw.Elapsed.TotalSeconds, 1),
            ["executionsAfter"] = ledger.Total,
        }.ToJsonString() + "\n");
        Console.WriteLine($"done: {ledger.Total} executions, reserve used {ledger.ReserveUsed}, {sw.Elapsed}");
        return 0;
    }

    internal sealed record CaseResult(string Baseline, string CaseId, JsonObject Final);

    private static CaseResult ExecuteWithRetry(BaselineHost host, SweepCaseGenerator.Case c, RowInfo row, JsonNode template,
        string bucket, Ledger ledger, string outRoot, string scratch)
    {
        var finalPath = Path.Combine(outRoot, host.Id, "case-results.jsonl");
        var existing = File.Exists(finalPath) ? File.ReadLines(finalPath).Select(l => JsonNode.Parse(l)!.AsObject())
            .FirstOrDefault(n => n["caseId"]!.GetValue<string>() == c.Id && n["bucket"]!.GetValue<string>() == bucket) : null;
        if (existing != null)
            return new CaseResult(host.Id, c.Id, existing);

        var attempts = new List<JsonObject>();
        var first = Attempt(host, c, row, template, bucket, 1, ledger, outRoot, scratch);
        attempts.Add(first);
        string? retryNotRun = null;
        if (NeedsRetry(first))
        {
            if (ledger.ReserveUsed < Reserve - ledger.ReservedAhead && ledger.Total < ExecutionCeiling)
                attempts.Add(Attempt(host, c, row, template, "reserve-retry", 2, ledger, outRoot, scratch));
            else
                retryNotRun = "reserve exhausted";
        }
        var final = Combine(c, attempts);
        final["bucket"] = bucket;
        final["retryNotRun"] = retryNotRun;
        File.AppendAllText(finalPath, final.ToJsonString() + "\n");
        Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} {host.Id} {c.Id} {final["token"]} {final["o1"]} -> {final["class"]} {string.Join(",", final["addedFindings"]!.AsArray())}");
        return new CaseResult(host.Id, c.Id, final);
    }

    private static bool NeedsRetry(JsonObject a) =>
        a["status"]!.GetValue<string>() is "crashed" or "over-time"
        || a["claim"]?["token"]?.GetValue<string>() == "TimeoutOrUnavailable";

    private static JsonObject Attempt(BaselineHost host, SweepCaseGenerator.Case c, RowInfo row, JsonNode template, string bucket,
        int n, Ledger ledger, string outRoot, string scratch)
    {
        var started = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();
        JsonObject record;
        var task = Task.Run(() =>
        {
            try { return CaseExecutor.Execute(host, c, row, template, scratch); }
            catch (Exception ex) { return new JsonObject { ["baseline"] = host.Id, ["caseId"] = c.Id, ["status"] = "harness-crash", ["crash"] = ex.ToString() }; }
        });
        if (task.Wait(TimeSpan.FromSeconds(CaseWallClockSeconds)))
            record = task.Result;
        else
        {
            record = new JsonObject { ["baseline"] = host.Id, ["caseId"] = c.Id, ["status"] = "over-time" };
            // In-process work cannot be killed: wait (bounded) so a retry never overlaps it, and keep its result.
            if (task.Wait(TimeSpan.FromMinutes(10)))
                record["lateObservation"] = task.Result;
        }
        if (row.Id.StartsWith("CTRL-", StringComparison.Ordinal) && record["status"]?.GetValue<string>() == "executed")
            record["cliCrosscheck"] = CliCrosscheck.Run(host, c, record, scratch);
        record["attempt"] = n;
        record["bucket"] = bucket;
        record["startedUtc"] = started.ToString("O");
        record["endedUtc"] = DateTime.UtcNow.ToString("O");
        record["durationMs"] = sw.ElapsedMilliseconds;
        var dir = Path.Combine(outRoot, host.Id, "attempts", c.Id);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{bucket}-attempt-{n}.json");
        File.WriteAllText(file, record.ToJsonString(Indented));
        ledger.Add(host.Id, c.Id, n, bucket, started, sw.ElapsedMilliseconds, Path.GetRelativePath(outRoot, file));
        return record;
    }

    /// <summary>Combines the attempts of one case (registration execution.retry).</summary>
    private static JsonObject Combine(SweepCaseGenerator.Case c, List<JsonObject> attempts)
    {
        static string? Token(JsonObject a) => a["claim"]?["token"]?.GetValue<string>();
        static string Class(JsonObject a) => a["status"]!.GetValue<string>() switch
        {
            "executed" => a["class"]!.GetValue<string>(),
            "crashed" or "over-time" => "crashed",
            _ => "harness-invalid",
        };
        var last = attempts[^1];
        string cls;
        if (attempts.Any(a => a["status"]?.GetValue<string>() == "executed" && Class(a) == "false-unconditional-proof"))
            cls = "false-unconditional-proof";
        else if (attempts.Count == 2 && (Token(attempts[0]) != Token(attempts[1]) || attempts[0]["status"]!.GetValue<string>() != attempts[1]["status"]!.GetValue<string>()))
            cls = "flaky";
        else if (Token(last) == "TimeoutOrUnavailable" && attempts.All(a => Token(a) == "TimeoutOrUnavailable"))
            cls = "timed-out";
        else
            cls = Class(last);
        var added = attempts.SelectMany(a => a["addedFindings"]?.AsArray().Select(x => x!.GetValue<string>()) ?? []).Distinct().Order(StringComparer.Ordinal);
        return new JsonObject
        {
            ["caseId"] = c.Id,
            ["rowId"] = c.RowId,
            ["templateId"] = c.TemplateId,
            ["baseline"] = last["baseline"]!.DeepClone(),
            ["claim"] = c.Claim,
            ["exhaustive"] = c.Exhaustive,
            ["token"] = Token(last),
            ["tokens"] = new JsonArray(attempts.Select(a => (JsonNode?)Token(a)).ToArray()),
            ["o1"] = last["o1"]?["Kind"]?.DeepClone(),
            ["o1Reached"] = last["o1"]?["Reached"]?.DeepClone(),
            ["class"] = cls,
            ["classReason"] = last["classReason"]?.DeepClone(),
            ["addedFindings"] = new JsonArray(added.Select(x => (JsonNode)x).ToArray()),
            ["guards"] = last["guards"]?.DeepClone(),
            ["coldToken"] = last["coldToken"]?.DeepClone(),
            ["cliContradiction"] = attempts.Any(a => a["cliCrosscheck"]?["contradiction"]?.GetValue<bool>() == true),
            ["attempts"] = new JsonArray(attempts.Select(a => (JsonNode)new JsonObject
            {
                ["attempt"] = a["attempt"]!.DeepClone(), ["bucket"] = a["bucket"]!.DeepClone(), ["status"] = a["status"]!.DeepClone(),
                ["token"] = Token(a), ["class"] = a["class"]?.DeepClone(), ["startedUtc"] = a["startedUtc"]!.DeepClone(),
                ["durationMs"] = a["durationMs"]!.DeepClone(),
                ["path"] = $"attempts/{c.Id}/{a["bucket"]}-attempt-{a["attempt"]}.json",
            }).ToArray()),
        };
    }

    private static bool AvailabilityHolds(string outRoot, string baseline, List<SweepCaseGenerator.Case> availability, Dictionary<string, JsonNode> templateById)
    {
        var results = File.ReadLines(Path.Combine(outRoot, baseline, "case-results.jsonl")).Select(l => JsonNode.Parse(l)!).ToList();
        var ok = true;
        foreach (var c in availability)
        {
            var r = results.LastOrDefault(x => x!["caseId"]!.GetValue<string>() == c.Id);
            var expected = templateById[c.TemplateId]["expectedOutcome"]!.GetValue<string>();
            if (r?["token"]?.GetValue<string>() != expected || r["cliContradiction"]!.GetValue<bool>())
            {
                Console.Error.WriteLine($"{baseline} {c.Id}: expected {expected}, got {r?["token"]} (cli contradiction {r?["cliContradiction"]})");
                ok = false;
            }
        }
        return ok;
    }

    private static JsonObject OracleControls(JsonNode templates)
    {
        var results = new JsonArray();
        var pass = true;
        foreach (var control in templates["oracleControls"]!.AsArray())
        {
            // Same expansion as SoundnessRegistrationTests.OracleControlsProduceTheirRegisteredVerdicts.
            var template = control!["template"]!.DeepClone();
            template["id"] = control["id"]!.GetValue<string>();
            template["row"] = "ORACLE-CONTROL";
            template["calor"] = "";
            if (control["overflow"] is { } overflow)
                template["overflow"] = overflow.GetValue<string>();
            var testCase = SweepCaseGenerator.Expand(template, control["id"]!.GetValue<string>(), 0, new SweepCaseGenerator.SplitMix64(1419));
            var verdict = IndependentOracle.Evaluate(testCase.OracleSource);
            var ok = verdict.Kind == control["expect"]!.GetValue<string>()
                && (control["witness"] is not { } w || verdict.Witness == w.GetValue<string>())
                && (control["violationKind"] is not { } k || verdict.ViolationKind == k.GetValue<string>());
            pass &= ok;
            results.Add(new JsonObject { ["id"] = control["id"]!.DeepClone(), ["expect"] = control["expect"]!.DeepClone(), ["got"] = verdict.Kind, ["witness"] = verdict.Witness, ["ok"] = ok });
        }
        return new JsonObject { ["executedUtc"] = DateTime.UtcNow.ToString("O"), ["pass"] = pass, ["controls"] = results };
    }

    private static JsonObject Pins(BaselineHost host, string registrationCommit)
    {
        string? Hash(string relative) { var p = Path.Combine(host.Directory, relative); return File.Exists(p) ? Hashing.Sha256File(p) : null; }
        // Force the native solver to load so its resolved path is pinned.
        var available = host.Z3Available;
        return new JsonObject
        {
            ["baseline"] = host.Id,
            ["registrationCommit"] = registrationCommit,
            ["binaryDirectory"] = host.Directory,
            ["calorDllSha256"] = Hash("calor.dll"),
            ["calorRuntimeDllSha256"] = Hash("Calor.Runtime.dll"),
            ["microsoftZ3DllSha256"] = Hash("Microsoft.Z3.dll"),
            ["z3Available"] = available,
            ["nativeZ3Path"] = host.NativeZ3Path,
            ["nativeZ3Sha256"] = host.NativeZ3Path == null ? null : Hashing.Sha256File(host.NativeZ3Path),
            ["processLoadedZ3"] = new JsonArray(MappedFiles().Where(f => f.Contains("libz3", StringComparison.OrdinalIgnoreCase))
                .Select(f => (JsonNode)new JsonObject { ["path"] = f, ["sha256"] = Hashing.Sha256File(f) }).ToArray()),
            ["translatorSemanticsVersion"] = host.TranslatorSemanticsVersion,
            ["dotnetVersion"] = DotnetVersion(),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = RuntimeInformation.OSDescription,
            ["rid"] = RuntimeInformation.RuntimeIdentifier,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["CALOR_NO_TYPE_CHECK"] = Environment.GetEnvironmentVariable("CALOR_NO_TYPE_CHECK"),
            ["culture"] = "en-US for O1/O2",
            ["recordedUtc"] = DateTime.UtcNow.ToString("O"),
        };
    }

    /// <summary>Files mapped into this process (lsof), used to pin the native solver image actually loaded.</summary>
    private static IEnumerable<string> MappedFiles()
    {
        var psi = new ProcessStartInfo("lsof", $"-p {Environment.ProcessId} -Fn") { RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        var lines = p.StandardOutput.ReadToEnd().Split('\n');
        p.WaitForExit();
        return lines.Where(l => l.StartsWith('n')).Select(l => l[1..]).Where(File.Exists).Distinct().ToList();
    }

    private static string DotnetVersion()
    {
        var psi = new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        var v = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return v;
    }

    internal sealed class Ledger
    {
        private readonly string _path;
        public int Total { get; private set; }
        public int ReserveUsed { get; private set; }

        /// <summary>Reserve executions committed ahead of time: the P845 availability check (14) runs after the controls.</summary>
        public int ReservedAhead => _p845AvailabilityDone ? 0 : 14 - _p845Availability;
        private int _p845Availability;
        private bool _p845AvailabilityDone;

        public Ledger(string path)
        {
            _path = path;
            if (File.Exists(path))
                foreach (var line in File.ReadLines(path))
                    Count(JsonNode.Parse(line)!["bucket"]!.GetValue<string>());
        }

        private void Count(string bucket)
        {
            Total++;
            if (bucket.StartsWith("reserve", StringComparison.Ordinal))
                ReserveUsed++;
            if (bucket == "reserve-p845-availability" && ++_p845Availability == 14)
                _p845AvailabilityDone = true;
            if (bucket == "p845")
                _p845AvailabilityDone = true;
        }

        public void Add(string baseline, string caseId, int attempt, string bucket, DateTime started, long ms, string file)
        {
            Count(bucket);
            File.AppendAllText(_path, new JsonObject
            {
                ["seq"] = Total, ["baseline"] = baseline, ["caseId"] = caseId, ["attempt"] = attempt, ["bucket"] = bucket,
                ["startedUtc"] = started.ToString("O"), ["durationMs"] = ms, ["file"] = file,
            }.ToJsonString() + "\n");
        }
    }
}

/// <summary>CH-CLI-CROSSCHECK (CTRL-* cases only): the baseline CLI must not contradict the in-process channel.</summary>
internal static partial class CliCrosscheck
{
    [GeneratedRegex(@"(\d+) proven, (\d+) unproven, (\d+) potentially violated, (\d+) unsupported")]
    private static partial Regex Summary();

    public static JsonObject Run(BaselineHost host, SweepCaseGenerator.Case c, JsonObject attempt, string scratch)
    {
        var dir = Path.Combine(scratch, "cli-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(dir, "home");
        Directory.CreateDirectory(home);
        var input = Path.Combine(dir, "case.calr");
        File.WriteAllText(input, c.CalorSource);
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir,
        };
        foreach (var a in new[] { Path.Combine(host.Directory, "calor.dll"), "--input", input, "--output", Path.Combine(dir, "case.g.cs"), "--verify", "--verbose", "--format", "json", "--no-telemetry" })
            psi.ArgumentList.Add(a);
        psi.Environment.Remove("CALOR_NO_TYPE_CHECK");
        psi.Environment["HOME"] = home; // fresh, empty verification cache (the in-process channel runs with the cache off)
        var record = new JsonObject { ["command"] = "calor " + string.Join(" ", psi.ArgumentList.Skip(1)), ["home"] = "fresh per case" };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000)) { p.Kill(true); record["status"] = "over-time"; record["contradiction"] = false; return record; }
        var stdout = stdoutTask.Result;
        record["exitCode"] = p.ExitCode;
        record["stdout"] = stdout;
        JsonNode? json = null;
        try { var start = stdout.IndexOf('{'); if (start >= 0) json = JsonNode.Parse(stdout[start..]); } catch (JsonException) { }
        if (json == null) { record["status"] = "unparseable"; record["contradiction"] = false; return record; }

        var contradictions = new JsonArray();
        var elided = (JsonObject)attempt["channels"]!["CH-CONTRACT"]!["elided"]!;
        var legacy = elided["contracts"]?.AsArray().SelectMany(f => f!["preconditions"]!.AsArray().Concat(f["postconditions"]!.AsArray()))
            .Select(r => r!["legacyStatus"]!.GetValue<string>()).ToList() ?? [];
        var summary = json["diagnostics"]?.AsArray().Select(d => d!["message"]?.GetValue<string>() ?? "").Select(m => Summary().Match(m)).FirstOrDefault(m => m.Success);
        if (summary != null)
        {
            var cli = Enumerable.Range(1, 4).Select(i => int.Parse(summary.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
            var inproc = new[] { "Proven", "Unproven", "Disproven", "Unsupported" }.Select(s => legacy.Count(x => x == s)).ToArray();
            record["summaryCli"] = string.Join(",", cli);
            record["summaryInProcess"] = string.Join(",", inproc);
            if (!cli.SequenceEqual(inproc))
                contradictions.Add($"summary cli [{record["summaryCli"]}] vs in-process [{record["summaryInProcess"]}]");
        }
        var claimLine = c.CalorSource.Split('\n').Select((l, i) => (l, i)).Where(x => x.l.TrimStart().StartsWith("§S ", StringComparison.Ordinal)).Select(x => x.i + 1).ToList();
        var token = attempt["claim"]?["token"]?.GetValue<string>();
        foreach (var d in json["diagnostics"]?.AsArray() ?? [])
        {
            var status = d!["verification"]?["status"]?.GetValue<string>();
            var line = d["location"]?["line"]?.GetValue<int>();
            if (status == null || line == null || claimLine.Count != 1 || line != claimLine[0])
                continue;
            var mapped = status switch
            {
                "proven" => d["verification"]!["vacuous"]?.GetValue<bool>() == true ? "ProvenVacuous" : "Proven",
                "refuted" => "Failed", "assumed" => "Assumed", "unsupported" => "Unsupported",
                "timeout" or "unknown" or "unavailable" => "TimeoutOrUnavailable", _ => "UNMAPPED:" + status,
            };
            record["claimCli"] = mapped;
            if (mapped != token && !(mapped == "Proven" && token == "ProvenVacuous"))
                contradictions.Add($"claim line {line}: cli {mapped} vs in-process {token}");
        }
        record["status"] = "compared";
        record["contradictions"] = contradictions;
        record["contradiction"] = contradictions.Count > 0;
        return record;
    }
}
