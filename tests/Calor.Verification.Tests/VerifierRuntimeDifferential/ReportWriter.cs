using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The default is Environment.NewLine, CRLF on Windows (#1135, contract determinism row
        // platform-CommittedReportsMatchGeneratedOracle). The committed report is LF.
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ToJson(DifferentialReport report) =>
        JsonSerializer.Serialize(report, JsonOptions) + "\n";

    public static string ToMarkdown(DifferentialReport report)
    {
        var coverage = report.Coverage;
        var builder = new StringBuilder();
        Line(builder, "# Verifier ↔ Generated Runtime Differential (F-4)");
        Line(builder);
        Line(builder, $"- **Result:** {(report.Passed ? "PASS" : "FAIL")}");
        Line(builder, $"- **Whitelist hash:** `{report.WhitelistSha256}`");
        Line(builder, $"- **Mismatches:** {coverage.Mismatches}");
        Line(builder,
            $"- **Forms solver-handled:** {coverage.FormsCovered}/{coverage.FormsWhitelisted} " +
            $"({Percent(coverage.FormCoverageFraction)})");
        Line(builder,
            $"- **Forms eliding:** {coverage.FormsEliding}/{coverage.FormsWhitelisted} " +
            $"({Percent(coverage.ElisionCoverageFraction)})");
        Line(builder,
            $"- **Cartesian cells solver-handled:** {coverage.MatrixCellsCovered}/{coverage.MatrixCellsApplicable} " +
            $"({Percent(coverage.MatrixCoverageFraction)})");
        Line(builder,
            $"- **Cartesian cells registered:** {coverage.MatrixCellsRegistered}");
        Line(builder,
            $"- **Generated cases:** {coverage.CasesGenerated} " +
            $"(3 positions × depths 1–{report.MaximumNestingDepth} × 2 polarities per applicable form)");
        Line(builder);

        Line(builder, "## Typed outcomes");
        Line(builder);
        Line(builder, "| Outcome | Cases |");
        Line(builder, "|---|---:|");
        foreach (var (status, count) in report.OutcomeCounts)
            Line(builder, $"| `{status}` | {count} |");
        Line(builder);

        Line(builder, "## Coverage by category");
        Line(builder);
        Line(builder, "| Category | Whitelisted | Applicable | Solver-handled | Eliding | Mismatches |");
        Line(builder, "|---|---:|---:|---:|---:|---:|");
        foreach (var category in report.Forms.GroupBy(form => form.Category, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Line(builder,
                $"| `{category.Key}` | {category.Count()} | " +
                $"{category.Count(form => form.Applicable)} | " +
                $"{category.Count(form => form.SolverHandled)} | " +
                $"{category.Count(form => form.Elides)} | " +
                $"{category.Sum(form => form.Mismatches)} |");
        }
        Line(builder);

        Line(builder, "## Encoding notes");
        Line(builder);
        foreach (var (form, note) in report.EncodingNotes)
            Line(builder, $"- `{form}` — {note}");
        Line(builder);

        Line(builder, "## Explicit Assumed allowances");
        Line(builder);
        Line(builder,
            "`Assumed` is accepted only for provable cells whose form lists the exact production " +
            "assumption set below. Refutable cells must always be `Refuted`.");
        Line(builder);
        foreach (var form in report.Forms.Where(form => form.AllowedAssumptions.Count > 0))
        {
            Line(builder,
                $"- `{form.Id}` — " +
                string.Join("; ", form.AllowedAssumptions.Select(ShortAssumption)));
        }
        Line(builder);

        Line(builder, "## Per-form coverage");
        Line(builder);
        Line(builder, "| Form | Solver-handled | Cases | Pre | Post | Obligation | Elides | Mismatches |");
        Line(builder, "|---|:---:|---:|---:|---:|---:|:---:|---:|");
        foreach (var form in report.Forms)
        {
            Line(builder,
                $"| `{form.Id}` | {(form.SolverHandled ? "yes" : "no")} | " +
                $"{form.Cases} | {form.PreconditionCases} | " +
                $"{form.PostconditionCases} | {form.ObligationCases} | " +
                $"{(form.Elides ? "yes" : "no")} | {form.Mismatches} |");
        }
        Line(builder);

        var excluded = report.Forms.Where(form => !form.Applicable).ToList();
        if (excluded.Count > 0)
        {
            Line(builder, "## Registered but not runtime-encodable");
            Line(builder);
            foreach (var form in excluded)
                Line(builder, $"- `{form.Id}` — {form.ExclusionReason}");
            Line(builder);
        }

        Line(builder, "## Fail-safe controls");
        Line(builder);
        Line(builder, "| Scenario | Channel | Typed status | Guard retained | Runtime | Result |");
        Line(builder, "|---|---|---|:---:|---|:---:|");
        foreach (var control in report.FailSafeControls)
        {
            Line(builder,
                $"| {control.Scenario} | {control.Channel} | `{control.Status}` | " +
                $"{(control.GuardRetained ? "yes" : "no")} | `{control.RuntimeVerdict}` | " +
                $"{(control.Passed ? "pass" : "fail")} |");
        }
        Line(builder);

        Line(builder, "## Oracle");
        Line(builder);
        Line(builder,
            "Every case is emitted twice. The runtime assembly is compiled from the guard-forced " +
            "emission (`ElideProvenGuards = false`); the elision-enabled emission " +
            "(`ElideProvenGuards = true`, the v0.15 default) is inspected separately " +
            "to measure actual postcondition/obligation elision. `proven`/`discharged` must execute " +
            "without a guard failure, `refuted`/`failed` must fire the generated guard, and every " +
            "non-decisive status must retain the guard. The generator also requires the declared " +
            "target form to occur in every base expression and rejects vacuous proofs.");
        Line(builder);

        if (report.Mismatches.Count > 0)
        {
            Line(builder, "## Mismatches");
            Line(builder);
            foreach (var mismatch in report.Mismatches)
                Line(builder, $"- `{mismatch.Id}` / `{mismatch.FormId}` — {mismatch.Detail}");
        }

        return builder.ToString();
    }

    // '\n', not AppendLine: Environment.NewLine is CRLF on Windows (#1135). Writing LF directly
    // (rather than replacing CRLF afterwards) leaves any CR inside a detail visible to the gate.
    private static StringBuilder Line(StringBuilder builder, string text = "") =>
        builder.Append(text).Append('\n');

    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string ShortAssumption(string assumption)
    {
        var separator = assumption.IndexOf(" — ", StringComparison.Ordinal);
        return separator < 0 ? assumption : assumption[..separator];
    }
}
