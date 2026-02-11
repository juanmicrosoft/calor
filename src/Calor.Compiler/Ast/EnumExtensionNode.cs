using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents an enum extension definition containing methods that extend an enum type.
/// §EEXT{id:EnumName}
///   §F{f001:MethodName:pub}
///     §I{EnumType:self}
///     §O{returnType}
///     // body
///   §/F{f001}
/// §/EEXT{id}
/// </summary>
public sealed class EnumExtensionNode : AstNode
{
    /// <summary>
    /// The unique identifier for this enum extension.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The name of the enum type being extended.
    /// </summary>
    public string EnumName { get; }

    /// <summary>
    /// The extension methods defined for this enum.
    /// </summary>
    public IReadOnlyList<FunctionNode> Methods { get; }

    /// <summary>
    /// The attributes on this enum extension.
    /// </summary>
    public AttributeCollection Attributes { get; }

    public EnumExtensionNode(
        TextSpan span,
        string id,
        string enumName,
        IReadOnlyList<FunctionNode> methods,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        EnumName = enumName ?? throw new ArgumentNullException(nameof(enumName));
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
