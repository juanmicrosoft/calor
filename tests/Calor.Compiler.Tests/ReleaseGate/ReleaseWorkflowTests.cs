using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Calor.Compiler.Tests.ReleaseGate;

/// <summary>
/// #1582: the release workflows run without the adjudication gate, and every check on the shipped
/// bits stays and still blocks. Structural checks over the committed workflow files: each kept
/// step exists, sits in the right job and order, and carries no condition, continue-on-error, or
/// masked exit that would let a failure pass. The publish job is the only route to the NuGet OIDC
/// exchange, and it runs in the main-only nuget-release environment (#1545).
/// scripts/check_release_credentials.py checks the credential path on parsed YAML, and
/// scripts/test_release_checks.py tests the checks' own logic.
/// </summary>
public class ReleaseWorkflowTests
{
    private const string Publish = "publish-nuget.yml";
    private const string KeyRef = "${{ steps.nuget-login.outputs.NUGET_API_KEY }}";

    public static TheoryData<string> ReleaseWorkflows => new()
    {
        "publish-nuget.yml", "nextjs-gh-pages.yml", "benchmark.yml", "verify-release.yml", "release-audit.yml",
    };

    [Theory]
    [InlineData("publish-nuget.yml")]
    [InlineData("nextjs-gh-pages.yml")]
    [InlineData("benchmark.yml")]
    [InlineData("verify-release.yml")]
    public void SurfaceIsNotTriggeredByReleaseEvents(string workflow)
    {
        Assert.DoesNotContain(Triggers(Read(workflow)), t => t.StartsWith("release:", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ReleaseWorkflows))]
    public void NoReleaseWorkflowCallsTheAdjudicationGate(string workflow)
    {
        var text = Read(workflow);
        foreach (var gone in new[] { "verify_release_adjudication", "verify_maintainer_override", "adjudication_identity",
                     "maintainer_override", "calor-adjudication", "RELEASE_MODE", "--select-mode" })
            Assert.DoesNotContain(gone, text);
    }

    [Fact]
    public void PublishRunsOnlyByDispatchFromMainAndOneAtATime()
    {
        var text = Read(Publish);
        // No tag-push trigger (#1545): a `v*` trigger would let any writer reach the publish job
        // with an edited workflow.
        Assert.Equal(new[] { "workflow_dispatch:" }, Triggers(text));
        Assert.Contains("concurrency:\n  group: release-publication\n  cancel-in-progress: false\n", text);
        var refuse = Step(Job(text, "preflight"), "Refuse a dispatch from any ref but main");
        Assert.Contains("if: github.ref != 'refs/heads/main'", refuse);
        Assert.Contains("exit 1", refuse);
        var check = Step(Job(text, "preflight"), "Check the version, CHANGELOG section, and tag");
        Assert.Contains("python3 scripts/release_checks.py preflight --version \"$VERSION\"", check);
        // Every job that builds or ships waits for preflight.
        foreach (var job in new[] { "test", "sdk-consumer", "release-quality", "site", "package", "publish", "install-check", "release", "website" })
            Assert.Matches(new Regex(@"(?m)^    needs: (?:preflight|\[preflight[,\]])"), Job(text, job));
    }

    [Fact]
    public void EveryCheckoutDropsCredentialsAndTheTestJobsFetchMainAndTags()
    {
        var text = Read(Publish);
        var checkouts = Steps(text).Where(s => s.Contains("uses: actions/checkout@", StringComparison.Ordinal)).ToList();
        Assert.Equal(8, checkouts.Count);
        Assert.All(checkouts, c => Assert.Contains("persist-credentials: false", c));
        foreach (var job in new[] { "test", "release-quality" })
        {
            var body = Job(text, job);
            Assert.Contains("fetch-depth: 0", Steps(body).First(s => s.Contains("actions/checkout@", StringComparison.Ordinal)));
            var fetch = Step(body, "Fetch protected main and tags for provenance (#1417)");
            Assert.Contains("'+refs/heads/main:refs/remotes/origin/main' '+refs/tags/*:refs/tags/*'", fetch);
            Assert.Contains("test \"$(git rev-parse --is-shallow-repository)\" = false", fetch);
        }
    }

