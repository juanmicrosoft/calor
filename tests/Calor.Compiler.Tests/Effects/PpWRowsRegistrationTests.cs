using System.Text.Json;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.16 entry gate — annex entry <b>A-1.12</b>, the PP-W-rows registration
/// (<c>docs/plans/agent-native-gates.md</c>, §A.2 row keyed <c>PP-W6</c> and the
/// §A.3 entry), pinned so its load-bearing constants cannot drift silently.
///
/// <para><b>Why this file exists.</b> The annex is append-only and byte-frozen
/// by <c>scripts/check-annex-freeze.py</c>: a number written into the A-1.12 row
/// can never be corrected in place, only superseded by a new entry. That makes
/// a silent divergence between the row's text and the artifacts it names the
/// worst available failure — the registration would still *look* honoured. This
/// is the mechanical link, following A-1.11's
/// <c>SpikeVerdictTests.PpE1FixtureBlobShasMatchTheFrozenAnnexRow</c>: recompute
/// the git object id of every starter the row freezes, and assert every other
/// registered constant appears verbatim in the frozen row.</para>
///
/// <para><b>What it does NOT do.</b> It does not adjudicate PP-W-rows, run an
/// epoch, or check the honesty of what the row says — that stays with review,
/// exactly as the freeze guard's own docstring records. It checks that the
/// bytes the row names are still the bytes on disk, and that the constants the
/// plan, the harness and the ledger will read are the constants the annex
/// froze.</para>
///
/// <para><b>Sequencing.</b> At registration the six pair directories under
/// <c>bench/phase0-agent-native/pairs/W-00*/</c> are on the (unmerged) branch
/// <c>bench/s3-ppw-rows-pairs</c>, so <see
/// cref="PairStarterCopiesMatchTheFrozenBlobShas"/> skips cleanly until that PR
/// lands and then pins the copies too. The spike sources the row names are on
/// main and are pinned unconditionally.</para>
/// </summary>
public sealed class PpWRowsRegistrationTests
{
    /// <summary>
    /// The commit the A-1.12 row freezes the starters at. Every blob SHA below
    /// was verified with <c>git ls-tree</c> at this commit at registration.
    /// </summary>
    private const string StarterFreezeCommit = "7d621c0d";

    /// <summary>
    /// The twelve per-arm starter slots the row freezes: six pairs × two arms.
    /// Ten distinct blobs, because W-006 deliberately reuses W-002's starters
    /// for a different task — that reuse is registered, and this table is where
    /// it is checked rather than assumed.
    /// </summary>
    private static readonly (string Pair, char Arm, string SpikeRelativePath, string BlobSha)[] FrozenStarters =
    [
        ("W-001", 'a', "before/A3-middleware.calr", "2d351d101f5972cf1f5c4cb5640be3bd2870974f"),
        ("W-001", 'b', "after/A3-middleware.calr", "e5ee81e24abcf38f9111407d8e5c635a482a7ed2"),
        ("W-002", 'a', "before/A3-map.calr", "9f108655fcc376a721efd3e4b1be187aeb4da5e4"),
        ("W-002", 'b', "after/A3-map.calr", "0885b3dd40fcff28c51de72860d47a32db60bf8c"),
        ("W-003", 'a', "before/A3-match.calr", "1f36ea6e36ac331679d4672b17294cd100a5c25e"),
        ("W-003", 'b', "after/A3-match.calr", "c1ce75179ff0ab0b80bd74e2e7f6709ffb542bfe"),
        ("W-004", 'a', "before/A3-callback.calr", "f2dca4a6a71e28266e27ccfd56e4d2a06bc5fd79"),
        ("W-004", 'b', "after/A3-callback.calr", "05ddc23d342e8652ae59be242d29dd0b8a3ca5c4"),
        ("W-005", 'a', "before/A2.calr", "d49d00178aff477288e5e0527e39834865820761"),
        ("W-005", 'b', "after/A2.calr", "93ecdf1605c4e220313c1dd76b3291d3a79bb705"),
        ("W-006", 'a', "before/A3-map.calr", "9f108655fcc376a721efd3e4b1be187aeb4da5e4"),
        ("W-006", 'b', "after/A3-map.calr", "0885b3dd40fcff28c51de72860d47a32db60bf8c"),
    ];

    /// <summary>The pair directory each pair id resolves to under <c>pairs/</c>.</summary>
    private static readonly (string Pair, string Directory, string File)[] PairDirectories =
    [
        ("W-001", "W-001-middleware-stage", "A3-middleware.calr"),
        ("W-002", "W-002-map-and-report", "A3-map.calr"),
        ("W-003", "W-003-match-fallback", "A3-match.calr"),
        ("W-004", "W-004-counter-peek", "A3-callback.calr"),
        ("W-005", "W-005-pipeline-trace", "A2.calr"),
        ("W-006", "W-006-map-doubler", "A3-map.calr"),
    ];

    private static readonly string[] BlindPairs = ["W-001", "W-004", "W-006"];   // ordinal order

    private static readonly string[] WarningVsErrorPairs = ["W-002", "W-003", "W-005"];

