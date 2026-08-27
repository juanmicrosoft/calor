using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// PP-E1 — the 0.15 effect-rows proof point (annex A-1.11, corrected by
/// A-1.11.1). This is the <b>instrument that reads the outcome</b>:
/// <c>bench/phase0-agent-native/effect-rows-probe-ledger.json</c>, compared by
/// exact equality to a recomputation.
///
/// <para><b>What is recomputed vs recorded</b>, stated so the pin is not
/// overread (the frozen row's own words): <b>leg A is recomputed</b> — each of
/// the ten frozen diffs is re-applied to the frozen fixture, compiled with the
/// pinned flag-free invocation, and its codes and declarations compared to the
/// ledger; the negative control is recomputed against A-1.11.1's post-E4
/// multisets; route (a) (a fixture that does not reach the effect pass) and
/// route (b) (the higher-order demand floor) are recomputed. <b>Leg B is
/// recorded</b> — only its arithmetic is recomputed, by
/// <c>bench/phase0-agent-native/ppe1-analyze.py</c> from the archived per-run
/// <c>result.json</c> files into
/// <c>epochs/e1-rows-parity-001/ppe1-analysis.json</c>; this test reads that
/// file when it exists and never re-runs the epoch. The four-valued verdict is
/// then <b>derived</b> from the frozen outcome map, in its precedence, so the
/// ledger cannot carry a verdict its own numbers do not imply: while leg B is
/// not run the verdict must be NOT-ADJUDICATED, and once it is run the verdict
/// must be exactly what the frozen rules compute.</para>
///
/// <para>Regenerate with <c>CALOR_REGENERATE_PPE1_LEDGER=1 dotnet test
/// --filter EffectRowsProbeLedger</c> — and only in the PR that names what
/// moved. The failure message below says the same thing, because the one
/// mistake A-1.11.1 was written to correct was a baseline quietly regenerated
/// to whatever the compiler happened to emit.</para>
/// </summary>
public sealed class EffectRowsProbeLedgerTests
{
    private const string RegenerateEnvVar = "CALOR_REGENERATE_PPE1_LEDGER";
    private const int SchemaVersion = 1;
    private const string LegBEpoch = "e1-rows-parity-001";
    private const string NotRunReason =
        "leg B epoch e1-rows-parity-001 not yet run; adjudication at the 0.15.0 release commit";

    /// <summary>A-1.11: drift is bounded to the row family — and nothing else.</summary>
    private static readonly string[] DriftSet = ["Calor0424", "Calor0425", "Calor0404", "Calor0405"];

    /// <summary>A-1.11: never detection, whatever they say.</summary>
    private static readonly string[] NeverDetection = ["Calor0418", "Calor0419", "Calor0411"];

    /// <summary>A-1.11: barred anywhere in a control compile.</summary>
    private static readonly string[] BarredInControl = ["Calor0405", "Calor0420", "Calor0421", "Calor0424"];

    /// <summary>
    /// A-1.11.1's registered POST-E4 negative-control baselines, per fixture —
    /// the binding control at adjudication. A2: 1× Calor0410 (23,9) + 2×
    /// Calor0411 (26,24)(28,19), exit 1; the four A3 fixtures: exit 0, zero
    /// diagnostics (A-1.11's words, which A-1.11.1 left standing).
    /// </summary>
    private static readonly (string Fixture, int ExitCode, PpE1BaselineEntry[] Baseline)[] ControlBaselines =
    [
        ("A2", 1,
        [
            new("Calor0410", 1, "m008", "error", 23, 9),
            new("Calor0411", 1, "_chainProcess010.ConfigureAwait", "warning", 26, 24),
            new("Calor0411", 1, "_chainNext011.ConfigureAwait", "warning", 28, 19),
        ]),
        ("A3-map", 0, []),
        ("A3-match", 0, []),
        ("A3-middleware", 0, []),
        ("A3-callback", 0, []),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string RepoRoot() => PpE1Probe.RepositoryRoot();

    private static string LedgerPath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "effect-rows-probe-ledger.json");

    private static string AnalysisRelativePath() =>
        $"bench/phase0-agent-native/epochs/{LegBEpoch}/ppe1-analysis.json";

    private static bool Regenerate() =>
        Environment.GetEnvironmentVariable(RegenerateEnvVar) == "1";

    // ------------------------------------------------------------------ the pin

