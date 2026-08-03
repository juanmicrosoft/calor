using System.Text.RegularExpressions;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>One fix-shaped commit mined from upstream history.</summary>
public sealed class FixCommit
{
    public required string Sha { get; init; }
    public required string Subject { get; init; }
    public required List<string> ChangedFiles { get; init; }

    /// <summary>Source files the commit touched under the library source prefix.</summary>
    public List<string> SourceFiles { get; init; } = new();

    /// <summary>Test files the commit touched (path contains a test-ish segment).</summary>
    public List<string> TestFiles { get; init; } = new();
}

/// <summary>
/// Mines a pinned submodule's git history for bug-fix commits whose fix touches a library source
/// region AND has an identifiable covering test — the revert-bugfix gold-standard task source
/// (D-W4.1 primary). The commit-shape heuristic and the <c>git log --name-status</c> parser are
/// pure (unit-testable with canned output); the live mining shells <c>git</c> against a checked-out
/// submodule (Slice E — the corpus submodules are absent in the Slice-C environment).
/// </summary>
public static partial class BugfixMiner
{
    /// <summary>
    /// Fix-shaped iff the subject line names a fix/bug/defect or references an issue, and is not a
    /// merge or a mere "prefix"/"suffix" false positive. Deliberately conservative — a false
    /// negative just drops a candidate; a false positive would revert a non-fix.
    /// </summary>
    public static bool IsFixShaped(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        if (subject.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase)) return false;
        return FixWord().IsMatch(subject) || IssueRef().IsMatch(subject);
    }

    /// <summary>True when the path looks like a test file (a test-ish directory or filename segment).</summary>
    public static bool IsTestPath(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/test/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
            || p.Contains(".Tests/", StringComparison.Ordinal)
            || p.Contains(".Test/", StringComparison.Ordinal)
            || Path.GetFileName(p).Contains("Tests.", StringComparison.Ordinal)
            || Path.GetFileName(p).Contains("Test.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Parse the output of
    /// <c>git log --name-status --pretty=format:%x01%H%x02%s</c>
    /// into fix-shaped commits, classifying each changed <c>.cs</c> path as source (under
    /// <paramref name="librarySourcePrefix"/>) or test.
    /// </summary>
    public static List<FixCommit> ParseFixCommits(string gitLog, string librarySourcePrefix)
    {
        var commits = new List<FixCommit>();
        var prefix = librarySourcePrefix.Replace('\\', '/').TrimEnd('/');

        // Records are delimited by \x01; within a record, header (\x02-split) then name-status lines.
        foreach (var record in gitLog.Split('\x01', StringSplitOptions.RemoveEmptyEntries))
        {
            var headerSplit = record.Split('\x02', 2);
            if (headerSplit.Length < 2) continue;
            var sha = headerSplit[0].Trim();
            var rest = headerSplit[1];
            var lines = rest.Split('\n');
            var subject = lines.Length > 0 ? lines[0].Trim() : "";
            if (!IsFixShaped(subject)) continue;

            var changed = new List<string>();
            foreach (var line in lines.Skip(1))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                // name-status: "M\tpath" (or R100\told\tnew). Take the last tab-separated field as the path.
                var parts = t.Split('\t');
                if (parts.Length < 2) continue;
                var path = parts[^1].Replace('\\', '/');
                changed.Add(path);
            }
            if (changed.Count == 0) continue;

            var commit = new FixCommit { Sha = sha, Subject = subject, ChangedFiles = changed };
            foreach (var path in changed)
            {
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsTestPath(path)) commit.TestFiles.Add(path);
                else if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) commit.SourceFiles.Add(path);
            }
            commits.Add(commit);
        }
        return commits;
    }

    /// <summary>
    /// Candidate fix commits worth reverting: fix-shaped AND touching at least one library source
    /// file AND at least one test file (an identifiable covering test). The reverted-fix defect is
    /// the gold standard because it is a REAL defect.
    /// </summary>
    public static List<FixCommit> SelectRevertCandidates(IEnumerable<FixCommit> commits) =>
        commits.Where(c => c.SourceFiles.Count > 0 && c.TestFiles.Count > 0).ToList();

    /// <summary>The <c>git log</c> invocation whose output <see cref="ParseFixCommits"/> parses.</summary>
    public static string GitLogArgs(string librarySourcePrefix, int maxCommits) =>
        $"log --name-status --no-merges -n {maxCommits} " +
        $"--pretty=format:%x01%H%x02%s -- \"{librarySourcePrefix}\"";

    [GeneratedRegex(@"\b(fix(es|ed)?|bug|defect|regression|incorrect|wrong|broken)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FixWord();

    [GeneratedRegex(@"#\d+|GH-\d+", RegexOptions.IgnoreCase)]
    private static partial Regex IssueRef();
}