    private static readonly string[] LegBPairs = ["W-001", "W-002", "W-003", "W-004", "W-006"];

    /// <summary>
    /// The registered scalars, each paired with the exact substring the frozen
    /// A-1.12 row must contain. A constant that moves in the plan, the harness
    /// or the ledger without a NEW annex entry turns this red — which is the
    /// only outcome the append-only freeze leaves available.
    /// </summary>
    private static readonly (string Name, string MustAppearInRow)[] RegisteredConstants =
    [
        // Leg A.
        ("pre-registered effect size", "Δ = 0.5"),
        ("leg-A bar", "one-sided 95 % lower bound of that delta exceeds 0"),
        ("blind-cell floor", "floor two"),
        // Leg B.
        ("leg-B margin", "the point estimate exceeds the margin **1.20**"),
        ("leg-B bound leg", "lower bound exceeds **1.0**"),
        // Margin derivation.
        ("margin population", "`e1-rows-parity-001`"),
        ("null redraw convention", "RESAMPLE-WITH-REPLACEMENT"),
        ("simulation size", "`SIMS = 3000`"),
        ("bootstrap size", "`BOOT = 400`"),
        ("seed", "seed 4537"),
        ("null p95", "p95 1.1766"),
        ("across-seed range", "range **1.1766–1.1864**"),
        ("Monte-Carlo half-width", "half-width of **0.005**"),
        // M8 — the derived half-width disclosure. Each figure was recomputed from the three
        // measured p95s at registration; none is a quoted number.
        ("half-width is half the range", "(1.1864 \u2212 1.1766) / 2 = 0.0049"),
        ("grid-line flip distance", "**0.0136** below the 1.20 grid line"),
        ("sample sd of the three p95s", "sample sd **0.005048**"),
        ("standard error", "standard error **0.002914**"),
        ("t half-width, df = 2", "4.3027 \u00d7 0.002914 = **0.01254**"),
        ("t-based headroom", "roughly **8 %** headroom"),
        // M9 — the spend-basis skew, recomputed from the 40 archived agent.json files.
        ("cost median", "**median is $0.8245**"),
        ("cost max", "**max $2.9843**"),
        ("cost CV", "the CV **0.50**"),
        ("runs above the mean", "only **15 of 40** runs sit above the mean"),
        ("margin rule", "the smallest 0.05 grid line at or above (p95 + its Monte-Carlo half-width)"),
        ("CV cap arithmetic", "1.5 × 0.2746 = 0.4119 → cap 0.41"),
        // Spend.
        ("archived per-run cost", "$1.0048 per run"),
        ("spend ceiling", "ceiling $150"),
        ("N > 9 does not fit", "N > 9 does not fit"),
        // Arms.
        ("arm A tag", "633169879e16a5e49d3b7ab51089f195d7573a0b"),
        ("arm A commit", "283ec9f9964ddd5b21da15b646a0dd77d53de99e"),
        ("arm B commit", "3bb2601e0cbd93fc25fdaaf2a0ea5183b8a2dd6a"),
        ("pre-rows control arm kind", "`controlArmKind: \"pre-rows\"`"),
        // Validity.
        ("per-turn field", "TOP-LEVEL"),
        ("transcript validity", "a run without `transcript.jsonl` is `invalid`"),
        // Outcome.
        ("outcome precedence", "NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT"),
        ("own-goal clause", "OWN-GOAL CLAUSE"),
        // Review round 2 (#1123's own review): the confound, the shape indicator, the escape
        // rule, and the two disclosures that change what a verdict means.
        ("this-qualified confound", "PRE-REGISTERED CONFOUND ON LEG A"),
        ("confound hits two blind cells", "two ARGUMENT-POSITION blind cells and not the direct-invocation one"),
        ("W-004 fails closed", "**W-004 is UNAFFECTED and fails closed**"),
        ("floor risk from the confound", "NOT-ADJUDICATED by route (a\u2032)"),
        ("shape-realized indicator", "A SHAPE-REALIZED INDICATOR IS OBLIGATORY"),
        ("escape semantics", "failing on a workspace that BUILT"),
        ("non-building runs published separately", "did not build at declared-done"),
        ("permissive lattice", "**`EffectSet.Empty`, not `EffectSet.Unknown`**"),
        ("leg-B cluster dependence", "W-002 and W-006 share a starter blob"),
        // Review round 4: #1123's independent reproduction, the runtime escapes, and #1136.
        ("confound issue", "#1136"),
        ("W-001 escape at runtime", "escapes **2 of 7**"),
        ("W-006 escape at runtime", "**2 of 10**"),
        ("W-004 negative control", "W-004 the **negative control**"),
        // Review round 5: the rule is general, not `this.`-specific.
        ("general rule", "any argument expression the effect pass cannot resolve to a rowed"),
        ("property instance", "a `§PROP` property, passed UNQUALIFIED"),
        ("property cannot carry a row", "a `§PROP` CANNOT CARRY A ROW AT ALL"),
        ("inherited instance", "an inherited field accessed unqualified"),
        ("fail-closed controls", "two controls that genuinely fail closed"),
        ("four-way escape classification", "`this-qualified` / `property` / `inherited` / "),
        ("indicator non-vacuity", "MUST NOT BE SATISFIABLE BY THE STARTER ITSELF"),
        ("unregistered roles excluded from the pin", "**excluded by name** from the frozen-cells pin"),
        ("arm-A route (a) is new", "**Arm A's are NEW and are registered here, not cited:**"),
    ];