    /// <summary>
    /// The exact-equality pin A-1.11 registers by name. Leg A recomputed, leg B
    /// recorded, verdict derived — see the class summary.
    /// </summary>
    [Fact]
    public void PpE1LedgerMatchesRecomputation()
    {
        var measurement = Measure();

        if (Regenerate())
        {
            var regenerated = Assemble(measurement, HeadSha(), recordedRouteDFires: false,
                recordedOwnGoal: false);
            File.WriteAllText(LedgerPath(), Serialize(regenerated));
            return;
        }

        Assert.True(File.Exists(LedgerPath()),
            $"PP-E1 ledger missing: {LedgerPath()}. It is the instrument annex A-1.11 registers; "
            + $"generate it in a PR that says so with {RegenerateEnvVar}=1.");

        var actualText = File.ReadAllText(LedgerPath()).Replace("\r\n", "\n", StringComparison.Ordinal);
        var recorded = JsonSerializer.Deserialize<PpE1Ledger>(actualText, JsonOptions);
        Assert.NotNull(recorded);

        AssertCommitSha(recorded!.MeasuredCommit);
        var recomputed = Assemble(measurement, recorded.MeasuredCommit,
            recorded.Routes.D.Fires, recorded.Routes.OwnGoal);
        var expectedText = Serialize(recomputed);

        var moved = DescribeWhatMoved(recorded, recomputed);
        if (expectedText != actualText)
        {
            var (lineNumber, expectedLine, actualLine) = FirstDifferingLine(expectedText, actualText);
            moved.Add($"first differing line {lineNumber}:\n      ledger:      {actualLine}\n      recomputed:  {expectedLine}");
        }

        Assert.True(moved.Count == 0,
            "PP-E1's ledger (bench/phase0-agent-native/effect-rows-probe-ledger.json) no longer "
            + "matches its recomputation. Something moved:\n  " + string.Join("\n  ", moved)
            + "\nThis ledger is the instrument annex A-1.11 registers, adjudicated at the 0.15.0 "
            + "release commit. Do NOT regenerate it silently: a cell that stopped detecting, a "
            + "control that drew a new diagnostic, or a verdict that changed is a finding to be "
            + "published with the change that caused it. Regenerate only in a PR that names what "
            + $"moved and why ({RegenerateEnvVar}=1 dotnet test --filter EffectRowsProbeLedger).");
    }

    /// <summary>
    /// The verdict field is a derivation, not a record: read only the file and
    /// check that its verdict is what its own leg-B state implies under the
    /// frozen outcome map. Independent of the compile, so a ledger hand-edited to
    /// HIT with leg B "not-run" fails here even if the compile leg is skipped.
    /// </summary>
    [Fact]
    public void PpE1Ledger_VerdictFollowsLegBState()
    {
        Assert.True(File.Exists(LedgerPath()), $"PP-E1 ledger missing: {LedgerPath()}");
        var ledger = JsonSerializer.Deserialize<PpE1Ledger>(File.ReadAllText(LedgerPath()), JsonOptions);
        Assert.NotNull(ledger);

        var (verdict, reason) = ComputeVerdict(ledger!.NegativeControl.Clean, ledger.LegA,
            ledger.Routes, ledger.LegB);

        Assert.True(verdict == ledger.Verdict && reason == ledger.Reason,
            $"Ledger verdict '{ledger.Verdict}' / reason '{ledger.Reason}' is not what its recorded "
            + $"state implies: '{verdict}' / '{reason}'. The verdict is derived from the frozen "
            + "outcome map (NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT); it cannot be written by hand.");

        if (ledger.LegB.Status != "run")
        {
            Assert.Equal("NOT-ADJUDICATED", ledger.Verdict);
            Assert.Null(ledger.LegB.PointEstimate);
            Assert.Null(ledger.LegB.LowerBound95);
            Assert.Null(ledger.LegB.RealizedMedianWithinCellCv);
        }
    }

    /// <summary>
    /// A-1.11 verified every anchor to occur exactly once at registration; a
    /// fixture edit that made one ambiguous would silently change which text the
    /// diff replaces.
    /// </summary>
    [Fact]
    public void PpE1Catalogue_EveryAnchorOccursExactlyOnce()
    {
        Assert.Equal(10, PpE1Probe.Catalogue.Length);
        foreach (var cell in PpE1Probe.Catalogue.Where(c => c.Anchor is not null))
        {
            var source = File.ReadAllText(PpE1Probe.FixturePath(cell.Fixture));
            Assert.True(PpE1Probe.CountOccurrences(source, cell.Anchor!) == 1,
                $"{cell.Id}: the registered anchor must occur exactly once in {cell.Fixture}.calr");
        }
    }

