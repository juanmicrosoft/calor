using System.Text;
using Calor.Compiler.Tests.Provenance;
using Xunit;
using static Calor.Compiler.Tests.EvidenceContract.CandidateInvalidation;
using static Calor.Compiler.Tests.EvidenceContract.ReleaseCandidateInvalidation;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>Controls for the generalized candidate classifier with the 0.25 profile: the path rule, then real git.</summary>
public sealed class ReleaseCandidateInvalidationTests : IDisposable
{
    private const string Regular = "100644";
    private static readonly CandidateProfile P = V025;

    [Theory]
    [InlineData("src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs", "compiler")]
    [InlineData(".github/workflows/publish-nuget.yml", "workflow")]
    [InlineData("Directory.Build.props", "MSBuild")]
    [InlineData("global.json", "environment")]
    [InlineData("CHANGELOG.md", "publication")]
    [InlineData("website/content/changelog.mdx", "publication")]
    [InlineData("eng/release-adjudication-map.json", "release adjudication map")]
    [InlineData("docs/plans/evidence/v0.25-r0-1426/scope.json", "scope")]
    [InlineData("docs/plans/evidence/v0.25-t1-1428/register.json", "register")]
    [InlineData("docs/plans/evidence/c1-1423/candidate-manifest.json", "documentation")]
    [InlineData("docs/plans/evidence/v0.25-candidatex/candidate-manifest.json", "documentation")]
    [InlineData("docs/plans/evidence/adjudication-1430/gate.py", "downstream evidence")]
    [InlineData("docs/plans/evidence/v0.25-regeneration/.hidden/r.json", "downstream evidence")]
    [InlineData("tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs", "test hosts")]
    [InlineData("scripts/verify_release_adjudication.py", "release scripts")]
    public void RelevantChangeInvalidatesInEitherPhase(string path, string category)
    {
        foreach (var phase in new[] { Phase.BeforeLanding, Phase.AfterLanding })
        {
            var decision = Classify(P, Modify(path), phase);
            Assert.False(decision.Exempt, $"{path} [{phase}]: {decision.Reason}");
            Assert.Contains(category, decision.Reason);
        }
    }

    [Theory]
    [InlineData("docs/plans/evidence/v0.25-candidate/candidate-manifest.json")]
    [InlineData("docs/plans/evidence/v0.25-candidate/generate_candidate_manifest.py")]
    [InlineData("docs/plans/evidence/v0.25-candidate/inputs.json")]
    [InlineData("tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs")]
    [InlineData("tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs")]
    public void CandidateFilesAreExemptBeforeTheLandingAndFrozenAfter(string path)
    {
        Assert.True(Classify(P, Modify(path), Phase.BeforeLanding).Exempt, path);
        var after = Classify(P, Modify(path), Phase.AfterLanding);
        Assert.False(after.Exempt);
        Assert.Contains("return to #1430", after.Reason);
    }

    [Theory]
    [InlineData("120000")]
    [InlineData("100755")]
    [InlineData("160000")]
    public void NonRegularFilesAreNeverExempt(string mode)
    {
        foreach (var path in new[] { "docs/plans/evidence/v0.25-regeneration/r.json", "docs/plans/evidence/v0.25-candidate/notes.md" })
            Assert.False(Classify(P, new Change("A", path, "000000", mode, null, Bytes("x")), Phase.BeforeLanding).Exempt, path);
    }

    [Theory]
    [InlineData("docs/plans/evidence/v0.25-candidate/reviews/round-1-codex.md", Phase.AfterLanding)]
    [InlineData("docs/plans/evidence/v0.25-regeneration/ledger.json", Phase.AfterLanding)]
    [InlineData("docs/plans/evidence/adjudication-1430/terminal-record.json", Phase.AfterLanding)]
    [InlineData("docs/plans/evidence/adjudication-1430/claim-registry.json", Phase.BeforeLanding)]
    public void AddedReviewAndDownstreamRecordsAreExempt(string path, Phase phase)
        => Assert.True(Classify(P, new Change("A", path, "000000", Regular, null, Bytes("{}")), phase).Exempt, path);

    [Theory]
    [InlineData("docs/plans/evidence/adjudication-1430/terminal-record.json")]
    [InlineData("docs/plans/evidence/v0.25-candidate/reviews/round-1-codex.md")]
    public void ModifyingOrDeletingARecordAfterTheLandingInvalidates(string path)
    {
        Assert.False(Classify(P, Modify(path), Phase.AfterLanding).Exempt);
        Assert.False(Classify(P, new Change("D", path, Regular, "000000", Bytes("{}"), null), Phase.AfterLanding).Exempt);
    }

