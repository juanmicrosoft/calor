using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.Tests.Provenance;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// #1423 (0.24 C1): the committed manifest, re-verified from the candidate's git objects. Each tamper
/// control changes one recorded fact and expects only its finding.
/// </summary>
public class CandidateManifestTests
{
    [Fact]
    public void CommittedManifestVerifiesAgainstTheCandidate()
    {
        var findings = Verify(Git(), Manifest());
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void CandidateIsTheReleasePrepMergeAndDeclaresTheReleaseVersion()
    {
        // Second re-freeze (C1 PR 2): the candidate is the merge of prep PR #1533 and supersedes 696ab824.
        var manifest = Manifest();
        var candidate = Str(manifest["candidate"]!["commit"]);
        Assert.Matches("^[0-9a-f]{40}$", candidate);
        Assert.Contains(manifest["additionalMerged"]!.AsArray(), p => Str(p!["mergeCommit"]) == candidate && p["pr"]!.GetValue<int>() == 1533);
        Assert.Equal("696ab82470626164979a07792ed74932ab88d9e6", Str(manifest["supersedes"]!["commit"]));
        var props = System.Text.Encoding.UTF8.GetString(Git().ReadBlob($"{candidate}:Directory.Build.props")!);
        Assert.Contains($"<Version>{Str(manifest["releasability"]!["versionAtCandidate"])}</Version>", props);
        Assert.Equal("0.24.0", Str(manifest["releasability"]!["versionAtCandidate"]));
        Assert.Equal("CLOSED", Str(manifest["s2Closure"]!["closure"]!["status"]));
        Assert.Equal("SUCCESS", Str(manifest["s2Closure"]!["closure"]!["result"]));
        Assert.Equal(7, manifest["s2Closure"]!["repairs"]!.AsArray().Count);
    }

    /// <summary>
    /// The C1 pull request lands after the candidate. By default this classifies every commit up to
    /// the newest one that changes a C1 file: C1 must change only exempt files, and nothing else may
    /// merge between the freeze and C1. A later unrelated merge is not judged by default, so this
    /// test never blocks other work on main. C2 (#1424) and A1 (#1408) set
    /// <c>CALOR_C1_CLASSIFY_TARGET</c> to their own commit to classify <c>candidate..target</c>.
    /// </summary>
    [Fact]
    public void RangeFromTheCandidateIsNotInvalidated()
    {
        var git = Git();
        var candidate = Str(Manifest()["candidate"]!["commit"]);
        var requested = Environment.GetEnvironmentVariable(TargetVariable);
        var target = string.IsNullOrWhiteSpace(requested) ? NewestC1Commit(git, candidate) : requested.Trim();
        Assert.True(target != null, "no commit after the candidate changes a C1 file; is history fetched?");

        var report = CandidateInvalidation.ClassifyRange(git, candidate, target!);
        _output.WriteLine(report.ToString());

        // Trust anchor: an explicit target is judged only by the reviewed classifier, so the C1
        // files of the checkout running this test must be byte-identical to those at the landing.
        if (!string.IsNullOrWhiteSpace(requested))
        {
            Assert.True(report.Landing != null, "C1 has not landed on origin/main; judge later commits only after it lands");
            var (_, listed, _) = git.Run("ls-tree", "-r", "--name-only", report.Landing!, "--", CandidateInvalidation.C1Directory);
            var trusted = listed.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith(CandidateInvalidation.C1Reviews, StringComparison.Ordinal))
                .Concat(CandidateInvalidation.C1Files).ToList();
            var drift = trusted.Where(p => git.RevParse("HEAD:" + p) != git.RevParse($"{report.Landing}:{p}")).ToList();
            Assert.True(drift.Count == 0, $"run this from a checkout of the C1 landing commit {report.Landing} "
                + $"(git worktree add <dir> {report.Landing}); differing at HEAD: {string.Join(", ", drift)}");
        }

        Assert.True(report.Verdict == CandidateInvalidation.NotInvalidated, report.ToString());
        if (string.IsNullOrWhiteSpace(requested))
            Assert.Contains(report.Commits.SelectMany(c => c.Decisions), d => d.Path == CandidateInvalidation.ManifestPath);
    }

    private const string TargetVariable = "CALOR_C1_CLASSIFY_TARGET";
    private readonly ITestOutputHelper _output;

