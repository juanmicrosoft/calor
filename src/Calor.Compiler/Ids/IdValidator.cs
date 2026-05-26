using System.Text.RegularExpressions;

namespace Calor.Compiler.Ids;

/// <summary>
/// Validates Calor IDs for format compliance.
///
/// v6+ accepts two payload formats:
///   - Legacy ULID: 26 Crockford Base32 characters (uppercase, used by
///     the historical <see cref="IdGenerator.Generate"/> path).
///   - Compact:     12 Crockford lowercase characters (used by the
///     <see cref="CompactIdGenerator"/> introduced in PR-2a).
/// Validation accepts either form; emitters choose one.
/// </summary>
public static partial class IdValidator
{
    /// <summary>
    /// The length of a ULID string (26 characters).
    /// </summary>
    public const int UlidLength = 26;

    /// <summary>
    /// Pattern for valid ULID characters (Crockford Base32, uppercase).
    /// Excludes I, L, O, U to avoid ambiguity.
    /// </summary>
    private static readonly Regex UlidPattern = UlidPatternRegex();

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

        // Extract the payload (post-prefix). v6+ accepts either format:
        //   26-char ULID  (legacy IdGenerator path)
        //   12-char compact (CompactIdGenerator path, PR-2a)
        var payload = IdGenerator.ExtractIdPortion(id);
        if (payload == null)
            return IdValidationResult.InvalidFormat;

        if (!IsValidPayload(payload))
            return IdValidationResult.InvalidFormat;

        return IdValidationResult.Valid;
    }

    /// <summary>
    /// Checks if a string is a valid ULID format (26 Crockford Base32
    /// chars). Preserved for callers that explicitly need ULID
    /// semantics. Prefer <see cref="IsValidPayload"/> for format-agnostic
    /// validation in v6+ code.
    /// </summary>
    /// <param name="ulid">The string to check.</param>
    /// <returns>True if valid ULID format.</returns>
    public static bool IsValidUlid(string ulid)
    {
        if (string.IsNullOrEmpty(ulid))
            return false;

        if (ulid.Length != UlidLength)
            return false;

        return UlidPattern.IsMatch(ulid);
    }

    /// <summary>
    /// True when <paramref name="payload"/> is a valid payload of
    /// either supported format (26-char ULID or 12-char compact). This
    /// is the v6+ canonical payload check.
    /// </summary>
    public static bool IsValidPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return false;
        return payload.Length switch
        {
            UlidLength => UlidPattern.IsMatch(payload),
            CompactIdGenerator.PayloadLength => CompactIdGenerator.IsValidPayload(payload),
            _ => false,
        };
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
    /// Checks if an ID is a canonical production ID (either 26-char ULID
    /// or 12-char compact payload, with a recognised prefix). The name
    /// "canonical" predates the v6 compact format; in v6+ "canonical"
    /// means "passes <see cref="IsValidPayload"/> on a recognised prefix."
    /// </summary>
    public static bool IsCanonicalId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        var kind = IdGenerator.GetKindFromId(id);
        if (kind == null)
            return false;

        var payload = IdGenerator.ExtractIdPortion(id);
        return payload != null && IsValidPayload(payload);
    }

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]+$", RegexOptions.IgnoreCase)]
    private static partial Regex UlidPatternRegex();

    [GeneratedRegex("^(m|f|c|i|p|mt|ctor|op|e)(\\d{3,})$", RegexOptions.IgnoreCase)]
    private static partial Regex TestIdPatternRegex();
}
