using System.Text.RegularExpressions;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// The WS-S1 failure-cause census (substrate plan §10 deviation, gates A-1.6(b)).
///
/// <para>D-S1.1's loss-ledger work-list covers 4.7% of the non-native gap; 95.3% is files that never
/// converted or compiled. This buckets those failures by <b>cause</b>, which is the statistic the
/// replacement gate decides on: <b>top-3 causes ≥ 50% → fidelity is a work-list, continue WS-S1;
/// otherwise → PP-S1 = miss.</b> The rule is exhaustive by construction — anything that is not
/// "top-3 ≥ 50%" is a miss — and it is encoded here rather than applied by hand so it cannot be
/// softened once the numbers are visible.</para>
/// </summary>
public static class FailureCensus
{
    /// <summary>The pre-committed threshold. Not a tunable.</summary>
    public const double Top3ContinueThreshold = 0.50;

    public sealed class CauseBucket
    {
        public required string Cause { get; init; }
        public required int Files { get; init; }
        public required IReadOnlyList<string> ExampleFiles { get; init; }
    }

    public sealed class Result
    {
        public required int TotalFailures { get; init; }
        public required IReadOnlyList<CauseBucket> Causes { get; init; }

        /// <summary>Failures whose cause could not be extracted — counted, never dropped.</summary>
        public required int Unattributed { get; init; }

        public double Top3Share => TotalFailures == 0
            ? 0
            : (double)Causes.Take(3).Sum(c => c.Files) / TotalFailures;

        public double Top10Share => TotalFailures == 0
            ? 0
            : (double)Causes.Take(10).Sum(c => c.Files) / TotalFailures;

        /// <summary>
        /// The pre-committed gate. Undecidable when there are no failures to classify — that is not
        /// a pass, and it is not a miss either; it means the census had no input.
        /// </summary>
        public string Verdict =>
            TotalFailures == 0 ? "UNDECIDABLE — no failures to classify"
            : Top3Share >= Top3ContinueThreshold
                ? $"CONTINUE WS-S1 — top-3 causes cover {Top3Share:P0} ≥ {Top3ContinueThreshold:P0}: fidelity is a work-list"
                : $"PP-S1 = MISS — top-3 causes cover {Top3Share:P0} < {Top3ContinueThreshold:P0}: long tail, not a work-list";
    }

    /// <summary>
    /// Normalize a build/emit diagnostic to a stable cause key. Compiler codes (CS####, Calor####)
    /// are the natural bucket; anything else falls back to a trimmed message shape so it is counted
    /// rather than silently discarded.
    /// </summary>
    public static string NormalizeCause(string status, IEnumerable<string> errors)
    {
        foreach (var e in errors)
        {
            var m = Regex.Match(e, @"\b(CS\d{4}|Calor\d{4})\b");
            if (m.Success) return $"{status}:{m.Groups[1].Value}";
        }

        var first = errors.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) return $"{status}:unattributed";

        // Collapse paths, positions and quoted identifiers so the same defect shape buckets together.
        var shape = Regex.Replace(first, @"[^\s]+\.cs\(\d+,\d+\)", "<file>");
        shape = Regex.Replace(shape, @"'[^']*'", "'<id>'");
        shape = shape.Trim();
        if (shape.Length > 80) shape = shape[..80];
        return $"{status}:{shape}";
    }

    public static Result Analyse(IEnumerable<(string Status, string Path, IReadOnlyList<string> Errors)> failures)
    {
        var byCause = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unattributed = 0;
        var total = 0;

        foreach (var (status, path, errors) in failures)
        {
            total++;
            var cause = NormalizeCause(status, errors);
            if (cause.EndsWith(":unattributed", StringComparison.Ordinal)) unattributed++;
            if (!byCause.TryGetValue(cause, out var list)) byCause[cause] = list = [];
            list.Add(path);
        }

        var causes = byCause
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new CauseBucket
            {
                Cause = kv.Key,
                Files = kv.Value.Count,
                ExampleFiles = kv.Value.OrderBy(p => p, StringComparer.Ordinal).Take(3).ToList(),
            })
            .ToList();

        return new Result { TotalFailures = total, Causes = causes, Unattributed = unattributed };
    }
}
