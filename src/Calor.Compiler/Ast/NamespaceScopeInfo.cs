using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

public enum NamespaceScopeKind
{
    Named,
    Global
}

/// <summary>
/// A lexical C# namespace declaration preserved during C# → Calor migration.
/// Multiple declarations may have the same <see cref="FullName"/> while keeping
/// distinct using scopes.
/// </summary>
public sealed record NamespaceScopeInfo(
    string Id,
    string Name,
    string FullName,
    string? ParentScopeId,
    bool IsFileScoped,
    TextSpan Span,
    NamespaceScopeKind Kind = NamespaceScopeKind.Named)
{
    public bool IsGlobal => Kind == NamespaceScopeKind.Global;
}
