using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.Provenance;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1159, then #1417 — every ledger under <c>bench/phase0-agent-native/</c> stamps the commit its
/// numbers were measured against, and the website's benchmark data stamps the commit it was
/// generated from. A stamp written on a branch names a commit the squash merge discards; short
/// stamps can collide. <c>commit-stamp-index.json</c> keeps every stamp as written (it is the
/// experimental record) and records a durable identity beside it.
///
/// <para><b>#1417 closes three gaps the #1199 version of this test left.</b> It checked
/// reachability from <c>HEAD</c> — in pull-request CI that is <c>refs/pull/N/merge</c>, so a
/// branch-only commit passed and then vanished with the squash. It skipped in a shallow clone, so
/// the gate could go quiet everywhere. And it never verified the <c>identical-src-tree</c> basis it
/// asserted. Now every identity must resolve from fetched <c>refs/remotes/origin/main</c> or a
/// release tag, every claimed tree and manifest hash is re-read from git, and a shallow or
/// unfetched clone fails (<see cref="DurableProvenance"/>). The rules are exercised on throwaway
/// repositories — squash merge, merge ref, shallow clone — by <c>DurableProvenanceTests</c>.</para>
/// </summary>
public class LedgerCommitStampTests
{
    private const string IndexFile = "commit-stamp-index.json";
    private const string WriteBackVariable = "CALOR_PROVENANCE_WRITEBACK";
    private static readonly string[] PublicationStampNames = { "commit", "sourceCommit" };

    [Fact]
    public void EveryAuthoritativeIdentityResolvesFromProtectedMainOrReleaseTag()
    {
        var root = RepoRoot();
        var entries = AllEntries(root);
        var result = DurableProvenance.Verify(new GitRepo(root), entries);

        Assert.True(result.Findings.Count == 0,
            $"{result.Findings.Count} provenance findings (§6; fetch full history and origin/main if "
            + "this is a fresh or shallow clone):" + Environment.NewLine
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
