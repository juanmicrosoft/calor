using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calor.Compiler.Tests.Provenance;

namespace Calor.Compiler.Tests.EvidenceContract;

/// <summary>
/// #1423 (0.24 C1) invalidation classifier. Given the frozen candidate and a later commit, it
/// decides whether any change after the freeze invalidates the candidate and every downstream
/// result: a change to the compiler, verifier, evidence producers, registered inputs, workflow
/// logic, dependencies, environment, or publication content.
///
/// <para><b>Default deny.</b> Only the short list below is exempt, each a file no build, test host,
/// workflow, package, or site reads. Any other documentation is <c>INVALID</c>: docs feed
/// <c>calor self-check docs</c>, packages, and the website. <b>Every commit counts:</b> each commit
/// in <c>candidate..target</c> is diffed against each parent (renames off), so a reverted change
/// still invalidates. <b>Two phases:</b> before C1 lands (first first-parent commit of protected
/// main holding the manifest) C1's own files are exempt; after it, only newly added review records
/// are, so a later edit cannot weaken this classifier and exempt itself.</para>
/// </summary>
public static class CandidateInvalidation
{
    public const string ManifestPath = "docs/plans/evidence/c1-1423/candidate-manifest.json";
    public const string TestManifestPath = "eng/test-manifest.json";
    public const string CompilerTestsProject = "tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj";
    public const string Invalid = "INVALID", NotInvalidated = "NOT-INVALIDATED", Undecided = "UNDECIDED";

    public enum Phase { BeforeLanding, AfterLanding }

    /// <summary>The C1 pull request's own files: exempt before the landing commit only.</summary>
    public static readonly string[] C1Files =
    {
        "docs/plans/v0.24-c1-candidate.md", "tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs",
        "tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidationTests.cs", "tests/Calor.Compiler.Tests/EvidenceContract/CandidateManifestTests.cs",
    };
    public const string C1Directory = "docs/plans/evidence/c1-1423/", C1Reviews = C1Directory + "reviews/";

    /// <summary>Downstream records about the candidate (C2 regeneration, A1 adjudication); added data files only.</summary>
    public static readonly string[] DownstreamEvidence = { "docs/plans/evidence/c2-1424/", "docs/plans/evidence/adjudication-1408/" };

