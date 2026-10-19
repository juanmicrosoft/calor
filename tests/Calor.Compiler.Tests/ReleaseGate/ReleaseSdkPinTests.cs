using System.Text.RegularExpressions;
using Xunit;
using static Calor.Compiler.Tests.ReleaseGate.ReleaseAdjudicationGateTests;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// #1548: the byte-reproducibility evidence holds for Microsoft's .NET SDK 10.0.401 only. Every
/// <c>actions/setup-dotnet</c> in a workflow that builds, tests, or verifies release bytes installs
/// an exact version into a job-private directory, and the job then fails unless
/// <c>dotnet --version</c> is 10.0.401. The private directory matters: the repository global.json
/// (10.0.100, latestMinor) would otherwise roll forward to any newer SDK on the runner image.
/// No release workflow may request a floating version such as 10.0.x. Structural checks over the
/// committed workflow files.
/// </summary>
public class ReleaseSdkPinTests
{
    internal const string PinnedSdk = "10.0.401";
    private const string InstallDir = "DOTNET_INSTALL_DIR: ${{ runner.temp }}/dotnet-release";
    private const string RequireStep = "- name: Require the pinned release SDK (#1548)";

    public static TheoryData<string> ReleaseWorkflows => new()
    {
        "publish-nuget.yml", "verify-release.yml", "nextjs-gh-pages.yml", "reproducible-builds.yml",
        "benchmark.yml", "release-audit.yml",
    };

    [Theory]
    [MemberData(nameof(ReleaseWorkflows))]
    public void ReleaseWorkflowRequestsNoFloatingDotnetVersion(string workflow)
    {
        var text = Read(workflow);
        Assert.DoesNotContain("global-json-file:", text);
        foreach (Match version in Regex.Matches(text, @"(?m)^\s*dotnet-version:(.*)$"))
            Assert.Matches(new Regex(@"^\s*'\d+\.\d+\.\d{3}'\s*$"), version.Groups[1].Value);
    }

    [Theory]
    [MemberData(nameof(ReleaseWorkflows))]
    public void EverySetupDotnetInstallsAnExactSdkAndTheJobRequiresTheReleaseSdk(string workflow)
    {
        foreach (var job in Jobs(Read(workflow)))
        {
            var steps = Regex.Split(job, @"\n(?=      - (?:name|uses): )");
            var setups = Enumerable.Range(0, steps.Length)
                .Where(i => steps[i].Contains("uses: actions/setup-dotnet@", StringComparison.Ordinal)).ToList();
            if (setups.Count == 0) continue;
            foreach (var i in setups)
                Assert.Contains(InstallDir, steps[i]);
            // The first install is the release SDK; a second one (8.0 for the corpus targets) may follow.
            Assert.Contains($"dotnet-version: '{PinnedSdk}'", steps[setups[0]]);
            foreach (var i in setups.Skip(1))
                Assert.Matches(new Regex(@"dotnet-version: '8\.0\.\d{3}'"), steps[i]);
            // The check comes right after the last install and before any other dotnet command.
            var require = setups[^1] + 1;
            Assert.True(require < steps.Length && steps[require].TrimStart().StartsWith(RequireStep, StringComparison.Ordinal),
                $"{workflow}: no '{RequireStep}' step after the last setup-dotnet");
            // Codex round 1 (PR 2): on Windows `dotnet --version` ends in CRLF; compare without the CR.
            Assert.Contains("sdk=\"$(dotnet --version | tr -d '\\r')\"", steps[require]);
            Assert.Contains($"if [ \"$sdk\" != {PinnedSdk} ]; then", steps[require]);
            Assert.Contains("shell: bash", steps[require]);
            foreach (var step in steps.Take(setups[0]))
                Assert.DoesNotMatch(new Regex(@"(?m)^\s+(?:run: )?dotnet "), step);
        }
    }

    [Fact]
    public void EveryReleaseWorkflowThatBuildsDotnetBytesInstallsTheReleaseSdk()
    {
        foreach (var workflow in new[] { "publish-nuget.yml", "verify-release.yml", "reproducible-builds.yml", "benchmark.yml" })
            Assert.Contains($"dotnet-version: '{PinnedSdk}'", Read(workflow));
        // The website and audit workflows build no .NET bytes and install no SDK.
        foreach (var workflow in new[] { "nextjs-gh-pages.yml", "release-audit.yml" })
            Assert.DoesNotContain("actions/setup-dotnet@", Read(workflow));
    }

    private static IEnumerable<string> Jobs(string text)
    {
        var jobs = text[(text.IndexOf("\njobs:\n", StringComparison.Ordinal) + 7)..];
        return Regex.Split(jobs, @"\n(?=  [A-Za-z0-9_-]+:\n)");
    }

    private static string Read(string workflow) =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", workflow)).Replace("\r\n", "\n");
}