    [Fact]
    public void TestManifestBumpIsExemptOnlyBeforeTheLanding()
    {
        var (before, after) = (Bytes(TestManifest), Bytes(TestManifest.Replace("100", "140").Replace("\"base.\"", "\"base. c.\"")));
        Assert.True(Classify(P, new Change("M", TestManifestPath, Regular, Regular, before, after), Phase.BeforeLanding).Exempt);
        Assert.False(Classify(P, new Change("M", TestManifestPath, Regular, Regular, before, after), Phase.AfterLanding).Exempt);
        Assert.False(Classify(P, new Change("M", TestManifestPath, Regular, Regular, after, before), Phase.BeforeLanding).Exempt);
    }

    // ---- Real git ----

    private readonly Repo _repo = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void FreezeOnlyCommitsDoNotInvalidateAndLandingIsFound()
    {
        var candidate = _repo.Candidate();
        var landing = _repo.Commit((P.ManifestPath, "{}"), ("docs/plans/evidence/v0.25-candidate/inputs.json", "{}"),
            ("eng/test-manifest.json", TestManifest.Replace("100", "140").Replace("\"base.\"", "\"base. c.\"")));
        _repo.PublishMain();
        _repo.Commit(("docs/plans/evidence/adjudication-1430/terminal-record.json", "{}"));
        var report = ClassifyRange(_repo.Git, P, candidate, "HEAD");
        Assert.True(report.Verdict == NotInvalidated, report.ToString());
        Assert.Equal(landing, report.Landing);
    }

    [Fact]
    public void AfterTheLandingTheClassifierCannotExemptItsOwnEdit()
    {
        var candidate = _repo.Candidate();
        _repo.Commit((P.ManifestPath, "{}"));
        _repo.PublishMain();
        _repo.Commit(("tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs", "// weakened"));
        var report = ClassifyRange(_repo.Git, P, candidate, "HEAD");
        Assert.Equal(Invalid, report.Verdict);
        Assert.All(report.Invalidating, d => Assert.Contains("return to #1430", d.Reason));
    }

    [Theory]
    [InlineData("src/Calor.Compiler/Migration/CalorEmitter.cs")]
    [InlineData("docs/plans/evidence/v0.25-r0-1426/scope.json")]
    [InlineData("CHANGELOG.md")]
    public void ARelevantCommitInAMergedBranchInvalidates(string path)
    {
        var candidate = _repo.Candidate();
        _repo.Branch("feature");
        _repo.Commit((path, "changed"));
        _repo.Checkout("main");
        _repo.Commit((P.ManifestPath, "{}"));
        _repo.Merge("feature");
        var report = ClassifyRange(_repo.Git, P, candidate, "HEAD");
        Assert.Equal(Invalid, report.Verdict);
        Assert.Equal(new[] { path }, report.Invalidating.Select(d => d.Path).Distinct());
    }

    [Fact]
    public void ARevertedChangeStillInvalidates()
    {
        var candidate = _repo.Candidate();
        _repo.Commit(("src/a.cs", "changed"));
        _repo.Commit(("src/a.cs", "a"));
        Assert.Equal(Invalid, ClassifyRange(_repo.Git, P, candidate, "HEAD").Verdict);
    }

    [Fact]
    public void UnfetchedMainOrAShortShaIsUndecided()
    {
        var candidate = _repo.Candidate();
        Assert.Equal(Undecided, ClassifyRange(_repo.Git, P, candidate[..12], "HEAD").Verdict);
        _repo.Git.Run("update-ref", "-d", DurableProvenance.ProtectedMainRef);
        Assert.Equal(Undecided, ClassifyRange(_repo.Git, P, candidate, "HEAD").Verdict);
    }

