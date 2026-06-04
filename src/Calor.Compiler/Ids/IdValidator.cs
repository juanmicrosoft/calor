using System.Text.RegularExpressions;

namespace Calor.Compiler.Ids;

/// <summary>
/// Validates Calor IDs for format compliance.
///
/// <para>
/// As of v0.6, canonical Calor IDs use the 12-char Crockford-lowercase
/// compact form (<see cref="CompactIdGenerator"/>). The 26-char
/// Crockford-uppercase ULID form remains <i>valid-but-legacy</i> and is
/// accepted by <see cref="Validate(string, IdKind, bool)"/> for
/// back-compat with existing repositories. Lint
/// <c>Calor0821 LegacyUlidPayload</c> is reserved for emitting a
/// migration hint when a ULID is encountered.
/// </para>
/// </summary>
public static partial class IdValidator
{
    /// <summary>
    /// The length of a ULID payload string (26 characters).
    /// </summary>
    public const int UlidLength = 26;

    /// <summary>
    /// The length of a compact ID payload (12 characters).
    /// </summary>
    public const int CompactLength = CompactIdGenerator.PayloadLength;

    /// <summary>
    /// Pattern for valid ULID characters (Crockford Base32).
    /// Excludes I, L, O, U to avoid ambiguity.
    /// </summary>
    private static readonly Regex UlidPattern = UlidPatternRegex();

    /// <summary>
    /// Pattern for valid compact-payload characters (Crockford Base32
    /// lowercase). Excludes i, l, o, u to avoid ambiguity. The leading
    /// alphabet character must be a digit OR letter — case is fixed to
    /// lowercase to distinguish from ULIDs.
    /// </summary>
    private static readonly Regex CompactPattern = CompactPatternRegex();

    /// <summary>
    /// Pattern for test IDs (e.g., f001, m001, c001).
    /// </summary>
    private static readonly Regex TestIdPattern = TestIdPatternRegex();

    /// <summary>
    /// Validates an ID for format compliance.
    /// </summary>
    /// <param name="id">The ID to validate.</param>
    /// <param name="expectedKind">The expected declaration kind.</param>
    /// <param name="isTestPath">True if the file is in tests/, docs/, or examples/.</param>
    /// <returns>The validation result.</returns>
    public static IdValidationResult Validate(string? id, IdKind expectedKind, bool isTestPath)
    {
        // Check for missing ID
        if (string.IsNullOrEmpty(id))
            return IdValidationResult.Missing;

        // Check if it's a test ID
        if (IsTestId(id))
        {
            // Test IDs are only allowed in test paths
            if (!isTestPath)
                return IdValidationResult.TestIdInProduction;

            // For test IDs, just verify the prefix matches
            var testPrefix = GetTestIdPrefix(id);
            var expectedPrefix = GetExpectedTestPrefix(expectedKind);
            if (testPrefix != expectedPrefix && !string.IsNullOrEmpty(expectedPrefix))
                return IdValidationResult.WrongPrefix;

            return IdValidationResult.Valid;
        }

        // Check prefix matches kind
        var kind = IdGenerator.GetKindFromId(id);
        if (kind == null)
            return IdValidationResult.InvalidFormat;

        if (kind != expectedKind)
            return IdValidationResult.WrongPrefix;

        // Extract and validate payload — accept v6 compact OR legacy ULID.
        var payload = IdGenerator.ExtractPayload(id);
        if (payload == null)
            return IdValidationResult.InvalidFormat;

        if (!IsValidCompactPayload(payload) && !IsValidUlid(payload))
            return IdValidationResult.InvalidFormat;

        return IdValidationResult.Valid;
    }

    /// <summary>
    /// Checks if a string is a valid legacy ULID format (26 chars,
    /// Crockford uppercase).
    /// </summary>
    public static bool IsValidUlid(string ulid)
    {
        if (string.IsNullOrEmpty(ulid))
            return false;

        if (ulid.Length != UlidLength)
            return false;

        return UlidPattern.IsMatch(ulid);
    }

