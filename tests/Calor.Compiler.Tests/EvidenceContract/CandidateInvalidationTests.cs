using System.Text;
using Calor.Compiler.Tests.Provenance;
using Xunit;
using static Calor.Compiler.Tests.EvidenceContract.CandidateInvalidation;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>#1423 (0.24 C1) classifier controls: the pure path rule, then real git with a simulated protected main.</summary>
public sealed class CandidateInvalidationTests : IDisposable
{
    private const string Regular = "100644";

    // ---- Path rule: positive controls (the candidate is invalidated) ----

    [Theory]
    [InlineData("src/Calor.Compiler/Parsing/Parser.cs", "compiler")]
    [InlineData(".github/workflows/test.yml", "workflow")]
    [InlineData(".github/z3-binaries-4.15.7.sha256", "workflow")]
    [InlineData("src/Calor.Compiler/packages.lock.json", "compiler")]
    [InlineData("Directory.Packages.props", "dependencies")]
    [InlineData("global.json", "environment")]
    [InlineData("website/content/docs/index.mdx", "publication")]
    [InlineData("website/public/data/benchmark-headline.json", "publication")]
    [InlineData("CHANGELOG.md", "publication")]
    [InlineData("README.md", "publication")]
    [InlineData("docs/syntax-reference.md", "documentation")]
    [InlineData("docs/plans/v0.24-evidence-contract.md", "documentation")]
    [InlineData("docs/plans/evidence/evidence-contract-1407/contract.json", "documentation")]
    [InlineData("docs/plans/evidence/g2-1421/protocol.json", "documentation")]
    [InlineData("docs/plans/evidence/s2-1413/reviews/late-note.md", "documentation")]
    [InlineData("scripts/determinism_runner.py", "evidence producers")]
    [InlineData("bench/Calor.Soundness.Sweep/Program.cs", "benchmark")]
    [InlineData("tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DeterminismRecord.cs", "test hosts")]
    [InlineData("eng/z3-consumers.json", "registered inventories")]
    [InlineData(".claude/skills/create-release/SKILL.md", "release procedure")]
    [InlineData("samples/FizzBuzz/fizzbuzz.calr", "samples")]
    [InlineData(".gitattributes", "checkout")]
    [InlineData("Docs/plans/evidence/c1-1423/candidate-manifest.json", "default deny")]
    [InlineData("docs/plans/evidence/c1-1423x/candidate-manifest.json", "documentation")]
    [InlineData("docs/plans/evidence/c2-1424/regenerate.py", "downstream evidence")]
    [InlineData("docs/plans/evidence/c2-1424/.gitattributes", "downstream evidence")]
    [InlineData("docs/plans/evidence/c2-1424/.hidden/results.json", "downstream evidence")]
    [InlineData("tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs", "test hosts")]
    public void RelevantChangeInvalidatesInEitherPhase(string path, string category)
    {
        foreach (var phase in new[] { Phase.BeforeLanding, Phase.AfterLanding })
        {
            var decision = Classify(Modify(path), phase);
            Assert.False(decision.Exempt, $"{path} [{phase}]: {decision.Reason}");
            Assert.Contains(category, decision.Reason);
        }
    }

    [Fact]
    public void SubmoduleBumpInvalidates()
    {
        var bump = new Change("M", "bench/corpus/MediatR", "160000", "160000", null, null);
        Assert.False(Classify(bump, Phase.BeforeLanding).Exempt);
    }

    [Theory]
    [InlineData("120000")]   // symlink
    [InlineData("100755")]   // executable
    [InlineData("160000")]   // gitlink
    public void NonRegularFileInAnExemptDirectoryInvalidates(string mode)
    {
        foreach (var path in new[] { "docs/plans/evidence/c2-1424/results.json", "docs/plans/evidence/c1-1423/notes.md" })
            Assert.False(Classify(new Change("A", path, "000000", mode, null, Bytes("x")), Phase.BeforeLanding).Exempt, path);
    }

