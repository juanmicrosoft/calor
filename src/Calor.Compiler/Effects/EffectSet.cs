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

/// <summary>
/// The verdict of the three-valued <see cref="EffectRow.Fits"/> relation
/// (effect-rows design doc §4.3). Deliberately NOT <c>bool</c>: a row whose
/// source or destination is Unknown is neither a fit nor a mismatch.
/// </summary>
public enum RowFit
{
    /// <summary>The source row is admitted by the destination row.</summary>
    Fits,

    /// <summary>The source row exceeds the destination row — Calor0424, never waived.</summary>
    DoesNotFit,

    /// <summary>One side is Unknown, so the site cannot be adjudicated — Calor0425.</summary>
    CannotTell,
}

/// <summary>
/// EMITTER SPIKE (roadmap §4.1 term 1; design doc §4).
///
/// A row is <c>Concrete(S) | Assumed(S, R) | Unknown</c> over an
/// <see cref="EffectSet"/> <c>S</c> and a canonically ordered set of assumption
/// reasons <c>R</c>. The spike carries the full three-point lattice because
/// §4.3's nine-cell <c>fits</c> table is what distinguishes rows from today's
/// <see cref="EffectSet.IsSubsetOf"/> — in particular <c>fits</c> does NOT
/// inherit <c>IsSubsetOf</c>'s two Unknown special cases.
///
/// A row may also carry <see cref="Variables"/> — rank-1 effect variables bound
/// by an <c>eff</c> modifier in the enclosing declaration's type-parameter list
/// (§7.2). Variables are normalised to their binder index so the
/// interface/implementation comparison is alpha-equivalent (§7.3).
///
/// Lives in EffectSet.cs rather than a new Effects/EffectRow.cs (§9's plan) so
/// the spike adds ZERO new files under src/ — see the Calor-first allowlist guard.
/// </summary>
public sealed class EffectRow : IEquatable<EffectRow>
{
    /// <summary>Which of the lattice's three shapes this row is.</summary>
    public enum RowKind
    {
        /// <summary>A promise: exactly these effects.</summary>
        Concrete,

        /// <summary>These effects, believed on the strength of the listed reasons.</summary>
        Assumed,

        /// <summary>No information. Top of the lattice; absorbing under join.</summary>
        Unknown,
    }

    private static readonly IReadOnlyList<string> NoReasons = Array.Empty<string>();
    private static readonly IReadOnlyList<int> NoVariables = Array.Empty<int>();

    /// <summary>The row with no information — top of the join lattice (§4.2).</summary>
    public static readonly EffectRow UnknownRow =
        new(RowKind.Unknown, EffectSet.Unknown, NoReasons, NoVariables);

    /// <summary>The pure row — identity of the join (§4.2).</summary>
    public static readonly EffectRow Pure =
        new(RowKind.Concrete, EffectSet.Empty, NoReasons, NoVariables);

    private EffectRow(
        RowKind kind,
        EffectSet effects,
        IReadOnlyList<string> reasons,
        IReadOnlyList<int> variables)
    {
        Kind = kind;
        Effects = effects;
        Reasons = reasons;
        Variables = variables;
    }

    /// <summary>Which lattice shape.</summary>
    public RowKind Kind { get; }

    /// <summary>The concrete part of the row. Unknown when <see cref="Kind"/> is Unknown.</summary>
    public EffectSet Effects { get; }

    /// <summary>
    /// Assumption reasons, canonically ordered (ordinal sort) so the join is
    /// commutative and diagnostics are traversal-order independent (§4.2).
    /// </summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>
    /// Rank-1 effect variables mentioned by this row, as BINDER INDICES (the
    /// position of the name in the declaring member's <c>eff</c> list), sorted
    /// ascending and de-duplicated. Indices rather than names is what makes the
    /// interface/implementation comparison alpha-equivalent (§7.3, W1c).
    /// </summary>
    public IReadOnlyList<int> Variables { get; }

    /// <summary>True when this row mentions at least one rank-1 effect variable.</summary>
    public bool IsPolymorphic => Variables.Count > 0;

    /// <summary>Builds a <c>Concrete(S)</c> row, optionally mentioning effect variables.</summary>
    public static EffectRow Concrete(EffectSet effects, IEnumerable<int>? variables = null)
        => new(RowKind.Concrete, effects ?? EffectSet.Empty, NoReasons, Canonicalise(variables));

    /// <summary>Builds an <c>Assumed(S, R)</c> row with canonically ordered reasons.</summary>
    public static EffectRow Assumed(
        EffectSet effects,
        IEnumerable<string> reasons,
        IEnumerable<int>? variables = null)
        => new(
            RowKind.Assumed,
            effects ?? EffectSet.Empty,
            Canonicalise(reasons),
            Canonicalise(variables));

