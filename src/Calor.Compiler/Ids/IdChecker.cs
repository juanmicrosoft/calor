using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Ids;

/// <summary>
/// Result of an ID check operation.
/// </summary>
public sealed class IdCheckResult
{
    /// <summary>
    /// IDs with missing values.
    /// </summary>
    public IReadOnlyList<IdEntry> MissingIds { get; init; } = Array.Empty<IdEntry>();

    /// <summary>
    /// IDs with invalid format.
    /// </summary>
    public IReadOnlyList<IdEntry> InvalidFormatIds { get; init; } = Array.Empty<IdEntry>();

    /// <summary>
    /// IDs with wrong prefix for their kind.
    /// </summary>
    public IReadOnlyList<IdEntry> WrongPrefixIds { get; init; } = Array.Empty<IdEntry>();

    /// <summary>
    /// Test IDs found in production code.
    /// </summary>
    public IReadOnlyList<IdEntry> TestIdsInProduction { get; init; } = Array.Empty<IdEntry>();

    /// <summary>
    /// Groups of duplicate IDs.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IdEntry>> DuplicateGroups { get; init; } = Array.Empty<IReadOnlyList<IdEntry>>();

    /// <summary>
    /// Returns true if there are no issues.
    /// </summary>
    public bool IsValid =>
        MissingIds.Count == 0 &&
        InvalidFormatIds.Count == 0 &&
        WrongPrefixIds.Count == 0 &&
        TestIdsInProduction.Count == 0 &&
        DuplicateGroups.Count == 0;

    /// <summary>
    /// Gets the total number of issues found.
    /// </summary>
    public int TotalIssues =>
        MissingIds.Count +
        InvalidFormatIds.Count +
        WrongPrefixIds.Count +
        TestIdsInProduction.Count +
        DuplicateGroups.Sum(g => g.Count - 1); // Duplicates count as n-1 issues (first is OK)
}

/// <summary>
/// Checks Calor IDs for validity, duplicates, and other issues.
/// </summary>
public static class IdChecker
{
    /// <summary>
    /// Checks a collection of ID entries for issues.
    /// </summary>
    /// <param name="entries">The ID entries to check.</param>
    /// <param name="allowTestIds">If true, allow test IDs in any location.</param>
    /// <returns>The check result with any issues found.</returns>
    public static IdCheckResult Check(IReadOnlyList<IdEntry> entries, bool allowTestIds = false)
    {
        var missingIds = new List<IdEntry>();
        var invalidFormatIds = new List<IdEntry>();
        var wrongPrefixIds = new List<IdEntry>();
        var testIdsInProduction = new List<IdEntry>();

        // Check individual entries
        foreach (var entry in entries)
        {
            var isTestPath = IdValidator.IsTestPath(entry.FilePath);
            var result = IdValidator.Validate(entry.Id, entry.Kind, isTestPath || allowTestIds);

            switch (result)
            {
                case IdValidationResult.Missing:
                    missingIds.Add(entry);
                    break;
                case IdValidationResult.InvalidFormat:
                    invalidFormatIds.Add(entry);
                    break;
                case IdValidationResult.WrongPrefix:
                    wrongPrefixIds.Add(entry);
                    break;
                case IdValidationResult.TestIdInProduction:
                    testIdsInProduction.Add(entry);
                    break;
            }
        }

        // Check for duplicates (only among non-missing, valid IDs)
        var validEntries = entries
            .Where(e => !string.IsNullOrEmpty(e.Id))
            .Where(e => !missingIds.Contains(e) && !invalidFormatIds.Contains(e) && !wrongPrefixIds.Contains(e))
            .ToList();

        var duplicateGroups = FindDuplicates(validEntries);

        return new IdCheckResult
        {
            MissingIds = missingIds,
            InvalidFormatIds = invalidFormatIds,
            WrongPrefixIds = wrongPrefixIds,
            TestIdsInProduction = testIdsInProduction,
            DuplicateGroups = duplicateGroups
        };
    }