    // ------------------------------------------------------------ measurement

    private sealed record Measurement(
        Dictionary<string, (int ExitCode, PpE1Diagnostic[] Diagnostics)> Controls,
        List<(PpE1Mutation Cell, int ExitCode, PpE1Diagnostic[] Diagnostics, int AnchorOccurrences)> Mutants);

    private static Measurement Measure()
    {
        var controls = new Dictionary<string, (int, PpE1Diagnostic[])>(StringComparer.Ordinal);
        foreach (var (fixture, expectedSha) in PpE1Probe.FrozenFixtures)
        {
            var path = PpE1Probe.FixturePath(fixture);
            Assert.True(File.Exists(path), $"PP-E1 fixture missing: {path}");
            Assert.True(PpE1Probe.GitBlobSha1(path) == expectedSha,
                $"PP-E1 fixture '{fixture}' no longer has the blob SHA A-1.11 froze ({expectedSha}).");
            controls[fixture] = PpE1Probe.Compile(path);
        }

        var mutants = new List<(PpE1Mutation, int, PpE1Diagnostic[], int)>();
        foreach (var cell in PpE1Probe.Catalogue)
        {
            if (cell.CommittedMutant is not null)
            {
                var committed = PpE1Probe.FixturePath(cell.CommittedMutant);
                Assert.True(PpE1Probe.GitBlobSha1(committed) == PpE1Probe.A2BroadeningBlobSha,
                    $"{cell.Id}: the committed mutant {cell.CommittedMutant}.calr no longer has the "
                    + $"blob SHA A-1.11 froze ({PpE1Probe.A2BroadeningBlobSha}).");
                var (exit, diagnostics) = PpE1Probe.Compile(committed);
                mutants.Add((cell, exit, diagnostics, 0));
                continue;
            }

            var source = File.ReadAllText(PpE1Probe.FixturePath(cell.Fixture));
            var occurrences = PpE1Probe.CountOccurrences(source, cell.Anchor!);
            Assert.True(occurrences == 1,
                $"{cell.Id}: the registered anchor occurs {occurrences} times in {cell.Fixture}.calr; "
                + "A-1.11 registered exactly once.");

            var mutant = Path.Combine(Path.GetTempPath(), $"calor-ppe1-{cell.Id}-{Guid.NewGuid():N}.calr");
            try
            {
                File.WriteAllText(mutant,
                    source.Replace(cell.Anchor!, cell.Replacement!, StringComparison.Ordinal));
                var (exit, diagnostics) = PpE1Probe.Compile(mutant);
                mutants.Add((cell, exit, diagnostics, occurrences));
            }
            finally
            {
                if (File.Exists(mutant)) File.Delete(mutant);
            }
        }

        return new Measurement(controls, mutants);
    }

    // --------------------------------------------------------------- assembly

    private static PpE1Ledger Assemble(Measurement m, string measuredCommit,
        bool recordedRouteDFires, bool recordedOwnGoal)
    {
        var control = AssembleControl(m);
        var legA = AssembleLegA(m);
        var routes = AssembleRoutes(m, legA, recordedRouteDFires, recordedOwnGoal);
        var legB = ReadLegB();
        var (verdict, reason) = ComputeVerdict(control.Clean, legA, routes, legB);

        return new PpE1Ledger(
            SchemaVersion,
            "PP-E1",
            "docs/plans/agent-native-gates.md §A.2 row PP-E1 + §A.3 entry A-1.11 (2026-08-25); "
            + "leg-A negative control re-frozen by sub-entry A-1.11.1 (2026-08-26)",
            measuredCommit,
            "dotnet <calor.dll> -i <source> -o <scratch>; no flags (--permissive-effects and any "
            + "manifest outside the built-in set are forbidden); diagnostics sorted by "
            + "(line, column, code, severity, text) under LC_ALL=C",
            "leg A recomputed by EffectRowsProbeLedgerTests.PpE1LedgerMatchesRecomputation (each "
            + "frozen diff re-applied to the frozen fixture, compiled with the pinned invocation, codes "
            + "and declarations compared); negative control and routes (a)/(b) recomputed; leg B "
            + "recorded, with only its arithmetic recomputed by bench/phase0-agent-native/ppe1-analyze.py "
            + "from the archived per-run result.json files; verdict derived from the frozen outcome map",
            "a cell is detected iff the registered code appears at the registered declaration; "
            + "L6-MAP/L6-MATCH must name the laundered effect 'cw' and L6-MID must name the "
            + "instantiation; L7 requires Calor0425 specifically, rising above the unmutated fixture's "
            + "count at that declaration; a different code at the registered declaration counts as "
            + "detected-with-code-drift only if it is in the drift set, and is published by name",
            DriftSet,
            NeverDetection,
            control,
            legA,
            routes,
            legB,
            "NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT",
            verdict,
            reason);
    }