    [Theory]
    [InlineData(CandidateInvalidation.ManifestPath)]
    [InlineData("docs/plans/evidence/c1-1423/generate_candidate_manifest.py")]
    [InlineData("tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs")]
    [InlineData("tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidationTests.cs")]
    [InlineData("docs/plans/v0.24-c1-candidate.md")]
    public void C1FilesAreFrozenAfterLanding(string path)
    {
        Assert.True(Classify(Modify(path), Phase.BeforeLanding).Exempt, path);
        var after = Classify(Modify(path), Phase.AfterLanding);
        Assert.False(after.Exempt);
        Assert.Contains("return to #1423", after.Reason);
    }

    // ---- Path rule: negative controls (exempt; the candidate stays valid) ----

    [Theory]
    [InlineData("docs/plans/evidence/c1-1423/reviews/round-1-codex.md", Phase.AfterLanding)]
    [InlineData("docs/plans/evidence/c1-1423/reviews/range-check.txt", Phase.BeforeLanding)]
    [InlineData("docs/plans/evidence/c2-1424/regeneration-ledger.json", Phase.AfterLanding)]
    [InlineData("docs/plans/evidence/c2-1424/runs/attempts.tar.gz", Phase.AfterLanding)]
    [InlineData("docs/plans/evidence/adjudication-1408/terminal-record.json", Phase.AfterLanding)]
    public void ReviewAndDownstreamRecordsAreExempt(string path, Phase phase)
        => Assert.True(Classify(new Change("A", path, "000000", Regular, null, Bytes("{}")), phase).Exempt, path);

    [Fact]
    public void ReviewScriptIsNotExempt()
        => Assert.False(Classify(Modify("docs/plans/evidence/c1-1423/reviews/rerun.py"), Phase.AfterLanding).Exempt);

    /// <summary>A downstream or late review record, once added, may be a frozen input (C2's claim registry).</summary>
    [Theory]
    [InlineData("docs/plans/evidence/c2-1424/claim-registry.json")]
    [InlineData("docs/plans/evidence/adjudication-1408/terminal-record.json")]
    [InlineData("docs/plans/evidence/c1-1423/reviews/round-1-codex.md")]
    public void ModifyingOrDeletingARecordInvalidates(string path)
    {
        Assert.False(Classify(Modify(path), Phase.AfterLanding).Exempt);
        Assert.False(Classify(new Change("D", path, Regular, "000000", Bytes("{}"), null), Phase.AfterLanding).Exempt);
    }

    [Fact]
    public void TestManifestBumpAfterLandingOrWithAModeChangeInvalidates()
    {
        var (before, after) = (Bytes(TestManifest), Bytes(TestManifest.Replace("100", "140").Replace("\"base.\"", "\"base. c1.\"")));
        Assert.True(Classify(new Change("M", TestManifestPath, Regular, Regular, before, after), Phase.BeforeLanding).Exempt);
        Assert.False(Classify(new Change("M", TestManifestPath, Regular, Regular, before, after), Phase.AfterLanding).Exempt);
        Assert.False(Classify(new Change("M", TestManifestPath, Regular, "100755", before, after), Phase.BeforeLanding).Exempt);
    }

    // ---- eng/test-manifest.json: only the C1 bump ----

    private const string TestManifest = """
        {
          "projects": [
            { "path": "tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj", "expectedTotal": 100, "expectedSkipped": 3, "note": "base." },
            { "path": "tests/Calor.Verification.Tests/Calor.Verification.Tests.csproj", "expectedTotal": 50, "expectedSkipped": 0, "note": "v." }
          ],
          "allowedSkippedTests": []
        }
        """;

    [Fact]
    public void TestManifestBumpForCompilerTestsIsExempt()
    {
        var bumped = TestManifest.Replace("\"expectedTotal\": 100", "\"expectedTotal\": 140").Replace("\"base.\"", "\"base. C1 +40.\"");
        var (exempt, reason) = TestManifestDelta(Bytes(TestManifest), Bytes(bumped));
        Assert.True(exempt, reason);
    }

