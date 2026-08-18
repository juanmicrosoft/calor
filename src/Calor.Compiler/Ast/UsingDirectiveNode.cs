using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a using directive for .NET namespace imports.
/// §U[System.Collections.Generic]            // using System.Collections.Generic;
/// §U[Gen:System.Collections.Generic]        // using Gen = System.Collections.Generic;
/// §U[static:System.Math]                    // using static System.Math;
/// §U[global:System.Text]                    // global using System.Text;
/// §U[global:static:System.Math]              // global using static System.Math;
/// §U[global:Gen:System.Collections.Generic]  // global using Gen = System.Collections.Generic;
/// </summary>
public sealed class UsingDirectiveNode : AstNode
{
    /// <summary>
    /// The namespace being imported.
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// Optional alias for the namespace (e.g., "Gen" in "using Gen = System.Collections.Generic").
    /// </summary>
    public string? Alias { get; }

    /// <summary>
    /// Whether this is a "using static" directive.
    /// </summary>
    public bool IsStatic { get; }

    /// <summary>
    /// Whether this is a file-wide <c>global using</c> directive.
    /// </summary>
    public bool IsGlobal { get; }

    public UsingDirectiveNode(
        TextSpan span,
        string @namespace,
        string? alias = null,
        bool isStatic = false,
        bool isGlobal = false,
        string? namespaceIdentity = null,
        string? namespaceScopeId = null)
        : base(span)
    {
        Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
        Alias = alias;
        IsStatic = isStatic;
        IsGlobal = isGlobal;
        NamespaceIdentity = namespaceIdentity;
        NamespaceScopeId = namespaceScopeId;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
