using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static Calor.Compiler.Tests.ReleaseGate.ReleaseAdjudicationGateTests;

namespace Calor.Compiler.Tests.ReleaseGate;

// Test fixture for ReleaseAdjudicationGateTests (#1410): builds throwaway repositories holding
// the real frozen #1407 packet, a candidate commit, and an adjudication commit.
/// <summary>A throwaway repository: candidate commit, then an adjudication commit on main.</summary>
internal sealed class GateRepo : IDisposable
{
    public required string Root { get; init; }
    public required string Scratch { get; init; }
    public string Candidate { get; private set; } = "";
    public string Adjudication { get; private set; } = "";
    public string RecordSha { get; private set; } = "";
    public string Identity => $"calor-adjudication:v1:{Adjudication}:{RecordSha}";
    public JsonObject Record { get; private set; } = new();
    public JsonNode Contract { get; private set; } = new JsonObject();
    public JsonNode Inventory { get; private set; } = new JsonObject();
    public string NotesPath => Path.Combine(Scratch, "notes.md");
    public string BodyPath => Path.Combine(Scratch, "body.md");
    public string NugetDir => Path.Combine(Scratch, "nupkg");
    public string WebsiteDir => Path.Combine(Scratch, "site");
    public string MetadataDir => Path.Combine(Scratch, "metadata");
    public string RegistryDir => Path.Combine(Scratch, "registry");
    public static readonly string[] Claims = { "claim:release-gate-consumes-identity" };