    /// <summary>Each case is a valid-looking bump plus exactly one other difference.</summary>
    [Theory]
    [InlineData("\"expectedTotal\": 50", "\"expectedTotal\": 60", "other fields")]          // another project's count
    [InlineData("\"expectedSkipped\": 3", "\"expectedSkipped\": 4", "other fields")]        // compiler skips
    [InlineData("\"base. c1.\"", "\"rewritten.\"", "append-only")]                          // note rewritten
    [InlineData("\"allowedSkippedTests\": []", "\"allowedSkippedTests\": [\"x\"]", "other fields")]
    [InlineData("\"expectedTotal\": 140", "\"expectedTotal\": 90", "did not rise")]         // count falls
    [InlineData("\"expectedTotal\": 140", "\"expectedTotal\": 100", "did not rise")]        // count unchanged
    public void TestManifestOtherChangeInvalidates(string from, string to, string reason)
    {
        var bumped = TestManifest.Replace("\"expectedTotal\": 100", "\"expectedTotal\": 140").Replace("\"base.\"", "\"base. c1.\"");
        var changed = bumped.Replace(from, to);
        Assert.NotEqual(bumped, changed);
        var (exempt, why) = TestManifestDelta(Bytes(TestManifest), Bytes(changed));
        Assert.False(exempt, to);
        Assert.Contains(reason, why);
    }

    [Fact]
    public void TestManifestAddedOrDeletedInvalidates()
    {
        Assert.False(TestManifestDelta(null, Bytes(TestManifest)).Exempt);
        Assert.False(TestManifestDelta(Bytes(TestManifest), null).Exempt);
        Assert.False(TestManifestDelta(Bytes(TestManifest), Bytes("not json")).Exempt);
    }

    // ---- End to end on real git ----

    private readonly Repo _repo = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void C1OnlyCommitsDoNotInvalidate()
    {
        var candidate = _repo.Candidate();
        _repo.Commit((ManifestPath, "{}"), ("docs/plans/v0.24-c1-candidate.md", "x"),
            ("tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs", "// c"),
            ("eng/test-manifest.json", TestManifest.Replace("100", "140").Replace("\"base.\"", "\"base. c1.\"")));
        var report = ClassifyRange(_repo.Git, candidate, "HEAD");
        Assert.True(report.Verdict == NotInvalidated, report.ToString());
        Assert.Null(report.Landing);
    }

    [Theory]
    [InlineData("src/Calor.Compiler/Verification/Z3/Z3Verifier.cs")]
    [InlineData(".github/workflows/publish-nuget.yml")]
    [InlineData("src/Calor.Runtime/packages.lock.json")]
    [InlineData("website/content/docs/index.mdx")]
    public void RelevantCommitInvalidates(string path)
    {
        var candidate = _repo.Candidate();
        _repo.Commit((ManifestPath, "{}"));
        _repo.Commit((path, "changed"));
        var report = ClassifyRange(_repo.Git, candidate, "HEAD");
        Assert.Equal(Invalid, report.Verdict);
        Assert.Equal(new[] { path }, report.Invalidating.Select(d => d.Path).Distinct());
    }

    /// <summary>A gitlink bump stays visible even when local config tells git diff to ignore the submodule.</summary>
    [Fact]
    public void AGitlinkBumpInvalidatesDespiteSubmoduleIgnoreConfig()
    {
        _repo.Commit((".gitmodules", "[submodule \"bench/corpus/MediatR\"]\n\tpath = bench/corpus/MediatR\n\turl = https://example.invalid/m.git\n"));
        var candidate = _repo.Candidate();
        _repo.Git.Run("config", "submodule.bench/corpus/MediatR.ignore", "all");
        _repo.Git.Run("update-index", "--add", "--cacheinfo", "160000," + candidate + ",bench/corpus/MediatR");
        _repo.Commit();
        // The configuration really hides the bump from a diff without --ignore-submodules=none.
        Assert.DoesNotContain("MediatR", _repo.Git.Run("diff-tree", "-r", "--name-only", "HEAD^", "HEAD").Output);
        var report = ClassifyRange(_repo.Git, candidate, "HEAD");
        Assert.Equal(Invalid, report.Verdict);
        Assert.Equal(new[] { "bench/corpus/MediatR" }, report.Invalidating.Select(d => d.Path));
    }