    private static PpE1Control AssembleControl(Measurement m)
    {
        var perFixture = new List<PpE1ControlFixture>();
        foreach (var (fixture, expectedExit, baseline) in ControlBaselines)
        {
            var (exit, diagnostics) = m.Controls[fixture];
            var observed = diagnostics.Select(ToObserved).ToArray();
            var violations = new List<string>();
            var migration = false;

            if (exit != expectedExit)
                violations.Add($"exit {exit}, expected {expectedExit}");

            var unmatched = baseline.ToList();
            foreach (var d in diagnostics)
            {
                var match = unmatched.FirstOrDefault(b =>
                    b.Code == d.Code && b.Line == d.Line && b.Column == d.Column);
                if (match is not null)
                {
                    unmatched.Remove(match);
                    if (match.Severity != d.Severity)
                        violations.Add($"{d.Code} at ({d.Line},{d.Column}) is {d.Severity}, baseline {match.Severity}");
                    continue;
                }

                // The one pre-allowed migration: a frozen Calor0410-'unknown'/Calor0411
                // entry replaced by Calor0425/Calor0419 at the SAME declaration.
                var migrated = unmatched.FirstOrDefault(b =>
                    b.Line == d.Line && b.Column == d.Column
                    && (b.Code == "Calor0410" || b.Code == "Calor0411")
                    && (d.Code == "Calor0425" || d.Code == "Calor0419"));
                if (migrated is not null)
                {
                    unmatched.Remove(migrated);
                    migration = true;
                    continue;
                }

                violations.Add(BarredInControl.Contains(d.Code, StringComparer.Ordinal)
                    ? $"barred {d.Code} at ({d.Line},{d.Column})"
                    : $"unexpected {d.Severity} {d.Code} at ({d.Line},{d.Column})");
            }

            foreach (var b in unmatched)
                violations.Add($"missing {b.Severity} {b.Code} at ({b.Line},{b.Column})");

            perFixture.Add(new PpE1ControlFixture(
                FixtureRef(fixture), expectedExit, baseline, exit, observed, migration,
                violations.ToArray(), violations.Count == 0));
        }

        return new PpE1Control(
            "the plain compile of each unmutated fixture must reproduce exactly its frozen post-E4 "
            + "multiset (A-1.11.1); any effect-family diagnostic beyond it — including a code "
            + "enumerated nowhere in the row — fails the control; Calor0405/0420/0421/0424 anywhere "
            + "fail it; the only pre-allowed move is A2's Calor0410-'unknown'/Calor0411 replaced by "
            + "Calor0425/Calor0419 at the same declaration",
            BarredInControl,
            perFixture.ToArray(),
            perFixture.All(f => f.Clean));
    }