    /// <summary>
    /// Every git failure is UNDECIDED, never "no changes" or "not landed". Without the landing, the weakened
    /// classifier below would pass as a before-landing edit, so each guard is needed for its row to pass.
    /// </summary>
    [Theory]
    [InlineData("merge-base", "target", "git merge-base cannot read the history from")]
    [InlineData("merge-base", "main", "git merge-base cannot read the history from")]
    [InlineData("merge-base", "phase", "git merge-base cannot place ")]
    [InlineData("rev-list", "--parents", "git rev-list ")]
    [InlineData("rev-list", "--first-parent", "git rev-list --first-parent ")]
    [InlineData("ls-tree", "", "git ls-tree ")]
    [InlineData("diff-tree", "", "git diff-tree ")]
    public void AFailedGitCommandIsUndecided(string command, string which, string error)
    {
        var candidate = _repo.Candidate();
        var main = _repo.Commit((P.ManifestPath, "{}"));
        _repo.PublishMain();
        var target = _repo.Commit(("tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs", "// weakened"));
        bool Fails(string[] a) => a[0] == command && which switch
        {
            "target" => a[^1] == target,
            "main" => a[^1] == main,
            "phase" => a[^2] == target && a[^1] == main,
            "" => true,
            _ => a[1] == which,
        };
        ReleaseCandidateInvalidation.GitRun run = args => Fails(args) ? (128, "", "fatal: injected") : _repo.Git.Run(args);
        var report = ClassifyRange(_repo.Git, P, candidate, "HEAD", run);
        Assert.True(report.Verdict == Undecided, report.ToString());
        Assert.Contains(report.Errors, e => e.StartsWith(error, StringComparison.Ordinal));
    }

    /// <summary>Each blob guard alone: a landing whose manifest blob is unreadable, and an unreadable added record.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnreadableBlobIsAnError(bool manifest)
    {
        var candidate = _repo.Candidate();
        _repo.Commit((P.ManifestPath, "{\"unique\": 1430}"));
        _repo.PublishMain();
        var record = "docs/plans/evidence/adjudication-1430/terminal-record.json";
        _repo.Commit((record, "{\"unique\": 1431}"));
        _repo.DeleteObject(_repo.Git.RevParse("HEAD:" + (manifest ? P.ManifestPath : record))!);
        var errors = new List<string>();
        Assert.Equal(manifest ? null : _repo.Git.RevParse("HEAD^"), FindLanding(_repo.Git, P, candidate, errors));
        Assert.Equal(manifest, errors.Count > 0);
        if (!manifest)
            Assert.Contains(ClassifyRange(_repo.Git, P, candidate, "HEAD").Errors, e => e.StartsWith("a blob of " + record, StringComparison.Ordinal));
    }

    private const string TestManifest = """
        {
          "projects": [
            { "path": "tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj", "expectedTotal": 100, "expectedSkipped": 3, "note": "base." }
          ],
          "allowedSkippedTests": []
        }
        """;

    private static Change Modify(string path) => new("M", path, Regular, Regular, Bytes("a"), Bytes("b"));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A scratch repository; refs/remotes/origin/main is set by hand to simulate fetched protected main.</summary>
    private sealed class Repo : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-v025-candidate-" + Guid.NewGuid().ToString("N"));

        public Repo()
        {
            Directory.CreateDirectory(_root);
            var config = _root + ".gitconfig";
            File.WriteAllText(config, "");
            Git = new GitRepo(_root, new Dictionary<string, string>
            {
                ["GIT_CONFIG_GLOBAL"] = config, ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_TERMINAL_PROMPT"] = "0",
                ["GIT_AUTHOR_NAME"] = "t", ["GIT_AUTHOR_EMAIL"] = "t@example.invalid",
                ["GIT_COMMITTER_NAME"] = "t", ["GIT_COMMITTER_EMAIL"] = "t@example.invalid",
            });
            Must("init", "--quiet", "--initial-branch=main");
        }

        public GitRepo Git { get; }

        public string Candidate()
        {
            var c = Commit(("src/a.cs", "a"), ("eng/test-manifest.json", TestManifest));
            PublishMain();
            return c;
        }

        public void PublishMain() => Must("update-ref", DurableProvenance.ProtectedMainRef, "HEAD");

        public string Commit(params (string Path, string Text)[] files)
        {
            foreach (var (path, text) in files)
            {
                var full = Path.Combine(_root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
                Must("add", "--", path);
            }
            Must("commit", "--quiet", "--allow-empty", "--no-gpg-sign", "-m", "c");
            return Git.RevParse("HEAD")!;
        }

        public void Branch(string name) => Must("checkout", "--quiet", "-b", name);
        public void Checkout(string what) => Must("checkout", "--quiet", what);
        public void Merge(string branch) => Must("merge", "--quiet", "--no-ff", "--no-edit", "--no-gpg-sign", branch);

        /// <summary>Removes a loose object, so it cannot be read.</summary>
        public void DeleteObject(string sha)
        {
            var file = Path.Combine(_root, ".git", "objects", sha[..2], sha[2..]);
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        private void Must(params string[] args)
            => Assert.True(Git.Run(args) is (0, _, _), $"git {string.Join(' ', args)} failed");

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_root, recursive: true);
                File.Delete(_root + ".gitconfig");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