    private static IReadOnlyList<string> Canonicalise(IEnumerable<string>? reasons)
        => reasons == null
            ? NoReasons
            : reasons
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToArray();

    private static IReadOnlyList<int> Canonicalise(IEnumerable<int>? variables)
        => variables == null
            ? NoVariables
            : variables.Distinct().OrderBy(v => v).ToArray();

    /// <summary>
    /// The join (§4.2). Associative, commutative, idempotent; identity
    /// <see cref="Pure"/>; top <see cref="UnknownRow"/>.
    /// </summary>
    public EffectRow Join(EffectRow other)
    {
        if (other == null) return this;
        if (Kind == RowKind.Unknown || other.Kind == RowKind.Unknown) return UnknownRow;

        var effects = Effects.Union(other.Effects);
        var variables = Variables.Concat(other.Variables);

        if (Kind == RowKind.Concrete && other.Kind == RowKind.Concrete)
            return Concrete(effects, variables);

        return Assumed(effects, Reasons.Concat(other.Reasons), variables);
    }

    /// <summary>
    /// §4.3's three-valued relation, total over all nine source×destination
    /// cells. NOT <see cref="EffectSet.IsSubsetOf"/>: that method's "everything
    /// is a subset of unknown" and "unknown is a subset of nothing" special
    /// cases are sound for a computed-vs-declared comparison and unsound here,
    /// where a destination can be Unknown by omission.
    ///
    /// Effect variables are compared as binder indices: a destination row
    /// mentioning variable #0 admits a source row mentioning variable #0
    /// (alpha-equivalence), and a source variable the destination does not
    /// mention is a mismatch.
    /// </summary>
    public static RowFit Fits(EffectRow source, EffectRow destination)
    {
        if (source == null || destination == null) return RowFit.CannotTell;
        if (source.Kind == RowKind.Unknown || destination.Kind == RowKind.Unknown)
            return RowFit.CannotTell;

        // Rank-1: every variable the source mentions must be mentioned by the
        // destination. Same binder index = same variable (§7.3).
        if (source.Variables.Any(v => !destination.Variables.Contains(v)))
            return RowFit.DoesNotFit;

        return SubsetIgnoringUnknownSpecialCases(source.Effects, destination.Effects)
            ? RowFit.Fits
            : RowFit.DoesNotFit;
    }

    /// <summary>
    /// <see cref="EffectSet.IsSubsetOf"/>'s loop body without its two Unknown
    /// special cases (§4.3). Neither side is Unknown when this is reached.
    /// </summary>
    private static bool SubsetIgnoringUnknownSpecialCases(EffectSet source, EffectSet destination)
    {
        if (source.IsUnknown || destination.IsUnknown) return false;
        return source.IsSubsetOf(destination);
    }

    /// <summary>
    /// The effects of <paramref name="source"/> that <paramref name="destination"/>
    /// does not admit — the "Extra effect(s)" clause of the Calor0424 message (§6.4).
    /// </summary>
    public static string ExtraEffects(EffectRow source, EffectRow destination)
    {
        var extra = source.Effects.Except(destination.Effects)
            .Select(e => EffectCodes.ToCompact(e.Kind, e.Value))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();
        return extra.Length == 0 ? "(none)" : string.Join(", ", extra);
    }

    /// <summary>
    /// Extends <see cref="EffectSet.ToDisplayString"/> with the row's shape
    /// (§8.3). Rows never appear in <c>BoundType.DisplayString</c>.
    /// </summary>
    public string ToDisplayString()
    {
        if (Kind == RowKind.Unknown) return "[unknown]";

        var parts = Variables
            .Select(v => "#" + v.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        if (!Effects.IsEmpty) parts.Add(Effects.ToDisplayString());

        var body = parts.Count == 0 ? "pure" : string.Join(", ", parts);
        return Kind == RowKind.Assumed ? $"[assumed: {body}]" : $"[{body}]";
    }

    /// <inheritdoc/>
    public bool Equals(EffectRow? other)
        => other != null
           && Kind == other.Kind
           && Effects.Equals(other.Effects)
           && Reasons.SequenceEqual(other.Reasons, StringComparer.Ordinal)
           && Variables.SequenceEqual(other.Variables);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as EffectRow);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = HashCode.Combine((int)Kind, Effects.GetHashCode());
        foreach (var reason in Reasons) hash = HashCode.Combine(hash, reason);
        foreach (var variable in Variables) hash = HashCode.Combine(hash, variable);
        return hash;
    }

    /// <inheritdoc/>
    public override string ToString() => ToDisplayString();
}
