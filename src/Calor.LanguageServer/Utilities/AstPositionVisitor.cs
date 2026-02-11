using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.LanguageServer.Utilities;

/// <summary>
/// Base class for AST visitors that search by position.
/// Provides position checking utilities and default traversal behavior.
/// </summary>
public abstract class AstPositionVisitor<T> : IAstVisitor<T> where T : class?
{
    protected int TargetOffset { get; private set; }
    protected T? FoundResult { get; set; }

    /// <summary>
    /// Sets the target offset for the search.
    /// </summary>
    public void SetTargetOffset(int offset)
    {
        TargetOffset = offset;
        FoundResult = null;
    }

    /// <summary>
    /// Checks if the span contains the target offset.
    /// </summary>
    protected bool SpanContainsTarget(TextSpan span)
        => TargetOffset >= span.Start && TargetOffset < span.End;

    /// <summary>
    /// Default visitor implementation - returns null.
    /// Override in derived classes to provide specific handling.
    /// </summary>
    protected virtual T? DefaultVisit(AstNode node) => default;

    // Core nodes
    public virtual T Visit(ModuleNode node) => DefaultVisit(node)!;
    public virtual T Visit(FunctionNode node) => DefaultVisit(node)!;
    public virtual T Visit(ParameterNode node) => DefaultVisit(node)!;
    public virtual T Visit(CallStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(ReturnStatementNode node) => DefaultVisit(node)!;

    // Literals
    public virtual T Visit(IntLiteralNode node) => DefaultVisit(node)!;
    public virtual T Visit(StringLiteralNode node) => DefaultVisit(node)!;
    public virtual T Visit(BoolLiteralNode node) => DefaultVisit(node)!;
    public virtual T Visit(FloatLiteralNode node) => DefaultVisit(node)!;
    public virtual T Visit(ReferenceNode node) => DefaultVisit(node)!;
    public virtual T Visit(ConditionalExpressionNode node) => DefaultVisit(node)!;

    // Control flow
    public virtual T Visit(ForStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(WhileStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(DoWhileStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(IfStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(BindStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(BinaryOperationNode node) => DefaultVisit(node)!;
    public virtual T Visit(UnaryOperationNode node) => DefaultVisit(node)!;
    public virtual T Visit(ContinueStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(BreakStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(PrintStatementNode node) => DefaultVisit(node)!;

    // Type system
    public virtual T Visit(RecordDefinitionNode node) => DefaultVisit(node)!;
    public virtual T Visit(UnionTypeDefinitionNode node) => DefaultVisit(node)!;
    public virtual T Visit(EnumDefinitionNode node) => DefaultVisit(node)!;
    public virtual T Visit(EnumMemberNode node) => DefaultVisit(node)!;
    public virtual T Visit(EnumExtensionNode node) => DefaultVisit(node)!;
    public virtual T Visit(RecordCreationNode node) => DefaultVisit(node)!;
    public virtual T Visit(FieldAccessNode node) => DefaultVisit(node)!;

    // Option/Result
    public virtual T Visit(SomeExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(NoneExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(OkExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(ErrExpressionNode node) => DefaultVisit(node)!;

    // Pattern matching
    public virtual T Visit(MatchExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(MatchStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(MatchCaseNode node) => DefaultVisit(node)!;
    public virtual T Visit(WildcardPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(VariablePatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(LiteralPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(SomePatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(NonePatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(OkPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(ErrPatternNode node) => DefaultVisit(node)!;

    // Contracts
    public virtual T Visit(RequiresNode node) => DefaultVisit(node)!;
    public virtual T Visit(EnsuresNode node) => DefaultVisit(node)!;
    public virtual T Visit(InvariantNode node) => DefaultVisit(node)!;

    // Using directives
    public virtual T Visit(UsingDirectiveNode node) => DefaultVisit(node)!;

    // Arrays and collections
    public virtual T Visit(ArrayCreationNode node) => DefaultVisit(node)!;
    public virtual T Visit(ArrayAccessNode node) => DefaultVisit(node)!;
    public virtual T Visit(ArrayLengthNode node) => DefaultVisit(node)!;
    public virtual T Visit(ForeachStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(ListCreationNode node) => DefaultVisit(node)!;
    public virtual T Visit(DictionaryCreationNode node) => DefaultVisit(node)!;
    public virtual T Visit(KeyValuePairNode node) => DefaultVisit(node)!;
    public virtual T Visit(SetCreationNode node) => DefaultVisit(node)!;
    public virtual T Visit(CollectionPushNode node) => DefaultVisit(node)!;
    public virtual T Visit(DictionaryPutNode node) => DefaultVisit(node)!;
    public virtual T Visit(CollectionRemoveNode node) => DefaultVisit(node)!;
    public virtual T Visit(CollectionSetIndexNode node) => DefaultVisit(node)!;
    public virtual T Visit(CollectionClearNode node) => DefaultVisit(node)!;
    public virtual T Visit(CollectionInsertNode node) => DefaultVisit(node)!;
    public virtual T Visit(CollectionContainsNode node) => DefaultVisit(node)!;
    public virtual T Visit(DictionaryForeachNode node) => DefaultVisit(node)!;
    public virtual T Visit(CollectionCountNode node) => DefaultVisit(node)!;

    // Generics
    public virtual T Visit(TypeParameterNode node) => DefaultVisit(node)!;
    public virtual T Visit(TypeConstraintNode node) => DefaultVisit(node)!;
    public virtual T Visit(GenericTypeNode node) => DefaultVisit(node)!;

    // Classes and interfaces
    public virtual T Visit(InterfaceDefinitionNode node) => DefaultVisit(node)!;
    public virtual T Visit(MethodSignatureNode node) => DefaultVisit(node)!;
    public virtual T Visit(ClassDefinitionNode node) => DefaultVisit(node)!;
    public virtual T Visit(ClassFieldNode node) => DefaultVisit(node)!;
    public virtual T Visit(MethodNode node) => DefaultVisit(node)!;
    public virtual T Visit(NewExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(CallExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(ThisExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(BaseExpressionNode node) => DefaultVisit(node)!;

    // Properties and constructors
    public virtual T Visit(PropertyNode node) => DefaultVisit(node)!;
    public virtual T Visit(PropertyAccessorNode node) => DefaultVisit(node)!;
    public virtual T Visit(ConstructorNode node) => DefaultVisit(node)!;
    public virtual T Visit(ConstructorInitializerNode node) => DefaultVisit(node)!;
    public virtual T Visit(AssignmentStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(CompoundAssignmentStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(UsingStatementNode node) => DefaultVisit(node)!;

    // Exception handling
    public virtual T Visit(TryStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(CatchClauseNode node) => DefaultVisit(node)!;
    public virtual T Visit(ThrowStatementNode node) => DefaultVisit(node)!;
    public virtual T Visit(RethrowStatementNode node) => DefaultVisit(node)!;

    // Lambdas and events
    public virtual T Visit(LambdaParameterNode node) => DefaultVisit(node)!;
    public virtual T Visit(LambdaExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(DelegateDefinitionNode node) => DefaultVisit(node)!;
    public virtual T Visit(EventDefinitionNode node) => DefaultVisit(node)!;
    public virtual T Visit(EventSubscribeNode node) => DefaultVisit(node)!;
    public virtual T Visit(EventUnsubscribeNode node) => DefaultVisit(node)!;

    // Async
    public virtual T Visit(AwaitExpressionNode node) => DefaultVisit(node)!;

    // String interpolation and modern operators
    public virtual T Visit(InterpolatedStringNode node) => DefaultVisit(node)!;
    public virtual T Visit(InterpolatedStringTextNode node) => DefaultVisit(node)!;
    public virtual T Visit(InterpolatedStringExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(NullCoalesceNode node) => DefaultVisit(node)!;
    public virtual T Visit(NullConditionalNode node) => DefaultVisit(node)!;
    public virtual T Visit(RangeExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(IndexFromEndNode node) => DefaultVisit(node)!;

    // Advanced patterns
    public virtual T Visit(WithExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(WithPropertyAssignmentNode node) => DefaultVisit(node)!;
    public virtual T Visit(PositionalPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(PropertyPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(PropertyMatchNode node) => DefaultVisit(node)!;
    public virtual T Visit(RelationalPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(ListPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(VarPatternNode node) => DefaultVisit(node)!;
    public virtual T Visit(ConstantPatternNode node) => DefaultVisit(node)!;

    // Extended features - documentation
    public virtual T Visit(ExampleNode node) => DefaultVisit(node)!;
    public virtual T Visit(IssueNode node) => DefaultVisit(node)!;
    public virtual T Visit(DependencyNode node) => DefaultVisit(node)!;
    public virtual T Visit(UsesNode node) => DefaultVisit(node)!;
    public virtual T Visit(UsedByNode node) => DefaultVisit(node)!;
    public virtual T Visit(AssumeNode node) => DefaultVisit(node)!;
    public virtual T Visit(ComplexityNode node) => DefaultVisit(node)!;
    public virtual T Visit(SinceNode node) => DefaultVisit(node)!;
    public virtual T Visit(DeprecatedNode node) => DefaultVisit(node)!;
    public virtual T Visit(BreakingChangeNode node) => DefaultVisit(node)!;
    public virtual T Visit(DecisionNode node) => DefaultVisit(node)!;
    public virtual T Visit(RejectedOptionNode node) => DefaultVisit(node)!;
    public virtual T Visit(ContextNode node) => DefaultVisit(node)!;
    public virtual T Visit(FileRefNode node) => DefaultVisit(node)!;
    public virtual T Visit(PropertyTestNode node) => DefaultVisit(node)!;
    public virtual T Visit(LockNode node) => DefaultVisit(node)!;
    public virtual T Visit(AuthorNode node) => DefaultVisit(node)!;
    public virtual T Visit(TaskRefNode node) => DefaultVisit(node)!;

    // Attributes
    public virtual T Visit(CalorAttributeNode node) => DefaultVisit(node)!;

    // Quantifiers
    public virtual T Visit(QuantifierVariableNode node) => DefaultVisit(node)!;
    public virtual T Visit(ForallExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(ExistsExpressionNode node) => DefaultVisit(node)!;
    public virtual T Visit(ImplicationExpressionNode node) => DefaultVisit(node)!;

    // Native operations
    public virtual T Visit(StringOperationNode node) => DefaultVisit(node)!;
    public virtual T Visit(CharOperationNode node) => DefaultVisit(node)!;
    public virtual T Visit(StringBuilderOperationNode node) => DefaultVisit(node)!;
}
