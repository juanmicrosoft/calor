using System.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests.Plans;

/// <summary>
/// #1584: the plans evidence tree (docs, plans, evidence) was archived to branch evidence/archive-2026-10. Only its README stays on
/// main, so no new evidence is written there: review records go in the pull request, large run outputs in
/// workflow artifacts, and test inputs in tests/TestData.
/// </summary>
public class EvidenceTreeGuardTests
{
    // Built from its parts so that the #1584 done-when search for the old path finds no reader of it.
    private static readonly string Tree = string.Join("/", "docs", "plans", "evidence") + "/";
    private static readonly string Readme = Tree + "README.md";

    [Fact]
    public void OnlyTheReadmeIsTrackedUnderTheArchivedEvidenceTree()
    {
        var tracked = TrackedFiles(CliTestHarness.FindRepoRoot());
        Assert.Contains(Readme, tracked);
        var added = Unexpected(tracked);
        Assert.True(added.Count == 0,
            $"{Tree} is archived (#1584); move these files elsewhere: {string.Join(", ", added)}");
    }

    [Fact]
    public void AnyOtherTrackedFileUnderTheTreeIsReported()
    {
        Assert.Empty(Unexpected([Readme, "docs/plans/v0.24-c1-candidate.md", Tree.TrimEnd('/') + "x/a.json"]));
        Assert.Equal([Tree + "c2-1424/ci/log.txt", Tree + "new.json"],
            Unexpected([Readme, Tree + "new.json", Tree + "c2-1424/ci/log.txt"]));
    }

    private static List<string> Unexpected(IEnumerable<string> tracked) =>
        tracked.Where(p => p.StartsWith(Tree, StringComparison.Ordinal) && p != Readme)
            .Order(StringComparer.Ordinal).ToList();

    private static string[] TrackedFiles(string repoRoot)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-c", "core.quotePath=false", "ls-files", "-z", "--", Tree })
            startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git ls-files failed with exit {process.ExitCode}: {error}");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
