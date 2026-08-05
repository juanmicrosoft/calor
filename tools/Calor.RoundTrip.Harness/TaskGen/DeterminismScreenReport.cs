using System.Text;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Renders the D-S5.1 determinism screen.</summary>
public static class DeterminismScreenReport
{
    private static string Glyphs(IReadOnlyList<DeterminismScreen.RunRecord> runs, Func<DeterminismScreen.RunRecord, bool> ok) =>
        runs.Count == 0 ? "—" : string.Concat(runs.Select(r => ok(r) ? "✓" : "✗"));

    public static string Render(DeterminismScreen.Result r)
    {
        var n = DeterminismScreen.RequiredConsecutiveGreenRuns;
        var sb = new StringBuilder();
        sb.AppendLine("# D-S5.1 determinism screen");
        sb.AppendLine();
        sb.AppendLine($"Gates §0.2: held-out tests must be deterministic, verified by **{n} consecutive green runs against");
        sb.AppendLine("the reference solution** — applied per plan D-S5.1 **on both arms**. The two arms have different");
        sb.AppendLine("references: the C# arm's is the pristine corpus; the Calor arm's is the **converted, unmutated**");
        sb.AppendLine("program. **M-S3 counts only screened tasks**, so a task failing here is not epoch-eligible however");
        sb.AppendLine("real its defect is.");
        sb.AppendLine();
        sb.AppendLine($"**{r.Screened} of {r.Tasks.Count} tasks pass.**");
        if (r.PassingWithIntermittentDefect > 0)
            sb.AppendLine($" {r.PassingWithIntermittentDefect} pass but their defect did not manifest on every mutated run (reported, not gated).");
        if (r.MutatedArmUnavailable > 0)
            sb.AppendLine($" {r.MutatedArmUnavailable} had no runnable mutated arm — reported as unavailable, **not** as intermittent.");
        sb.AppendLine();
        sb.AppendLine("| Task | Project | C# reference | Calor reference | Mutated (reported) | Verdict |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var t in r.Tasks)
            sb.AppendLine($"| `{t.TaskId}` | {t.ProjectName} | {Glyphs(t.CSharpReferenceRuns, x => x.Green)} | " +
                          $"{Glyphs(t.CalorReferenceRuns, x => x.Green)} | {t.MutatedNote} | {t.Verdict} |");
        sb.AppendLine();
        sb.AppendLine("Reference columns: ✓ = the held-out set was green (required on **both**, 5/5).");
        sb.AppendLine("Mutated column is reported only — §0.2 constrains the reference, not the defect's manifestation rate.");
        sb.AppendLine();
        sb.AppendLine("## Power of this instrument, stated");
        sb.AppendLine();
        sb.AppendLine($"Detection at per-run flake rate *p* is 1−(1−p)^{n}: ~97% at p=0.5, **41% at p=0.1, 10% at p=0.02**.");
        sb.AppendLine("Five runs bound *gross* flakiness only. The filtered runs are also far smaller than a full-suite");
        sb.AppendLine("pass, so they carry little of the xUnit parallel-collection interleaving — the very mechanism that");
        sb.AppendLine("produced a real nondeterminism defect in this epoch's own instrument (`ArmsDiverge` 3→0). A pass");
        sb.AppendLine("here is not a claim that the held-out set is deterministic under all conditions.");
        sb.AppendLine();
        sb.AppendLine("## Provenance");
        sb.AppendLine();
        sb.AppendLine($"- Harness commit: `{r.HarnessCommit}`");
        foreach (var (proj, sha) in r.CorpusPins.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.AppendLine($"- {proj}: `{sha}`");
        sb.AppendLine();
        sb.AppendLine("Per-run test counts and durations are in `determinism-screen.json`.");
        return sb.ToString();
    }
}
