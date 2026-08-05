using System.Text;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>Renders the WS-S1 failure-cause census and its pre-committed verdict.</summary>
public static class FailureCensusReport
{
    public static string Render(FailureCensus.Result r, IReadOnlyDictionary<string, int> perProject)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# WS-S1 failure-cause census");
        sb.AppendLine();
        sb.AppendLine("The D-S1.1 loss ledger covers 4.7% of the non-native gap; 95.3% is files that never converted");
        sb.AppendLine("or compiled. This buckets those by **cause**, which is what the replacement gate decides on.");
        sb.AppendLine();
        sb.AppendLine($"**Pre-committed rule** (gates A-1.6(b), substrate plan §10): top-3 causes ≥ ");
        sb.AppendLine($"{FailureCensus.Pct(FailureCensus.Top3ContinueThreshold)} → continue WS-S1; **otherwise → PP-S1 = miss**. Exhaustive by construction.");
        sb.AppendLine();
        sb.AppendLine($"## Verdict: {r.Verdict}");
        sb.AppendLine();
        sb.AppendLine($"- Failures classified: **{r.TotalFailures}**");
        sb.AppendLine($"- Top-3 share: **{FailureCensus.Pct(r.Top3Share)}**   ·   top-10 share: {FailureCensus.Pct(r.Top10Share)}");
        sb.AppendLine($"- Distinct causes: {r.Causes.Count}");
        if (r.Unattributed > 0)
            sb.AppendLine($"- ⚠ **{r.Unattributed} failure(s) had no extractable cause** and are bucketed as `unattributed`. They count in the denominator; a large share here means the census is measuring the harness's record-keeping, not the converter.");
        sb.AppendLine();
        sb.AppendLine("### Failures by project");
        sb.AppendLine();
        foreach (var (proj, n) in perProject.OrderByDescending(kv => kv.Value))
            sb.AppendLine($"- {proj}: {n}");
        sb.AppendLine();
        sb.AppendLine("### Causes, ranked");
        sb.AppendLine();
        sb.AppendLine("| # | Cause | Files | Share | Example |");
        sb.AppendLine("|---:|---|---:|---:|---|");
        var i = 0;
        foreach (var c in r.Causes)
        {
            i++;
            var share = r.TotalFailures == 0 ? 0 : (double)c.Files / r.TotalFailures;
            sb.AppendLine($"| {i} | `{c.Cause}` | {c.Files} | {FailureCensus.Pct(share)} | `{c.ExampleFiles.FirstOrDefault()}` |");
        }
        sb.AppendLine();
        sb.AppendLine("A cause key is `Status:CS####` where a compiler code was recoverable, else a normalized");
        sb.AppendLine("message shape (paths, positions and quoted identifiers collapsed) so the same defect buckets");
        sb.AppendLine("together. Reverted files carry the build diagnostics attributed to them during recovery.");
        return sb.ToString();
    }
}
