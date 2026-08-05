using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Renders task bundles, provenance, and the exclusion-accounting report.</summary>
public static class TaskGenReportWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOpts);

    /// <summary>Per-bundle README: what an agent working the task receives + the provenance/eligibility proof.</summary>
    /// <summary>
    /// Culture-INVARIANT percent. `:P1` renders "40.0%" under en-US and "40.0 %" under the invariant
    /// culture, so epoch artifacts generated on a developer machine did not match the same artifacts
    /// regenerated on CI — a reproducibility defect in a committed record, caught by the
    /// README-equality test. Formatting is pinned here rather than left to the ambient culture.
    /// </summary>
    internal static string Pct(double fraction, int decimals = 1) =>
        (fraction * 100).ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture) + "%";

    public static string BundleReadme(TaskBundle b)
    {
        var p = b.Provenance;
        var proof = b.EligibilityProof;
        var sb = new StringBuilder();
        sb.AppendLine($"# Task bundle: {b.TaskId}");
        sb.AppendLine();
        sb.AppendLine($"- Project: **{b.ProjectName}**");
        sb.AppendLine($"- Mutation source: **{p.Source}**");
        sb.AppendLine($"- Operator: `{p.OperatorDescription}` ({p.Operator})");
        sb.AppendLine($"- Mutated region: `{p.MutatedFileRelPath}` line {p.Line}, col {p.Column}");
        if (p.RevertedCommit != null)
            sb.AppendLine($"- Reverted upstream fix: `{p.RevertedCommit}` — {p.RevertedCommitSubject}");
        sb.AppendLine();
        sb.AppendLine("## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)");
        sb.AppendLine();
        sb.AppendLine($"> {b.FailingBehavior.Symptom}");
        if (b.FailingBehavior.SubjectHint != null)
            sb.AppendLine($">\n> Subject hint: {b.FailingBehavior.SubjectHint}");
        sb.AppendLine($">\n> {b.FailingBehavior.Notes}");
        sb.AppendLine();
        sb.AppendLine("## Arms");
        sb.AppendLine();
        sb.AppendLine($"- `csharp-arm/` — idiomatic original C# carrying the mutation.");
        sb.AppendLine($"- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.");
        sb.AppendLine();
        sb.AppendLine($"Presentation asymmetry (recorded): {p.PresentationAsymmetry}");
        sb.AppendLine();
        sb.AppendLine("## Held-out test(s) — removed from the visible suite, kept as the regression net");
        sb.AppendLine();
        foreach (var h in b.HeldOut)
            sb.AppendLine($"- `{h.FilterName}` (assembly `{h.Assembly}`)");
        sb.AppendLine();
        sb.AppendLine($"- Visible-suite filter: `{b.VisibleTestFilter}`");
        sb.AppendLine($"- Regression-net project: `{b.RegressionNetProject}` (full suite; escaped bug = held-out failure at declared-done)");
        sb.AppendLine();
        sb.AppendLine("## Native-eligibility proof (D-W4.1)");
        sb.AppendLine();
        sb.AppendLine($"- Clause (a): mutated file `{proof.MutatedFileRelPath}` — ConvertedNative=**{proof.MutatedFileConvertedNative}** (Status={proof.MutatedFileStatus}, LossCount={proof.MutatedFileLossCount})");
        sb.AppendLine($"- Clause (b): held-out test outcome — C# arm=**{proof.CSharpArmHeldOutOutcome}**, Calor arm=**{proof.CalorArmHeldOutOutcome}**");
        sb.AppendLine($"  - failure signatures — C#=`{proof.CSharpArmFailureSignature}`, Calor=`{proof.CalorArmFailureSignature}`");
        sb.AppendLine($"- D-W4.3 attribution: **{proof.AttributionOutcome}**");
        sb.AppendLine($"- Project NativeFraction at generation: {Pct(proof.ProjectNativeFraction)}");
        sb.AppendLine();
        sb.AppendLine($"## Defect stratum: **{proof.Stratum}**");
        sb.AppendLine();
        if (proof.VerificationCheckFired != null)
        {
            sb.AppendLine($"- Verification-addressable **at generation time**: compiling the mutated file as Calor makes ");
            sb.AppendLine($"  **{proof.VerificationCheckFired}** fire, and it does not fire on the clean conversion — a signal the ");
            sb.AppendLine($"  C# compiler has no equivalent of. {proof.AddressabilityNote}");
            sb.AppendLine();
            sb.AppendLine("  > **This bundle does NOT present that diagnostic to an agent.** Both arms ship plain `.cs` — the ");
            sb.AppendLine("  > calor arm is round-tripped C# — and the runner never invokes the Calor compiler, so the check ");
            sb.AppendLine("  > cannot fire in the loop and neither arm's build fails. The differential above is a ");
            sb.AppendLine("  > **compiler-level** property, established out-of-band by the addressability probe. An epoch over ");
            sb.AppendLine("  > these arms measures the **conversion penalty** (plus the arm-symmetric ceiling-recurrence leg), ");
            sb.AppendLine("  > NOT the verification-depth thesis. See `docs/plans/substrate-arm-validity-finding.md`.");
            if (proof.VerificationCheckFired == ExpressibleMutationOperators.CalorForbiddenEffect)
            {
                sb.AppendLine();
                sb.AppendLine("  > **Papering-over residual — a property of the DEFECT, not of this bundle:** were the arm a real ");
                sb.AppendLine("  > Calor build, the agent could clear it by REMOVING the injected effect (correct → caught) or by ");
                sb.AppendLine("  > DECLARING it in §E (papers over → the bug still ships → escaped). With no `.calr` sources and ");
                sb.AppendLine("  > no Calor build present, **neither path is exercisable here** and that choice is not measured.");
            }
        }
        else
        {
            sb.AppendLine("- Logic stratum: Calor has NO mechanical signal for this defect class (the conversion-penalty / ");
            sb.AppendLine("  PP-A2 measurement). Reported with CIs alongside the expressible stratum, not conflated with it.");
        }
        return sb.ToString();
    }

    /// <summary>The run-level exclusion-accounting + fidelity-gate report (markdown).</summary>
    public static string RunReport(TaskGenRunResult run)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# WS-W4 Slice C — task-generation run report");
        sb.AppendLine();
        sb.AppendLine("Mutate-then-convert task generation with the D-W4.1 eligibility predicate and D-W4.3 fidelity gate. ");
        sb.AppendLine("This run produces the substrate the Slice-E dry-run consumes; it does NOT run agents.");
        sb.AppendLine();

        sb.AppendLine("## Definition of done");
        sb.AppendLine();
        sb.AppendLine($"- Eligible bundles: **{run.TotalEligible}** (target ≥ 3)");
        sb.AppendLine($"- Projects with eligible tasks that pass the fidelity gate: **{FidelityPassingWithTasks(run)}** (target ≥ 2)");
        sb.AppendLine($"- **DoD met: {run.MeetsDefinitionOfDone}**");
        sb.AppendLine();

        sb.AppendLine("## Fidelity gate (D-W4.3)");
        sb.AppendLine();
        sb.AppendLine($"- Bar: {run.FidelityGate.Config.BarLabel}");
        sb.AppendLine($"- {run.FidelityGate.Signal}");
        sb.AppendLine();
        sb.AppendLine("| Project | NativeFraction | Passes gate | Reason |");
        sb.AppendLine("|---|---:|:---:|---|");
        foreach (var d in run.FidelityGate.Decisions)
            sb.AppendLine($"| {d.ProjectName} | {Pct(d.NativeFraction)} | {(d.Passed ? "yes" : "no")} | {d.Reason} |");
        sb.AppendLine();

        sb.AppendLine("## Exclusion accounting (D-W4.1 — every candidate counted, no silent shrinkage)");
        sb.AppendLine();
        sb.AppendLine("| Project | Enumerated | Considered | Eligible | Excl (a) | Excl (b) | Excl attribution | Excl leak | Excl no-cover | Excl no-compile | Excl multi-src | Excl inseparable | Eligibility rate |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var p in run.Projects)
        {
            var a = p.Accounting;
            sb.AppendLine($"| {p.ProjectName} | {p.TotalEnumeratedCandidates} | {a.Considered} | {a.Eligible} | {a.ExcludedClauseA} | {a.ExcludedClauseB} | {a.ExcludedAttribution} | {a.ExcludedHeldOutFilterLeak} | {a.ExcludedNoCoveringTest} | {a.ExcludedDidNotCompile} | {a.ExcludedMultipleSourceFiles} | {a.ExcludedInseparableRevert} | {Pct(a.EligibilityRate, 0)} |");
        }
        var totEnumerated = run.Projects.Sum(p => p.TotalEnumeratedCandidates);
        var totConsidered = run.Projects.Sum(p => p.Accounting.Considered);
        var totEligible = run.Projects.Sum(p => p.Accounting.Eligible);
        sb.AppendLine($"| **TOTAL** | **{totEnumerated}** | **{totConsidered}** | **{totEligible}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedClauseA)}** | **{run.Projects.Sum(p => p.Accounting.ExcludedClauseB)}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedAttribution)}** | **{run.Projects.Sum(p => p.Accounting.ExcludedHeldOutFilterLeak)}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedNoCoveringTest)}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedDidNotCompile)}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedMultipleSourceFiles)}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedInseparableRevert)}** | " +
            $"**{Pct(totConsidered == 0 ? 0 : (double)totEligible / totConsidered, 0)}** |");
        sb.AppendLine();
        sb.AppendLine("'Excl multi-src' and 'Excl inseparable' are revert-source SUPPLY exclusions (a mined fix commit ");
        sb.AppendLine("touching >1 source file, or whose source hunk could not be cleanly reverse-applied onto the pinned ");
        sb.AppendLine("tree). They are recorded before any build/test cost and are 0 for injected-only runs.");
        sb.AppendLine();
        sb.AppendLine("Eligibility rate is Eligible/Considered (Considered = evaluated candidates; Enumerated is the ");
        sb.AppendLine("full sited set before the per-project cap / early-stop, so the rate is honest about truncation).");
        sb.AppendLine();

        sb.AppendLine("### Per-candidate dispositions");
        sb.AppendLine();
        foreach (var p in run.Projects)
        {
            sb.AppendLine($"**{p.ProjectName}** (NativeFraction {Pct(p.NativeFraction)}, {p.NativeSourceFiles} native of {p.TotalConvertibleFiles} files)");
            sb.AppendLine();
            sb.AppendLine("| Candidate | File | Stratum | Operator | Expected check | Addressable | Verdict | Reason |");
            sb.AppendLine("|---|---|---|---|---|:---:|:---:|---|");
            foreach (var d in p.Accounting.Dispositions)
            {
                var addr = d.Stratum == DefectStratum.Expressible
                    ? (!d.AddressabilityProbed ? "-" : !d.AddressabilityDeterminable ? "indet" : d.VerificationAddressable ? "yes" : "no")
                    : "n/a";
                sb.AppendLine($"| {d.CandidateId} | {d.FileRelPath} | {d.Stratum} | {d.Operator} | {d.ExpectedCheck ?? "-"} | {addr} | {(d.Eligible ? "ELIGIBLE" : "excluded")} | {d.Reason} |");
            }
            sb.AppendLine();
        }

        AppendAddressabilitySection(sb, run);

        sb.AppendLine("## Eligible task bundles");
        sb.AppendLine();
        foreach (var p in run.Projects)
            foreach (var b in p.Bundles)
            {
                var checkTag = b.EligibilityProof.VerificationCheckFired != null
                    ? $", fires **{b.EligibilityProof.VerificationCheckFired}**"
                    : "";
                sb.AppendLine($"- `{b.TaskId}` — [{b.EligibilityProof.Stratum}] {b.Provenance.Source} `{b.Provenance.OperatorDescription}` in `{b.Provenance.MutatedFileRelPath}`:{b.Provenance.Line}; " +
                    $"held-out: {string.Join(", ", b.HeldOut.Select(h => h.TestName))}; " +
                    $"native={b.EligibilityProof.MutatedFileConvertedNative}, attribution={b.EligibilityProof.AttributionOutcome}{checkTag}");
            }
        sb.AppendLine();

        sb.AppendLine("## Interpretation note (recorded)");
        sb.AppendLine();
        sb.AppendLine("The eligibility rate is itself a Slice-E signal: too low a rate on the OSS corpus means insufficient ");
        sb.AppendLine("native-eligible surface to yield a decidable dry-run. **This synthetic rate is an UPPER BOUND**: both ");
        sb.AppendLine("synthetic projects are 100%-native, so clause-(a) exclusions are 0 here, whereas on OSS (Slice-B ");
        sb.AppendLine("NativeFraction 0.40–0.53) clause (a) will exclude heavily — the OSS rate will be materially lower. ");
        sb.AppendLine("The fidelity bar is PROVISIONAL (pending A-1.4 tranche-2). The Calor arm works on machine-converted ");
        sb.AppendLine("§-syntax vs the C# arm's idiomatic original — a bias AGAINST Calor, so a PP-W2 win is conservative and ");
        sb.AppendLine("a loss is confounded with conversion idiom.");
        return sb.ToString();
    }

    /// <summary>
    /// The verification-addressability base-rate section (expressible stratum). Discloses, of the
    /// expressible sites the probe could resolve, the fraction whose Calor check the mutation actually
    /// makes fire — the honesty number that bounds how often real defects of these shapes would be
    /// Calor-catchable. Also names any expected check that NEVER fired (a gap), so the epoch's claim is
    /// not overstated.
    /// </summary>
    private static void AppendAddressabilitySection(StringBuilder sb, TaskGenRunResult run)
    {
        var expressible = run.Projects
            .SelectMany(p => p.Accounting.Dispositions)
            .Where(d => d.Stratum == DefectStratum.Expressible)
            .ToList();
        if (expressible.Count == 0) return;

        var probedDeterminable = expressible.Count(d => d.AddressabilityProbed && d.AddressabilityDeterminable);
        var addressable = expressible.Count(d => d.VerificationAddressable);
        var indeterminable = expressible.Count(d => d.AddressabilityProbed && !d.AddressabilityDeterminable);
        var baseRate = probedDeterminable == 0 ? 0.0 : (double)addressable / probedDeterminable;

        sb.AppendLine("## Verification-addressability (expressible stratum) — base-rate honesty");
        sb.AppendLine();
        sb.AppendLine("A defect is *expressible* (verification-addressable) only if the differential probe confirms Calor's ");
        sb.AppendLine("expected check is INTRODUCED by the mutation on the converted arm (fires on the mutated conversion, ");
        sb.AppendLine("absent on the clean one). The base rate below **bounds how often real defects of these shapes would be ");
        sb.AppendLine("Calor-catchable** and MUST be read alongside any escaped-bug claim so the claim is not overstated.");
        sb.AppendLine();
        sb.AppendLine($"- Expressible candidates considered: **{expressible.Count}**");
        sb.AppendLine($"- Probed & determinable: **{probedDeterminable}** (indeterminable — a conversion did not compile: {indeterminable})");
        sb.AppendLine($"- Verification-addressable (check introduced by the mutation): **{addressable}**");
        sb.AppendLine($"- **Verification-addressability base rate: {Pct(baseRate, 0)}** (addressable / probed-determinable)");
        sb.AppendLine();
        sb.AppendLine("| Expected check | Operator class | Probed-determinable | Addressable | Rate |");
        sb.AppendLine("|---|---|---:|---:|---:|");
        foreach (var g in expressible.Where(d => d.ExpectedCheck != null)
                     .GroupBy(d => (d.ExpectedCheck!, d.Operator))
                     .OrderBy(g => g.Key.Item1))
        {
            var probed = g.Count(d => d.AddressabilityProbed && d.AddressabilityDeterminable);
            var addr = g.Count(d => d.VerificationAddressable);
            var rate = probed == 0 ? "n/a" : Pct((double)addr / probed, 0);
            sb.AppendLine($"| {g.Key.Item1} | {g.Key.Item2} | {probed} | {addr} | {rate} |");
        }
        sb.AppendLine();

        var neverFired = expressible.Where(d => d.ExpectedCheck != null)
            .GroupBy(d => d.ExpectedCheck!)
            .Where(g => g.All(d => !d.VerificationAddressable) && g.Any(d => d.AddressabilityProbed && d.AddressabilityDeterminable))
            .Select(g => g.Key)
            .ToList();
        if (neverFired.Count > 0)
        {
            sb.AppendLine($"**Gap disclosed:** the following expected check(s) NEVER fired on any probed candidate — the ");
            sb.AppendLine($"defect class is real but not verification-addressable by the current checker on converted code: ");
            sb.AppendLine($"**{string.Join(", ", neverFired)}**. Notably, Calor's null bug-pattern models Option/Result ");
            sb.AppendLine("`.unwrap`/`.expect` shapes (not plain reference null-deref), and the index-OOB checker keys on ");
            sb.AppendLine("specific array-access call shapes — converted corpus code may not lower to either, so those strata ");
            sb.AppendLine("may show a 0% base rate. This is reported, not hidden.");
            sb.AppendLine();
        }
    }

    private static int FidelityPassingWithTasks(TaskGenRunResult run) =>
        run.Projects.Count(p => p.Bundles.Count > 0
            && run.FidelityGate.Decisions.Any(d => d.ProjectName == p.ProjectName && d.Passed));
}
