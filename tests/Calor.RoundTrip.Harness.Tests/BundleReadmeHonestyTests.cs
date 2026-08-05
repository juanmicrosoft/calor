using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Pins the bundle README's claims about the Calor arm. The README template asserted "the Calor arm's
/// agent is confronted by the diagnostic" and that the agent could "clear the Calor build by DECLARING
/// it in §E". Both are false as shipped: bundles contain zero `.calr` files and the runner never
/// invokes the Calor compiler, so Calor0410 cannot fire in the loop. That sentence was generated into
/// every task bundle, so it travelled into any epoch record built on them.
/// </summary>
public class BundleReadmeHonestyTests
{
    private static EligibilityProof Proof(string? check) => new()
    {
        MutatedFileRelPath = "src/F.cs",
        MutatedFileConvertedNative = true,
        MutatedFileLossCount = 0,
        MutatedFileStatus = "Replaced",
        CSharpArmHeldOutOutcome = "Failed",
        CalorArmHeldOutOutcome = "Failed",
        VerificationCheckFired = check,
        AddressabilityNote = "note",
        Stratum = check == null ? "Logic" : "Expressible",
    };

    private static TaskBundle Bundle(string? check) => new()
    {
        TaskId = "t", ProjectName = "P",
        Provenance = new TaskProvenance
        {
            Source = MutationSource.InjectedMutation,
            Operator = MutationOperatorKind.EffectViolation,
            OperatorDescription = "d",
            MutatedFileRelPath = "src/F.cs",
            Line = 1, Column = 1,
            OriginalSnippet = "a", MutatedSnippet = "b",
            NativeEligibility = Proof(check),
        },
        CSharpArmDir = "cs", CalorArmDir = "cal",
        HeldOut = [],
        VisibleTestFilter = "f",
        RegressionNetProject = "t.csproj",
        FailingBehavior = new FailingBehaviorReport { Symptom = "s" },
        EligibilityProof = Proof(check),
    };

    [Fact]
    public void The_readme_does_not_claim_the_agent_sees_the_diagnostic()
    {
        var md = TaskGenReportWriter.BundleReadme(Bundle("Calor0410"));

        Assert.DoesNotContain("agent is confronted by the diagnostic", md);
        Assert.Contains("does NOT present that diagnostic to an agent", md);
        Assert.Contains("round-tripped C#", md);
        Assert.Contains("compiler-level", md);
    }

    [Fact]
    public void The_readme_states_what_an_epoch_over_these_arms_measures()
    {
        var md = TaskGenReportWriter.BundleReadme(Bundle("Calor0410"));

        Assert.Contains("conversion penalty", md);
        Assert.Contains("NOT the verification-depth thesis", md);
        Assert.Contains("substrate-arm-validity-finding.md", md);
    }

    [Fact]
    public void The_papering_over_residual_is_marked_unexercisable_in_this_bundle()
    {
        // The residual is a real property of the DEFECT's design. It is not available here, because
        // there is no §E to declare in and no Calor build to clear.
        var md = TaskGenReportWriter.BundleReadme(Bundle("Calor0410"));

        Assert.Contains("neither path is exercisable here", md);
        Assert.Contains("property of the DEFECT", md);
    }

    [Fact]
    public void The_readme_never_says_the_agents_choice_is_the_measurement()
    {
        // The exact sentences an in-place patch left behind in all 8 shipped bundles while the
        // inserted correction contradicted them four lines below. Asserting their ABSENCE is the
        // regression guard; asserting the presence of the new text is not, because the broken
        // artifacts contained the new text too.
        var md = TaskGenReportWriter.BundleReadme(Bundle("Calor0410"));

        Assert.DoesNotContain("Which path the agent takes IS", md);
        Assert.DoesNotContain("both remain possible", md);
    }

    [Fact]
    public void Every_committed_bundle_readme_matches_what_the_generator_emits()
    {
        // The test that would have caught the patched-not-regenerated artifacts. provenance.json is a
        // serialized TaskBundle, so a shipped README must be byte-identical to BundleReadme(it).
        var epoch = FindRepoPath("bench/phase0-agent-native/epochs/s1-funnel-001/bundles");
        if (epoch == null) return;   // bundles absent in this checkout — nothing to verify

        var opts = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var checkedAny = false;
        foreach (var dir in Directory.GetDirectories(epoch!))
        {
            var prov = Path.Combine(dir, "provenance.json");
            var readme = Path.Combine(dir, "README.md");
            if (!File.Exists(prov) || !File.Exists(readme)) continue;

            var bundle = JsonSerializer.Deserialize<TaskBundle>(File.ReadAllText(prov), opts);
            Assert.NotNull(bundle);
            Assert.Equal(TaskGenReportWriter.BundleReadme(bundle!), File.ReadAllText(readme));
            checkedAny = true;
        }
        Assert.True(checkedAny, "no bundle with both provenance.json and README.md was found");
    }

    private static string? FindRepoPath(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, rel);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void The_logic_stratum_wording_is_untouched()
    {
        // Only the expressible block made the false claim; the logic block already said Calor has no
        // mechanical signal, and must keep saying it.
        var md = TaskGenReportWriter.BundleReadme(Bundle(null));

        Assert.Contains("Calor has NO mechanical signal", md);
        Assert.DoesNotContain("does NOT present that diagnostic", md);
    }
}
