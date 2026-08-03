using System.Diagnostics;
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

/// <summary>Outcome of trying to build a source-hunk-only revert candidate from a fix commit.</summary>
public sealed class RevertCandidateResult
{
    public required FixCommit Commit { get; init; }

    /// <summary>Non-null when the source-hunk-only revert succeeded — the reverted (defective) candidate.</summary>
    public MutationCandidate? Candidate { get; init; }

    /// <summary>Non-null when the candidate could NOT be built (counted + disclosed exclusion).</summary>
    public ExclusionReason? Exclusion { get; init; }
    public string? ExclusionDetail { get; init; }

    public bool Succeeded => Candidate != null;
}

/// <summary>
/// Mines a pinned submodule's git history for bug-fix commits whose fix touches a library source
/// region AND has an identifiable covering test — the revert-bugfix gold-standard task source
/// (D-W4.1 primary). The commit-shape heuristic and the <c>git log --name-status</c> parser are
/// pure (unit-testable with canned output); the live mining and the source-hunk-only revert shell
/// <c>git</c> against a checked-out submodule (unit-testable with synthetic git fixtures, no corpus).
///
/// <para><b>Source-hunk-only revert (the correctness pivot):</b> reverting a bug-fix must reintroduce
/// the defect in the LIBRARY SOURCE while KEEPING the covering test at its fixed state (the test is
/// the held-out oracle that now fails). A whole-commit revert would delete the test too, destroying
/// the oracle. <see cref="TryBuildRevertCandidate"/> therefore reverse-applies ONLY the fix commit's
/// hunks for the single library-source file (via <c>git apply -R --3way</c> in a throwaway worktree
/// of the pinned tree) and splices JUST that reverted file into the generator's clone; the test files
/// in that clone stay at the pinned/fixed state untouched. A fix whose source hunk cannot be cleanly
/// reverse-applied — or that touches more than one source file — is excluded (counted + disclosed).</para>
/// </summary>
public static partial class BugfixMiner
{
    /// <summary>
    /// Fix-shaped iff the subject line names actual fix vocabulary (fix/bug/defect/regression/crash/…)
    /// and is not a merge. A bare issue/PR reference (<c>#NNNN</c>, <c>GH-NNNN</c>) is deliberately NOT
    /// evidence of a fix: GitHub squash-merges append <c>(#NNNN)</c> to EVERY PR subject, so treating a
    /// bare number as fix-evidence would classify every feature-add and refactor as a "fix" and grossly
    /// overstate the gold-standard bug-fix supply (reverting a feature-add is behavior-removal, not a
    /// reintroduced defect). Deliberately conservative — a false negative just drops a candidate; a
    /// false positive would revert a non-fix.
    /// </summary>
    public static bool IsFixShaped(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        if (subject.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase)) return false;
        return FixWord().IsMatch(subject);
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
                else if (path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                    || path.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    commit.SourceFiles.Add(path);
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

    /// <summary>
    /// The <c>git log</c> invocation whose output <see cref="ParseFixCommits"/> parses.
    ///
    /// <para><b>Pathspec fix:</b> the log is deliberately NOT pathspec-restricted to the library
    /// source prefix. The former <c>-- "src/…"</c> restriction meant <c>--name-status</c> only ever
    /// listed source files, so <see cref="FixCommit.TestFiles"/> was always empty and
    /// <see cref="SelectRevertCandidates"/> (which requires a covering test) could never fire — 0
    /// candidates on every real project. Surfacing the FULL name-status lets the parser see BOTH the
    /// source and the covering-test changes; classification into source/test happens in the parser.</para>
    /// </summary>
    public static string GitLogArgs(int maxCommits) =>
        $"log --name-status --no-merges -n {maxCommits} --pretty=format:%x01%H%x02%s";

    /// <summary>
    /// Live-mine <paramref name="repoDir"/>'s history for fix-shaped commits (most recent
    /// <paramref name="maxCommits"/>), classifying each against <paramref name="librarySourcePrefix"/>.
    /// Shells <c>git</c>; returns the parsed fix commits (unfiltered — call
    /// <see cref="SelectRevertCandidates"/> for the source+test subset).
    /// </summary>
    public static List<FixCommit> MineFixCommits(string repoDir, string librarySourcePrefix, int maxCommits)
    {
        var (exit, stdout, _) = Git(repoDir, GitLogArgs(maxCommits));
        if (exit != 0) return new List<FixCommit>();
        return ParseFixCommits(stdout, librarySourcePrefix);
    }

    /// <summary>
    /// Build a source-hunk-only revert candidate from a fix commit: reverse-apply ONLY the commit's
    /// hunks for its single library-source file onto the pinned tree, KEEPING the covering test at
    /// its fixed state (the test lives in a different file the generator never overwrites). The
    /// reverted (defective) full-file text becomes <see cref="MutationCandidate.MutatedSource"/>.
    ///
    /// <para>Excludes (counted + disclosed) when the fix touches more than one source file
    /// (<see cref="ExclusionReason.MultipleSourceFiles"/> — cannot map to the single-file eligibility
    /// predicate without reverting more than one region) or when the source hunk cannot be cleanly
    /// reverse-applied onto the pinned tree (<see cref="ExclusionReason.InseparableRevert"/> — a later
    /// commit changed the same region, so the defect cannot be reintroduced in isolation).</para>
    /// </summary>
    public static RevertCandidateResult TryBuildRevertCandidate(string repoDir, FixCommit commit)
    {
        // Single-source-file restriction: the eligibility predicate decides over ONE mutated file's
        // native conversion. A multi-source-file fix would require reverting >1 region to reintroduce
        // the defect faithfully — out of scope for the single-file predicate. Disclose, don't fudge.
        if (commit.SourceFiles.Count != 1)
            return Excluded(commit, ExclusionReason.MultipleSourceFiles,
                $"fix touches {commit.SourceFiles.Count} library source files; the single-file eligibility "
                + "predicate cannot faithfully reintroduce a multi-file defect — excluded.");

        var sourcePath = commit.SourceFiles[0];

        // The fix's diff for the SOURCE FILE ONLY (a/… b/… unified diff). Restricting the pathspec to
        // the source file is what keeps the covering test out of the revert entirely.
        var (showExit, patch, _) = Git(repoDir, $"show {commit.Sha} -- \"{sourcePath}\"");
        if (showExit != 0 || string.IsNullOrWhiteSpace(patch) || !patch.Contains("@@"))
            return Excluded(commit, ExclusionReason.InseparableRevert,
                $"could not obtain a source-file diff for {Short(commit.Sha)} on {sourcePath}.");

        string? worktree = null;
        var patchFile = Path.Combine(Path.GetTempPath(), $"calor-revert-{Guid.NewGuid():N}.patch");
        try
        {
            worktree = Path.Combine(Path.GetTempPath(), $"calor-revert-wt-{Guid.NewGuid():N}");
            var (wtExit, _, wtErr) = Git(repoDir, $"worktree add --detach -q \"{worktree}\" HEAD");
            if (wtExit != 0)
                return Excluded(commit, ExclusionReason.InseparableRevert,
                    $"could not create a throwaway worktree to compute the revert: {wtErr.Trim()}");

            var worktreeSourceAbs = Path.Combine(worktree, sourcePath);
            if (!File.Exists(worktreeSourceAbs))
                return Excluded(commit, ExclusionReason.InseparableRevert,
                    $"source file {sourcePath} is absent at the pinned HEAD — cannot reverse-apply.");

            var pinnedContent = File.ReadAllText(worktreeSourceAbs);
            File.WriteAllText(patchFile, patch);

            // Reverse-apply with a 3-way merge so a later, non-overlapping change is preserved and an
            // overlapping one CONFLICTS LOUDLY (rather than a plain `git apply -R` silently no-op-ing on
            // shifted context). Any non-zero exit ⇒ the source hunk is not cleanly separable ⇒ exclude.
            var (applyExit, _, applyErr) = Git(worktree, $"apply -R --3way -p1 \"{patchFile}\"");
            if (applyExit != 0)
                return Excluded(commit, ExclusionReason.InseparableRevert,
                    $"3-way reverse-apply of the fix's source hunk conflicted (a later commit changed the same region): {applyErr.Trim()}");

            var revertedContent = File.ReadAllText(worktreeSourceAbs);

            // Belt-and-suspenders: never emit a partial 3-way result, and never emit a no-op "revert"
            // that did not actually reintroduce the defect (clause (b) would drop it downstream, but an
            // honest exclusion reason here keeps the accounting truthful).
            if (revertedContent.Contains("<<<<<<<", StringComparison.Ordinal)
                || revertedContent.Contains(">>>>>>>", StringComparison.Ordinal))
                return Excluded(commit, ExclusionReason.InseparableRevert,
                    "reverse-apply left conflict markers — the source hunk is not cleanly separable.");
            if (string.Equals(revertedContent, pinnedContent, StringComparison.Ordinal))
                return Excluded(commit, ExclusionReason.InseparableRevert,
                    "reverse-apply was a no-op — the defect was not reintroduced (the fixed region no longer matches).");

            var (line, col, fixedSnippet, defectSnippet) = SummarizePatch(patch);
            var candidate = new MutationCandidate
            {
                FileRelPath = sourcePath,
                Source = MutationSource.RevertBugfix,
                Operator = MutationOperatorKind.Revert,
                OperatorDescription = $"revert upstream fix {Short(commit.Sha)} (reintroduce defect)",
                Line = line,
                Column = col,
                OriginalSnippet = fixedSnippet,   // the FIXED code the pinned tree had
                MutatedSnippet = defectSnippet,    // the DEFECT the revert reintroduces
                MutatedSource = revertedContent,
                RevertedCommit = commit.Sha,
                RevertedCommitSubject = commit.Subject,
            };
            return new RevertCandidateResult { Commit = commit, Candidate = candidate };
        }
        finally
        {
            if (worktree != null)
            {
                Git(repoDir, $"worktree remove --force \"{worktree}\"");
                Git(repoDir, "worktree prune");
                if (Directory.Exists(worktree))
                    try { Directory.Delete(worktree, recursive: true); } catch { /* best effort */ }
            }
            if (File.Exists(patchFile))
                try { File.Delete(patchFile); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Extract, from a unified fix diff, the fixed-file start line and short before/after snippets.
    /// <c>+</c> lines are the FIXED code (what the pinned tree has); <c>-</c> lines are the DEFECT the
    /// revert reintroduces. Best-effort provenance — never throws.
    /// </summary>
    internal static (int line, int col, string fixedSnippet, string defectSnippet) SummarizePatch(string patch)
    {
        var line = 1;
        const int col = 1;
        string? fixedSnippet = null;
        string? defectSnippet = null;
        foreach (var raw in patch.Split('\n'))
        {
            var m = HunkHeader().Match(raw);
            if (m.Success)
            {
                if (int.TryParse(m.Groups[1].Value, out var start))
                    line = start;
                continue;
            }
            if (raw.StartsWith("+++", StringComparison.Ordinal) || raw.StartsWith("---", StringComparison.Ordinal))
                continue;
            if (defectSnippet == null && raw.StartsWith('-') && raw.Length > 1)
                defectSnippet = raw[1..].Trim();
            else if (fixedSnippet == null && raw.StartsWith('+') && raw.Length > 1)
                fixedSnippet = raw[1..].Trim();
            if (fixedSnippet != null && defectSnippet != null)
                break;
        }
        return (line, col, fixedSnippet ?? "", defectSnippet ?? "");
    }

    private static RevertCandidateResult Excluded(FixCommit commit, ExclusionReason reason, string detail) =>
        new() { Commit = commit, Exclusion = reason, ExclusionDetail = detail };

    private static string Short(string sha) => sha[..Math.Min(8, sha.Length)];

    /// <summary>
    /// Run a git command in <paramref name="workDir"/>, capturing stdout/stderr BYTE-EXACT (synchronous).
    ///
    /// <para>Stdout is read with <see cref="StreamReader.ReadToEndAsync()"/> rather than the line-oriented
    /// <c>OutputDataReceived</c> event: the latter strips <c>\r</c> and re-joins with <c>\n</c>, which
    /// silently converts a CRLF patch (git show for a Windows-line-ending source file — e.g. all of
    /// FluentValidation) to LF. That LF patch then fails to context-match the CRLF worktree files, so
    /// EVERY revert on a CRLF project would spuriously be reported "inseparable". Preserving the exact
    /// bytes keeps the diff and the tree in the same line-ending form.</para>
    /// </summary>
    private static (int Exit, string Stdout, string Stderr) Git(string workDir, string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        // Drain both streams concurrently (avoids the classic full-pipe deadlock) while preserving
        // the exact stdout bytes/line-endings.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    [GeneratedRegex(
        @"\b(fix(es|ed)?|bug(fix)?|defect|regression|incorrect(ly)?|wrong(ly)?|broken|crash(es|ed|ing)?|npe|nullref|deadlock|hang(s|ing)?|off-by-one|overflow|underflow|corrupt(s|ed|ion)?|faulty?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex FixWord();

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
