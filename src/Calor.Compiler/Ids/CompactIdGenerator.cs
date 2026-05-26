using System.Security.Cryptography;

namespace Calor.Compiler.Ids;

/// <summary>
/// Generates compact 12-character Crockford-lowercase IDs for Calor
/// declarations. This is the Phase 2 replacement for
/// <see cref="IdGenerator"/>'s 26-character ULID payloads.
///
/// Per RFC §6:
/// <list type="bullet">
///   <item><description>Alphabet: <c>0123456789abcdefghjkmnpqrstvwxyz</c>
///   (Crockford base32, lowercased; <c>i</c>, <c>l</c>, <c>o</c>,
///   <c>u</c> excluded to prevent OCR/visual ambiguity).</description></item>
///   <item><description>Length: 12 chars after the prefix. 60 bits of
///   entropy gives a 50% collision probability around ~10^9
///   IDs.</description></item>
///   <item><description>Generation uses <see cref="RandomNumberGenerator"/>
///   for cryptographic-quality randomness; uniqueness within a
///   compile unit is enforced by <see cref="IdRegistry"/>.</description></item>
/// </list>
///
/// Both Phase 1 (ULID) and Phase 2 (compact) generators are present
/// during the migration window; the migrator <c>calor fix
/// --compact-ids</c> rewrites old payloads using
/// <see cref="GenerateWithPrefix"/>.
/// </summary>
public static class CompactIdGenerator
{
    /// <summary>
    /// Crockford base32 alphabet, lowercased, in the order used to map
    /// 5-bit nibbles to characters. Excludes <c>i</c>, <c>l</c>,
    /// <c>o</c>, <c>u</c>.
    /// </summary>
    public const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>
    /// Length of the payload portion (chars after the prefix).
    /// </summary>
    public const int PayloadLength = 12;

    /// <summary>
    /// Generate a new compact ID with the prefix appropriate for the
    /// given declaration <see cref="IdKind"/>.
    /// </summary>
    public static string Generate(IdKind kind)
    {
        return IdGenerator.GetPrefix(kind) + NewPayload();
    }

    /// <summary>
    /// Generate a new compact ID with an explicit prefix string.
    /// Intended for the parser's auto-id path and for the migrator
    /// which already has the prefix string in hand.
    /// </summary>
    public static string GenerateWithPrefix(string prefix)
    {
        return prefix + NewPayload();
    }

    /// <summary>
    /// Returns true if <paramref name="value"/> consists exactly of
    /// <see cref="PayloadLength"/> characters drawn from
    /// <see cref="Alphabet"/>. Use after stripping the prefix.
    /// </summary>
    public static bool IsValidPayload(string? value)
    {
        if (value is null || value.Length != PayloadLength) return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (Alphabet.IndexOf(value[i]) < 0) return false;
        }
        return true;
    }

    private static string NewPayload()
    {
        // 12 chars * 5 bits = 60 bits — pull 8 bytes (64 bits) and
        // discard the top 4. This avoids any modulo bias.
        Span<byte> raw = stackalloc byte[8];
        RandomNumberGenerator.Fill(raw);
        ulong bits = 0;
        for (int i = 0; i < 8; i++) bits = (bits << 8) | raw[i];
        bits >>= 4; // drop the top 4 bits

        Span<char> chars = stackalloc char[PayloadLength];
        for (int i = PayloadLength - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(bits & 0x1f)];
            bits >>= 5;
        }
        return new string(chars);
    }
}
