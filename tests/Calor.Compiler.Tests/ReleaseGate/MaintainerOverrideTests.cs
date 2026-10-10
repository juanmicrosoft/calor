using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using static Calor.Compiler.Tests.ReleaseGate.ReleaseAdjudicationGateTests;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// The 0.24.0 maintainer override (authorized 2026-10-10): <c>scripts/verify_maintainer_override.py</c>
/// accepts exactly one of the two dispatch inputs, and in override mode passes only with the
/// committed override record, the MILESTONE-FAILED terminal record it names (by sha256), a matching
/// version, and surfaces consistent with the candidate. Each test builds a throwaway repository and
/// runs the real script; each negative control changes one fact and asserts the exact codes.
/// </summary>
public class MaintainerOverrideTests
{
    private const string OverridePath = "docs/plans/evidence/adjudication-1408/maintainer-override.json";
    private const string Script = "scripts/verify_maintainer_override.py";
    private const string Required = "MILESTONE-FAILED";

    // ------------------------------------------------------------------
    // Mode selection: exactly one input
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("calor-adjudication:v1:x:y", "", "mode=identity")]
    [InlineData("", "0.24.0", "mode=override")]
    public void ExactlyOneInputSelectsItsMode(string identity, string version, string expected)
    {
        var (code, output) = RunScript(".", "--select-mode", "--identity", identity, "--override", version);
        Assert.True(code == 0, output);
        Assert.Contains(expected, output);
    }

    [Theory]
    [InlineData("calor-adjudication:v1:x:y", "0.24.0")]
    [InlineData("", "")]
    [InlineData("  ", " ")]
    public void BothOrNeitherInputFails(string identity, string version)
    {
        var (code, output) = RunScript(".", "--select-mode", "--identity", identity, "--override", version);
        Assert.Equal(1, code);
        Assert.Contains("O001", output);
        Assert.DoesNotContain("mode=", output);
        using var repo = OverrideRepo.Build();
        AssertCodes(repo.Run(new[] { "--identity", identity, "--override", version, "--candidate", repo.Candidate }), "O001");
    }

    // ------------------------------------------------------------------
    // Override mode
    // ------------------------------------------------------------------

    [Fact]
    public void CommittedOverridePassesForEverySurface()
    {
        using var repo = OverrideRepo.Build();
        var (code, output) = repo.Run(repo.AllSurfaces());
        Assert.True(code == 0, output);
        Assert.Contains($"candidate={repo.Candidate}", output);
        Assert.Contains("tag=v0.24.0", output);
        Assert.Contains("prerelease=true", output);
        Assert.Contains($"override_sha256={repo.OverrideSha}", output);
        Assert.Contains("UNGATED", output);
    }

    [Fact]
    public void MissingOverrideRecordFails()
    {
        using var repo = OverrideRepo.Build(writeOverride: false);
        AssertCodes(repo.Run(repo.Basic()), "O004");
    }

    [Fact]
    public void WrongTerminalRecordHashFails()
    {
        using var repo = OverrideRepo.Build(over: o => o["terminalRecord"]!["sha256"] = new string('0', 64));
        AssertCodes(repo.Run(repo.Basic()), "O006");
    }

    [Fact]
    public void MissingTerminalRecordFails()
    {
        using var repo = OverrideRepo.Build(writeRecord: false);
        AssertCodes(repo.Run(repo.Basic()), "O006");
    }

    [Fact]
    public void OutcomeOtherThanFailedFails()
    {
        using var repo = OverrideRepo.Build(outcome: "MILESTONE-SUCCEEDED");
        AssertCodes(repo.Run(repo.Basic()), "O007");
    }

    [Fact]
    public void VersionMismatchFails()
    {
        using var repo = OverrideRepo.Build();
        AssertCodes(repo.Run(new[] { "--override", "0.24.1", "--candidate", repo.Candidate }), "O005");
        using var props = OverrideRepo.Build(propsVersion: "0.25.0");
        AssertCodes(props.Run(props.Basic()), "O005");
    }

    [Theory]
    [InlineData("v0.24.0")]
    [InlineData("0.24")]
    public void MalformedOverrideVersionFails(string version)
    {
        using var repo = OverrideRepo.Build();
        AssertCodes(repo.Run(new[] { "--override", version, "--candidate", repo.Candidate }), "O002");
    }

    [Fact]
    public void RecordMustStayUngatedAndCarryTheAuthorization()
    {
        using var repo = OverrideRepo.Build(over: o => { o["gated"] = true; o["authorization"] = "ok"; });
        AssertCodes(repo.Run(repo.Basic()), "O004");
    }

    [Fact]
    public void CandidateNotOnMainFails()
    {
        using var repo = OverrideRepo.Build();
        repo.Git("update-ref", "refs/remotes/origin/main", repo.Base);
        AssertCodes(repo.Run(repo.Basic()), "O003");
    }

    [Fact]
    public void NotesThatDifferOrOmitTheOverrideFail()
    {
        using var repo = OverrideRepo.Build();
        File.WriteAllText(repo.NotesPath, "## [0.24.0] - 2026-10-07\n\nAll good.\n");
        AssertCodes(repo.Run(repo.Basic("--release-notes", repo.NotesPath)), "O009");
    }

    [Fact]
    public void ForbiddenWordingInTheNotesFails()
    {
        using var repo = OverrideRepo.Build(extraNote: "The evidence was independently verified.");
        AssertCodes(repo.Run(repo.Basic("--release-notes", repo.NotesPath)), "G012");
    }

    [Fact]
    public void ReleaseBodyWithoutTheOverrideTrailerFails()
    {
        using var repo = OverrideRepo.Build();
        File.WriteAllText(repo.BodyPath, File.ReadAllText(repo.NotesPath) + "\n<!-- calor-adjudication: x -->\n");
        AssertCodes(repo.Run(repo.Basic("--release-body", repo.BodyPath)), "O010");
    }

    [Fact]
    public void DifferentPackageAlreadyOnNugetFails()
    {
        using var repo = OverrideRepo.Build();
        OverrideRepo.WritePackage(Path.Combine(repo.RegistryDir, "calor.0.24.0.nupkg"), "calor", "0.24.0", repo.Candidate, "other");
        AssertCodes(repo.Run(repo.Basic("--nuget-dir", repo.NugetDir, "--registry-dir", repo.RegistryDir)), "O011");
    }

    [Fact]
    public void PackageBuiltFromAnotherCommitFails()
    {
        using var repo = OverrideRepo.Build();
        OverrideRepo.WritePackage(Path.Combine(repo.NugetDir, "Calor.Sdk.0.24.0.nupkg"), "Calor.Sdk", "0.24.0", repo.Base);
        AssertCodes(repo.Run(repo.Basic("--nuget-dir", repo.NugetDir)), "O011");
    }

    [Fact]
    public void HeadOtherThanTheCandidateFails()
    {
        using var repo = OverrideRepo.Build();
        repo.Git("checkout", "-q", "--detach", repo.Base);
        AssertCodes(repo.Run(repo.Basic("--expect-head")), "O008");
    }

    // ------------------------------------------------------------------
    // The committed record and notes in this repository
    // ------------------------------------------------------------------

    [Fact]
    public void CommittedOverrideRecordMatchesTheRepository()
    {
        var root = RepoRoot();
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(root, OverridePath)))!;
        var version = Regex.Match(File.ReadAllText(Path.Combine(root, "Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Equal("calor.maintainer-override/1", record["schema"]!.GetValue<string>());
        Assert.Equal(version, record["version"]!.GetValue<string>());
        Assert.Equal("I authorize changing the release gate", record["authorization"]!.GetValue<string>());
        Assert.False(record["gated"]!.GetValue<bool>());
        Assert.Equal(RecordPath, record["terminalRecord"]!["path"]!.GetValue<string>());
        Assert.Equal(Required, record["terminalRecord"]!["outcome"]!.GetValue<string>());
        foreach (var file in new[] { "CHANGELOG.md", "website/content/changelog.mdx" })
        {
            var text = File.ReadAllText(Path.Combine(root, file)).Replace("\r\n", "\n");
            var section = Regex.Match(text, $@"^## \[{Regex.Escape(version)}\][\s\S]*?(?=^## \[)", RegexOptions.Multiline).Value;
            var flat = Regex.Replace(section, @"\s+", " ");
            foreach (var needle in record["requiredNoteText"]!.AsArray())
                Assert.Contains(needle!.GetValue<string>(), flat);
            Assert.DoesNotMatch(new Regex(@"(?<!not )independently (?:adjudicated|verified)", RegexOptions.IgnoreCase), section);
        }
        // Once #1543 lands the terminal record, its bytes must be the ones the override names.
        var terminal = Path.Combine(root, RecordPath);
        if (File.Exists(terminal))
            Assert.Equal(record["terminalRecord"]!["sha256"]!.GetValue<string>(), Sha(File.ReadAllBytes(terminal)));
    }

    private static void AssertCodes((int Code, string Output) result, params string[] expected)
    {
        var actual = Regex.Matches(result.Output, @"maintainer override ([GO]\d{3}):")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(expected.ToHashSet().SetEquals(actual),
            $"expected codes [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]\n{result.Output}");
        Assert.Equal(1, result.Code);
    }

    internal static (int Code, string Output) RunScript(string repo, params string[] args)
    {
        var start = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(RepoRoot(), Script));
        start.ArgumentList.Add("--repo");
        start.ArgumentList.Add(repo);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment.Remove("GITHUB_OUTPUT");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr);
    }

    /// <summary>A throwaway repository: a base commit, then the candidate on main.</summary>
    private sealed class OverrideRepo : IDisposable
    {
        public required string Root { get; init; }
        public required string Scratch { get; init; }
        public string Base { get; private set; } = "";
        public string Candidate { get; private set; } = "";
        public string OverrideSha { get; private set; } = "";
        public string NotesPath => Path.Combine(Scratch, "notes.md");
        public string BodyPath => Path.Combine(Scratch, "body.md");
        public string NugetDir => Path.Combine(Scratch, "nupkg");
        public string MetadataDir => Path.Combine(Scratch, "metadata");
        public string RegistryDir => Path.Combine(Scratch, "registry");

        public static OverrideRepo Build(
            Action<JsonObject>? over = null, string outcome = Required, bool writeOverride = true,
            bool writeRecord = true, string propsVersion = ReleaseVersion, string extraNote = "")
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "calor-ovr-" + Guid.NewGuid().ToString("N")[..12]);
            var repo = new OverrideRepo { Root = Path.Combine(baseDir, "repo"), Scratch = Path.Combine(baseDir, "scratch") };
            foreach (var dir in new[] { repo.Root, repo.NugetDir, repo.MetadataDir, repo.RegistryDir })
                Directory.CreateDirectory(dir);
            repo.Git("init", "-q", "-b", "main");
            repo.Write("README.md", "base\n");
            repo.Base = repo.Commit("base");

            var recordBytes = Encoding.UTF8.GetBytes(new JsonObject
            {
                ["schema"] = "calor.adjudication-terminal-record/1",
                ["issue"] = 1408,
                ["outcome"] = outcome,
            }.ToJsonString());
            var notes = $"## [{ReleaseVersion}] - 2026-10-07\n\nThe adjudication recorded {Required} "
                + $"([record]({RecordPath})). This is a maintainer override. {extraNote}\n";
            var overrideNode = new JsonObject
            {
                ["schema"] = "calor.maintainer-override/1",
                ["version"] = ReleaseVersion,
                ["tag"] = "v" + ReleaseVersion,
                ["publishCommit"] = "dispatch-ref",
                ["terminalRecord"] = new JsonObject
                {
                    ["path"] = RecordPath, ["sha256"] = Sha(recordBytes), ["outcome"] = Required,
                },
                ["decisionDate"] = "2026-10-10",
                ["authorization"] = "I authorize changing the release gate",
                ["gated"] = false,
                ["statement"] = "This release is ungated.",
                ["requiredNoteText"] = new JsonArray(Required, RecordPath, "maintainer override"),
                ["benchmarkPublication"] = "none",
            };
            over?.Invoke(overrideNode);
            var overrideBytes = Encoding.UTF8.GetBytes(overrideNode.ToJsonString());
            repo.OverrideSha = Sha(overrideBytes);
            if (writeRecord) repo.Write(RecordPath, Encoding.UTF8.GetString(recordBytes));
            if (writeOverride) repo.Write(OverridePath, Encoding.UTF8.GetString(overrideBytes));
            repo.Write("Directory.Build.props", $"<Project><PropertyGroup><Version>{propsVersion}</Version></PropertyGroup></Project>\n");
            repo.Write("CHANGELOG.md", "# Changelog\n\n## [Unreleased]\n\n" + notes + "\n## [0.22.0] - 2026-09-15\n\nOld.\n");
            repo.Candidate = repo.Commit("candidate");
            repo.Git("update-ref", "refs/remotes/origin/main", repo.Candidate);

            File.WriteAllText(repo.NotesPath, notes);
            File.WriteAllText(repo.BodyPath, notes + $"\n<!-- calor-maintainer-override: v1:{repo.Candidate}:{repo.OverrideSha} -->\n");
            WritePackage(Path.Combine(repo.NugetDir, $"calor.{ReleaseVersion}.nupkg"), "calor", ReleaseVersion, repo.Candidate);
            WritePackage(Path.Combine(repo.NugetDir, $"Calor.Sdk.{ReleaseVersion}.nupkg"), "Calor.Sdk", ReleaseVersion, repo.Candidate);
            var hashes = Directory.GetFiles(repo.NugetDir).Select(f => Sha(File.ReadAllBytes(f)));
            File.WriteAllText(Path.Combine(repo.MetadataDir, "x.provenance.json"),
                new JsonObject { ["commit"] = repo.Candidate, ["subjects"] = new JsonArray(hashes.Select(h => (JsonNode)h!).ToArray()) }.ToJsonString());
            return repo;
        }

        public string[] Basic(params string[] extra) =>
            new[] { "--override", ReleaseVersion, "--candidate", Candidate }.Concat(extra).ToArray();

        public string[] AllSurfaces() => Basic(
            "--expect-head", "--release-notes", NotesPath, "--release-body", BodyPath,
            "--release-tag", "v" + ReleaseVersion, "--release-title", "v" + ReleaseVersion,
            "--nuget-dir", NugetDir, "--registry-dir", RegistryDir, "--metadata-dir", MetadataDir);

        public (int Code, string Output) Run(string[] args) => RunScript(Root, args);

        public void Git(params string[] args) => GateRepo.RunGit(Root, args);

        public static void WritePackage(string path, string id, string version, string commit, string payload = "")
        {
            if (File.Exists(path)) File.Delete(path);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(archive.CreateEntry($"{id}.nuspec").Open()))
                writer.Write($"<package><metadata><id>{id}</id><version>{version}</version>"
                    + $"<repository type=\"git\" commit=\"{commit}\" /></metadata></package>");
            using (var writer = new StreamWriter(archive.CreateEntry("lib/net10.0/_._").Open()))
                writer.Write(id + payload);
        }

        public void Dispose() => GateRepo.Delete(Path.GetDirectoryName(Root)!);

        private string Commit(string message)
        {
            Git("add", "-A");
            Git("commit", "-q", "-m", message);
            return GateRepo.RunGit(Root, "rev-parse", "HEAD");
        }

        private void Write(string relative, string content)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}
