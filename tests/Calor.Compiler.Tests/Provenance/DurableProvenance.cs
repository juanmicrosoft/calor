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
/// <para>What it never consults: <c>HEAD</c>, a <c>refs/pull/N/merge</c> ref, or any branch other
/// than fetched protected <c>main</c>. In pull-request CI <c>HEAD</c> is the merge ref, so the
/// #1199 test's "reachable from HEAD" accepted a commit that the squash merge then discarded —
/// the #1159 defect, one step removed.</para>
///
/// <para>What it never does: skip. A shallow clone, a clone without
/// <c>refs/remotes/origin/main</c>, or a missing object is a finding, so authoritative validation
/// fails in exactly the places it used to go quiet.</para>
/// </summary>
public static class DurableProvenance
{
    public const string ProtectedMainRef = "refs/remotes/origin/main";

    public const string BasisIdenticalSrcTree = "identical-src-tree";
    public const string BasisMeasuredOnTopOf = "measured-on-top-of";
    public const string BasisLandingCommitOnly = "landing-commit-only";
    public const string BasisUniquePrefixExpansion = "unique-prefix-expansion";
    public const string BasisPostMergeWriteBack = "post-merge-write-back";

    private static readonly Regex FullSha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex ShortSha = new("^[0-9a-f]{7,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ReleaseTagRef = new(@"^refs/tags/v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Finding codes. Each negative control asserts its own code, so a test cannot pass because an
    /// unrelated rule fired.
    /// </summary>
    public static class Codes
    {
        public const string MissingIdentity = "P001";     // no durableIdentity, or unknown status
        public const string NotFullSha = "P002";          // commit is not a full 40-hex SHA
        public const string ForbiddenRef = "P003";        // resolvedVia is HEAD, a PR ref, a branch, a non-release tag
        public const string RefNotFetched = "P004";       // origin/main or the tag is not present in this clone
        public const string NotDurable = "P005";          // commit absent, not an ancestor of main, or not the tag target
        public const string ShallowClone = "P006";        // shallow clone or not a git repository
        public const string TreeMismatch = "P007";        // treeHashes missing, malformed, or not what the commit holds
        public const string ContentMismatch = "P008";     // inputContentHashes missing, malformed, or wrong
        public const string FalseIdenticalTree = "P009";  // identical-src-tree claim not verifiable or false
        public const string OnTopOfMismatch = "P010";     // measured-on-top-of names a different base
        public const string NotLandingCommit = "P011";    // landing-commit-only commit did not change the artifact
        public const string BadPrefixExpansion = "P012";  // short stamp does not expand uniquely to the commit
        public const string WriteBackInvalid = "P013";    // pending/write-back record malformed or trees differ
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
                ? "the clone is shallow, so no identity can be decided here. Authoritative "
                  + "validation fails rather than skips (§6); fetch full history (fetch-depth: 0)."
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

        if (!entry.TryGetProperty("durableIdentity", out var identity)
            || identity.ValueKind != JsonValueKind.Object)
        {
            Fail(Codes.MissingIdentity, "no durableIdentity; a stamp without one is not authoritative.");
            result.Findings.AddRange(findings);
            return;
        }

        var status = Str(identity, "status");
        var basis = Str(entry, "basis");
        var measured = Str(entry, "measuredCommit");

        if (status == "pending")
        {
            VerifyPending(git, entry, identity, basis, measured, Fail);
            result.Pending.Add(subject);
            result.Findings.AddRange(findings);
            return;
        }
        if (status != "complete")
        {
            Fail(Codes.MissingIdentity, $"durableIdentity.status '{status}' is neither 'complete' nor 'pending'.");
            result.Findings.AddRange(findings);
            return;
        }

        var commit = Str(identity, "commit");
        if (commit == null || !FullSha.IsMatch(commit))
        {
            Fail(Codes.NotFullSha, $"commit '{commit}' is not a full lowercase 40-hex SHA.");
            result.Findings.AddRange(findings);
            return;
        }

        VerifyDurability(git, commit, Str(identity, "resolvedVia"), mainTip, Fail);
        var trees = VerifyTreeHashes(git, commit, identity, Fail);
        VerifyContentHashes(git, commit, identity, Fail);

        switch (basis)
        {
            case BasisIdenticalSrcTree:
                VerifyIdenticalTree(git, entry, commit, measured, trees, Fail);
                break;
            case BasisMeasuredOnTopOf:
                if (measured != commit)
                    Fail(Codes.OnTopOfMismatch,
                        $"basis {basis} says the numbers were measured on top of {measured}, "
                        + $"but the durable identity names {commit}.");
                break;
            case BasisLandingCommitOnly:
                VerifyLandingCommit(git, ArtifactPath(entry), commit, Fail);
                break;
            case BasisUniquePrefixExpansion:
                VerifyPrefixExpansion(git, measured, commit, Fail);
                break;
            case BasisPostMergeWriteBack:
                VerifyWriteBack(git, identity, measured, trees, Fail);
                break;
            default:
                Fail(Codes.UnknownBasis, $"unknown basis '{basis}'.");
                break;
        }

        var legacy = Str(entry, "resolvableOnMain");
        if (legacy != null && legacy != commit)
            Fail(Codes.LegacyDisagrees,
                $"resolvableOnMain {legacy} (#1199) disagrees with durableIdentity.commit {commit}.");

        if (findings.Count == 0) result.Authoritative.Add(subject);
        result.Findings.AddRange(findings);
    }

    private static void VerifyDurability(
        GitRepo git, string commit, string? resolvedVia, string? mainTip, Action<string, string> fail)
    {
        if (resolvedVia == ProtectedMainRef)
        {
            if (mainTip == null)
            {
                fail(Codes.RefNotFetched,
                    $"{ProtectedMainRef} is not present: this clone has not fetched protected main, "
                    + "so durability cannot be decided. Run `git fetch origin main`.");
                return;
            }
            if (!git.CommitExists(commit))
            {
                fail(Codes.NotDurable,
                    $"{commit} does not resolve in this clone. A stamp written on a branch names a "
                    + "commit the squash merge discards (#1159).");
                return;
            }
            if (!git.IsAncestor(commit, mainTip))
                fail(Codes.NotDurable,
                    $"{commit} resolves but is not an ancestor of {ProtectedMainRef}; a commit "
                    + "reachable only from a branch, a pull-request merge ref, or HEAD is not durable.");
            return;
        }

        if (resolvedVia != null && ReleaseTagRef.IsMatch(resolvedVia))
        {
            var target = git.RevParse(resolvedVia + "^{commit}");
            if (target == null)
                fail(Codes.RefNotFetched, $"{resolvedVia} is not present in this clone; fetch tags.");
            else if (target != commit)
                fail(Codes.NotDurable,
                    $"{resolvedVia} targets {target}, not {commit}. A release-tag identity is the "
                    + "tag's own target, not merely an ancestor of it.");
            return;
        }

        fail(Codes.ForbiddenRef,
            $"resolvedVia '{resolvedVia}' is not {ProtectedMainRef} or a release tag refs/tags/vX.Y.Z. "
            + "HEAD, pull-request refs, and branches are rejected (§6).");
    }

    private static Dictionary<string, string> VerifyTreeHashes(
        GitRepo git, string commit, JsonElement identity, Action<string, string> fail)
    {
        var trees = ReadHashMap(identity, "treeHashes", FullSha, "treeHashes", Codes.TreeMismatch, fail);
        foreach (var (path, expected) in trees)
        {
            var actual = git.RevParse($"{commit}:{path}");
            if (actual != expected)
                fail(Codes.TreeMismatch,
                    $"treeHashes[{path}] claims {expected}, but {commit} holds {actual ?? "nothing"} there.");
        }
        return trees;
    }

    private static void VerifyContentHashes(
        GitRepo git, string commit, JsonElement identity, Action<string, string> fail)
    {
        var hashes = ReadHashMap(identity, "inputContentHashes", Sha256Hex, "inputContentHashes",
            Codes.ContentMismatch, fail);
        foreach (var (path, expected) in hashes)
        {
            var bytes = git.ReadBlob($"{commit}:{path}");
            var actual = bytes == null ? null : Sha256(bytes);
            if (actual != expected)
                fail(Codes.ContentMismatch,
                    $"inputContentHashes[{path}] claims {expected}, but the file at {commit} hashes to "
                    + $"{actual ?? "nothing (absent)"}.");
        }
    }

    private static void VerifyIdenticalTree(
        GitRepo git, JsonElement entry, string commit, string? measured,
        Dictionary<string, string> trees, Action<string, string> fail)
    {
        if (!entry.TryGetProperty("measuredTreeHashes", out var measuredTrees)
            || measuredTrees.ValueKind != JsonValueKind.Object
            || !measuredTrees.EnumerateObject().Any())
        {
            fail(Codes.FalseIdenticalTree,
                "basis identical-src-tree names no measuredTreeHashes, so the claim is unverifiable.");
            return;
        }
        if (measured == null || !FullSha.IsMatch(measured) || !git.CommitExists(measured))
        {
            fail(Codes.FalseIdenticalTree,
                $"measuredCommit {measured} does not resolve here, so an identical tree cannot be "
                + "verified. Reachability alone is insufficient (§6).");
            return;
        }
        foreach (var claim in measuredTrees.EnumerateObject())
        {
            var path = claim.Name;
            var recorded = claim.Value.ValueKind == JsonValueKind.String ? claim.Value.GetString() : null;
            var atMeasured = git.RevParse($"{measured}:{path}");
            var atDurable = git.RevParse($"{commit}:{path}");
            if (recorded == null || atMeasured != recorded || atDurable != recorded
                || !trees.TryGetValue(path, out var durableClaim) || durableClaim != recorded)
            {
                fail(Codes.FalseIdenticalTree,
                    $"identical {path} tree is false: recorded {recorded}, measured {measured} holds "
                    + $"{atMeasured ?? "nothing"}, durable {commit} holds {atDurable ?? "nothing"}.");
            }
        }
    }

    private static void VerifyLandingCommit(GitRepo git, string artifactPath, string commit, Action<string, string> fail)
    {
        var here = git.RevParse($"{commit}:{artifactPath}");
        var parent = git.RevParse($"{commit}^1:{artifactPath}");
        if (here == null || here == parent)
            fail(Codes.NotLandingCommit,
                $"{commit} does not change {artifactPath} relative to its first parent, so it is not "
                + "the commit that landed it.");
    }

    private static void VerifyPrefixExpansion(GitRepo git, string? stamp, string commit, Action<string, string> fail)
    {
        if (stamp == null || !ShortSha.IsMatch(stamp) || !commit.StartsWith(stamp, StringComparison.Ordinal))
        {
            fail(Codes.BadPrefixExpansion,
                $"stamp '{stamp}' is not a 7-39 hex prefix of {commit}.");
            return;
        }
        var (exit, output, _) = git.Run("rev-parse", "--disambiguate=" + stamp);
        var matches = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (exit != 0 || matches.Length != 1 || matches[0] != commit)
            fail(Codes.BadPrefixExpansion,
                $"stamp '{stamp}' names {matches.Length} objects in this clone; a prefix expansion is "
                + "only an identity when it is unique.");
    }

    private static void VerifyWriteBack(
        GitRepo git, JsonElement identity, string? measured,
        Dictionary<string, string> trees, Action<string, string> fail)
    {
        if (!identity.TryGetProperty("writeBack", out var wb) || wb.ValueKind != JsonValueKind.Object)
        {
            fail(Codes.WriteBackInvalid, "basis post-merge-write-back without a writeBack record.");
            return;
        }
        var head = Str(wb, "phase1HeadCommit");
        if (head == null || !FullSha.IsMatch(head) || head != measured)
            fail(Codes.WriteBackInvalid,
                $"writeBack.phase1HeadCommit '{head}' must be the full measuredCommit '{measured}'.");
        if (!wb.TryGetProperty("pr", out var pr) || pr.ValueKind != JsonValueKind.Number)
            fail(Codes.WriteBackInvalid, "writeBack.pr is missing.");

        var phase1 = ReadHashMap(wb, "phase1TreeHashes", FullSha, "writeBack.phase1TreeHashes",
            Codes.WriteBackInvalid, fail);
        foreach (var (path, value) in phase1)
        {
            if (!trees.TryGetValue(path, out var landed) || landed != value)
                fail(Codes.WriteBackInvalid,
                    $"phase-1 tree {path} {value} differs from the landed tree "
                    + $"{(trees.TryGetValue(path, out var l) ? l : "(not recorded)")}; the merge changed a "
                    + "measured input.");
        }
        // The pre-squash head is usually gone after a squash merge; when it is still present (merge
        // commit, or the branch survives), its trees are checked too.
        if (head != null && FullSha.IsMatch(head) && git.CommitExists(head))
        {
            foreach (var (path, value) in phase1)
                if (git.RevParse($"{head}:{path}") != value)
                    fail(Codes.WriteBackInvalid, $"phase-1 head {head} does not hold {value} at {path}.");
        }
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
        var head = Str(p1, "headCommit");
        if (head == null || !FullSha.IsMatch(head) || head != measured)
            fail(Codes.WriteBackInvalid, $"phase1.headCommit '{head}' must be the full measuredCommit '{measured}'.");
        if (!p1.TryGetProperty("pr", out var pr) || pr.ValueKind != JsonValueKind.Number)
            fail(Codes.WriteBackInvalid, "phase1.pr is missing.");
        var trees = ReadHashMap(p1, "treeHashes", FullSha, "phase1.treeHashes", Codes.WriteBackInvalid, fail);
        ReadHashMap(p1, "inputContentHashes", Sha256Hex, "phase1.inputContentHashes", Codes.WriteBackInvalid, fail);
        var artifactSha = Str(p1, "artifactSha256");
        if (artifactSha == null || !Sha256Hex.IsMatch(artifactSha))
            fail(Codes.WriteBackInvalid, "phase1.artifactSha256 is not a SHA-256.");
        else
        {
            // The committed artifact (git's index, not HEAD) must be the one phase 1 describes.
            var bytes = git.ReadBlob(":" + ArtifactPath(entry));
            if (bytes == null || Sha256(bytes) != artifactSha)
                fail(Codes.WriteBackInvalid,
                    $"the committed {ArtifactPath(entry)} does not hash to phase1.artifactSha256.");
        }
        if (head != null && FullSha.IsMatch(head) && git.CommitExists(head))
        {
            foreach (var (path, value) in trees)
                if (git.RevParse($"{head}:{path}") != value)
                    fail(Codes.WriteBackInvalid, $"phase-1 head {head} does not hold {value} at {path}.");
        }
    }

    /// <summary>
    /// Phase 2 of §6(b): find the commit on protected main that landed the phase-1 artifact, and
    /// re-verify that every phase-1 tree is unchanged there. Returns <c>(null, null)</c> while the
    /// change has not landed. Returns a finding — never a weaker identity — when it landed with a
    /// different tree: the merge changed a measured input, so the numbers must be re-measured or a
    /// weaker basis recorded by hand in review.
    /// </summary>
    public static (JsonObject? Completed, Finding? Failure) CompleteWriteBack(GitRepo git, JsonElement entry)
    {
        var subject = Subject(entry);
        var identity = entry.GetProperty("durableIdentity");
        var p1 = identity.GetProperty("phase1");
        var artifactPath = ArtifactPath(entry);
        var artifactSha = p1.GetProperty("artifactSha256").GetString()!;
        var mainTip = git.RevParse(ProtectedMainRef + "^{commit}");
        if (git.IsShallow() != false || mainTip == null)
            return (null, new Finding(Codes.RefNotFetched, subject,
                "write-back needs a full clone with fetched protected main."));

        var (_, log, _) = git.Run("log", "--first-parent", "--format=%H", ProtectedMainRef, "--", artifactPath);
        var touching = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? landing = null;
        for (var i = touching.Length - 1; i >= 0; i--)
        {
            var bytes = git.ReadBlob($"{touching[i]}:{artifactPath}");
            if (bytes != null && Sha256(bytes) == artifactSha) { landing = touching[i]; break; }
        }
        if (landing == null) return (null, null);

        var phase1Trees = p1.GetProperty("treeHashes");
        foreach (var tree in phase1Trees.EnumerateObject())
        {
            var atLanding = git.RevParse($"{landing}:{tree.Name}");
            if (atLanding != tree.Value.GetString())
                return (null, new Finding(Codes.WriteBackInvalid, subject,
                    $"landed at {landing}, but {tree.Name} there is {atLanding ?? "absent"}, not the "
                    + $"phase-1 {tree.Value.GetString()}. The merge changed a measured input."));
        }

        var completed = new JsonObject
        {
            ["status"] = "complete",
            ["commit"] = landing,
            ["resolvedVia"] = ProtectedMainRef,
            ["treeHashes"] = JsonNode.Parse(phase1Trees.GetRawText()),
            ["inputContentHashes"] = JsonNode.Parse(p1.GetProperty("inputContentHashes").GetRawText()),
            ["writeBack"] = new JsonObject
            {
                ["phase1HeadCommit"] = p1.GetProperty("headCommit").GetString(),
                ["pr"] = p1.GetProperty("pr").GetInt32(),
                ["phase1TreeHashes"] = JsonNode.Parse(phase1Trees.GetRawText()),
            },
        };
        return (completed, null);
    }

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
        if (map.Count == 0 && !obj.EnumerateObject().Any())
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
