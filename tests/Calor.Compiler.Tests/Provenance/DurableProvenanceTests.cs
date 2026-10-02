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
    private readonly GitWorld _world = new();

    public void Dispose() => _world.Dispose();

    [Fact]
    public void CommitOnProtectedMainIsAuthoritativeInAFreshClone()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"), (Ledger, Stamp("x")));
        _world.PushMain();
        var clone = _world.FreshClone();

        var result = Verify(clone, Entry(c, DurableProvenance.BasisMeasuredOnTopOf, measured: c, git: clone));

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

        var result = Verify(clone, Entry(branchOnly, DurableProvenance.BasisMeasuredOnTopOf, branchOnly, clone));

        AssertOnly(result, DurableProvenance.Codes.NotDurable);
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

        var asMain = Verify(clone, Entry(mergeRef, DurableProvenance.BasisMeasuredOnTopOf, mergeRef, clone));
        AssertOnly(asMain, DurableProvenance.Codes.NotDurable);

        foreach (var via in new[] { "HEAD", "refs/pull/7/merge", "refs/remotes/pull/7/merge", "refs/remotes/origin/feature" })
        {
            var entry = Entry(mergeRef, DurableProvenance.BasisMeasuredOnTopOf, mergeRef, clone, resolvedVia: via);
            AssertOnly(Verify(clone, entry), DurableProvenance.Codes.ForbiddenRef);
        }
    }

    [Fact]
    public void ShallowCloneFailsInsteadOfSkipping()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"));
        _world.CommitOnMain(("src/a.cs", "b"));
        _world.PushMain();
        var full = _world.FreshClone();
        var entry = Entry(c, DurableProvenance.BasisMeasuredOnTopOf, c, full);
        Assert.Empty(Verify(full, entry).Findings);

        var shallow = _world.FreshClone(depth: 1);
        var result = Verify(shallow, entry);

        AssertOnly(result, DurableProvenance.Codes.ShallowClone);
        Assert.Empty(result.Authoritative);
    }

    [Fact]
    public void CloneWithoutFetchedMainFails()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var clone = _world.FreshClone();
        var entry = Entry(c, DurableProvenance.BasisMeasuredOnTopOf, c, clone);
        clone.Run("update-ref", "-d", DurableProvenance.ProtectedMainRef);

        AssertOnly(Verify(clone, entry), DurableProvenance.Codes.RefNotFetched);
    }

    [Fact]
    public void FalseIdenticalTreeClaimIsRejected()
    {
        _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "measured compiler"));
        _world.PushBranch("feature");
        _world.Checkout("main");
        var landed = _world.CommitOnMain(("src/a.cs", "a different compiler"));
        _world.PushMain();
        var clone = _world.FreshClone();

        var entry = Entry(landed, DurableProvenance.BasisIdenticalSrcTree, measured, clone,
            measuredTrees: new() { ["src"] = clone.RevParse(measured + ":src")! });

        AssertOnly(Verify(clone, entry), DurableProvenance.Codes.FalseIdenticalTree);
    }

    [Fact]
    public void TrueIdenticalTreeClaimVerifiesAtBothCommits()
    {
        _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "same compiler"), (Ledger, Stamp("m")));
        _world.PushBranch("feature");
        _world.Checkout("main");
        var landed = _world.CommitOnMain(("src/a.cs", "same compiler"), ("docs/x.md", "unrelated"));
        _world.PushMain();
        var clone = _world.FreshClone();
        var tree = clone.RevParse(measured + ":src")!;

        var ok = Entry(landed, DurableProvenance.BasisIdenticalSrcTree, measured, clone,
            measuredTrees: new() { ["src"] = tree });
        Assert.Empty(Verify(clone, ok).Findings);

        // Unverifiable is not true: the claim fails when no measuredTreeHashes are recorded.
        var unrecorded = Entry(landed, DurableProvenance.BasisIdenticalSrcTree, measured, clone);
        AssertOnly(Verify(clone, unrecorded), DurableProvenance.Codes.FalseIdenticalTree);
    }

    [Fact]
    public void ClaimedTreeAndManifestHashesAreReRead()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"), ("manifest.json", "{}"));
        _world.PushMain();
        var clone = _world.FreshClone();

        var wrongTree = Entry(c, DurableProvenance.BasisMeasuredOnTopOf, c, clone,
            trees: new() { ["src"] = new string('0', 40) });
        AssertOnly(Verify(clone, wrongTree), DurableProvenance.Codes.TreeMismatch);

        var noTrees = Entry(c, DurableProvenance.BasisMeasuredOnTopOf, c, clone, trees: new());
        AssertOnly(Verify(clone, noTrees), DurableProvenance.Codes.TreeMismatch);

        var wrongContent = Entry(c, DurableProvenance.BasisMeasuredOnTopOf, c, clone,
            contents: new() { ["manifest.json"] = new string('a', 64) });
        AssertOnly(Verify(clone, wrongContent), DurableProvenance.Codes.ContentMismatch);
    }

    [Fact]
    public void ShortShaAndTagRulesAreEnforced()
    {
        var parent = _world.CommitOnMain(("src/a.cs", "0"));
        var tagged = _world.CommitOnMain(("src/a.cs", "a"));
        _world.CommitOnMain(("src/a.cs", "b"));
        _world.Dev.Run("tag", "v1.2.3", tagged);
        _world.Dev.Run("tag", "nightly", tagged);
        _world.PushMain();
        _world.Dev.Run("push", "origin", "refs/tags/v1.2.3", "refs/tags/nightly");
        var clone = _world.FreshClone();

        var shortSha = Entry(tagged[..12], DurableProvenance.BasisMeasuredOnTopOf, tagged, clone, treesAt: tagged);
        AssertOnly(Verify(clone, shortSha), DurableProvenance.Codes.NotFullSha);

        var viaTag = Entry(tagged, DurableProvenance.BasisMeasuredOnTopOf, tagged, clone, resolvedVia: "refs/tags/v1.2.3");
        Assert.Empty(Verify(clone, viaTag).Findings);

        var nonRelease = Entry(tagged, DurableProvenance.BasisMeasuredOnTopOf, tagged, clone, resolvedVia: "refs/tags/nightly");
        AssertOnly(Verify(clone, nonRelease), DurableProvenance.Codes.ForbiddenRef);

        var ancestorOfTag = Entry(parent, DurableProvenance.BasisMeasuredOnTopOf, parent, clone, resolvedVia: "refs/tags/v1.2.3");
        AssertOnly(Verify(clone, ancestorOfTag), DurableProvenance.Codes.NotDurable);
    }

    [Fact]
    public void ShortStampExpandsOnlyWhenUniqueAndMatching()
    {
        var c = _world.CommitOnMain(("src/a.cs", "a"));
        var other = _world.CommitOnMain(("src/a.cs", "b"));
        _world.PushMain();
        var clone = _world.FreshClone();

        var ok = Entry(c, DurableProvenance.BasisUniquePrefixExpansion, c[..12], clone);
        Assert.Empty(Verify(clone, ok).Findings);

        var wrong = Entry(other, DurableProvenance.BasisUniquePrefixExpansion, c[..12], clone);
        AssertOnly(Verify(clone, wrong), DurableProvenance.Codes.BadPrefixExpansion);

        var tooShort = Entry(c, DurableProvenance.BasisUniquePrefixExpansion, c[..4], clone);
        AssertOnly(Verify(clone, tooShort), DurableProvenance.Codes.BadPrefixExpansion);
    }

    /// <summary>
    /// The #1159 shape end to end: a ledger measured and stamped on a branch, squash-merged, branch
    /// deleted. The measured commit is gone from a fresh clone; the pending identity is never
    /// authoritative; the write-back finds the squash commit, re-verifies the trees, and the
    /// completed identity resolves from the fresh clone.
    /// </summary>
    [Fact]
    public void SquashMergeWriteBackResolvesFromAFreshClone()
    {
        _world.CommitOnMain(("src/a.cs", "a"), ("docs/readme.md", "r"));
        _world.PushMain();
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "new compiler"));
        var ledgerText = Stamp(measured);
        _world.CommitOnBranch("feature", (Ledger, ledgerText));
        _world.PushBranch("feature");
        var phase1Trees = new Dictionary<string, string> { ["src"] = _world.Dev.RevParse(measured + ":src")! };

        var squash = _world.SquashMergeAndDeleteBranch("feature");
        var clone = _world.FreshClone();
        Assert.False(clone.CommitExists(measured), "the squash should have discarded the branch commit");

        var pending = PendingEntry(measured, phase1Trees, ledgerText);
        var before = Verify(clone, pending);
        Assert.Empty(before.Findings);
        Assert.Empty(before.Authoritative);
        Assert.Single(before.Pending);

        var (completed, failure) = DurableProvenance.CompleteWriteBack(clone, pending);
        Assert.Null(failure);
        Assert.NotNull(completed);
        Assert.Equal(squash, completed!["commit"]!.GetValue<string>());

        var after = Verify(clone, WithIdentity(pending, completed));
        Assert.Empty(after.Findings);
        Assert.Single(after.Authoritative);
    }

    [Fact]
    public void MergeCommitWriteBackNamesTheMergeCommit()
    {
        _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "new compiler"));
        var ledgerText = Stamp(measured);
        _world.CommitOnBranch("feature", (Ledger, ledgerText));
        var merge = _world.MergeCommitInto("feature");
        var clone = _world.FreshClone();

        var pending = PendingEntry(measured, new() { ["src"] = clone.RevParse(measured + ":src")! }, ledgerText);
        var (completed, failure) = DurableProvenance.CompleteWriteBack(clone, pending);

        Assert.Null(failure);
        Assert.Equal(merge, completed!["commit"]!.GetValue<string>());
        Assert.Empty(Verify(clone, WithIdentity(pending, completed)).Findings);
    }

    [Fact]
    public void WriteBackFailsClosedWhenTheMergeChangedAMeasuredTree()
    {
        _world.CommitOnMain(("src/a.cs", "a"));
        _world.PushMain();
        var measured = _world.CommitOnBranch("feature", ("src/a.cs", "new compiler"));
        var ledgerText = Stamp(measured);
        _world.CommitOnBranch("feature", (Ledger, ledgerText));
        _world.PushBranch("feature");
        _world.Checkout("main");
        _world.CommitOnMain(("src/b.cs", "someone else's compiler change"));
        _world.PushMain();
        _world.SquashMergeAndDeleteBranch("feature");
        var clone = _world.FreshClone();

        var pending = PendingEntry(measured, new() { ["src"] = _world.Dev.RevParse(measured + ":src")! }, ledgerText);
        var (completed, failure) = DurableProvenance.CompleteWriteBack(clone, pending);

        Assert.Null(completed);
        Assert.Equal(DurableProvenance.Codes.WriteBackInvalid, failure!.Code);

        // And a hand-written completion with the phase-1 tree is caught by the verifier.
        var squash = clone.RevParse(DurableProvenance.ProtectedMainRef)!;
        var forged = WithIdentity(pending, new JsonObject
        {
            ["status"] = "complete",
            ["commit"] = squash,
            ["resolvedVia"] = DurableProvenance.ProtectedMainRef,
            ["treeHashes"] = new JsonObject { ["src"] = clone.RevParse(squash + ":src") },
            ["inputContentHashes"] = Contents(clone, squash, Ledger),
            ["writeBack"] = new JsonObject
            {
                ["phase1HeadCommit"] = measured,
                ["pr"] = 1,
                ["phase1TreeHashes"] = new JsonObject { ["src"] = _world.Dev.RevParse(measured + ":src") },
            },
        });
        AssertOnly(Verify(clone, forged), DurableProvenance.Codes.WriteBackInvalid);
    }

    [Fact]
    public void PendingIdentityIsNeverAuthoritativeAndMustDescribeTheCommittedArtifact()
    {
        _world.CommitOnMain(("src/a.cs", "a"));
        var measured = _world.CommitOnMain(("src/a.cs", "b"));
        var ledgerText = Stamp(measured);
        _world.CommitOnMain((Ledger, ledgerText));
        _world.PushMain();
        var clone = _world.FreshClone();
        var trees = new Dictionary<string, string> { ["src"] = clone.RevParse(measured + ":src")! };

        var ok = Verify(clone, PendingEntry(measured, trees, ledgerText));
        Assert.Empty(ok.Findings);
        Assert.Empty(ok.Authoritative);

        var drifted = Verify(clone, PendingEntry(measured, trees, ledgerText + " "));
        AssertOnly(drifted, DurableProvenance.Codes.WriteBackInvalid);
        Assert.Empty(drifted.Authoritative);
    }

    [Fact]
    public void LandingCommitOnlyMustHaveLandedTheArtifact()
    {
        var landed = _world.CommitOnMain(("src/a.cs", "a"), (Ledger, Stamp("gone")));
        var later = _world.CommitOnMain(("src/a.cs", "b"));
        _world.PushMain();
        var clone = _world.FreshClone();

        Assert.Empty(Verify(clone, Entry(landed, DurableProvenance.BasisLandingCommitOnly, "gone", clone)).Findings);
        AssertOnly(Verify(clone, Entry(later, DurableProvenance.BasisLandingCommitOnly, "gone", clone)),
            DurableProvenance.Codes.NotLandingCommit);
    }

    // ---- helpers ----

    private static string Stamp(string commit) => $"{{\"measuredCommit\": \"{commit}\"}}\n";

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

    /// <summary>A complete entry whose hashes are read from git unless overridden.</summary>
    private static JsonElement Entry(
        string commit, string basis, string measured, GitRepo git,
        string resolvedVia = DurableProvenance.ProtectedMainRef,
        Dictionary<string, string>? trees = null,
        Dictionary<string, string>? contents = null,
        Dictionary<string, string>? measuredTrees = null,
        string? treesAt = null)
    {
        var at = treesAt ?? commit;
        var treeObject = new JsonObject();
        foreach (var (k, v) in trees ?? new() { ["src"] = git.RevParse(at + ":src")! }) treeObject[k] = v;
        var contentObject = new JsonObject();
        if (contents != null) foreach (var (k, v) in contents) contentObject[k] = v;
        else
        {
            var path = git.RevParse(at + ":manifest.json") != null ? "manifest.json" : "src/a.cs";
            contentObject[path] = DurableProvenance.Sha256(git.ReadBlob($"{at}:{path}")!);
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
                ["treeHashes"] = treeObject,
                ["inputContentHashes"] = contentObject,
            },
        };
        if (measuredTrees != null)
        {
            var m = new JsonObject();
            foreach (var (k, v) in measuredTrees) m[k] = v;
            entry["measuredTreeHashes"] = m;
        }
        return JsonDocument.Parse(entry.ToJsonString()).RootElement.Clone();
    }

    private static JsonElement PendingEntry(string measured, Dictionary<string, string> trees, string ledgerText)
    {
        var treeObject = new JsonObject();
        foreach (var (k, v) in trees) treeObject[k] = v;
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
                    ["treeHashes"] = treeObject,
                    ["inputContentHashes"] = new JsonObject
                    {
                        [Ledger] = DurableProvenance.Sha256(Encoding.UTF8.GetBytes(ledgerText)),
                    },
                    ["artifactSha256"] = DurableProvenance.Sha256(Encoding.UTF8.GetBytes(ledgerText)),
                },
            },
        };
        return JsonDocument.Parse(entry.ToJsonString()).RootElement.Clone();
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
        private int _clones;

        public GitWorld()
        {
            Directory.CreateDirectory(_root);
            var emptyConfig = Path.Combine(_root, "empty.gitconfig");
            File.WriteAllText(emptyConfig, "");
            _env = new Dictionary<string, string>
            {
                ["GIT_CONFIG_GLOBAL"] = emptyConfig,
                ["GIT_CONFIG_NOSYSTEM"] = "1",
                ["GIT_AUTHOR_NAME"] = "t", ["GIT_AUTHOR_EMAIL"] = "t@example.invalid",
                ["GIT_COMMITTER_NAME"] = "t", ["GIT_COMMITTER_EMAIL"] = "t@example.invalid",
                ["GIT_TERMINAL_PROMPT"] = "0",
            };
            Origin = Path.Combine(_root, "origin.git");
            Must(new GitRepo(_root, _env), "init", "--quiet", "--bare", "--initial-branch=main", Origin);
            var dev = Path.Combine(_root, "dev");
            Must(new GitRepo(_root, _env), "clone", "--quiet", Origin, dev);
            Dev = new GitRepo(dev, _env);
            Must(Dev, "symbolic-ref", "HEAD", "refs/heads/main");
        }

        public string Origin { get; }
        public GitRepo Dev { get; }
        private string OriginUrl => new Uri(Origin).AbsoluteUri;

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
            if (exit == 0 && current.Trim() == branch) return;
            Must(Dev, "checkout", "--quiet", branch);
        }
        public void PushMain() => Must(Dev, "push", "--quiet", "origin", "main");
        public void PushBranch(string branch) => Must(Dev, "push", "--quiet", "origin", branch);

        /// <summary>Publishes a GitHub-style refs/pull/N/merge without touching main.</summary>
        public string MergeRefFor(string branch, string baseCommit, int prNumber)
        {
            Must(Dev, "checkout", "--quiet", "--detach", baseCommit);
            Must(Dev, "merge", "--quiet", "--no-ff", "--no-edit", branch);
            var merge = Dev.RevParse("HEAD")!;
            Must(Dev, "push", "--quiet", "origin", $"{merge}:refs/pull/{prNumber}/merge");
            Checkout("main");
            return merge;
        }

        public string SquashMergeAndDeleteBranch(string branch)
        {
            Checkout("main");
            Must(Dev, "merge", "--quiet", "--squash", branch);
            Must(Dev, "commit", "--quiet", "-m", "squash " + branch);
            var squash = Dev.RevParse("HEAD")!;
            PushMain();
            Dev.Run("push", "--quiet", "origin", "--delete", branch);
            Must(Dev, "branch", "--quiet", "-D", branch);
            return squash;
        }

        public string MergeCommitInto(string branch)
        {
            Checkout("main");
            Must(Dev, "merge", "--quiet", "--no-ff", "--no-edit", branch);
            PushMain();
            return Dev.RevParse("HEAD")!;
        }

        public GitRepo FreshClone(int? depth = null)
        {
            var path = Path.Combine(_root, "clone" + _clones++);
            var args = new List<string> { "clone", "--quiet", "--no-local" };
            if (depth is int d) args.AddRange(new[] { "--depth", d.ToString(), OriginUrl });
            else args.Add(Origin);
            args.Add(path);
            Must(new GitRepo(_root, _env), args.ToArray());
            return new GitRepo(path, _env);
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
