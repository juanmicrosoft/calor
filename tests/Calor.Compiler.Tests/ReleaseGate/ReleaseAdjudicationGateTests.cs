using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.EvidenceContract;
using Xunit;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// #1410 (milestone 0.24 R2) — <c>scripts/verify_release_adjudication.py</c>, the gate every public
/// release surface runs, accepts exactly one well-formed successful #1408 adjudication identity and
/// fails closed on everything else. Each test builds a throwaway git repository holding the real
/// frozen #1407 contract packet, a candidate commit, and an adjudication commit with a terminal
/// record, then runs the real script. The positive control passes with every surface checked at
/// once; each negative control changes one fact and asserts the exact set of violation codes, so a
/// gate that rejected everything would fail the positive control and a gate that accepted the
/// changed fact would fail its negative control.
/// </summary>
public class ReleaseAdjudicationGateTests
{
    internal const string PacketDir = "docs/plans/evidence/evidence-contract-1407";
    internal const string RecordPath = "docs/plans/evidence/adjudication-1408/terminal-record.json";
    internal const string ManifestPath = "docs/plans/evidence/candidate-1423/manifest.json";
    internal const string FreezePath = "docs/plans/evidence/regeneration-1424/freeze-manifest.json";
    internal const string RegistryPath = "docs/plans/evidence/regeneration-1424/claim-registry.json";
    internal const string BenchmarkPath = "website/public/data/benchmark-results.json";
    internal const string ReleaseVersion = "0.24.0";
    internal const string Limitation =
        "adjudicated by the maintainer who directed and merged the repairs; not independently adjudicated";

    // ------------------------------------------------------------------
    // Positive controls
    // ------------------------------------------------------------------

    [Fact]
    public void WellFormedAdjudicationPassesForEverySurface()
    {
        using var repo = GateRepo.Build();
        var (code, output) = repo.Run(repo.AllSurfaces());
        Assert.True(code == 0, output);
        Assert.Contains($"candidate={repo.Candidate}", output);
        Assert.Contains("prerelease=true", output);
    }

