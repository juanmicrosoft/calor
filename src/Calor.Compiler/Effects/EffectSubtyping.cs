using Calor.Compiler.Binding.BoundTypes;

namespace Calor.Compiler.Effects;

/// <summary>
/// Defines subtype relationships for effects.
/// A "readwrite" effect encompasses both "read" and "write" effects.
/// This allows declaring a broad effect that covers narrower ones.
///
/// <para>v0.15 E2 slice b — this table is no longer written here. It is
/// DERIVED from <see cref="EffectRow.FamilySubtypes"/>, the compact-code
/// family/narrow table that lives in <c>Binding/BoundTypes/</c> because
/// <c>Binding/</c> may not reference <c>Effects/</c> (design-doc §4.1 and
/// <c>ArchitectureTests.BindingLayer_HasNoReferenceToEffectsNamespace</c>).
/// Two hand-written tables would be two things to keep in step; a derivation
/// cannot drift. The 0.15 widening — a bare <c>db</c> / <c>net</c> / <c>env</c>
/// now encompassing its narrow siblings — arrives here through that table.</para>
/// </summary>
public static class EffectSubtyping
{
    /// <summary>
    /// Maps broad effects to their constituent narrower effects, projected from
    /// <see cref="EffectRow.FamilySubtypes"/> into the internal
    /// <c>(kind, value)</c> vocabulary. Enumeration order is the source table's,
    /// which is what keeps <see cref="GetBroadestEncompassing"/> byte-identical
    /// to 0.14 (the <c>:rw</c> rows come first).
    /// </summary>
    private static readonly Dictionary<(EffectKind Kind, string Value), List<(EffectKind Kind, string Value)>> Subtypes =
        EffectRow.FamilySubtypes.ToDictionary(
            entry => ToInternal(entry.Key),
            entry => entry.Value.Select(ToInternal).ToList());

    private static (EffectKind Kind, string Value) ToInternal(string compactCode)
    {
        var parsed = EffectCodes.ParseCompact(compactCode);
        return (parsed.Kind, parsed.Value);
    }

    /// <summary>
    /// Returns true if the declared effect encompasses the required effect.
    /// This includes both exact matches and subtype relationships.
    /// </summary>
    /// <param name="declared">The effect that was declared on a function</param>
    /// <param name="required">The effect that is required by some operation</param>
    /// <returns>True if the declaration satisfies the requirement</returns>
    public static bool Encompasses((EffectKind Kind, string Value) declared, (EffectKind Kind, string Value) required)
    {
        // Exact match
        if (declared == required)
            return true;

        // Check if declared effect has subtypes that include the required effect
        if (Subtypes.TryGetValue(declared, out var subtypes))
        {
            if (subtypes.Contains(required))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns all effects that are encompassed by the given effect.
    /// Includes the effect itself plus any subtypes.
    /// </summary>
    public static IEnumerable<(EffectKind Kind, string Value)> GetEncompassedEffects((EffectKind Kind, string Value) effect)
    {
        yield return effect;

        if (Subtypes.TryGetValue(effect, out var subtypes))
        {
            foreach (var subtype in subtypes)
            {
                yield return subtype;
            }
        }
    }

    /// <summary>
    /// Returns the broadest effect that encompasses the given effect.
    /// If no broader effect exists, returns the effect itself.
    /// </summary>
    public static (EffectKind Kind, string Value) GetBroadestEncompassing((EffectKind Kind, string Value) effect)
    {
        foreach (var (broad, subtypes) in Subtypes)
        {
            if (subtypes.Contains(effect))
            {
                return broad;
            }
        }
        return effect;
    }

    /// <summary>
    /// Checks if an effect is a granular (read or write specific) effect.
    /// </summary>
    public static bool IsGranularEffect(string value)
    {
        return value.EndsWith("_read") || value.EndsWith("_write");
    }

    /// <summary>
    /// Checks if an effect is a combined readwrite effect.
    /// </summary>
    public static bool IsReadWriteEffect(string value)
    {
        return value.EndsWith("_readwrite");
    }
}
