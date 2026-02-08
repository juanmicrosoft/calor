namespace Calor.Compiler.Ids;

/// <summary>
/// The result of validating an ID.
/// </summary>
public enum IdValidationResult
{
    /// <summary>The ID is valid.</summary>
    Valid,

    /// <summary>The ID is missing (empty or null).</summary>
    Missing,

    /// <summary>The ID format is invalid (not a valid ULID or test ID).</summary>
    InvalidFormat,

    /// <summary>The ID prefix doesn't match the declaration kind.</summary>
    WrongPrefix,

    /// <summary>The ID is a test ID (f001) in production code.</summary>
    TestIdInProduction,

    /// <summary>The ID is a duplicate of another ID in the project.</summary>
    Duplicate
}
