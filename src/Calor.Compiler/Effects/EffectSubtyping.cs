namespace Calor.Compiler.Effects;

/// <summary>
/// Defines subtype relationships for effects.
/// A "readwrite" effect encompasses both "read" and "write" effects.
/// This allows declaring a broad effect that covers narrower ones.
/// </summary>
public static class EffectSubtyping
{
    /// <summary>
    /// Maps broad effects to their constituent narrower effects.
    /// For example, filesystem_readwrite encompasses filesystem_read and filesystem_write.
    /// </summary>
    private static readonly Dictionary<(EffectKind Kind, string Value), List<(EffectKind Kind, string Value)>> Subtypes = new()
    {
        // Filesystem: rw encompasses r and w
        [(EffectKind.IO, "filesystem_readwrite")] = new()
        {
            (EffectKind.IO, "filesystem_read"),
            (EffectKind.IO, "filesystem_write")
        },

        // Network: rw encompasses r and w
        [(EffectKind.IO, "network_readwrite")] = new()
        {
            (EffectKind.IO, "network_read"),
            (EffectKind.IO, "network_write")
        },

        // Database: rw encompasses r and w
        [(EffectKind.IO, "database_readwrite")] = new()
        {
            (EffectKind.IO, "database_read"),
            (EffectKind.IO, "database_write")
        },

        // Environment: rw encompasses r and w
        [(EffectKind.IO, "environment_readwrite")] = new()
        {
            (EffectKind.IO, "environment_read"),
            (EffectKind.IO, "environment_write")
        }
    };

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
