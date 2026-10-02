using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Calor.Compiler.Tests.Provenance;

/// <summary>
/// #1417 discriminating controls. Each builds a throwaway origin repository, performs the real git
/// operation the rule is about (squash merge, merge commit, pull-request merge ref, shallow clone,
/// fetch omission), and asserts the exact finding code — and that no other code fired, so a control
/// cannot pass because an unrelated rule tripped.
/// </summary>
public sealed class DurableProvenanceTests : IDisposable
{
    private const string Ledger = "bench/ledger.json";
    private const string Identical = DurableProvenance.BasisIdenticalSrcTree;
    private readonly GitWorld _world = new();

    public void Dispose() => _world.Dispose();

    [Fact]
    public void CommitOnProtectedMainIsAuthoritativeInAFreshClone()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"), (Ledger, Stamp("x")));
        _world.PushMain();
        var clone = _world.FreshClone();

        var result = Verify(clone, Entry(c, Identical, measured: c, git: clone));

        Assert.Empty(result.Findings);
        Assert.Single(result.Authoritative);
    }

    [Fact]
    public void BranchOnlyCommitIsRejectedEvenWhenItIsHead()
    {
        _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var branchOnly = _world.CommitOnBranch("feature", ("src/a.cs", "b"));
        _world.PushBranch("feature");
        var clone = _world.FreshClone();
        clone.Run("checkout", "--detach", branchOnly);   // HEAD now names it, as a PR checkout would

        AssertOnly(Verify(clone, Entry(branchOnly, Identical, branchOnly, clone)), DurableProvenance.Codes.NotDurable);
    }

    [Fact]
    public void PullRequestMergeRefIsNotProtectedMain()
    {
        var baseCommit = _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        _world.CommitOnBranch("feature", ("src/a.cs", "b"));
        var mergeRef = _world.MergeRefFor("feature", baseCommit, prNumber: 7);
        var clone = _world.FreshClone();
        clone.Run("fetch", "origin", "+refs/pull/7/merge:refs/remotes/pull/7/merge");
        clone.Run("checkout", "--detach", mergeRef);

        AssertOnly(Verify(clone, Entry(mergeRef, Identical, mergeRef, clone)), DurableProvenance.Codes.NotDurable);

        foreach (var via in new[] { "HEAD", "refs/pull/7/merge", "refs/remotes/pull/7/merge", "refs/remotes/origin/feature" })
            AssertOnly(Verify(clone, Entry(mergeRef, Identical, mergeRef, clone, resolvedVia: via)),
                DurableProvenance.Codes.ForbiddenRef);
    }

    [Fact]
    public void ShallowCloneFailsInsteadOfSkipping()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"));
        _world.CommitOnMain(("src/a.cs", "b"));
        _world.PushMain();
        var full = _world.FreshClone();
        var entry = Entry(c, Identical, c, full);
        Assert.Empty(Verify(full, entry).Findings);

        var result = Verify(_world.FreshClone(depth: 1), entry);

        AssertOnly(result, DurableProvenance.Codes.ShallowClone);
    }

    [Fact]
    public void CloneWithoutFetchedMainFails()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var clone = _world.FreshClone();
        var entry = Entry(c, Identical, c, clone);
        clone.Run("update-ref", "-d", DurableProvenance.ProtectedMainRef);

        AssertOnly(Verify(clone, entry), DurableProvenance.Codes.RefNotFetched);
    }

    [Fact]
    public void FalseIdenticalTreeClaimIsRejected()
    {
        _world.CommitOnMain(("src/a.cs", "a"), ("global.json", "{}"));
        _world.PushMain();
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "measured compiler"));
        _world.PushBranch("feature");
        var landed = _world.CommitOnMain(("src/a.cs", "a different compiler"));
        _world.PushMain();
        var clone = _world.FreshClone();

        var falseSrc = Entry(landed, Identical, measured, clone,
            measuredTrees: new() { ["src"] = clone.RevParse(measured + ":src")! });
        AssertOnly(Verify(clone, falseSrc), DurableProvenance.Codes.FalseIdenticalTree);

        // Round 1: a claim that omits src (here: only an identical global.json, recorded on both
        // sides) is not a src claim.
        var globalJson = clone.RevParse(measured + ":global.json")!;
        var noSrc = Entry(landed, Identical, measured, clone,
            trees: new() { ["/"] = clone.RevParse(landed + "^{tree}")!, ["src"] = clone.RevParse(landed + ":src")!, ["global.json"] = globalJson },
            measuredTrees: new() { ["global.json"] = globalJson });
        AssertOnly(Verify(clone, noSrc), DurableProvenance.Codes.FalseIdenticalTree);
    }

    [Fact]
    public void TrueIdenticalTreeClaimVerifiesAtBothCommits()
    {
        _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "same compiler"), (Ledger, Stamp("m")));
        _world.PushBranch("feature");
        var landed = _world.CommitOnMain(("src/a.cs", "same compiler"), ("docs/x.md", "unrelated"));
        _world.PushMain();
        var clone = _world.FreshClone();

        Assert.Empty(Verify(clone, Entry(landed, Identical, measured, clone)).Findings);

        // Unverifiable is not true: the claim fails when no measuredTreeHashes are recorded.
        var unrecorded = Entry(landed, Identical, measured, clone, measuredTrees: new());
        AssertOnly(Verify(clone, unrecorded), DurableProvenance.Codes.FalseIdenticalTree);
    }

    [Fact]
    public void ClaimedTreeAndManifestHashesAreReRead()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"), ("manifest.json", "{}"));
        _world.PushMain();
        var clone = _world.FreshClone();
        var root = clone.RevParse(c + "^{tree}")!;
        var src = clone.RevParse(c + ":src")!;

        var wrongTree = Entry(c, Identical, c, clone, trees: new() { ["/"] = root, ["src"] = src, ["manifest.json"] = new string('0', 40) });
        AssertOnly(Verify(clone, wrongTree), DurableProvenance.Codes.TreeMismatch);

        // Round 1: without the whole-repository tree "/", inputs outside src go unrecorded.
        var noRoot = Entry(c, Identical, c, clone, trees: new() { ["src"] = src });
        AssertOnly(Verify(clone, noRoot), DurableProvenance.Codes.TreeMismatch);

        var wrongContent = Entry(c, Identical, c, clone, contents: new() { ["manifest.json"] = new string('a', 64) });
        AssertOnly(Verify(clone, wrongContent), DurableProvenance.Codes.ContentMismatch);
    }

    [Fact]
    public void ShortShasAndTagsAreRejected()
    {
        _world.CommitOnMain(("src/a.cs", "0"));
        var tagged = _world.CommitOnBranch("release", ("src/a.cs", "a"));
        _world.Dev.Run("tag", "v1.2.3", tagged);
        _world.PushMain();
        _world.Dev.Run("push", "origin", "refs/tags/v1.2.3");
        var clone = _world.FreshClone();

        var shortSha = Entry(tagged[..12], Identical, tagged, clone, treesAt: tagged);
        AssertOnly(Verify(clone, shortSha), DurableProvenance.Codes.NotFullSha);

        // Round 1: a lightweight v* tag is mutable, so it does not make a branch-only commit durable.
        var viaTag = Entry(tagged, Identical, tagged, clone, resolvedVia: "refs/tags/v1.2.3");
        AssertOnly(Verify(clone, viaTag), DurableProvenance.Codes.ForbiddenRef);
    }

    [Fact]
    public void ShortStampExpandsOnlyWhenUniqueAndMatching()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"));
        var other = _world.CommitOnMain(("src/a.cs", "b"));
        _world.PushMain();
        var clone = _world.FreshClone();
        const string prefix = DurableProvenance.BasisUniquePrefixExpansion;

        Assert.Empty(Verify(clone, Entry(c, prefix, c[..12], clone)).Findings);
        AssertOnly(Verify(clone, Entry(other, prefix, c[..12], clone)), DurableProvenance.Codes.BadPrefixExpansion);
        AssertOnly(Verify(clone, Entry(c, prefix, c[..4], clone)), DurableProvenance.Codes.BadPrefixExpansion);
    }

    /// <summary>
    /// The #1159 shape end to end: a ledger measured and stamped on a branch, squash-merged, branch
    /// deleted. The measured commit is gone from a fresh clone; the pending identity is never
    /// authoritative; the write-back finds the squash commit, re-verifies the inputs, and the
    /// completed identity resolves from the fresh clone.
    /// </summary>
    [Fact]
    public void SquashMergeWriteBackResolvesFromAFreshClone()
    {
        _world.CommitOnMain(("src/a.cs", "a"), ("manifest.json", "{}"));
        _world.PushMain();
        var (measured, pending) = MeasureOnBranch();
        _world.PushBranch("feature");

        var squash = _world.SquashMergeAndDeleteBranch("feature");
        var later = _world.CommitOnMain(("docs/later.md", "after the landing; same src and manifest"));
        _world.PushMain();
        var clone = _world.FreshClone();
        Assert.False(clone.CommitExists(measured), "the squash should have discarded the branch commit");

        var before = Verify(clone, pending);
        Assert.Empty(before.Findings);
        Assert.Empty(before.Authoritative);
        Assert.Single(before.Pending);

        var (completed, failure) = DurableProvenance.CompleteWriteBack(clone, pending);
        Assert.Null(failure);
        Assert.Equal(squash, completed!["commit"]!.GetValue<string>());
        var after = Verify(clone, WithIdentity(pending, completed));
        Assert.Empty(after.Findings);
        Assert.Single(after.Authoritative);

        // Round 1: a completion naming a later main commit with the same inputs did not land the artifact.
        var moved = completed.DeepClone().AsObject();
        moved["commit"] = later;
        moved["treeHashes"]!["/"] = clone.RevParse(later + "^{tree}");
        AssertOnly(Verify(clone, WithIdentity(pending, moved)), DurableProvenance.Codes.WriteBackInvalid);
    }

    [Fact]
    public void MergeCommitWriteBackNamesTheMergeCommit()
    {
        _world.CommitOnMain(("src/a.cs", "a"), ("manifest.json", "{}"));
        _world.PushMain();
        var (_, pending) = MeasureOnBranch();
        var merge = _world.MergeCommitInto("feature");
        var clone = _world.FreshClone();

        var (completed, failure) = DurableProvenance.CompleteWriteBack(clone, pending);
        Assert.Null(failure);
        Assert.Equal(merge, completed!["commit"]!.GetValue<string>());
        var complete = WithIdentity(pending, completed);
        Assert.Empty(Verify(clone, complete).Findings);

        // Round 2: the artifact changing after the write-back (same stamp, new numbers) needs a new identity.
        _world.CommitOnMain((Ledger, Stamp(pending.GetProperty("measuredCommit").GetString()!) + "{}\n"));
        _world.PushMain();
        AssertOnly(Verify(_world.FreshClone(), complete), DurableProvenance.Codes.WriteBackInvalid);
    }

    [Theory]
    [InlineData("src/b.cs")]        // a measured tree changed in the merge
    [InlineData("manifest.json")]   // round 1: a measured manifest changed in the merge
    public void WriteBackFailsClosedWhenTheMergeChangedAMeasuredInput(string changedOnMain)
    {
        _world.CommitOnMain(("src/a.cs", "a"), ("manifest.json", "{}"));
        _world.PushMain();
        var (_, pending) = MeasureOnBranch();
        _world.PushBranch("feature");
        _world.CommitOnMain((changedOnMain, "someone else's change"));
        _world.PushMain();
        var squash = _world.SquashMergeAndDeleteBranch("feature");
        var clone = _world.FreshClone();

        var (completed, failure) = DurableProvenance.CompleteWriteBack(clone, pending);
        Assert.Null(completed);
        Assert.Equal(DurableProvenance.Codes.WriteBackInvalid, failure!.Code);

        // Hand-written completions recording the landed values: one keeps the reviewed phase 1
        // (caught by the comparison), one also rewrites phase 1 to match (round 2: caught because
        // phase 1 must equal the pending record committed in the index at the landing commit).
        var landedTrees = new JsonObject { ["/"] = clone.RevParse(squash + "^{tree}"), ["src"] = clone.RevParse(squash + ":src") };
        var landedContents = Contents(clone, squash, "manifest.json");
        var writeBack = new JsonObject();
        foreach (var p in pending.GetProperty("durableIdentity").GetProperty("phase1").EnumerateObject())
            writeBack[p.Name == "pr" ? "pr" : "phase1" + char.ToUpperInvariant(p.Name[0]) + p.Name[1..]] = JsonNode.Parse(p.Value.GetRawText());
        JsonElement Forged(JsonObject wb) => WithIdentity(pending, new JsonObject
        {
            ["status"] = "complete", ["commit"] = squash, ["resolvedVia"] = DurableProvenance.ProtectedMainRef,
            ["treeHashes"] = landedTrees.DeepClone(), ["inputContentHashes"] = landedContents.DeepClone(), ["writeBack"] = wb,
        });
        AssertOnly(Verify(clone, Forged(writeBack)), DurableProvenance.Codes.WriteBackInvalid);
        var rewritten = writeBack.DeepClone().AsObject();
        rewritten["phase1TreeHashes"] = new JsonObject { ["src"] = landedTrees["src"]!.DeepClone() };
        rewritten["phase1InputContentHashes"] = landedContents.DeepClone();
        AssertOnly(Verify(clone, Forged(rewritten)), DurableProvenance.Codes.WriteBackInvalid);
    }

    [Fact]
    public void PendingIdentityIsNeverAuthoritativeAndMustDescribeTheCommittedArtifact()
    {
        _world.CommitOnMain(("src/a.cs", "a"), ("manifest.json", "{}"));
        var measured = _world.CommitOnMain(("src/a.cs", "b"));
        var ledgerText = Stamp(measured);
        _world.CommitOnMain((Ledger, ledgerText));
        _world.PushMain();
        var clone = _world.FreshClone();

        var ok = Verify(clone, PendingEntry(clone, measured, ledgerText));
        Assert.Empty(ok.Findings);
        Assert.Empty(ok.Authoritative);

        AssertOnly(Verify(clone, PendingEntry(clone, measured, ledgerText + " ")), DurableProvenance.Codes.WriteBackInvalid);
        // Round 2: phase 1 must claim the measured compiler, and a new measurement drops the #1199 field.
        AssertOnly(Verify(clone, Edit(PendingEntry(clone, measured, ledgerText),
            n => n["durableIdentity"]!["phase1"]!["treeHashes"] = new JsonObject { ["manifest.json"] = clone.RevParse(measured + ":manifest.json") })),
            DurableProvenance.Codes.WriteBackInvalid);
        AssertOnly(Verify(clone, Edit(PendingEntry(clone, measured, ledgerText), n => n["resolvableOnMain"] = measured)),
            DurableProvenance.Codes.WriteBackInvalid);
    }

    [Fact]
    public void LandingCommitOnlyMustHaveLandedTheArtifact()
    {
        var landed = _world.CommitOnMain(("src/a.cs", "a"), (Ledger, Stamp("gone")));
        var later = _world.CommitOnMain(("src/a.cs", "b"));
        _world.PushMain();
        var clone = _world.FreshClone();
        const string basis = DurableProvenance.BasisLandingCommitOnly;

        Assert.Empty(Verify(clone, Entry(landed, basis, "gone", clone)).Findings);
        AssertOnly(Verify(clone, Entry(later, basis, "gone", clone)), DurableProvenance.Codes.NotLandingCommit);
    }

    /// <summary>
    /// Round 1: measured-on-top-of names the base; the entry must also name the commit that landed
    /// the measured artifact, descending from that base, so the repaired source is pinned too.
    /// </summary>
    [Fact]
    public void MeasuredOnTopOfMustNameTheLandingOfTheRepair()
    {
        var baseCommit = _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var ledgerText = Stamp(baseCommit);
        _world.CommitOnBranch("feature", ("src/a.cs", "repair"), (Ledger, ledgerText));
        var landing = _world.SquashMergeAndDeleteBranch("feature");
        var unrelated = _world.CommitOnMain(("docs/x.md", "x"));
        _world.PushMain();
        var clone = _world.FreshClone();
        const string basis = DurableProvenance.BasisMeasuredOnTopOf;

        Assert.Empty(Verify(clone, WithLanding(Entry(baseCommit, basis, baseCommit, clone), clone, landing, ledgerText)).Findings);
        AssertOnly(Verify(clone, Entry(baseCommit, basis, baseCommit, clone)), DurableProvenance.Codes.OnTopOfMismatch);
        AssertOnly(Verify(clone, WithLanding(Entry(baseCommit, basis, baseCommit, clone), clone, unrelated, ledgerText)),
            DurableProvenance.Codes.OnTopOfMismatch);
        AssertOnly(Verify(clone, WithLanding(Entry(baseCommit, basis, landing, clone), clone, landing, ledgerText)),
            DurableProvenance.Codes.OnTopOfMismatch);
    }

    // ---- helpers ----

    private static string Stamp(string commit) => $"{{\"measuredCommit\": \"{commit}\"}}\n";

    /// <summary>
    /// Measures on branch "feature": a compiler change, then the stamped ledger committed together
    /// with its pending phase-1 entry in the index, as the measuring PR would.
    /// </summary>
    private (string Measured, JsonElement Pending) MeasureOnBranch()
    {
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "new compiler"));
        var ledgerText = Stamp(measured);
        var pending = PendingEntry(_world.Dev, measured, ledgerText);
        var index = new JsonObject { ["ledgers"] = new JsonArray(), ["publicationStamps"] = new JsonArray(JsonNode.Parse(pending.GetRawText())) };
        _world.CommitOnBranch("feature", (Ledger, ledgerText), (DurableProvenance.IndexPath, index.ToJsonString()));
        return (measured, pending);
    }

    private static DurableProvenance.Result Verify(GitRepo git, JsonElement entry)
        => DurableProvenance.Verify(git, new[] { entry });

    private static void AssertOnly(DurableProvenance.Result result, string code)
    {
        Assert.True(result.Findings.Count > 0, $"expected {code}, but nothing fired");
        Assert.True(result.Findings.All(f => f.Code == code),
            $"expected only {code}; got:" + Environment.NewLine + string.Join(Environment.NewLine, result.Findings));
        Assert.Empty(result.Authoritative);
    }

    private static JsonObject Contents(GitRepo git, string commit, string path)
        => new() { [path] = DurableProvenance.Sha256(git.ReadBlob($"{commit}:{path}")!) };

    private static JsonObject ToObject(Dictionary<string, string> map)
    {
        var o = new JsonObject();
        foreach (var (k, v) in map) o[k] = v;
        return o;
    }

    /// <summary>
    /// A complete entry whose hashes are read from git unless overridden. For identical-src-tree the
    /// src claim is filled from the measured commit unless <paramref name="measuredTrees"/> is given.
    /// </summary>
    private static JsonElement Entry(
        string commit, string basis, string measured, GitRepo git,
        string resolvedVia = DurableProvenance.ProtectedMainRef,
        Dictionary<string, string>? trees = null,
        Dictionary<string, string>? contents = null,
        Dictionary<string, string>? measuredTrees = null,
        string? treesAt = null)
    {
        var at = treesAt ?? commit;
        trees ??= new() { ["/"] = git.RevParse(at + "^{tree}")!, ["src"] = git.RevParse(at + ":src")! };
        if (contents == null)
        {
            var path = git.RevParse(at + ":manifest.json") != null ? "manifest.json" : "src/a.cs";
            contents = new() { [path] = DurableProvenance.Sha256(git.ReadBlob($"{at}:{path}")!) };
        }
        var entry = new JsonObject
        {
            ["path"] = Ledger,
            ["measuredCommit"] = measured,
            ["basis"] = basis,
            ["durableIdentity"] = new JsonObject
            {
                ["status"] = "complete",
                ["commit"] = commit,
                ["resolvedVia"] = resolvedVia,
                ["treeHashes"] = ToObject(trees),
                ["inputContentHashes"] = ToObject(contents),
            },
        };
        if (basis == Identical)
            entry["measuredTreeHashes"] = ToObject(measuredTrees ?? new() { ["src"] = git.RevParse(measured + ":src")! });
        return JsonDocument.Parse(entry.ToJsonString()).RootElement.Clone();
    }

    /// <summary>A phase-1 entry for <paramref name="measured"/>: src tree and manifest read from git.</summary>
    private static JsonElement PendingEntry(GitRepo git, string measured, string ledgerText)
    {
        var artifact = DurableProvenance.Sha256(Encoding.UTF8.GetBytes(ledgerText));
        var entry = new JsonObject
        {
            ["path"] = Ledger,
            ["measuredCommit"] = measured,
            ["basis"] = DurableProvenance.BasisPostMergeWriteBack,
            ["durableIdentity"] = new JsonObject
            {
                ["status"] = "pending",
                ["phase1"] = new JsonObject
                {
                    ["headCommit"] = measured,
                    ["pr"] = 1,
                    ["treeHashes"] = new JsonObject { ["src"] = git.RevParse(measured + ":src") },
                    ["inputContentHashes"] = Contents(git, measured, "manifest.json"),
                    ["artifactSha256"] = artifact,
                },
            },
        };
        return JsonDocument.Parse(entry.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement WithLanding(JsonElement entry, GitRepo git, string landing, string ledgerText)
    {
        var node = JsonNode.Parse(entry.GetRawText())!.AsObject();
        node["landing"] = new JsonObject
        {
            ["commit"] = landing,
            ["artifactSha256"] = DurableProvenance.Sha256(Encoding.UTF8.GetBytes(ledgerText)),
            ["treeHashes"] = new JsonObject { ["src"] = git.RevParse(landing + ":src") },
        };
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement Edit(JsonElement entry, Action<JsonObject> edit)
    {
        var node = JsonNode.Parse(entry.GetRawText())!.AsObject();
        edit(node);
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement WithIdentity(JsonElement entry, JsonObject identity)
    {
        var node = JsonNode.Parse(entry.GetRawText())!.AsObject();
        node["durableIdentity"] = identity.DeepClone();
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    /// <summary>
    /// A bare "origin" plus a developer clone, isolated from the user's git configuration (no
    /// signing, hooks, or templates), deleted on dispose.
    /// </summary>
    private sealed class GitWorld : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-1417-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> _env;
        private readonly GitRepo _top;
        private int _clones;

        public GitWorld()
        {
            Directory.CreateDirectory(_root);
            var emptyConfig = Path.Combine(_root, "empty.gitconfig");
            File.WriteAllText(emptyConfig, "");
            _env = new()
            {
                ["GIT_CONFIG_GLOBAL"] = emptyConfig, ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_TERMINAL_PROMPT"] = "0",
                ["GIT_AUTHOR_NAME"] = "t", ["GIT_AUTHOR_EMAIL"] = "t@example.invalid",
                ["GIT_COMMITTER_NAME"] = "t", ["GIT_COMMITTER_EMAIL"] = "t@example.invalid",
            };
            _top = new GitRepo(_root, _env);
            Origin = Path.Combine(_root, "origin.git");
            Must(_top, "init", "--quiet", "--bare", "--initial-branch=main", Origin);
            Must(_top, "clone", "--quiet", Origin, Path.Combine(_root, "dev"));
            Dev = new GitRepo(Path.Combine(_root, "dev"), _env);
            Must(Dev, "symbolic-ref", "HEAD", "refs/heads/main");
        }

        public string Origin { get; }
        public GitRepo Dev { get; }

        public string CommitOnMain(params (string Path, string Text)[] files)
        {
            Checkout("main");
            return Commit(files);
        }

        public string CommitOnBranch(string branch, params (string Path, string Text)[] files)
        {
            if (Dev.RevParse("refs/heads/" + branch) == null) Must(Dev, "checkout", "--quiet", "-b", branch);
            else Checkout(branch);
            return Commit(files);
        }

        public void Checkout(string branch)
        {
            var (exit, current, _) = Dev.Run("symbolic-ref", "--quiet", "--short", "HEAD");
            if (exit != 0 || current.Trim() != branch) Must(Dev, "checkout", "--quiet", branch);
        }

        public void PushMain() => Must(Dev, "push", "--quiet", "origin", "main");
        public void PushBranch(string branch) => Must(Dev, "push", "--quiet", "origin", branch);

        /// <summary>Publishes a GitHub-style refs/pull/N/merge without touching main.</summary>
        public string MergeRefFor(string branch, string baseCommit, int prNumber)
        {
            Must(Dev, "checkout", "--quiet", "--detach", baseCommit);
            var merge = MergeNoFf(branch);
            Must(Dev, "push", "--quiet", "origin", $"{merge}:refs/pull/{prNumber}/merge");
            Checkout("main");
            return merge;
        }

        public string SquashMergeAndDeleteBranch(string branch)
        {
            Checkout("main");
            Must(Dev, "merge", "--quiet", "--squash", branch);
            Must(Dev, "commit", "--quiet", "-m", "squash " + branch);
            PushMain();
            Dev.Run("push", "--quiet", "origin", "--delete", branch);
            Must(Dev, "branch", "--quiet", "-D", branch);
            return Dev.RevParse("HEAD")!;
        }

        public string MergeCommitInto(string branch)
        {
            Checkout("main");
            var merge = MergeNoFf(branch);
            PushMain();
            return merge;
        }

        public GitRepo FreshClone(int? depth = null)
        {
            var path = Path.Combine(_root, "clone" + _clones++);
            var source = depth is int d ? new[] { "--depth", d.ToString(), new Uri(Origin).AbsoluteUri } : new[] { Origin };
            Must(_top, new[] { "clone", "--quiet", "--no-local" }.Concat(source).Append(path).ToArray());
            return new GitRepo(path, _env);
        }

        private string MergeNoFf(string branch)
        {
            Must(Dev, "merge", "--quiet", "--no-ff", "--no-edit", branch);
            return Dev.RevParse("HEAD")!;
        }

        private string Commit((string Path, string Text)[] files)
        {
            foreach (var (path, text) in files)
            {
                var full = Path.Combine(Dev.Root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
                Must(Dev, "add", "--", path);
            }
            Must(Dev, "commit", "--quiet", "--allow-empty", "-m", "c" + Guid.NewGuid().ToString("N")[..8]);
            return Dev.RevParse("HEAD")!;
        }

        private static void Must(GitRepo git, params string[] args)
        {
            var (exit, output, error) = git.Run(args);
            Assert.True(exit == 0, $"git {string.Join(' ', args)} failed ({exit}): {output}{error}");
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
