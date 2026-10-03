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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ToJson(DifferentialReport report) =>
        JsonSerializer.Serialize(report, JsonOptions) + "\n";

    public static string ToMarkdown(DifferentialReport report)
    {
        var coverage = report.Coverage;
        var builder = new StringBuilder();
        builder.AppendLine("# Verifier ↔ Generated Runtime Differential (F-4)");
        builder.AppendLine();
        builder.AppendLine($"- **Result:** {(report.Passed ? "PASS" : "FAIL")}");
        builder.AppendLine($"- **Whitelist hash:** `{report.WhitelistSha256}`");
        builder.AppendLine($"- **Mismatches:** {coverage.Mismatches}");
        builder.AppendLine(
            $"- **Forms solver-handled:** {coverage.FormsCovered}/{coverage.FormsWhitelisted} " +
            $"({Percent(coverage.FormCoverageFraction)})");
        builder.AppendLine(
            $"- **Forms eliding:** {coverage.FormsEliding}/{coverage.FormsWhitelisted} " +
            $"({Percent(coverage.ElisionCoverageFraction)})");
        builder.AppendLine(
            $"- **Cartesian cells solver-handled:** {coverage.MatrixCellsCovered}/{coverage.MatrixCellsApplicable} " +
            $"({Percent(coverage.MatrixCoverageFraction)})");
        builder.AppendLine(
            $"- **Cartesian cells registered:** {coverage.MatrixCellsRegistered}");
        builder.AppendLine(
            $"- **Generated cases:** {coverage.CasesGenerated} " +
            $"(3 positions × depths 1–{report.MaximumNestingDepth} × 2 polarities per applicable form)");
        builder.AppendLine();

        builder.AppendLine("## Typed outcomes");
        builder.AppendLine();
        builder.AppendLine("| Outcome | Cases |");
        builder.AppendLine("|---|---:|");
        foreach (var (status, count) in report.OutcomeCounts)
            builder.AppendLine($"| `{status}` | {count} |");
        builder.AppendLine();

        builder.AppendLine("## Coverage by category");
        builder.AppendLine();
        builder.AppendLine("| Category | Whitelisted | Applicable | Solver-handled | Eliding | Mismatches |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var category in report.Forms.GroupBy(form => form.Category, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"| `{category.Key}` | {category.Count()} | " +
                $"{category.Count(form => form.Applicable)} | " +
                $"{category.Count(form => form.SolverHandled)} | " +
                $"{category.Count(form => form.Elides)} | " +
                $"{category.Sum(form => form.Mismatches)} |");
        }
        builder.AppendLine();

        builder.AppendLine("## Encoding notes");
        builder.AppendLine();
        foreach (var (form, note) in report.EncodingNotes)
            builder.AppendLine($"- `{form}` — {note}");
        builder.AppendLine();

        builder.AppendLine("## Explicit Assumed allowances");
        builder.AppendLine();
        builder.AppendLine(
            "`Assumed` is accepted only for provable cells whose form lists the exact production " +
            "assumption set below. Refutable cells must always be `Refuted`.");
        builder.AppendLine();
        foreach (var form in report.Forms.Where(form => form.AllowedAssumptions.Count > 0))
        {
            builder.AppendLine(
                $"- `{form.Id}` — " +
                string.Join("; ", form.AllowedAssumptions.Select(ShortAssumption)));
        }
        builder.AppendLine();

        builder.AppendLine("## Per-form coverage");
        builder.AppendLine();
        builder.AppendLine("| Form | Solver-handled | Cases | Pre | Post | Obligation | Elides | Mismatches |");
        builder.AppendLine("|---|:---:|---:|---:|---:|---:|:---:|---:|");
        foreach (var form in report.Forms)
        {
            builder.AppendLine(
                $"| `{form.Id}` | {(form.SolverHandled ? "yes" : "no")} | " +
                $"{form.Cases} | {form.PreconditionCases} | " +
                $"{form.PostconditionCases} | {form.ObligationCases} | " +
                $"{(form.Elides ? "yes" : "no")} | {form.Mismatches} |");
        }
        builder.AppendLine();

        var excluded = report.Forms.Where(form => !form.Applicable).ToList();
        if (excluded.Count > 0)
        {
            builder.AppendLine("## Registered but not runtime-encodable");
            builder.AppendLine();
            foreach (var form in excluded)
                builder.AppendLine($"- `{form.Id}` — {form.ExclusionReason}");
            builder.AppendLine();
        }

        builder.AppendLine("## Fail-safe controls");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Channel | Typed status | Guard retained | Runtime | Result |");
        builder.AppendLine("|---|---|---|:---:|---|:---:|");
        foreach (var control in report.FailSafeControls)
        {
            builder.AppendLine(
                $"| {control.Scenario} | {control.Channel} | `{control.Status}` | " +
                $"{(control.GuardRetained ? "yes" : "no")} | `{control.RuntimeVerdict}` | " +
                $"{(control.Passed ? "pass" : "fail")} |");
        }
        builder.AppendLine();

        builder.AppendLine("## Oracle");
        builder.AppendLine();
        builder.AppendLine(
            "Every case is emitted twice. The runtime assembly is compiled from the guard-forced " +
            "emission (`ElideProvenGuards = false`); the elision-enabled emission " +
            "(`ElideProvenGuards = true`, the v0.15 default) is inspected separately " +
            "to measure actual postcondition/obligation elision. `proven`/`discharged` must execute " +
            "without a guard failure, `refuted`/`failed` must fire the generated guard, and every " +
            "non-decisive status must retain the guard. The generator also requires the declared " +
            "target form to occur in every base expression and rejects vacuous proofs.");
        builder.AppendLine();

        if (report.Mismatches.Count > 0)
        {
            builder.AppendLine("## Mismatches");
            builder.AppendLine();
            foreach (var mismatch in report.Mismatches)
                builder.AppendLine($"- `{mismatch.Id}` / `{mismatch.FormId}` — {mismatch.Detail}");
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string ShortAssumption(string assumption)
    {
        var separator = assumption.IndexOf(" — ", StringComparison.Ordinal);
        return separator < 0 ? assumption : assumption[..separator];
    }
}

/// <summary>
/// #1421 (0.24 G2): opt-in record for the registered verifier determinism protocol
/// (docs/plans/evidence/g2-1421/protocol.json). It writes nothing unless
/// CALOR_DETERMINISM_RECORD_DIR is set, and it never changes a verdict, the report, or an
/// assertion. Cells are written as compact JSON (no platform newline); generated reports are
/// written as the exact bytes the gate compares, so the protocol can compare them unnormalized.
/// </summary>
internal static class DeterminismRecord
{
    public const string DirectoryVariable = "CALOR_DETERMINISM_RECORD_DIR";

    private static readonly JsonSerializerOptions CellOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static void WriteCells(IReadOnlyList<CaseResult> results) => WriteCells(results, Configured());

    public static void WriteGenerated(string name, byte[] bytes) => WriteGenerated(name, bytes, Configured());

    internal static void WriteCells(IReadOnlyList<CaseResult> results, string? directory)
    {
        if (!string.IsNullOrEmpty(directory))
            File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(directory).FullName, "cells.json"),
                JsonSerializer.SerializeToUtf8Bytes(results, CellOptions));
    }

    internal static void WriteGenerated(string name, byte[] bytes, string? directory)
    {
        if (!string.IsNullOrEmpty(directory))
            File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(directory, "generated")).FullName, name), bytes);
    }

    private static string? Configured() => Environment.GetEnvironmentVariable(DirectoryVariable);
}
