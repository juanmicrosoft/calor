namespace Calor.Compiler.Ids;

/// <summary>
/// The kinds of declarations that require unique IDs.
/// </summary>
public enum IdKind
{
    /// <summary>Module declaration (§M).</summary>
    Module,

    /// <summary>Function declaration (§F).</summary>
    Function,

    /// <summary>Class declaration (§CL).</summary>
    Class,

    /// <summary>Interface declaration (§IFACE).</summary>
    Interface,

    /// <summary>Property declaration (§PROP).</summary>
    Property,

    /// <summary>Method declaration (§MT).</summary>
    Method,

    /// <summary>Constructor declaration (§CTOR).</summary>
    Constructor,

    /// <summary>Enum declaration (§EN/§ENUM).</summary>
    Enum
}
