using System.Collections.Concurrent;

namespace Calor.Compiler.Ids;

/// <summary>
/// Per-compile-unit registry of issued IDs. Tracks both legacy ULID
/// payloads and Phase 2 compact payloads so we can detect collisions
/// at generation time and at parse time.
///
/// Thread-safe — backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class IdRegistry
{
    private readonly ConcurrentDictionary<string, IdMetadata> _ids =
        new(StringComparer.Ordinal);

    /// <summary>
    /// What we know about an ID we have seen.
    /// </summary>
    public sealed record IdMetadata(string Id, IdKind? Kind, string? SourcePath);

    /// <summary>
    /// Try to register an ID. Returns <c>true</c> on first registration,
    /// <c>false</c> if the ID was already present (collision).
    /// </summary>
    public bool TryRegister(string id, IdKind? kind = null, string? sourcePath = null)
    {
        return _ids.TryAdd(id, new IdMetadata(id, kind, sourcePath));
    }

    /// <summary>
    /// Generate a fresh compact ID for the given kind and register it.
    /// Retries (up to <paramref name="maxAttempts"/>) on the
    /// astronomically unlikely event of a collision. Returns the
    /// newly-registered ID.
    /// </summary>
    public string GenerateAndRegister(IdKind kind, int maxAttempts = 8)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var candidate = CompactIdGenerator.Generate(kind);
            if (TryRegister(candidate, kind))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException(
            $"unable to generate a unique compact ID after {maxAttempts} attempts");
    }

    /// <summary>
    /// Same as <see cref="GenerateAndRegister(IdKind,int)"/> but takes
    /// a raw prefix string. Used by the parser's auto-id path where we
    /// only have the prefix character ("m", "f", "l", …).
    /// </summary>
    public string GenerateAndRegister(string prefix, int maxAttempts = 8)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var candidate = CompactIdGenerator.GenerateWithPrefix(prefix);
            if (TryRegister(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException(
            $"unable to generate a unique compact ID after {maxAttempts} attempts");
    }

    /// <summary>
    /// True when <paramref name="id"/> has been registered.
    /// </summary>
    public bool Contains(string id) => _ids.ContainsKey(id);

    /// <summary>
    /// All registered IDs (ordering is undefined).
    /// </summary>
    public IEnumerable<IdMetadata> All => _ids.Values;

    /// <summary>
    /// Number of registered IDs.
    /// </summary>
    public int Count => _ids.Count;

    /// <summary>
    /// Clear the registry — used by tests and by the migrator when
    /// switching compile units.
    /// </summary>
    public void Clear() => _ids.Clear();
}