    private static PpE1LegA AssembleLegA(Measurement m)
    {
        var cells = new List<PpE1Cell>();
        foreach (var (cell, exit, diagnostics, occurrences) in m.Mutants)
        {
            var (_, control) = m.Controls[cell.Fixture];
            var atLine = diagnostics.Where(d => d.Line == cell.RegisteredLine).ToArray();

            var baselineCount = control.Count(d =>
                d.Line == cell.RegisteredLine && d.Code == cell.RegisteredCode);
            var registeredHits = atLine.Count(d =>
                d.Code == cell.RegisteredCode && MessageRuleHolds(cell, d.Message));
            var registeredPresentButUnnamed = atLine.Any(d => d.Code == cell.RegisteredCode)
                && registeredHits == 0;

            string? observedCode;
            string? drift = null;
            string? note = null;
            bool detected;
            if (registeredHits > baselineCount)
            {
                detected = true;
                observedCode = cell.RegisteredCode;
            }
            else
            {
                var driftHit = atLine.FirstOrDefault(d =>
                    DriftSet.Contains(d.Code, StringComparer.Ordinal)
                    && d.Code != cell.RegisteredCode
                    && diagnostics.Count(x => x.Line == d.Line && x.Code == d.Code)
                       > control.Count(x => x.Line == d.Line && x.Code == d.Code));
                if (driftHit is not null)
                {
                    detected = true;
                    drift = driftHit.Code;
                    observedCode = driftHit.Code;
                    note = $"detected-with-code-drift: {driftHit.Code} at the registered declaration "
                           + $"instead of {cell.RegisteredCode} (row family; published against design §6.1)";
                }
                else
                {
                    detected = false;
                    observedCode = atLine.Select(d => d.Code).FirstOrDefault();
                    note = registeredPresentButUnnamed
                        ? $"{cell.RegisteredCode} present at the registered declaration but its message "
                          + "does not satisfy the L6 naming rule — not detection"
                        : atLine.Any(d => NeverDetection.Contains(d.Code, StringComparer.Ordinal))
                            ? "only never-detection codes at the registered declaration"
                            : "registered code absent at the registered declaration";
                }
            }

            var diff = cell.CommittedMutant is not null
                ? new PpE1Diff("committed-mutant", null, null, null,
                    PpE1Probe.FixtureRelativePath(cell.CommittedMutant), PpE1Probe.A2BroadeningBlobSha)
                : new PpE1Diff("textual", cell.Anchor, cell.Replacement, occurrences, null, null);

            cells.Add(new PpE1Cell(
                cell.Id, cell.Class, FixtureRef(cell.Fixture), diff,
                cell.RegisteredCode, cell.RegisteredDeclaration, cell.RegisteredLine,
                cell.MessageRule, cell.BeforeCode,
                exit, observedCode, atLine.Select(ToObserved).ToArray(),
                baselineCount, registeredHits,
                cell.MessageRule is null ? null : registeredHits > 0,
                detected, drift, note));
        }

        var detectedCount = cells.Count(c => c.Detected);
        return new PpE1LegA(
            cells.Count,
            false,
            "design §7.5 / roadmap §4.1 ramp VALIDATED at the spike (spike-verdict.json ramp.verdict); "
            + "rank-1 was NOT cut from E2, so the three L6 cells stay in the denominator (10, not 7)",
            detectedCount,
            cells.Count(c => c.Drift is not null),
            cells.ToArray(),
            "10/10 detected with the registered code at the registered declaration, with a clean negative control",
            detectedCount == cells.Count);
    }

    private static bool MessageRuleHolds(PpE1Mutation cell, string message)
        => cell.MessageMustContain is null
           || cell.MessageMustContain.All(s => message.Contains(s, StringComparison.Ordinal));

