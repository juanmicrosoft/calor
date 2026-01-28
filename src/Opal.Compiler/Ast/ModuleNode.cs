using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents an OPAL module declaration.
/// §MODULE[id=xxx][name=xxx]
/// </summary>
public sealed class ModuleNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<FunctionNode> Functions { get; }
    public AttributeCollection Attributes { get; }

    public ModuleNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<FunctionNode> functions,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Functions = functions ?? throw new ArgumentNullException(nameof(functions));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
