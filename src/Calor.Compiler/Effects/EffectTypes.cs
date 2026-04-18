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
/// Shared utility for converting internal effect category/value pairs to compact surface codes.
/// Used by both CalorEmitter and CalorFormatter to ensure consistent serialization.
/// </summary>
public static class EffectCodes
{
    /// <summary>
    /// Convert internal effect category/value to compact code.
    /// E.g., ("io", "console_write") → "cw", ("io", "filesystem_read") → "fs:r"
    /// </summary>
    public static string ToCompact(string category, string value)
    {
        return (category.ToLowerInvariant(), value.ToLowerInvariant()) switch
        {
            // Console I/O
            ("io", "console_write") => "cw",
            ("io", "console_read") => "cr",
            // File I/O
            ("io", "filesystem_write") => "fs:w",
            ("io", "filesystem_read") => "fs:r",
            ("io", "filesystem_readwrite") => "fs:rw",
            // File I/O (legacy formatter values)
            ("io", "file_write") => "fw",
            ("io", "file_read") => "fr",
            ("io", "file_delete") => "fd",
            // Network
            ("io", "network") => "net",
            ("io", "network_read") => "net:r",
            ("io", "network_write") => "net:w",
            ("io", "network_readwrite") => "net:rw",
            ("io", "http") => "http",
            // Database
            ("io", "database") => "db",
            ("io", "database_read") => "db:r",
            ("io", "database_write") => "db:w",
            ("io", "database_readwrite") => "db:rw",
            // Legacy database codes (keep for backward compat)
            ("io", "dbr") => "db:r",
            ("io", "dbw") => "db:w",
            // Environment / process
            ("io", "environment") => "env",
            ("io", "environment_read") => "env:r",
            ("io", "environment_write") => "env:w",
            ("io", "environment_readwrite") => "env:rw",
            ("io", "process") => "proc",
            // Mutation
            ("mutation", "heap_write") => "mut",
            ("mutation", "collection") => "mut:col",
            // Memory
            ("memory", "allocation") => "alloc",
            // Nondeterminism
            ("nondeterminism", "time") => "time",
            ("nondeterminism", "random") => "rand",
            // Exception
            ("exception", "intentional") => "throw",
            _ => value // Pass through unknown values
        };
    }
}
