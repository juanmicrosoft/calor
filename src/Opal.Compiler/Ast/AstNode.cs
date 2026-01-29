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
    void Visit(MatchCaseNode node);
    void Visit(WildcardPatternNode node);
    void Visit(VariablePatternNode node);
    void Visit(LiteralPatternNode node);
    void Visit(SomePatternNode node);
    void Visit(NonePatternNode node);
    void Visit(OkPatternNode node);
    void Visit(ErrPatternNode node);
    // Phase 4: Contracts
    void Visit(RequiresNode node);
    void Visit(EnsuresNode node);
    void Visit(InvariantNode node);
    // Phase 5: Using Statements
    void Visit(UsingDirectiveNode node);
    // Phase 6: Arrays and Collections
    void Visit(ArrayCreationNode node);
    void Visit(ArrayAccessNode node);
    void Visit(ArrayLengthNode node);
    void Visit(ForeachStatementNode node);
    // Phase 7: Generics
    void Visit(TypeParameterNode node);
    void Visit(TypeConstraintNode node);
    void Visit(GenericTypeNode node);
    // Phase 8: Classes, Interfaces, Inheritance
    void Visit(InterfaceDefinitionNode node);
    void Visit(MethodSignatureNode node);
    void Visit(ClassDefinitionNode node);
    void Visit(ClassFieldNode node);
    void Visit(MethodNode node);
    void Visit(NewExpressionNode node);
    void Visit(ThisExpressionNode node);
    void Visit(BaseExpressionNode node);
    // Phase 9: Properties and Constructors
    void Visit(PropertyNode node);
    void Visit(PropertyAccessorNode node);
    void Visit(ConstructorNode node);
    void Visit(ConstructorInitializerNode node);
    void Visit(AssignmentStatementNode node);
    // Phase 10: Try/Catch/Finally
    void Visit(TryStatementNode node);
    void Visit(CatchClauseNode node);
    void Visit(ThrowStatementNode node);
    void Visit(RethrowStatementNode node);
    // Phase 11: Lambdas, Delegates, Events
    void Visit(LambdaParameterNode node);
    void Visit(LambdaExpressionNode node);
    void Visit(DelegateDefinitionNode node);
    void Visit(EventDefinitionNode node);
    void Visit(EventSubscribeNode node);
    void Visit(EventUnsubscribeNode node);
    // Phase 12: Async/Await
    void Visit(AwaitExpressionNode node);
    // Phase 9: String Interpolation and Modern Operators
    void Visit(InterpolatedStringNode node);
    void Visit(InterpolatedStringTextNode node);
    void Visit(InterpolatedStringExpressionNode node);
    void Visit(NullCoalesceNode node);
    void Visit(NullConditionalNode node);
    void Visit(RangeExpressionNode node);
    void Visit(IndexFromEndNode node);
    // Phase 10: Advanced Patterns
    void Visit(WithExpressionNode node);
    void Visit(WithPropertyAssignmentNode node);
    void Visit(PositionalPatternNode node);
    void Visit(PropertyPatternNode node);
    void Visit(PropertyMatchNode node);
    void Visit(RelationalPatternNode node);
    void Visit(ListPatternNode node);
    void Visit(VarPatternNode node);
    void Visit(ConstantPatternNode node);
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
    T Visit(MatchCaseNode node);
    T Visit(WildcardPatternNode node);
    T Visit(VariablePatternNode node);
    T Visit(LiteralPatternNode node);
    T Visit(SomePatternNode node);
    T Visit(NonePatternNode node);
    T Visit(OkPatternNode node);
    T Visit(ErrPatternNode node);
    // Phase 4: Contracts
    T Visit(RequiresNode node);
    T Visit(EnsuresNode node);
    T Visit(InvariantNode node);
    // Phase 5: Using Statements
    T Visit(UsingDirectiveNode node);
    // Phase 6: Arrays and Collections
    T Visit(ArrayCreationNode node);
    T Visit(ArrayAccessNode node);
    T Visit(ArrayLengthNode node);
    T Visit(ForeachStatementNode node);
    // Phase 7: Generics
    T Visit(TypeParameterNode node);
    T Visit(TypeConstraintNode node);
    T Visit(GenericTypeNode node);
    // Phase 8: Classes, Interfaces, Inheritance
    T Visit(InterfaceDefinitionNode node);
    T Visit(MethodSignatureNode node);
    T Visit(ClassDefinitionNode node);
    T Visit(ClassFieldNode node);
    T Visit(MethodNode node);
    T Visit(NewExpressionNode node);
    T Visit(ThisExpressionNode node);
    T Visit(BaseExpressionNode node);
    // Phase 9: Properties and Constructors
    T Visit(PropertyNode node);
    T Visit(PropertyAccessorNode node);
    T Visit(ConstructorNode node);
    T Visit(ConstructorInitializerNode node);
    T Visit(AssignmentStatementNode node);
    // Phase 10: Try/Catch/Finally
    T Visit(TryStatementNode node);
    T Visit(CatchClauseNode node);
    T Visit(ThrowStatementNode node);
    T Visit(RethrowStatementNode node);
    // Phase 11: Lambdas, Delegates, Events
    T Visit(LambdaParameterNode node);
    T Visit(LambdaExpressionNode node);
    T Visit(DelegateDefinitionNode node);
    T Visit(EventDefinitionNode node);
    T Visit(EventSubscribeNode node);
    T Visit(EventUnsubscribeNode node);
    // Phase 12: Async/Await
    T Visit(AwaitExpressionNode node);
    // Phase 9: String Interpolation and Modern Operators
    T Visit(InterpolatedStringNode node);
    T Visit(InterpolatedStringTextNode node);
    T Visit(InterpolatedStringExpressionNode node);
    T Visit(NullCoalesceNode node);
    T Visit(NullConditionalNode node);
    T Visit(RangeExpressionNode node);
    T Visit(IndexFromEndNode node);
    // Phase 10: Advanced Patterns
    T Visit(WithExpressionNode node);
    T Visit(WithPropertyAssignmentNode node);
    T Visit(PositionalPatternNode node);
    T Visit(PropertyPatternNode node);
    T Visit(PropertyMatchNode node);
    T Visit(RelationalPatternNode node);
    T Visit(ListPatternNode node);
    T Visit(VarPatternNode node);
    T Visit(ConstantPatternNode node);
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
