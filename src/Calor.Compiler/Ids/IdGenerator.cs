using System.Security.Cryptography;

namespace Calor.Compiler.Ids;

/// <summary>
/// Generates canonical ULID-based IDs for Calor declarations.
/// </summary>
public static class IdGenerator
{
    /// <summary>
    /// The prefix for module IDs.
    /// </summary>
    public const string ModulePrefix = "m_";

    /// <summary>
    /// The prefix for function IDs.
    /// </summary>
    public const string FunctionPrefix = "f_";

    /// <summary>
    /// The prefix for class IDs.
    /// </summary>
    public const string ClassPrefix = "c_";

    /// <summary>
    /// The prefix for interface IDs.
    /// </summary>
    public const string InterfacePrefix = "i_";

    /// <summary>
    /// The prefix for property IDs.
    /// </summary>
    public const string PropertyPrefix = "p_";

    /// <summary>
    /// The prefix for method IDs.
    /// </summary>
    public const string MethodPrefix = "mt_";

    /// <summary>
    /// The prefix for constructor IDs.
    /// </summary>
    public const string ConstructorPrefix = "ctor_";

    /// <summary>
    /// The prefix for enum IDs.
    /// </summary>
    public const string EnumPrefix = "e_";

    /// <summary>
    /// Generates a new ID for the specified declaration kind.
    /// </summary>
    /// <param name="kind">The kind of declaration.</param>
    /// <returns>A new unique ID with the appropriate prefix.</returns>
    public static string Generate(IdKind kind)
    {
        var ulid = Ulid.NewUlid();
        return GetPrefix(kind) + ulid.ToString();
    }

    /// <summary>
    /// Generates a new ID with the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to use.</param>
    /// <returns>A new unique ID with the specified prefix.</returns>
    public static string GenerateWithPrefix(string prefix)
    {
        var ulid = Ulid.NewUlid();
        return prefix + ulid.ToString();
    }

    /// <summary>
    /// Gets the expected prefix for a declaration kind.
    /// </summary>
    /// <param name="kind">The declaration kind.</param>
    /// <returns>The prefix string.</returns>
    public static string GetPrefix(IdKind kind) => kind switch
    {
        IdKind.Module => ModulePrefix,
        IdKind.Function => FunctionPrefix,
        IdKind.Class => ClassPrefix,
        IdKind.Interface => InterfacePrefix,
        IdKind.Property => PropertyPrefix,
        IdKind.Method => MethodPrefix,
        IdKind.Constructor => ConstructorPrefix,
        IdKind.Enum => EnumPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Gets the declaration kind from an ID based on its prefix.
    /// </summary>
    /// <param name="id">The ID to check.</param>
    /// <returns>The kind, or null if the prefix is not recognized.</returns>
    public static IdKind? GetKindFromId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        // Check prefixes in order from longest to shortest to avoid partial matches
        if (id.StartsWith(ConstructorPrefix))
            return IdKind.Constructor;
        if (id.StartsWith(MethodPrefix))
            return IdKind.Method;
        if (id.StartsWith(ModulePrefix))
            return IdKind.Module;
        if (id.StartsWith(FunctionPrefix))
            return IdKind.Function;
        if (id.StartsWith(ClassPrefix))
            return IdKind.Class;
        if (id.StartsWith(InterfacePrefix))
            return IdKind.Interface;
        if (id.StartsWith(PropertyPrefix))
            return IdKind.Property;
        if (id.StartsWith(EnumPrefix))
            return IdKind.Enum;

        return null;
    }

    /// <summary>
    /// Extracts the ULID portion from an ID (removes prefix).
    /// </summary>
    /// <param name="id">The full ID with prefix.</param>
    /// <returns>Just the ULID portion, or null if invalid.</returns>
    public static string? ExtractUlid(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var kind = GetKindFromId(id);
        if (kind == null)
            return null;

        var prefix = GetPrefix(kind.Value);
        return id.Substring(prefix.Length);
    }
}
