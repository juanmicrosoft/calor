namespace Calor.Compiler.Effects;

/// <summary>
/// Categories of effects.
/// </summary>
public enum EffectKind
{
    Unknown,
    IO,
    Mutation,
    Memory,
    Exception,
    Nondeterminism
}

/// <summary>
/// Represents a specific effect (kind + value).
/// </summary>
public sealed class EffectInfo : IEquatable<EffectInfo>
{
    public EffectKind Kind { get; }
    public string Value { get; }

    public EffectInfo(EffectKind kind, string value)
    {
        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool Equals(EffectInfo? other)
    {
        if (other is null) return false;
        return Kind == other.Kind && Value == other.Value;
    }

    public override bool Equals(object? obj)
        => obj is EffectInfo other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, Value);

    public override string ToString() => $"{Kind}:{Value}";
}

/// <summary>
/// One entry in the effect-code registry: an internal category/value pair and its
/// compact surface code. <paramref name="Legacy"/> entries are accepted for backward
/// compatibility but are not part of the documented surface (and are excluded from
/// the docs-drift check's "must be documented" set).
/// </summary>
public sealed record EffectCodeEntry(string Category, string Value, string Compact, bool Legacy = false);

/// <summary>
/// Shared registry for converting internal effect category/value pairs to compact surface codes.
/// Used by both CalorEmitter and CalorFormatter to ensure consistent serialization, and by
/// <c>calor self-check docs</c> as the single source of truth for documented effect codes.
/// </summary>
public static class EffectCodes
{
    /// <summary>
    /// The full effect-code registry. Single source of truth: docs
    /// (docs/syntax-reference/effects.md) are checked against this table by
    /// <c>calor self-check docs</c>. Add new effect codes here.
    /// </summary>
    public static readonly IReadOnlyList<EffectCodeEntry> Registry =
    [
        // Console I/O
        new("io", "console_write", "cw"),
        new("io", "console_read", "cr"),
        // File I/O
        new("io", "filesystem_write", "fs:w"),
        new("io", "filesystem_read", "fs:r"),
        new("io", "filesystem_readwrite", "fs:rw"),
        // File I/O (legacy formatter values)
        new("io", "file_write", "fw", Legacy: true),
        new("io", "file_read", "fr", Legacy: true),
        new("io", "file_delete", "fd", Legacy: true),
        // Network
        new("io", "network", "net"),
        new("io", "network_read", "net:r"),
        new("io", "network_write", "net:w"),
        new("io", "network_readwrite", "net:rw"),
        new("io", "http", "http"),
        // Database
        new("io", "database", "db"),
        new("io", "database_read", "db:r"),
        new("io", "database_write", "db:w"),
        new("io", "database_readwrite", "db:rw"),
        // Legacy database codes (keep for backward compat)
        new("io", "dbr", "db:r", Legacy: true),
        new("io", "dbw", "db:w", Legacy: true),
        // Environment / process
        new("io", "environment", "env"),
        new("io", "environment_read", "env:r"),
        new("io", "environment_write", "env:w"),
        new("io", "environment_readwrite", "env:rw"),
        new("io", "process", "proc"),
        // Mutation
        new("mutation", "heap_write", "mut"),
        new("mutation", "collection", "mut:col"),
        // Memory
        new("memory", "allocation", "alloc"),
        // Nondeterminism
        new("nondeterminism", "time", "time"),
        new("nondeterminism", "random", "rand"),
        // Exception
        new("exception", "intentional", "throw"),
    ];

    private static readonly Dictionary<(string Category, string Value), string> CompactByInternal =
        Registry.ToDictionary(e => (e.Category, e.Value), e => e.Compact);

    /// <summary>
    /// All compact surface codes the compiler knows, including legacy forms.
    /// </summary>
    public static readonly IReadOnlyCollection<string> KnownCompactCodes =
        Registry.Select(e => e.Compact).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The compact surface codes that must appear in the effect-code documentation
    /// (all non-legacy codes).
    /// </summary>
    public static readonly IReadOnlyCollection<string> DocumentedCompactCodes =
        Registry.Where(e => !e.Legacy).Select(e => e.Compact).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Convert internal effect category/value to compact code.
    /// E.g., ("io", "console_write") → "cw", ("io", "filesystem_read") → "fs:r"
    /// </summary>
    public static string ToCompact(string category, string value)
    {
        return CompactByInternal.TryGetValue(
            (category.ToLowerInvariant(), value.ToLowerInvariant()), out var compact)
            ? compact
            : value; // Pass through unknown values
    }
}
