using System.Text.RegularExpressions;
using Calor.Compiler.Tests.Provenance;
using static Calor.Compiler.Tests.EvidenceContract.CandidateInvalidation;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// The 0.24 C1 (#1423) invalidation classifier, generalized to any release candidate by a
/// <see cref="CandidateProfile"/>. The 0.24 classifier (<see cref="CandidateInvalidation"/>) stays
/// byte-for-byte as it landed, because editing it would invalidate the 0.24 freeze; this class
/// reuses its public pieces (verdicts, phases, change and report records, the test-manifest rule)
/// and applies the same rules to the profile's paths.
///
/// <para><b>Default deny.</b> Every path invalidates except: the candidate PR's own files before
/// the landing; newly added review records under the profile's reviews directory at any time; the
/// <c>eng/test-manifest.json</c> compiler-test bump before the landing; and newly added data or text
/// files in the profile's downstream directories (regeneration and adjudication records). Exempt
/// files must be plain non-executable files with no dot-segment. <b>Every commit counts:</b> each
/// commit in <c>candidate..target</c> is diffed against each parent, renames off, so a reverted
/// change still invalidates. The landing is the first first-parent commit of protected main after
/// the candidate that holds the profile's manifest; after it, the profile's own files are frozen.</para>
/// </summary>
public static class ReleaseCandidateInvalidation
{
    /// <param name="Release">The release the candidate is for, e.g. "0.25.0".</param>
    /// <param name="Issue">The issue a frozen-file change must return to.</param>
    /// <param name="ManifestPath">The candidate manifest; its first commit on main is the landing.</param>
    /// <param name="Directory">The candidate's evidence directory (generator, inputs, manifest, reviews).</param>
    /// <param name="Files">Other files the candidate PR owns (classifier, its tests, design note).</param>
    /// <param name="Downstream">Directories that receive records about the candidate after the freeze.</param>
    public sealed record CandidateProfile(string Release, int Issue, string ManifestPath, string Directory, string[] Files, string[] Downstream)
    {
        public string Reviews => Directory + "reviews/";
    }

    /// <summary>0.25: no C1 issue exists, so frozen-file changes return to A1 (#1430).</summary>
    public static readonly CandidateProfile V025 = new(
        "0.25.0", 1430,
        "docs/plans/evidence/v0.25-candidate/candidate-manifest.json",
        "docs/plans/evidence/v0.25-candidate/",
        [
            "tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs",
            "tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidationTests.cs",
            "tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs",
        ],
        ["docs/plans/evidence/v0.25-regeneration/", "docs/plans/evidence/adjudication-1430/"]);

    private static readonly string[] DataExtensions = { ".json", ".jsonl", ".md", ".txt", ".log", ".trx", ".csv", ".gz", ".sha256" };
    private const string RegularFile = "100644";

    private static readonly (string Prefix, string Category)[] Categories =
    {
        ("src/", "compiler, runtime, SDK, or tasks source"),
        ("tests/", "test hosts, oracles, or evidence producers"),
        ("tools/", "tools and round-trip harness"),
        ("bench/", "benchmark inputs, corpus, or sweep tool"),
        (".github/", "workflow logic or CI configuration"),
        ("scripts/", "evidence producers or release scripts"),
        ("eng/", "registered inventories, manifests, or the release adjudication map"),
        ("website/", "publication content"),
        (".claude/", "release procedure (skills)"),
        ("docs/", "documentation, scope, register, or evidence (not proven inert)"),
        ("samples/", "samples (Tier 2 inputs)"),
    };

    public static Decision Classify(CandidateProfile profile, Change change, Phase phase)
    {
        var (exempt, reason) = ClassifyPath(profile, change, phase);
        return new Decision(change.Path, exempt, reason);
    }

    private static (bool, string) ClassifyPath(CandidateProfile p, Change change, Phase phase)
    {
        var path = change.Path;
        if (path == TestManifestPath)
            return phase == Phase.BeforeLanding && change.OldMode == RegularFile && change.NewMode == RegularFile
                ? TestManifestDelta(change.OldBytes, change.NewBytes)
                : (false, "test manifest change after the candidate PR landed, or a mode change (registered input)");

        var dataFile = IsDataFile(path) && change.Status == "A" && change.NewMode == RegularFile;
        if (path.StartsWith(p.Directory, StringComparison.Ordinal) || p.Files.Contains(path, StringComparer.Ordinal))
        {
            if (phase == Phase.BeforeLanding && IsRegularOrAbsent(change) && !HasDotSegment(path))
                return (true, $"{p.Release} candidate PR file (manifest, inputs, generator, classifier, or its tests); read by no build, workflow, package, or site");
            if (path.StartsWith(p.Reviews, StringComparison.Ordinal) && dataFile)
                return (true, $"{p.Release} candidate review record (data or text) added after the landing");
            return (false, $"changes the frozen {p.Release} candidate manifest or classifier after it landed, or a non-regular file: return to #{p.Issue}");
        }
        foreach (var prefix in p.Downstream)
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return dataFile
                    ? (true, "downstream evidence about the candidate (data or text)")
                    : (false, "downstream evidence directory: not a newly added regular data file (code, a non-regular file, or a modified or deleted record)");

        var category = Categories.FirstOrDefault(c => path.StartsWith(c.Prefix, StringComparison.Ordinal)).Category;
        return (false, category ?? RootCategory(path));
    }

