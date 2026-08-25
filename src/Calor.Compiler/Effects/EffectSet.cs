namespace Calor.Compiler.Effects;

/// <summary>
/// Represents a set of effects that a function may perform.
/// Supports set operations for effect analysis.
/// </summary>
public sealed class EffectSet : IEquatable<EffectSet>
{
    private readonly HashSet<(EffectKind Kind, string Value)> _effects;

    /// <summary>
    /// An empty effect set (pure function).
    /// </summary>
    public static readonly EffectSet Empty = new(Array.Empty<(EffectKind, string)>());

    /// <summary>
    /// An unknown effect set representing worst-case (all possible effects).
    /// Used for unresolved external calls.
    /// </summary>
    public static readonly EffectSet Unknown = CreateUnknown();

    private EffectSet(IEnumerable<(EffectKind Kind, string Value)> effects)
    {
        _effects = new HashSet<(EffectKind, string)>(effects);
    }

    /// <summary>
    /// Creates an EffectSet from surface codes (e.g., "cw", "fr", "rand").
    /// </summary>
    public static EffectSet From(params string[] surfaceCodes)
    {
        var effects = new List<(EffectKind, string)>();
        foreach (var code in surfaceCodes)
        {
            var (kind, value) = ParseSurfaceCode(code);
            effects.Add((kind, value));
        }
        return new EffectSet(effects);
    }

    /// <summary>
    /// Creates an EffectSet from internal effect tuples.
    /// </summary>
    public static EffectSet FromInternal(IEnumerable<(EffectKind Kind, string Value)> effects)
    {
        return new EffectSet(effects);
    }

    /// <summary>
    /// Creates an EffectSet from a single EffectInfo.
    /// </summary>
    public static EffectSet FromInfo(EffectInfo info)
    {
        return new EffectSet(new[] { (info.Kind, info.Value) });
    }

    /// <summary>
    /// Creates an EffectSet from multiple EffectInfo objects.
    /// </summary>
    public static EffectSet FromInfos(IEnumerable<EffectInfo> infos)
    {
        return new EffectSet(infos.Select(i => (i.Kind, i.Value)));
    }

    /// <summary>
    /// Returns true if this set contains no effects.
    /// </summary>
    public bool IsEmpty => _effects.Count == 0;

    /// <summary>
    /// Returns true if this set represents unknown/worst-case effects.
    /// </summary>
    public bool IsUnknown => _effects.Contains((EffectKind.Unknown, "*"));

    /// <summary>
    /// Returns the number of effects in this set.
    /// </summary>
    public int Count => _effects.Count;

    /// <summary>
    /// Returns the union of this set with another.
    /// </summary>
    public EffectSet Union(EffectSet other)
    {
        if (other == null) return this;
        if (IsUnknown || other.IsUnknown) return Unknown;

        var combined = new HashSet<(EffectKind, string)>(_effects);
        combined.UnionWith(other._effects);
        return new EffectSet(combined);
    }

    /// <summary>
    /// Returns true if this set is a subset of the other set.
    /// Takes into account effect subtyping (e.g., fs:rw encompasses fs:r and fs:w).
    /// </summary>
    public bool IsSubsetOf(EffectSet other)
    {
        if (other == null) return IsEmpty;
        if (other.IsUnknown) return true;  // Everything is subset of unknown
        if (IsUnknown) return false;       // Unknown is not subset of anything else

        // Check each effect in this set
        foreach (var effect in _effects)
        {
            // Direct membership check
            if (other._effects.Contains(effect))
                continue;

            // Check if any declared effect encompasses this required effect
            var encompassed = other._effects.Any(declared =>
                EffectSubtyping.Encompasses(declared, effect));

            if (!encompassed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the effects in this set that are not in the other set.
    /// Takes into account effect subtyping (e.g., fs:rw encompasses fs:r and fs:w).
    /// </summary>
    public IEnumerable<(EffectKind Kind, string Value)> Except(EffectSet other)
    {
        if (other == null) return _effects;
        if (other.IsUnknown) return Array.Empty<(EffectKind, string)>();
        if (IsUnknown) return new[] { (EffectKind.Unknown, "*") };

        // Return effects that are not covered by any effect in the other set
        return _effects.Where(effect =>
        {
            // Check direct membership
            if (other._effects.Contains(effect))
                return false;

            // Check if any declared effect encompasses this effect
            if (other._effects.Any(declared => EffectSubtyping.Encompasses(declared, effect)))
                return false;

            return true;
        });
    }

    /// <summary>
    /// Returns true if this set contains the specified effect.
    /// </summary>
    public bool Contains(EffectKind kind, string value)
    {
        if (IsUnknown) return true;
        return _effects.Contains((kind, value));
    }

    /// <summary>
    /// Returns true if this set contains any effect of the specified kind.
    /// </summary>
    public bool ContainsKind(EffectKind kind)
    {
        if (IsUnknown) return true;
        return _effects.Any(e => e.Kind == kind);
    }

    /// <summary>
    /// Enumerates all effects in this set.
    /// </summary>
    public IEnumerable<(EffectKind Kind, string Value)> Effects => _effects;

    /// <summary>
    /// Returns a sorted, stable string representation for diagnostics.
    /// </summary>
    public string ToDisplayString()
    {
        if (IsUnknown) return "[unknown]";
        if (IsEmpty) return "[pure]";

        var sorted = _effects
            .OrderBy(e => e.Kind.ToString())
            .ThenBy(e => e.Value)
            .Select(e => ToSurfaceCode(e.Kind, e.Value));

        return string.Join(", ", sorted);
    }

    /// <summary>
    /// Converts internal representation to surface code for display.
    /// </summary>
    private static string ToSurfaceCode(EffectKind kind, string value)
        => EffectCodes.ToCompact(kind, value);

    /// <summary>
    /// Parses a surface code to internal representation.
    /// </summary>
    private static (EffectKind Kind, string Value) ParseSurfaceCode(string code)
    {
        var parsed = EffectCodes.ParseCompact(code);
        return (parsed.Kind, parsed.Value);
    }

    private static EffectSet CreateUnknown()
    {
        return new EffectSet(new[] { (EffectKind.Unknown, "*") });
    }

    public bool Equals(EffectSet? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _effects.SetEquals(other._effects);
    }

    public override bool Equals(object? obj) => obj is EffectSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var effect in _effects.OrderBy(e => e.Kind).ThenBy(e => e.Value))
        {
            hash = HashCode.Combine(hash, effect.Kind, effect.Value);
        }
        return hash;
    }

    public override string ToString() => ToDisplayString();

    public static bool operator ==(EffectSet? left, EffectSet? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(EffectSet? left, EffectSet? right) => !(left == right);
}
