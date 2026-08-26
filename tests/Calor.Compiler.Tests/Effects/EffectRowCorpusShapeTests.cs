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
/// <para>This test keeps measuring it. If someone adds a file using a same-line
/// <c>§E</c>, the meaning of that file changed under this feature and the change has
/// to be looked at rather than discovered later.</para>
///
/// <para>It is deliberately a <b>shape</b> pin, not a compile sweep: the full
/// 886-file compile is the <c>compile-all-committed-calr</c> CI leg (gate 5), and
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

    [Fact]
    public void NoCommittedCalrWritesAFormWhoseMeaningTheLineRuleChanges()
    {
        var root = RepositoryRoot();
        var files = CommittedCalrFiles(root);

        // §3.2 and §9 both quote 886. A drift here means the sweep below is no
        // longer measuring the corpus the design doc's argument is about.
        Assert.Equal(886, files.Count);

        var offenders = new List<string>();
        foreach (var relative in files)
        {
            var lines = File.ReadAllLines(Path.Combine(root, relative));
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (name, pattern) in MeaningChangingForms)
                {
                    if (Regex.IsMatch(lines[i], pattern))
                        offenders.Add($"{relative}:{i + 1}: [{name}] {lines[i].Trim()}");
                }
            }
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
    /// .calr fixtures as EVIDENCE, and they are deliberately full of rows.
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
