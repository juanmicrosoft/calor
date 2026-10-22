using System.Text.RegularExpressions;
using Xunit;
using static Calor.Compiler.Tests.Release.ReleaseWorkflowTests;

namespace Calor.Compiler.Tests.Release;

/// <summary>
/// #1548: the byte-reproducibility evidence holds for Microsoft's .NET SDK 10.0.401 only. #1582
/// keeps one copy of the pin, the composite action <c>.github/actions/setup-release-sdk</c>: it
/// installs exactly that SDK into a job-private directory, and then fails the job unless
/// <c>dotnet --version</c> is 10.0.401. The private directory matters: the repository global.json
/// (10.0.100, latestMinor) would otherwise roll forward to any newer SDK on the runner image.
/// Release workflows never call <c>actions/setup-dotnet</c> themselves, never request a floating
/// version, and run the action before any dotnet command. Structural checks over committed files.
/// </summary>
public class ReleaseSdkPinTests
{
    internal const string PinnedSdk = "10.0.401";
    private const string InstallDir = "DOTNET_INSTALL_DIR: ${{ runner.temp }}/dotnet-release";
    internal const string ActionUse = "uses: ./.github/actions/setup-release-sdk";

    public static TheoryData<string> ReleaseWorkflows => new()
    {
        "publish-nuget.yml", "verify-release.yml", "nextjs-gh-pages.yml", "reproducible-builds.yml",
        "benchmark.yml", "release-audit.yml",
    };

    [Theory]
    [MemberData(nameof(ReleaseWorkflows))]
    public void ReleaseWorkflowRequestsNoFloatingDotnetVersionAndNeverInstallsAnSdkItself(string workflow)
    {
        var text = Read(workflow);
        Assert.DoesNotContain("global-json-file:", text);
        Assert.DoesNotContain("actions/setup-dotnet@", text);
        foreach (Match version in Regex.Matches(text, @"(?m)^\s*(?:dotnet-version|also-install):(.*)$"))
            Assert.Matches(new Regex(@"^\s*'\d+\.\d+\.\d{3}'\s*$"), version.Groups[1].Value);
    }

    [Theory]
    [MemberData(nameof(ReleaseWorkflows))]
    public void EveryJobThatRunsDotnetSetsUpTheReleaseSdkFirst(string workflow)
    {
        foreach (var job in Jobs(Read(workflow)))
        {
            var steps = Regex.Split(job, @"\n(?=      - (?:name|uses): )");
            var runsDotnet = steps.Select((s, i) => (s, i))
                .Where(x => Regex.IsMatch(x.s, @"(?m)^\s+(?:run: )?(?:dotnet |bash \.github/scripts/test-sdk-package\.sh|""\$RUNNER_TEMP/calorcheck/calor"" )")).Select(x => x.i).ToList();
            if (runsDotnet.Count == 0) continue;
            var setups = Enumerable.Range(0, steps.Length).Where(i => steps[i].Contains(ActionUse, StringComparison.Ordinal)).ToList();
            Assert.True(setups.Count == 1, $"{workflow}: a job that runs dotnet must set up the release SDK exactly once:\n{job[..Math.Min(200, job.Length)]}");
            Assert.True(setups[0] < runsDotnet[0], $"{workflow}: a dotnet command runs before the release SDK is set up");
        }
    }

    [Fact]
    public void TheCompositeActionIsTheOnlyPinAndRequiresTheReleaseSdk()
    {
        var action = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "actions", "setup-release-sdk", "action.yml")).Replace("\r\n", "\n");
        var steps = Regex.Split(action, @"\n(?=    - name: )");
        Assert.Equal(4, steps.Length);
        // The release SDK first, unconditionally, into the private directory.
        Assert.Contains("uses: actions/setup-dotnet@v4", steps[1]);
        Assert.Contains(InstallDir, steps[1]);
        Assert.Contains($"dotnet-version: '{PinnedSdk}'", steps[1]);
        Assert.DoesNotContain("if:", steps[1]);
        // An optional extra SDK (8.0 for the corpus targets) goes into the same directory.
        Assert.Contains("if: inputs.also-install != ''", steps[2]);
        Assert.Contains(InstallDir, steps[2]);
        // Then the check, unconditional and last. Codex round 1 (PR 2): on Windows
        // `dotnet --version` ends in CRLF; compare without the CR.
        Assert.StartsWith("    - name: Require the pinned release SDK (#1548)", steps[3]);
        Assert.DoesNotContain("if:", steps[3]);
        Assert.DoesNotContain("continue-on-error", action);
        Assert.Contains("sdk=\"$(dotnet --version | tr -d '\\r')\"", steps[3]);
        Assert.Contains($"if [ \"$sdk\" != {PinnedSdk} ]; then", steps[3]);
        Assert.Contains("shell: bash", steps[3]);
        Assert.Contains("set -euo pipefail", steps[3]);
        // No other file in the repository's workflows or actions pins the release SDK.
        var gh = Path.Combine(RepoRoot(), ".github");
        var pinned = Directory.GetFiles(gh, "*.yml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains($"dotnet-version: '{PinnedSdk}'", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(gh, f).Replace('\\', '/')).ToList();
        Assert.Equal(new[] { "actions/setup-release-sdk/action.yml" }, pinned);
    }

    [Fact]
    public void EveryReleaseWorkflowThatBuildsDotnetBytesUsesTheReleaseSdk()
    {
        foreach (var workflow in new[] { "publish-nuget.yml", "verify-release.yml", "reproducible-builds.yml", "benchmark.yml" })
            Assert.Contains(ActionUse, Read(workflow));
        // The website and audit workflows build no .NET bytes and install no SDK.
        foreach (var workflow in new[] { "nextjs-gh-pages.yml", "release-audit.yml" })
            Assert.DoesNotContain("setup-release-sdk", Read(workflow));
        // Only the corpus job adds .NET 8, next to (never instead of) the release SDK.
        Assert.Contains($"{ActionUse}\n        with:\n          also-install: '8.0.426'\n", Read("publish-nuget.yml"));
    }

    private static IEnumerable<string> Jobs(string text)
    {
        var jobs = text[(text.IndexOf("\njobs:\n", StringComparison.Ordinal) + 7)..];
        return Regex.Split(jobs, @"\n(?=  [A-Za-z0-9_-]+:\n)");
    }

    private static string Read(string workflow) =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", workflow)).Replace("\r\n", "\n");
}
