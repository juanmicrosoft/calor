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
        sb.AppendLine($"- Project NativeFraction at generation: {proof.ProjectNativeFraction:P1}");
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
            sb.AppendLine($"| {d.ProjectName} | {d.NativeFraction:P1} | {(d.Passed ? "yes" : "no")} | {d.Reason} |");
        sb.AppendLine();

        sb.AppendLine("## Exclusion accounting (D-W4.1 — every candidate counted, no silent shrinkage)");
        sb.AppendLine();
        sb.AppendLine("| Project | Enumerated | Considered | Eligible | Excl (a) | Excl (b) | Excl attribution | Excl no-cover | Excl no-compile | Eligibility rate |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var p in run.Projects)
        {
            var a = p.Accounting;
            sb.AppendLine($"| {p.ProjectName} | {p.TotalEnumeratedCandidates} | {a.Considered} | {a.Eligible} | {a.ExcludedClauseA} | {a.ExcludedClauseB} | {a.ExcludedAttribution} | {a.ExcludedNoCoveringTest} | {a.ExcludedDidNotCompile} | {a.EligibilityRate:P0} |");
        }
        var totEnumerated = run.Projects.Sum(p => p.TotalEnumeratedCandidates);
        var totConsidered = run.Projects.Sum(p => p.Accounting.Considered);
        var totEligible = run.Projects.Sum(p => p.Accounting.Eligible);
        sb.AppendLine($"| **TOTAL** | **{totEnumerated}** | **{totConsidered}** | **{totEligible}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedClauseA)}** | **{run.Projects.Sum(p => p.Accounting.ExcludedClauseB)}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedAttribution)}** | **{run.Projects.Sum(p => p.Accounting.ExcludedNoCoveringTest)}** | " +
            $"**{run.Projects.Sum(p => p.Accounting.ExcludedDidNotCompile)}** | " +
            $"**{(totConsidered == 0 ? 0 : (double)totEligible / totConsidered):P0}** |");
        sb.AppendLine();
        sb.AppendLine("Eligibility rate is Eligible/Considered (Considered = evaluated candidates; Enumerated is the ");
        sb.AppendLine("full sited set before the per-project cap / early-stop, so the rate is honest about truncation).");
        sb.AppendLine();

        sb.AppendLine("### Per-candidate dispositions");
        sb.AppendLine();
        foreach (var p in run.Projects)
        {
            sb.AppendLine($"**{p.ProjectName}** (NativeFraction {p.NativeFraction:P1}, {p.NativeSourceFiles} native of {p.TotalConvertibleFiles} files)");
            sb.AppendLine();
            sb.AppendLine("| Candidate | File | Operator | Verdict | Reason | Explanation |");
            sb.AppendLine("|---|---|---|:---:|---|---|");
            foreach (var d in p.Accounting.Dispositions)
                sb.AppendLine($"| {d.CandidateId} | {d.FileRelPath} | {d.Operator} | {(d.Eligible ? "ELIGIBLE" : "excluded")} | {d.Reason} | {d.Explanation} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Eligible task bundles");
        sb.AppendLine();
        foreach (var p in run.Projects)
            foreach (var b in p.Bundles)
            {
                sb.AppendLine($"- `{b.TaskId}` — {b.Provenance.Source} `{b.Provenance.OperatorDescription}` in `{b.Provenance.MutatedFileRelPath}`:{b.Provenance.Line}; " +
                    $"held-out: {string.Join(", ", b.HeldOut.Select(h => h.TestName))}; " +
                    $"native={b.EligibilityProof.MutatedFileConvertedNative}, attribution={b.EligibilityProof.AttributionOutcome}");
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

    private static int FidelityPassingWithTasks(TaskGenRunResult run) =>
        run.Projects.Count(p => p.Bundles.Count > 0
            && run.FidelityGate.Decisions.Any(d => d.ProjectName == p.ProjectName && d.Passed));
}
