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
        var target = string.IsNullOrWhiteSpace(requested) ? NewestCandidateCommit(git, candidate) : requested.Trim();
        Assert.True(target != null, "no commit after the candidate changes a candidate file; is history fetched?");
        var report = ClassifyRange(git, V025, candidate, target!);
        _output.WriteLine(report.ToString());
        if (!string.IsNullOrWhiteSpace(requested))
        {
            Assert.True(report.Landing != null, "the freeze has not landed on origin/main; judge later commits only after it lands");
            var (_, listed, _) = git.Run("ls-tree", "-r", "--name-only", report.Landing!, "--", V025.Directory);
            var trusted = listed.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith(V025.Reviews, StringComparison.Ordinal))
                .Concat(V025.Files).ToList();
            var drift = trusted.Where(p => git.RevParse("HEAD:" + p) != git.RevParse($"{report.Landing}:{p}")).ToList();
            Assert.True(drift.Count == 0, $"run this from a checkout of the landing commit {report.Landing}; differing at HEAD: {string.Join(", ", drift)}");
        }
        Assert.True(report.Verdict == CandidateInvalidation.NotInvalidated, report.ToString());
        if (string.IsNullOrWhiteSpace(requested))
            Assert.Contains(report.Commits.SelectMany(c => c.Decisions), d => d.Path == V025.ManifestPath);
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
        foreach (var group in new[] { "lockfiles", "node" })
            foreach (var (path, want) in manifest["dependencies"]![group]!.AsObject())
                if (Sha(path) != Str(want)) Fail($"lockfile {path} differs");
        foreach (var p in manifest["producers"]!.AsArray())
            foreach (var (path, want) in p!["files"]!.AsObject())
                if (Sha(path) != Str(want)) Fail($"producer {Str(p["id"])}: {path} differs");
        var pins = Encoding.UTF8.GetString(Blob(".github/z3-binaries-4.15.7.sha256") ?? []).Split('\n')
            .Where(l => !l.StartsWith('#')).Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 3).Select(p => $"{p[1]} {p[0]} {p[2]}").ToHashSet();
        var assets = manifest["toolchain"]!["z3"]!["assets"]!.AsArray().Select(a => $"{Str(a!["name"])} {Str(a["sha256"])} {a["bytes"]}").ToHashSet();
        if (pins.Count == 0 || !pins.SetEquals(assets)) Fail("Z3 pins differ from .github/z3-binaries-4.15.7.sha256 at the candidate");

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
        if (RefOutContractValidator.Seal(d1) != Str(packets["seals"]!["d1Contract"]!["computed"])) Fail("D1 seal differs from the recorded seal");
        var w1 = JsonNode.Parse(Blob(Str(packets["w1Records"]!["path"]))!)!;
        if (Str(w1["scopeVersion"]) != Str(scope["scopeVersion"])) Fail("W1 records: consumed scope version differs");

        // 0.24 S2 repairs: the dispositions' witnesses, present at the candidate.
        var s2 = JsonNode.Parse(Blob("docs/plans/evidence/s2-1413/dispositions.json")!)!;
        var repairs = s2["repairs"]!.AsArray().Select(r => $"{r!["id"]} {r["pr"]} {r["mergeCommit"]} {string.Join(",", r["regressionWitness"]!.AsArray().Select(Str))}");
        var witnesses = manifest["s2Witnesses"]!.AsArray();
        if (!repairs.SequenceEqual(witnesses.Select(r => $"{r!["id"]} {r["pr"]} {r["mergeCommit"]} {string.Join(",", r["regressionWitness"]!.AsArray().Select(Str))}"))
            || witnesses.Any(w => w!["regressionWitness"]!.AsArray().Any(p => Blob(Str(p)) == null)))
            Fail("S2 witness list differs from dispositions.json or a witness is missing");

        // Every recorded PR is GitHub's merge of that PR, inside the candidate.
        foreach (var gate in manifest["merged"]!.AsArray())
            foreach (var pr in gate!["prs"]!.AsArray())
            {
                var merge = Str(pr!["mergeCommit"]);
                var ok = git.CommitExists(merge) && git.IsAncestor(merge, candidate)
                    && git.Run("log", "-1", "--format=%s", merge).Output.StartsWith($"Merge pull request #{pr["pr"]} from ", StringComparison.Ordinal);
                if (!ok) Fail($"merge: {gate["gate"]} PR #{pr["pr"]} is not GitHub's merge inside the candidate");
            }

        // The release-gap closures and the live check output retained beside the manifest.
        var gaps = manifest["issueStatesAtFreeze"]!["releaseGaps"]!;
        var states = manifest["issueStatesAtFreeze"]!["states"]!;
        foreach (var c in gaps["closures"]!.AsArray())
            if (Str(states[c!["issue"]!.ToString()]?["state"]) != "CLOSED" || Str(states[c["issue"]!.ToString()]?["closedAtUtc"]) != Str(c["closedAtUtc"]))
                Fail($"live check: release gap #{c["issue"]} is not recorded closed");
        var output = gaps["liveCheck"]!["output"]!;
        var file = Path.Combine(RepoRoot(), Str(output["path"]));
        if (!File.Exists(file) || DurableProvenance.Sha256(File.ReadAllBytes(file)) != Str(output["sha256"])
            || !File.ReadAllText(file).Contains("RELEASE CREDENTIALS: CLOSED", StringComparison.Ordinal))
            Fail("live check: the retained output is missing, differs, or is not CLOSED");
        return findings;
    }

    private static byte[] Lf(byte[] bytes) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));

    private static string? NewestCandidateCommit(GitRepo git, string candidate)
    {
        var (_, log, _) = git.Run("rev-list", $"{candidate}..HEAD");
        foreach (var commit in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var (_, names, _) = git.Run("diff-tree", "--no-commit-id", "--name-only", "-r", "-z", "--root", commit);
            if (names.Split('\0').Any(p => (p.StartsWith(V025.Directory, StringComparison.Ordinal) && !p.StartsWith(V025.Reviews, StringComparison.Ordinal))
                    || V025.Files.Contains(p)))
                return commit;
        }
        return null;
    }

    private static void Set(JsonNode root, string pointer, string value)
    {
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
                if (node is JsonArray arr) arr[int.Parse(key)] = JsonValue.Create(value);
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