    public CandidateManifestTests(ITestOutputHelper output) => _output = output;

    // ---- Negative controls: one recorded fact changed, one finding expected ----

    [Theory]
    [InlineData("candidate.commit", "0000000000000000000000000000000000000001", "durable")]
    [InlineData("submodules.entries.0.commit", "0000000000000000000000000000000000000002", "submodule")]
    [InlineData("submodules.entries.0", "<remove>", "submodule")]
    [InlineData("dependencies.lockfiles.src/Calor.Compiler/packages.lock.json", "00", "lockfile")]
    [InlineData("contractPacket.sha256Json.sha256", "00", "contract packet")]
    [InlineData("contractPacket.amendmentLog.7.reviewedInPr", "1506", "amendment")]
    [InlineData("contractPacket.amendmentLog.2.afterDecisionBearingInspection", "true", "amendment")]
    [InlineData("contractPacket.amendmentLogSha256", "00", "amendment")]
    [InlineData("registeredManifests.packets.2.sha256", "00", "packet")]
    [InlineData("acceptedChildren.3.mergedPrs.0.mergeCommit", "0000000000000000000000000000000000000003", "child")]
    [InlineData("acceptedChildren.4", "<remove>", "child set")]
    [InlineData("acceptedChildren.4.mergedPrs.0", "<remove>", "child G1")]
    [InlineData("acceptedChildren.3.mergedPrs.0", "<remove>", "child")]
    [InlineData("acceptedChildren.4.mergedPrs.0.mergeCommit", "6a1a78db71b04bee3f2d42268e92800d94333f67", "identity")]
    [InlineData("s2Closure.closure.result", "FAILURE", "S2 closure")]
    [InlineData("z3.assetPins.assets.0.sha256", "00", "Z3 pin")]
    [InlineData("seeds.0.value", "1", "seeds")]
    [InlineData("supersedes.commit", "5ebdbee2fee7fc87d0db187ea81024c3aadcbc54", "supersedes")]
    public void ATamperedFieldIsFound(string pointer, string value, string expected)
    {
        var manifest = Manifest();
        Set(manifest, pointer, value);

        var findings = Verify(Git(), manifest);

        Assert.True(findings.Count >= 1 && findings.All(f => f.Contains(expected, StringComparison.Ordinal)),
            $"expected only '{expected}' findings, got:{Environment.NewLine}{string.Join(Environment.NewLine, findings)}");
    }

    // ---- Verifier ----

    /// <summary>The accepted child set: every merged PR of every gate, as reviewed in C1.</summary>
    private static readonly Dictionary<string, int[]> GatePrs = new()
    {
        ["R0"] = [1466, 1470, 1477, 1478, 1484, 1488, 1501, 1504, 1507, 1533], ["R1"] = [1476], ["S1"] = [1480],
        ["S2"] = [1494, 1495, 1496, 1497, 1498, 1502, 1503, 1499], ["G1"] = [1471], ["G2"] = [1479, 1486],
        ["G3"] = [1492, 1500], ["G4"] = [1481], ["P1"] = [1472], ["B1"] = [1473, 1482], ["B2"] = [1487, 1527], ["R2"] = [1474, 1475],
    };

