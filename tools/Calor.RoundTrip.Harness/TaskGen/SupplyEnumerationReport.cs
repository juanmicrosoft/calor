using System.Text;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// Renders the S1 pre-pass result. Every number is reported with the caveat that governs how it may
/// be used — the close-out's repeated lesson was that caveats degrade one document at a time, so
/// they are emitted by the tool rather than left for a human to re-attach downstream.
/// </summary>
public static class SupplyEnumerationReport
{
    public static string Render(SupplyEnumeration.Result r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# v0.12 S1 — expressible-stratum supply enumeration (pre-pass)");
        sb.AppendLine();
        sb.AppendLine("**Conversion-only.** No builds, no test runs, no recovery, no bundles, and no");
        sb.AppendLine("eligibility evaluation — which is what permits this to run *before* the A-1.5 freeze");
        sb.AppendLine("(plan §3 constraint (a)).");
        sb.AppendLine();
        sb.AppendLine("**Upper bound, not realized supply.** Build and `RecoverBuildAsync` are skipped, so a");
        sb.AppendLine("file that converted but does not compile is still counted native here — recovery is");
        sb.AppendLine("precisely what would revert it. Every later eligibility clause only removes candidates,");
        sb.AppendLine("so these figures bound eligibility from above. That is the correct direction for a");
        sb.AppendLine("\"is there conceivably enough supply?\" go/no-go, and the wrong number to quote as supply.");
        sb.AppendLine();

        sb.AppendLine("## Supply by project");
        sb.AppendLine();
        sb.AppendLine("| Project | Native files | With-loss files | Supply (native) | Supply (with-loss) | with-loss / native |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var p in r.Projects)
        {
            var ratio = p.WithLossOverNativeRatio is { } v ? v.ToString("F2") : "n/a";
            sb.AppendLine($"| {p.ProjectName} | {p.NativeFiles} | {p.WithLossFiles} | **{p.SupplyNative}** | {p.SupplyWithLoss} | {ratio} |");
        }
        sb.AppendLine($"| **TOTAL** | | | **{r.TotalSupplyNative}** | {r.TotalSupplyWithLoss} | " +
                      $"{(r.PooledRatio is { } pr ? pr.ToString("F2") : "n/a")} |");
        sb.AppendLine();

        sb.AppendLine("## D-5 — region-granularity clause (a)");
        sb.AppendLine();
        sb.AppendLine($"Pre-committed rule (S1 kickoff): accept site-level clause (a) iff with-loss/native ≥ {SupplyEnumeration.Result.D5AcceptThreshold:F2}.");
        sb.AppendLine();
        sb.AppendLine($"**{r.D5Verdict}**");
        sb.AppendLine();
        sb.AppendLine("Per-project ratios are in the table above; a pooled figure must not conceal a split.");
        sb.AppendLine();

        sb.AppendLine("## Candidates by operator");
        sb.AppendLine();
        foreach (var p in r.Projects)
        {
            sb.AppendLine($"### {p.ProjectName}");
            sb.AppendLine();
            if (p.NativeByOperator.Count == 0 && p.WithLossByOperator.Count == 0)
            {
                sb.AppendLine("_No expressible candidates enumerated in either population._");
                sb.AppendLine();
                continue;
            }
            sb.AppendLine("| Operator | Native | With-loss |");
            sb.AppendLine("|---|---:|---:|");
            foreach (var op in p.NativeByOperator.Keys.Union(p.WithLossByOperator.Keys).OrderBy(k => k, StringComparer.Ordinal))
                sb.AppendLine($"| {op} | {p.NativeByOperator.GetValueOrDefault(op)} | {p.WithLossByOperator.GetValueOrDefault(op)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Clustering (native candidates by file)");
        sb.AppendLine();
        sb.AppendLine("The probe's `--max-candidates` cap takes a **lexicographic prefix**, not a sample, so");
        sb.AppendLine("candidates concentrated in few files mean the probe's effective *n* is far below its");
        sb.AppendLine("nominal *n* (S1 kickoff D-3). This table is what makes that visible before the probe runs.");
        sb.AppendLine();
        foreach (var p in r.Projects)
        {
            sb.AppendLine($"### {p.ProjectName} — {p.NativeByFile.Count} native file(s) carrying candidates");
            sb.AppendLine();
            if (p.NativeByFile.Count == 0) { sb.AppendLine("_none_"); sb.AppendLine(); continue; }
            foreach (var (file, n) in p.NativeByFile.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                sb.AppendLine($"- `{file}` — {n}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
