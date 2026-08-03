using System.Diagnostics;
using System.Text;
using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Exercises the live bug-fix miner and the source-hunk-only revert against SYNTHETIC git fixtures
/// (a throwaway repo built per test) — no corpus submodule required. Covers the pathspec fix (test
/// files now surface in <c>--name-status</c> so <see cref="BugfixMiner.SelectRevertCandidates"/> fires)
/// and the critical correctness property: reverting a fix reintroduces the defect in the LIBRARY
/// SOURCE while the covering TEST is kept at its fixed state (never whole-commit-reverted).
/// </summary>
public sealed class TaskGenRevertTests : IDisposable
{
    private readonly string _repo;
    private const string Prefix = "src/Lib";

    public TaskGenRevertTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), $"calor-miner-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repo);
        Git("init -q");
        Git("config user.email test@calor.dev");
        Git("config user.name calor-test");
        Git("config commit.gpgsign false");
        Git("config core.autocrlf false");
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { /* best effort */ }
    }

    // ---- Pathspec fix: test files now surface, so revert candidates can be selected ----

    [Fact]
    public void MineFixCommits_SurfacesTestFiles_SoRevertCandidatesFire()
    {
        SeedInitial();
        CommitFix(); // fixes src AND touches the covering test

        var fixCommits = BugfixMiner.MineFixCommits(_repo, Prefix, 50);
        var candidates = BugfixMiner.SelectRevertCandidates(fixCommits);

        var fix = Assert.Single(candidates);
        Assert.Equal("src/Lib/Calc.cs", Assert.Single(fix.SourceFiles));
        // The pathspec-fix payoff: the covering test is now visible (was ALWAYS empty under the old
        // pathspec-restricted log), so SelectRevertCandidates (source>0 AND test>0) can fire at all.
        Assert.NotEmpty(fix.TestFiles);
        Assert.Contains("test/Lib.Tests/CalcTests.cs", fix.TestFiles);
    }

    // ---- Source-hunk-only revert: clean case reintroduces the defect in source, keeps the test ----

    [Fact]
    public void TryBuildRevertCandidate_ReintroducesDefectInSource_KeepsTest()
    {
        SeedInitial();
        CommitFix();

        var commit = Assert.Single(BugfixMiner.SelectRevertCandidates(BugfixMiner.MineFixCommits(_repo, Prefix, 50)));
        var result = BugfixMiner.TryBuildRevertCandidate(_repo, commit);

        Assert.True(result.Succeeded);
        var cand = result.Candidate!;
        Assert.Equal(MutationSource.RevertBugfix, cand.Source);
        Assert.Equal("src/Lib/Calc.cs", cand.FileRelPath);
        Assert.False(BugfixMiner.IsTestPath(cand.FileRelPath)); // the reverted file is SOURCE, never the test
        Assert.Equal(commit.Sha, cand.RevertedCommit);

        // Defect reintroduced: the reverted source carries the pre-fix (defective) comparison, not the fix.
        Assert.Contains("x < 10", cand.MutatedSource);
        Assert.DoesNotContain("x <= 10", cand.MutatedSource);

        // Correctness pivot: a WHOLE-COMMIT revert would also delete the covering test (the fix added it),
        // destroying the oracle. Prove the hazard is real AND that our candidate avoids it: the pre-fix
        // test differs from the fixed test (so a whole-commit revert loses coverage), yet our candidate's
        // mutated payload is the SOURCE file only — the generator keeps the test at its fixed state.
        var preFixTest = Show($"{commit.Sha}^:test/Lib.Tests/CalcTests.cs");
        var fixedTest = Show($"{commit.Sha}:test/Lib.Tests/CalcTests.cs");
        Assert.NotEqual(preFixTest, fixedTest); // the fix added covering-test content
        Assert.DoesNotContain("Boundary", cand.MutatedSource); // the test's covering method is NOT in the source payload
    }

    // ---- Far-away later change: still cleanly reverts via 3-way (permissive) ----

    [Fact]
    public void TryBuildRevertCandidate_LaterUnrelatedChange_StillRevertsCleanly()
    {
        SeedInitial();
        CommitFix();
        // A later commit edits a DIFFERENT method far from the fix — must not block the revert.
        Write("src/Lib/Calc.cs", CalcSource(fixComparison: true, extraMethodReturns: 999));
        CommitAll("tune Helper return value");

        var commit = FixCommit();
        var result = BugfixMiner.TryBuildRevertCandidate(_repo, commit);

        Assert.True(result.Succeeded);
        Assert.Contains("x < 10", result.Candidate!.MutatedSource);   // defect reintroduced
        Assert.Contains("Helper() => 999", result.Candidate!.MutatedSource); // later unrelated change preserved
    }

    // ---- Adjacent later change on the same line: inseparable → excluded (counted + disclosed) ----

    [Fact]
    public void TryBuildRevertCandidate_LaterChangeOnFixedLine_ExcludedInseparable()
    {
        SeedInitial();
        CommitFix();
        // A later commit rewrites the very line the fix touched → reverse-apply must conflict.
        Write("src/Lib/Calc.cs", CalcSource(fixComparison: true).Replace("x <= 10", "x <= 25"));
        CommitAll("bump threshold to 25");

        var commit = FixCommit();
        var result = BugfixMiner.TryBuildRevertCandidate(_repo, commit);

        Assert.False(result.Succeeded);
        Assert.Null(result.Candidate);
        Assert.Equal(ExclusionReason.InseparableRevert, result.Exclusion);
    }

    // ---- CRLF source files: patch must stay byte-exact or every revert spuriously "inseparable" ----

    [Fact]
    public void TryBuildRevertCandidate_CrlfSource_RevertsCleanly()
    {
        // Regression pin: a project with Windows line endings (e.g. FluentValidation) produces a CRLF
        // `git show` patch. If the git wrapper normalizes CRLF→LF, the patch stops context-matching the
        // CRLF worktree and EVERY revert is falsely reported inseparable. Store CRLF and require success.
        Write("src/Lib/Calc.cs", CalcSource(fixComparison: false).Replace("\n", "\r\n"));
        Write("test/Lib.Tests/CalcTests.cs", TestSource(withBoundary: false).Replace("\n", "\r\n"));
        CommitAll("initial");
        Write("src/Lib/Calc.cs", CalcSource(fixComparison: true).Replace("\n", "\r\n"));
        Write("test/Lib.Tests/CalcTests.cs", TestSource(withBoundary: true).Replace("\n", "\r\n"));
        CommitAll("Fix off-by-one boundary in Classify #42");

        var commit = FixCommit();
        var result = BugfixMiner.TryBuildRevertCandidate(_repo, commit);

        Assert.True(result.Succeeded);
        Assert.Contains("x < 10", result.Candidate!.MutatedSource);   // defect reintroduced
        Assert.Contains("\r\n", result.Candidate!.MutatedSource);      // CRLF preserved end-to-end
    }

    // ---- Multi-source-file fix: excluded (single-file predicate scope), disclosed ----

    [Fact]
    public void TryBuildRevertCandidate_MultipleSourceFiles_ExcludedAndDisclosed()
    {
        SeedInitial();
        Write("src/Lib/Other.cs", "namespace Lib; public static class Other { public static int V() => 1; }\n");
        CommitAll("add Other");
        // A fix touching TWO source files plus a test.
        Write("src/Lib/Calc.cs", CalcSource(fixComparison: true));
        Write("src/Lib/Other.cs", "namespace Lib; public static class Other { public static int V() => 2; }\n");
        Write("test/Lib.Tests/CalcTests.cs", TestSource(withBoundary: true));
        CommitAll("Fix off-by-one and Other #7");

        var commit = Assert.Single(BugfixMiner.SelectRevertCandidates(BugfixMiner.MineFixCommits(_repo, Prefix, 50)));
        Assert.Equal(2, commit.SourceFiles.Count);
        var result = BugfixMiner.TryBuildRevertCandidate(_repo, commit);

        Assert.False(result.Succeeded);
        Assert.Equal(ExclusionReason.MultipleSourceFiles, result.Exclusion);
        Assert.NotNull(result.ExclusionDetail);
    }

    // ---------- fixture helpers ----------

    private void SeedInitial()
    {
        Write("src/Lib/Calc.cs", CalcSource(fixComparison: false)); // pre-fix defect: x < 10
        Write("test/Lib.Tests/CalcTests.cs", TestSource(withBoundary: false));
        CommitAll("initial");
    }

    /// <summary>The bug-fix commit: corrects the boundary in source AND adds the covering test.</summary>
    private void CommitFix()
    {
        Write("src/Lib/Calc.cs", CalcSource(fixComparison: true));   // fixed: x <= 10
        Write("test/Lib.Tests/CalcTests.cs", TestSource(withBoundary: true)); // adds Boundary() covering test
        CommitAll("Fix off-by-one boundary in Classify #42");
    }

    private FixCommit FixCommit() =>
        Assert.Single(BugfixMiner.SelectRevertCandidates(BugfixMiner.MineFixCommits(_repo, Prefix, 50)));

    private static string CalcSource(bool fixComparison, int extraMethodReturns = 1)
    {
        var cmp = fixComparison ? "x <= 10" : "x < 10";
        // Classify (the fix site) and Helper (the far-away later-change site) are separated by ample
        // filler so an edit to one is well outside the other's diff context (>3 lines) — a genuine
        // far-away change that the 3-way reverse-apply must preserve.
        var sb = new StringBuilder();
        sb.Append("namespace Lib;\n");
        sb.Append("public static class Calc\n");
        sb.Append("{\n");
        sb.Append($"    public static int Classify(int x) => {cmp} ? 1 : 2;\n");
        for (var i = 0; i < 12; i++)
            sb.Append($"    public static int Pad{i}() => {i};\n");
        sb.Append($"    public static int Helper() => {extraMethodReturns};\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string TestSource(bool withBoundary)
    {
        var boundary = withBoundary
            ? "    public void Boundary() { /* covers the off-by-one at x==10 */ }\n"
            : "";
        return
            "namespace Lib.Tests;\n" +
            "public class CalcTests\n" +
            "{\n" +
            "    public void Basic() { }\n" +
            boundary +
            "}\n";
    }

    private void Write(string relPath, string content)
    {
        var abs = Path.Combine(_repo, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    private void CommitAll(string message)
    {
        Git("add -A");
        Git($"commit -q -m \"{message}\"");
    }

    private string Show(string spec) => Git($"show {spec}").Stdout;

    private (int Exit, string Stdout, string Stderr) Git(string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
