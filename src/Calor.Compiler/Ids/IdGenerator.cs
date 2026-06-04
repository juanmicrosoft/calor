namespace Calor.Compiler.Ids;

/// <summary>
/// Generates canonical IDs for Calor declarations.
///
/// <para>
/// As of v0.6 (per <c>docs/plans/path-2-drop-ids-v6-implementation.md</c>),
/// <see cref="Generate(IdKind)"/> mints v6 <b>compact</b> IDs (12-char
/// Crockford-lowercase payload) via <see cref="CompactIdGenerator"/>.
/// The legacy ULID-based form (26-char Crockford-uppercase payload) is
/// still produced by <see cref="GenerateUlid(IdKind)"/> for tests and
/// back-compat scenarios that need the historical format. Both forms
/// remain valid input to the parser (see
/// <c>Parsing/AttributeHelper.cs</c>) and to <see cref="IdValidator"/>.
/// </para>
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
    /// The prefix for operator overload IDs.
    /// </summary>
    public const string OperatorOverloadPrefix = "op_";

    /// <summary>The prefix for enum extension IDs.</summary>
    public const string EnumExtensionPrefix = "ext_";

    /// <summary>The prefix for refinement type IDs.</summary>
    public const string RefinementTypePrefix = "rt_";

    /// <summary>The prefix for proof obligation IDs.</summary>
    public const string ProofObligationPrefix = "po_";

    /// <summary>The prefix for indexed type IDs.</summary>
    public const string IndexedTypePrefix = "it_";

    /// <summary>The prefix for indexer IDs.</summary>
    public const string IndexerPrefix = "ix_";

    /// <summary>
    /// Generates a new canonical (v6 compact) ID for the specified
    /// declaration kind. Equivalent to
    /// <see cref="CompactIdGenerator.Generate(IdKind)"/>.
    /// </summary>
    /// <param name="kind">The kind of declaration.</param>
    /// <returns>A new unique compact ID with the appropriate prefix.</returns>
    public static string Generate(IdKind kind) => CompactIdGenerator.Generate(kind);

    /// <summary>
    /// Generates a new canonical (v6 compact) ID with the specified prefix.
    /// </summary>
    public static string GenerateWithPrefix(string prefix) =>
        CompactIdGenerator.GenerateWithPrefix(prefix);

    /// <summary>
    /// Generates a legacy 26-char Crockford-uppercase ULID-based ID.
    /// Retained for back-compat (tests, format-pinned fixtures) — new
    /// code should call <see cref="Generate(IdKind)"/>, which produces
    /// the v6 compact form.
    /// </summary>
    public static string GenerateUlid(IdKind kind)
    {
        var ulid = Ulid.NewUlid();
        return GetPrefix(kind) + ulid.ToString();
    }

    /// <summary>
    /// Generates a legacy 26-char Crockford-uppercase ULID-based ID with
    /// the specified prefix.
    /// </summary>
    public static string GenerateUlidWithPrefix(string prefix)
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
        IdKind.EnumExtension => EnumExtensionPrefix,
        IdKind.OperatorOverload => OperatorOverloadPrefix,
        IdKind.RefinementType => RefinementTypePrefix,
        IdKind.ProofObligation => ProofObligationPrefix,
        IdKind.IndexedType => IndexedTypePrefix,
        IdKind.Indexer => IndexerPrefix,
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

        // Check prefixes in order from longest to shortest to avoid partial matches.
        if (id.StartsWith(ConstructorPrefix))
            return IdKind.Constructor;
        if (id.StartsWith(EnumExtensionPrefix))
            return IdKind.EnumExtension;
        if (id.StartsWith(OperatorOverloadPrefix))
            return IdKind.OperatorOverload;
        if (id.StartsWith(ProofObligationPrefix))
            return IdKind.ProofObligation;
        if (id.StartsWith(RefinementTypePrefix))
            return IdKind.RefinementType;
        if (id.StartsWith(IndexedTypePrefix))
            return IdKind.IndexedType;
        if (id.StartsWith(IndexerPrefix))
            return IdKind.Indexer;
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
    /// Extracts the payload portion (i.e. everything after the kind
    /// prefix) from an ID. Works for both v6 compact (12-char) and
    /// legacy ULID (26-char) IDs.
    /// </summary>
    public static string? ExtractPayload(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var kind = GetKindFromId(id);
        if (kind == null)
            return null;

        var prefix = GetPrefix(kind.Value);
        return id.Substring(prefix.Length);
    }

    /// <summary>
    /// Extracts the ULID portion from an ID. Returns null when the
    /// payload is not a 26-char ULID (e.g. it's a v6 compact payload).
    /// New code should prefer <see cref="ExtractPayload(string)"/>.
    /// </summary>
    public static string? ExtractUlid(string id)
    {
        var payload = ExtractPayload(id);
        if (payload == null || payload.Length != IdValidator.UlidLength)
            return null;
        return payload;
    }
}
