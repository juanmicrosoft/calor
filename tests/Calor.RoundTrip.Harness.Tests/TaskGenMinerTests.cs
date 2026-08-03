using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Pins the bug-fix miner's fix-shape heuristic and the <c>git log --name-status</c> parser
/// (D-W4.1 primary source). Pure over canned git output — the live mining against a checked-out
/// submodule is a Slice-E activity (the OSS submodules are absent here).
/// </summary>
public class TaskGenMinerTests
{
    [Theory]
    [InlineData("Fix off-by-one in range check", true)]
    [InlineData("fixes #1234 incorrect boundary", true)]
    [InlineData("Bug: null reference in validator", true)]
    [InlineData("Correct wrong comparison operator", true)]
    [InlineData("Fix boundary error (#42)", true)]              // fix-word WITH a PR ref is still a fix
    [InlineData("Add new feature for logging", false)]
    [InlineData("Refactor internal API", false)]
    [InlineData("Merge branch 'main' into dev", false)]
    // A bare (#NNNN) / GH-NNNN reference is NOT fix-evidence: GitHub squash-merges append it to EVERY
    // PR subject, so these feature-adds/refactors must NOT be classified as fixes (honesty pin — a
    // bare number would grossly overstate the gold-standard bug-fix supply).
    [InlineData("Inline TraceId and SpanId JSON string formatting (#2215)", false)]
    [InlineData("Property factory as AddPropertyIfAbsent parameter (#2149)", false)]
    [InlineData("Add `LevelAlias.Off` (#1910)", false)]
    [InlineData("Update dependencies GH-77", false)]
    [InlineData("", false)]
    public void IsFixShaped_Heuristic(string subject, bool expected)
        => Assert.Equal(expected, BugfixMiner.IsFixShaped(subject));

    [Theory]
    [InlineData("test/MediatR.Tests/PipelineTests.cs", true)]
    [InlineData("src/MediatR/Mediator.cs", false)]
    [InlineData("tests/Foo/BarTests.cs", true)]
    [InlineData("src/Lib/ValidatorTest.cs", true)]
    public void IsTestPath_Heuristic(string path, bool expected)
        => Assert.Equal(expected, BugfixMiner.IsTestPath(path));

    [Fact]
    public void ParseFixCommits_ClassifiesSourceAndTestFiles()
    {
        // Two records delimited by ; header split by ; name-status lines follow.
        // (Fixed-length \u escapes — \x is variable-length and would swallow following hex letters.)
        var log =
            "abc123Fix off-by-one in Send #42\n" +
            "M\tsrc/MediatR/Mediator.cs\n" +
            "M\ttest/MediatR.Tests/SendTests.cs\n" +
            "def456Add caching feature\n" +
            "A\tsrc/MediatR/Cache.cs\n";

        var commits = BugfixMiner.ParseFixCommits(log, "src/MediatR");

        var fix = Assert.Single(commits); // only the fix-shaped commit is kept
        Assert.Equal("abc123", fix.Sha);
        Assert.Equal("src/MediatR/Mediator.cs", Assert.Single(fix.SourceFiles));
        Assert.Equal("test/MediatR.Tests/SendTests.cs", Assert.Single(fix.TestFiles));
    }

    [Fact]
    public void SelectRevertCandidates_RequiresBothSourceAndTest()
    {
        var withBoth = new FixCommit { Sha = "a", Subject = "fix", ChangedFiles = [] };
        withBoth.SourceFiles.Add("src/X.cs");
        withBoth.TestFiles.Add("test/XTests.cs");

        var srcOnly = new FixCommit { Sha = "b", Subject = "fix", ChangedFiles = [] };
        srcOnly.SourceFiles.Add("src/Y.cs");

        var selected = BugfixMiner.SelectRevertCandidates([withBoth, srcOnly]);
        Assert.Equal("a", Assert.Single(selected).Sha);
    }

    [Fact]
    public void GitLogArgs_SurfacesFullNameStatus_NotPathspecRestricted()
    {
        // Pathspec-fix regression pin: the log MUST NOT be pathspec-restricted to the library source,
        // or --name-status never lists test files and SelectRevertCandidates (which requires a covering
        // test) can never fire — the 0-live-tasks bug. Classification into source/test is the parser's job.
        var args = BugfixMiner.GitLogArgs(200);
        Assert.Contains("--name-status", args);
        Assert.Contains("--no-merges", args);
        Assert.Contains("-n 200", args);
        Assert.DoesNotContain("-- \"", args);        // no pathspec restriction
        Assert.DoesNotContain("src/", args);
    }
}