    /// <summary>
    /// Checks if a string is a valid v6 compact payload (12 chars,
    /// Crockford lowercase, no i/l/o/u).
    /// </summary>
    public static bool IsValidCompactPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            return false;

        if (payload.Length != CompactLength)
            return false;

        return CompactPattern.IsMatch(payload);
    }

    /// <summary>
    /// Checks if an ID is a test ID (e.g., f001, m001).
    /// </summary>
    /// <param name="id">The ID to check.</param>
    /// <returns>True if it's a test ID.</returns>
    public static bool IsTestId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        return TestIdPattern.IsMatch(id);
    }

    /// <summary>
    /// Checks if a file path is in a test location (tests/, docs/, examples/).
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if the path is in a test location.</returns>
    public static bool IsTestPath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();

        return normalizedPath.Contains("/tests/") ||
               normalizedPath.Contains("/docs/") ||
               normalizedPath.Contains("/examples/") ||
               normalizedPath.StartsWith("tests/") ||
               normalizedPath.StartsWith("docs/") ||
               normalizedPath.StartsWith("examples/");
    }

    /// <summary>
    /// Gets the letter prefix from a test ID (e.g., "f" from "f001").
    /// </summary>
    private static string? GetTestIdPrefix(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var match = TestIdPattern.Match(id);
        if (!match.Success)
            return null;

        return match.Groups[1].Value.ToLowerInvariant();
    }

    /// <summary>
    /// Gets the expected test ID prefix for a declaration kind.
    /// </summary>
    private static string GetExpectedTestPrefix(IdKind kind) => kind switch
    {
        IdKind.Module => "m",
        IdKind.Function => "f",
        IdKind.Class => "c",
        IdKind.Interface => "i",
        IdKind.Property => "p",
        IdKind.Method => "mt",
        IdKind.Constructor => "ctor",
        IdKind.Enum => "e",
        IdKind.OperatorOverload => "op",
        _ => ""
    };

    /// <summary>
    /// Checks if an ID is a canonical production-shape ID. Accepts
    /// <b>both</b> v6 compact (12-char Crockford-lowercase) and legacy
    /// ULID (26-char Crockford-uppercase) payloads. Use
    /// <see cref="IsCompactId(string)"/> or
    /// <see cref="IsLegacyUlidId(string)"/> if you need to discriminate
    /// between the two forms.
    /// </summary>
    public static bool IsCanonicalId(string? id)
    {
        return IsCompactId(id) || IsLegacyUlidId(id);
    }

    /// <summary>
    /// Checks if an ID is the v6 canonical compact form: kind prefix +
    /// 12-char Crockford-lowercase payload.
    /// </summary>
    public static bool IsCompactId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        var kind = IdGenerator.GetKindFromId(id);
        if (kind == null)
            return false;

        var payload = IdGenerator.ExtractPayload(id);
        return payload != null && IsValidCompactPayload(payload);
    }

    /// <summary>
    /// Checks if an ID is a legacy ULID-bearing ID: kind prefix +
    /// 26-char Crockford-uppercase payload. The compiler still accepts
    /// these, but they should be migrated via
    /// <c>calor fix --compact-ids</c>.
    /// </summary>
    public static bool IsLegacyUlidId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        var kind = IdGenerator.GetKindFromId(id);
        if (kind == null)
            return false;

        var payload = IdGenerator.ExtractPayload(id);
        return payload != null && IsValidUlid(payload);
    }

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]+$", RegexOptions.IgnoreCase)]
    private static partial Regex UlidPatternRegex();

    [GeneratedRegex("^[0-9a-hjkmnp-tv-z]+$")]
    private static partial Regex CompactPatternRegex();

    [GeneratedRegex("^(m|f|c|i|p|mt|ctor|op|e)(\\d{3,})$", RegexOptions.IgnoreCase)]
    private static partial Regex TestIdPatternRegex();
}