    public static GateRepo Build(
        Action<JsonObject>? record = null,
        Action<JsonObject>? contract = null,
        bool repairInventory = true,
        bool changePacketAtAdjudication = false,
        bool sideCandidate = false,
        string? notes = null,
        string website = "<p>Calor 0.24.0</p>",
        string description = "Calor compiler",
        string? freezeManifest = null,
        string benchmark = "{\"overallAdvantage\":1.0}\n")
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "calor-r2-" + Guid.NewGuid().ToString("N")[..12]);
        var repo = new GateRepo { Root = Path.Combine(baseDir, "repo"), Scratch = Path.Combine(baseDir, "scratch") };
        Directory.CreateDirectory(repo.Root);
        Directory.CreateDirectory(repo.NugetDir);
        Directory.CreateDirectory(repo.WebsiteDir);
        Directory.CreateDirectory(repo.MetadataDir);
        Directory.CreateDirectory(repo.RegistryDir);
        repo.Git("init", "-q", "-b", "main");

        var source = RepoRoot();
        var contractNode = JsonNode.Parse(File.ReadAllText(Path.Combine(source, PacketDir, "contract.json")))!.AsObject();
        contract?.Invoke(contractNode);
        var inventoryNode = JsonNode.Parse(File.ReadAllText(Path.Combine(source, PacketDir, "artifact-inventory.json")))!.AsObject();
        if (repairInventory)
        {
            foreach (var artifact in inventoryNode["artifacts"]!.AsArray())
                if (artifact!["classification"]!.GetValue<string>() == "stale")
                    artifact["classification"] = "authoritative";
        }
        repo.Contract = contractNode;
        repo.Inventory = inventoryNode;
        repo.Write("docs/plans/v0.24-evidence-contract.md", File.ReadAllText(Path.Combine(source, "docs/plans/v0.24-evidence-contract.md")));
        repo.Write($"{PacketDir}/contract.json", contractNode.ToJsonString());
        repo.Write($"{PacketDir}/artifact-inventory.json", inventoryNode.ToJsonString());
        repo.WritePacketHashes();
        repo.Write("Directory.Build.props", $"<Project><PropertyGroup><Version>{ReleaseVersion}</Version></PropertyGroup></Project>\n");
        repo.Candidate = repo.Commit("candidate");
        var recordedCandidate = repo.Candidate;
        if (sideCandidate)
        {
            repo.Git("checkout", "-q", "-b", "side");
            repo.Git("commit", "-q", "--allow-empty", "-m", "side");
            recordedCandidate = RunGit(repo.Root, "rev-parse", "HEAD");
            repo.Git("checkout", "-q", "main");
        }

        if (changePacketAtAdjudication)
        {
            File.AppendAllText(Path.Combine(repo.Root, "docs/plans/v0.24-evidence-contract.md"), "\nchanged after the candidate\n");
            repo.WritePacketHashes();
        }
        var manifest = "{\"candidate\":\"" + recordedCandidate + "\"}\n";
        repo.Write(ManifestPath, manifest);
        var freeze = freezeManifest?.Replace("{CANDIDATE}", recordedCandidate) ?? manifest;
        repo.Write(FreezePath, freeze);
        var registry = new JsonObject { ["claims"] = new JsonArray(Claims.Select(c => (JsonNode)c).ToArray()) }.ToJsonString();
        repo.Write(RegistryPath, registry);

        var notesText = notes ?? $"## [{ReleaseVersion}] - 2026-11-01\n\nEvidence is {Limitation}.\n";
        File.WriteAllText(repo.NotesPath, notesText);
        WritePackage(Path.Combine(repo.NugetDir, $"Calor.{ReleaseVersion}.nupkg"), "Calor", description);
        WritePackage(Path.Combine(repo.NugetDir, $"Calor.Sdk.{ReleaseVersion}.nupkg"), "Calor.Sdk", description);
        File.WriteAllText(Path.Combine(repo.MetadataDir, "sbom.json"), "{\"sbom\":true}\n");
        File.WriteAllText(Path.Combine(repo.WebsiteDir, "index.html"), website);

        var subjects = inventoryNode["artifacts"]!.AsArray()
            .Select(a => (a!["id"]!.GetValue<string>(), a["classification"]!.GetValue<string>()))
            .Concat(contractNode["children"]!.AsArray().Select(c => c!["issue"]!.GetValue<int>())
                .Where(i => i != 1408).Select(i => ($"gate:#{i}", "gate")))
            .Concat(Claims.Select(c => (c, "claim")));
        var rows = new JsonArray(subjects.Select(s => (JsonNode)new JsonObject
        {
            ["subject"] = s.Item1,
            ["outcome"] = s.Item2 == "historical-only" ? "HISTORICAL-ONLY" : "BOUNDED",
            ["independence"] = "reduced-maintainer-adjudicated",
        }).ToArray());
        var packetHashes = JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Root, PacketDir, "sha256.json")))!["files"]!.DeepClone();
        var recordNode = new JsonObject
        {
            ["schema"] = "calor.adjudication-terminal-record/1",
            ["issue"] = 1408,
            ["outcome"] = "MILESTONE-SUCCEEDED",
            ["adjudicationIndependence"] = "reduced",
            ["limitation"] = Limitation,
            ["epicIndependentAdjudicationMet"] = false,
            ["contract"] = new JsonObject { ["version"] = contractNode["contractVersion"]!.DeepClone(), ["files"] = packetHashes },
            ["candidate"] = new JsonObject { ["commit"] = recordedCandidate, ["version"] = ReleaseVersion },
            ["evidenceManifests"] = new JsonArray(
                new JsonObject { ["role"] = "candidate-manifest", ["path"] = ManifestPath, ["sha256"] = Sha(Encoding.UTF8.GetBytes(manifest)) },
                new JsonObject { ["role"] = "raw-artifact-freeze", ["path"] = FreezePath, ["sha256"] = Sha(Encoding.UTF8.GetBytes(freeze)) }),
            ["claimRegistry"] = new JsonObject { ["path"] = RegistryPath, ["sha256"] = Sha(Encoding.UTF8.GetBytes(registry)) },
            ["adjudications"] = rows,
            ["publication"] = new JsonObject
            {
                ["release-notes"] = new JsonObject { ["sha256"] = Sha(Encoding.UTF8.GetBytes(notesText.Replace("\r\n", "\n").TrimEnd() + "\n")) },
                ["nuget-packages"] = new JsonObject { ["files"] = FileHashes(repo.NugetDir) },
                ["release-metadata"] = new JsonObject { ["files"] = FileHashes(repo.MetadataDir) },
                ["website"] = new JsonObject { ["treeSha256"] = TreeDigest(repo.WebsiteDir) },
                ["benchmark-results"] = new JsonObject
                {
                    ["files"] = new JsonObject { [BenchmarkPath] = Sha(Encoding.UTF8.GetBytes(benchmark)) },
                },
            },
        };
        record?.Invoke(recordNode);
        repo.Record = recordNode;
        var recordBytes = Encoding.UTF8.GetBytes(recordNode.ToJsonString());
        repo.Write(RecordPath, Encoding.UTF8.GetString(recordBytes));
        repo.RecordSha = Sha(recordBytes);
        repo.Adjudication = repo.Commit("adjudication");
        repo.Git("update-ref", "refs/remotes/origin/main", repo.Adjudication);
        repo.Git("checkout", "-q", "--detach", repo.Candidate);
        repo.Write(BenchmarkPath, benchmark);
        File.WriteAllText(repo.BodyPath, notesText + $"\n<!-- calor-adjudication: {repo.Identity} -->\n");
        return repo;
    }

    public string[] AllSurfaces() => new[]
    {
        "--expect-head", "--version", ReleaseVersion, "--release-notes", NotesPath, "--release-body", BodyPath,
        "--nuget-dir", NugetDir, "--registry-dir", RegistryDir, "--metadata-dir", MetadataDir,
        "--website-dir", WebsiteDir, "--benchmark-worktree", "--release-tag", "v" + ReleaseVersion,
        "--release-title", "v" + ReleaseVersion,
    };

    public (int Code, string Output) Run(string[] args, bool includeIdentity = true, bool includeRepo = true)
    {
        var all = new List<string> { Path.Combine(RepoRoot(), "scripts", "verify_release_adjudication.py") };
        if (includeIdentity) all.AddRange(new[] { "--identity", Identity });
        if (includeRepo) all.AddRange(new[] { "--repo", Root });
        all.AddRange(args);
        var start = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in all) start.ArgumentList.Add(arg);
        start.Environment.Remove("GITHUB_OUTPUT");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr);
    }

    public void Git(params string[] args) => RunGit(Root, args);

    public static string RunGit(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-c", "user.name=gate", "-c", "user.email=gate@example.invalid", "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false" })
            start.ArgumentList.Add(arg);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stderr}");
        return stdout.Result.Trim();
    }

    public static void Delete(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    public void Dispose() => Delete(Path.GetDirectoryName(Root)!);

    private string Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
        return RunGit(Root, "rev-parse", "HEAD");
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WritePacketHashes()
    {
        var files = new JsonObject();
        foreach (var path in new[] { "docs/plans/v0.24-evidence-contract.md", $"{PacketDir}/contract.json", $"{PacketDir}/artifact-inventory.json" })
            files[path] = Sha(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Root, path)).Replace("\r\n", "\n")));
        Write($"{PacketDir}/sha256.json", new JsonObject { ["files"] = files }.ToJsonString());
    }

    /// <summary>A minimal .nupkg: a zip with a nuspec carrying the package description.</summary>
    public static void WritePackage(string path, string id, string description, bool signed = false)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry($"{id}.nuspec").Open()))
            writer.Write($"<package><metadata><id>{id}</id><description>{description}</description></metadata></package>");
        using (var writer = new StreamWriter(archive.CreateEntry("lib/net10.0/_._").Open()))
            writer.Write(id);
        if (signed)
            using (var writer = new StreamWriter(archive.CreateEntry(".signature.p7s").Open()))
                writer.Write("repository signature");
    }

    private static JsonObject FileHashes(string dir) => new(Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal)
        .Select(f => KeyValuePair.Create(Path.GetFileName(f), (JsonNode?)Sha(File.ReadAllBytes(f)))));

    private static string TreeDigest(string root)
    {
        var lines = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Rel: Path.GetRelativePath(root, f).Replace('\\', '/'), File: f))
            .OrderBy(e => e.Rel, StringComparer.Ordinal)
            .Select(e => $"{Sha(File.ReadAllBytes(e.File))}  {e.Rel}\n");
        return Sha(Encoding.UTF8.GetBytes(string.Concat(lines)));
    }
}
