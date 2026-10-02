using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// #1410 (milestone 0.24 R2) — every public release surface in the workflows consumes the
/// adjudication identity before it acts. These are structural checks over the committed workflow
/// files: no publishing workflow is reachable from a <c>release</c> event, each takes a required
/// identity input, and in every job a publishing command (NuGet push, GitHub release creation or
/// upload, Pages deployment, publication PR, push to a branch) comes after a run of the gate script
/// in the same job.
/// </summary>
public class ReleaseWorkflowGateTests
{
    private const string Verifier = "scripts/verify_release_adjudication.py";

    // Pinned inputs are re-materialized, never republished by a release (#1407 section 7); the
    // z3-binaries release is that pinned input, not a Calor release surface.
    private static readonly string[] Exempt = { "build-z3.yml" };

    private static readonly Regex Publishing = new(
        @"dotnet nuget push|gh release (?:create|upload|edit)|actions/deploy-pages@|peter-evans/create-pull-request@|git push|gh workflow run",
        RegexOptions.Compiled);

    public static TheoryData<string> GatedWorkflows => new()
    {
        "publish-nuget.yml", "nextjs-gh-pages.yml", "benchmark.yml", "verify-release.yml",
    };

    [Theory]
    [MemberData(nameof(GatedWorkflows))]
    public void SurfaceIsNotTriggeredByReleaseEvents(string workflow)
    {
        var triggers = Triggers(Read(workflow));
        Assert.DoesNotContain(triggers, t => t.StartsWith("release:", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(GatedWorkflows))]
    public void SurfaceRequiresTheAdjudicationIdentity(string workflow)
    {
        var text = Read(workflow);
        Assert.Matches(new Regex(@"\n      adjudication_identity:\n(?:        .*\n)*?        required: true\n"), text);
        Assert.Contains(Verifier, text);
    }

    [Fact]
    public void EveryPublishingCommandFollowsTheGateInItsJob()
    {
        var violations = new List<string>();
        var publishing = 0;
        foreach (var path in Directory.GetFiles(WorkflowDir(), "*.yml"))
        {
            var name = Path.GetFileName(path);
            if (Exempt.Contains(name)) continue;
            foreach (var (job, lines) in Jobs(File.ReadAllLines(path)))
            {
                var gateSeen = false;
                foreach (var line in lines)
                {
                    var code = line.TrimStart();
                    if (code.StartsWith('#')) continue;
                    if (code.Contains(Verifier)) gateSeen = true;
                    else if (Publishing.IsMatch(code))
                    {
                        publishing++;
                        if (!gateSeen) violations.Add($"{name}:{job}: '{code}' runs before the adjudication gate");
                    }
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join("\n", violations));
        Assert.True(publishing >= 7, $"expected the known publishing commands, found {publishing}");
    }

    [Fact]
    public void OnlyTheAuditListensToReleaseEvents()
    {
        var listeners = Directory.GetFiles(WorkflowDir(), "*.yml")
            .Where(p => Triggers(File.ReadAllText(p)).Any(t => t.StartsWith("release:", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(new[] { "release-audit.yml" }, listeners);
        Assert.Contains(Verifier, Read("release-audit.yml"));
    }

    [Fact]
    public void PublishWorkflowHasNoVersionOverrideAndNoAssetClobber()
    {
        var text = string.Join("\n", File.ReadAllLines(Path.Combine(WorkflowDir(), "publish-nuget.yml"))
            .Where(l => !l.TrimStart().StartsWith('#')));
        Assert.DoesNotContain("inputs.version", text);
        Assert.DoesNotContain("--clobber", text);
        Assert.DoesNotContain("/p:Version=", text);
    }

    [Fact]
    public void ReleaseIsCreatedAfterThePackagesAndBeforeDownstreamDispatch()
    {
        var lines = File.ReadAllLines(Path.Combine(WorkflowDir(), "publish-nuget.yml"));
        int Index(string marker) => Array.FindIndex(lines, l => l.Contains(marker) && !l.TrimStart().StartsWith('#'));
        var verify = Index("--nuget-dir ./nupkg");
        var push = Index("dotnet nuget push");
        var served = Index("--registry-dir \"$RUNNER_TEMP/served\"");
        var create = Index("gh release create");
        var dispatch = Index("gh workflow run nextjs-gh-pages.yml");
        Assert.True(verify > 0 && verify < push && push < served && served < create && create < dispatch,
            $"order verify={verify} push={push} served={served} create={create} dispatch={dispatch}");
    }

    private static string WorkflowDir() => Path.Combine(ReleaseAdjudicationGateTests.RepoRoot(), ".github", "workflows");

    private static string Read(string workflow) => File.ReadAllText(Path.Combine(WorkflowDir(), workflow));

    /// <summary>Top-level keys under <c>on:</c>.</summary>
    private static List<string> Triggers(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l is "on:" or "\"on\":" or "'on':");
        Assert.True(start >= 0, "workflow has no 'on:' block");
        return lines.Skip(start + 1)
            .TakeWhile(l => l.Length == 0 || l.StartsWith(' ') || l.StartsWith('#'))
            .Where(l => Regex.IsMatch(l, @"^  [a-z_]+:"))
            .Select(l => l.Trim())
            .ToList();
    }

    /// <summary>Lines of each job under the top-level <c>jobs:</c> key.</summary>
    private static IEnumerable<(string Job, List<string> Lines)> Jobs(string[] lines)
    {
        var start = Array.IndexOf(lines, "jobs:");
        if (start < 0) yield break;
        string? job = null;
        var body = new List<string>();
        foreach (var line in lines.Skip(start + 1))
        {
            var header = Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
            if (header.Success)
            {
                if (job is not null) yield return (job, body);
                job = header.Groups[1].Value;
                body = new List<string>();
            }
            else if (line.Length > 0 && !line.StartsWith(' ') && !line.StartsWith('#'))
            {
                break;
            }
            else
            {
                body.Add(line);
            }
        }
        if (job is not null) yield return (job, body);
    }
}