    private static PpE1Routes AssembleRoutes(Measurement m, PpE1LegA legA,
        bool recordedRouteDFires, bool recordedOwnGoal)
    {
        // (a) any of the five fixtures fails to compile, mutated or not — read as
        // "does not reach the effect pass": a lexer/parser/semantic-class code.
        var routeAEvidence = new List<string>();
        foreach (var (fixture, (_, diagnostics)) in m.Controls.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            foreach (var d in diagnostics.Where(BlocksEffectPass))
                routeAEvidence.Add($"{fixture}: {d.Code} at ({d.Line},{d.Column})");
        }

        foreach (var (cell, _, diagnostics, _) in m.Mutants)
        {
            foreach (var d in diagnostics.Where(BlocksEffectPass))
                routeAEvidence.Add($"{cell.Id}: {d.Code} at ({d.Line},{d.Column})");
        }

        // (b) the §4.1 higher-order demand ledger at this commit.
        using var demand = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(),
            "bench", "phase0-agent-native", "higher-order-demand-ledger.json")));
        var dA = demand.RootElement.GetProperty("dA").GetProperty("total").GetInt32();
        var dB = demand.RootElement.GetProperty("dB").GetProperty("aggregate").GetProperty("total").GetInt32();
        var floor = demand.RootElement.GetProperty("floor").GetInt32();
        Assert.Equal(25, floor);

        // (d) recorded, with mechanical evidence recomputed beside it.
        var calor0424InSource = Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "src", "Calor.Compiler"), "*.cs", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains("Calor0424", StringComparison.Ordinal));
        var l7With0425 = legA.Cells.Count(c => c.Id.StartsWith("L7-", StringComparison.Ordinal)
                                               && c.ObservedCode == "Calor0425");
        var l5With0421 = legA.Cells.Count(c => c.Id.StartsWith("L5-", StringComparison.Ordinal)
                                               && c.ObservedCode == "Calor0421");

        var legB = ReadLegB();
        return new PpE1Routes(
            new PpE1RouteA(routeAEvidence.Count > 0,
                "any of the five fixtures fails to compile on the release compiler, mutated or not — "
                + "recomputed as: a lexer/parser/semantic-class code (Calor0001–Calor0299) in any control "
                + "or mutant compile",
                routeAEvidence.ToArray()),
            new PpE1RouteB(dA + dB < floor, floor, dA, dB, dA + dB,
                "bench/phase0-agent-native/higher-order-demand-ledger.json (dA.total + dB.aggregate.total)"),
            new PpE1RouteC(legB.Status == "run" ? legB.HarnessValid == false : null,
                "the PP-W5 validity floor (a cell with < 2 valid runs drops its pair, disclosed; fewer than "
                + "3 surviving pairs, or either arm below 12 valid runs), two arms without distinct repo "
                + "roots and distinct Calor.Tasks hashes, or either arm above §2's 40 % censoring cap — "
                + "recorded by ppe1-analyze.py as harnessValid"),
            new PpE1RouteD(recordedRouteDFires,
                "E2, E3 or E4 does not ship in 0.15.0, and only where roadmap §4.2's cut line was invoked "
                + "in writing; an unplanned E4 slip is a MISS — recorded",
                [
                    $"Calor0424 (E2's DoesNotFit) present in src/Calor.Compiler: {calor0424InSource}",
                    $"L7 cells observing Calor0425 (E4's code): {l7With0425}/5",
                    $"L5 cells observing Calor0421 (E3's row text): {l5With0421}/2",
                ]),
            recordedOwnGoal,
            "a not-adjudicated route caused by this workstream's own change is adjudicated MISS; the "
            + "cause must be published with the artifact that shows it — recorded");
    }

    private static bool BlocksEffectPass(PpE1Diagnostic d)
        => d.Code.Length == 9
           && int.TryParse(d.Code.AsSpan(5), System.Globalization.NumberStyles.None,
               System.Globalization.CultureInfo.InvariantCulture, out var n)
           && n < 300;

    /// <summary>
    /// Leg B is recorded. If <c>ppe1-analyze.py</c> has written the epoch's
    /// analysis, its figures are read from there; otherwise every figure is
    /// null and the status says so. Numbers are never invented here.
    /// </summary>
    private static PpE1LegB ReadLegB()
    {
        const string rule =
            "fails iff BOTH the one-sided 95 % two-level cluster-bootstrap lower bound of the median "
            + "paired per-pair output-tokens-to-green ratio (0.15.0 release build / v0.14.3) exceeds 1.0 "
            + "AND the point estimate exceeds 1.35; UNDERPOWERED if the point exceeds 1.35 with the bound "
            + "not firing, or the realized median within-cell CV exceeds 0.66; per-run figure = tokens.output "
            + "as derived by token-usage.py (A-1.9.1), never usage.output_tokens";
        var path = Path.Combine(RepoRoot(), AnalysisRelativePath().Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return new PpE1LegB("not-run", LegBEpoch, AnalysisRelativePath(),
                "bench/phase0-agent-native/ppe1-analyze.py", rule,
                null, null, null, null, null, null, null, null);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.True(root.GetProperty("epoch").GetString() == LegBEpoch,
            $"{path} analyses epoch '{root.GetProperty("epoch").GetString()}', not {LegBEpoch}");
        Assert.False(root.TryGetProperty("dryRun", out var dry) && dry.GetBoolean(),
            $"{path} is a DRY RUN analysis and cannot be recorded as leg B");
        return new PpE1LegB("run", LegBEpoch, AnalysisRelativePath(),
            "bench/phase0-agent-native/ppe1-analyze.py", rule,
            Number(root, "pointEstimate"), Number(root, "lowerBound95"),
            Number(root, "realizedMedianWithinCellCv"),
            root.GetProperty("harnessValid").GetBoolean(),
            NullableBool(root, "legBFails"), NullableBool(root, "underpowered"),
            root.GetProperty("armA").GetProperty("label").GetString(),
            root.GetProperty("armB").GetProperty("label").GetString());
    }

    private static double? Number(JsonElement root, string name)
        => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

    private static bool? NullableBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? e.GetBoolean() : null;

    /// <summary>
    /// The frozen four-valued outcome map, in its precedence. A pure function of
    /// the ledger's recorded state, so the file's verdict can be re-derived from
    /// the file alone.
    /// </summary>
    private static (string Verdict, string Reason) ComputeVerdict(bool controlClean, PpE1LegA legA,
        PpE1Routes routes, PpE1LegB legB)
    {
        if (legB.Status != "run")
            return ("NOT-ADJUDICATED", NotRunReason);

        var legASummary = $"leg A {legA.Detected}/{legA.Denominator}, control {(controlClean ? "clean" : "NOT clean")}";
        var legBSummary = $"leg B point {Fmt(legB.PointEstimate)}, lower bound {Fmt(legB.LowerBound95)}, "
                          + $"realized median within-cell CV {Fmt(legB.RealizedMedianWithinCellCv)}";

        var notAdjudicatedRoutes = new List<string>();
        if (routes.A.Fires) notAdjudicatedRoutes.Add("(a) a fixture does not reach the effect pass");
        if (routes.B.Fires) notAdjudicatedRoutes.Add($"(b) demand ledger {routes.B.Total} below the floor of {routes.B.Floor}");
        if (routes.C.Fires == true) notAdjudicatedRoutes.Add("(c) the harness is invalid");
        if (routes.D.Fires) notAdjudicatedRoutes.Add("(d) E2/E3/E4 cut in writing");
        if (notAdjudicatedRoutes.Count > 0)
        {
            var route = string.Join("; ", notAdjudicatedRoutes);
            return routes.OwnGoal
                ? ("MISS", $"own-goal: route {route} caused by this workstream's own change; {legASummary}; {legBSummary}")
                : ("NOT-ADJUDICATED", $"route {route}; {legASummary}; {legBSummary}");
        }

        if (legA.Detected < legA.Denominator || !controlClean || legB.Fails == true)
            return ("MISS", $"{legASummary}; {legBSummary}; leg B fails: {legB.Fails}");

        if (legB.Underpowered == true)
            return ("UNDERPOWERED", $"{legASummary}; {legBSummary}");

        return ("HIT", $"{legASummary}; {legBSummary}; leg B does not fail");
    }

    private static string Fmt(double? value)
        => value is null ? "n/a" : value.Value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- helpers

    private static PpE1FixtureRef FixtureRef(string fixture)
        => new(fixture, PpE1Probe.FixtureRelativePath(fixture),
            PpE1Probe.FrozenFixtures.Single(f => f.Fixture == fixture).BlobSha);

    private static PpE1ObservedDiagnostic ToObserved(PpE1Diagnostic d)
        => new(d.Line, d.Column, d.Code, d.Severity, d.Message);

    private static List<string> DescribeWhatMoved(PpE1Ledger recorded, PpE1Ledger recomputed)
    {
        var moved = new List<string>();
        foreach (var (a, b) in recorded.LegA.Cells.Zip(recomputed.LegA.Cells))
        {
            if (a.Id != b.Id)
            {
                moved.Add($"cell order: ledger {a.Id}, recomputed {b.Id}");
                continue;
            }

            if (a.Detected != b.Detected || a.ObservedCode != b.ObservedCode || a.Drift != b.Drift)
            {
                moved.Add($"{a.Id}: ledger detected={a.Detected} observed={a.ObservedCode ?? "none"} drift={a.Drift ?? "none"}; "
                          + $"recomputed detected={b.Detected} observed={b.ObservedCode ?? "none"} drift={b.Drift ?? "none"}");
            }
        }

        if (recorded.LegA.Cells.Length != recomputed.LegA.Cells.Length)
            moved.Add($"cell count: ledger {recorded.LegA.Cells.Length}, recomputed {recomputed.LegA.Cells.Length}");

        foreach (var (a, b) in recorded.NegativeControl.PerFixture.Zip(recomputed.NegativeControl.PerFixture))
        {
            if (a.Clean != b.Clean || !a.Violations.SequenceEqual(b.Violations, StringComparer.Ordinal))
                moved.Add($"control {a.Fixture.Name}: ledger [{string.Join(", ", a.Violations)}]; recomputed [{string.Join(", ", b.Violations)}]");
        }

        if (recorded.Verdict != recomputed.Verdict || recorded.Reason != recomputed.Reason)
            moved.Add($"verdict: ledger {recorded.Verdict} ({recorded.Reason}); recomputed {recomputed.Verdict} ({recomputed.Reason})");

        if (recorded.LegB.Status != recomputed.LegB.Status)
            moved.Add($"leg B status: ledger {recorded.LegB.Status}; recomputed {recomputed.LegB.Status}");

        return moved;
    }

    private static (int Line, string Expected, string Actual) FirstDifferingLine(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end of file>";
            var al = i < a.Length ? a[i] : "<end of file>";
            if (!string.Equals(el, al, StringComparison.Ordinal)) return (i + 1, el, al);
        }

        return (0, "", "");
    }

    private static string Serialize(PpE1Ledger ledger)
        => JsonSerializer.Serialize(ledger, JsonOptions) + "\n";

    private static void AssertCommitSha(string? sha)
        => Assert.True(sha is { Length: 40 } && sha.All(Uri.IsHexDigit),
            $"measuredCommit must be a 40-hex commit SHA, was '{sha}'. It records WHERE the measurement "
            + "came from; like the other ledgers it is shape-checked, not compared to HEAD.");

    private static string HeadSha()
    {
        var psi = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && output.Length == 40, $"could not resolve HEAD: '{output}'");
        return output;
    }
}