    /// <summary>
    /// Finds groups of duplicate IDs.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<IdEntry>> FindDuplicates(IReadOnlyList<IdEntry> entries)
    {
        return entries
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<IdEntry>)g.ToList())
            .ToList();
    }

    /// <summary>
    /// Generates diagnostics from a check result.
    /// </summary>
    public static IEnumerable<Diagnostic> GenerateDiagnostics(IdCheckResult result)
    {
        foreach (var entry in result.MissingIds)
        {
            yield return new Diagnostic(
                DiagnosticCode.Calor0800,
                DiagnosticSeverity.Error,
                $"Missing ID for {entry.Kind} '{entry.Name}'",
                entry.FilePath,
                entry.Span.Line,
                entry.Span.Column);
        }

        foreach (var entry in result.InvalidFormatIds)
        {
            yield return new Diagnostic(
                DiagnosticCode.Calor0801,
                DiagnosticSeverity.Error,
                $"Invalid ID format '{entry.Id}' for {entry.Kind} '{entry.Name}'",
                entry.FilePath,
                entry.Span.Line,
                entry.Span.Column);
        }

        foreach (var entry in result.WrongPrefixIds)
        {
            var expectedPrefix = IdGenerator.GetPrefix(entry.Kind);
            yield return new Diagnostic(
                DiagnosticCode.Calor0802,
                DiagnosticSeverity.Error,
                $"Wrong prefix for {entry.Kind} '{entry.Name}': expected '{expectedPrefix}', got '{entry.Id}'",
                entry.FilePath,
                entry.Span.Line,
                entry.Span.Column);
        }

        foreach (var entry in result.TestIdsInProduction)
        {
            yield return new Diagnostic(
                DiagnosticCode.Calor0804,
                DiagnosticSeverity.Error,
                $"Test ID '{entry.Id}' not allowed in production code for {entry.Kind} '{entry.Name}'",
                entry.FilePath,
                entry.Span.Line,
                entry.Span.Column);
        }

        foreach (var group in result.DuplicateGroups)
        {
            var first = group[0];
            var locations = string.Join(", ", group.Skip(1).Select(e => $"{Path.GetFileName(e.FilePath)}:{e.Span.Line}"));
            yield return new Diagnostic(
                DiagnosticCode.Calor0803,
                DiagnosticSeverity.Error,
                $"Duplicate ID '{first.Id}' for {first.Kind} '{first.Name}'. Also used at: {locations}",
                first.FilePath,
                first.Span.Line,
                first.Span.Column);
        }
    }

    /// <summary>
    /// Checks if ID churn occurred (existing ID was modified).
    /// </summary>
    /// <param name="oldEntries">IDs before the change.</param>
    /// <param name="newEntries">IDs after the change.</param>
    /// <returns>Entries where the ID was changed (churn detected).</returns>
    public static IReadOnlyList<(IdEntry Old, IdEntry New)> DetectIdChurn(
        IReadOnlyList<IdEntry> oldEntries,
        IReadOnlyList<IdEntry> newEntries)
    {
        var churn = new List<(IdEntry Old, IdEntry New)>();

        // Match by file + name + kind
        foreach (var oldEntry in oldEntries.Where(e => !string.IsNullOrEmpty(e.Id)))
        {
            var newEntry = newEntries.FirstOrDefault(e =>
                e.FilePath == oldEntry.FilePath &&
                e.Name == oldEntry.Name &&
                e.Kind == oldEntry.Kind);

            if (newEntry != null &&
                !string.IsNullOrEmpty(newEntry.Id) &&
                !string.Equals(oldEntry.Id, newEntry.Id, StringComparison.OrdinalIgnoreCase))
            {
                churn.Add((oldEntry, newEntry));
            }
        }

        return churn;
    }
}
