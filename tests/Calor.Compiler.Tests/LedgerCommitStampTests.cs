using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.Provenance;
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
///
/// <para><b>#1417</b>: the #1199 version checked reachability from <c>HEAD</c> (in PR CI, the
/// merge ref), skipped in a shallow clone, and never verified the bases it asserted. Identities now
/// resolve only from fetched <c>refs/remotes/origin/main</c>, claimed trees and manifests are
/// re-read, shallow or unfetched clones fail (<see cref="DurableProvenance"/>), and the website's
/// benchmark stamps are covered. <c>DurableProvenanceTests</c> exercises the rules on real git.</para>
/// </summary>
public class LedgerCommitStampTests
{
    private const string IndexFile = "commit-stamp-index.json";
    private const string WriteBackVariable = "CALOR_PROVENANCE_WRITEBACK";
    private static readonly string[] PublicationStampNames = { "commit", "sourceCommit" };

    [Fact]
    public void EveryAuthoritativeIdentityResolvesFromProtectedMain()
    {
        var root = RepoRoot();
        var entries = AllEntries(root);
        var result = DurableProvenance.Verify(new GitRepo(root), entries);

        Assert.True(result.Findings.Count == 0,
            $"{result.Findings.Count} provenance findings (§6; fetch full history and origin/main if " + "this is a fresh or shallow clone):" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Findings));
        Assert.True(entries.Count >= 11, $"expected at least 11 indexed stamps; found {entries.Count}");
        Assert.Equal(entries.Count, result.Authoritative.Count + result.Pending.Count);
    }

    /// <summary>
    /// The index describes real files, and keeps describing them. Without this it could drift into
    /// a set of true statements about artifacts that have since been re-measured.
    /// </summary>
    [Fact]
    public void EveryIndexEntryStillMatchesItsArtifactsOwnStamp()
    {
        var root = RepoRoot();
        var mismatches = new List<string>();

        foreach (var entry in AllEntries(root))
        {
            var subject = DurableProvenance.Subject(entry);
            var expected = entry.GetProperty("measuredCommit").GetString()!;
            var path = Path.Combine(root, DurableProvenance.ArtifactPath(entry));
            if (!File.Exists(path))
            {
                mismatches.Add($"{subject}: indexed but the file does not exist");
                continue;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var pointer = DurableProvenance.StampPointer(entry);
            var actual = pointer == null ? MeasuredCommit(doc.RootElement) : AtPointer(doc.RootElement, pointer);
            if (actual == null)
                mismatches.Add($"{subject}: indexed but records no stamp there");
            else if (!string.Equals(actual, expected, StringComparison.Ordinal))
                mismatches.Add($"{subject}: index says {expected}, artifact says {actual}");
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
        var indexed = IndexEntries(root, "ledgers")
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

    /// <summary>
    /// #1417: the website's benchmark data stamps short SHAs (<c>c2a8816d</c>). Every
    /// <c>commit</c>/<c>sourceCommit</c> stamp under <c>website/public/data/</c> must be indexed with a
    /// durable identity, so a stamp added later cannot bypass the check.
    /// </summary>
    [Fact]
    public void EveryPublishedBenchmarkStampIsIndexed()
    {
        var root = RepoRoot();
        var data = Path.Combine(root, "website", "public", "data");
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(data, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            CollectStamps(doc.RootElement, "", relative, found);
        }

        var indexed = IndexEntries(root, "publicationStamps")
            .Select(DurableProvenance.Subject)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(found.Count >= 6, $"expected at least six published stamps; found {found.Count}");
        var missing = found.Where(s => !indexed.Contains(s)).ToList();
        Assert.True(missing.Count == 0,
            "published stamps without a durable identity in " + IndexFile + " (#1417): "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// §6(b) phase 2. A pending identity either has not landed on protected main yet, or it landed
    /// with every phase-1 tree unchanged. A landing that changed a measured tree fails here rather
    /// than waiting for someone to notice. With <c>CALOR_PROVENANCE_WRITEBACK=1</c> the completed
    /// identities are written into the index for the write-back PR.
    /// </summary>
    [Fact]
    public void PendingIdentitiesAwaitLandingOrCompleteWithEqualTrees()
    {
        var root = RepoRoot();
        var git = new GitRepo(root);
        var failures = new List<string>();
        var completions = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (var entry in AllEntries(root))
        {
            if (!entry.TryGetProperty("durableIdentity", out var identity)
                || !identity.TryGetProperty("status", out var status)
                || status.GetString() != "pending")
                continue;

            var (completed, failure) = DurableProvenance.CompleteWriteBack(git, entry);
            if (failure != null) failures.Add(failure.ToString());
            else if (completed != null) completions[DurableProvenance.Subject(entry)] = completed;
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        if (completions.Count > 0
            && Environment.GetEnvironmentVariable(WriteBackVariable) == "1")
        {
            WriteBack(root, completions);
        }
    }

    private static void WriteBack(string root, Dictionary<string, JsonObject> completions)
    {
        var path = IndexPath(root);
        var index = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var array in new[] { "ledgers", "publicationStamps" })
        {
            if (index[array] is not JsonArray entries) continue;
            foreach (var node in entries)
            {
                var element = JsonDocument.Parse(node!.ToJsonString()).RootElement;
                if (completions.TryGetValue(DurableProvenance.Subject(element), out var completed))
                    node.AsObject()["durableIdentity"] = completed.DeepClone();
            }
        }
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(path, index.ToJsonString(options) + "\n");
    }

    private static void CollectStamps(JsonElement element, string pointer, string file, ISet<string> found)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var child = pointer + "/" + property.Name;
                if (property.Value.ValueKind == JsonValueKind.String
                    && PublicationStampNames.Contains(property.Name, StringComparer.Ordinal))
                    found.Add(file + "#" + child);
                else
                    CollectStamps(property.Value, child, file, found);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
                CollectStamps(item, pointer + "/" + i++, file, found);
        }
    }

    private static string? AtPointer(JsonElement root, string pointer)
    {
        var current = root;
        foreach (var part in pointer.Trim('/').Split('/'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
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

    private static string IndexPath(string root)
        => Path.Combine(root, "bench", "phase0-agent-native", IndexFile);

    private static List<JsonElement> AllEntries(string root)
        => IndexEntries(root, "ledgers").Concat(IndexEntries(root, "publicationStamps")).ToList();

    private static List<JsonElement> IndexEntries(string root, string array)
    {
        var path = IndexPath(root);
        Assert.True(File.Exists(path), $"{IndexFile} is missing — #1159's provenance record");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty(array, out var entries)
            && entries.ValueKind == JsonValueKind.Array, $"{IndexFile} has no '{array}' array");
        return entries.EnumerateArray().Select(e => e.Clone()).ToList();
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
