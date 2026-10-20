using System.Text.RegularExpressions;
using Calor.Compiler.Tests.Provenance;
using static Calor.Compiler.Tests.EvidenceContract.CandidateInvalidation;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// The 0.24 C1 (#1423) classifier generalized by a <see cref="CandidateProfile"/>; the 0.24 one
/// (<see cref="CandidateInvalidation"/>) stays as it landed, since editing it would invalidate the 0.24
/// freeze. Same rules: default deny; exempt only the candidate PR's files before the landing, added
/// review records, the compiler-test count bump before the landing, and added data files in the
/// downstream directories (plain files, no dot-segment). Every commit in candidate..target is diffed
/// against each parent, so a reverted change still invalidates. See docs/plans/evidence/v0.25-candidate/.
/// </summary>
public static class ReleaseCandidateInvalidation
{
    /// <summary>A release's candidate: the issue a frozen-file change returns to, the manifest (its first commit on main
    /// is the landing), the evidence directory and other owned files, and the downstream record directories.</summary>
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
        // Scope amendment 1.4.0 (D-READY-EVIDENCE-DIRS): each family's READY record and evidence are added under its
        // evidence directory's ready/ until A1; scope.json and the T1 register are written back after A1.
        ["docs/plans/evidence/v0.25-regeneration/", "docs/plans/evidence/adjudication-1430/",
         "docs/plans/evidence/v0.25-f1-943/ready/", "docs/plans/evidence/v0.25-f2-906/ready/", "docs/plans/evidence/v0.25-f3-847/ready/",
         "docs/plans/evidence/v0.25-f4-1139/ready/", "docs/plans/evidence/v0.25-f5-1132/ready/", "docs/plans/evidence/v0.25-f6-1144/ready/"]);

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

    // ---- Git driver (same contract as the 0.24 classifier; every git failure gives UNDECIDED) ----

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>Runs one git command: (exit code, stdout, stderr).</summary>
    public delegate (int ExitCode, string Output, string Error) GitRun(params string[] args);

    /// <summary>
    /// Classifies every commit in candidate..target. Fails closed: errors give UNDECIDED. <paramref name="run"/>
    /// replaces <see cref="GitRepo.Run"/> for the history, landing and diff commands (fault-injection controls).
    /// </summary>
    public static Report ClassifyRange(GitRepo git, CandidateProfile profile, string candidate, string target, GitRun? run = null)
    {
        run ??= git.Run;
        var errors = new List<string>();
        if (git.IsShallow() != false) errors.Add("shallow clone or not a git repository: fail, never skip");
        if (!FullSha.IsMatch(candidate) || !git.CommitExists(candidate)) errors.Add($"candidate {candidate} is not a full SHA present here");
        var targetSha = git.RevParse(target + "^{commit}");
        if (targetSha == null) errors.Add($"target {target} does not resolve");
        if (errors.Count > 0) return new Report(Undecided, null, Array.Empty<CommitReport>(), errors);
        switch (run("merge-base", "--is-ancestor", candidate, targetSha!).ExitCode)
        {
            case 0: break;
            case 1: return new Report(Invalid, null, Array.Empty<CommitReport>(), new[] { $"target {targetSha} does not contain the candidate" });
            default: return new Report(Undecided, null, Array.Empty<CommitReport>(), new[] { $"git merge-base cannot read the history from {candidate} to {targetSha}" });
        }

        var landing = FindLanding(git, profile, candidate, errors, run);
        var reports = new List<CommitReport>();
        var (revExit, revs, revError) = run("rev-list", "--parents", $"{candidate}..{targetSha}");
        if (revExit != 0) errors.Add($"git rev-list {candidate}..{targetSha} failed: {revError.Trim()}");
        foreach (var line in revExit == 0 ? revs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [])
        {
            var ids = line.Split(' ');
            var inLanding = landing == null ? 0 : run("merge-base", "--is-ancestor", ids[0], landing).ExitCode;
            if (inLanding is not (0 or 1)) errors.Add($"git merge-base cannot place {ids[0]} relative to the landing {landing}");
            var phase = inLanding == 1 ? Phase.AfterLanding : Phase.BeforeLanding;
            foreach (var parent in ids.Length > 1 ? ids.Skip(1) : new[] { EmptyTree })
                reports.Add(new CommitReport(ids[0], parent, phase,
                    Diff(git, run, parent, ids[0], errors).Select(c => Classify(profile, c, phase)).ToList()));
        }
        var verdict = errors.Count > 0 ? Undecided
            : reports.Any(r => r.Decisions.Any(d => !d.Exempt)) ? Invalid : NotInvalidated;
        return new Report(verdict, landing, reports, errors);
    }

    /// <summary>
    /// The default target: the newest commit (topological order) in candidate..HEAD that changes one of the profile's
    /// own files (not a review record), each commit diffed against its first parent, so a merge counts. Any git failure
    /// is an error, never "none".
    /// </summary>
    public static string? NewestProfileCommit(GitRepo git, CandidateProfile profile, string candidate, List<string> errors, GitRun? run = null)
    {
        run ??= git.Run;
        var (exit, log, error) = run("rev-list", "--topo-order", "--parents", $"{candidate}..HEAD");
        if (exit != 0)
        {
            errors.Add($"git rev-list {candidate}..HEAD failed: {error.Trim()}");
            return null;
        }
        foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ids = line.Split(' ');
            var (diffExit, names, diffError) = run("diff-tree", "-r", "--name-only", "-z", "--no-renames", ids.Length > 1 ? ids[1] : EmptyTree, ids[0]);
            if (diffExit != 0)
            {
                errors.Add($"git diff-tree {ids[0]} failed: {diffError.Trim()}");
                return null;
            }
            if (names.Split('\0').Any(p => (p.StartsWith(profile.Directory, StringComparison.Ordinal) && !p.StartsWith(profile.Reviews, StringComparison.Ordinal))
                    || profile.Files.Contains(p, StringComparer.Ordinal)))
                return ids[0];
        }
        return null;
    }

    /// <summary>
    /// The first first-parent commit of protected main after the candidate that holds the manifest naming this
    /// candidate (its candidate.commit). A re-freeze keeps the path, so an earlier manifest naming another (superseded)
    /// candidate is not this candidate's landing. A commit's tree is listed to decide whether the path exists; an
    /// existing manifest whose blob cannot be read or parsed is an error, never "absent", so an unreadable landing cannot
    /// turn a frozen-file edit into a before-landing one.
    /// </summary>
    public static string? FindLanding(GitRepo git, CandidateProfile profile, string candidate, List<string> errors, GitRun? run = null)
    {
        run ??= git.Run;
        if (git.RevParse(DurableProvenance.ProtectedMainRef + "^{commit}") is not { } main)
        {
            errors.Add($"{DurableProvenance.ProtectedMainRef} is not fetched");
            return null;
        }
        switch (run("merge-base", "--is-ancestor", candidate, main).ExitCode)
        {
            case 0: break;
            case 1: return null;
            default:
                errors.Add($"git merge-base cannot read the history from {candidate} to {DurableProvenance.ProtectedMainRef}");
                return null;
        }
        var (exit, log, error) = run("rev-list", "--first-parent", "--reverse", $"{candidate}..{main}");
        if (exit != 0)
        {
            errors.Add($"git rev-list --first-parent {candidate}..{main} failed: {error.Trim()}");
            return null;
        }
        foreach (var commit in log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (lsExit, listed, lsError) = run("ls-tree", commit, "--", profile.ManifestPath);
            if (lsExit != 0)
            {
                errors.Add($"git ls-tree {commit} failed while looking for the landing: {lsError.Trim()}");
                return null;
            }
            if (string.IsNullOrWhiteSpace(listed)) continue;
            var blob = listed.Split('\t')[0].Split(' ').Last();
            var (catExit, text, _) = run("cat-file", "blob", blob);
            if (catExit != 0)
            {
                errors.Add($"the manifest {profile.ManifestPath} at {commit} exists but its blob {blob} cannot be read");
                return null;
            }
            string? named;
            try { named = System.Text.Json.Nodes.JsonNode.Parse(text)?["candidate"]?["commit"]?.GetValue<string>(); }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { named = null; }
            // Only a manifest naming a full commit SHA may be skipped as another candidate's; anything else is an error.
            if (named is null || !System.Text.RegularExpressions.Regex.IsMatch(named, "^[0-9a-f]{40}$"))
            {
                errors.Add($"the manifest {profile.ManifestPath} at {commit} does not name a full candidate.commit");
                return null;
            }
            if (named == candidate) return commit;
        }
        return null;
    }

    private static IEnumerable<Change> Diff(GitRepo git, GitRun run, string parent, string commit, List<string> errors)
    {
        var (exit, raw, error) = run("diff-tree", "-r", "-z", "--no-renames", "--raw", "--no-ext-diff", "--ignore-submodules=none", parent, commit);
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
            if ((oldBytes == null && meta[0] is not ("000000" or "160000")) || (newBytes == null && meta[1] is not ("000000" or "160000")))
                errors.Add($"a blob of {fields[i + 1]} in {commit} cannot be read");
            yield return new Change(meta[4], fields[i + 1], meta[0], meta[1], oldBytes, newBytes);
        }
    }
}
