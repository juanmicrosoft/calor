using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.InteropScope;
using Calor.Compiler.Tests.Provenance;
using Xunit;
using Xunit.Abstractions;
using static Calor.Compiler.Tests.EvidenceContract.ReleaseCandidateInvalidation;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// The 0.25 release candidate (#1430): the committed manifest, re-verified from the candidate's git objects
/// independently of its Python generator, plus the 0.25 validators run on the candidate's own blobs. Each
/// tamper control changes one recorded fact and expects only its finding.
/// </summary>
public class V025CandidateManifestTests
{
    private const string ScopeDir = "docs/plans/evidence/v0.25-r0-1426";
    private const string TargetVariable = "CALOR_V025_CLASSIFY_TARGET";
    private readonly ITestOutputHelper _output;

    public V025CandidateManifestTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CommittedManifestVerifiesAgainstTheCandidate()
    {
        var findings = Verify(Git(), Manifest());
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void CandidateIsThePreFreezeMergeAndDeclaresTheReleaseVersion()
    {
        var manifest = Manifest();
        Assert.Equal("48cae911700ceb0dfc358e00933650445d81604f", Str(manifest["candidate"]!["commit"]));
        Assert.Equal(1576, manifest["candidate"]!["mergeOf"]!["pr"]!.GetValue<int>());
        Assert.Equal("0.25.0", Str(manifest["releasability"]!["versionAtCandidate"]));
        Assert.Equal("not-yet-adjudicated", Str(manifest["releasability"]!["adjudicationMapEntry"]!["status"]));
        var gaps = manifest["issueStatesAtFreeze"]!["releaseGaps"]!;
        Assert.True(gaps["met"]!.GetValue<bool>());
        Assert.Equal(new[] { 1545, 1546, 1547 }, gaps["closures"]!.AsArray().Select(c => c!["issue"]!.GetValue<int>()));
        Assert.All(gaps["closures"]!.AsArray(), c => Assert.EndsWith("Z", Str(c!["closedAtUtc"])));
        Assert.Contains(manifest["knownOpenItems"]!.AsArray(), i => i!["issue"]?.GetValue<int>() == 1578);
        Assert.Contains(manifest["maintainerDecisions"]!.AsArray(), d => Str(d!["id"]) == "D-ACCEPT-FILE-FREEZE");
    }

    /// <summary>
    /// The freeze PR lands after the candidate. By default this classifies every commit up to the newest one that
    /// changes a candidate file; the regeneration and A1 set <c>CALOR_V025_CLASSIFY_TARGET</c> to their own commit,
    /// from a checkout of the landing (the trust anchor).
    /// </summary>
    [Fact]
    public void RangeFromTheCandidateIsNotInvalidated()
    {
        var git = Git();
        var candidate = Str(Manifest()["candidate"]!["commit"]);
        var requested = Environment.GetEnvironmentVariable(TargetVariable);
        var errors = new List<string>();
        var target = string.IsNullOrWhiteSpace(requested) ? NewestProfileCommit(git, V025, candidate, errors) : requested.Trim();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.True(target != null, "no commit after the candidate changes a candidate file; is history fetched?");
        var report = ClassifyRange(git, V025, candidate, target!);
        _output.WriteLine(report.ToString());
        // R0 scope amendment 1.5.0 (D-AMEND-REFREEZE): a recorded maintainer decision can supersede this candidate. Until the
        // re-freeze lands a manifest naming its successor, the superseding changes invalidate the range, and the test records
        // that instead of claiming NOT-INVALIDATED. A run with an explicit target (the regeneration, A1) is never excused.
        if (string.IsNullOrWhiteSpace(requested) && SupersedingDecision(candidate) is { } superseding)
        {
            _output.WriteLine($"candidate {candidate} is superseded by {superseding}");
            Assert.True(report.Verdict == CandidateInvalidation.Invalid, report.ToString());
            return;
        }
        if (!string.IsNullOrWhiteSpace(requested))
        {
            Assert.True(report.Landing != null, "the freeze has not landed on origin/main; judge later commits only after it lands");
            var anchor = TrustAnchorProblems(git, report.Landing!);
            Assert.True(anchor.Count == 0, string.Join(Environment.NewLine, anchor));
        }
        Assert.True(report.Verdict == CandidateInvalidation.NotInvalidated, report.ToString());
        if (string.IsNullOrWhiteSpace(requested))
            Assert.Contains(report.Commits.SelectMany(c => c.Decisions), d => d.Path == V025.ManifestPath);
    }

    /// <summary>The maintainer decision in the R0 scope packet that supersedes this candidate (its <c>supersedes</c>), or null.</summary>
    private static string? SupersedingDecision(string candidate) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), ScopeDir, "scope.json")))!["amendments"]!.AsArray()
            .SelectMany(a => a!["maintainerDecisions"]?.AsArray() ?? [])
            .Where(d => Str(d!["supersedes"]) == candidate).Select(d => Str(d!["id"])).FirstOrDefault();

    /// <summary>
    /// Trust anchor: an explicit target is judged only by the reviewed classifier, so the running checkout must be the
    /// landing commit itself (every compiled input, including linked shared sources and MSBuild files) with no
    /// modified tracked file and no untracked file outside ignored build output.
    /// </summary>
    internal static List<string> TrustAnchorProblems(GitRepo git, string landing)
    {
        var problems = new List<string>();
        if (git.RevParse("HEAD") != landing) problems.Add($"run this from a checkout of the landing commit {landing}; HEAD is {git.RevParse("HEAD")}");
        var (exit, dirty, _) = git.Run("status", "--porcelain", "--untracked-files=all");
        if (exit != 0 || dirty.Trim() != "") problems.Add($"the landing checkout is not clean: {dirty.Trim()}");
        // An ignored file is invisible to status but still compiled by the SDK's default globs (or imported by MSBuild).
        // Build output (bin/, obj/) is regenerated; the ignored Z3 assets are binaries checked by the download script's pins.
        // -z (#1580): NUL-separated raw paths, so git never quotes a non-ASCII name and hides its extension.
        var (ignoredExit, ignored, _) = git.Run("ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--", "src", "tests", "tools",
            ":(glob)*", ":(exclude,glob)**/bin/**", ":(exclude,glob)**/obj/**");
        var inputs = ignored.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(f => CompileInputExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)) || Path.GetFileName(f).StartsWith("Directory.Build.", StringComparison.Ordinal))
            .ToList();
        if (ignoredExit != 0 || inputs.Count > 0) problems.Add($"the landing checkout has ignored build inputs: {string.Join(", ", inputs)}");
        return problems;
    }

    private static readonly string[] CompileInputExtensions =
        [".cs", ".csproj", ".props", ".targets", ".rsp", ".editorconfig", ".globalconfig", ".resx", ".runsettings"];

    /// <summary>An ignored C# file (which the SDK would compile into the test assembly) is refused, including one whose
    /// non-ASCII name git would print quoted without -z (#1580).</summary>
    [Theory]
    [InlineData("Injected.cs")]
    [InlineData("\u00e9.cs")]
    public void TheTrustAnchorRefusesAnIgnoredSourceFile(string name)
    {
        var dir = Directory.CreateTempSubdirectory("calor-v025-anchor-").FullName;
        try
        {
            var git = new GitRepo(dir);
            File.WriteAllText(Path.Combine(dir, ".gitignore"), "tmp/\n");
            Assert.Equal(0, git.Run("init", "-q").ExitCode);
            Assert.Equal(0, git.Run("add", ".gitignore").ExitCode);
            Assert.Equal(0, git.Run("-c", "user.name=t", "-c", "user.email=t@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "root").ExitCode);
            Directory.CreateDirectory(Path.Combine(dir, "tests", "tmp"));
            File.WriteAllText(Path.Combine(dir, "tests", "tmp", name), "// injected\n");
            var problems = TrustAnchorProblems(git, git.RevParse("HEAD")!);
            Assert.Equal(new[] { $"the landing checkout has ignored build inputs: tests/tmp/{name}".Normalize() }, problems.Select(p => p.Normalize()));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A checkout other than the landing (here the candidate stands in for any non-landing commit) is refused.</summary>
    [Fact]
    public void TheTrustAnchorRefusesAnotherCheckout()
    {
        var git = Git();
        var problems = TrustAnchorProblems(git, Str(Manifest()["candidate"]!["commit"]));
        Assert.Contains(problems, p => p.StartsWith("run this from a checkout of the landing commit", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("candidate.commit", "0000000000000000000000000000000000000001", "durable")]
    [InlineData("candidate.durableIdentity.treeHashes.src", "0000000000000000000000000000000000000002", "durable")]
    [InlineData("releasability.versionAtCandidate", "0.25.1", "version")]
    [InlineData("dependencies.submodules.0.commit", "0000000000000000000000000000000000000003", "submodule")]
    [InlineData("dependencies.lockfiles.src/Calor.Compiler/packages.lock.json", "00", "lockfile")]
    [InlineData("toolchain.z3.assets.0.sha256", "00", "Z3")]
    [InlineData("packets.scope.sha256", "00", "scope packet")]
    [InlineData("packets.seals.d1Contract.computed", "00", "D1 seal")]
    [InlineData("packets.t1Register.sha256", "00", "T1 register")]
    [InlineData("packets.w1Records.sha256", "00", "W1 records")]
    [InlineData("producers.0.files.docs/plans/evidence/v0.25-r0-1426/reproduce/reproduce.py", "00", "producer")]
    [InlineData("s2Witnesses.0.regressionWitness.0", "tests/NoSuchWitness.cs", "S2 witness")]
    [InlineData("merged.0.prs.0.mergeCommit", "0000000000000000000000000000000000000004", "merge")]
    [InlineData("issueStatesAtFreeze.releaseGaps.liveCheck.output.sha256", "00", "live check")]
    [InlineData("merged.6.prs.0.pr", "1512", "PR")] // the reviewed table and the merge check both catch a substituted PR
    [InlineData("merged.6", "<remove>", "gate")]
    [InlineData("merged.6", "<copy:8>", "gate")]
    [InlineData("toolchain.dotnetSdkBuild.evidence", "<only:2>", "fingerprint")]
    [InlineData("packets.t1Register.readiness.F1", "READY", "T1 register")]
    [InlineData("packets.t1Register.approvalSeal", "00", "T1 register")]
    [InlineData("packets.t1Register.baselineApprovalSeal", "00", "T1 register")]
    [InlineData("toolchain.releaseSdk", "10.0.402", "SDK")] // the job pins and the fingerprint evidence both name 10.0.401
    [InlineData("toolchain.releaseWorkflowPins.publish-nuget.yml:publish.0", "10.0.402", "toolchain")]
    [InlineData("toolchain.runtime", "10.0.13", "toolchain")]
    [InlineData("toolchain.dotnetSdkBuild.value", "sha256:0000000000000000000000000000000000000000000000000000000000000000", "fingerprint")]
    [InlineData("toolchain.dotnetSdkBuild.evidence.0.sha256", "00", "fingerprint")]
    [InlineData("packets.seals.scope.computed", "00", "scope seal")]
    [InlineData("packets.seals.scope.pinned", "00", "scope seal")]
    [InlineData("packets.seals.t1Baseline.pinned", "00", "T1 seal")]
    [InlineData("packets.scope.scopeVersion", "1.3.0", "scope packet")]
    [InlineData("packets.scope.readiness.F1", "READY", "scope packet")]
    [InlineData("packets.t1Register.registerVersion", "1.0.1", "T1 register")]
    [InlineData("packets.d1Contract.contractVersion", "1.0.5", "D1 contract")]
    [InlineData("packets.w1Records.scopeVersion", "1.3.0", "W1 records")]
    [InlineData("dependencies.lockfiles.src/Calor.Compiler/packages.lock.json", "<remove>", "lockfile")]
    [InlineData("dependencies.lockfiles", "<clear>", "lockfile")]
    [InlineData("dependencies.node.website/package-lock.json", "<remove>", "lockfile")]
    [InlineData("producers.2", "<remove>", "producer")]
    [InlineData("producers.0.files.docs/plans/evidence/v0.25-r0-1426/reproduce/reproduce.py", "<remove>", "producer")]
    [InlineData("toolchain.globalJson.version", "10.0.101", "toolchain")]
    [InlineData("toolchain.determinismProtocol.version", "1.5.0", "toolchain")]
    [InlineData("toolchain.seeds.0.value", "7", "toolchain")]
    [InlineData("toolchain.z3.upstreamArchives.0.sha256", "00", "Z3")]
    [InlineData("toolchain.releaseWorkflowPins.publish-nuget.yml:publish", "<rename:publish-nuget.yml:no-such-job>", "toolchain")]
    [InlineData("issueStatesAtFreeze.releaseGaps.closures.0.closedAtUtc|issueStatesAtFreeze.states.1545.closedAtUtc", "2099-01-01T00:00:00Z", "issue states")]
    [InlineData("issueStatesAtFreeze.states.1578.state", "CLOSED", "issue states")]
    [InlineData("maintainerDecisions.0.decision", "Accept and freeze", "decision")]
    [InlineData("maintainerDecisions.0.record", "docs/plans/evidence/v0.25-candidate/reviews/no-such-record.md", "decision")]
    [InlineData("issueStatesAtFreeze.releaseGaps.liveCheck.exitCode", "1", "live check")]
    [InlineData("issueStatesAtFreeze.releaseGaps.liveCheck.treeAtCommit", "0000000000000000000000000000000000000005", "live check")]
    [InlineData("issueStatesAtFreeze.releaseGaps.liveCheck.attestation", "none", "live check")]
    public void ATamperedFieldIsFound(string pointer, string value, string expected)
    {
        var manifest = Manifest();
        Set(manifest, pointer, value);
        var findings = Verify(Git(), manifest);
        Assert.True(findings.Count >= 1 && findings.All(f => f.Contains(expected, StringComparison.Ordinal)),
            $"expected only '{expected}' findings, got:{Environment.NewLine}{string.Join(Environment.NewLine, findings)}");
    }

    // ---- Verifier ----

    internal static List<string> Verify(GitRepo git, JsonNode manifest)
    {
        var findings = new List<string>();
        void Fail(string message) => findings.Add(message);
        var candidate = Str(manifest["candidate"]!["commit"]);

        // P1: the durable identity through DurableProvenance.Verify.
        var identity = manifest["candidate"]!["durableIdentity"]!;
        var entry = new JsonObject
        {
            ["path"] = V025.ManifestPath, ["measuredCommit"] = candidate, ["basis"] = DurableProvenance.BasisIdenticalSrcTree,
            ["measuredTreeHashes"] = new JsonObject { ["src"] = identity["treeHashes"]!["src"]!.DeepClone() },
            ["durableIdentity"] = identity.DeepClone(),
        };
        using (var doc = JsonDocument.Parse(entry.ToJsonString()))
            foreach (var f in DurableProvenance.Verify(git, new[] { doc.RootElement.Clone() }).Findings)
                Fail($"durable identity: {f}");
        if (Str(identity["commit"]) != candidate) Fail("durable identity: commit differs from candidate.commit");
        if (!git.CommitExists(candidate)) return findings;
        byte[]? Blob(string path) => git.ReadBlob($"{candidate}:{path}");
        string? Sha(string path, bool lf = false) => Blob(path) is { } b ? DurableProvenance.Sha256(lf ? Lf(b) : b) : null;

        var subject = git.Run("log", "-1", "--format=%s", candidate).Output.Trim();
        if (!subject.StartsWith($"Merge pull request #{manifest["candidate"]!["mergeOf"]!["pr"]} from ", StringComparison.Ordinal))
            Fail("merge: the candidate is not GitHub's merge of its PR");
        var props = Encoding.UTF8.GetString(Blob("Directory.Build.props") ?? []);
        if (!props.Contains($"<Version>{Str(manifest["releasability"]!["versionAtCandidate"])}</Version>", StringComparison.Ordinal)
            || Str(manifest["releasability"]!["versionAtCandidate"]) != Str(manifest["release"]))
            Fail("version: the candidate does not declare the recorded release version");

        var (_, tree, _) = git.Run("ls-tree", "-r", candidate);
        var gitlinks = tree.Split('\n').Select(l => l.Split(new[] { ' ', '\t' }, 4)).Where(p => p.Length == 4 && p[1] == "commit")
            .Select(p => $"{p[3]} {p[2]}").ToHashSet();
        var recorded = manifest["dependencies"]!["submodules"]!.AsArray().Select(s => $"{Str(s!["path"])} {Str(s["commit"])}").ToHashSet();
        if (gitlinks.Count != 3 || !gitlinks.SetEquals(recorded)) Fail("submodule inventory differs from the candidate's gitlinks");
        var allPaths = git.Run("ls-tree", "-r", "--name-only", candidate).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lockPaths = allPaths.Where(f => f.EndsWith("packages.lock.json", StringComparison.Ordinal) || f.EndsWith("Directory.Packages.props", StringComparison.Ordinal)
            || f.EndsWith("dotnet-tools.json", StringComparison.Ordinal) || f.ToLowerInvariant().EndsWith("nuget.config", StringComparison.Ordinal)).ToHashSet();
        foreach (var (group, required) in new[] { ("lockfiles", lockPaths), ("node", new HashSet<string> { "website/package.json", "website/package-lock.json" }) })
        {
            var recordedLocks = manifest["dependencies"]![group]!.AsObject();
            if (required.Count == 0 || !recordedLocks.Select(kv => kv.Key).ToHashSet().SetEquals(required))
                Fail($"lockfile inventory ({group}) differs from the candidate's lockfiles");
            foreach (var (path, want) in recordedLocks)
                if (Sha(path) != Str(want)) Fail($"lockfile {path} differs");
        }
        var producers = manifest["producers"]!.AsArray();
        if (!producers.Select(p => Str(p!["id"])).SequenceEqual(Producers.Select(p => p.Id)))
            Fail("producer: the producer set differs from the reviewed table");
        foreach (var p in producers)
        {
            var reviewed = Producers.FirstOrDefault(r => r.Id == Str(p!["id"]));
            var required = (reviewed.Directory is { } d ? allPaths.Where(f => f.StartsWith(d, StringComparison.Ordinal)) : Enumerable.Empty<string>()).Concat(reviewed.Extra ?? []).ToHashSet();
            if (reviewed.Id == null || required.Count == 0 || !p!["files"]!.AsObject().Select(kv => kv.Key).ToHashSet().SetEquals(required))
                Fail($"producer {Str(p!["id"])}: its file set differs from the candidate");
            foreach (var (path, want) in p!["files"]!.AsObject())
                if (Sha(path) != Str(want)) Fail($"producer {Str(p["id"])}: {path} differs");
        }
        var pins = Encoding.UTF8.GetString(Blob(".github/z3-binaries-4.15.7.sha256") ?? []).Split('\n')
            .Where(l => !l.StartsWith('#')).Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 3).Select(p => $"{p[1]} {p[0]} {p[2]}").ToHashSet();
        var assets = manifest["toolchain"]!["z3"]!["assets"]!.AsArray().Select(a => $"{Str(a!["name"])} {Str(a["sha256"])} {a["bytes"]}").ToHashSet();
        if (pins.Count == 0 || !pins.SetEquals(assets)) Fail("Z3 pins differ from .github/z3-binaries-4.15.7.sha256 at the candidate");
        var upstream = Encoding.UTF8.GetString(Blob("src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256") ?? []).Split('\n')
            .Where(l => !l.StartsWith('#')).Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 3).Select(p => $"{p[1]} {p[0]} {p[2]}").ToHashSet();
        var z3 = manifest["toolchain"]!["z3"]!;
        if (upstream.Count == 0 || !upstream.SetEquals(z3["upstreamArchives"]!.AsArray().Select(a => $"{Str(a!["name"])} {Str(a["sha256"])} {a["bytes"]}"))
            || Str(z3["version"]) != Str(JsonNode.Parse(Blob("eng/z3-consumers.json")!)!["z3Version"]))
            Fail("Z3 upstream archives or version differ from the candidate");

        // The packets at the candidate, and the 0.25 validators on the candidate's own blobs.
        var packets = manifest["packets"]!;
        foreach (var (key, label) in new[] { ("scope", "scope packet"), ("t1Register", "T1 register"), ("d1Contract", "D1 contract"), ("w1Records", "W1 records") })
            if (Sha(Str(packets[key]!["path"])) != Str(packets[key]!["sha256"])) Fail($"{label}: file hash differs");
        var scope = JsonNode.Parse(Blob($"{ScopeDir}/scope.json")!)!;
        List<string> Files(string sub) => git.Run("ls-tree", "-r", "--name-only", candidate, "--", $"{ScopeDir}/{sub}/").Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var results = JsonNode.Parse(Blob($"{ScopeDir}/baseline-results.json")!)!;
        foreach (var v in InteropScopeValidator.Validate(scope, results, p => Blob(p), Files("fixtures"), Files("generated")))
            Fail($"scope packet: {v.Code} {v.Subject}: {v.Message}");
        var register = JsonNode.Parse(Blob(Str(packets["t1Register"]!["path"]))!)!;
        foreach (var v in T1RegisterValidator.Validate(register, scope, p => Blob(p)))
            Fail($"T1 register: {v.Code} {v.Subject}: {v.Message}");
        if (T1RegisterValidator.ApprovalSeal(register) != Str(packets["seals"]!["t1Amendments"]!["recorded"]!.AsArray()[^1]![1]))
            Fail("T1 register: the approval seal differs from the recorded one");
        var d1 = JsonNode.Parse(Blob(Str(packets["d1Contract"]!["path"]))!)!;
        foreach (var v in RefOutContractValidator.Validate(d1, scope, p => Blob(p)))
            Fail($"D1 contract: {v.Code} {v.Subject}: {v.Message}");
        if (RefOutContractValidator.Seal(d1) != Str(packets["seals"]!["d1Contract"]!["computed"])) Fail("D1 seal differs from the recorded seal");
        var w1 = JsonNode.Parse(Blob(Str(packets["w1Records"]!["path"]))!)!;
        if (Str(w1["scopeVersion"]) != Str(scope["scopeVersion"])) Fail("W1 records: consumed scope version differs");

        // Packet summaries against the candidate's files.
        var sp = packets["scope"]!;
        if (Str(sp["scopeVersion"]) != Str(scope["scopeVersion"]) || Str(sp["status"]) != Str(scope["status"]) || Str(sp["gateStatus"]) != Str(scope["gateStatus"])
            || !JsonNode.DeepEquals(sp["evidenceSeal"], scope["evidenceSeal"]) || !JsonNode.DeepEquals(sp["acceptancePreconditions"], scope["acceptancePreconditions"])
            || !scope["families"]!.AsArray().Select(f => $"{f!["id"]}={f["readiness"]!["status"]}").SequenceEqual(sp["readiness"]!.AsObject().Select(kv => $"{kv.Key}={Str(kv.Value)}"))
            || !scope["amendments"]!.AsArray().Select(a => $"{a!["version"]}|{a["dateUtc"]}|{a["pr"]}|{a["afterInspection"]}")
                .SequenceEqual(sp["amendments"]!.AsArray().Select(a => $"{a!["version"]}|{a["dateUtc"]}|{a["pr"]}|{a["afterInspection"]}")))
            Fail("scope packet: a recorded summary field differs from scope.json");
        var t1 = packets["t1Register"]!;
        if (Str(t1["registerVersion"]) != Str(register["registerVersion"]) || Str(t1["approvalSeal"]) != T1RegisterValidator.ApprovalSeal(register)
            || Str(t1["baselineApprovalSeal"]) != Str(register["approvalSeal"])
            || !register["families"]!.AsArray().Select(f => $"{f!["id"]}={f["readiness"]}").SequenceEqual(t1["readiness"]!.AsObject().Select(kv => $"{kv.Key}={Str(kv.Value)}")))
            Fail("T1 register: a recorded summary field differs (registerVersion, readiness or approval seals)");
        if (Str(packets["d1Contract"]!["contractVersion"]) != Str(d1["contractVersion"]) || Str(packets["d1Contract"]!["status"]) != Str(d1["status"])
            || !JsonNode.DeepEquals(packets["d1Contract"]!["consumes"], d1["consumes"]))
            Fail("D1 contract: a recorded summary field differs");
        if (Str(packets["w1Records"]!["scopeVersion"]) != Str(w1["scopeVersion"]) || Str(packets["w1Records"]!["registerVersion"]) != Str(w1["registerVersion"]))
            Fail("W1 records: a recorded summary field differs");
        var seals = packets["seals"]!;
        string Pin(string test, string name) => System.Text.RegularExpressions.Regex.Match(Encoding.UTF8.GetString(Blob(test) ?? []), name + @"\s*=\s*""([0-9a-f]{64})""").Groups[1].Value;
        var scopeSeal = DurableProvenance.Sha256(Encoding.UTF8.GetBytes(scope["denominatorV1"]!.ToJsonString() + "\n" + Str(scope["evidenceSeal"]!["baselineResults"])
            + "\n" + Str(scope["evidenceSeal"]!["generated"]) + "\n" + scope["evidenceSeal"]!["producers"]!.ToJsonString()));
        var scopePin = Pin("tests/Calor.Compiler.Tests/InteropScope/InteropScopeTests.cs", "FrozenSeal");
        if (Str(seals["scope"]!["computed"]) != scopeSeal || Str(seals["scope"]!["pinned"]) != scopePin || scopeSeal != scopePin)
            Fail("scope seal: computed or pinned value differs from the candidate");
        if (Str(seals["d1Contract"]!["pinned"]) != Pin("tests/Calor.Compiler.Tests/InteropScope/RefOutContractTests.cs", "FrozenSeal")
            || Str(seals["d1Contract"]!["pinned"]) != Str(seals["d1Contract"]!["computed"]))
            Fail("D1 seal: the pinned value differs from the candidate's test");
        var t1Amend = string.Join(";", register["amendments"]!.AsArray().Select(a => $"{a!["version"]},{a["approvalSeal"]}"));
        var t1Pins = string.Join(";", System.Text.RegularExpressions.Regex.Matches(Encoding.UTF8.GetString(Blob("tests/Calor.Compiler.Tests/InteropScope/T1RegisterTests.cs") ?? []),
            @"\(""([0-9.]+)"",\s*""([0-9a-f]{64})""\)").Select(m => $"{m.Groups[1].Value},{m.Groups[2].Value}"));
        string Pairs(JsonNode? n) => string.Join(";", n!.AsArray().Select(x => $"{Str(x![0])},{Str(x[1])}"));
        if (Str(seals["t1Baseline"]!["recorded"]) != Str(register["approvalSeal"])
            || Str(seals["t1Baseline"]!["pinned"]) != Pin("tests/Calor.Compiler.Tests/InteropScope/T1RegisterTests.cs", "BaselineApprovalSeal")
            || Pairs(seals["t1Amendments"]!["recorded"]) != t1Amend || Pairs(seals["t1Amendments"]!["pinned"]) != t1Pins)
            Fail("T1 seal: recorded or pinned approval seals differ from the candidate");

        // Toolchain: the release SDK as every release-workflow job installs it, the runtime pin, and the SDK build fingerprint
        // from the retained reproducible-builds evidence.
        var tc = manifest["toolchain"]!;
        var recordedPins = tc["releaseWorkflowPins"]!.AsObject();
        var releaseWorkflows = new[] { "publish-nuget.yml", "verify-release.yml", "reproducible-builds.yml", "benchmark.yml" };
        foreach (var wf in releaseWorkflows)
        {
            var text = Encoding.UTF8.GetString(Blob($".github/workflows/{wf}") ?? []);
            var parts = System.Text.RegularExpressions.Regex.Split(text.Split("\njobs:", 2)[^1], @"(?m)^  ([A-Za-z0-9_-]+):[ \t]*$");
            var actual = Enumerable.Range(0, (parts.Length - 1) / 2)
                .Select(i => (Job: $"{wf}:{parts[1 + 2 * i]}", Versions: System.Text.RegularExpressions.Regex.Matches(parts[2 + 2 * i], @"dotnet-version:[ \t]*(\S+)")
                    .Select(m => m.Groups[1].Value.Trim('\'', '"')).ToList()))
                .Where(j => j.Versions.Count > 0).Select(j => $"{j.Job}={string.Join(",", j.Versions)}").ToList();
            var recordedForWf = recordedPins.Where(kv => kv.Key.StartsWith(wf + ":", StringComparison.Ordinal))
                .Select(kv => $"{kv.Key}={string.Join(",", kv.Value!.AsArray().Select(Str))}").ToList();
            if (actual.Count == 0 || !actual.SequenceEqual(recordedForWf)) Fail($"toolchain: {wf} SDK pins differ from the recorded jobs");
        }
        if (recordedPins.Any(kv => !releaseWorkflows.Any(wf => kv.Key.StartsWith(wf + ":", StringComparison.Ordinal))))
            Fail("toolchain: a recorded job is not in a release workflow");
        if (!JsonNode.DeepEquals(tc["globalJson"], JsonNode.Parse(Blob("global.json")!)!["sdk"])) Fail("toolchain: globalJson differs from the candidate's global.json");
        if (Str(tc["releaseSdk"]) != "10.0.401" || recordedPins.Any(kv => Str(kv.Value![0]) != Str(tc["releaseSdk"])))
            Fail("toolchain: the release SDK is not the first install of every release job");
        var protocol = JsonNode.Parse(Blob("docs/plans/evidence/g2-1421/protocol.json")!)!;
        if (!Str(tc["runtime"]).StartsWith(Str(protocol["toolchain"]!["runtime"]) + " ", StringComparison.Ordinal)) Fail("toolchain: runtime differs from the G2 pin");
        var dp = tc["determinismProtocol"]!;
        if (Str(dp["version"]) != Str(protocol["protocolVersion"]) || dp.AsObject().Count != 8
            || new[] { "environments", "environmentRule", "checkout" }.Any(k => !JsonNode.DeepEquals(dp[k], protocol[k]))
            || new[] { "sdk", "runtime", "globalJson", "configuration" }.Any(k => !JsonNode.DeepEquals(dp[k], protocol["toolchain"]![k])))
            Fail("toolchain: determinismProtocol differs from the candidate's G2 protocol");
        var seeds = tc["seeds"]!.AsArray();
        var seed = protocol["z3"]!["randomSeed"]!;
        if (seeds.Count != 2 || Str(seeds[0]!["id"]) != "z3-random-seed" || new[] { "value", "source", "applied" }.Any(k => !JsonNode.DeepEquals(seeds[0]![k], seed[k]))
            || Str(seeds[1]!["id"]) != "0.25-producers" || seeds[1]!["value"] != null)
            Fail("toolchain: seeds differ from the candidate's G2 protocol");
        var fingerprint = tc["dotnetSdkBuild"]!;
        var evidence = fingerprint["evidence"]!.AsArray();
        var hashFiles = evidence.Select(e => Str(e!["path"])).Where(p => p.EndsWith(".json", StringComparison.Ordinal)).ToHashSet();
        if (!hashFiles.SetEquals(new[] { "docs/plans/evidence/v0.25-candidate/sdk-fingerprint/packages-1.json", "docs/plans/evidence/v0.25-candidate/sdk-fingerprint/packages-2.json" }))
            Fail("fingerprint: the evidence must retain both reproducible-builds hash files (packages-1.json and packages-2.json)");
        foreach (var e in evidence)
        {
            var path = Path.Combine(RepoRoot(), Str(e!["path"]));
            if (!File.Exists(path) || DurableProvenance.Sha256(File.ReadAllBytes(path)) != Str(e["sha256"])) { Fail($"fingerprint: {Str(e["path"])} is missing or differs"); continue; }
            if (!path.EndsWith(".json", StringComparison.Ordinal)) continue;
            var hashes = JsonNode.Parse(File.ReadAllText(path))!["toolchain"];
            if (Str(hashes?["dotnetSdkBuild"]) != Str(fingerprint["value"]) || Str(hashes?["dotnet"]) != Str(tc["releaseSdk"]) || Str(hashes?["os"]) != Str(fingerprint["os"]))
                Fail($"fingerprint: {Str(e["path"])} records another SDK build");
        }

        // 0.24 S2 repairs: the dispositions' witnesses, present at the candidate.
        var s2 = JsonNode.Parse(Blob("docs/plans/evidence/s2-1413/dispositions.json")!)!;
        var repairs = s2["repairs"]!.AsArray().Select(r => $"{r!["id"]} {r["pr"]} {r["mergeCommit"]} {string.Join(",", r["regressionWitness"]!.AsArray().Select(Str))}");
        var witnesses = manifest["s2Witnesses"]!.AsArray();
        if (!repairs.SequenceEqual(witnesses.Select(r => $"{r!["id"]} {r["pr"]} {r["mergeCommit"]} {string.Join(",", r["regressionWitness"]!.AsArray().Select(Str))}"))
            || witnesses.Any(w => w!["regressionWitness"]!.AsArray().Any(p => Blob(Str(p)) == null)))
            Fail("S2 witness list differs from dispositions.json or a witness is missing");

        // The gate set and each gate's PRs equal the table reviewed with this freeze; every recorded PR is GitHub's
        // merge of that PR, inside the candidate.
        var gates = manifest["merged"]!.AsArray();
        var gateIds = gates.Select(g => Str(g!["gate"])).ToList();
        if (gates.Count != GatePrs.Count || gateIds.Distinct().Count() != gateIds.Count || !gateIds.ToHashSet().SetEquals(GatePrs.Keys)
            || gates.Any(g => !GatePrs.TryGetValue(Str(g!["gate"]), out var want)
                || want.Issue.ToString() != g["issue"]!.ToString() || !want.Prs.Select(x => x.ToString()).SequenceEqual(g["prs"]!.AsArray().Select(x => x!["pr"]!.ToString()))))
            Fail("gate: the gate set or a gate's PRs differ from the reviewed table");
        foreach (var gate in gates)
            foreach (var pr in gate!["prs"]!.AsArray())
            {
                var merge = Str(pr!["mergeCommit"]);
                var ok = git.CommitExists(merge) && git.IsAncestor(merge, candidate)
                    && git.Run("log", "-1", "--format=%s", merge).Output.StartsWith($"Merge pull request #{pr["pr"]} from ", StringComparison.Ordinal);
                if (!ok) Fail($"merge: {gate["gate"]} PR #{pr["pr"]} is not GitHub's merge inside the candidate");
            }

        // The release-gap closures and the live check output retained beside the manifest.
        var gaps = manifest["issueStatesAtFreeze"]!["releaseGaps"]!;
        var states = manifest["issueStatesAtFreeze"]!["states"]!.AsObject();
        var freezeUtc = Str(manifest["candidate"]!["freezeObservedUtc"]);
        if (!states.Select(kv => $"{kv.Key}={Str(kv.Value!["state"])}|{Str(kv.Value["stateReason"])}|{Str(kv.Value["closedAtUtc"])}").SequenceEqual(IssueStates.Select(kv => $"{kv.Key}={kv.Value}")))
            Fail("issue states: the recorded states differ from the reviewed snapshot");
        var closures = gaps["closures"]!.AsArray();
        if (!closures.Select(c => $"{c!["issue"]}|{Str(c["stateReason"])}|{Str(c["closedAtUtc"])}").SequenceEqual(new[] { 1545, 1546, 1547 }.Select(i => $"{i}|{IssueStates[i.ToString()][7..]}"))
            || closures.Any(c => string.CompareOrdinal(Str(c!["closedAtUtc"]), freezeUtc) >= 0) || gaps["met"]?.GetValue<bool>() != true)
            Fail("issue states: the release-gap closures differ from the reviewed snapshot or follow the freeze");
        var live = gaps["liveCheck"]!;
        const string Attestation = "docs/plans/evidence/release-gaps-0.25/actions-workflow-permissions-attestation.json";
        var output = live["output"]!;
        var file = Path.Combine(RepoRoot(), Str(output["path"]));
        var liveText = File.Exists(file) ? File.ReadAllText(file) : "";
        if (!File.Exists(file) || DurableProvenance.Sha256(File.ReadAllBytes(file)) != Str(output["sha256"])
            || !liveText.Contains("RELEASE CREDENTIALS: CLOSED", StringComparison.Ordinal) || !liveText.TrimEnd().EndsWith(Str(live["verdict"]), StringComparison.Ordinal)
            || !liveText.Contains($"A001: Actions workflow permissions ATTESTED", StringComparison.Ordinal) || !liveText.Contains(Attestation, StringComparison.Ordinal))
            Fail("live check: the retained output is missing, differs, is not CLOSED, or does not carry the A001 attestation");
        if (Str(live["treeAtCommit"]) != candidate || live["exitCode"]?.ToString() != "0" || Str(live["ranAtUtc"]) != freezeUtc
            || Str(live["command"]) != $"python3 scripts/check_release_credentials.py --live --attest-workflow-permissions {Attestation}"
            || !Str(live["attestation"]).StartsWith("A001: Actions workflow permissions ATTESTED", StringComparison.Ordinal) || !Str(live["attestation"]).Contains(Attestation, StringComparison.Ordinal)
            || Blob(Attestation) == null)
            Fail("live check: the recorded command, tree, exit code, time or attestation differs");
        var accept = manifest["maintainerDecisions"]!.AsArray().Where(d => Str(d!["id"]) == "D-ACCEPT-FILE-FREEZE").ToList();
        if (accept.Count != 1 || Str(accept[0]!["decision"]) != "Accept, file issue, freeze" || Str(accept[0]!["by"]) != "@juanmicrosoft"
            || Str(accept[0]!["date"]) != "2026-10-19" || accept[0]!["issue"]?.ToString() != "1578"
            || Str(accept[0]!["record"]) != "docs/plans/evidence/v0.25-candidate/reviews/prefreeze-exception-codex.md" || Blob(Str(accept[0]!["record"])) == null)
            Fail("decision: D-ACCEPT-FILE-FREEZE differs from the maintainer's decision or its review record is missing at the candidate");
        return findings;
    }

    private static byte[] Lf(byte[] bytes) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));

    /// <summary>The gates and merged PRs reviewed with this freeze (the 0.25 form of the 0.24 C1 child table).</summary>
    private static readonly Dictionary<string, (int Issue, int[] Prs)> GatePrs = new()
    {
        ["R0"] = (1426, [1483, 1552, 1553]), ["D1"] = (1427, [1518, 1559]), ["F1"] = (943, [1558, 1571]), ["F2"] = (906, [1522]),
        ["F3"] = (847, [1519, 1531]), ["F4"] = (1139, [1510]), ["F5"] = (1132, [1554]), ["F6"] = (1144, [1513]), ["W0"] = (1143, [1512]),
        ["T1"] = (1428, [1562]), ["W1"] = (1429, [1566]), ["R2-1545"] = (1545, [1565, 1577]), ["R2-1546"] = (1546, [1563]),
        ["R2-1547"] = (1547, [1563]), ["PREP"] = (1430, [1568, 1569, 1575, 1576]),
    };

    /// <summary>Issue states observed at the freeze (gh issue view at 2026-10-19T18:23:52Z), reviewed with this freeze.</summary>
    private static readonly Dictionary<string, string> IssueStates = new()
    {
        ["1425"] = "OPEN||", ["1426"] = "OPEN||", ["1427"] = "OPEN||", ["1428"] = "OPEN||", ["1429"] = "OPEN||", ["1430"] = "OPEN||",
        ["943"] = "OPEN||", ["906"] = "OPEN||", ["847"] = "OPEN||", ["1139"] = "OPEN||", ["1132"] = "OPEN||", ["1144"] = "OPEN||",
        ["1143"] = "OPEN||", ["1545"] = "CLOSED|COMPLETED|2026-10-19T15:23:15Z", ["1546"] = "CLOSED|COMPLETED|2026-10-19T15:23:17Z",
        ["1547"] = "CLOSED|COMPLETED|2026-10-19T15:23:19Z", ["1548"] = "OPEN||", ["1573"] = "OPEN||", ["1578"] = "OPEN||",
    };

    /// <summary>The evidence producers reviewed with this freeze: a directory whose files are all included, plus extra files.</summary>
    private static readonly (string Id, string? Directory, string[]? Extra)[] Producers =
    [
        ("r0-reproduce", "docs/plans/evidence/v0.25-r0-1426/reproduce/", null),
        ("family-reproduce", null, ["docs/plans/evidence/v0.25-f1-943/reproduce/reproduce.py", "docs/plans/evidence/v0.25-f3-847/reproduce/reproduce.py",
            "docs/plans/evidence/v0.25-f4-1139/reproduce/reproduce.py", "docs/plans/evidence/v0.25-f5-1132/reproduce/reproduce.py",
            "docs/plans/evidence/v0.25-f5-1132/reproduce/reproduce_pr2.py"]),
        ("t1-aggregate", "docs/plans/evidence/v0.25-t1-1428/reproduce/", null),
        ("w1-wording-scan", "docs/plans/evidence/v0.25-w1-1429/reproduce/", null),
        ("g2-determinism", null, ["scripts/determinism_protocol.py", "scripts/determinism_runner.py", "docs/plans/evidence/g2-1421/protocol.json",
            "docs/plans/evidence/g2-1421/cases.json"]),
        ("r2-release-gate", null, ["scripts/verify_release_adjudication.py", "scripts/render_release_notes.py", "scripts/verify_maintainer_override.py",
            "scripts/check_release_credentials.py", "scripts/test_reproducible_builds.py"]),
        ("candidate-generator", null, ["docs/plans/evidence/v0.25-candidate/generate_candidate_manifest.py"]),
    ];

    /// <summary>Sets one field; <c>a|b</c> sets several. Values: a literal, <c>&lt;remove&gt;</c>, <c>&lt;clear&gt;</c>,
    /// <c>&lt;copy:N&gt;</c>, <c>&lt;only:N&gt;</c>, or <c>&lt;rename:key&gt;</c>.</summary>
    private static void Set(JsonNode root, string pointer, string value)
    {
        if (pointer.Contains('|'))
        {
            foreach (var part in pointer.Split('|')) Set(root, part, value);
            return;
        }
        var node = root;
        var rest = pointer;
        while (true)
        {
            var key = node is JsonObject obj ? obj.Select(p => p.Key).Where(k => rest == k || rest.StartsWith(k + ".", StringComparison.Ordinal))
                .OrderByDescending(k => k.Length).First() : rest.Split('.')[0];
            rest = rest.Length > key.Length ? rest[(key.Length + 1)..] : "";
            JsonNode? Child() => node is JsonArray a ? a[int.Parse(key)] : node![key];
            if (rest == "")
            {
                if (value == "<remove>" && node is JsonArray removeFrom) removeFrom.RemoveAt(int.Parse(key));
                else if (value == "<remove>" && node is JsonObject removeKey) removeKey.Remove(key);
                else if (value == "<clear>" && Child() is JsonObject clearObject) clearObject.Clear();
                else if (value.StartsWith("<rename:", StringComparison.Ordinal) && node is JsonObject renameIn)
                {
                    var moved = renameIn[key]!.DeepClone();
                    var order = renameIn.Select(kv => kv.Key).ToList();
                    var copy = order.Select(k => (Key: k == key ? value[8..^1] : k, Value: k == key ? moved : renameIn[k]!.DeepClone())).ToList();
                    renameIn.Clear();
                    foreach (var (k, v) in copy) renameIn[k] = v;
                }
                else if (value.StartsWith("<copy:", StringComparison.Ordinal) && node is JsonArray copyIn)
                    copyIn[int.Parse(key)] = copyIn[int.Parse(value[6..^1])]!.DeepClone();
                else if (value.StartsWith("<only:", StringComparison.Ordinal) && Child() is JsonArray keepIn)
                {
                    var keep = keepIn[int.Parse(value[6..^1])]!.DeepClone();
                    keepIn.Clear();
                    keepIn.Add(keep);
                }
                else if (node is JsonArray arr) arr[int.Parse(key)] = JsonValue.Create(value);
                else node![key] = JsonValue.Create(value);
                return;
            }
            node = Child()!;
        }
    }

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node?.ToString() ?? "";

    private static JsonNode Manifest() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), V025.ManifestPath)))!;

    private static GitRepo Git() => new(RepoRoot());

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")) && !File.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