    /// <summary>
    /// The starters A-1.12 freezes are the effect-rows spike sources on main.
    /// Recompute each git object id and compare to the row: an edit to any of
    /// them leaves the frozen row pointing at content that no longer exists,
    /// and the row cannot be corrected in place.
    /// </summary>
    [Fact]
    public void FrozenStarterBlobShasMatchTheSpikeSourcesOnDisk()
    {
        var spike = SpikeDirectory();
        foreach (var (pair, arm, relative, expected) in FrozenStarters)
        {
            var path = Path.Combine(spike, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"A-1.12 starter missing for {pair} arm {arm}: {path}");

            var actual = GitBlobSha1(path);
            Assert.True(
                string.Equals(actual, expected, StringComparison.Ordinal),
                $"A-1.12 starter for {pair} arm {arm} ({relative}) has blob SHA {actual}, but the "
                + $"frozen §A.2 row names {expected} (verified with `git ls-tree` at {StarterFreezeCommit}). "
                + "The row is append-only and cannot be corrected: either restore the file, or register "
                + "the new content in a NEW annex entry that supersedes the cell "
                + "(docs/plans/agent-native-gates.md §A.3).");
        }
    }

    /// <summary>
    /// Every distinct frozen blob SHA is written into the row itself, so the
    /// constants in this file and the constants in the annex cannot drift apart
    /// in either direction.
    /// </summary>
    [Fact]
    public void FrozenRowNamesEveryDistinctStarterBlobSha()
    {
        var row = FrozenRow();
        foreach (var sha in FrozenStarters.Select(s => s.BlobSha).Distinct(StringComparer.Ordinal))
        {
            Assert.True(
                row.Contains(sha, StringComparison.Ordinal),
                $"blob SHA {sha} is registered in PpWRowsRegistrationTests but does not appear in the "
                + "frozen A-1.12 §A.2 row.");
        }
    }

