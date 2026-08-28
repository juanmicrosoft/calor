using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 E2 slice a — the corpus-regression pin for Decision 1 (design-doc §3.2).
///
/// <para>Line adjacency is a <b>breaking change to forms that parse today</b>:
/// <c>§I{str:m} §E{cw}</c> (Y1b), <c>§O{void} §E{cw}</c> (X2b) and
/// <c>-&gt; void §E{cw}</c> (Y5a) all compile on main with the <c>§E</c> read as the
/// declaration's own row, and after this slice each is the annotated type's row
/// instead. The entire "zero regressions" claim rests on one measured fact: <b>no
/// committed .calr writes any of those forms</b>. §3.2 measured it once, by hand,
/// at one commit.</para>
///
/// <para>This test keeps measuring it <b>over the swept corpus</b>. If someone adds a
/// file using a same-line <c>§E</c>, the meaning of that file changed under this feature
/// and the change has to be looked at rather than discovered later.</para>
///
/// <para><b>What is deliberately outside the sweep, stated rather than implied.</b> Two
/// fixture sets are excluded because rows are their subject matter, not an accident:
/// <c>docs/design/spikes/</c> (the emitter spike's before/after evidence) and the
/// PP-W-rows measurement fixtures and epoch archives <see cref="PpwFixture"/> names.
/// Measured on this PR's tree: <b>928 files are swept</b>, and <b>39 excluded files do
/// write a meaning-changing form</b> — 8 spike artifacts and 31 PP-W-rows fixtures (the
/// per-arm <c>after/</c> starters carry inline parameter rows verbatim, and the seeded
/// mutants carry field and binding rows). That number grows with every PP-W-rows epoch
/// archive. The disposition is deliberate: those files were authored under the line rule
/// as inputs to a measurement, so they are not regressions of §3.2's claim, and sweeping
/// them would turn the pin into a permanent red. Nothing outside these two sets is
/// exempt.</para>
///
/// <para>It is deliberately a <b>shape</b> pin, not a compile sweep: the full
/// committed-corpus compile (886 at §3.2; 926 since PP-E1 leg B's 40 archived
/// <c>final-src/*.calr</c> solutions; 927 since #1104's crash-repro fixture; 936
/// since ES-08's nine edit-script snapshots — see the count pin below) is the
/// <c>compile-all-committed-calr</c> CI leg (gate 5), and
/// the 23-file two-line <c>§O</c>/<c>§E</c> subset is already pinned by
/// <c>o53/baseline.json</c> through P30.</para>
/// </summary>
public sealed class EffectRowCorpusShapeTests
{
    /// <summary>
    /// The forms §3.2 swept for, with the counts it recorded. Each is a form whose
    /// MEANING moves under the line rule, so each must stay at zero.
    /// </summary>
    private static readonly (string Name, string Pattern)[] MeaningChangingForms =
    [
        ("§O{…} §E{…} same line",   @"§O\{[^}]*\}[ \t]*§E\{"),
        ("§I{…} §E{…} same line",   @"§I\{[^}]*\}[ \t]*§E\{"),
        ("-> T §E{…} same line",    @"->[ \t]*[^ \t]+[ \t]+§E\{"),
        ("§FLD{…} §E{…} same line", @"§FLD\{[^}]*\}[ \t]*§E\{"),
        ("§B{…} §E{…} same line",   @"§B\{[^}]*\}[ \t]*§E\{"),
        ("inline parameter row",    @"\([^)]*§E\{"),
    ];

