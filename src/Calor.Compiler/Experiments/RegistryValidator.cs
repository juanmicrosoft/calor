namespace Calor.Compiler.Experiments;

/// <summary>
/// Enforces the append-only invariant on <c>docs/experiments/registry.json</c>:
/// once an entry has been committed (identified by its <c>id</c>), no field of
/// that entry may change. The only legal diff is the addition of net-new entries.
///
/// Per §5.0f of <c>docs/plans/calor-native-type-system-v2.md</c>, this check is the
/// authoritative enforcement layer (CI-side). The client-side pre-commit hook runs
/// the same logic for fast feedback but is not binding — CI is.
///
/// Corrections to existing entries are expressed as new entries with
/// <c>supersedes: &lt;predecessor-id&gt;</c>, preserving the original entry intact.
/// </summary>
public static class RegistryValidator
{
    /// <summary>
    /// Compare the base (target branch) registry against the head (PR) registry.
    /// Returns a structured result describing every violation found.
    /// </summary>
    public static ValidationResult Validate(RegistryDocument baseDoc, RegistryDocument headDoc)
    {
        var violations = new List<ValidationViolation>();

        // Index head entries by id for O(1) lookup.
        var headById = new Dictionary<string, RegistryEntry>(StringComparer.Ordinal);
        foreach (var entry in headDoc.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                violations.Add(new ValidationViolation(
                    Kind: "missing-id",
                    EntryId: "<unknown>",
                    Message: "Entry is missing a non-empty id field."));
                continue;
            }
            if (headById.ContainsKey(entry.Id))
            {
                violations.Add(new ValidationViolation(
                    Kind: "duplicate-id",
                    EntryId: entry.Id,
                    Message: $"Duplicate id '{entry.Id}' in head registry. Each entry must have a unique id; use 'supersedes' to chain revisions."));
                continue;
            }
            headById[entry.Id] = entry;
        }

        // For each entry in base, verify it still exists in head and is field-identical.
        foreach (var baseEntry in baseDoc.Entries)
        {
            if (!headById.TryGetValue(baseEntry.Id, out var headEntry))
            {
                violations.Add(new ValidationViolation(
                    Kind: "entry-deleted",
                    EntryId: baseEntry.Id,
                    Message: $"Entry '{baseEntry.Id}' was removed from the head registry. The registry is append-only — removal is not permitted."));
                continue;
            }

            var fieldDiffs = DiffEntryFields(baseEntry, headEntry);
            foreach (var field in fieldDiffs)
            {
                violations.Add(new ValidationViolation(
                    Kind: "field-modified",
                    EntryId: baseEntry.Id,
                    Message: $"Entry '{baseEntry.Id}' field '{field}' was modified. " +
                             $"Append-only: to correct a prior entry, add a new entry with supersedes='{baseEntry.Id}'."));
            }
        }

        // Net-new entries in head are always allowed — no violation added.
        var baseIds = new HashSet<string>(baseDoc.Entries.Select(e => e.Id), StringComparer.Ordinal);
        var addedCount = headDoc.Entries.Count(e => !baseIds.Contains(e.Id));

        return new ValidationResult(violations.Count == 0, addedCount, violations);
    }

    /// <summary>
    /// Return the names of fields whose values differ between two entries.
    /// </summary>
    private static IEnumerable<string> DiffEntryFields(RegistryEntry a, RegistryEntry b)
    {
        if (!string.Equals(a.Tag, b.Tag, StringComparison.Ordinal)) yield return "tag";
        if (!string.Equals(a.Hypothesis, b.Hypothesis, StringComparison.Ordinal)) yield return "hypothesis";
        if (!string.Equals(a.TupleCodeClass, b.TupleCodeClass, StringComparison.Ordinal)) yield return "tuple_code_class";
        if (!string.Equals(a.TupleEffectDirection, b.TupleEffectDirection, StringComparison.Ordinal)) yield return "tuple_effect_direction";
        if (!string.Equals(a.Status, b.Status, StringComparison.Ordinal)) yield return "status";
        if (!string.Equals(a.Supersedes, b.Supersedes, StringComparison.Ordinal)) yield return "supersedes";
        if (!string.Equals(a.HoldOwner, b.HoldOwner, StringComparison.Ordinal)) yield return "hold_owner";
        if (!string.Equals(a.HoldStarted, b.HoldStarted, StringComparison.Ordinal)) yield return "hold_started";
        if (!string.Equals(a.QuarterlyReviewDue, b.QuarterlyReviewDue, StringComparison.Ordinal)) yield return "quarterly_review_due";
        if (!string.Equals(a.MetricChangeRationale, b.MetricChangeRationale, StringComparison.Ordinal)) yield return "metric_change_rationale";
        if (!string.Equals(a.CommitSha, b.CommitSha, StringComparison.Ordinal)) yield return "commit_sha";
        if (!string.Equals(a.MergedAt, b.MergedAt, StringComparison.Ordinal)) yield return "merged_at";
        if (!string.Equals(a.DecisionMemoUrl, b.DecisionMemoUrl, StringComparison.Ordinal)) yield return "decision_memo_url";
        if (!string.Equals(a.OverrideRationale, b.OverrideRationale, StringComparison.Ordinal)) yield return "override_rationale";
    }
}

/// <summary>
/// Overall verdict plus per-violation detail. Serialized to JSON for CI output.
/// </summary>
public sealed record ValidationResult(bool IsValid, int EntriesAdded, IReadOnlyList<ValidationViolation> Violations);

/// <summary>
/// One rule break found by the validator. <c>Kind</c> is a machine-readable tag;
/// <c>Message</c> is the human-readable explanation surfaced in PR checks.
/// </summary>
public sealed record ValidationViolation(string Kind, string EntryId, string Message);