    /// <summary>Extensions that are data or text, never code, configuration, or a build input.</summary>
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
        ("eng/", "registered inventories and manifests"),
        ("website/", "publication content"),
        (".claude/", "release procedure (skills)"),
        ("docs/", "documentation (not proven inert)"),
        ("samples/", "samples (Tier 2 inputs)"),
    };

    public sealed record Change(string Status, string Path, string OldMode, string NewMode, byte[]? OldBytes, byte[]? NewBytes);

    public sealed record Decision(string Path, bool Exempt, string Reason);

    public sealed record CommitReport(string Commit, string Parent, Phase Phase, IReadOnlyList<Decision> Decisions);

    public sealed record Report(string Verdict, string? Landing, IReadOnlyList<CommitReport> Commits, IReadOnlyList<string> Errors)
    {
        public IEnumerable<Decision> Invalidating => Commits.SelectMany(c => c.Decisions).Where(d => !d.Exempt);

        public override string ToString()
        {
            var text = new StringBuilder($"{Verdict}; landing {Landing ?? "(not landed)"}; {Commits.Count} commit diffs");
            foreach (var e in Errors) text.Append(Environment.NewLine).Append("error: ").Append(e);
            foreach (var c in Commits)
                foreach (var d in c.Decisions)
                    text.Append(Environment.NewLine).Append($"{c.Commit[..8]} vs {c.Parent[..Math.Min(8, c.Parent.Length)]} [{c.Phase}] {(d.Exempt ? "exempt" : "INVALID")} {d.Path}: {d.Reason}");
            return text.ToString();
        }
    }

    // ---- Path rule (pure) ----

    public static Decision Classify(Change change, Phase phase)
    {
        var (exempt, reason) = ClassifyPath(change, phase);
        return new Decision(change.Path, exempt, reason);
    }

    private static (bool, string) ClassifyPath(Change change, Phase phase)
    {
        var path = change.Path;
        if (path == TestManifestPath)
            return phase == Phase.BeforeLanding && change.OldMode == RegularFile && change.NewMode == RegularFile
                ? TestManifestDelta(change.OldBytes, change.NewBytes)
                : (false, "test manifest change after C1 landed, or a mode change (registered input)");

        // After the landing, and in downstream directories, only a newly added data file is exempt:
        // a modified or deleted record may be a frozen input (C2's claim registry, A1's record).
        var dataFile = IsDataFile(path) && change.Status == "A" && change.NewMode == RegularFile;
        var c1Owned = path.StartsWith(C1Directory, StringComparison.Ordinal) || C1Files.Contains(path, StringComparer.Ordinal);
        if (c1Owned)
        {
            if (phase == Phase.BeforeLanding && IsRegularOrAbsent(change))
                return (true, "C1 pull request file (manifest, record, classifier, or its tests); read by no build, workflow, package, or site");
            if (path.StartsWith(C1Reviews, StringComparison.Ordinal) && dataFile)
                return (true, "C1 review record (data or text) added after the landing");
            return (false, "changes the frozen C1 manifest or classifier after it landed: return to #1423");
        }
        foreach (var prefix in DownstreamEvidence)
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return dataFile
                    ? (true, "downstream evidence about the candidate (data or text)")
                    : (false, "downstream evidence directory: not a newly added regular data file (code, a non-regular file, or a modified or deleted record)");

        var category = Categories.FirstOrDefault(c => path.StartsWith(c.Prefix, StringComparison.Ordinal)).Category;
        category ??= RootCategory(path);
        return (false, category);
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

    private static bool IsDataFile(string path)
        => DataExtensions.Any(e => path.EndsWith(e, StringComparison.Ordinal))
           && !path.Split('/').Any(s => s.StartsWith('.'));

    /// <summary>Only plain non-executable files (or a deletion) are exempt: no symlink, gitlink, or executable.</summary>
    private static bool IsRegularOrAbsent(Change change)
        => change.NewMode is RegularFile or "000000" && change.OldMode is RegularFile or "000000";

    /// <summary>
    /// eng/test-manifest.json may change only by the C1 bump: the Calor.Compiler.Tests
    /// expectedTotal rises and its note gains appended text; every other byte of the parsed JSON is
    /// unchanged. Test files that could account for the rise are themselves classified, so a rise
    /// can only come from exempt C1 tests when the verdict is NOT-INVALIDATED.
    /// </summary>
    public static (bool Exempt, string Reason) TestManifestDelta(byte[]? oldBytes, byte[]? newBytes)
    {
        const string Deny = "test manifest change beyond the C1 bump (registered input)";
        if (oldBytes == null || newBytes == null) return (false, Deny + ": added or deleted");
        JsonNode? before, after;
        try { (before, after) = (JsonNode.Parse(oldBytes), JsonNode.Parse(newBytes)); }
        catch (System.Text.Json.JsonException) { return (false, Deny + ": not JSON"); }
        var (oldProject, newProject) = (CompilerProject(before), CompilerProject(after));
        if (oldProject == null || newProject == null) return (false, Deny + ": compiler project missing");
        if (newProject["expectedTotal"]?.GetValue<int>() is not int newTotal
            || oldProject["expectedTotal"]?.GetValue<int>() is not int oldTotal || newTotal <= oldTotal)
            return (false, Deny + ": expectedTotal did not rise");
        var oldNote = oldProject["note"]?.GetValue<string>() ?? "";
        var newNote = newProject["note"]?.GetValue<string>() ?? "";
        if (!newNote.StartsWith(oldNote, StringComparison.Ordinal) || newNote.Length == oldNote.Length)
            return (false, Deny + ": note not append-only");
        foreach (var project in new[] { oldProject, newProject })
        {
            project.Remove("expectedTotal");
            project.Remove("note");
        }
        return JsonNode.DeepEquals(before, after)
            ? (true, $"C1 test-count bump only ({oldTotal} -> {newTotal})")
            : (false, Deny + ": other fields differ");
    }

    private static JsonObject? CompilerProject(JsonNode? manifest)
        => (manifest?["projects"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(p => p["path"]?.GetValue<string>() == CompilerTestsProject);

    // ---- Git driver ----

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

    /// <summary>Classifies every commit in candidate..target. Fails closed: errors give UNDECIDED.</summary>
    public static Report ClassifyRange(GitRepo git, string candidate, string target)
    {
        var errors = new List<string>();
        if (git.IsShallow() != false) errors.Add("shallow clone or not a git repository (contract §6: fail, never skip)");
        if (!FullSha.IsMatch(candidate) || !git.CommitExists(candidate)) errors.Add($"candidate {candidate} is not a full SHA present here");
        var targetSha = git.RevParse(target + "^{commit}");
        if (targetSha == null) errors.Add($"target {target} does not resolve");
        if (errors.Count > 0) return new Report(Undecided, null, Array.Empty<CommitReport>(), errors);
        if (!git.IsAncestor(candidate, targetSha!))
            return new Report(Invalid, null, Array.Empty<CommitReport>(), new[] { $"target {targetSha} does not contain the candidate" });

        var landing = FindLanding(git, candidate, errors);
        var reports = new List<CommitReport>();
        var (_, revs, _) = git.Run("rev-list", "--parents", $"{candidate}..{targetSha}");
        foreach (var line in revs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ids = line.Split(' ');
            var phase = landing != null && !git.IsAncestor(ids[0], landing) ? Phase.AfterLanding : Phase.BeforeLanding;
            var parents = ids.Length > 1 ? ids.Skip(1) : new[] { EmptyTree };
            foreach (var parent in parents)
                reports.Add(new CommitReport(ids[0], parent, phase,
                    Diff(git, parent, ids[0], errors).Select(c => Classify(c, phase)).ToList()));
        }
        var verdict = errors.Count > 0 ? Undecided
            : reports.Any(r => r.Decisions.Any(d => !d.Exempt)) ? Invalid : NotInvalidated;
        return new Report(verdict, landing, reports, errors);
    }

    /// <summary>The first first-parent commit of protected main after the candidate that holds the manifest.</summary>
    private static string? FindLanding(GitRepo git, string candidate, List<string> errors)
    {
        if (git.RevParse(DurableProvenance.ProtectedMainRef + "^{commit}") is not { } main)
        {
            errors.Add($"{DurableProvenance.ProtectedMainRef} is not fetched");
            return null;
        }
        if (!git.IsAncestor(candidate, main)) return null;
        var (_, log, _) = git.Run("rev-list", "--first-parent", "--reverse", $"{candidate}..{main}");
        return log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(c => git.Run("cat-file", "-e", $"{c}:{ManifestPath}").ExitCode == 0);
    }

    /// <summary>git's empty tree (SHA-1 object format, which this repository uses).</summary>
    private const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

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
            var path = fields[i + 1];
            var oldBytes = meta[0] == "000000" || meta[0] == "160000" ? null : git.ReadBlob(meta[2]);
            var newBytes = meta[1] == "000000" || meta[1] == "160000" ? null : git.ReadBlob(meta[3]);
            yield return new Change(meta[4], path, meta[0], meta[1], oldBytes, newBytes);
        }
    }
}