    internal static List<string> Verify(GitRepo git, JsonNode manifest)
    {
        var findings = new List<string>();
        var candidate = Str(manifest["candidate"]!["commit"]);
        void Fail(string message) => findings.Add(message);

        // §6 / P1: durable identity through the P1 verifier itself.
        var identity = manifest["candidate"]!["durableIdentity"]!;
        var entry = new JsonObject
        {
            ["path"] = CandidateInvalidation.ManifestPath,
            ["measuredCommit"] = candidate,
            ["basis"] = DurableProvenance.BasisIdenticalSrcTree,
            ["measuredTreeHashes"] = new JsonObject { ["src"] = identity["treeHashes"]!["src"]!.DeepClone() },
            ["durableIdentity"] = identity.DeepClone(),
        };
        using (var doc = JsonDocument.Parse(entry.ToJsonString()))
            foreach (var f in DurableProvenance.Verify(git, new[] { doc.RootElement.Clone() }).Findings)
                Fail($"durable identity: {f}");
        if (Str(identity["commit"]) != candidate) Fail("durable identity: commit differs from candidate.commit");
        if (!git.CommitExists(candidate)) return findings;

        // The complete gitlink inventory of the candidate tree, not only the recorded entries.
        var (_, tree, _) = git.Run("ls-tree", "-r", candidate);
        var gitlinks = tree.Split('\n').Select(l => l.Split(new[] { ' ', '\t' }, 4)).Where(p => p.Length == 4 && p[1] == "commit")
            .Select(p => $"{p[3]} {p[2]}").ToHashSet();
        var recordedLinks = manifest["submodules"]!["entries"]!.AsArray().Select(s => $"{Str(s!["path"])} {Str(s["commit"])}").ToHashSet();
        if (gitlinks.Count == 0 || !gitlinks.SetEquals(recordedLinks)) Fail("submodule inventory differs from the candidate's gitlinks");
        CheckHashes(git, candidate, manifest["dependencies"]!["lockfiles"]!.AsObject(), "lockfile", Fail);
        CheckHashes(git, candidate, manifest["dependencies"]!["toolManifests"]!["node"]!.AsObject(), "lockfile", Fail);
        foreach (var p in manifest["producers"]!.AsArray())
            CheckHashes(git, candidate, p!["files"]!.AsObject(), "producer " + Str(p["id"]), Fail);
        CheckHashes(git, candidate, manifest["registeredManifests"]!["files"]!.AsObject(), "registered manifest", Fail);
        CheckZ3Pins(git, candidate, manifest["z3"]!, Fail);
        var seeds = manifest["seeds"]!.AsArray().ToDictionary(s => Str(s!["id"]), s => s!["value"]?.GetValue<string>());
        var r1 = JsonNode.Parse(Blob(git, $"{candidate}:docs/plans/evidence/r1-1419/registration.json")!)!;
        var b1 = Blob(git, $"{candidate}:docs/plans/evidence/b1-1276/registration/registration.json")!;
        if (seeds.GetValueOrDefault("r1-master-seed") != Str(r1["generation"]!["masterSeed"]) || seeds.GetValueOrDefault("b1-bootstrap") != "1276"
            || !System.Text.Encoding.UTF8.GetString(b1).Contains("SplitMix64 seed 1276", StringComparison.Ordinal))
            Fail("seeds differ from the R1 and B1 registrations");

        // Contract packet: sha256.json bytes, its file map, and the amendment log.
        var packet = manifest["contractPacket"]!;
        var shaPath = Str(packet["sha256Json"]!["path"]);
        if (Sha(git, candidate, shaPath) != Str(packet["sha256Json"]!["sha256"])) Fail("contract packet: sha256.json hash differs");
        var contractSha = JsonNode.Parse(Blob(git, $"{candidate}:{shaPath}")!)!;
        if (!JsonNode.DeepEquals(contractSha["files"], packet["sha256Json"]!["files"])) Fail("contract packet: file map differs");
        var contract = JsonNode.Parse(Blob(git, $"{candidate}:docs/plans/evidence/evidence-contract-1407/contract.json")!)!;
        if (Str(contract["contractVersion"]) != Str(packet["contractVersion"])) Fail("contract packet: contractVersion differs");
        var log = contract["amendmentLog"]!.AsArray();
        var recorded = packet["amendmentLog"]!.AsArray();
        if (log.Count != recorded.Count) Fail("amendment log: entry count differs");
        foreach (var (actual, rec) in log.Zip(recorded))
        {
            if (Str(actual!["version"]) != Str(rec!["version"]) || Str(actual["timestampUtc"]) != Str(rec["timestampUtc"])
                || actual["reviewedInPr"]!.GetValue<int>() != rec["reviewedInPr"]!.GetValue<int>()
                || actual["afterDecisionBearingInspection"]!.GetValue<bool>() != rec["afterDecisionBearingInspection"]!.GetValue<bool>())
                Fail($"amendment log: {rec["version"]} differs from contract.json");
            CheckPrMerge(git, candidate, rec["reviewedInPr"]!.GetValue<int>(), Str(rec["mergeCommit"]), null, $"amendment log: {rec["version"]}", Fail);
        }
        var canonical = string.Concat(recorded.Select(r => $"{r!["version"]}|{r["timestampUtc"]}|{r["reviewedInPr"]}|"
            + $"{(r["afterDecisionBearingInspection"]!.GetValue<bool>() ? "true" : "false")}|{r["mergeCommit"]}\n"));
        if (DurableProvenance.Sha256(System.Text.Encoding.UTF8.GetBytes(canonical)) != Str(packet["amendmentLogSha256"]))
            Fail("amendment log: amendmentLogSha256 differs");

        // Registered packets: the sha256.json bytes and every file they list, at the candidate.
        foreach (var p in manifest["registeredManifests"]!["packets"]!.AsArray())
        {
            var path = Str(p!["path"]);
            if (Sha(git, candidate, path) != Str(p["sha256"])) Fail($"packet {path}: sha256.json hash differs");
            var lf = Str(p["normalization"]) == "LF";
            var files = JsonNode.Parse(Blob(git, $"{candidate}:{path}") ?? "{}"u8.ToArray())!["files"]?.AsObject();
            if (files == null || files.Count == 0) Fail($"packet {path}: no files");
            lock (Blobs) Preload(git, (files ?? new JsonObject()).Select(f => $"{candidate}:{f.Key}"));
            foreach (var (file, want) in files ?? new JsonObject())
                if (Sha(git, candidate, file, lf) != Str(want)) Fail($"packet {path}: {file} does not verify");
        }

        // Accepted child set: exactly the contract's children other than C1, C2, and A1, each once,
        // and every merge commit is GitHub's merge of the named PR, inside the candidate.
        var required = contract["children"]!.AsArray().Where(c => c!["issue"]!.GetValue<int>() is not (1423 or 1424 or 1408))
            .Select(c => $"{c!["gate"]} #{c["issue"]}").Append("R0 #1407").OrderBy(s => s, StringComparer.Ordinal);
        var children = manifest["acceptedChildren"]!.AsArray();
        if (!required.SequenceEqual(children.Select(c => $"{c!["gate"]} #{c["issue"]}").OrderBy(s => s, StringComparer.Ordinal)))
            Fail("child set differs from contract.json children plus R0 (other than C1, C2, A1)");
        // Each gate's PR set is bound twice: to the table below (reviewed with this PR) and, for R0
        // and S2, to the candidate's own records (contract acceptance and amendment log; S2 repairs).
        var r0 = contract["amendmentLog"]!.AsArray().Select(a => a!["reviewedInPr"]!.GetValue<int>()).Append(contract["acceptance"]!["pr"]!.GetValue<int>());
        if (!r0.Order().SequenceEqual(GatePrs["R0"].Order())) Fail("child R0: PR set differs from contract.json acceptance and amendment log");
        var prMerges = new Dictionary<int, string>();
        foreach (var child in children)
        {
            var prs = child!["mergedPrs"]!.AsArray();
            if (!GatePrs.TryGetValue(Str(child["gate"]), out var expected) || !prs.Select(p => p!["pr"]!.GetValue<int>()).Order().SequenceEqual(expected.Order()))
                Fail($"child {child["gate"]}: PR set differs from the registered gate PRs");
            foreach (var pr in prs)
                prMerges[pr!["pr"]!.GetValue<int>()] = Str(pr["mergeCommit"]);
        }
        foreach (var child in children)
            foreach (var pr in child!["mergedPrs"]!.AsArray())
            {
                var (number, via) = (pr!["pr"]!.GetValue<int>(), pr["mergedViaPr"]?.GetValue<int>());
                if (via != null && (number, via) != (1474, 1475)) Fail($"child {child["gate"]} PR #{number}: only #1474 merged through another PR");
                CheckPrMerge(git, candidate, number, Str(pr["mergeCommit"]), via is int v ? prMerges.GetValueOrDefault(v) : null, $"child {child["gate"]}", Fail);
            }
        foreach (var extra in manifest["additionalMerged"]!.AsArray())
            CheckPrMerge(git, candidate, extra!["pr"]!.GetValue<int>(), Str(extra["mergeCommit"]), null, "child extra", Fail);
        // The superseded candidate: a proper ancestor, named by the manifest that landed for it.
        var superseded = manifest["supersedes"];
        var at = Str(superseded?["manifestAtLanding"]);
        var old = Str(superseded?["commit"]);
        var landed = at.Contains(':') ? Blob(git, at) : null;
        if (old == candidate || !Ancestor(git, old, candidate) || landed == null
            || Str(JsonNode.Parse(landed)!["candidate"]!["commit"]) != old || !Ancestor(git, at.Split(':')[0], candidate))
            Fail("supersedes: the superseded candidate is not a proper ancestor named by its landed manifest");
        var s2 = JsonNode.Parse(Blob(git, $"{candidate}:docs/plans/evidence/s2-1413/dispositions.json")!)!;
        if (!JsonNode.DeepEquals(s2["closure"], manifest["s2Closure"]!["closure"])) Fail("S2 closure differs from dispositions.json");
        var repairs = s2["repairs"]!.AsArray().Select(r => $"{r!["id"]} {r["pr"]} {r["mergeCommit"]}");
        if (!repairs.SequenceEqual(manifest["s2Closure"]!["repairs"]!.AsArray().Select(r => $"{r!["id"]} {r["pr"]} {r["mergeCommit"]}")))
            Fail("S2 closure repairs differ from dispositions.json");
        var s2Child = children.FirstOrDefault(c => Str(c!["gate"]) == "S2")?["mergedPrs"]?.AsArray() ?? new JsonArray();
        foreach (var r in s2["repairs"]!.AsArray().Select(r => r!))
        {
            if (!s2Child.Any(p => p!["pr"]!.GetValue<int>() == r["pr"]!.GetValue<int>() && Str(p["mergeCommit"]) == Str(r["mergeCommit"])))
                Fail($"S2 closure: repair {r["id"]} not in the S2 child");
            foreach (var w in r["regressionWitness"]!.AsArray())
                if (Blob(git, $"{candidate}:{Str(w)}") == null) Fail($"S2 closure: repair {r["id"]} witness {w} absent");
        }
        return findings;
    }

