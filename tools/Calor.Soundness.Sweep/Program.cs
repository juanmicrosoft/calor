using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.SoundnessRegistration;

namespace Calor.Soundness.Sweep;

// #1311 sweep driver: run, report, reclassify (optional O2 re-replay), crossrun, native-check. Follows registration.json "execution" and "budget".
internal static partial class Program
{
    private const int CaseWallClockSeconds = 120, Reserve = 74, ExecutionCeiling = 1500;
    internal static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly string[] Pair = ["B1", "N1"];

    public static int Main(string[] args)
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i + 1 < args.Length; i += 2) o[args[i].TrimStart('-')] = args[i + 1];
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return args.FirstOrDefault() switch
        {
            "run" => Run(o), "report" => Report.Write(o["repo"], o["out"]), "reclassify" => Reclassify(o),
            "native-check" => NativeCheck(o), "crossrun" => CrossRun(o),
            _ => Fail("usage: run|report|reclassify|crossrun|native-check --repo R --out O --b1 DIR --n1 DIR --p845 DIR ...", 2),
        };
    }

    private static string? _out; // set by run/crossrun: invalid-run reasons are persisted there and honored by report
    private static int Fail(string message, int code) { Console.Error.WriteLine(message); if (_out != null && code is 4 or 5 or 6) File.AppendAllText(Path.Combine(_out, "invalid-run.jsonl"), new JsonObject { ["utc"] = DateTime.UtcNow.ToString("O"), ["reason"] = message }.ToJsonString() + "\n"); return code; }
    internal static string? Native(JsonNode? pins, string dir) => pins?["processLoadedZ3"]?.AsArray().FirstOrDefault(x => S(x!["path"]).StartsWith(dir, StringComparison.Ordinal))?["sha256"]?.GetValue<string>();

    internal static string S(JsonNode? n) => n!.GetValue<string>();

    internal static (JsonNode Registration, Dictionary<string, JsonNode> Templates, IReadOnlyList<SweepCaseGenerator.Case> Cases, Dictionary<string, RowInfo> Rows) Load(string repo)
    {
        var packet = Path.Combine(repo, "docs/plans/evidence/r1-1419");
        var registration = JsonNode.Parse(File.ReadAllText(Path.Combine(packet, "registration.json")))!;
        var templates = JsonNode.Parse(File.ReadAllText(Path.Combine(packet, "templates.json")))!;
        var rows = registration["denominator"]!["rows"]!.AsArray().Select((r, i) => new RowInfo(S(r!["id"]), S(r["classification"]),
            r["releaseCritical"]!.GetValue<bool>(), S(r["channel"]), r["frontEndRefusalCodes"]?.AsArray().Select(S).ToArray(), i)).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var byId = templates["templates"]!.AsArray().ToDictionary(t => S(t!["id"]), t => t!, StringComparer.Ordinal);
        byId["$oracleControls"] = templates["oracleControls"]!;
        return (registration, byId, SweepCaseGenerator.Generate(registration, templates), rows);
    }

    private static Dictionary<string, SweepCaseGenerator.Case> ById(IReadOnlyList<SweepCaseGenerator.Case> cases) => cases.ToDictionary(c => c.Id, StringComparer.Ordinal);

    private static string Scratch(string tag) { var d = Path.Combine(Path.GetTempPath(), $"r1-{tag}-{Environment.ProcessId}"); Directory.CreateDirectory(d); return d; }

    private static int Run(Dictionary<string, string> opts)
    {
        var repo = Path.GetFullPath(opts["repo"]);
        var outRoot = _out = Path.GetFullPath(opts["out"]);
        var (_, templates, cases, rows) = Load(repo);
        Directory.CreateDirectory(outRoot);
        var scratch = Scratch("sweep");
        // Preflight: the generator reproduces the frozen case index; the 13 oracle controls hold on this machine.
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "docs/plans/evidence/r1-1419/cases-index.json")))!["cases"]!.AsArray();
        if (cases.Count != index.Count || cases.Zip(index).Any(p => p.First.Id != S(p.Second!["id"]) || p.First.CalorSha256 != S(p.Second["calorSha256"]) || p.First.OracleSha256 != S(p.Second["oracleSha256"])))
            return Fail("PREFLIGHT FAIL: case index mismatch", 3);
        var oracleControls = OracleControls(templates["$oracleControls"]);
        File.WriteAllText(Path.Combine(outRoot, "oracle-controls.json"), oracleControls.ToJsonString(Indented));
        if (!oracleControls["pass"]!.GetValue<bool>())
            return Fail("PREFLIGHT FAIL: oracle control mismatch (invalid run)", 3);
        // Pins before the first case; a missing or changed pin (including the native solver image) invalidates the run.
        var hosts = new[] { "B1", "N1", "P845" }.ToDictionary(id => id, id => new BaselineHost(id, opts[id.ToLowerInvariant()]));
        string PinsPath(string id) => Path.Combine(outRoot, id, "pins.json");
        string[] pinKeys = ["calorDllSha256", "calorRuntimeDllSha256", "microsoftZ3DllSha256"];
        foreach (var (id, host) in hosts)
        {
            Directory.CreateDirectory(Path.Combine(outRoot, id));
            var pins = Pins(host, opts["registration-commit"]);
            if (!File.Exists(PinsPath(id)))
                File.WriteAllText(PinsPath(id), pins.ToJsonString(Indented));
            var pinned = JsonNode.Parse(File.ReadAllText(PinsPath(id)))!;
            if (pinKeys.Append("dotnetVersion").Append("os").Any(k => pinned[k]?.ToJsonString() != pins[k]?.ToJsonString() || pinned[k] == null)
                || Native(pinned, host.Directory) is not { } native || native != Native(pins, host.Directory))
                return Fail($"INVALID RUN: {id} pin missing or changed before the first case", 4);
        }
        var ledger = new Ledger(Path.Combine(outRoot, "ledger.jsonl"));
        var byRow = cases.GroupBy(c => c.RowId).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var sw = Stopwatch.StartNew();
        void Exec(string b, SweepCaseGenerator.Case c, string bucket) => ExecuteWithRetry(hosts[b], c, rows[c.RowId], templates[c.TemplateId], bucket, ledger, outRoot, scratch);
        bool AnyCliContradiction(string b) => File.ReadLines(Path.Combine(outRoot, b, "case-results.jsonl")).Any(l => JsonNode.Parse(l)!["cliContradiction"]!.GetValue<bool>());
        // 1. Solver availability on B1 and N1 (alternating per case), then 2. the other controls.
        var availability = byRow["CTRL-POSITIVE"].Concat(byRow["CTRL-NEGATIVE"]).ToList();
        foreach (var c in availability) foreach (var b in Pair) Exec(b, c, "controls");
        foreach (var b in Pair)
            if (!AvailabilityHolds(outRoot, b, availability, templates)) return Fail($"INVALID RUN: {b} solver-availability control mismatch", 5);
        foreach (var c in new[] { "CTRL-MUTATION", "CTRL-RETRO-845", "CTRL-RETRO-FIXED" }.SelectMany(r => byRow[r])) foreach (var b in Pair) Exec(b, c, "controls");
        foreach (var b in Pair)
            if (AnyCliContradiction(b)) return Fail($"INVALID RUN: {b} CLI cross-check contradiction on a control", 6);
        // 3. P845: availability (charged to the reserve), then the six #845 cases.
        foreach (var c in availability) Exec("P845", c, "reserve-p845-availability");
        if (AvailabilityHolds(outRoot, "P845", availability, templates))
            foreach (var c in byRow["CTRL-RETRO-845"]) Exec("P845", c, "p845");
        else
            Console.Error.WriteLine("P845 solver-availability mismatch: P845 run invalid; the #845 control cannot discriminate");
        // 4. Sweep: round-robin by case index across rows (registration row order), B1 and N1 alternating.
        var sweepRows = rows.Values.OrderBy(r => r.Order).Where(r => !r.Id.StartsWith("CTRL-", StringComparison.Ordinal) && byRow.ContainsKey(r.Id)).ToList();
        for (var k = 0; k < sweepRows.Max(r => byRow[r.Id].Count); k++)
            foreach (var r in sweepRows.Where(r => byRow[r.Id].Count > k))
                foreach (var b in Pair)
                {
                    if (ledger.Total >= ExecutionCeiling) { Console.Error.WriteLine("STOP: case-execution ceiling reached"); goto done; }
                    Exec(b, byRow[r.Id][k], "sweep");
                }
        done:
        foreach (var (id, host) in hosts)
        {
            var after = Pins(host, opts["registration-commit"]);
            var before = JsonNode.Parse(File.ReadAllText(PinsPath(id)))!;
            if (pinKeys.Append("dotnetVersion").Any(k => before[k]?.ToJsonString() != after[k]?.ToJsonString() || before[k] == null) || Native(before, host.Directory) != Native(after, host.Directory))
                return Fail($"INVALID RUN: {id} pin missing or changed after the run", 4);
        }
        File.AppendAllText(Path.Combine(outRoot, "sessions.jsonl"), new JsonObject
        { ["endedUtc"] = DateTime.UtcNow.ToString("O"), ["wallClockSeconds"] = Math.Round(sw.Elapsed.TotalSeconds, 1), ["executionsAfter"] = ledger.Total }.ToJsonString() + "\n");
        Console.WriteLine($"done: {ledger.Total} executions, reserve used {ledger.ReserveUsed}, {sw.Elapsed}");
        return 0;
    }

    private static void ExecuteWithRetry(BaselineHost host, SweepCaseGenerator.Case c, RowInfo row, JsonNode template, string bucket, Ledger ledger, string outRoot, string scratch)
    {
        var finalPath = Path.Combine(outRoot, host.Id, "case-results.jsonl");
        if (File.Exists(finalPath) && File.ReadLines(finalPath).Select(l => JsonNode.Parse(l)!).Any(n => S(n["caseId"]) == c.Id && S(n["bucket"]) == bucket))
            return; // resume: already executed
        var attempts = new List<JsonObject> { Attempt(host, c, row, template, bucket, 1, ledger, outRoot, scratch) };
        string? retryNotRun = null;
        var first = attempts[0];
        if (S(first["status"]) is "crashed" or "over-time" || first["claim"]?["token"]?.GetValue<string>() == "TimeoutOrUnavailable")
        {
            if (ledger.ReserveUsed < Reserve - ledger.ReservedAhead && ledger.Total < ExecutionCeiling)
                attempts.Add(Attempt(host, c, row, template, "reserve-retry", 2, ledger, outRoot, scratch));
            else retryNotRun = "reserve exhausted";
        }
        var final = Combine(c, attempts);
        (final["bucket"], final["retryNotRun"]) = (bucket, retryNotRun);
        File.AppendAllText(finalPath, final.ToJsonString() + "\n");
        Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} {host.Id} {c.Id} {final["token"]} {final["o1"]} -> {final["class"]} {string.Join(",", final["addedFindings"]!.AsArray())}");
    }

    private static JsonObject Attempt(BaselineHost host, SweepCaseGenerator.Case c, RowInfo row, JsonNode template, string bucket, int n, Ledger ledger, string outRoot, string scratch)
    {
        var started = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() =>
        {
            try { return CaseExecutor.Execute(host, c, row, template, scratch); }
            catch (Exception ex) { return new JsonObject { ["baseline"] = host.Id, ["caseId"] = c.Id, ["status"] = "harness-crash", ["crash"] = ex.ToString() }; }
        });
        JsonObject record;
        if (task.Wait(TimeSpan.FromSeconds(CaseWallClockSeconds))) record = task.Result;
        else
        {
            // In-process work cannot be killed: wait (bounded) so a retry never overlaps it, and keep what it produced.
            record = new JsonObject { ["baseline"] = host.Id, ["caseId"] = c.Id, ["status"] = "over-time" };
            record["lateObservation"] = task.Wait(TimeSpan.FromMinutes(10)) ? task.Result : null;
        }
        if (row.Id.StartsWith("CTRL-", StringComparison.Ordinal) && record["status"]?.GetValue<string>() == "executed")
            record["cliCrosscheck"] = CliCrosscheck.Run(host, c, record, scratch);
        (record["attempt"], record["bucket"], record["startedUtc"]) = (n, bucket, started.ToString("O"));
        (record["endedUtc"], record["durationMs"]) = (DateTime.UtcNow.ToString("O"), sw.ElapsedMilliseconds);
        var file = Path.Combine(outRoot, host.Id, "attempts", c.Id, $"{bucket}-attempt-{n}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, record.ToJsonString(Indented));
        ledger.Add(host.Id, c.Id, n, bucket, started, sw.ElapsedMilliseconds, Path.GetRelativePath(outRoot, file));
        if (!task.IsCompleted) throw new InvalidOperationException($"{c.Id} did not end; attempt retained, session stopped (no overlapping retry)");
        return record;
    }

    // Combines the attempts of one case (registration execution.retry).
    private static JsonObject Combine(SweepCaseGenerator.Case c, List<JsonObject> attempts)
    {
        static string? Token(JsonObject a) => a["claim"]?["token"]?.GetValue<string>();
        static string Class(JsonObject a) => S(a["status"]) switch { "executed" => S(a["class"]), "crashed" or "over-time" => "crashed", _ => "harness-invalid" };
        var last = attempts[^1];
        var cls = attempts.Select(a => a["lateObservation"] as JsonObject ?? a).Any(a => a["status"]?.GetValue<string>() == "executed" && Class(a) == "false-unconditional-proof") ? "false-unconditional-proof"
            : attempts.Count == 2 && (Token(attempts[0]) != Token(attempts[1]) || S(attempts[0]["status"]) != S(attempts[1]["status"])) ? "flaky"
            : attempts.All(a => Token(a) == "TimeoutOrUnavailable") ? "timed-out"
            : Class(last);
        var added = attempts.SelectMany(a => a["addedFindings"]?.AsArray().Select(S) ?? []).Distinct().Order(StringComparer.Ordinal);
        return new JsonObject
        {
            ["caseId"] = c.Id, ["rowId"] = c.RowId, ["templateId"] = c.TemplateId, ["baseline"] = last["baseline"]!.DeepClone(), ["claim"] = c.Claim,
            ["exhaustive"] = c.Exhaustive, ["token"] = Token(last), ["tokens"] = new JsonArray(attempts.Select(a => (JsonNode?)Token(a)).ToArray()),
            ["o1"] = last["o1"]?["Kind"]?.DeepClone(), ["o1Reached"] = last["o1"]?["Reached"]?.DeepClone(), ["class"] = cls,
            ["classReason"] = last["classReason"]?.DeepClone(), ["addedFindings"] = new JsonArray(added.Select(x => (JsonNode)x).ToArray()),
            ["guards"] = last["guards"]?.DeepClone(), ["coldToken"] = last["coldToken"]?.DeepClone(),
            ["cliContradiction"] = attempts.Any(a => a["cliCrosscheck"]?["contradiction"]?.GetValue<bool>() == true),
            ["attempts"] = new JsonArray(attempts.Select(a => (JsonNode)new JsonObject
            {
                ["attempt"] = a["attempt"]!.DeepClone(), ["bucket"] = a["bucket"]!.DeepClone(), ["status"] = a["status"]!.DeepClone(), ["token"] = Token(a),
                ["class"] = a["class"]?.DeepClone(), ["startedUtc"] = a["startedUtc"]!.DeepClone(), ["durationMs"] = a["durationMs"]!.DeepClone(),
                ["path"] = $"attempts/{c.Id}/{a["bucket"]}-attempt-{a["attempt"]}.json",
            }).ToArray()),
        };
    }

    private static bool AvailabilityHolds(string outRoot, string b, List<SweepCaseGenerator.Case> availability, Dictionary<string, JsonNode> templates)
    {
        var results = File.ReadLines(Path.Combine(outRoot, b, "case-results.jsonl")).Select(l => JsonNode.Parse(l)!).ToList();
        var bad = availability.Select(c => (c, r: results.LastOrDefault(x => S(x["caseId"]) == c.Id), e: S(templates[c.TemplateId]["expectedOutcome"])))
            .Where(x => x.r?["token"]?.GetValue<string>() != x.e || x.r["cliContradiction"]!.GetValue<bool>()).ToList();
        bad.ForEach(x => Console.Error.WriteLine($"{b} {x.c.Id}: expected {x.e}, got {x.r?["token"]}"));
        return bad.Count == 0;
    }

    // Same expansion as SoundnessRegistrationTests.OracleControlsProduceTheirRegisteredVerdicts.
    private static JsonObject OracleControls(JsonNode controls)
    {
        var results = new JsonArray();
        foreach (var control in controls.AsArray())
        {
            var template = control!["template"]!.DeepClone();
            (template["id"], template["row"], template["calor"]) = (S(control["id"]), "ORACLE-CONTROL", "");
            if (control["overflow"] is { } overflow) template["overflow"] = S(overflow);
            var verdict = IndependentOracle.Evaluate(SweepCaseGenerator.Expand(template, S(control["id"]), 0, new SweepCaseGenerator.SplitMix64(1419)).OracleSource);
            var ok = verdict.Kind == S(control["expect"]) && (control["witness"] is not { } w || verdict.Witness == S(w))
                && (control["violationKind"] is not { } k || verdict.ViolationKind == S(k));
            results.Add(new JsonObject { ["id"] = control["id"]!.DeepClone(), ["expect"] = control["expect"]!.DeepClone(), ["got"] = verdict.Kind, ["witness"] = verdict.Witness, ["ok"] = ok });
        }
        return new JsonObject { ["executedUtc"] = DateTime.UtcNow.ToString("O"), ["pass"] = results.All(r => r!["ok"]!.GetValue<bool>()), ["controls"] = results };
    }

    internal static JsonObject Pins(BaselineHost host, string registrationCommit)
    {
        string? Hash(string relative) { var p = Path.Combine(host.Directory, relative); return File.Exists(p) ? Hashing.Sha256File(p) : null; }
        var available = host.Z3Available; // forces the native solver to load so the mapped image can be pinned
        return new JsonObject
        {
            ["baseline"] = host.Id, ["registrationCommit"] = registrationCommit, ["binaryDirectory"] = host.Directory,
            ["calorDllSha256"] = Hash("calor.dll"), ["calorRuntimeDllSha256"] = Hash("Calor.Runtime.dll"), ["microsoftZ3DllSha256"] = Hash("Microsoft.Z3.dll"),
            ["z3Available"] = available, ["nativeZ3Path"] = host.NativeZ3Path, ["nativeZ3Sha256"] = host.NativeZ3Path == null ? null : Hashing.Sha256File(host.NativeZ3Path),
            ["processLoadedZ3"] = new JsonArray(Capture("lsof", $"-p {Environment.ProcessId} -Fn").Split('\n').Where(l => l.StartsWith('n')).Select(l => l[1..])
                .Where(f => f.Contains("libz3", StringComparison.OrdinalIgnoreCase) && File.Exists(f)).Distinct()
                .Select(f => (JsonNode)new JsonObject { ["path"] = f, ["sha256"] = Hashing.Sha256File(f) }).ToArray()),
            ["translatorSemanticsVersion"] = host.TranslatorSemanticsVersion, ["dotnetVersion"] = Capture("dotnet", "--version").Trim(),
            ["runtime"] = RuntimeInformation.FrameworkDescription, ["os"] = RuntimeInformation.OSDescription, ["rid"] = RuntimeInformation.RuntimeIdentifier,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(), ["CALOR_NO_TYPE_CHECK"] = Environment.GetEnvironmentVariable("CALOR_NO_TYPE_CHECK"),
            ["culture"] = "en-US for O1/O2", ["recordedUtc"] = DateTime.UtcNow.ToString("O"),
        };
    }

    private static string Capture(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true })!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return text;
    }

    // noPooling.crossBaselineUse: each finding case is re-run on the other baseline from the reserve, false proofs first, until the execution
    // ceiling. These executions never enter row status.
    private static int CrossRun(Dictionary<string, string> opts)
    {
        var outRoot = _out = Path.GetFullPath(opts["out"]);
        var (_, templates, cases, rows) = Load(Path.GetFullPath(opts["repo"]));
        var (caseById, ledger, scratch) = (ById(cases), new Ledger(Path.Combine(outRoot, "ledger.jsonl")), Scratch("cross"));
        var hosts = Pair.ToDictionary(id => id, id => new BaselineHost(id, opts[id.ToLowerInvariant()]));
        bool Drift() => hosts.Values.Any(h => Pins(h, "") is var now && JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, h.Id, "pins.json"))) is var then && (Native(now, h.Directory) is null || Native(now, h.Directory) != Native(then, h.Directory)
            || new[] { "calorDllSha256", "calorRuntimeDllSha256", "microsoftZ3DllSha256", "dotnetVersion", "os" }.Any(k => now[k]?.ToJsonString() != then![k]?.ToJsonString())));
        if (Drift()) return Fail("INVALID: a pin (binary, native image, or SDK) differs before crossrun", 4);
        string[] priority = ["false-unconditional-proof", "stale-cache-proof", "required-demotion-absent", "spurious-refutation"];
        var work = Pair.SelectMany(source => JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, source, "findings-index.json")))!.AsArray()
            .GroupBy(f => S(f!["caseId"])).Select(g => (Rank: g.Min(f => Array.IndexOf(priority, S(f!["class"])) is var i and >= 0 ? i : priority.Length), Target: source == "B1" ? "N1" : "B1", CaseId: g.Key)));
        foreach (var (_, target, caseId) in work.OrderBy(w => w.Rank).ThenBy(w => w.CaseId, StringComparer.Ordinal).ThenBy(w => w.Target, StringComparer.Ordinal))
        {
            var path = Path.Combine(outRoot, target, "cross-baseline.jsonl");
            if (File.Exists(path) && File.ReadLines(path).Any(l => S(JsonNode.Parse(l)!["caseId"]) == caseId)) continue;
            var c = caseById[caseId];
            var line = new JsonObject { ["caseId"] = caseId, ["baseline"] = target, ["status"] = "not-run", ["reason"] = "1,500-execution ceiling reached" };
            if (ledger.Total < ExecutionCeiling && Attempt(hosts[target], c, rows[c.RowId], templates[c.TemplateId], "reserve-cross-baseline", 1, ledger, outRoot, scratch) is var a)
                line = new JsonObject
                {
                    ["caseId"] = caseId, ["baseline"] = target, ["status"] = a["status"]!.DeepClone(), ["token"] = a["claim"]?["token"]?.DeepClone(), ["class"] = a["class"]?.DeepClone(),
                    ["addedFindings"] = a["addedFindings"]?.DeepClone(), ["path"] = $"attempts/{caseId}/reserve-cross-baseline-attempt-1.json",
                };
            File.AppendAllText(path, line.ToJsonString() + "\n");
        }
        if (Drift()) return Fail("INVALID: a pin (binary, native image, or SDK) differs after crossrun", 4);
        Console.WriteLine($"ledger {ledger.Total}, reserve used {ledger.ReserveUsed}");
        return 0;
    }

    // Loads the three baselines in the run's order and layout and records the libz3 images mapped (no case executed).
    private static int NativeCheck(Dictionary<string, string> opts)
    {
        var result = new JsonObject { ["recordedUtc"] = DateTime.UtcNow.ToString("O") };
        foreach (var host in new[] { "B1", "N1", "P845" }.Select(id => new BaselineHost(id, opts[id.ToLowerInvariant()])))
            result[host.Id] = host.Compile("§M{m1:R1Native}\n  §F{f1:Probe:pub} (i32:x) -> i32\n    §E{}\n    §S (== x x)\n    §R INT:0\n", false, true, null) is not null ? Pins(host, opts["registration-commit"]) : null;
        File.WriteAllText(opts["out-file"], result.ToJsonString(Indented));
        return 0;
    }

    // Re-applies the claim mapping, guard observation, and classification to every retained attempt without calling any compiler; with --rereplay,
    // R1-O2 is re-run from the retained forced emission and the pinned Calor.Runtime.dll. Run-time values are kept (runtimeClassification, runtimeO2).
    private static int Reclassify(Dictionary<string, string> opts)
    {
        var outRoot = Path.GetFullPath(opts["out"]);
        var (_, templates, cases, rows) = Load(Path.GetFullPath(opts["repo"]));
        var caseById = ById(cases);
        foreach (var b in new[] { "B1", "N1", "P845" })
        {
            var path = Path.Combine(outRoot, b, "case-results.jsonl");
            var rebuilt = new List<string>();
            foreach (var old in File.ReadLines(path).Select(l => JsonNode.Parse(l)!.AsObject()).ToList())
            {
                var c = caseById[S(old["caseId"])];
                var t = templates[c.TemplateId];
                var attempts = new List<JsonObject>();
                foreach (var file in old["attempts"]!.AsArray().Select(a => Path.Combine(outRoot, b, S(a!["path"]))))
                {
                    var record = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                    attempts.Add(record);
                    if (record["status"]?.GetValue<string>() != "executed") continue;
                    var snapshot = new JsonObject { ["harnessCommit"] = opts["runtime-harness"] };
                    foreach (var k in new[] { "claim", "forcedClaim", "guards", "class", "classReason", "addedFindings", "nonVacuityCheck", "coldToken" }) snapshot[k] = record[k]?.DeepClone();
                    record["runtimeClassification"] ??= snapshot;
                    var o1 = JsonSerializer.Deserialize<IndependentOracle.Verdict>(record["o1"]!.ToJsonString())!;
                    var (elided, forced) = CaseExecutor.Emissions(record);
                    if (opts.ContainsKey("rereplay"))
                    {
                        if (Hashing.Sha256File(Path.Combine(opts[b.ToLowerInvariant()], "Calor.Runtime.dll")) != S(JsonNode.Parse(File.ReadAllText(Path.Combine(outRoot, b, "pins.json")))!["calorRuntimeDllSha256"])) return Fail("INVALID: runtime differs from pins", 4);
                        record["runtimeO2"] ??= record["o2"]?.DeepClone();
                        record["o2"] = CaseExecutor.Replay(c, t, o1, S(forced["emitted"]), Path.Combine(opts[b.ToLowerInvariant()], "Calor.Runtime.dll"), S(t["claimSite"]), t["obligationKind"]?.GetValue<string>());
                        record["o2ReplayedBy"] = opts["harness"];
                    }
                    CaseExecutor.Adjudicate(record, c, rows[c.RowId], t, o1, elided, forced);
                    record["reclassifiedBy"] = opts["harness"];
                    File.WriteAllText(file, record.ToJsonString(Indented));
                }
                var final = Combine(c, attempts);
                (final["bucket"], final["retryNotRun"]) = (old["bucket"]!.DeepClone(), old["retryNotRun"]?.DeepClone());
                if (S(old["class"]) != S(final["class"]) || old["addedFindings"]!.ToJsonString() != final["addedFindings"]!.ToJsonString())
                    Console.WriteLine($"{b} {c.Id}: {old["class"]} {old["addedFindings"]!.ToJsonString()} -> {final["class"]} {final["addedFindings"]!.ToJsonString()}");
                rebuilt.Add(final.ToJsonString() + "\n");
            }
            File.WriteAllText(path, string.Concat(rebuilt));
        }
        return 0;
    }

    // Every case execution, in order. The 14-case P845 availability check (reserve) runs after the controls, so it is held back from retries.
    internal sealed class Ledger
    {
        private readonly string _path;
        private int _p845Availability;
        public int Total { get; private set; }
        public int ReserveUsed { get; private set; }
        public int ReservedAhead => 14 - _p845Availability;
        public Ledger(string path) { _path = path; if (File.Exists(path)) foreach (var line in File.ReadLines(path)) Count(S(JsonNode.Parse(line)!["bucket"])); }
        private void Count(string bucket)
        {
            Total++;
            if (bucket.StartsWith("reserve", StringComparison.Ordinal)) ReserveUsed++;
            _p845Availability = bucket == "p845" ? 14 : _p845Availability + (bucket == "reserve-p845-availability" ? 1 : 0);
        }
        public void Add(string baseline, string caseId, int attempt, string bucket, DateTime started, long ms, string file)
        {
            Count(bucket);
            File.AppendAllText(_path, new JsonObject { ["seq"] = Total, ["baseline"] = baseline, ["caseId"] = caseId, ["attempt"] = attempt, ["bucket"] = bucket,
                ["startedUtc"] = started.ToString("O"), ["durationMs"] = ms, ["file"] = file }.ToJsonString() + "\n");
        }
    }
}

