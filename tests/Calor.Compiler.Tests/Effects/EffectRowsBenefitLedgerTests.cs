using System.Diagnostics;
using System.Text.Json;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// PP-W-rows — the 0.16 rows-benefit proof point (annex A-1.12, §A.2 row
/// <c>PP-W6</c>; roadmap v0.16 §4.1, §5 gate 10). This is the <b>instrument
/// that reads the outcome</b>: <c>bench/phase0-agent-native/effect-rows-benefit-ledger.json</c>,
/// compared by <b>exact equality</b> to a fresh recomputation by
/// <c>bench/phase0-agent-native/ppw-analyze.py</c>.
///
/// <para><b>What is recomputed vs recorded.</b> Everything is recomputed. The
/// ledger is a pure function of the six frozen pair directories, the frozen
/// seeded-compile multisets (<c>pairs/ppw-seeded-compiles.json</c>), and — once
/// it exists — the archived epoch <c>epochs/w-rows-001</c>. It carries no
/// timestamp, no HEAD sha and no absolute path, so a re-run reproduces it byte
/// for byte. There is nothing here a human may hand-edit: the four-valued
/// verdict is <b>derived</b> from the frozen outcome map in its precedence
/// NOT-ADJUDICATED &gt; MISS &gt; UNDERPOWERED &gt; HIT, so the ledger cannot
/// carry a verdict its own numbers do not imply.</para>
///
/// <para><b>Before the epoch runs</b> — which is where 0.16 stands until the
/// release PR's author runs <c>run-ppw-epoch.sh</c> — the ledger records
/// <c>epochRun: false</c> and NOT-ADJUDICATED with the reason, exactly as
/// PP-E1's ledger did while its leg B was unrun. The epoch-dependent
/// assertions below skip cleanly in that state; the byte-equality pin does
/// not, because the static half (pairs, classes, starter blob SHAs, frozen
/// multisets, the registered constants) is the half gate 10 pins.</para>
///
/// <para><b>Discriminating pin</b> (gate 10): <i>dropping a pair from the
/// ledger, or editing one frozen per-arm multiset, fails the test</i>. Both
/// mutations are executed here against a scratch copy of the pair tree
/// (<c>--pairs-root</c>), with the unmutated copy as the control — a mutation
/// test whose control is not also asserted proves nothing.</para>
///
/// <para>Regenerate with <c>python3 bench/phase0-agent-native/ppw-analyze.py
/// --ledger</c>, and name the cause of every delta in the PR body. The
/// arithmetic itself — the escape semantics, the median convention, the
/// bootstrap, the four-valued map, the escape classifier, the validity
/// conditions — is pinned rule-by-rule in
/// <c>bench/phase0-agent-native/tests/test_ppw_analyze.py</c>; this class is
/// the C#-lane observation of the committed bytes.</para>
/// </summary>
public class EffectRowsBenefitLedgerTests
{
    private const string ScriptRelativePath = "bench/phase0-agent-native/ppw-analyze.py";
    private const string LedgerRelativePath = "bench/phase0-agent-native/effect-rows-benefit-ledger.json";
    private const string PairsRelativePath = "bench/phase0-agent-native/pairs";
    private const string EpochRelativePath = "bench/phase0-agent-native/epochs/w-rows-001";

    /// <summary>A-1.12's six registered pairs, by directory and cell class.</summary>
    private static readonly (string Directory, string Class, bool Blind, bool LegB)[] RegisteredPairs =
    [
        ("W-001-middleware-stage", "blind", true, true),
        ("W-002-map-and-report", "warning-vs-error", false, true),
        ("W-003-match-fallback", "warning-vs-error", false, true),
        ("W-004-counter-peek", "blind", true, true),
        // W-005 is leg A ONLY: its arm-B starter does not build, the agent must repair
        // Handle before extending, and that repair would confound the leg-B ratio.
        ("W-005-pipeline-trace", "warning-vs-error", false, false),
        ("W-006-map-doubler", "blind", true, true),
    ];

    private static string RepoRoot() => PpE1Probe.RepositoryRoot();

