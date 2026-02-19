using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

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
    void Visit(ConditionalExpressionNode node);
    void Visit(FloatLiteralNode node);
    void Visit(ReferenceNode node);
    // Phase 2: Control Flow
    void Visit(ForStatementNode node);
    void Visit(WhileStatementNode node);
    void Visit(DoWhileStatementNode node);
    void Visit(IfStatementNode node);
    void Visit(BindStatementNode node);
    void Visit(BinaryOperationNode node);
    void Visit(UnaryOperationNode node);
    void Visit(ContinueStatementNode node);
    void Visit(BreakStatementNode node);
    // v2 built-in operations
    void Visit(PrintStatementNode node);
    // Phase 3: Type System
    void Visit(RecordDefinitionNode node);
    void Visit(UnionTypeDefinitionNode node);
    void Visit(EnumDefinitionNode node);
    void Visit(EnumMemberNode node);
    void Visit(EnumExtensionNode node);
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
    // Phase 6 Extended: Collections (List, Dictionary, HashSet)
    void Visit(ListCreationNode node);
    void Visit(DictionaryCreationNode node);
    void Visit(KeyValuePairNode node);
    void Visit(SetCreationNode node);
    void Visit(CollectionPushNode node);
    void Visit(DictionaryPutNode node);
    void Visit(CollectionRemoveNode node);
    void Visit(CollectionSetIndexNode node);
    void Visit(CollectionClearNode node);
    void Visit(CollectionInsertNode node);
    void Visit(CollectionContainsNode node);
    void Visit(DictionaryForeachNode node);
    void Visit(CollectionCountNode node);
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
    void Visit(CallExpressionNode node);
    void Visit(ThisExpressionNode node);
    void Visit(BaseExpressionNode node);
    // Phase 9: Properties and Constructors
    void Visit(PropertyNode node);
    void Visit(PropertyAccessorNode node);
    void Visit(ConstructorNode node);
    void Visit(ConstructorInitializerNode node);
    void Visit(AssignmentStatementNode node);
    void Visit(CompoundAssignmentStatementNode node);
    void Visit(UsingStatementNode node);
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
    // Extended Features Phase 1: Quick Wins
    void Visit(ExampleNode node);
    void Visit(IssueNode node);
    // Extended Features Phase 2: Core Features
    void Visit(DependencyNode node);
    void Visit(UsesNode node);
    void Visit(UsedByNode node);
    void Visit(AssumeNode node);
    // Extended Features Phase 3: Enhanced Contracts
    void Visit(ComplexityNode node);
    void Visit(SinceNode node);
    void Visit(DeprecatedNode node);
    void Visit(BreakingChangeNode node);
    // Extended Features Phase 4: Future Extensions
    void Visit(DecisionNode node);
    void Visit(RejectedOptionNode node);
    void Visit(ContextNode node);
    void Visit(FileRefNode node);
    void Visit(PropertyTestNode node);
    void Visit(LockNode node);
    void Visit(AuthorNode node);
    void Visit(TaskRefNode node);
    // C# Attribute Support
    void Visit(CalorAttributeNode node);
    // Quantified Contracts
    void Visit(QuantifierVariableNode node);
    void Visit(ForallExpressionNode node);
    void Visit(ExistsExpressionNode node);
    void Visit(ImplicationExpressionNode node);
    // Native String Operations
    void Visit(StringOperationNode node);
    // Native Char Operations
    void Visit(CharOperationNode node);
    // Native StringBuilder Operations
    void Visit(StringBuilderOperationNode node);
    // Native Type Operations
    void Visit(TypeOperationNode node);
    // Expression statement (bare expression as statement)
    void Visit(ExpressionStatementNode node);
    // Fallback nodes for unsupported C# constructs
    void Visit(FallbackExpressionNode node);
    void Visit(FallbackCommentNode node);
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
    T Visit(ConditionalExpressionNode node);
    T Visit(FloatLiteralNode node);
    T Visit(ReferenceNode node);
    // Phase 2: Control Flow
    T Visit(ForStatementNode node);
    T Visit(WhileStatementNode node);
    T Visit(DoWhileStatementNode node);
    T Visit(IfStatementNode node);
    T Visit(BindStatementNode node);
    T Visit(BinaryOperationNode node);
    T Visit(UnaryOperationNode node);
    T Visit(ContinueStatementNode node);
    T Visit(BreakStatementNode node);
    // v2 built-in operations
    T Visit(PrintStatementNode node);
    // Phase 3: Type System
    T Visit(RecordDefinitionNode node);
    T Visit(UnionTypeDefinitionNode node);
    T Visit(EnumDefinitionNode node);
    T Visit(EnumMemberNode node);
    T Visit(EnumExtensionNode node);
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
    // Phase 6 Extended: Collections (List, Dictionary, HashSet)
    T Visit(ListCreationNode node);
    T Visit(DictionaryCreationNode node);
    T Visit(KeyValuePairNode node);
    T Visit(SetCreationNode node);
    T Visit(CollectionPushNode node);
    T Visit(DictionaryPutNode node);
    T Visit(CollectionRemoveNode node);
    T Visit(CollectionSetIndexNode node);
    T Visit(CollectionClearNode node);
    T Visit(CollectionInsertNode node);
    T Visit(CollectionContainsNode node);
    T Visit(DictionaryForeachNode node);
    T Visit(CollectionCountNode node);
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
    T Visit(CallExpressionNode node);
    T Visit(ThisExpressionNode node);
    T Visit(BaseExpressionNode node);
    // Phase 9: Properties and Constructors
    T Visit(PropertyNode node);
    T Visit(PropertyAccessorNode node);
    T Visit(ConstructorNode node);
    T Visit(ConstructorInitializerNode node);
    T Visit(AssignmentStatementNode node);
    T Visit(CompoundAssignmentStatementNode node);
    T Visit(UsingStatementNode node);
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
    // Extended Features Phase 1: Quick Wins
    T Visit(ExampleNode node);
    T Visit(IssueNode node);
    // Extended Features Phase 2: Core Features
    T Visit(DependencyNode node);
    T Visit(UsesNode node);
    T Visit(UsedByNode node);
    T Visit(AssumeNode node);
    // Extended Features Phase 3: Enhanced Contracts
    T Visit(ComplexityNode node);
    T Visit(SinceNode node);
    T Visit(DeprecatedNode node);
    T Visit(BreakingChangeNode node);
    // Extended Features Phase 4: Future Extensions
    T Visit(DecisionNode node);
    T Visit(RejectedOptionNode node);
    T Visit(ContextNode node);
    T Visit(FileRefNode node);
    T Visit(PropertyTestNode node);
    T Visit(LockNode node);
    T Visit(AuthorNode node);
    T Visit(TaskRefNode node);
    // C# Attribute Support
    T Visit(CalorAttributeNode node);
    // Quantified Contracts
    T Visit(QuantifierVariableNode node);
    T Visit(ForallExpressionNode node);
    T Visit(ExistsExpressionNode node);
    T Visit(ImplicationExpressionNode node);
    // Native String Operations
    T Visit(StringOperationNode node);
    // Native Char Operations
    T Visit(CharOperationNode node);
    // Native StringBuilder Operations
    T Visit(StringBuilderOperationNode node);
    // Native Type Operations
    T Visit(TypeOperationNode node);
    // Expression statement (bare expression as statement)
    T Visit(ExpressionStatementNode node);
    // Fallback nodes for unsupported C# constructs
    T Visit(FallbackExpressionNode node);
    T Visit(FallbackCommentNode node);
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
