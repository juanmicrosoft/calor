using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ids;

/// <summary>
/// Represents an ID entry found in a Calor file.
/// </summary>
/// <param name="Id">The ID string value.</param>
/// <param name="Kind">The declaration kind this ID belongs to.</param>
/// <param name="Name">The name of the declaration (function name, class name, etc.).</param>
/// <param name="Span">The text span where this ID appears.</param>
/// <param name="FilePath">The file path where this ID was found.</param>
public sealed record IdEntry(
    string Id,
    IdKind Kind,
    string Name,
    TextSpan Span,
    string FilePath)
{
    /// <summary>
    /// Returns true if this is a test ID (e.g., f001, m001).
    /// </summary>
    public bool IsTestId => IdValidator.IsTestId(Id);

    /// <summary>
    /// Returns true if this ID is missing or empty.
    /// </summary>
    public bool IsMissing => string.IsNullOrEmpty(Id);
}