// ============================================================================
// The ledger's schema (A-1.11: schemaVersion, measuredCommit, the frozen
// per-fixture negative-control baselines, the per-cell record, leg B's point
// estimate, bound, realized CV and the four-valued verdict). Property order is
// the file's order; exact equality is over the serialized text.
// ============================================================================

internal sealed record PpE1Ledger(
    int SchemaVersion,
    string ProofPoint,
    string Registration,
    string MeasuredCommit,
    string PinnedInvocation,
    string RecomputedVsRecorded,
    string DetectionRule,
    string[] DriftSet,
    string[] NeverDetection,
    PpE1Control NegativeControl,
    PpE1LegA LegA,
    PpE1Routes Routes,
    PpE1LegB LegB,
    string Precedence,
    string Verdict,
    string Reason);

internal sealed record PpE1FixtureRef(string Name, string Path, string BlobSha);

internal sealed record PpE1BaselineEntry(
    string Code, int Count, string Declaration, string Severity, int Line, int Column);

internal sealed record PpE1ObservedDiagnostic(int Line, int Column, string Code, string Severity, string Message);

internal sealed record PpE1ControlFixture(
    PpE1FixtureRef Fixture,
    int ExpectedExitCode,
    PpE1BaselineEntry[] Baseline,
    int ObservedExitCode,
    PpE1ObservedDiagnostic[] Observed,
    bool PreAllowedMigrationTaken,
    string[] Violations,
    bool Clean);