    /// <summary>
    /// The patterns above are line-scoped, and an inline parameter list may WRAP.
    /// Executed case <b>Z5</b> confirms wrapped lists are really written, which is why
    /// §3.1 calls Z3 "not hypothetical" — a row on a continuation line inside an open
    /// <c>(</c> is exactly the Z3 shape, and it is invisible to a per-line regex. The
    /// sweep would report zero while the corpus held one. This walks each file tracking
    /// paren depth instead.
    /// </summary>
    private static IEnumerable<string> WrappedInlineParameterRows(string relative, string[] lines)
    {
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (depth > 0 && line.Contains("§E{", StringComparison.Ordinal))
                yield return $"{relative}:{i + 1}: [wrapped inline parameter row] {line.Trim()}";

            foreach (var c in line)
            {
                if (c == '(') depth++;
                else if (c == ')' && depth > 0) depth--;
            }
        }
    }

    /// <summary>
    /// Committed <c>.calr</c> written AFTER Decision 1 landed, in the new syntax, on
    /// purpose. The sweep below guards §3.2's "zero regressions" claim — that no
    /// file written BEFORE the line rule changes meaning under it — and a file
    /// authored under the rule is not a regression of it. Each entry names why it
    /// exists; adding one is a review event (the sweep's own message says so), not
    /// a silent widening.
    ///
    /// <list type="bullet">
    /// <item><c>tests/TestData/QueryCorpus/project/app.calr</c> — v0.15 E5 (review
    /// round 1, #2): gate 7's polymorphic golden, <c>Map&lt;eff e&gt; (Func&lt;i32,i32&gt;:f §E{e}, …)</c>,
    /// pins that the inferred row of a rank-1 body keeps its variable part.</item>
    /// <item><c>tests/TestData/EditScripts/ES-08-effect-row-edit/step-00-clean/combinators.calr</c>
    /// and <c>…/step-01-callee-row-widens/combinators.calr</c> — v0.16 kickoff sweep: the
    /// effect-row edit script (F-3′ §6), whose callee is the same rank-1 shape
    /// (<c>Map&lt;eff e&gt; (…, Func&lt;i32,i32&gt;:f §E{e})</c>) so that a cross-module
    /// caller instantiates a row under the driver cache. Step 2 erases the row and is
    /// therefore NOT allowlisted — it writes no same-line row.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> AuthoredUnderDecisionOne = new(StringComparer.Ordinal)
    {
        "tests/TestData/QueryCorpus/project/app.calr",
        "tests/TestData/EditScripts/ES-08-effect-row-edit/step-00-clean/combinators.calr",
        "tests/TestData/EditScripts/ES-08-effect-row-edit/step-01-callee-row-widens/combinators.calr",
    };

    [Fact]
    public void NoCommittedCalrWritesAFormWhoseMeaningTheLineRuleChanges()
    {
        var root = RepositoryRoot();
        var files = CommittedCalrFiles(root);

        // §3.2 and §9 both quote 886 — the corpus the design doc's argument was
        // measured on. PP-E1 leg B (epoch e1-rows-parity-001) archived its 40 declared-done
        // solutions as final-src/*.calr, exactly as w5-parity-002 does, so the committed
        // corpus is 926 = 886 + 40 since then; 927 with #1104's crash-repro fixture
        // (tests/Calor.Enforcement.Tests/Scenarios/Effects/Issue1104_BatchingSink_LoopAsync.calr,
        // v0.16 W3(c)); and 936 = 927 + 9 since the v0.16 kickoff sweep registered ES-08
        // (three steps × three files under tests/TestData/EditScripts/ES-08-effect-row-edit/).
        // The sweep below still covers every file. A drift from 936 means the sweep is no
        // longer measuring the corpus it claims to.
        Assert.Equal(936, files.Count);

        // The allowlist must not go stale: an entry earns its place by actually
        // writing a same-line row, and it must still be a committed file.
        foreach (var allowed in AuthoredUnderDecisionOne)
        {
            Assert.Contains(allowed, files);
            var allowedLines = File.ReadAllLines(Path.Combine(root, allowed));
            Assert.True(
                allowedLines.Any(line => MeaningChangingForms.Any(form => Regex.IsMatch(line, form.Pattern))),
                $"{allowed} is allowlisted but writes no same-line row — remove it from AuthoredUnderDecisionOne.");
        }

        var offenders = new List<string>();
        foreach (var relative in files)
        {
            if (AuthoredUnderDecisionOne.Contains(relative))
                continue;

            var lines = File.ReadAllLines(Path.Combine(root, relative));
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (name, pattern) in MeaningChangingForms)
                {
                    if (Regex.IsMatch(lines[i], pattern))
                        offenders.Add($"{relative}:{i + 1}: [{name}] {lines[i].Trim()}");
                }
            }

            offenders.AddRange(WrappedInlineParameterRows(relative, lines));
        }

        Assert.True(offenders.Count == 0,
            "Design-doc §3.2 measured ZERO committed .calr writing a same-line effect row, and the "
            + "\"zero regressions\" claim for line adjacency rests on that. These files now write one, "
            + "so their meaning changed under Decision 1 — review each before this ships:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The compact two-line arrow form is the dominant real corpus shape (§3.2:
    /// 2948 occurrences across 471 files) and the rule provably cannot reach it. Kept
    /// as a live floor rather than a quoted number, so the sweep above is known to be
    /// looking at a corpus that still contains the form it must not disturb.
    /// </summary>
    [Fact]
    public void TheTwoLineArrowFormIsStillTheDominantShape()
    {
        var root = RepositoryRoot();
        var files = CommittedCalrFiles(root);
        var arrowThenNewline = new Regex(@"->[ \t]*[^ \t]+[ \t]*$");

        var occurrences = files
            .SelectMany(relative => File.ReadAllLines(Path.Combine(root, relative)))
            .Count(line => arrowThenNewline.IsMatch(line));

        Assert.True(occurrences >= 2900,
            $"Expected the two-line arrow form to remain dominant (§3.2 measured 2948); saw {occurrences}.");
    }

    /// <summary>
    /// <c>docs/design/spikes/</c> is excluded for the same reason
    /// <c>HigherOrderDemandLedgerTests</c>, <c>LosslessFormattingTests</c> and
    /// <c>facts.py</c> exclude it (§13.5(b)): the emitter spike commits before/after
    /// .calr fixtures as EVIDENCE, and they are deliberately full of rows. The
    /// PP-W-rows measurement fixtures and epoch archives <see cref="PpwFixture"/>
    /// names are excluded by the same rule and for the same reason — they are those
    /// fixtures again, as per-arm starters plus seeded mutants. The class summary
    /// records how many excluded files actually write a meaning-changing form.
    /// </summary>

    private static IReadOnlyList<string> CommittedCalrFiles(string root)
    {
        var process = Process.Start(new ProcessStartInfo("git", "ls-files *.calr")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !line.StartsWith("docs/design/spikes/", StringComparison.Ordinal))
            // Harness scratch that is not product corpus, excluded the way docs/design/spikes/ is:
            // (1) templates/ — the arm csproj template and the permissive canary run-pair.sh compiles
            //     before a pre-rows epoch (v0.16 W1), a program written to draw Calor0410;
            // (2) the PP-W-rows fixtures and epoch archives PpwFixture names (roadmap v0.16 §4.1,
            //     S3 (c)). W1 landed the seeded-only form and asked for the STARTERS to be
            //     dispositioned when the pairs landed; PpwFixture records that disposition and why
            //     the other two options it listed are unavailable. Counts are unchanged either way.
            .Where(line => !line.StartsWith("bench/phase0-agent-native/templates/", StringComparison.Ordinal))
            .Where(line => !PpwFixture.IsMatch(line))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