    private static string Rel(string relative)
        => Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Committed()
        => File.ReadAllText(Rel(LedgerRelativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    // ------------------------------------------------------------------ the pin

    [SkippableFact]
    public void CommittedLedgerEqualsFreshRecomputation()
    {
        var tmp = NewScratch("calor-ppw-ledger-");
        var outPath = Path.Combine(tmp, "recomputed.json");
        var (exit, log) = RunScript("--ledger", "--out", outPath);
        Assert.True(exit == 0, "ppw-analyze.py --ledger failed:\n" + log);

        var recomputed = File.ReadAllText(outPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(recomputed == Committed(),
            LedgerRelativePath + " is stale, or a frozen pair artifact moved. Regenerate with "
            + "`python3 " + ScriptRelativePath + " --ledger` and name the cause of every delta in "
            + "the PR body (annex A-1.12 registers this ledger as the instrument that reads "
            + "PP-W-rows' outcome; the row is append-only and may not be edited to match).");
    }

    /// <summary>The ledger is a pure function of frozen inputs: two runs agree byte for byte.</summary>
    [SkippableFact]
    public void RecomputationIsByteStableAndTimestampFree()
    {
        var tmp = NewScratch("calor-ppw-stable-");
        var first = Path.Combine(tmp, "a.json");
        var second = Path.Combine(tmp, "b.json");
        Assert.Equal(0, RunScript("--ledger", "--out", first).Exit);
        Assert.Equal(0, RunScript("--ledger", "--out", second).Exit);
        Assert.Equal(File.ReadAllText(first), File.ReadAllText(second));

        var text = Committed();
        Assert.DoesNotContain("T00:", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}", text);
        Assert.DoesNotContain(RepoRoot(), text, StringComparison.Ordinal);
    }

    // --------------------------------------------------- the registered contents

    [Fact]
    public void LedgerCarriesTheSixRegisteredPairsWithTheirClasses()
    {
        using var document = JsonDocument.Parse(Committed());
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();
        Assert.Equal(RegisteredPairs.Length, pairs.Count);

        foreach (var (directory, cellClass, blind, legB) in RegisteredPairs)
        {
            var pair = pairs.Single(p => p.GetProperty("directory").GetString() == directory);
            Assert.Equal(cellClass, pair.GetProperty("class").GetString());
            Assert.Equal(blind, pair.GetProperty("blind").GetBoolean());
            Assert.Equal(legB, pair.GetProperty("legB").GetBoolean());
            Assert.Equal(2, pair.GetProperty("effectObservingTests").GetArrayLength());
            foreach (var arm in new[] { "A", "B" })
            {
                var sha = pair.GetProperty("starterBlobs").GetProperty(arm).GetProperty("sha").GetString();
                Assert.False(string.IsNullOrWhiteSpace(sha), directory + " arm " + arm);
                Assert.Equal(40, sha!.Length);
            }

            // The frozen per-arm seeded-compile multisets, all three registered roles.
            var compiles = pair.GetProperty("frozenCompiles");
            foreach (var role in new[] { "starter", "shortcut", "clean" })
            {
                Assert.True(compiles.TryGetProperty(role, out var cell), directory + "/" + role);
                Assert.True(cell.TryGetProperty("A", out _));
                Assert.True(cell.TryGetProperty("B", out _));
            }
        }

        Assert.Equal(3, pairs.Count(p => p.GetProperty("blind").GetBoolean()));
        Assert.Equal(5, pairs.Count(p => p.GetProperty("legB").GetBoolean()));
    }

    /// <summary>
    /// The shape-realized indicator's own obligation (A-1.12, #1123 M2): for
    /// every pair it must be FALSE on that pair's own frozen starter and TRUE
    /// on its frozen <c>clean</c> seed. An indicator satisfiable by the starter
    /// measures nothing — the review found W-004's vacuously true.
    /// </summary>
    [Fact]
    public void EveryIndicatorIsFalseOnItsStarterAndTrueOnItsCleanSeed()
    {
        using var document = JsonDocument.Parse(Committed());
        foreach (var pair in document.RootElement.GetProperty("pairs").EnumerateArray())
        {
            var name = pair.GetProperty("directory").GetString();
            Assert.False(string.IsNullOrWhiteSpace(pair.GetProperty("shapeRealizedIndicator").GetString()),
                name + " has no shapeRealizedIndicator");
            var check = pair.GetProperty("indicatorSelfCheck");
            Assert.True(check.GetProperty("falseOnStarter").GetBoolean(), name + ": indicator matches its own starter");
            Assert.True(check.GetProperty("trueOnClean").GetBoolean(), name + ": indicator misses its clean seed");
            Assert.True(check.GetProperty("ok").GetBoolean(), name);
        }
    }

    /// <summary>The load-bearing constants A-1.12 freezes, read off the ledger.</summary>
    [Fact]
    public void LedgerCarriesTheRegisteredConstants()
    {
        using var document = JsonDocument.Parse(Committed());
        var root = document.RootElement;
        var constants = root.GetProperty("constants");
        Assert.Equal(0.0, constants.GetProperty("legABar").GetDouble());
        Assert.Equal(0.5, constants.GetProperty("legAEffectSize").GetDouble());
        Assert.Equal(2, constants.GetProperty("blindFloor").GetInt32());
        Assert.Equal(1.20, constants.GetProperty("margin").GetDouble());
        Assert.Equal(1.0, constants.GetProperty("lowerBoundGate").GetDouble());
        Assert.Equal(0.41, constants.GetProperty("cvCap").GetDouble());
        Assert.Equal(2, constants.GetProperty("minRunsPerCell").GetInt32());
        Assert.Equal(0.40, constants.GetProperty("censorCap").GetDouble());
        Assert.Equal(150.0, constants.GetProperty("spendCeilingUsd").GetDouble());
        Assert.Equal(4537, constants.GetProperty("seed").GetInt32());

        // The pre-registered sensitivity is an OBLIGATION, not a courtesy: the
        // registered 1.20, the pooled 1.30 and w5-parity-002's 1.35.
        Assert.Equal(new[] { 1.20, 1.30, 1.35 },
            constants.GetProperty("sensitivityMargins").EnumerateArray().Select(e => e.GetDouble()));

        Assert.Equal("NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT", root.GetProperty("precedence").GetString());
        Assert.Equal("283ec9f9964ddd5b21da15b646a0dd77d53de99e",
            root.GetProperty("arms").GetProperty("A").GetProperty("commit").GetString());
        Assert.Equal("3bb2601e0cbd93fc25fdaaf2a0ea5183b8a2dd6a",
            root.GetProperty("arms").GetProperty("B").GetProperty("commit").GetString());
    }

    /// <summary>
    /// The escape semantics, which A-1.12 registers AGAINST the pair specs'
    /// <c>escapedBugs</c> rule: an escape is at least one named
    /// <c>effectObservingTest</c> failing on a workspace that BUILT. Getting it
    /// backwards inverts leg A's sign, because arm B is precisely the arm on
    /// which the laundering shortcut does not compile.
    /// </summary>
    [Fact]
    public void LedgerStatesTheRegisteredEscapeSemantics()
    {
        using var document = JsonDocument.Parse(Committed());
        var semantics = document.RootElement.GetProperty("escapeSemantics").GetString()!;
        Assert.Contains("effectObservingTests", semantics, StringComparison.Ordinal);
        Assert.Contains("BUILT", semantics, StringComparison.Ordinal);
        Assert.Contains("did not build at declared-done", semantics, StringComparison.Ordinal);
        Assert.Contains("INVERTS", semantics, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the mutations

    /// <summary>
    /// Gate 10's pin, executed: <i>dropping a pair fails the test</i>. The
    /// scratch pair tree is otherwise identical, and the control below proves
    /// the harness itself reproduces the committed bytes.
    /// </summary>
    [SkippableFact]
    public void DroppingOnePairChangesTheRecomputation()
    {
        var tmp = NewScratch("calor-ppw-mutation-");
        var pairs = CopyPairs(tmp);

        var control = Path.Combine(tmp, "control.json");
        Assert.Equal(0, RunScript("--ledger", "--pairs-root", pairs, "--out", control).Exit);
        Assert.Equal(Committed(), File.ReadAllText(control).Replace("\r\n", "\n", StringComparison.Ordinal));

        Directory.Delete(Path.Combine(pairs, "W-006-map-doubler"), recursive: true);
        var mutated = Path.Combine(tmp, "mutated.json");
        var (exit, _) = RunScript("--ledger", "--pairs-root", pairs, "--out", mutated);
        // Dropping a pair either fails the recomputation outright or changes its bytes;
        // both are red, and neither can be mistaken for the committed ledger.
        if (exit == 0)
        {
            Assert.NotEqual(Committed(), File.ReadAllText(mutated).Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Gate 10's other pin: <i>editing one frozen per-arm multiset fails the
    /// test</i>. W-001's arm-B shortcut is exit 1 with <c>error Calor0410</c>
    /// at (26,7) — flip the exit code and the ledger no longer matches.
    /// </summary>
    [SkippableFact]
    public void EditingOneFrozenMultisetChangesTheRecomputation()
    {
        var tmp = NewScratch("calor-ppw-multiset-");
        var pairs = CopyPairs(tmp);
        var compilesPath = Path.Combine(pairs, "ppw-seeded-compiles.json");

        var text = File.ReadAllText(compilesPath);
        using (var document = JsonDocument.Parse(text))
        {
            var target = document.RootElement.GetProperty("compiles").EnumerateArray()
                .Single(c => c.GetProperty("pair").GetString() == "W-001-middleware-stage"
                             && c.GetProperty("role").GetString() == "shortcut"
                             && c.GetProperty("arm").GetString() == "B");
            Assert.Equal(1, target.GetProperty("exitCode").GetInt32());
        }

        File.WriteAllText(compilesPath, text.Replace("\"exitCode\": 1", "\"exitCode\": 0", StringComparison.Ordinal));
        var mutated = Path.Combine(tmp, "mutated.json");
        Assert.Equal(0, RunScript("--ledger", "--pairs-root", pairs, "--out", mutated).Exit);
        Assert.NotEqual(Committed(), File.ReadAllText(mutated).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------- the epoch half

    /// <summary>
    /// Until the 0.16.0 release PR's author runs the epoch, the ledger's
    /// verdict is NOT-ADJUDICATED with the reason recorded — the PP-E1 pattern
    /// (a ledger may not carry a verdict its own numbers do not imply).
    /// </summary>
    [Fact]
    public void BeforeTheEpochRunsTheVerdictIsNotAdjudicated()
    {
        Skip.If(Directory.Exists(Rel(EpochRelativePath)),
            "epoch w-rows-001 has been archived; the run assertions below cover it");

        using var document = JsonDocument.Parse(Committed());
        var root = document.RootElement;
        Assert.False(root.GetProperty("epochRun").GetBoolean());
        Assert.Equal("NOT-ADJUDICATED", root.GetProperty("verdict").GetString());
        Assert.Contains("has not run", root.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("legA").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("legB").ValueKind);
        Assert.Equal(0, root.GetProperty("perCell").GetArrayLength());
    }

    /// <summary>
    /// Once the epoch is archived: the denominators in the ledger are the ones
    /// the epoch's <c>pins.json</c> carried (never a script default), every
    /// cell publishes its shape-realized indicator beside its escape rate and
    /// its non-building count, and leg B publishes point and bound at all three
    /// candidate margins with the verdict read at 1.20 only.
    /// </summary>
    [SkippableFact]
    public void WhenTheEpochIsArchivedTheLedgerCarriesItsPerCellReporting()
    {
        Skip.If(!Directory.Exists(Rel(EpochRelativePath)), "epoch w-rows-001 not archived yet");

        using var document = JsonDocument.Parse(Committed());
        var root = document.RootElement;
        Assert.True(root.GetProperty("epochRun").GetBoolean());
        Assert.Contains(root.GetProperty("verdict").GetString(),
            new[] { "NOT-ADJUDICATED", "MISS", "UNDERPOWERED", "HIT" });

        Assert.Equal(
            RegisteredPairs.Where(p => p.LegB).Select(p => p.Directory).Order(StringComparer.Ordinal),
            root.GetProperty("legBPairsFromPins").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(
            RegisteredPairs.Where(p => p.Blind).Select(p => p.Directory).Order(StringComparer.Ordinal),
            root.GetProperty("blindPairsFromPins").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal));

        var cells = root.GetProperty("perCell").EnumerateArray().ToList();
        Assert.Equal(RegisteredPairs.Length * 2, cells.Count);
        foreach (var cell in cells)
        {
            Assert.True(cell.TryGetProperty("escapeRate", out _));
            Assert.True(cell.TryGetProperty("shapeRealizedRate", out _));
            Assert.True(cell.TryGetProperty("didNotBuildAtDeclaredDone", out _));
            Assert.True(cell.TryGetProperty("escapeCategories", out _));
        }

        var sensitivity = root.GetProperty("legB").GetProperty("sensitivity").EnumerateArray().ToList();
        Assert.Equal(new[] { 1.20, 1.30, 1.35 }, sensitivity.Select(s => s.GetProperty("margin").GetDouble()));
        Assert.Single(sensitivity.Where(s => s.GetProperty("registered").GetBoolean()));
        foreach (var entry in sensitivity)
        {
            Assert.True(entry.TryGetProperty("pointEstimate", out _));
            Assert.True(entry.TryGetProperty("lowerBound95", out _));
        }
    }

    // ------------------------------------------------------------------ plumbing

    private string NewScratch(string prefix)
    {
        var tmp = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        return tmp;
    }

    private static string CopyPairs(string tmp)
    {
        var target = Path.Combine(tmp, "pairs");
        Directory.CreateDirectory(target);
        var source = Rel(PairsRelativePath);
        File.Copy(Path.Combine(source, "ppw-seeded-compiles.json"),
            Path.Combine(target, "ppw-seeded-compiles.json"));
        foreach (var (directory, _, _, _) in RegisteredPairs)
        {
            CopyDirectory(Path.Combine(source, directory), Path.Combine(target, directory));
        }

        return target;
    }

    private static (int Exit, string Log) RunScript(params string[] args)
    {
        Skip.If(!Python3OnPath(), "python3 not on PATH");
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Rel(ScriptRelativePath));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["LC_ALL"] = "C";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";

        using var process = Process.Start(start);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static bool Python3OnPath()
    {
        try
        {
            var probe = new ProcessStartInfo("python3", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(probe);
            if (process == null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
