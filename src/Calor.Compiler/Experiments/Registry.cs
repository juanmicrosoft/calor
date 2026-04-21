using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Experiments;

/// <summary>
/// Terminal lifecycle statuses — entries with these statuses will never change state.
/// </summary>
public static class RegistryStatus
{
    public const string PreRegisteredStage1 = "pre-registered-stage-1";
    public const string PreRegisteredStage2 = "pre-registered-stage-2";
    public const string BehindFlag = "behind-flag";
    public const string Promoted = "promoted";
    public const string Held = "held";
    public const string Dropped = "dropped";

    public static readonly IReadOnlyCollection<string> Terminal = new[] { Promoted, Held, Dropped };

    public static bool IsTerminal(string? status) => status != null && Terminal.Contains(status);
}

/// <summary>
/// In-memory registry providing the five queries from §5.0h of the Calor-native
/// type-system research plan.
///
/// Load once (<see cref="Load"/> or <see cref="Empty"/>), then query repeatedly.
/// Target: all queries complete under 200ms on a 1000-entry registry.
/// </summary>
public sealed class Registry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly List<RegistryEntry> _entries;
    private readonly Dictionary<string, RegistryEntry> _byId;

    private Registry(List<RegistryEntry> entries)
    {
        _entries = entries;
        _byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// An empty registry — convenient default for projects that haven't started Phase 0 work.
    /// </summary>
    public static Registry Empty => new(new List<RegistryEntry>());

    /// <summary>
    /// Load registry from a JSON file. Missing file returns an empty registry; malformed file throws.
    /// </summary>
    public static Registry Load(string path)
    {
        if (!File.Exists(path))
            return Empty;

        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<RegistryDocument>(json, JsonOptions)
                  ?? new RegistryDocument();
        return new Registry(doc.Entries);
    }

    /// <summary>
    /// All entries, in file order.
    /// </summary>
    public IReadOnlyList<RegistryEntry> Entries => _entries;

    public int Count => _entries.Count;

    // ========================================================================
    // Query 1: current-state — walk supersedes chain back to origin.
    // ========================================================================

    /// <summary>
    /// Walks the supersedes chain starting from the latest entry with the given ID
    /// (or any descendant via supersedes linkage), returning all entries in the chain
    /// from latest to origin.
    /// </summary>
    public CurrentStateResult CurrentState(string hypothesisId)
    {
        // Find the latest entry whose chain reaches this id.
        // A hypothesis identified by <hypothesisId> may appear as the ID itself or as
        // an ancestor via supersedes links.
        var chain = BuildChain(hypothesisId);
        if (chain.Count == 0)
        {
            return new CurrentStateResult(hypothesisId, null, null, Array.Empty<RegistryEntry>());
        }

        // Latest entry in the chain is the head (first element since we walk backwards).
        var head = chain[0];
        return new CurrentStateResult(hypothesisId, head.Status, head.Id, chain);
    }

    /// <summary>
    /// Build the full supersedes chain anchored at any entry whose chain passes through
    /// <paramref name="hypothesisId"/>. Returns entries ordered latest → origin.
    /// Empty list if no entry matches.
    /// </summary>
    private List<RegistryEntry> BuildChain(string hypothesisId)
    {
        // Find the head: an entry whose chain (itself + supersedes links) contains the id.
        // Head = entry that is not superseded by any other entry AND whose chain contains id.
        var supersededIds = _entries
            .Where(e => e.Supersedes != null)
            .Select(e => e.Supersedes!)
            .ToHashSet(StringComparer.Ordinal);

        RegistryEntry? head = null;
        foreach (var entry in _entries)
        {
            if (supersededIds.Contains(entry.Id))
                continue; // Not a head — something supersedes it.

            // Walk this entry's chain to see if it contains hypothesisId.
            if (ChainContains(entry, hypothesisId))
            {
                head = entry;
                break;
            }
        }

        if (head == null)
            return new List<RegistryEntry>();

        var chain = new List<RegistryEntry>();
        var current = head;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current != null)
        {
            if (!visited.Add(current.Id))
                break; // Cycle guard.
            chain.Add(current);
            current = current.Supersedes != null && _byId.TryGetValue(current.Supersedes, out var prev)
                ? prev
                : null;
        }
        return chain;
    }

    private bool ChainContains(RegistryEntry head, string hypothesisId)
    {
        var current = head;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current != null)
        {
            if (!visited.Add(current.Id))
                return false;
            if (string.Equals(current.Id, hypothesisId, StringComparison.Ordinal))
                return true;
            current = current.Supersedes != null && _byId.TryGetValue(current.Supersedes, out var prev)
                ? prev
                : null;
        }
        return false;
    }

    // ========================================================================
    // Query 2: two-kill-risk — find dropped entries matching a tuple.
    // ========================================================================

    /// <summary>
    /// Return all dropped entries whose (tag, code class, effect direction) tuple
    /// matches the given tuple. Used by the two-kill-rule reviewer gate (§4.5) to
    /// detect metric-substitution evasion attempts mechanically.
    /// </summary>
    public IReadOnlyList<RegistryEntry> TwoKillRisk(string tag, string codeClass, string effectDirection)
    {
        return _entries
            .Where(e => string.Equals(e.Status, RegistryStatus.Dropped, StringComparison.Ordinal))
            .Where(e =>
                string.Equals(e.Tag, tag, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.TupleCodeClass, codeClass, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.TupleEffectDirection, effectDirection, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ========================================================================
    // Query 3: held-owned-by — held features with given owner.
    // ========================================================================

    /// <summary>
    /// List held features with <c>hold_owner == user</c>, sorted by quarterly-review-due date ascending.
    /// Entries with no due date sort last.
    /// </summary>
    public IReadOnlyList<RegistryEntry> HeldOwnedBy(string user)
    {
        return _entries
            .Where(e => string.Equals(e.Status, RegistryStatus.Held, StringComparison.Ordinal))
            .Where(e => string.Equals(e.HoldOwner, user, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => ParseDate(e.QuarterlyReviewDue) ?? DateTime.MaxValue)
            .ToList();
    }

    // ========================================================================
    // Query 4: stale-holds — held features with missing owner or overdue review.
    // ========================================================================

    /// <summary>
    /// List held features whose <c>hold_owner</c> is absent, OR whose <c>quarterly_review_due</c>
    /// is in the past. Feeds the §4.4 auto-drop rule.
    /// </summary>
    public IReadOnlyList<StaleHoldReason> StaleHolds(DateTime? asOf = null)
    {
        var cutoff = asOf ?? DateTime.UtcNow;
        var result = new List<StaleHoldReason>();

        foreach (var entry in _entries)
        {
            if (!string.Equals(entry.Status, RegistryStatus.Held, StringComparison.Ordinal))
                continue;

            if (string.IsNullOrWhiteSpace(entry.HoldOwner))
            {
                result.Add(new StaleHoldReason(entry, "no-owner"));
                continue;
            }

            var due = ParseDate(entry.QuarterlyReviewDue);
            if (due.HasValue && due.Value < cutoff)
            {
                result.Add(new StaleHoldReason(entry, "review-overdue"));
            }
        }

        return result;
    }

    // ========================================================================
    // Query 5: audit-trail — chronological record for a hypothesis.
    // ========================================================================

    /// <summary>
    /// Return all entries in the supersedes chain of the given hypothesis, ordered
    /// chronologically (earliest <c>merged_at</c> first). Entries without a <c>merged_at</c>
    /// sort first in file-order as a conservative default.
    /// </summary>
    public IReadOnlyList<RegistryEntry> AuditTrail(string hypothesisId)
    {
        var chain = BuildChain(hypothesisId);
        if (chain.Count == 0)
            return Array.Empty<RegistryEntry>();

        // Chronological order: oldest first.
        return chain
            .OrderBy(e => ParseDate(e.MergedAt) ?? DateTime.MinValue)
            .ThenBy(e => _entries.IndexOf(e))
            .ToList();
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static DateTime? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return null;
        return DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt
            : (DateTime?)null;
    }
}

/// <summary>
/// Result of a current-state query: the hypothesis's latest status, the chain head's ID,
/// and the full supersedes chain from latest to origin.
/// </summary>
public sealed record CurrentStateResult(
    string QueriedId,
    string? CurrentStatus,
    string? HeadEntryId,
    IReadOnlyList<RegistryEntry> Chain);

/// <summary>
/// A held entry flagged as stale, with the reason: <c>no-owner</c> or <c>review-overdue</c>.
/// </summary>
public sealed record StaleHoldReason(RegistryEntry Entry, string Reason);