    [Fact]
    public void DryRunStopsBeforeTheOidcExchange()
    {
        var text = Read(Publish);
        Assert.Matches(new Regex(@"\n      dry_run:\n(?:        .*\n)*?        default: false\n        type: boolean\n"), text);
        var publish = Job(text, "publish");
        Assert.Contains("\n    if: ${{ !inputs.dry_run }}\n", publish);
        // Only the publish job may hold id-token: write, an environment, or the login; the job that
        // packs (and so runs during a dry run) holds none of them.
        foreach (var (name, commented) in Jobs(text))
        {
            if (name == "publish") continue;
            var body = string.Join("\n", commented.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
            Assert.DoesNotContain("id-token", body);
            Assert.DoesNotContain("environment:", body);
            Assert.DoesNotContain("nuget-login", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NuGet/login", body, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("id-token", text[..text.IndexOf("\njobs:\n", StringComparison.Ordinal)]);
        // Downstream jobs need publish, so a dry run skips them too.
        Assert.Contains("needs: [preflight, publish]", Job(text, "install-check"));
        Assert.Contains("needs: [preflight, package, publish, install-check]", Job(text, "release"));
        Assert.Contains("needs: [preflight, release]", Job(text, "website"));
    }

    [Fact]
    public void NugetKeyIsReachableOnlyThroughTheMainOnlyEnvironment()
    {
        // #1545: older tags read the repository secret NUGET_API_KEY. Publishing now uses nuget.org
        // Trusted Publishing: no key is stored; the publish job, in the main-only nuget-release
        // environment, exchanges its OIDC token through the pinned NuGet/login action. nuget.org's
        // policy matches the workflow file name and the environment, not the ref, so the
        // environment is what keeps older tags out. The full analysis, on parsed YAML with its
        // mutation controls, is scripts/check_release_credentials.py and
        // scripts/test_check_release_credentials.py; this lane has no PyYAML, so it checks the
        // committed facts directly and that CI runs the parser-based check with the pinned parser.
        var lines = File.ReadAllLines(Path.Combine(WorkflowDir(), Publish));
        var publish = Read(Publish);
        Assert.Contains("  publish:\n    runs-on: ubuntu-latest\n    needs: [preflight, test, sdk-consumer, release-quality, site, package]\n", publish);
        Assert.Single(lines, l => l == "    environment: nuget-release");
        Assert.Contains("    environment: nuget-release\n    permissions:\n      contents: read\n      id-token: write\n", publish);
        Assert.Contains("    steps:\n      # Fail closed before any work", publish);
        Assert.Contains(
            "      - name: Exchange the GitHub OIDC token for a short-lived NuGet key (#1545)\n" +
            "        id: nuget-login\n" +
            "        uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841 # v1.2.0\n" +
            "        with:\n" +
            "          user: juanmicrosoft\n\n" +
            "      - name: Require the Trusted Publishing NuGet key (#1545)\n" +
            "        env:\n" +
            "          NUGET_OIDC_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n", publish);
        Assert.Contains("--api-key \"$NUGET_OIDC_KEY\"", publish);
        // The login action's output is the only place the name NUGET_API_KEY may appear (twice: the
        // guard and the push), and no workflow may reference any NuGet secret.
        Assert.Equal(2, publish.Split(KeyRef).Length - 1);
        foreach (var path in Directory.GetFiles(WorkflowDir(), "*.yml"))
        {
            var text = File.ReadAllText(path).Replace(KeyRef, "", StringComparison.Ordinal);
            Assert.DoesNotContain("NUGET_API_KEY", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NUGET_RELEASE_API_KEY", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(new Regex(@"secrets\s*(\.|\[)\s*['""]?[A-Za-z0-9_]*NUGET", RegexOptions.IgnoreCase), text);
        }
    }

    [Fact]
    public void CiRunsTheCredentialCheckWithThePinnedYamlParser()
    {
        // EXC-RELEASE-GAPS-A-YAML: the check decides on parsed YAML. CI installs the hash-pinned
        // parser into its own environment and runs both the check's tests and the check with it.
        var requirements = File.ReadAllText(Path.Combine(ReleaseAdjudicationGateTests.RepoRoot(), "eng", "release-credentials-requirements.txt"));
        Assert.Matches(new Regex(@"(?m)^PyYAML==\d+\.\d+\.\d+ \\$"), requirements);
        Assert.Matches(new Regex(@"--hash=sha256:[0-9a-f]{64}"), requirements);
        var test = Read("test.yml");
        Assert.Contains("/bin/pip\" install --require-hashes -r eng/release-credentials-requirements.txt", test);
        Assert.Contains("\"$CRED_PY\" scripts/test_check_release_credentials.py", test);
        Assert.Contains("\"$CRED_PY\" scripts/check_release_credentials.py", test);
        Assert.DoesNotContain("python3 scripts/test_check_release_credentials.py", test);
        // The release checks' own tests run in CI and in the release-quality job.
        Assert.Contains("python3 scripts/test_release_checks.py", test);
        Assert.Contains("python3 scripts/test_release_checks.py", Job(Read(Publish), "release-quality"));
    }

    [Fact]
    public void TestMatrixKeepsEveryReleaseCriticalProjectAndItsGuards()
    {
        var job = Job(Read(Publish), "test");
        var matrix = Regex.Matches(job, @"(?m)^            project: (\S+)$").Select(m => m.Groups[1].Value).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ReleaseAdjudicationGateTests.RepoRoot(), "eng", "test-manifest.json")))!;
        var critical = manifest["projects"]!.AsArray().Where(p => (bool)p!["releaseCritical"]!).Select(p => (string)p!["path"]!)
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(13, matrix.Count);
        Assert.Equal(critical, matrix);
        Assert.Contains("fail-fast: false", job);
        Assert.Contains("uses: ./.github/actions/bootstrap-z3", job);
        Assert.Contains("run: dotnet restore --locked-mode", job);
        Assert.Contains("is not release-critical in eng/test-manifest.json", Step(job, "Assert this project is release-critical in the manifest"));
        var inventory = Step(job, "Enforce test inventory");
        Assert.Equal(2, Regex.Matches(inventory, @"python3 scripts/check_trx\.py").Count);
        Assert.Equal(2, Regex.Matches(inventory, @"--submodules").Count);
        var z3 = Step(job, "Assert no Z3-gated test silently skipped");
        Assert.Contains("if grep -q \"Z3 not available\" test-output.log; then", z3);
        Assert.Contains("exit 1", z3);
    }

    [Fact]
    public void SdkConsumerRunsOnFiveRids()
    {
        var job = Job(Read(Publish), "sdk-consumer");
        var rids = Regex.Matches(job, @"(?m)^            rid: (\S+)$").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "linux-x64", "linux-arm64", "osx-arm64", "win-x64", "win-arm64" }, rids);
        var check = Step(job, "Run M-A1 consumer check");
        Assert.Contains("CALOR_SDK_REQUIRE_ALL_RIDS: \"1\"", check);
        Assert.Contains("run: bash .github/scripts/test-sdk-package.sh", check);
    }

    [Fact]
    public void ReleaseQualityKeepsEveryRatchetAndTheRoundTripHarness()
    {
        var job = Job(Read(Publish), "release-quality");
        foreach (var command in new[]
                 {
                     "dotnet restore --locked-mode",
                     "python3 scripts/check_test_quality.py --self-test", "python3 scripts/check_trx.py --self-test",
                     "bash scripts/check_sample_ids.sh --self-test", "python3 scripts/check_coverage.py --self-test",
                     "python3 scripts/run_mutation_gate.py --self-test", "python3 scripts/run_performance_gate.py --self-test",
                     "python3 scripts/run_flake_gate.py --self-test",
                     "python3 scripts/check_test_quality.py\n", "bash scripts/check_sample_ids.sh\n",
                     "python3 scripts/check_coverage.py\n", "python3 scripts/run_mutation_gate.py\n",
                     "python3 scripts/run_performance_gate.py\n", "python3 scripts/run_flake_gate.py --runs 100",
                     "dotnet run --project tools/Calor.RoundTrip.Harness/ -c Release -- \\\n            run --all",
                     "scorecard --format both",
                 })
            Assert.Contains(command, job);
    }

    [Fact]
    public void PackingAndEveryPrePushCheckRunInTheJobThePublishJobNeeds()
    {
        var text = Read(Publish);
        var package = Job(text, "package");
        Assert.Contains("    needs: [preflight, test, sdk-consumer, release-quality, site]\n", package);
        var order = new[]
        {
            "uses: ./.github/actions/setup-release-sdk",
            "python3 scripts/reproducible_builds.py check-runtime --sdk 10.0.401 --prune",
            "uses: ./.github/actions/bootstrap-z3",
            "dotnet restore src/Calor.Compiler/Calor.Compiler.csproj --locked-mode",
            "dotnet pack src/Calor.Compiler/Calor.Compiler.csproj -c Release --no-build -o ./nupkg",
            "dotnet pack src/Calor.Sdk/Calor.Sdk.csproj -c Release -o ./nupkg /p:CalorSdkRequireAllRids=true",
            "bash .github/scripts/inspect-sdk-nupkg.sh \"$SDK_NUPKG\" --all-rids",
            "python3 scripts/check-packaged-z3.py ./nupkg/calor.*.nupkg --prefix tools/net10.0/any --all-rids",
            "python3 scripts/check-packaged-z3.py ./nupkg/Calor.Sdk.*.nupkg --prefix tasks/net10.0 --all-rids",
            "python3 scripts/generate-release-metadata.py",
            "python3 scripts/render_release_notes.py \"$VER\" CHANGELOG.md > ./release-notes/notes.md",
            "python3 scripts/release_checks.py wording ./release-notes/notes.md ./nupkg/calor.*.nupkg ./nupkg/Calor.Sdk.*.nupkg ./release-metadata",
            "python3 scripts/release_checks.py registry --mode existing",
            "python3 scripts/release_checks.py bundle --version \"$VER\" --nuget-dir ./nupkg",
            "name: release-bundle",
        };
        AssertInOrder(package, order);
        // Both packages exist before either is pushed: packing is in an earlier job, and the job
        // that pushes packs nothing.
        var publish = Job(text, "publish");
        Assert.DoesNotContain("dotnet pack", publish);
        Assert.DoesNotContain("dotnet nuget push", package);
        Assert.Single(Regex.Matches(text, "dotnet nuget push"));
        // The notes come from the shared renderer, never an ad-hoc awk over CHANGELOG.md.
        Assert.DoesNotContain(text.Split('\n'), l => !l.TrimStart().StartsWith('#') && l.Contains("awk") && l.Contains("## "));
    }

    [Fact]
    public void PublishJobOrdersTheExchangeChecksAndPushAndTheReleaseJobFollowsTheInstallCheck()
    {
        var text = Read(Publish);
        var publish = Job(text, "publish");
        AssertInOrder(publish, new[]
        {
            "uses: NuGet/login@",
            "name: Require the Trusted Publishing NuGet key (#1545)",
            "name: release-bundle",
            "test \"$(sha256sum SHA256SUMS | cut -d' ' -f1)\" = \"$SUMS\"",
            "sha256sum --check --strict SHA256SUMS",
            "uses: actions/attest-build-provenance@",
            // The existing-version compare, again, immediately before the push, never advisory.
            "python3 scripts/release_checks.py registry --mode existing --version \"$VER\" --nuget-dir ./nupkg\n",
            "dotnet nuget push ./nupkg/*.nupkg",
            "python3 scripts/release_checks.py registry --mode served --version \"$VER\" --nuget-dir ./nupkg\n",
        });
        Assert.DoesNotContain("--dry-run", publish);
        Assert.DoesNotContain("gh release", publish);
        Assert.DoesNotContain("contents: write", publish);
        // The GitHub release and its metadata come after the served compare (publish) and the
        // install check, from the bundle the package job checked.
        var release = Job(text, "release");
        Assert.Contains("    needs: [preflight, package, publish, install-check]\n", release);
        AssertInOrder(release, new[]
        {
            "name: release-bundle",
            "test \"$(sha256sum SHA256SUMS | cut -d' ' -f1)\" = \"$SUMS\"",
            "sha256sum --check --strict SHA256SUMS",
            "gh release create \"$TAG\" --target \"$CANDIDATE\" --title \"$TAG\" --notes-file ./release-notes/notes.md $FLAG",
            "gh release upload \"$TAG\" \"$file\"",
        });
        Assert.DoesNotContain("id-token", release);
        // The remote tag must be absent or at the candidate, before and after creation.
        var create = Step(release, "Create the GitHub release");
        Assert.Contains("if [ -n \"$existing\" ] && [ \"$existing\" != \"$CANDIDATE\" ]; then", create);
        Assert.Contains("if [ \"$(remote_tag)\" != \"$CANDIDATE\" ]; then", create);
        Assert.Contains("PRERELEASE: ${{ needs.preflight.outputs.prerelease }}", create);
        var code = string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        Assert.DoesNotContain("gh release edit", code);
        Assert.DoesNotContain("--clobber", code);
        Assert.DoesNotContain("/p:Version=", code);
    }

    [Fact]
    public void ExistingAndServedComparesIgnoreOnlyTheRepositorySignature()
    {
        var checks = File.ReadAllText(Path.Combine(ReleaseAdjudicationGateTests.RepoRoot(), "scripts", "release_checks.py"));
        Assert.Contains("SIGNATURE = \".signature.p7s\"", checks);
        Assert.Contains("theirs = {k: v for k, v in zip_entries(served).items() if k != SIGNATURE}", checks);
        Assert.Contains("return zip_entries(local) == theirs", checks);
        Assert.Contains("PACKAGE_IDS = (\"calor\", \"calor.sdk\")", checks);
    }

    [Fact]
    public void KeptStepsAreNeverMaskedOrConditional()
    {
        // A kept check that may fail without failing its job is no check. Only these steps carry a
        // condition: the main-only refusal (which then fails), report uploads, and the optional
        // second SDK inside the composite action.
        var text = Read(Publish);
        Assert.DoesNotContain("continue-on-error", string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith('#'))));
        var allowedIf = new Dictionary<string, string>
        {
            ["Refuse a dispatch from any ref but main"] = "if: github.ref != 'refs/heads/main'",
            ["Upload test reports"] = "if: always()",
            ["Upload release quality reports"] = "if: always()",
        };
        foreach (var step in Steps(text))
        {
            var code = string.Join("\n", step.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
            Assert.DoesNotMatch(new Regex(@"\|\|\s*(?:true\b|:(?=\s|;|$)|exit\s+0\b)|;\s*true\b|set \+e"), code);
            var condition = Regex.Match(step, @"(?m)^        if: .*$");
            if (!condition.Success) continue;
            var name = Regex.Match(step, @"- name: (.*)").Groups[1].Value.Trim();
            Assert.True(allowedIf.TryGetValue(name, out var expected) && condition.Value.Trim() == expected,
                $"step '{name}' carries an unexpected condition: {condition.Value.Trim()}");
        }
        // Job-level conditions: only publish (the dry-run stop).
        foreach (var (name, body) in Jobs(text))
            if (name != "publish")
                Assert.DoesNotMatch(new Regex(@"(?m)^    if:"), body);
    }

