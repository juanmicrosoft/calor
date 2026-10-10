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
    // The 0.24.0 maintainer override (ungated, recorded) is the only other accepted check.
    private const string OverrideVerifier = "scripts/verify_maintainer_override.py";

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

    // Workflows that also accept the maintainer override. benchmark.yml is deliberately absent.
    private static readonly string[] OverrideWorkflows = { "publish-nuget.yml", "nextjs-gh-pages.yml", "verify-release.yml" };

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
        Assert.Contains(Verifier, text);
        if (!OverrideWorkflows.Contains(workflow))
        {
            Assert.Matches(new Regex(@"\n      adjudication_identity:\n(?:        .*\n)*?        required: true\n"), text);
            Assert.DoesNotContain("maintainer_override", text);
            Assert.DoesNotContain(OverrideVerifier, text);
            return;
        }
        // The identity is optional only because the override can replace it; the mode step
        // refuses a dispatch with both inputs or neither, and every mode switch fails closed.
        Assert.Matches(new Regex(@"\n      adjudication_identity:\n(?:        .*\n)*?        required: false\n"), text);
        Assert.Matches(new Regex(@"\n      maintainer_override:\n(?:        .*\n)*?        required: false\n"), text);
        Assert.Contains(OverrideVerifier + " --select-mode", text);
        var switches = Regex.Matches(text, @"case ""\$(?:RELEASE_)?MODE"" in").Count;
        Assert.True(switches > 0, "no release-mode switch");
        Assert.Equal(switches, Regex.Matches(text, @"\n\s*\*\) echo ""::error::[^""]*mode[^""]*""; exit 1 ;;").Count);
    }

    [Fact]
    public void OverrideModeKeepsTheReleaseTestGatesAndNeverPublishesBenchmarks()
    {
        var text = Read("publish-nuget.yml");
        Assert.Contains("needs: [adjudication-gate, test, sdk-consumer, release-quality]", text);
        var dispatch = Regex.Match(text, @"(?s)- name: Dispatch website and benchmark publication.*").Value;
        var overrideBranch = Regex.Match(dispatch, @"(?s)\n\s*override\)(.*?);;").Groups[1].Value;
        Assert.Contains("nextjs-gh-pages.yml", overrideBranch);
        Assert.DoesNotContain("benchmark.yml", overrideBranch);
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
            publishing += GateViolations(name, File.ReadAllLines(path), violations);
        }
        Assert.True(violations.Count == 0, string.Join("\n", violations));
        Assert.True(publishing >= 7, $"expected the known publishing commands, found {publishing}");
    }

    [Theory]
    [InlineData("python3 scripts/verify_release_adjudication.py --identity \"$ID\" || true")]
    [InlineData("python3 scripts/verify_release_adjudication.py --identity \"$ID\" || :")]
    [InlineData("python3 scripts/verify_release_adjudication.py --identity \"$ID\" || exit 0")]
    [InlineData("python3 scripts/verify_release_adjudication.py --identity \"$ID\" \\\n            --expect-head || echo skipped")]
    public void GateCheckerRejectsASuppressedGate(string gate)
    {
        // Mutation control: the structural check itself must not accept a fail-open gate.
        var lines = $"jobs:\n  publish:\n    steps:\n      - run: |\n          {gate}\n          dotnet nuget push x.nupkg\n".Split('\n');
        var violations = new List<string>();
        GateViolations("mutant.yml", lines, violations);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void GateCheckerRejectsContinueOnErrorOnTheGateStep()
    {
        var lines = ("jobs:\n  publish:\n    steps:\n      - continue-on-error: true\n        run: python3 scripts/verify_release_adjudication.py --identity x\n"
            + "      - run: dotnet nuget push x.nupkg\n").Split('\n');
        var violations = new List<string>();
        GateViolations("mutant.yml", lines, violations);
        Assert.NotEmpty(violations);
    }

    [Theory]
    [MemberData(nameof(GatedWorkflows))]
    public void DispatchInputsAreNeverInterpolatedIntoShell(string workflow)
    {
        // An input such as $(...) inside a run block would execute before the gate.
        var lines = File.ReadAllLines(Path.Combine(WorkflowDir(), workflow));
        var runIndent = -1;
        foreach (var line in lines)
        {
            var indent = line.Length - line.TrimStart().Length;
            if (runIndent >= 0 && line.Trim().Length > 0 && indent <= runIndent) runIndent = -1;
            var run = Regex.Match(line, @"^(\s*)(?:- )?run:");
            if (run.Success) runIndent = run.Groups[1].Length;
            if (runIndent >= 0)
                Assert.DoesNotMatch(new Regex(@"\$\{\{\s*(?:github\.event\.)?inputs\."), line);
        }
    }

    /// <summary>
    /// Counts publishing commands and records each that is not preceded, in its job, by an
    /// unsuppressed run of the gate, and any gate step in a publishing job that uses continue-on-error.
    /// </summary>
    private static int GateViolations(string name, string[] workflow, List<string> violations)
    {
        var publishing = 0;
        foreach (var (job, lines) in Jobs(workflow))
        {
            var gateSeen = false;
            var jobPublishes = false;
            var inGate = false;
            foreach (var line in lines)
            {
                var code = line.TrimStart();
                if (code.StartsWith('#')) continue;
                var isGate = code.Contains(Verifier) || code.Contains(OverrideVerifier);
                if (isGate || inGate)
                {
                    if (Regex.IsMatch(code, @"\|\|\s*(?:true\b|:(?=\s|;|$)|echo\b|exit\s+0\b)|;\s*true\b"))
                        violations.Add($"{name}:{job}: the gate's failure is suppressed: '{code}'");
                    else if (isGate)
                        gateSeen = true;
                    inGate = code.EndsWith('\\');
                }
                else if (Publishing.IsMatch(code))
                {
                    publishing++;
                    jobPublishes = true;
                    if (!gateSeen) violations.Add($"{name}:{job}: '{code}' runs before the adjudication gate");
                }
            }
            // A gate step that may fail without failing the job is no gate.
            var steps = new List<List<string>>();
            foreach (var line in lines)
            {
                if (Regex.IsMatch(line, @"^\s{6}- ")) steps.Add(new List<string>());
                if (steps.Count > 0) steps[^1].Add(line);
            }
            if (jobPublishes && steps.Any(step => step.Any(l => l.Contains(Verifier) || l.Contains(OverrideVerifier))
                    && step.Any(l => Regex.IsMatch(l, @"^\s*-?\s*continue-on-error:\s*true"))))
                violations.Add($"{name}:{job}: a gate step uses continue-on-error");
        }
        return publishing;
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