    [Fact]
    public void FixtureRecordIsAlsoAValidTerminalRecordForTheContractValidator()
    {
        using var repo = GateRepo.Build();
        var violations = EvidenceContractValidator.ValidateTerminalRecord(
            repo.Contract, repo.Inventory, GateRepo.Claims, repo.Record);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void NegatedIndependenceWordingIsAllowed()
    {
        using var repo = GateRepo.Build(notes: $"## [{ReleaseVersion}]\n\nEvidence is {Limitation}. It is not independently verified.\n");
        var (code, output) = repo.Run(repo.AllSurfaces());
        Assert.True(code == 0, output);
    }

    [Fact]
    public void ExistingTagAtTheCandidatePasses()
    {
        using var repo = GateRepo.Build();
        repo.Git("tag", "v" + ReleaseVersion, repo.Candidate);
        AssertCodes(repo, Array.Empty<string>(), "--expect-head", "--require-tag");
    }

    // ------------------------------------------------------------------
    // Identity and record
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("sha256:0000")]
    [InlineData("calor-adjudication:v1:ABCDEF0000000000000000000000000000000000:0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("calor-adjudication:v2:0000000000000000000000000000000000000000:0000000000000000000000000000000000000000000000000000000000000000")]
    public void MissingOrMalformedIdentityFails(string identity)
    {
        using var repo = GateRepo.Build();
        AssertCodes(repo.Run(new[] { "--identity", identity }, includeIdentity: false), "G001");
    }

    [Fact]
    public void CommitWithoutARecordFails()
    {
        using var repo = GateRepo.Build();
        var identity = $"calor-adjudication:v1:{repo.Candidate}:{repo.RecordSha}";
        AssertCodes(repo.Run(new[] { "--identity", identity }, includeIdentity: false), "G004");
    }

    [Fact]
    public void MismatchedRecordHashFails()
    {
        using var repo = GateRepo.Build();
        var identity = $"calor-adjudication:v1:{repo.Adjudication}:{new string('0', 64)}";
        AssertCodes(repo.Run(new[] { "--identity", identity }, includeIdentity: false), "G004");
    }

    [Fact]
    public void RecordWithAnotherSchemaFails()
    {
        using var repo = GateRepo.Build(record: r => r["schema"] = "calor.adjudication-terminal-record/0");
        AssertCodes(repo, new[] { "G004" });
    }

    [Fact]
    public void AdjudicationCommitNotOnMainFails()
    {
        using var repo = GateRepo.Build();
        repo.Git("update-ref", "refs/remotes/origin/main", repo.Candidate);
        AssertCodes(repo, new[] { "G003" });
    }

    [Fact]
    public void MissingMainRefFails()
    {
        using var repo = GateRepo.Build();
        repo.Git("update-ref", "-d", "refs/remotes/origin/main");
        AssertCodes(repo, new[] { "G002" });
    }

    [Fact]
    public void ShallowCloneFailsInsteadOfSkipping()
    {
        using var repo = GateRepo.Build();
        var shallow = repo.Root + "-shallow";
        GateRepo.RunGit(Path.GetDirectoryName(repo.Root)!, "clone", "-q", "--depth", "1", "file://" + repo.Root, shallow);
        try
        {
            AssertCodes(repo.Run(new[] { "--identity", repo.Identity, "--repo", shallow }, includeIdentity: false, includeRepo: false), "G002");
        }
        finally
        {
            GateRepo.Delete(shallow);
        }
    }

    // ------------------------------------------------------------------
    // Contract identity (method)
    // ------------------------------------------------------------------

    [Fact]
    public void RecordContractHashesThatDifferFromThePacketFail()
    {
        using var repo = GateRepo.Build(record: r =>
            r["contract"]!["files"]![$"{PacketDir}/contract.json"] = new string('a', 64));
        AssertCodes(repo, new[] { "G005" });
    }

    [Fact]
    public void ContractChangedBetweenCandidateAndAdjudicationFails()
    {
        using var repo = GateRepo.Build(changePacketAtAdjudication: true);
        AssertCodes(repo, new[] { "G005" });
    }

    [Fact]
    public void UnfrozenContractFails()
    {
        using var repo = GateRepo.Build(contract: c => c["status"] = "PROPOSED");
        AssertCodes(repo, new[] { "G005" });
    }

    // ------------------------------------------------------------------
    // Terminal outcome and independence
    // ------------------------------------------------------------------

    [Fact]
    public void FailedMilestoneFails()
    {
        using var repo = GateRepo.Build(record: r => r["outcome"] = "MILESTONE-FAILED");
        AssertCodes(repo, new[] { "G006" });
    }

    [Theory]
    [InlineData("adjudicationIndependence", "independent")]
    [InlineData("limitation", "adjudicated by the maintainer")]
    [InlineData("epicIndependentAdjudicationMet", "true")]
    [InlineData("adjudicationIndependence", null)]
    public void IndependenceDeviationFieldsAreRequired(string field, string? value)
    {
        using var repo = GateRepo.Build(record: r =>
        {
            if (value is null) r.Remove(field);
            else r[field] = value == "true" ? JsonValue.Create(true) : JsonValue.Create(value);
        });
        AssertCodes(repo, new[] { "G007" });
    }

    [Theory]
    [InlineData("SUPPORTED")]
    [InlineData("BLOCKED")]
    [InlineData("CONFIRMED")]
    [InlineData("HISTORICAL-ONLY")]
    public void NonReleasableRowOutcomeFails(string outcome)
    {
        using var repo = GateRepo.Build(record: r => Row(r, "release-quality-reports")["outcome"] = outcome);
        AssertCodes(repo, new[] { "G008" });
    }

    [Fact]
    public void RowWithoutReducedIndependenceFails()
    {
        using var repo = GateRepo.Build(record: r => Row(r, "gate:#1410")["independence"] = "independent");
        AssertCodes(repo, new[] { "G008" });
    }

    [Fact]
    public void MissingRequiredSubjectFails()
    {
        using var repo = GateRepo.Build(record: r => r["adjudications"]!.AsArray().Remove(Row(r, "gate:#1424")));
        AssertCodes(repo, new[] { "G008" });
    }

    [Fact]
    public void DuplicateSubjectFails()
    {
        using var repo = GateRepo.Build(record: r => r["adjudications"]!.AsArray().Add(Row(r, "changelog").DeepClone()));
        AssertCodes(repo, new[] { "G008" });
    }

    [Fact]
    public void InventoryThatStillHasStaleArtifactsFails()
    {
        using var repo = GateRepo.Build(repairInventory: false);
        AssertCodes(repo, new[] { "G008" });
    }

    // ------------------------------------------------------------------
    // Candidate (changed SHA)
    // ------------------------------------------------------------------

    [Fact]
    public void CheckoutOtherThanTheCandidateFails()
    {
        using var repo = GateRepo.Build();
        repo.Git("checkout", "-q", "--detach", repo.Adjudication);
        AssertCodes(repo, new[] { "G009" }, "--expect-head");
    }

    [Fact]
    public void CandidateThatIsAbsentFails()
    {
        using var repo = GateRepo.Build(record: r => r["candidate"]!["commit"] = new string('1', 40));
        AssertCodes(repo, new[] { "G009" });
    }

    [Fact]
    public void DivergentCandidateThatIsNotAnAncestorFails()
    {
        // A real commit with the same version and packet, on a side branch the adjudication
        // commit does not contain: only the ancestry rule can reject it.
        using var repo = GateRepo.Build(sideCandidate: true);
        AssertCodes(repo, new[] { "G009" });
    }

    [Fact]
    public void CandidateVersionThatDiffersFromTheBuildPropsFails()
    {
        using var repo = GateRepo.Build(record: r => r["candidate"]!["version"] = "0.24.1");
        AssertCodes(repo, new[] { "G009" });
    }

    [Fact]
    public void RequestedVersionOtherThanTheAdjudicatedOneFails()
    {
        using var repo = GateRepo.Build();
        AssertCodes(repo, new[] { "G009" }, "--version", "0.22.0");
    }

    [Fact]
    public void TagPointingAwayFromTheCandidateFails()
    {
        using var repo = GateRepo.Build();
        repo.Git("tag", "v" + ReleaseVersion, repo.Adjudication);
        AssertCodes(repo, new[] { "G009" });
    }

    [Fact]
    public void MissingRequiredTagFails()
    {
        using var repo = GateRepo.Build();
        AssertCodes(repo, new[] { "G009" }, "--require-tag");
    }

    // ------------------------------------------------------------------
    // Evidence manifests (changed evidence hash)
    // ------------------------------------------------------------------

    [Fact]
    public void ChangedEvidenceManifestHashFails()
    {
        using var repo = GateRepo.Build(record: r => r["evidenceManifests"]![0]!["sha256"] = new string('b', 64));
        AssertCodes(repo, new[] { "G010" });
    }

    [Fact]
    public void RecordWithoutEvidenceManifestsFails()
    {
        using var repo = GateRepo.Build(record: r => r["evidenceManifests"] = new JsonArray());
        AssertCodes(repo, new[] { "G010" });
    }

    // ------------------------------------------------------------------
    // Surfaces (changed bytes or public claim)
    // ------------------------------------------------------------------

    [Fact]
    public void RecordWithoutASurfaceFails()
    {
        using var repo = GateRepo.Build(record: r => r["publication"]!.AsObject().Remove("website"));
        AssertCodes(repo, new[] { "G011" });
    }

    [Fact]
    public void ChangedPackageFails()
    {
        using var repo = GateRepo.Build();
        File.AppendAllText(Path.Combine(repo.NugetDir, $"Calor.{ReleaseVersion}.nupkg"), "x");
        AssertCodes(repo, new[] { "G011" }, "--nuget-dir", repo.NugetDir);
    }

    [Fact]
    public void UnadjudicatedExtraPackageFails()
    {
        using var repo = GateRepo.Build();
        GateRepo.WritePackage(Path.Combine(repo.NugetDir, "Calor.Extra.0.24.0.nupkg"), "Calor.Extra", "extra");
        AssertCodes(repo, new[] { "G011" }, "--nuget-dir", repo.NugetDir);
    }

    [Fact]
    public void ChangedReleaseNotesFail()
    {
        using var repo = GateRepo.Build();
        File.AppendAllText(repo.NotesPath, "\nAlso faster.\n");
        AssertCodes(repo, new[] { "G011" }, "--release-notes", repo.NotesPath);
    }

    [Fact]
    public void ChangedWebsiteTreeFails()
    {
        using var repo = GateRepo.Build();
        File.WriteAllText(Path.Combine(repo.WebsiteDir, "extra.html"), "<p>new claim</p>");
        AssertCodes(repo, new[] { "G011" }, "--website-dir", repo.WebsiteDir);
    }

    [Fact]
    public void UnadjudicatedBenchmarkFileFails()
    {
        using var repo = GateRepo.Build();
        File.WriteAllText(Path.Combine(repo.Root, "website/public/data/other-results.json"), "{}");
        AssertCodes(repo, new[] { "G011" }, "--benchmark-worktree");
    }

    [Fact]
    public void ChangedBenchmarkFileFails()
    {
        using var repo = GateRepo.Build();
        File.AppendAllText(Path.Combine(repo.Root, BenchmarkPath), " ");
        AssertCodes(repo, new[] { "G011" }, "--benchmark-worktree");
    }

    [Theory]
    [InlineData("0.24 evidence was independently adjudicated.")]
    [InlineData("Every proof is Independently\n  Verified.")]
    [InlineData("Results passed independent verification.")]
    public void IndependentClaimInAdjudicatedNotesFails(string claim)
    {
        // The record adjudicated these exact notes, so only the wording rule can fire.
        using var repo = GateRepo.Build(notes: $"## [{ReleaseVersion}]\n\n{claim}\n");
        AssertCodes(repo, new[] { "G012" }, "--release-notes", repo.NotesPath);
    }

    [Fact]
    public void IndependentClaimOnTheAdjudicatedWebsiteFails()
    {
        using var repo = GateRepo.Build(website: "<p>Independently <b>adjudicated</b> evidence</p>");
        AssertCodes(repo, new[] { "G012" }, "--website-dir", repo.WebsiteDir);
    }

    [Fact]
    public void ReleaseBodyWithoutTheIdentityTrailerFails()
    {
        using var repo = GateRepo.Build();
        File.WriteAllText(repo.BodyPath, File.ReadAllText(repo.NotesPath));
        AssertCodes(repo, new[] { "G013" }, "--release-body", repo.BodyPath);
    }

    [Fact]
    public void ReleaseBodyNamingAnotherIdentityFails()
    {
        using var repo = GateRepo.Build();
        var other = $"calor-adjudication:v1:{repo.Adjudication}:{new string('c', 64)}";
        File.WriteAllText(repo.BodyPath, File.ReadAllText(repo.NotesPath) + $"\n<!-- calor-adjudication: {other} -->\n");
        AssertCodes(repo, new[] { "G013" }, "--release-body", repo.BodyPath);
    }

    [Fact]
    public void ReleaseBodyWithEditedNotesFails()
    {
        using var repo = GateRepo.Build();
        File.WriteAllText(repo.BodyPath, "## [0.24.0]\n\nEdited after release.\n" + $"\n<!-- calor-adjudication: {repo.Identity} -->\n");
        AssertCodes(repo, new[] { "G013" }, "--release-body", repo.BodyPath);
    }

    [Fact]
    public void ReleaseBodyWithCrLfLineEndingsPasses()
    {
        using var repo = GateRepo.Build();
        File.WriteAllText(repo.BodyPath, File.ReadAllText(repo.BodyPath).Replace("\n", "\r\n"));
        AssertCodes(repo, Array.Empty<string>(), "--release-body", repo.BodyPath);
    }

    // ------------------------------------------------------------------
    // Round 1 review controls: claims, manifests, schema, metadata, registry, wording
    // ------------------------------------------------------------------

    [Fact]
    public void UnregisteredClaimSubjectFails()
    {
        using var repo = GateRepo.Build(record: r => r["adjudications"]!.AsArray().Add(new JsonObject
        {
            ["subject"] = "claim:not-registered", ["outcome"] = "BOUNDED", ["independence"] = "reduced-maintainer-adjudicated",
        }));
        AssertCodes(repo, new[] { "G008" });
    }

    [Fact]
    public void OmittedRegisteredClaimFails()
    {
        using var repo = GateRepo.Build(record: r => r["adjudications"]!.AsArray().Remove(Row(r, GateRepo.Claims[0])));
        AssertCodes(repo, new[] { "G008" });
    }

    [Fact]
    public void BlockedRegisteredClaimFails()
    {
        using var repo = GateRepo.Build(record: r => Row(r, GateRepo.Claims[0])["outcome"] = "BLOCKED");
        AssertCodes(repo, new[] { "G008" });
    }

    [Fact]
    public void ChangedClaimRegistryHashFails()
    {
        using var repo = GateRepo.Build(record: r => r["claimRegistry"]!["sha256"] = new string('d', 64));
        AssertCodes(repo, new[] { "G008" });
    }

    [Theory]
    [InlineData("candidate-manifest")]
    [InlineData("raw-artifact-freeze")]
    public void MissingEvidenceManifestRoleFails(string role)
    {
        using var repo = GateRepo.Build(record: r => r["evidenceManifests"]!.AsArray()
            .Remove(r["evidenceManifests"]!.AsArray().First(m => m!["role"]!.GetValue<string>() == role)));
        AssertCodes(repo, new[] { "G010" });
    }

    [Fact]
    public void EvidenceManifestThatDoesNotNameTheCandidateFails()
    {
        // Point the freeze role at the committed claim registry: present and correctly hashed,
        // but it does not bind the candidate.
        using var repo = GateRepo.Build(record: r =>
        {
            var freeze = r["evidenceManifests"]!.AsArray().First(m => m!["role"]!.GetValue<string>() == "raw-artifact-freeze")!;
            freeze["path"] = RegistryPath;
            freeze["sha256"] = r["claimRegistry"]!["sha256"]!.DeepClone();
        });
        AssertCodes(repo, new[] { "G010" });
    }

    [Theory]
    [InlineData("release-notes")]
    [InlineData("nuget-packages")]
    [InlineData("release-metadata")]
    [InlineData("website")]
    [InlineData("benchmark-results")]
    public void EmptyPublicationBlockFailsWithoutAnySurfaceOption(string name)
    {
        using var repo = GateRepo.Build(record: r => r["publication"]![name] = new JsonObject());
        AssertCodes(repo, new[] { "G011" });
    }

    [Fact]
    public void ChangedReleaseMetadataFails()
    {
        using var repo = GateRepo.Build();
        File.AppendAllText(Path.Combine(repo.MetadataDir, "sbom.json"), " ");
        AssertCodes(repo, new[] { "G011" }, "--metadata-dir", repo.MetadataDir);
    }

    [Fact]
    public void RegistryPackageDifferingOnlyByTheRepositorySignaturePasses()
    {
        using var repo = GateRepo.Build();
        GateRepo.WritePackage(Path.Combine(repo.RegistryDir, $"calor.{ReleaseVersion}.nupkg"), "Calor", "Calor compiler", signed: true);
        AssertCodes(repo, Array.Empty<string>(), "--nuget-dir", repo.NugetDir, "--registry-dir", repo.RegistryDir);
    }

    [Fact]
    public void RegistryPackageWithOtherContentFails()
    {
        using var repo = GateRepo.Build();
        GateRepo.WritePackage(Path.Combine(repo.RegistryDir, $"calor.{ReleaseVersion}.nupkg"), "Calor", "Another build", signed: true);
        AssertCodes(repo, new[] { "G011" }, "--nuget-dir", repo.NugetDir, "--registry-dir", repo.RegistryDir);
    }

    [Theory]
    [InlineData("<p>Independently&#32;verified evidence</p>")]
    [InlineData("<p>Indepen<span>dently</span> verified evidence</p>")]
    [InlineData("<p>**Independently** adjudicated</p>")]
    public void ObfuscatedIndependentClaimOnTheWebsiteFails(string page)
    {
        using var repo = GateRepo.Build(website: page);
        AssertCodes(repo, new[] { "G012" }, "--website-dir", repo.WebsiteDir);
    }

    [Fact]
    public void IndependentClaimInThePackageDescriptionFails()
    {
        using var repo = GateRepo.Build(description: "Independently verified compiler");
        AssertCodes(repo, new[] { "G012" }, "--nuget-dir", repo.NugetDir);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static JsonNode Row(JsonObject record, string subject) =>
        record["adjudications"]!.AsArray().First(r => r!["subject"]!.GetValue<string>() == subject)!;

    private static void AssertCodes(GateRepo repo, string[] expected, params string[] extra) =>
        AssertCodes(repo.Run(extra), expected);

    private static void AssertCodes((int Code, string Output) result, params string[] expected)
    {
        var actual = Regex.Matches(result.Output, @"release gate \(#1410\) (G\d{3}):")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(expected.ToHashSet().SetEquals(actual),
            $"expected codes [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]\n{result.Output}");
        Assert.Equal(expected.Length == 0 ? 0 : 1, result.Code);
    }

    internal static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