    private static string RootCategory(string path) => path switch
    {
        "global.json" or "NuGet.Config" or "nuget.config" => "SDK or package-source pin (environment)",
        "CHANGELOG.md" or "README.md" => "publication content (release notes, package readme)",
        ".gitmodules" => "corpus submodule configuration",
        ".gitattributes" or ".editorconfig" or ".gitignore" => "checkout or build configuration",
        _ when path.StartsWith("Directory.", StringComparison.Ordinal) => "MSBuild configuration or central package versions (dependencies)",
        _ when path.EndsWith("packages.lock.json", StringComparison.Ordinal) => "dependency lockfile",
        _ => "unlisted path (default deny)",
    };

    private static bool HasDotSegment(string path) => path.Split('/').Any(s => s.StartsWith('.'));

    private static bool IsDataFile(string path)
        => DataExtensions.Any(e => path.EndsWith(e, StringComparison.Ordinal)) && !HasDotSegment(path);

    private static bool IsRegularOrAbsent(Change change)
        => change.NewMode is RegularFile or "000000" && change.OldMode is RegularFile or "000000";

    // ---- Git driver (same contract as the 0.24 classifier) ----

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>Classifies every commit in candidate..target. Fails closed: errors give UNDECIDED.</summary>
    public static Report ClassifyRange(GitRepo git, CandidateProfile profile, string candidate, string target)
    {
        var errors = new List<string>();
        if (git.IsShallow() != false) errors.Add("shallow clone or not a git repository: fail, never skip");
        if (!FullSha.IsMatch(candidate) || !git.CommitExists(candidate)) errors.Add($"candidate {candidate} is not a full SHA present here");
        var targetSha = git.RevParse(target + "^{commit}");
        if (targetSha == null) errors.Add($"target {target} does not resolve");
        if (errors.Count > 0) return new Report(Undecided, null, Array.Empty<CommitReport>(), errors);
        switch (git.Run("merge-base", "--is-ancestor", candidate, targetSha!).ExitCode)
        {
            case 0: break;
            case 1: return new Report(Invalid, null, Array.Empty<CommitReport>(), new[] { $"target {targetSha} does not contain the candidate" });
            default: return new Report(Undecided, null, Array.Empty<CommitReport>(), new[] { $"git merge-base cannot read the history from {candidate} to {targetSha}" });
        }

        var landing = FindLanding(git, profile, candidate, errors);
        var reports = new List<CommitReport>();
        var (revExit, revs, revError) = git.Run("rev-list", "--parents", $"{candidate}..{targetSha}");
        if (revExit != 0) errors.Add($"git rev-list {candidate}..{targetSha} failed: {revError.Trim()}");
        foreach (var line in revs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ids = line.Split(' ');
            var phase = landing != null && !git.IsAncestor(ids[0], landing) ? Phase.AfterLanding : Phase.BeforeLanding;
            foreach (var parent in ids.Length > 1 ? ids.Skip(1) : new[] { EmptyTree })
                reports.Add(new CommitReport(ids[0], parent, phase,
                    Diff(git, parent, ids[0], errors).Select(c => Classify(profile, c, phase)).ToList()));
        }
        var verdict = errors.Count > 0 ? Undecided
            : reports.Any(r => r.Decisions.Any(d => !d.Exempt)) ? Invalid : NotInvalidated;
        return new Report(verdict, landing, reports, errors);
    }

    /// <summary>The first first-parent commit of protected main after the candidate that holds the manifest.</summary>
    public static string? FindLanding(GitRepo git, CandidateProfile profile, string candidate, List<string> errors)
    {
        if (git.RevParse(DurableProvenance.ProtectedMainRef + "^{commit}") is not { } main)
        {
            errors.Add($"{DurableProvenance.ProtectedMainRef} is not fetched");
            return null;
        }
        switch (git.Run("merge-base", "--is-ancestor", candidate, main).ExitCode)
        {
            case 0: break;
            case 1: return null;
            default:
                errors.Add($"git merge-base cannot read the history from {candidate} to {DurableProvenance.ProtectedMainRef}");
                return null;
        }
        var (exit, log, error) = git.Run("rev-list", "--first-parent", "--reverse", $"{candidate}..{main}");
        if (exit != 0)
        {
            errors.Add($"git rev-list --first-parent {candidate}..{main} failed: {error.Trim()}");
            return null;
        }
        return log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(c => git.Run("cat-file", "-e", $"{c}:{profile.ManifestPath}").ExitCode == 0);
    }

    private static IEnumerable<Change> Diff(GitRepo git, string parent, string commit, List<string> errors)
    {
        var (exit, raw, error) = git.Run("diff-tree", "-r", "-z", "--no-renames", "--raw", "--no-ext-diff", "--ignore-submodules=none", parent, commit);
        if (exit != 0)
        {
            errors.Add($"git diff-tree {parent} {commit} failed: {error.Trim()}");
            yield break;
        }
        var fields = raw.Split('\0');
        for (var i = 0; i + 1 < fields.Length; i += 2)
        {
            var meta = fields[i].TrimStart(':').Split(' ');
            if (meta.Length < 5) continue;
            var oldBytes = meta[0] is "000000" or "160000" ? null : git.ReadBlob(meta[2]);
            var newBytes = meta[1] is "000000" or "160000" ? null : git.ReadBlob(meta[3]);
            yield return new Change(meta[4], fields[i + 1], meta[0], meta[1], oldBytes, newBytes);
        }
    }
}
