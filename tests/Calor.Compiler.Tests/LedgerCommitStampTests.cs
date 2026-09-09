using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1159 — every ledger under <c>bench/phase0-agent-native/</c> stamps the commit its numbers were
/// measured against, and every roadmap since 0.15 cites those stamps as the provenance of its
/// published figures. None of them resolved.
///
/// <para>The mechanism is the repository's own merge policy: it squash-merges, so a stamp written
/// on a branch names a commit the squash discards. Three of five named commits are contained by
/// <b>zero</b> remote branches — they survive only as unreferenced objects in individual clones and
/// would not survive a <c>git gc</c>. The numbers were never in doubt; what was gone is a third
/// party's ability to check them, which is the entire purpose of the stamp.</para>
///
/// <para><b>The stamps are not repointed, and that is deliberate.</b> A <c>measuredCommit</c> is the
/// experimental record, not a pointer: overwriting it would falsify what was measured. One of them
/// is additionally <c>ARM_B_COMMIT</c>, a frozen constant of a completed epoch that
/// <c>ppw-compile.py</c>, <c>ppw-analyze.py</c> and <c>PpWRowsRegistrationTests</c> all pin. Two of
/// the ledgers are byte-compared against their own generators, so even adding a field by hand would
/// leave them permanently "stale". The resolvable equivalent therefore lives beside them, in
/// <c>commit-stamp-index.json</c>.</para>
///
/// <para>This is the test roadmap-v0.18 §3.2 S3 asked for — <i>"a test that fails when a stamp does
/// not resolve on main — cheap, and it would have caught this the first time"</i>. Three properties,
/// because the first alone would pass vacuously on an empty index: every indexed commit resolves and
/// is reachable; every entry's <c>measuredCommit</c> still matches the ledger it describes, so the
/// index cannot drift away from the files it speaks for; and every ledger carrying a
/// <c>measuredCommit</c> is covered, so #1159 cannot recur by simply not adding an entry.</para>
/// </summary>
public class LedgerCommitStampTests
{
    private const string IndexFile = "commit-stamp-index.json";

    // SkippableFact, not Fact: Skip.If throws, and xUnit reports that as a FAILURE on a
    // plain [Fact]. Caught by CI rather than locally, because a developer clone is not
    // shallow and the guard never fired here.
    [SkippableFact]
    public void EveryIndexedCommitResolvesAndIsReachableFromThisBranch()
    {
        var root = RepoRoot();
        Skip.If(IsShallowClone(root),
            "shallow clone: history is not present, so reachability cannot be decided here");

        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var entry in IndexEntries(root))
        {
            var ledger = entry.GetProperty("ledger").GetString()!;
            var sha = entry.GetProperty("resolvableOnMain").GetString()!;
            checkedCount++;

            if (sha.Length != 40 || !sha.All(Uri.IsHexDigit))
            {
                failures.Add($"{ledger}: resolvableOnMain '{sha}' is not a full commit sha");
                continue;
            }
            if (!Git(root, "cat-file", "-e", $"{sha}^{{commit}}"))
            {
                failures.Add(
                    $"{ledger}: {sha} does not resolve in this repository. A stamp written on a "
                    + "branch names a commit the squash-merge discards (#1159).");
                continue;
            }
            if (!Git(root, "merge-base", "--is-ancestor", sha, "HEAD"))
            {
                failures.Add(
                    $"{ledger}: {sha} resolves but is not reachable from HEAD, so a fresh clone "
                    + "cannot check it (#1159).");
            }
        }

        Assert.True(checkedCount >= 5, $"expected at least five indexed ledgers; found {checkedCount}");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {checkedCount} indexed commits do not resolve on this branch:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The index describes real files, and keeps describing them. Without this it could drift into
    /// a set of true statements about ledgers that have since been re-measured.
    /// </summary>
    [Fact]
    public void EveryIndexEntryStillMatchesItsLedgersOwnStamp()
    {
        var root = RepoRoot();
        var bench = Path.Combine(root, "bench", "phase0-agent-native");
        var mismatches = new List<string>();

        foreach (var entry in IndexEntries(root))
        {
            var ledger = entry.GetProperty("ledger").GetString()!;
            var expected = entry.GetProperty("measuredCommit").GetString()!;
            var path = Path.Combine(bench, ledger);

            if (!File.Exists(path))
            {
                mismatches.Add($"{ledger}: indexed but the file does not exist");
                continue;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var actual = MeasuredCommit(doc.RootElement);
            if (actual == null)
                mismatches.Add($"{ledger}: indexed but records no measuredCommit");
            else if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"{ledger}: index says {expected}, ledger says {actual}");
        }

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// Coverage, which is what stops the first test passing on an index that quietly shrank. A new
    /// ledger that stamps a measurement must be indexed in the same change.
    /// </summary>
    [Fact]
    public void EveryLedgerThatStampsAMeasurementIsIndexed()
    {
        var root = RepoRoot();
        var bench = Path.Combine(root, "bench", "phase0-agent-native");
        var indexed = IndexEntries(root)
            .Select(e => e.GetProperty("ledger").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unindexed = new List<string>();
        foreach (var file in Directory.EnumerateFiles(bench, "*.json")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, IndexFile, StringComparison.OrdinalIgnoreCase)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(file)); }
            catch (JsonException) { continue; }
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                if (MeasuredCommit(doc.RootElement) != null && !indexed.Contains(name))
                    unindexed.Add(name);
            }
        }

        Assert.True(unindexed.Count == 0,
            "these ledgers stamp a measuredCommit but are not in " + IndexFile
            + ", so nothing checks that their provenance resolves (#1159): "
            + string.Join(", ", unindexed));
    }

    private static string? MeasuredCommit(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "measuredCommit", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }
        return null;
    }

    private static List<JsonElement> IndexEntries(string root)
    {
        var path = Path.Combine(root, "bench", "phase0-agent-native", IndexFile);
        Assert.True(File.Exists(path), $"{IndexFile} is missing — #1159's provenance record");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("ledgers").EnumerateArray()
            .Select(e => e.Clone()).ToList();
    }

    private static bool IsShallowClone(string root)
        => Run(root, "rev-parse", "--is-shallow-repository").Output
            .Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool Git(string root, params string[] args) => Run(root, args).ExitCode == 0;

    private static (int ExitCode, string Output) Run(string root, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null
            && !Directory.Exists(Path.Combine(dir, ".git"))
            && !File.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