internal sealed record PpE1Control(
    string Rule,
    string[] BarredAnywhere,
    PpE1ControlFixture[] PerFixture,
    bool Clean);

internal sealed record PpE1Diff(
    string Kind,
    string? Anchor,
    string? Replacement,
    int? AnchorOccurrences,
    string? Path,
    string? BlobSha);

internal sealed record PpE1Cell(
    string Id,
    string Class,
    PpE1FixtureRef Fixture,
    PpE1Diff Mutation,
    string RegisteredCode,
    string RegisteredDeclaration,
    int RegisteredLine,
    string? MessageRule,
    string BeforeCode,
    int ObservedExitCode,
    string? ObservedCode,
    PpE1ObservedDiagnostic[] ObservedAtRegisteredDeclaration,
    int BaselineCountAtRegisteredDeclaration,
    int RegisteredCodeCountAtRegisteredDeclaration,
    bool? MessageRuleSatisfied,
    bool Detected,
    string? Drift,
    string? Note);

internal sealed record PpE1LegA(
    int Denominator,
    bool RampFired,
    string RampNote,
    int Detected,
    int DetectedWithDrift,
    PpE1Cell[] Cells,
    string Bar,
    bool MeetsBar);

internal sealed record PpE1RouteA(bool Fires, string Rule, string[] Evidence);

internal sealed record PpE1RouteB(bool Fires, int Floor, int DA, int DB, int Total, string Source);

internal sealed record PpE1RouteC(bool? Fires, string Rule);

internal sealed record PpE1RouteD(bool Fires, string Rule, string[] Evidence);

internal sealed record PpE1Routes(
    PpE1RouteA A,
    PpE1RouteB B,
    PpE1RouteC C,
    PpE1RouteD D,
    bool OwnGoal,
    string OwnGoalClause);

internal sealed record PpE1LegB(
    string Status,
    string Epoch,
    string AnalysisPath,
    string Analyzer,
    string Rule,
    double? PointEstimate,
    double? LowerBound95,
    double? RealizedMedianWithinCellCv,
    bool? HarnessValid,
    bool? Fails,
    bool? Underpowered,
    string? ControlArm,
    string? TreatmentArm);
