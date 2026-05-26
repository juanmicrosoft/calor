using System.Text;

namespace Calor.Compiler.Diagnostics;

/// <summary>
/// Formats human-readable addresses for use inside diagnostic messages,
/// per RFC v3+ §8.3 ("standalone diagnostic addressing").
///
/// Today, bug-pattern diagnostics name a variable but not the enclosing
/// function. RFC §8.3 prescribes the form:
///
/// <c>Calor0501: division by zero in Calculator.Divide (f_01J5X7…) at file:42</c>
///
/// where <c>Calculator.Divide</c> is the qualified human-readable name,
/// and <c>f_01J5X7…</c> is a stable identifier that survives renames.
///
/// Truncation rule:
/// <list type="bullet">
///   <item><description>ULID-shaped IDs (pre-Phase-2): show prefix + the
///   first 7 chars of the ULID payload + a horizontal ellipsis.</description></item>
///   <item><description>Compact IDs (post-Phase-2, 12 chars total): show
///   in full — no truncation.</description></item>
///   <item><description>Anything shorter than the ULID payload length is
///   shown verbatim.</description></item>
/// </list>
///
/// Both inputs are optional. The helper produces an empty string when
/// neither name nor id is available, which keeps existing diagnostic
/// messages unchanged at unaffected call sites.
/// </summary>
public static class DiagnosticAddress
{
    /// <summary>
    /// ULID payload (Crockford base32) length excluding any prefix.
    /// </summary>
    internal const int UlidPayloadLength = 26;

    /// <summary>
    /// Number of leading payload characters retained when a ULID is
    /// truncated for display.
    /// </summary>
    internal const int UlidDisplayPrefixLength = 7;

    /// <summary>
    /// Horizontal ellipsis (U+2026); RFC §8.3 specifies this character.
    /// </summary>
    internal const string Ellipsis = "\u2026";

    /// <summary>
    /// Formats an address suffix suitable for embedding in a diagnostic
    /// message. Returns an empty string when both inputs are null/empty.
    /// </summary>
    /// <param name="qualifiedName">Human-readable qualified name,
    /// e.g. <c>Calculator.Divide</c>. May be null or empty.</param>
    /// <param name="id">The construct's stable ID, e.g.
    /// <c>f_01J5X7K9M2NPQRSTABWXYZ12</c> (ULID) or
    /// <c>f_abc123def456</c> (compact). May be null or empty.</param>
    /// <returns>
    /// One of:
    /// <list type="bullet">
    ///   <item><description><c>""</c> — when both inputs are blank.</description></item>
    ///   <item><description><c>Name</c> — when only <paramref name="qualifiedName"/> is provided.</description></item>
    ///   <item><description><c>(id)</c> — when only <paramref name="id"/> is provided.</description></item>
    ///   <item><description><c>Name (id)</c> — when both are provided.</description></item>
    /// </list>
    /// </returns>
    public static string Format(string? qualifiedName, string? id)
    {
        var hasName = !string.IsNullOrWhiteSpace(qualifiedName);
        var hasId = !string.IsNullOrWhiteSpace(id);
        if (!hasName && !hasId) return string.Empty;

        var displayId = hasId ? TruncateForDisplay(id!) : null;

        if (hasName && hasId)
        {
            return $"{qualifiedName} ({displayId})";
        }
        if (hasName)
        {
            return qualifiedName!;
        }
        return $"({displayId})";
    }

    /// <summary>
    /// Truncates an ID for display per the §8.3 rule.
    /// </summary>
    /// <remarks>
    /// Behaviour:
    /// <list type="bullet">
    ///   <item><description>If the payload (post-prefix) is exactly
    ///   <see cref="UlidPayloadLength"/> chars, the ID is treated as a
    ///   ULID and the payload is truncated to
    ///   <see cref="UlidDisplayPrefixLength"/> chars + ellipsis.</description></item>
    ///   <item><description>Otherwise the ID is shown verbatim. Compact
    ///   IDs (12-char payload) and any future format pass through
    ///   unchanged.</description></item>
    /// </list>
    /// </remarks>
    public static string TruncateForDisplay(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;

        // Split off the prefix at the first underscore (if any). All
        // current ID kinds use the `kind_payload` form, e.g.
        // `f_01J5...`, `m_01J5...`, `ctor_01J5...`.
        var underscoreIx = id.IndexOf('_');
        string prefix;
        string payload;
        if (underscoreIx >= 0 && underscoreIx < id.Length - 1)
        {
            prefix = id.Substring(0, underscoreIx + 1);
            payload = id.Substring(underscoreIx + 1);
        }
        else
        {
            prefix = string.Empty;
            payload = id;
        }

        if (payload.Length != UlidPayloadLength)
        {
            // Not a ULID payload — return as-is. Compact IDs (12-char
            // payload) hit this branch by design.
            return id;
        }

        var sb = new StringBuilder(prefix.Length + UlidDisplayPrefixLength + 1);
        sb.Append(prefix);
        sb.Append(payload, 0, UlidDisplayPrefixLength);
        sb.Append(Ellipsis);
        return sb.ToString();
    }
}
