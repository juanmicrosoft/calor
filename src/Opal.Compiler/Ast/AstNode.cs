using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Base class for all AST nodes.
/// </summary>
public abstract class AstNode
{
    public TextSpan Span { get; }

    protected AstNode(TextSpan span)
    {
        Span = span;
    }

    public abstract void Accept(IAstVisitor visitor);
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

/// <summary>
/// Visitor interface for AST nodes.
/// </summary>
public interface IAstVisitor
{
    void Visit(ModuleNode node);
    void Visit(FunctionNode node);
    void Visit(ParameterNode node);
    void Visit(CallStatementNode node);
    void Visit(ReturnStatementNode node);
    void Visit(IntLiteralNode node);
    void Visit(StringLiteralNode node);
    void Visit(BoolLiteralNode node);
    void Visit(FloatLiteralNode node);
    void Visit(ReferenceNode node);
    // Phase 2: Control Flow
    void Visit(ForStatementNode node);
    void Visit(WhileStatementNode node);
    void Visit(IfStatementNode node);
    void Visit(BindStatementNode node);
    void Visit(BinaryOperationNode node);
    // Phase 3: Type System
    void Visit(RecordDefinitionNode node);
    void Visit(UnionTypeDefinitionNode node);
    void Visit(RecordCreationNode node);
    void Visit(FieldAccessNode node);
    void Visit(SomeExpressionNode node);
    void Visit(NoneExpressionNode node);
    void Visit(OkExpressionNode node);
    void Visit(ErrExpressionNode node);
    void Visit(MatchExpressionNode node);
    void Visit(MatchStatementNode node);
    // Phase 4: Contracts
    void Visit(RequiresNode node);
    void Visit(EnsuresNode node);
    void Visit(InvariantNode node);
}

/// <summary>
/// Visitor interface for AST nodes with return values.
/// </summary>
public interface IAstVisitor<T>
{
    T Visit(ModuleNode node);
    T Visit(FunctionNode node);
    T Visit(ParameterNode node);
    T Visit(CallStatementNode node);
    T Visit(ReturnStatementNode node);
    T Visit(IntLiteralNode node);
    T Visit(StringLiteralNode node);
    T Visit(BoolLiteralNode node);
    T Visit(FloatLiteralNode node);
    T Visit(ReferenceNode node);
    // Phase 2: Control Flow
    T Visit(ForStatementNode node);
    T Visit(WhileStatementNode node);
    T Visit(IfStatementNode node);
    T Visit(BindStatementNode node);
    T Visit(BinaryOperationNode node);
    // Phase 3: Type System
    T Visit(RecordDefinitionNode node);
    T Visit(UnionTypeDefinitionNode node);
    T Visit(RecordCreationNode node);
    T Visit(FieldAccessNode node);
    T Visit(SomeExpressionNode node);
    T Visit(NoneExpressionNode node);
    T Visit(OkExpressionNode node);
    T Visit(ErrExpressionNode node);
    T Visit(MatchExpressionNode node);
    T Visit(MatchStatementNode node);
    // Phase 4: Contracts
    T Visit(RequiresNode node);
    T Visit(EnsuresNode node);
    T Visit(InvariantNode node);
}

/// <summary>
/// Represents a collection of key-value attributes on a tag.
/// </summary>
public sealed class AttributeCollection
{
    private readonly Dictionary<string, string> _attributes = new(StringComparer.Ordinal);

    public string? this[string key]
        => _attributes.TryGetValue(key, out var value) ? value : null;

    public void Add(string key, string value)
    {
        _attributes[key] = value;
    }

    public bool TryGetValue(string key, out string? value)
        => _attributes.TryGetValue(key, out value);

    public bool ContainsKey(string key)
        => _attributes.ContainsKey(key);

    public IEnumerable<KeyValuePair<string, string>> All()
        => _attributes;
}
