namespace Calor.Compiler.Effects;

/// <summary>
/// Serializable per-module summary of everything the cross-module effect pass needs:
///   - Module identity (name, file path tracked externally via cache key)
///   - Declared effects of public/internal functions + class methods (cross-module registry inputs)
///   - All internal function/method names (to resolve "is this call internal?" without the AST)
///   - Per-caller list of call targets with diagnostic spans (cross-module caller inputs)
///
/// Summaries are persisted in the build cache so that warm builds (where some files are
/// incrementally skipped) can still run a complete cross-module check using cached summaries
/// for skipped files alongside fresh summaries for newly-compiled files.
///
/// This type is a plain POCO (no domain types like EffectSet) for JSON compatibility.
/// </summary>
public sealed class EffectSummary
{
    public string ModuleName { get; set; } = "";

    /// <summary>
    /// Public/internal top-level functions eligible for the cross-module registry.
    /// </summary>
    public List<EffectFunctionSummary> PublicFunctions { get; set; } = new();

    /// <summary>
    /// Public/internal class methods eligible for the cross-module registry.
    /// <see cref="EffectFunctionSummary.ClassName"/> is populated for these.
    /// </summary>
    public List<EffectFunctionSummary> PublicMethods { get; set; } = new();

    /// <summary>
    /// All function names in the module (regardless of visibility). Used by the cross-module
    /// pass to decide whether a call target refers to an internal-to-this-module function
    /// (and should therefore be ignored, since the per-module pass handles it).
    /// </summary>
    public List<string> InternalFunctionNames { get; set; } = new();

    /// <summary>
    /// All bare method names for class methods in this module (regardless of visibility).
    /// Same purpose as <see cref="InternalFunctionNames"/>.
    /// </summary>
    public List<string> InternalMethodNames { get; set; } = new();

    /// <summary>
    /// Per-caller call-target listings, one per declaration, identified by the
    /// declaration's structural id (<see cref="EffectCallerSummary.CallerId"/>) —
    /// v0.15 E5, design-doc §8.5 / P26: the name is carried for messages and is
    /// not a key. Each caller tracks its own declared effects plus the call targets
    /// it issues; the cross-module pass uses this to verify that each caller
    /// declares the effects of its cross-module callees.
    /// </summary>
    public List<EffectCallerSummary> Callers { get; set; } = new();
}

/// <summary>
/// Declared effects of one public/internal function or method, plus the span to use
/// when reporting Calor0417 (no-declaration warning) or building call-graph indices.
/// </summary>
public sealed class EffectFunctionSummary
{
    public string Name { get; set; } = "";
    public string? ClassName { get; set; }

    /// <summary>True if the function declared §E at all; false if no declaration was present.</summary>
    public bool HasEffectDeclaration { get; set; }

    public List<EffectEntry> DeclaredEffects { get; set; } = new();

    public int DeclarationLine { get; set; }
    public int DeclarationColumn { get; set; }
}

/// <summary>
/// Call-site enumeration for one caller — its declared-effect span, its declared effects,
/// and every call target it issues.
/// </summary>
public sealed class EffectCallerSummary
{
    /// <summary>
    /// The declaration's STRUCTURAL id — <c>FunctionNode.Id</c> for a top-level
    /// function (<c>f001</c>), <c>ClassName.MethodId</c> for a member
    /// (<c>Cls.m001</c>): the key <c>CallGraphAnalysis</c> and
    /// <c>EffectEnforcementPass.DeclarationFacts</c> use, and the <c>ast:</c>
    /// component the binder's <c>SymbolId</c> embeds. v0.15 E5 (P26) replaced
    /// the name key that lived here; two members sharing a name in different
    /// classes are two entries, not one.
    /// </summary>
    public string CallerId { get; set; } = "";

    /// <summary>
    /// Top-level function name, or "ClassName.MethodName" for class methods —
    /// what the cross-module diagnostics print. Display only, never a key.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Span where diagnostics should be reported — pointing at the §E declaration
    /// (or the function span if no §E was declared).
    /// </summary>
    public int DiagnosticLine { get; set; }
    public int DiagnosticColumn { get; set; }

    public List<EffectEntry> DeclaredEffects { get; set; } = new();

    public List<EffectCallSummary> Calls { get; set; } = new();
}

/// <summary>
/// One call site collected from a caller's body.
/// </summary>
public sealed class EffectCallSummary
{
    public string Target { get; set; } = "";
    public bool IsConstructor { get; set; }
}

/// <summary>
/// One effect entry: kind (e.g., "IO") and internal value (e.g., "database_write").
/// Kind is serialized as the enum name (stable across releases) rather than as a number.
/// </summary>
public sealed class EffectEntry
{
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
}
