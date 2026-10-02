using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Calor.Compiler.Tests.Provenance;

/// <summary>
/// #1417 / v0.24 contract §6: a durable provenance identity is
/// <c>{commit, resolvedVia, treeHashes, inputContentHashes}</c>. This verifier decides whether
/// one resolves the way a third party's fresh clone would resolve it.
///
/// <para>It never consults <c>HEAD</c>, a <c>refs/pull/N/merge</c> ref, a branch, or a tag: only
/// fetched protected <c>main</c>. In pull-request CI <c>HEAD</c> is the merge ref, so the #1199
/// test's "reachable from HEAD" accepted a commit the squash merge then discarded. §6 also allows
/// an immutable release tag; this repository's <c>v*</c> tags are lightweight and unprotected, so
/// none qualifies, and the stricter rule is used.</para>
///
/// <para>It never skips. A shallow clone, a clone without <c>refs/remotes/origin/main</c>, or a
/// missing object is a finding.</para>
/// </summary>
public static class DurableProvenance
{
    public const string ProtectedMainRef = "refs/remotes/origin/main";
    /// <summary>treeHashes key for the whole repository tree at the commit.</summary>
    public const string RootTree = "/";

    public const string BasisIdenticalSrcTree = "identical-src-tree";
    public const string BasisMeasuredOnTopOf = "measured-on-top-of";
    public const string BasisLandingCommitOnly = "landing-commit-only";
    public const string BasisUniquePrefixExpansion = "unique-prefix-expansion";
    public const string BasisPostMergeWriteBack = "post-merge-write-back";

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex ShortSha = new("^[0-9a-f]{7,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Finding codes. Each negative control asserts its own code, so a test cannot pass because an
    /// unrelated rule fired.
    /// </summary>
    public static class Codes
    {
        public const string MissingIdentity = "P001";     // no durableIdentity, or unknown status
        public const string NotFullSha = "P002";          // commit is not a full 40-hex SHA
        public const string ForbiddenRef = "P003";        // resolvedVia is not protected main
        public const string RefNotFetched = "P004";       // origin/main is not present in this clone
        public const string NotDurable = "P005";          // commit absent or not an ancestor of main
        public const string ShallowClone = "P006";        // shallow clone or not a git repository
        public const string TreeMismatch = "P007";        // treeHashes missing (incl. "/"), malformed, or wrong
        public const string ContentMismatch = "P008";     // inputContentHashes missing, malformed, or wrong
        public const string FalseIdenticalTree = "P009";  // identical-src-tree claim not verifiable or false
        public const string OnTopOfMismatch = "P010";     // measured-on-top-of base or landing record wrong
        public const string NotLandingCommit = "P011";    // landing-commit-only commit did not land the artifact
        public const string BadPrefixExpansion = "P012";  // short stamp does not expand uniquely to the commit
        public const string WriteBackInvalid = "P013";    // pending/write-back record malformed or inputs differ
        public const string UnknownBasis = "P014";
        public const string LegacyDisagrees = "P015";     // #1199 resolvableOnMain disagrees with the identity
    }

    public sealed record Finding(string Code, string Subject, string Message)
    {
        public override string ToString() => $"{Code} {Subject}: {Message}";
    }

    public sealed class Result
    {
        public List<Finding> Findings { get; } = new();
        /// <summary>Subjects whose complete identity verified with no finding.</summary>
        public List<string> Authoritative { get; } = new();
        /// <summary>Subjects carrying only a phase-1 identity. Never authoritative (§6).</summary>
        public List<string> Pending { get; } = new();
    }

    /// <summary>The repository-relative artifact path an index entry speaks for.</summary>
    public static string ArtifactPath(JsonElement entry)
        => entry.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : "bench/phase0-agent-native/" + entry.GetProperty("ledger").GetString();

    /// <summary>JSON pointer of the stamp inside the artifact; ledgers use a top-level measuredCommit.</summary>
    public static string? StampPointer(JsonElement entry)
        => entry.TryGetProperty("stampPointer", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    public static string Subject(JsonElement entry)
        => StampPointer(entry) is { } pointer ? ArtifactPath(entry) + "#" + pointer : ArtifactPath(entry);

    public static Result Verify(GitRepo git, IEnumerable<JsonElement> entries)
    {
        var result = new Result();
        var list = entries.ToList();

        var shallow = git.IsShallow();
        if (shallow != false)
        {
            var why = shallow == true
                ? "the clone is shallow, so no identity can be decided here. Authoritative " + "validation fails rather than skips (§6); fetch full history (fetch-depth: 0)."
                : "not a git repository, or git is unavailable.";
            foreach (var entry in list)
                result.Findings.Add(new Finding(Codes.ShallowClone, Subject(entry), why));
            if (list.Count == 0)
                result.Findings.Add(new Finding(Codes.ShallowClone, "(clone)", why));
            return result;
        }

        var mainTip = git.RevParse(ProtectedMainRef + "^{commit}");
        foreach (var entry in list)
            VerifyEntry(git, entry, mainTip, result);
        return result;
    }

    private static void VerifyEntry(GitRepo git, JsonElement entry, string? mainTip, Result result)
    {
        var subject = Subject(entry);
        var findings = new List<Finding>();
        void Fail(string code, string message) => findings.Add(new Finding(code, subject, message));

        var basis = Str(entry, "basis");
        var measured = Str(entry, "measuredCommit");
        var status = entry.TryGetProperty("durableIdentity", out var identity)
            && identity.ValueKind == JsonValueKind.Object ? Str(identity, "status") : null;

        if (status == "pending")
        {
            VerifyPending(git, entry, identity, basis, measured, Fail);
            result.Pending.Add(subject);
        }
        else if (status != "complete")
        {
            Fail(Codes.MissingIdentity, $"durableIdentity is missing or has status '{status}'; " + "a stamp without a complete identity is not authoritative.");
        }
        else if (Str(identity, "commit") is not { } commit || !FullSha.IsMatch(commit))
        {
            Fail(Codes.NotFullSha, $"commit '{Str(identity, "commit")}' is not a full lowercase 40-hex SHA.");
        }
        else
        {
            VerifyComplete(git, entry, identity, commit, basis, measured, mainTip, Fail);
            if (findings.Count == 0) result.Authoritative.Add(subject);
        }
        result.Findings.AddRange(findings);
    }

    private static void VerifyComplete(
        GitRepo git, JsonElement entry, JsonElement identity, string commit, string? basis,
        string? measured, string? mainTip, Action<string, string> fail)
    {
        VerifyOnMain(git, commit, Str(identity, "resolvedVia"), mainTip, fail);
        var trees = ReadHashMap(identity, "treeHashes", FullSha, "treeHashes", Codes.TreeMismatch, fail);
        if (!trees.ContainsKey(RootTree))
            fail(Codes.TreeMismatch, "treeHashes has no \"/\" (whole repository tree); every input " + "an identity speaks for must be covered, not only the compiler.");
        CheckTrees(git, commit, trees, Codes.TreeMismatch, "treeHashes", fail);
        var contents = ReadHashMap(identity, "inputContentHashes", Sha256Hex, "inputContentHashes",
            Codes.ContentMismatch, fail);
        CheckContents(git, commit, contents, Codes.ContentMismatch, "inputContentHashes", fail);

        switch (basis)
        {
            case BasisIdenticalSrcTree:
                VerifyIdenticalTree(git, entry, commit, measured, trees, fail);
                break;
            case BasisMeasuredOnTopOf:
                VerifyOnTopOf(git, entry, commit, measured, mainTip, fail);
                break;
            case BasisLandingCommitOnly:
                var here = git.RevParse($"{commit}:{ArtifactPath(entry)}");
                if (here == null || here == git.RevParse($"{commit}^1:{ArtifactPath(entry)}"))
                    fail(Codes.NotLandingCommit, $"{commit} does not change {ArtifactPath(entry)} " + "relative to its first parent, so it is not the commit that landed it.");
                break;
            case BasisUniquePrefixExpansion:
                VerifyPrefixExpansion(git, measured, commit, fail);
                break;
            case BasisPostMergeWriteBack:
                VerifyWriteBack(git, entry, identity, commit, measured, trees, contents, fail);
                break;
            default:
                fail(Codes.UnknownBasis, $"unknown basis '{basis}'.");
                break;
        }

        var legacy = Str(entry, "resolvableOnMain");
        if (legacy != null && legacy != commit)
            fail(Codes.LegacyDisagrees, $"resolvableOnMain {legacy} (#1199) disagrees with durableIdentity.commit {commit}.");
    }

    private static void VerifyOnMain(
        GitRepo git, string commit, string? resolvedVia, string? mainTip, Action<string, string> fail)
    {
        if (resolvedVia != ProtectedMainRef)
            fail(Codes.ForbiddenRef, $"resolvedVia '{resolvedVia}' is not {ProtectedMainRef}. HEAD, " + "pull-request refs, branches, and (unprotected, lightweight) tags are rejected.");
        else if (mainTip == null)
            fail(Codes.RefNotFetched, $"{ProtectedMainRef} is not present: this clone has not fetched " + "protected main, so durability cannot be decided. Run `git fetch origin main`.");
        else if (!IsOnMain(git, commit, mainTip))
            fail(Codes.NotDurable, $"{commit} is absent or not an ancestor of {ProtectedMainRef}; a " + "commit reachable only from a branch, a pull-request merge ref, or HEAD is not durable (#1159).");
    }

    private static bool IsOnMain(GitRepo git, string commit, string mainTip)
        => git.CommitExists(commit) && git.IsAncestor(commit, mainTip);

    private static void VerifyIdenticalTree(
        GitRepo git, JsonElement entry, string commit, string? measured,
        Dictionary<string, string> trees, Action<string, string> fail)
    {
        var claims = entry.TryGetProperty("measuredTreeHashes", out var m) && m.ValueKind == JsonValueKind.Object
            ? m.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null)
            : new Dictionary<string, string?>();
        if (!claims.ContainsKey("src"))
        {
            fail(Codes.FalseIdenticalTree, "basis identical-src-tree without a measuredTreeHashes[\"src\"] " + "claim; the src tree is what the basis asserts.");
            return;
        }
        if (measured == null || !FullSha.IsMatch(measured) || !git.CommitExists(measured))
        {
            fail(Codes.FalseIdenticalTree, $"measuredCommit {measured} does not resolve here, so an " + "identical tree cannot be verified. Reachability alone is insufficient (§6).");
            return;
        }
        foreach (var (path, recorded) in claims)
        {
            var atMeasured = TreeAt(git, measured, path);
            var atDurable = TreeAt(git, commit, path);
            if (recorded == null || atMeasured != recorded || atDurable != recorded
                || !trees.TryGetValue(path, out var durableClaim) || durableClaim != recorded)
                fail(Codes.FalseIdenticalTree, $"identical {path} tree is false: recorded {recorded}, " + $"measured {measured} holds {atMeasured ?? "nothing"}, durable {commit} holds {atDurable ?? "nothing"}.");
        }
    }

    /// <summary>
    /// measured-on-top-of: the stamp names the base the measurement sat on, which must be the
    /// durable commit. The measured working tree was never committed, so the entry must also name the
    /// commit that landed the artifact (on main, descending from the base, holding the current
    /// artifact bytes, which its first parent does not). That pins where the repair landed; it does
    /// not prove the working tree equalled it.
    /// </summary>
    private static void VerifyOnTopOf(
        GitRepo git, JsonElement entry, string commit, string? measured, string? mainTip, Action<string, string> fail)
    {
        if (measured != commit)
            fail(Codes.OnTopOfMismatch, $"measured on top of {measured}, but the durable identity names {commit}.");
        if (!entry.TryGetProperty("landing", out var landing) || landing.ValueKind != JsonValueKind.Object
            || Str(landing, "commit") is not { } landed || !FullSha.IsMatch(landed)
            || Str(landing, "artifactSha256") is not { } artifactSha || !Sha256Hex.IsMatch(artifactSha))
        {
            fail(Codes.OnTopOfMismatch, "measured-on-top-of needs landing {commit, artifactSha256, treeHashes}.");
            return;
        }
        if (mainTip == null || !IsOnMain(git, landed, mainTip))
            fail(Codes.OnTopOfMismatch, $"landing commit {landed} is not on {ProtectedMainRef}.");
        else if (!git.IsAncestor(commit, landed))
            fail(Codes.OnTopOfMismatch, $"base {commit} is not an ancestor of landing commit {landed}.");
        CheckLanding(git, ArtifactPath(entry), landed, artifactSha, Codes.OnTopOfMismatch, fail);
        CheckCommittedArtifact(git, ArtifactPath(entry), artifactSha, Codes.OnTopOfMismatch, "landing.artifactSha256", fail);
        var trees = ReadHashMap(landing, "treeHashes", FullSha, "landing.treeHashes", Codes.OnTopOfMismatch, fail);
        CheckTrees(git, landed, trees, Codes.OnTopOfMismatch, "landing.treeHashes", fail);
    }

    private static void VerifyPrefixExpansion(GitRepo git, string? stamp, string commit, Action<string, string> fail)
    {
        if (stamp == null || !ShortSha.IsMatch(stamp) || !commit.StartsWith(stamp, StringComparison.Ordinal))
        {
            fail(Codes.BadPrefixExpansion, $"stamp '{stamp}' is not a 7-39 hex prefix of {commit}.");
            return;
        }
        var (exit, output, _) = git.Run("rev-parse", "--disambiguate=" + stamp);
        var matches = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (exit != 0 || matches.Length != 1 || matches[0] != commit)
            fail(Codes.BadPrefixExpansion, $"stamp '{stamp}' names {matches.Length} objects in this clone; " + "a prefix expansion is only an identity when it is unique.");
    }

    /// <summary>
    /// A completed §6(b) identity keeps its phase-1 record. The landed commit must hold every
    /// phase-1 tree and manifest unchanged, and must be the commit that landed the phase-1 artifact.
    /// </summary>
    private static void VerifyWriteBack(
        GitRepo git, JsonElement entry, JsonElement identity, string commit, string? measured,
        Dictionary<string, string> trees, Dictionary<string, string> contents, Action<string, string> fail)
    {
        if (!identity.TryGetProperty("writeBack", out var wb) || wb.ValueKind != JsonValueKind.Object)
        {
            fail(Codes.WriteBackInvalid, "basis post-merge-write-back without a writeBack record.");
            return;
        }
        var phase1 = ReadPhase1(git, entry, wb, "writeBack.phase1", measured, fail);
        foreach (var (path, value) in phase1.Trees)
            if (!trees.TryGetValue(path, out var landed) || landed != value)
                fail(Codes.WriteBackInvalid, $"phase-1 tree {path} {value} is not the landed tree; " + "the merge changed a measured input.");
        foreach (var (path, value) in phase1.Contents)
            if (!contents.TryGetValue(path, out var landed) || landed != value)
                fail(Codes.WriteBackInvalid, $"phase-1 manifest {path} {value} is not the landed manifest; " + "the merge changed a measured input.");
        if (phase1.ArtifactSha256 != null)
            CheckLanding(git, ArtifactPath(entry), commit, phase1.ArtifactSha256, Codes.WriteBackInvalid, fail);
    }

    private static void VerifyPending(
        GitRepo git, JsonElement entry, JsonElement identity, string? basis, string? measured,
        Action<string, string> fail)
    {
        if (basis != BasisPostMergeWriteBack)
            fail(Codes.WriteBackInvalid, $"a pending identity requires basis {BasisPostMergeWriteBack}, not '{basis}'.");
        if (!identity.TryGetProperty("phase1", out var p1) || p1.ValueKind != JsonValueKind.Object)
        {
            fail(Codes.WriteBackInvalid, "pending identity without a phase1 record.");
            return;
        }
        var phase1 = ReadPhase1(git, entry, p1, "phase1", measured, fail);
        if (phase1.ArtifactSha256 != null)
            CheckCommittedArtifact(git, ArtifactPath(entry), phase1.ArtifactSha256, Codes.WriteBackInvalid,
                "phase1.artifactSha256", fail);
    }

    private sealed record Phase1(
        string? Head, Dictionary<string, string> Trees, Dictionary<string, string> Contents, string? ArtifactSha256);

    /// <summary>
    /// Reads a phase-1 record (pending <c>phase1</c>, or a completed identity's <c>writeBack</c>,
    /// whose fields carry a <c>phase1</c> prefix). While the head commit still exists, its trees and
    /// manifests are checked too; after a squash merge it usually does not.
    /// </summary>
    private static Phase1 ReadPhase1(GitRepo git, JsonElement record, JsonElement p1, string label, string? measured, Action<string, string> fail)
    {
        var prefixed = label.StartsWith("writeBack", StringComparison.Ordinal);
        string Key(string name) => prefixed ? "phase1" + char.ToUpperInvariant(name[0]) + name[1..] : name;
        const string code = Codes.WriteBackInvalid;

        var head = Str(p1, Key("headCommit"));
        if (head == null || !FullSha.IsMatch(head) || head != measured)
            fail(code, $"{label} head '{head}' must be the full measuredCommit '{measured}'.");
        if (!p1.TryGetProperty("pr", out var pr) || pr.ValueKind != JsonValueKind.Number)
            fail(code, $"{label} pr is missing.");
        var trees = ReadHashMap(p1, Key("treeHashes"), FullSha, label + " treeHashes", code, fail);
        if (trees.ContainsKey(RootTree))
            fail(code, $"{label} treeHashes names \"/\"; the whole tree always changes when the artifact " + "is committed, so phase 1 claims only the measured inputs.");
        var contents = ReadHashMap(p1, Key("inputContentHashes"), Sha256Hex, label + " inputContentHashes", code, fail);
        var artifactSha = Str(p1, Key("artifactSha256"));
        if (artifactSha == null || !Sha256Hex.IsMatch(artifactSha))
        {
            fail(code, $"{label} artifactSha256 is not a SHA-256.");
            artifactSha = null;
        }
        if (head != null && FullSha.IsMatch(head) && git.CommitExists(head))
        {
            CheckTrees(git, head, trees, code, label + " treeHashes at the phase-1 head", fail);
            CheckContents(git, head, contents, code, label + " inputContentHashes at the phase-1 head", fail);
        }
        return new Phase1(head, trees, contents, artifactSha);
    }

    /// <summary>
    /// Phase 2 of §6(b): find the first-parent commit on protected main that landed the phase-1
    /// artifact, and re-verify every phase-1 tree and manifest there. Returns <c>(null, null)</c>
    /// while the change has not landed. Returns a finding — never a weaker identity — when an input
    /// changed in the merge: the numbers must then be re-measured.
    /// </summary>
    public static (JsonObject? Completed, Finding? Failure) CompleteWriteBack(GitRepo git, JsonElement entry)
    {
        var subject = Subject(entry);
        var p1 = entry.GetProperty("durableIdentity").GetProperty("phase1");
        var artifactPath = ArtifactPath(entry);
        var artifactSha = p1.GetProperty("artifactSha256").GetString()!;
        var mainTip = git.RevParse(ProtectedMainRef + "^{commit}");
        if (git.IsShallow() != false || mainTip == null)
            return (null, new Finding(Codes.RefNotFetched, subject, "write-back needs a full clone with fetched protected main."));

        var (_, log, _) = git.Run("log", "--first-parent", "--format=%H", ProtectedMainRef, "--", artifactPath);
        var landing = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()
            .FirstOrDefault(c => BlobSha(git, c, artifactPath) == artifactSha);
        if (landing == null) return (null, null);

        var changed = new List<string>();
        var trees = p1.GetProperty("treeHashes");
        var contents = p1.GetProperty("inputContentHashes");
        foreach (var t in trees.EnumerateObject())
            if (TreeAt(git, landing, t.Name) != t.Value.GetString()) changed.Add(t.Name);
        foreach (var c in contents.EnumerateObject())
            if (BlobSha(git, landing, c.Name) != c.Value.GetString()) changed.Add(c.Name);
        if (changed.Count > 0)
            return (null, new Finding(Codes.WriteBackInvalid, subject, $"landed at {landing}, but " + $"{string.Join(", ", changed)} differ from phase 1. The merge changed a measured input."));

        var landedTrees = JsonNode.Parse(trees.GetRawText())!.AsObject();
        landedTrees[RootTree] = TreeAt(git, landing, RootTree);
        var completed = new JsonObject
        {
            ["status"] = "complete",
            ["commit"] = landing,
            ["resolvedVia"] = ProtectedMainRef,
            ["treeHashes"] = landedTrees,
            ["inputContentHashes"] = JsonNode.Parse(contents.GetRawText()),
            ["writeBack"] = new JsonObject
            {
                ["phase1HeadCommit"] = p1.GetProperty("headCommit").GetString(),
                ["pr"] = p1.GetProperty("pr").GetInt32(),
                ["phase1TreeHashes"] = JsonNode.Parse(trees.GetRawText()),
                ["phase1InputContentHashes"] = JsonNode.Parse(contents.GetRawText()),
                ["phase1ArtifactSha256"] = artifactSha,
            },
        };
        return (completed, null);
    }

    /// <summary>The commit holds the artifact bytes and its first parent does not: it landed them.</summary>
    private static void CheckLanding(GitRepo git, string artifactPath, string commit, string artifactSha, string code, Action<string, string> fail)
    {
        if (BlobSha(git, commit, artifactPath) != artifactSha || BlobSha(git, commit + "^1", artifactPath) == artifactSha)
            fail(code, $"{commit} is not the commit that landed {artifactPath} with SHA-256 {artifactSha}.");
    }

    /// <summary>The checked-out artifact (git's index, not HEAD) must be the one the record describes.</summary>
    private static void CheckCommittedArtifact(GitRepo git, string artifactPath, string artifactSha, string code, string label, Action<string, string> fail)
    {
        var bytes = git.ReadBlob(":" + artifactPath);
        if (bytes == null || Sha256(bytes) != artifactSha)
            fail(code, $"the committed {artifactPath} does not hash to {label}; the artifact changed after it was recorded.");
    }

    private static void CheckTrees(GitRepo git, string commit, Dictionary<string, string> trees, string code, string label, Action<string, string> fail)
    {
        foreach (var (path, expected) in trees)
            if (TreeAt(git, commit, path) is var actual && actual != expected)
                fail(code, $"{label}[{path}] claims {expected}, but {commit} holds {actual ?? "nothing"} there.");
    }

    private static void CheckContents(GitRepo git, string commit, Dictionary<string, string> hashes, string code, string label, Action<string, string> fail)
    {
        foreach (var (path, expected) in hashes)
            if (BlobSha(git, commit, path) is var actual && actual != expected)
                fail(code, $"{label}[{path}] claims {expected}, but the file at {commit} hashes to {actual ?? "nothing (absent)"}.");
    }

    private static string? TreeAt(GitRepo git, string commit, string path)
        => git.RevParse(path == RootTree ? commit + "^{tree}" : $"{commit}:{path}");

    private static string? BlobSha(GitRepo git, string commit, string path)
        => git.ReadBlob($"{commit}:{path}") is { } bytes ? Sha256(bytes) : null;

    private static Dictionary<string, string> ReadHashMap(
        JsonElement owner, string property, Regex shape, string label, string code, Action<string, string> fail)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!owner.TryGetProperty(property, out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            fail(code, $"{label} is missing.");
            return map;
        }
        foreach (var item in obj.EnumerateObject())
        {
            var value = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null;
            if (value == null || !shape.IsMatch(value))
                fail(code, $"{label}[{item.Name}] '{value}' is malformed.");
            else
                map[item.Name] = value;
        }
        if (!obj.EnumerateObject().Any())
            fail(code, $"{label} is empty; reachability alone is insufficient (§6).");
        return map;
    }

