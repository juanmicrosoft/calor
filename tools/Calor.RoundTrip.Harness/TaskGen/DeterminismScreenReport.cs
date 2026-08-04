using System.Text;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Renders the D-S5.1 determinism screen.</summary>
public static class DeterminismScreenReport
{
    public static string Render(DeterminismScreen.Result r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# D-S5.1 determinism screen");
        sb.AppendLine();
        sb.AppendLine($"Gates §0.2: held-out tests must be deterministic, verified by **{DeterminismScreen.RequiredConsecutiveGreenRuns} consecutive green runs");
        sb.AppendLine("against the reference solution** — here, the pristine (unmutated) corpus. **M-S3 counts only");
        sb.AppendLine("screened tasks**, so a task failing this screen is not epoch-eligible however real its defect is.");
        sb.AppendLine();
        sb.AppendLine($"**{r.Screened} of {r.Tasks.Count} tasks pass.**");
        if (r.PassingWithIntermittentDefect > 0)
            sb.AppendLine($" {r.PassingWithIntermittentDefect} pass the gate but their defect did not manifest on every mutated run (reported, not gated).");
        sb.AppendLine();
        sb.AppendLine("| Task | Project | Reference runs | Mutated runs (reported) | Verdict |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var t in r.Tasks)
        {
            var refStr = t.ReferenceRuns.Count == 0 ? "—" : string.Concat(t.ReferenceRuns.Select(g => g ? "✓" : "✗"));
            var mutStr = t.MutatedRuns.Count == 0 ? "—" : string.Concat(t.MutatedRuns.Select(f => f ? "✓" : "·"));
            sb.AppendLine($"| `{t.TaskId}` | {t.ProjectName} | {refStr} | {mutStr} | {t.Verdict} |");
        }
        sb.AppendLine();
        sb.AppendLine("Reference column: ✓ = held-out set green on the pristine reference (required).");
        sb.AppendLine("Mutated column: ✓ = the defect manifested (held-out set failed). Reported only — gates §0.2");
        sb.AppendLine("constrains the reference, not the defect's manifestation rate.");
        return sb.ToString();
    }
}
