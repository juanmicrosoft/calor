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

    private static readonly string[] BlindPairs = ["W-001", "W-004", "W-006"];

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
        ("margin rule", "the 0.05 grid line above (p95 + its Monte-Carlo half-width)"),
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