    [Fact]
    public void InstallCheckRunsFromPublishBeforeTheReleaseAndTheWebsite()
    {
        var text = Read(Publish);
        Assert.Contains("  install-check:\n    needs: [preflight, publish]\n    uses: ./.github/workflows/verify-release.yml\n    with:\n"
                        + "      version: ${{ needs.preflight.outputs.version }}\n      ref: ${{ needs.preflight.outputs.candidate }}\n", text);
        Assert.Contains("needs: [preflight, package, publish, install-check]", Job(text, "release"));
        var website = Job(text, "website");
        Assert.Contains("needs: [preflight, release]", website);
        Assert.Contains("gh workflow run nextjs-gh-pages.yml --repo \"$GITHUB_REPOSITORY\" --ref main -f tag=\"$TAG\" -f commit=\"$CANDIDATE\"", website);
        Assert.Single(Regex.Matches(text, "gh workflow run"));
        AssertInOrder(text, new[] { "\n  publish:\n", "\n  install-check:\n", "\n  release:\n", "\n  website:\n" });
        var verify = Read("verify-release.yml");
        Assert.Contains("on:\n  workflow_call:\n    inputs:\n      version:\n", verify);
        // The sample comes from the candidate commit publish-nuget passes, never a tag that could move.
        Assert.Contains("ref: ${{ inputs.ref || format('v{0}', inputs.version) }}", verify);
        Assert.Contains("echo \"$OUT\" | grep -Eq \"Proven:\\s+14\\b\"", verify);
        Assert.Contains("echo \"$OUT\" | grep -Eq \"Skipped:\\s+0\\b\"", verify);
        Assert.Contains("--add-source https://api.nuget.org/v3/index.json", verify);
        Assert.DoesNotContain("continue-on-error", verify);
        Assert.Equal(new[] { "linux-x64", "osx-arm64", "win-x64" },
            Regex.Matches(verify, @"(?m)^            rid: (\S+)$").Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void WordingScanCoversTheNotesThePackagesAndTheBuiltSite()
    {
        // Before the push: the candidate's site is built and scanned in `site`, which `package`
        // and `publish` need, so it also runs in a dry run.
        var text = Read(Publish);
        var site = Job(text, "site");
        Assert.Contains("    needs: preflight\n", site);
        Assert.Contains("ref: ${{ needs.preflight.outputs.candidate }}", site);
        AssertInOrder(site, new[]
        {
            "run: npm ci",
            "run: npm run build",
            "python3 scripts/check_website_fonts.py --out website/out",
            "run: python3 scripts/release_checks.py wording website/out",
        });
        Assert.Contains("site, package]", Job(text, "publish"));
        // At deployment: the tagged commit must be on main and, when publish-nuget dispatches it,
        // be exactly the released commit; the deployed build is scanned again.
        var deploy = Read("nextjs-gh-pages.yml");
        AssertInOrder(deploy, new[]
        {
            "git merge-base --is-ancestor \"$commit\" HEAD",
            "if [ -n \"$EXPECTED\" ] && [ \"$commit\" != \"$EXPECTED\" ]; then",
            "run: npm run build",
            "python3 scripts/check_website_fonts.py --out website/out",
            "run: python3 \"$RUNNER_TEMP/release_checks.py\" wording website/out",
            "uses: actions/upload-pages-artifact@",
        });
        Assert.Contains("if: github.ref != 'refs/heads/main'", deploy);
        var checks = File.ReadAllText(Path.Combine(ReleaseAdjudicationGateTests.RepoRoot(), "scripts", "release_checks.py"));
        Assert.Contains("independently\\s+(?:adjudicated|verified)|independent\\s+(?:adjudication|verification)", checks);
    }

    [Fact]
    public void ReleasePathNeverDispatchesBenchmarkPublication()
    {
        // #1547: publishing benchmark results is a separate maintainer dispatch of benchmark.yml.
        foreach (var path in Directory.GetFiles(WorkflowDir(), "*.yml"))
        {
            var code = File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith('#'));
            Assert.DoesNotContain(code, l => Regex.IsMatch(l, @"gh workflow run\s+\S*benchmark"));
        }
    }

    [Fact]
    public void BenchmarkPublicationIsASeparateExplicitDispatch()
    {
        // #1547: publishing needs publish_results=true on a dispatch (default false); the step
        // that pushes and opens the PR is the only publishing step and carries that condition.
        var text = Read("benchmark.yml");
        Assert.Matches(new Regex(@"\n      publish_results:\n(?:        .*\n)*?        default: false\n(?:        .*\n)*?        type: boolean\n"), text);
        var steps = Regex.Split(text, @"\n(?=      - name: )");
        var publishing = steps.Where(step => Regex.IsMatch(step, @"(?m)^\s+(?:git push|gh pr create)\b")).ToList();
        var only = Assert.Single(publishing);
        Assert.Contains("\n        if: github.event_name == 'workflow_dispatch' && github.event.inputs.publish_results == 'true'\n", only);
        Assert.Contains("the work tree changes files other than the #1422 outputs", only);
        Assert.Contains("benchmark publication is dispatched only from main", text);
    }

    [Fact]
    public void OnlyTheAuditListensToReleaseEvents()
    {
        var listeners = Directory.GetFiles(WorkflowDir(), "*.yml")
            .Where(p => Triggers(File.ReadAllText(p)).Any(t => t.StartsWith("release:", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(new[] { "release-audit.yml" }, listeners);
        // The audit replaces the trailer check (which caught a hand-made `gh release create`): the
        // tag commit must be on main and nuget.org must serve both packages at that version.
        var audit = Read("release-audit.yml");
        Assert.Contains("if ! git merge-base --is-ancestor \"$commit\" refs/remotes/origin/main; then", audit);
        Assert.Contains("for id in calor calor.sdk; do", audit);
        Assert.Contains("if [ \"$code\" != 200 ]; then", audit);
        Assert.DoesNotContain("continue-on-error", audit);
        Assert.DoesNotContain("contents: write", audit);
    }

    [Theory]
    [MemberData(nameof(ReleaseWorkflows))]
    public void DispatchInputsAreNeverInterpolatedIntoShell(string workflow)
    {
        // An input such as $(...) inside a run block would execute. Inputs reach the shell only
        // through env.
        var lines = File.ReadAllLines(Path.Combine(WorkflowDir(), workflow));
        var runIndent = -1;
        foreach (var line in lines)
        {
            var indent = line.Length - line.TrimStart().Length;
            if (runIndent >= 0 && line.Trim().Length > 0 && indent <= runIndent) runIndent = -1;
            var run = Regex.Match(line, @"^(\s*)(?:- )?run:");
            if (run.Success) runIndent = run.Groups[1].Length;
            if (runIndent >= 0)
                Assert.DoesNotMatch(new Regex(@"\$\{\{\s*(?:github\.event\.)?(?:inputs\.|release\.)"), line);
        }
    }

    [Fact]
    public void TheVersionInputIsCheckedNeverUsedAsAnOverride()
    {
        var text = string.Join("\n", Read(Publish).Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        // inputs.version reaches exactly one place: the preflight comparison with Directory.Build.props.
        Assert.Single(Regex.Matches(text, @"inputs\.version"));
        Assert.Contains("          VERSION: ${{ inputs.version }}\n", text);
        Assert.Contains("--version \"$VERSION\"", Job(text, "preflight"));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static void AssertInOrder(string text, IEnumerable<string> markers)
    {
        var last = -1;
        foreach (var marker in markers)
        {
            var at = text.IndexOf(marker, last + 1, StringComparison.Ordinal);
            Assert.True(at > last, $"'{marker}' is missing or out of order");
            last = at;
        }
    }

    private static string WorkflowDir() => Path.Combine(ReleaseAdjudicationGateTests.RepoRoot(), ".github", "workflows");

    private static string Read(string workflow) =>
        File.ReadAllText(Path.Combine(WorkflowDir(), workflow)).Replace("\r\n", "\n");

    private static string Job(string text, string name) =>
        Jobs(text).Single(j => j.Name == name).Body;

    private static IEnumerable<string> Steps(string text) =>
        Regex.Split(text, @"\n(?=      - (?:name|uses): )").Skip(1);

    private static string Step(string job, string name) =>
        Steps(job).Single(s => s.StartsWith($"      - name: {name}\n", StringComparison.Ordinal) || s.StartsWith($"- name: {name}\n", StringComparison.Ordinal));

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

    /// <summary>Each job under the top-level <c>jobs:</c> key, with its body text.</summary>
    private static IEnumerable<(string Name, string Body)> Jobs(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.IndexOf(lines, "jobs:");
        if (start < 0) yield break;
        string? job = null;
        var body = new List<string>();
        foreach (var line in lines.Skip(start + 1))
        {
            var header = Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
            if (header.Success)
            {
                if (job is not null) yield return (job, "\n" + string.Join("\n", body));
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
        if (job is not null) yield return (job, "\n" + string.Join("\n", body));
    }
}