// CH-CLI-CROSSCHECK (CTRL-* cases only): the baseline CLI must not contradict the in-process channel.
internal static partial class CliCrosscheck
{
    private static string S(JsonNode? n) => Program.S(n);

    [GeneratedRegex(@"(\d+) proven, (\d+) unproven, (\d+) potentially violated, (\d+) unsupported")]
    private static partial Regex Summary();

    public static JsonObject Run(BaselineHost host, SweepCaseGenerator.Case c, JsonObject attempt, string scratch)
    {
        var dir = Path.Combine(scratch, "cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "home"));
        var input = Path.Combine(dir, "case.calr");
        File.WriteAllText(input, c.CalorSource);
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir };
        foreach (var a in new[] { Path.Combine(host.Directory, "calor.dll"), "--input", input, "--output", Path.Combine(dir, "case.g.cs"), "--verify", "--verbose", "--format", "json", "--no-telemetry" })
            psi.ArgumentList.Add(a);
        psi.Environment.Remove("CALOR_NO_TYPE_CHECK");
        psi.Environment["HOME"] = Path.Combine(dir, "home"); // fresh, empty verification cache (the in-process channel runs with the cache off)
        var record = new JsonObject { ["command"] = "calor " + string.Join(" ", psi.ArgumentList.Skip(1)), ["home"] = "fresh per case" };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000)) { p.Kill(true); record["status"] = "over-time"; record["contradiction"] = false; return record; }
        var stdout = stdoutTask.Result;
        (record["exitCode"], record["stdout"]) = (p.ExitCode, stdout);
        JsonNode? json = null;
        try { var start = stdout.IndexOf('{'); if (start >= 0) json = JsonNode.Parse(stdout[start..]); } catch (JsonException) { }
        if (json == null) { record["status"] = "unparseable"; record["contradiction"] = false; return record; }
        var contradictions = new JsonArray();
        var legacy = attempt["channels"]!["CH-CONTRACT"]!["elided"]!["contracts"]?.AsArray()
            .SelectMany(f => f!["preconditions"]!.AsArray().Concat(f["postconditions"]!.AsArray())).Select(r => S(r!["legacyStatus"])).ToList() ?? [];
        var summary = json["diagnostics"]?.AsArray().Select(d => Summary().Match(d!["message"]?.GetValue<string>() ?? "")).FirstOrDefault(m => m.Success);
        if (summary != null)
        {
            var cli = Enumerable.Range(1, 4).Select(i => int.Parse(summary.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
            var inproc = new[] { "Proven", "Unproven", "Disproven", "Unsupported" }.Select(s => legacy.Count(x => x == s)).ToArray();
            (record["summaryCli"], record["summaryInProcess"]) = (string.Join(",", cli), string.Join(",", inproc));
            if (!cli.SequenceEqual(inproc)) contradictions.Add($"summary cli [{record["summaryCli"]}] vs in-process [{record["summaryInProcess"]}]");
        }
        var claimLine = c.CalorSource.Split('\n').Select((l, i) => (l, i)).Where(x => x.l.TrimStart().StartsWith("§S ", StringComparison.Ordinal)).Select(x => x.i + 1).ToList();
        var token = attempt["claim"]?["token"]?.GetValue<string>();
        foreach (var d in json["diagnostics"]?.AsArray() ?? [])
        {
            var status = d!["verification"]?["status"]?.GetValue<string>();
            var line = d["location"]?["line"]?.GetValue<int>();
            if (status == null || line == null || claimLine.Count != 1 || line != claimLine[0]) continue;
            var mapped = status switch
            {
                "proven" => d["verification"]!["vacuous"]?.GetValue<bool>() == true ? "ProvenVacuous" : "Proven", "refuted" => "Failed", "assumed" => "Assumed",
                "unsupported" => "Unsupported", "timeout" or "unknown" or "unavailable" => "TimeoutOrUnavailable", _ => "UNMAPPED:" + status,
            };
            record["claimCli"] = mapped;
            if (mapped != token && !(mapped == "Proven" && token == "ProvenVacuous")) contradictions.Add($"claim line {line}: cli {mapped} vs in-process {token}");
        }
        (record["status"], record["contradictions"], record["contradiction"]) = ("compared", contradictions, contradictions.Count > 0);
        return record;
    }
}