    /// <summary>
    /// The merge commit is an ancestor of the candidate and is GitHub's "Merge pull request #N" commit.
    /// A stacked PR merged through another (#1474 through #1475) must be an ancestor of that PR's merge.
    /// </summary>
    private static void CheckPrMerge(GitRepo git, string candidate, int pr, string merge, string? viaMerge, string label, Action<string> fail)
    {
        if (!Ancestor(git, merge, candidate)) fail($"{label} PR #{pr}: merge {merge} is not an ancestor of the candidate");
        else if (viaMerge != null ? !Ancestor(git, merge, viaMerge)
                 : !git.Run("log", "-1", "--format=%s", merge).Output.StartsWith($"Merge pull request #{pr} from ", StringComparison.Ordinal))
            fail($"{label} PR #{pr}: {merge} is not the merge of that PR (identity)");
    }

    private static void CheckZ3Pins(GitRepo git, string candidate, JsonNode z3, Action<string> fail)
    {
        foreach (var (key, list) in new[] { ("assetPins", "assets"), ("upstreamArchivePins", "archives") })
        {
            var text = System.Text.Encoding.UTF8.GetString(Blob(git, $"{candidate}:{Str(z3[key]!["file"])}") ?? Array.Empty<byte>());
            var lines = text.Split('\n').Where(l => !l.StartsWith('#')).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 3).Select(p => $"{p[0]} {p[1]} {p[2]}").ToHashSet();
            var recorded = z3[key]![list]!.AsArray().Select(a => $"{a!["sha256"]} {a["name"]} {a["bytes"]}").ToHashSet();
            if (recorded.Count == 0 || !lines.SetEquals(recorded)) fail($"Z3 pin {key} differs from {z3[key]!["file"]}");
        }
    }

    private static void CheckHashes(GitRepo git, string candidate, JsonObject map, string label, Action<string> fail)
    {
        if (map.Count == 0) fail($"{label}: empty hash map");
        foreach (var (path, want) in map)
            if (Sha(git, candidate, path) != Str(want)) fail($"{label} {path} does not hash to the recorded value at the candidate");
    }

    private static string? Sha(GitRepo git, string commit, string path, bool lf = false)
    {
        var bytes = Blob(git, $"{commit}:{path}");
        if (bytes == null) return null;
        if (lf) bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));
        return DurableProvenance.Sha256(bytes);
    }

    /// <summary>Blob bytes at "commit:path", read once per test process through one <c>git cat-file --batch</c>.</summary>
    private static byte[]? Blob(GitRepo git, string spec)
    {
        lock (Blobs)
        {
            if (!Blobs.TryGetValue((git.Root, spec), out var bytes))
            {
                Preload(git, new[] { spec });
                bytes = Blobs[(git.Root, spec)];
            }
            return bytes;
        }
    }

    private static readonly Dictionary<(string, string), byte[]?> Blobs = new();

    private static void Preload(GitRepo git, IEnumerable<string> specs)
    {
        var todo = specs.Where(s => !Blobs.ContainsKey((git.Root, s)) && !s.Contains('\n')).Distinct().ToList();
        if (todo.Count == 0) return;
        var info = new System.Diagnostics.ProcessStartInfo("git", "cat-file --batch")
        {
            WorkingDirectory = git.Root, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(info)!;
        var writer = Task.Run(() =>
        {
            foreach (var s in todo) process.StandardInput.Write(s + "\n");
            process.StandardInput.Close();
        });
        var output = process.StandardOutput.BaseStream;
        foreach (var spec in todo)
        {
            var header = ReadLine(output).Split(' ');
            if (header.Length == 3 && header[1] == "blob")
            {
                var bytes = new byte[int.Parse(header[2])];
                output.ReadExactly(bytes);
                output.ReadByte();   // trailing LF
                Blobs[(git.Root, spec)] = bytes;
            }
            else
            {
                if (header.Length == 3) output.ReadExactly(new byte[int.Parse(header[2]) + 1]);
                Blobs[(git.Root, spec)] = null;
            }
        }
        writer.Wait();
        process.WaitForExit();
    }

    private static string ReadLine(Stream stream)
    {
        var line = new List<byte>();
        for (var b = stream.ReadByte(); b != '\n' && b != -1; b = stream.ReadByte()) line.Add((byte)b);
        return System.Text.Encoding.UTF8.GetString(line.ToArray());
    }

    private static string? Gitlink(GitRepo git, string commit, string path)
    {
        var (exit, output, _) = git.Run("ls-tree", commit, "--", path);
        var parts = output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return exit == 0 && parts.Length == 4 && parts[1] == "commit" ? parts[2] : null;
    }

    private static bool Ancestor(GitRepo git, string commit, string candidate)
    {
        lock (Ancestry)
        {
            var key = (git.Root, commit, candidate);
            if (!Ancestry.TryGetValue(key, out var result))
                Ancestry[key] = result = git.CommitExists(commit) && git.IsAncestor(commit, candidate);
            return result;
        }
    }

    private static readonly Dictionary<(string, string, string), bool> Ancestry = new();

    /// <summary>Newest commit after the candidate whose diff against its first parent touches a non-review C1 file.</summary>
    private static string? NewestC1Commit(GitRepo git, string candidate)
    {
        var (_, revs, _) = git.Run("rev-list", "--topo-order", $"{candidate}..HEAD");
        foreach (var commit in revs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (_, names, _) = git.Run("diff-tree", "-r", "--no-renames", "--ignore-submodules=none", "--name-only", "-z", commit + "^1", commit);
            if (names.Split('\0').Any(p => (p.StartsWith(CandidateInvalidation.C1Directory, StringComparison.Ordinal)
                    && !p.StartsWith(CandidateInvalidation.C1Reviews, StringComparison.Ordinal))
                    || CandidateInvalidation.C1Files.Contains(p)))
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
            // Keys may contain dots (file paths): take the longest existing key first.
            var key = node is JsonObject obj ? obj.Select(p => p.Key).Where(k => rest == k || rest.StartsWith(k + ".", StringComparison.Ordinal))
                .OrderByDescending(k => k.Length).First() : rest.Split('.')[0];
            rest = rest.Length > key.Length ? rest[(key.Length + 1)..] : "";
            JsonNode? Child() => node is JsonArray a ? a[int.Parse(key)] : node![key];
            if (rest == "")
            {
                var old = Child();
                JsonNode? replacement = old is JsonValue v && v.TryGetValue<int>(out _) ? JsonValue.Create(int.Parse(value))
                    : old is JsonValue b && b.TryGetValue<bool>(out _) ? JsonValue.Create(bool.Parse(value)) : JsonValue.Create(value);
                if (value == "<remove>" && node is JsonArray removeFrom) removeFrom.RemoveAt(int.Parse(key));
                else if (node is JsonArray arr) arr[int.Parse(key)] = replacement;
                else node![key] = replacement;
                return;
            }
            node = Child()!;
        }
    }

    private static string Str(JsonNode? node) => node?.GetValue<string>() ?? "";

    private static JsonNode Manifest()
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), CandidateInvalidation.ManifestPath)))!;

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
