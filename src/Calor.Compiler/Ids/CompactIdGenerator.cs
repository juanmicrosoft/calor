using System.Security.Cryptography;

namespace Calor.Compiler.Ids;

/// <summary>
/// Generates v6 compact IDs: 12 characters of Crockford-lowercase Base32
/// payload, prefixed by the declaration-kind tag (e.g. <c>f_abc123def456</c>).
///
/// <para>
/// The alphabet (<see cref="Alphabet"/>) is the Crockford Base32 alphabet
/// rendered in lowercase, deliberately excluding the visually-ambiguous
/// characters <c>i</c>, <c>l</c>, <c>o</c>, <c>u</c>. With 32 distinct
/// payload characters and a 12-character payload, the address space is
/// 32^12 ≈ 1.15×10^18 IDs, comfortably collision-safe for the
/// 10^7-IDs-per-repo design target documented in
/// <c>docs/plans/path-2-drop-ids-v5.md</c> §6.1.
/// </para>
///
/// <para>
/// Randomness comes from <see cref="RandomNumberGenerator"/>. Each
/// payload byte is mapped to a character by reducing the byte modulo 32;
/// because 256 is exactly divisible by 32 this introduces no modulo bias.
/// </para>
/// </summary>
public static class CompactIdGenerator
{
    /// <summary>
    /// The compact ID payload length in characters.
    /// </summary>
    public const int PayloadLength = 12;

    /// <summary>
    /// Crockford Base32 lowercase, excluding <c>i</c>, <c>l</c>,
    /// <c>o</c>, <c>u</c>. 32 characters total.
    /// </summary>
    public const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>
    /// Generates a new compact ID for the specified declaration kind.
    /// </summary>
    public static string Generate(IdKind kind)
    {
        return IdGenerator.GetPrefix(kind) + GeneratePayload();
    }

    /// <summary>
    /// Generates a new compact ID with the specified prefix.
    /// </summary>
    public static string GenerateWithPrefix(string prefix)
    {
        return prefix + GeneratePayload();
    }

    /// <summary>
    /// Generates a bare 12-character compact payload (no prefix).
    /// </summary>
    public static string GeneratePayload()
    {
        Span<byte> bytes = stackalloc byte[PayloadLength];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[PayloadLength];
        for (int i = 0; i < PayloadLength; i++)
        {
            chars[i] = Alphabet[bytes[i] & 0x1F]; // mask to 0..31, unbiased
        }

        return new string(chars);
    }

    /// <summary>
    /// Deterministically derive a compact payload from a legacy 26-char
    /// Crockford-uppercase ULID payload. Used by the
    /// <c>calor fix --compact-ids</c> migrator so that semantic
    /// identity is preserved when rewriting existing ULIDs.
    /// </summary>
    /// <param name="ulidPayload">The 26-char ULID payload (no prefix).</param>
    /// <returns>
    /// A 12-character lowercase compact payload formed from the last
    /// 12 characters of the ULID, lowercased. Returns null if the input
    /// is not a 26-char Crockford-Base32 payload.
    /// </returns>
    public static string? DeriveFromUlid(string? ulidPayload)
    {
        if (string.IsNullOrEmpty(ulidPayload) || ulidPayload.Length != IdValidator.UlidLength)
        {
            return null;
        }

        // Take the last 12 chars (the random tail of a ULID — first 10
        // are timestamp). 60 bits of randomness preserved.
        var tail = ulidPayload[^PayloadLength..].ToLowerInvariant();

        // Verify every char is in our compact alphabet.
        foreach (var c in tail)
        {
            if (Alphabet.IndexOf(c) < 0)
            {
                return null;
            }
        }
        return tail;
    }

    /// <summary>
    /// Checks whether a payload is a well-formed compact payload (length
    /// 12, every char in <see cref="Alphabet"/>).
    /// </summary>
    public static bool IsValidPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload) || payload.Length != PayloadLength)
        {
            return false;
        }
        foreach (var c in payload)
        {
            if (Alphabet.IndexOf(c) < 0)
            {
                return false;
            }
        }
        return true;
    }
}