    private static string? Str(JsonElement owner, string property)
        => owner.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>Thin git CLI wrapper. Every call is explicit about the object it names.</summary>
public sealed class GitRepo
{
    private readonly IReadOnlyDictionary<string, string>? _environment;

    public GitRepo(string root, IReadOnlyDictionary<string, string>? environment = null)
    {
        Root = root;
        _environment = environment;
    }

    public string Root { get; }

    /// <summary>true, false, or null when this is not a git repository / git is unavailable.</summary>
    public bool? IsShallow()
    {
        var (exit, output, _) = Run("rev-parse", "--is-shallow-repository");
        if (exit != 0) return null;
        return output.Trim() switch { "true" => true, "false" => false, _ => null };
    }

    public string? RevParse(string spec)
    {
        var (exit, output, _) = Run("rev-parse", "--verify", "--quiet", "--end-of-options", spec);
        return exit == 0 ? output.Trim() : null;
    }

    public bool CommitExists(string sha) => Run("cat-file", "-e", sha + "^{commit}").ExitCode == 0;

    public bool IsAncestor(string ancestor, string descendant)
        => Run("merge-base", "--is-ancestor", ancestor, descendant).ExitCode == 0;

    public byte[]? ReadBlob(string spec)
    {
        var (exit, bytes, _) = RunBytes("cat-file", "blob", spec);
        return exit == 0 ? bytes : null;
    }

    public (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var (exit, bytes, error) = RunBytes(args);
        return (exit, System.Text.Encoding.UTF8.GetString(bytes), error);
    }

    private (int ExitCode, byte[] Output, string Error) RunBytes(params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (_environment != null)
            foreach (var (key, value) in _environment) info.Environment[key] = value;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("git could not be started");
        var error = process.StandardError.ReadToEndAsync();
        using var buffer = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(buffer);
        process.WaitForExit();
        return (process.ExitCode, buffer.ToArray(), error.GetAwaiter().GetResult());
    }
}