    [Fact]
    public void ARevertedChangeStillInvalidates()
    {
        var candidate = _repo.Candidate();
        _repo.Commit(("src/a.cs", "changed"));
        _repo.Commit(("src/a.cs", "a"));
        Assert.Equal(_repo.Git.RevParse(candidate + "^{tree}"), _repo.Git.RevParse("HEAD^{tree}"));
        Assert.Equal(Invalid, ClassifyRange(_repo.Git, candidate, "HEAD").Verdict);
    }

    [Fact]
    public void AMergedBranchIsClassifiedCommitByCommit()
    {
        var candidate = _repo.Candidate();
        _repo.Branch("feature");
        _repo.Commit((".github/workflows/test.yml", "on: push"));
        _repo.Checkout("main");
        _repo.Commit(("docs/plans/evidence/c1-1423/reviews/r1.md", "ok"));
        _repo.Merge("feature");
        var report = ClassifyRange(_repo.Git, candidate, "HEAD");
        Assert.Equal(Invalid, report.Verdict);
        Assert.All(report.Invalidating, d => Assert.Equal(".github/workflows/test.yml", d.Path));
    }

    [Fact]
    public void AfterLandingTheClassifierCannotExemptItsOwnEdit()
    {
        var candidate = _repo.Candidate();
        var landing = _repo.Commit((ManifestPath, "{}"), ("tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs", "// c"));
        _repo.PublishMain();
        _repo.Commit(("docs/plans/evidence/c1-1423/reviews/late.md", "ok"));
        var reviewOnly = ClassifyRange(_repo.Git, candidate, "HEAD");
        Assert.True(reviewOnly.Verdict == NotInvalidated, reviewOnly.ToString());
        Assert.Equal(landing, reviewOnly.Landing);

        _repo.Commit(("tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs", "// weakened"));
        var weakened = ClassifyRange(_repo.Git, candidate, "HEAD");
        Assert.Equal(Invalid, weakened.Verdict);
        Assert.All(weakened.Invalidating, d => Assert.Contains("return to #1423", d.Reason));
    }

    [Fact]
    public void ATargetWithoutTheCandidateIsInvalid()
    {
        var root = _repo.Commit(("README.md", "r"));
        var candidate = _repo.Candidate();
        _repo.Branch("other", start: root);
        _repo.Commit(("docs/plans/evidence/c1-1423/reviews/r.md", "x"));
        Assert.Equal(Invalid, ClassifyRange(_repo.Git, candidate, "HEAD").Verdict);
    }

    [Fact]
    public void UnfetchedMainOrAShortShaIsUndecided()
    {
        var candidate = _repo.Candidate();
        _repo.Commit((ManifestPath, "{}"));
        Assert.Equal(Undecided, ClassifyRange(_repo.Git, candidate[..12], "HEAD").Verdict);
        _repo.Git.Run("update-ref", "-d", DurableProvenance.ProtectedMainRef);
        Assert.Equal(Undecided, ClassifyRange(_repo.Git, candidate, "HEAD").Verdict);
    }

    private static Change Modify(string path) => new("M", path, Regular, Regular, Bytes("a"), Bytes("b"));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>One repository; refs/remotes/origin/main is set by hand to simulate fetched protected main.</summary>
    private sealed class Repo : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-1423-" + Guid.NewGuid().ToString("N"));

        public Repo()
        {
            Directory.CreateDirectory(_root);
            var config = Path.Combine(_root, "..", Path.GetFileName(_root) + ".gitconfig");
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

        /// <summary>A candidate on protected main with a source file and a test manifest.</summary>
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

        public void Branch(string name, string? start = null)
            => Must(start == null ? new[] { "checkout", "--quiet", "-b", name } : new[] { "checkout", "--quiet", "-b", name, start });

        public void Checkout(string what) => Must("checkout", "--quiet", what);

        public void Merge(string branch) => Must("merge", "--quiet", "--no-ff", "--no-edit", "--no-gpg-sign", branch);

        private void Must(params string[] args)
        {
            var (exit, output, error) = Git.Run(args);
            Assert.True(exit == 0, $"git {string.Join(' ', args)} failed ({exit}): {output}{error}");
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_root, recursive: true);
                File.Delete(Path.Combine(_root, "..", Path.GetFileName(_root) + ".gitconfig"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