    /// <summary>
    /// The cell classes and the leg-B denominator are what the verdict is read
    /// on and what <c>ppw-analyze.py</c> takes from <c>pins.json</c>. Both are
    /// asserted against the row's own text, spelled the way the row spells them.
    /// </summary>
    [Fact]
    public void FrozenRowNamesTheCellClassesAndTheLegBDenominator()
    {
        var row = FrozenRow();

        Assert.Contains("*blind* = {**W-001, W-004, W-006**}", row, StringComparison.Ordinal);
        Assert.Contains("*warning-vs-error* = {**W-002, W-003, W-005**}", row, StringComparison.Ordinal);
        Assert.Contains("`legBPairs` = {W-001, W-002, W-003, W-004, W-006}", row, StringComparison.Ordinal);
        Assert.Contains("`blindPairs` = {W-001, W-004, W-006}", row, StringComparison.Ordinal);
        Assert.Contains("W-005 NAMED AS EXCLUDED", row, StringComparison.Ordinal);

        // The three sets are consistent with each other and with the six pairs.
        Assert.Equal(3, BlindPairs.Length);
        Assert.Equal(3, WarningVsErrorPairs.Length);
        Assert.Equal(5, LegBPairs.Length);
        Assert.Empty(BlindPairs.Intersect(WarningVsErrorPairs, StringComparer.Ordinal));
        Assert.Equal(
            PairDirectories.Select(p => p.Pair).OrderBy(p => p, StringComparer.Ordinal),
            BlindPairs.Concat(WarningVsErrorPairs).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(["W-005"], PairDirectories.Select(p => p.Pair).Except(LegBPairs, StringComparer.Ordinal));
    }

    /// <summary>
    /// M7 (review round 1): the cell classes are <b>parsed out of the frozen row</b> and compared
    /// to the pinned sets, in both directions. The earlier version only cross-checked the two
    /// pinned arrays against each other, so swapping W-002 and W-006 between them stayed green
    /// while inverting which pairs the verdict is read on — the single most consequential silent
    /// mutation available on this row.
    /// </summary>
    [Fact]
    public void TheRowsOwnCellClassesEqualThePinnedSets()
    {
        var row = FrozenRow();

        Assert.Equal(BlindPairs, ParseBraceSet(row, "*blind* = {"));
        Assert.Equal(WarningVsErrorPairs, ParseBraceSet(row, "*warning-vs-error* = {"));
        Assert.Equal(LegBPairs, ParseBraceSet(row, "`legBPairs` = {"));
        Assert.Equal(BlindPairs, ParseBraceSet(row, "`blindPairs` = {"));
    }

    /// <summary>
    /// The scar left by review round 1's CRITICAL: the frozen row asserted that
    /// <c>Calor0418</c> "stays an error under every flag" while its own six arm-A cells recorded
    /// <c>warning Calor0418</c> at exit 0. The row cannot be corrected after merge, so the
    /// contradiction is pinned out of any future row instead.
    /// </summary>
    [Fact]
    public void TheRowNeverClaimsCalor0418IsAlwaysAnErrorWhileRecordingItAsAWarning()
    {
        var row = FrozenRow();
        var recordsWarning = row.Contains("warning Calor0418", StringComparison.Ordinal)
            || row.Contains("`warning Calor0418`", StringComparison.Ordinal)
            || row.Contains("w 0418", StringComparison.Ordinal);
        Assert.True(recordsWarning, "the row is expected to record arm A's demoted Calor0418");

        foreach (var claim in new[]
        {
            "or `Calor0418`, which stay errors under every flag",
            "`Calor0418`, which stay errors under every flag",
            "`Calor0418` stays an error under every flag",
        })
        {
            Assert.False(
                row.Contains(claim, StringComparison.Ordinal),
                "the frozen row records `warning Calor0418` on arm A, so it may not also claim "
                + $"\"{claim}\" — that is review round 1's C1, and it cannot be fixed after merge.");
        }

        // The verified per-arm facts must be the ones the row states.
        Assert.Contains("suppresses `Calor0411`", row, StringComparison.Ordinal);
        Assert.Contains("demotes `Calor0418` to a warning", row, StringComparison.Ordinal);
        Assert.Contains("`Calor0424` and `Calor0425` DO NOT EXIST at `63316987`", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 (review round 1): nothing linked the 43 frozen per-arm compile multisets, or the
    /// per-pair escape counts, to the row — flipping W-004's arm-A cell from blind to
    /// warning-vs-error, or halving an escape count, both left the suite green. This asserts each
    /// registered cell's <c>(exitCode, code, severity, line, column)</c> against the row's own
    /// text. Skips until #1123 lands, mirroring
    /// <see cref="PairStarterCopiesMatchTheFrozenBlobShas"/>.
    /// </summary>
    [SkippableFact]
    public void FrozenSeededCompilesAgreeWithTheRowsEnumeratedCells()
    {
        var manifest = Path.Combine(
            RepositoryRoot(), "bench", "phase0-agent-native", "pairs", "ppw-seeded-compiles.json");
        Skip.IfNot(
            File.Exists(manifest),
            "ppw-seeded-compiles.json is not in the tree yet (branch bench/s3-ppw-rows-pairs, "
            + "PR #1123, unmerged at A-1.12's registration)");

        var row = FrozenRow();
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var compiles = document.RootElement.GetProperty("compiles");

        var seen = 0;
        foreach (var compile in compiles.EnumerateArray())
        {
            var role = compile.GetProperty("role").GetString()!;

            // REGISTERED ROLES ONLY. `unregistered-*` cells are recorded evidence for a
            // published confound (#1136), not part of any frozen denominator: enumerating
            // them in the row would promote them into the registration, which is the
            // post-hoc change gates §0.3 forbids. They are pinned instead — as recorded
            // evidence that must not vanish — by TheThisQualifiedEscapeEvidenceIsRecorded.
            Assert.False(
                role.StartsWith("unregistered-", StringComparison.Ordinal) && RegisteredRoles.Contains(role),
                $"role '{role}' cannot be both unregistered and registered");
            if (role.StartsWith("unregistered-", StringComparison.Ordinal)
                || !string.Equals(role, "shortcut", StringComparison.Ordinal))
            {
                continue;
            }

            var pair = compile.GetProperty("pair").GetString()!;
            var shortId = pair[..5];
            var arm = compile.GetProperty("arm").GetString()!;
            var exit = compile.GetProperty("exitCode").GetInt32();
            seen++;

            // The class the row registers must match what the shortcut actually emitted:
            // blind  => arm A carries no Calor0410 and builds; otherwise arm A warns Calor0410.
            var isArmA = string.Equals(arm, "A", StringComparison.Ordinal);
            var armAHasForbidden = string.Equals(arm, "A", StringComparison.Ordinal)
                && compile.GetProperty("diagnostics").EnumerateArray()
                    .Any(x => x.GetProperty("code").GetString() == "Calor0410");

            if (string.Equals(arm, "A", StringComparison.Ordinal))
            {
                Assert.Equal(0, exit);
                Assert.Equal(!BlindPairs.Contains(shortId, StringComparer.Ordinal), armAHasForbidden);
            }
            else
            {
                Assert.Equal(1, exit);
            }

            foreach (var diagnostic in compile.GetProperty("diagnostics").EnumerateArray())
            {
                var code = diagnostic.GetProperty("code").GetString()!;
                var line = diagnostic.GetProperty("line").GetInt32();
                var column = diagnostic.GetProperty("column").GetInt32();

                // The row enumerates these cells in prose, so the pin is on the
                // FACTS (code and position), not on a particular spelling: a cell
                // whose position or code moves leaves the frozen row stale, and the
                // row cannot be edited after merge.
                // Scoped to THIS pair's THIS arm's half of the cell (review round 2, minor):
                // whole-row containment let a corrupted position stay green whenever the correct
                // value happened to appear in another cell — 5 of the 12 sampled positions are
                // duplicated across the starter/clean/shortcut cells.
                var forward = PairCellHalf(row, shortId, isArmA);
                Assert.True(
                    forward.Contains(code, StringComparison.Ordinal),
                    $"{pair} arm {arm} emits {code}, which the frozen A-1.12 row's own "
                    + $"{shortId} arm-{arm} cell never names.");
                Assert.True(
                    forward.Contains($"({line},{column})", StringComparison.Ordinal),
                    $"{pair} arm {arm} emits {code} at ({line},{column}), a position the frozen "
                    + $"A-1.12 row's {shortId} arm-{arm} cell does not record. The row cannot be "
                    + "edited after merge: register the corrected measurement in a NEW annex entry.");
            }

            // The other direction, PER ARM (review round 1 follow-up). Checking bare codes over
            // the whole cell is too weak: a spurious `warning Calor0410` added to W-004's arm-A
            // prose would pass, because Calor0410 already appears in that pair's arm-B cell. So
            // the assertion is on (severity, code) pairs within each arm's own half of the cell.
            // A bare mention with no severity word — "**no `Calor0410`**" — is deliberately not
            // matched by the pattern and so is not treated as a claimed diagnostic.
            var half = PairCellHalf(row, shortId, isArmA);
            var claimed = System.Text.RegularExpressions.Regex
                .Matches(half, "`(warning|error) (Calor0\\d{3})`")
                .Select(m => (Severity: m.Groups[1].Value, Code: m.Groups[2].Value))
                .Distinct()
                .ToArray();
            var emitted = compiles.EnumerateArray()
                .Where(c => c.GetProperty("pair").GetString()!.StartsWith(shortId, StringComparison.Ordinal)
                    && c.GetProperty("role").GetString()!.StartsWith("shortcut", StringComparison.Ordinal)
                    && !c.GetProperty("role").GetString()!.StartsWith("unregistered-", StringComparison.Ordinal)
                    && c.GetProperty("arm").GetString() == arm)
                .SelectMany(c => c.GetProperty("diagnostics").EnumerateArray())
                .Select(x => (
                    Severity: x.GetProperty("severity").GetString()!,
                    Code: x.GetProperty("code").GetString()!))
                .Distinct()
                .ToArray();
            foreach (var (severity, code) in claimed)
            {
                Assert.True(
                    emitted.Contains((severity, code)),
                    $"the frozen A-1.12 row's {shortId} arm-{arm} cell claims a {severity} {code}, "
                    + "which that pair's shortcut compile on that arm does not emit — the row "
                    + "claims a diagnostic the artifact does not have.");
            }
        }

        Assert.True(seen >= 12, $"expected at least the six pairs' two shortcut cells, saw {seen}");

        // The escape count the row publishes as pre-measured: every pair names
        // exactly two effect-observing tests, which is what "leaks exactly 2" means.
        Assert.Contains("leaks **2 escaped** effect-observing tests in every one of the six pairs", row, StringComparison.Ordinal);
        foreach (var (_, directory, _) in PairDirectories)
        {
            var manifestPath = Path.Combine(
                RepositoryRoot(), "bench", "phase0-agent-native", "pairs", directory, "pair.json");
            Assert.True(File.Exists(manifestPath), $"pair manifest missing: {manifestPath}");
            using var pairJson = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var observing = pairJson.RootElement
                .GetProperty("tests").GetProperty("effectObservingTests").EnumerateArray().Count();
            Assert.True(
                observing == 2,
                $"{directory} names {observing} effect-observing tests; the frozen A-1.12 row "
                + "publishes the pre-measured escape as exactly 2 per pair.");
        }
    }

    /// <summary>
    /// Route (a) — "any unmutated starter fails to reproduce its frozen multiset on its arm" — is
    /// A-1.12's first NOT-ADJUDICATED route. Its two references have different provenance and the
    /// pin keeps them apart (#1123 review round 1, M4):
    /// <list type="bullet">
    ///   <item><b>Arm B is inherited.</b> The six arm-B starters must reproduce
    ///   <b>A-1.11.1</b>'s corrected post-E4 control — a byte-frozen entry A-1.12 rests on and may
    ///   not amend.</item>
    ///   <item><b>Arm A is A-1.12's own.</b> A-1.11 recorded those <c>Calor0418</c> locations under
    ///   the pinned FLAGLESS invocation, as <b>errors</b> at <b>exit 1</b>; arm A runs
    ///   <c>--permissive-effects</c>, where they are <b>warnings</b> at <b>exit 0</b>. Only the
    ///   positions coincide.</item>
    /// </list>
    /// Severity and exit are therefore asserted, not just code and position: a check on codes and
    /// positions alone cannot tell the pinned invocation from the forbidden one, which is exactly
    /// how A-1.11's A2 baseline came to be unreproducible (A-1.11.1). Skips until #1123 lands.
    /// </summary>
    [SkippableFact]
    public void StarterCompilesReproduceTheRegisteredRouteAMultisets()
    {
        var manifest = Path.Combine(
            RepositoryRoot(), "bench", "phase0-agent-native", "pairs", "ppw-seeded-compiles.json");
        Skip.IfNot(
            File.Exists(manifest),
            "ppw-seeded-compiles.json is not in the tree yet (branch bench/s3-ppw-rows-pairs, "
            + "PR #1123, unmerged at A-1.12's registration)");

        // A-1.12's OWN arm-A multisets: same positions as A-1.11's row-less Calor0418s, but
        // WARNINGS at exit 0 under --permissive-effects, where A-1.11 recorded errors at exit 1.
        var armA = new Dictionary<string, (int Line, int Column)[]>(StringComparer.Ordinal)
        {
            ["W-001"] = [(4, 19), (5, 20)],
            ["W-002"] = [(7, 22)],
            ["W-003"] = [(5, 10), (6, 8)],
            ["W-004"] = [(6, 7)],
            ["W-005"] = [(25, 27)],
            ["W-006"] = [(7, 22)],
        };

        // A-1.11.1's corrected post-E4 control, per arm-B starter: the four A3 fixtures exit 0
        // with zero diagnostics; A2 is 1x Calor0410 at (23,9) + 2x Calor0411.
        var armB = new Dictionary<string, (string Code, int Line, int Column)[]>(StringComparer.Ordinal)
        {
            ["W-001"] = [],
            ["W-002"] = [],
            ["W-003"] = [],
            ["W-004"] = [],
            ["W-006"] = [],
            ["W-005"] = [("Calor0410", 23, 9), ("Calor0411", 26, 24), ("Calor0411", 28, 19)],
        };

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var starters = 0;
        foreach (var compile in document.RootElement.GetProperty("compiles").EnumerateArray())
        {
            var role = compile.GetProperty("role").GetString()!;
            if (role.StartsWith("unregistered-", StringComparison.Ordinal)
                || !string.Equals(role, "starter", StringComparison.Ordinal))
            {
                continue;
            }

            var shortId = compile.GetProperty("pair").GetString()![..5];
            var arm = compile.GetProperty("arm").GetString()!;
            var isArmA = string.Equals(arm, "A", StringComparison.Ordinal);
            var observed = compile.GetProperty("diagnostics").EnumerateArray()
                .Select(x => (
                    Code: x.GetProperty("code").GetString()!,
                    Severity: x.GetProperty("severity").GetString()!,
                    Line: x.GetProperty("line").GetInt32(),
                    Column: x.GetProperty("column").GetInt32()))
                .OrderBy(x => x.Code, StringComparer.Ordinal)
                .ThenBy(x => x.Line).ThenBy(x => x.Column)
                .ToArray();

            // Severity is part of the registered multiset: the forbidden flagless invocation
            // emits the same codes at the same positions with a different severity and exit.
            var expected = isArmA
                ? armA[shortId].Select(x => ("Calor0418", "warning", x.Line, x.Column))
                    .OrderBy(x => x.Item3).ThenBy(x => x.Item4).ToArray()
                : armB[shortId]
                    .Select(x => (x.Code, x.Code == "Calor0410" ? "error" : "warning", x.Line, x.Column))
                    .OrderBy(x => x.Item1, StringComparer.Ordinal)
                    .ThenBy(x => x.Item3).ThenBy(x => x.Item4).ToArray();

            Assert.True(
                observed.SequenceEqual(expected),
                $"{shortId} arm {arm}'s unmutated starter no longer reproduces the multiset "
                + (isArmA
                    ? "REGISTERED BY A-1.12 for arm A (permissive: warnings at exit 0 — NOT A-1.11's "
                      + "flagless errors)"
                    : "frozen by A-1.11.1 (the corrected post-E4 control)")
                + $". Observed [{string.Join(", ", observed)}], registered [{string.Join(", ", expected)}]. "
                + "That is A-1.12's route (a): NOT-ADJUDICATED, and MISS under the own-goal clause "
                + "if this workstream caused it.");

            // Exit code is registered too: arm A builds under the waiver, and A2 is the only
            // arm-B starter that does not.
            var expectedExit = isArmA ? 0 : (shortId == "W-005" ? 1 : 0);
            Assert.True(
                compile.GetProperty("exitCode").GetInt32() == expectedExit,
                $"{shortId} arm {arm} starter exit code moved; A-1.12 registers {expectedExit}.");
            starters++;
        }

        Assert.Equal(12, starters);
    }

    /// <summary>
    /// The three roles A-1.12 registers. Everything else in
    /// <c>ppw-seeded-compiles.json</c> — every <c>unregistered-*</c> role, the published
    /// <c>sibling-W-001s</c>, the <c>shortcut-b-repaired</c> disclosure — is recorded evidence
    /// that sits outside every denominator.
    /// </summary>
    private static readonly string[] RegisteredRoles = ["starter", "shortcut", "clean"];

    /// <summary>
    /// The other half of the honesty bargain (review rounds 4–5). The frozen-cells pin
    /// deliberately ignores <c>unregistered-*</c> roles so that recording the confound cannot
    /// promote it into the registration. That alone would let the evidence be deleted silently, so
    /// the evidence is pinned here instead — as evidence, not as a denominator.
    ///
    /// <para>The published confound (#1136) is <b>general</b>: on arm B the row charge is defeated
    /// by any argument expression the effect pass cannot resolve to a rowed declaration in the
    /// enclosing class. Known instances: a <c>this.</c>-qualified field, a <c>§PROP</c> property
    /// passed unqualified, and an inherited field. W-001 and W-006 escape; <b>W-004 is the negative
    /// control and fails closed</b>.</para>
    ///
    /// <para>Two corrections this pin carries. First, the role is present for W-004 too — it is not
    /// absent — so what is asserted is the OUTCOME that distinguishes it. Second, the pin is named
    /// for the general rule, not for the <c>this.</c>-qualified instance it was first written
    /// against: every <c>unregistered-*-escape</c> role is required to show the escape on W-001 and
    /// W-006, so #1123's property seeds tighten this automatically when they land.</para>
    /// </summary>
    [SkippableFact]
    public void TheArgumentResolutionEscapeEvidenceIsRecorded()
    {
        var manifest = Path.Combine(
            RepositoryRoot(), "bench", "phase0-agent-native", "pairs", "ppw-seeded-compiles.json");
        Skip.IfNot(
            File.Exists(manifest),
            "ppw-seeded-compiles.json is not in the tree yet (branch bench/s3-ppw-rows-pairs, "
            + "PR #1123, unmerged at A-1.12's registration)");

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var escapeRoles = document.RootElement.GetProperty("compiles").EnumerateArray()
            .Select(c => c.GetProperty("role").GetString()!)
            .Where(r => r.StartsWith("unregistered-", StringComparison.Ordinal)
                && r.EndsWith("-escape", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        // The this.-qualified instance is the one A-1.12 measured itself; it must be on the record.
        Assert.Contains("unregistered-this-qualified-escape", escapeRoles, StringComparer.Ordinal);

        foreach (var role in escapeRoles)
        {
            var cells = document.RootElement.GetProperty("compiles").EnumerateArray()
                .Where(c => string.Equals(c.GetProperty("role").GetString(), role, StringComparison.Ordinal))
                .ToDictionary(c => c.GetProperty("pair").GetString()![..5], c => c, StringComparer.Ordinal);

            // W-001 and W-006 are the argument-position cells: the route reaches them.
            foreach (var pair in new[] { "W-001", "W-006" })
            {
                Assert.True(
                    cells.ContainsKey(pair),
                    $"the #1136 escape evidence '{role}' for {pair} is gone from "
                    + "ppw-seeded-compiles.json. A-1.12 publishes that confound; its evidence may "
                    + "be superseded by a NEW annex entry, never silently deleted.");
                var cell = cells[pair];
                Assert.Equal("B", cell.GetProperty("arm").GetString());
                Assert.True(
                    cell.GetProperty("exitCode").GetInt32() == 0,
                    $"{pair}'s '{role}' seed no longer builds on arm B; #1136's escape is the claim "
                    + "that it does.");
                var codes = cell.GetProperty("diagnostics").EnumerateArray()
                    .Select(x => x.GetProperty("code").GetString()!).ToArray();
                Assert.Contains("Calor0425", codes);
                Assert.DoesNotContain("Calor0410", codes);
            }

            // W-004 is the direct-invocation cell: the route must NOT reach it. Where the seed
            // exists it is the negative control and must still fail closed.
            if (cells.TryGetValue("W-004", out var control))
            {
                Assert.True(
                    control.GetProperty("exitCode").GetInt32() == 1,
                    $"W-004 is #1136's negative control for '{role}' and must still fail closed; if "
                    + "it builds, the confound has widened to all three blind cells and needs a NEW "
                    + "annex entry.");
                Assert.Contains(
                    "Calor0410",
                    control.GetProperty("diagnostics").EnumerateArray()
                        .Select(x => x.GetProperty("code").GetString()!));
            }
        }
    }

    /// <summary>
    /// One arm's half of the row's cell-by-cell enumeration for a pair. The cell runs from
    /// <c>**W-00n** shortcut:</c> to the next pair marker (or the end of the enumeration) and is
    /// split at <c>; B exit</c>, which is how every cell separates the arms.
    /// </summary>
    private static string PairCellHalf(string row, string shortId, bool isArmA)
    {
        var start = row.IndexOf($"**{shortId}** shortcut:", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the frozen A-1.12 row does not enumerate {shortId}'s shortcut cell");
        var next = PairDirectories
            .Select(p => row.IndexOf($"**{p.Pair}** shortcut:", StringComparison.Ordinal))
            .Where(i => i > start)
            .DefaultIfEmpty(-1)
            .Min();
        var stop = next > start ? next : row.IndexOf("Every **clean** solution", StringComparison.Ordinal);
        Assert.True(stop > start, $"could not bound {shortId}'s cell");
        var cell = row[start..stop];

        var split = cell.IndexOf("; B exit", StringComparison.Ordinal);
        Assert.True(split > 0, $"{shortId}'s cell does not separate the arms with \"; B exit\"");
        return isArmA ? cell[..split] : cell[split..];
    }

    /// <summary>
    /// The set literal the row writes for a named class, e.g. <c>{**W-001, W-004, W-006**}</c>.
    /// Parsing the row rather than trusting a duplicate constant is the whole point of M7.
    /// </summary>
    private static string[] ParseBraceSet(string row, string marker)
    {
        var start = row.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the frozen A-1.12 row does not contain the marker \"{marker}\"");
        start += marker.Length;
        var end = row.IndexOf('}', start);
        Assert.True(end > start, $"unterminated set literal after \"{marker}\"");
        return row[start..end]
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Every registered scalar — Δ, the margin, the CV cap, the ceiling, the arm
    /// commits, the seed, the per-turn field — appears verbatim in the frozen
    /// row. This is the drift guard for the numbers the roadmap, the harness and
    /// the ledger all quote independently.
    /// </summary>
    [Fact]
    public void FrozenRowNamesEveryRegisteredConstant()
    {
        var row = FrozenRow();
        foreach (var (name, needle) in RegisteredConstants)
        {
            Assert.True(
                row.Contains(needle, StringComparison.Ordinal),
                $"A-1.12 registered constant '{name}' — expected the frozen §A.2 row to contain "
                + $"\"{needle}\", and it does not.");
        }
    }

    /// <summary>
    /// The unregistered cells are named in the row precisely so they cannot be
    /// promoted into the denominator after results are seen. Pin that they are
    /// still named, and named as excluded.
    /// </summary>
    [Fact]
    public void FrozenRowKeepsTheUnregisteredCellsOutOfTheDenominator()
    {
        var row = FrozenRow();
        Assert.Contains("published sibling, not adjudicated", row, StringComparison.Ordinal);
        Assert.Contains("fourth blind cell is measured and NOT registered", row, StringComparison.Ordinal);
        Assert.Contains("may not be promoted into it after results are seen", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// Structural pin on the registration itself: exactly one §A.2 row is keyed
    /// <c>PP-W6</c>, it is a single line (the freeze guard keys rows by line),
    /// it names the plan's own id <c>PP-W-rows</c> so a reader looking for that
    /// name finds it, and the annex version pointer has moved to A-1.12 with a
    /// matching §A.3 entry.
    /// </summary>
    [Fact]
    public void TheA112RegistrationIsShapedTheWayTheFreezeGuardRequires()
    {
        var annex = File.ReadAllLines(AnnexPath());

        var rows = annex.Where(l => l.StartsWith("| **PP-W6**", StringComparison.Ordinal)).ToArray();
        Assert.Single(rows);
        Assert.EndsWith("|", rows[0].TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("PP-W-rows", rows[0], StringComparison.Ordinal);

        Assert.Single(annex.Where(l => l.StartsWith("**Annex version: A-1.12", StringComparison.Ordinal)));
        Assert.Single(annex.Where(l => l.StartsWith("**A-1.12 ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Once <c>bench/s3-ppw-rows-pairs</c> merges, the per-pair starter copies
    /// must hash to the same blobs as the spike sources the row freezes — that
    /// identity is what makes the registration's <c>git ls-tree</c> verification
    /// mean anything about the files the epoch actually runs. Skips cleanly
    /// while the pairs are still on the unmerged branch.
    /// </summary>
    [SkippableFact]
    public void PairStarterCopiesMatchTheFrozenBlobShas()
    {
        var pairsRoot = Path.Combine(RepositoryRoot(), "bench", "phase0-agent-native", "pairs");
        var directories = PairDirectories.ToDictionary(p => p.Pair, p => p, StringComparer.Ordinal);
        var present = PairDirectories
            .Select(p => Path.Combine(pairsRoot, p.Directory))
            .Where(Directory.Exists)
            .ToArray();

        Skip.If(
            present.Length == 0,
            "the six PP-W-rows pair directories are not in the tree yet (branch bench/s3-ppw-rows-pairs, "
            + "PR #1123, unmerged at A-1.12's registration)");
        Assert.Equal(PairDirectories.Length, present.Length);

        foreach (var (pair, arm, _, expected) in FrozenStarters)
        {
            var meta = directories[pair];
            var path = Path.Combine(pairsRoot, meta.Directory, $"starter-{arm}", meta.File);
            Assert.True(File.Exists(path), $"PP-W-rows starter missing: {path}");
            Assert.Equal(expected, GitBlobSha1(path));
        }
    }

    /// <summary>
    /// The §A.2 row A-1.12 registers, as one line. The freeze guard keys rows by
    /// line, so reading it the same way keeps this pin and the guard in step.
    /// </summary>
    private static string FrozenRow()
        => File.ReadAllLines(AnnexPath())
            .Single(l => l.StartsWith("| **PP-W6**", StringComparison.Ordinal));

    private static string AnnexPath()
        => Path.Combine(RepositoryRoot(), "docs", "plans", "agent-native-gates.md");

    private static string SpikeDirectory() => PpE1Probe.SpikeDirectory();

    private static string RepositoryRoot() => PpE1Probe.RepositoryRoot();

    private static string GitBlobSha1(string path) => PpE1Probe.GitBlobSha1(path);
}
