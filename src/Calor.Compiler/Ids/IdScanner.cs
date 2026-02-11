using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ids;

/// <summary>
/// Scans Calor AST nodes to collect all IDs with their metadata.
/// </summary>
public sealed class IdScanner : IAstVisitor
{
    private readonly List<IdEntry> _entries = new();
    private string _currentFilePath = "";

    /// <summary>
    /// The collected ID entries.
    /// </summary>
    public IReadOnlyList<IdEntry> Entries => _entries;

    /// <summary>
    /// Scans a module node and collects all ID entries.
    /// </summary>
    /// <param name="module">The module to scan.</param>
    /// <param name="filePath">The file path of the module.</param>
    /// <returns>The list of ID entries found.</returns>
    public IReadOnlyList<IdEntry> Scan(ModuleNode module, string filePath)
    {
        _currentFilePath = filePath;
        _entries.Clear();
        module.Accept(this);
        return _entries.ToList();
    }

    /// <summary>
    /// Scans multiple files and returns all ID entries.
    /// </summary>
    /// <param name="files">Files to scan (path -> parsed module).</param>
    /// <returns>All ID entries across all files.</returns>
    public static IReadOnlyList<IdEntry> ScanFiles(IEnumerable<(string Path, ModuleNode Module)> files)
    {
        var scanner = new IdScanner();
        var allEntries = new List<IdEntry>();

        foreach (var (path, module) in files)
        {
            allEntries.AddRange(scanner.Scan(module, path));
        }

        return allEntries;
    }

    public void Visit(ModuleNode node)
    {
        AddEntry(node.Id, IdKind.Module, node.Name, node.Span);

        foreach (var function in node.Functions)
            function.Accept(this);

        foreach (var iface in node.Interfaces)
            iface.Accept(this);

        foreach (var cls in node.Classes)
            cls.Accept(this);

        foreach (var enumDef in node.Enums)
            enumDef.Accept(this);
    }

    public void Visit(FunctionNode node)
    {
        AddEntry(node.Id, IdKind.Function, node.Name, node.Span);
    }

    public void Visit(InterfaceDefinitionNode node)
    {
        AddEntry(node.Id, IdKind.Interface, node.Name, node.Span);

        foreach (var method in node.Methods)
            method.Accept(this);
    }

    public void Visit(MethodSignatureNode node)
    {
        AddEntry(node.Id, IdKind.Method, node.Name, node.Span);
    }

    public void Visit(ClassDefinitionNode node)
    {
        AddEntry(node.Id, IdKind.Class, node.Name, node.Span);

        foreach (var property in node.Properties)
            property.Accept(this);

        foreach (var constructor in node.Constructors)
            constructor.Accept(this);

        foreach (var method in node.Methods)
            method.Accept(this);
    }

    public void Visit(PropertyNode node)
    {
        AddEntry(node.Id, IdKind.Property, node.Name, node.Span);
    }

    public void Visit(ConstructorNode node)
    {
        // Constructors don't have a Name property - use the ID as the name identifier
        AddEntry(node.Id, IdKind.Constructor, ".ctor", node.Span);
    }

    public void Visit(MethodNode node)
    {
        AddEntry(node.Id, IdKind.Method, node.Name, node.Span);
    }

    public void Visit(EnumDefinitionNode node)
    {
        AddEntry(node.Id, IdKind.Enum, node.Name, node.Span);
    }

    private void AddEntry(string id, IdKind kind, string name, TextSpan span)
    {
        _entries.Add(new IdEntry(id, kind, name, span, _currentFilePath));
    }

    // All other visitor methods - no IDs to collect from these nodes
    public void Visit(ParameterNode node) { }
    public void Visit(CallStatementNode node) { }
    public void Visit(ReturnStatementNode node) { }
    public void Visit(IntLiteralNode node) { }
    public void Visit(StringLiteralNode node) { }
    public void Visit(BoolLiteralNode node) { }
    public void Visit(ConditionalExpressionNode node) { }
    public void Visit(FloatLiteralNode node) { }
    public void Visit(ReferenceNode node) { }
    public void Visit(ForStatementNode node) { }
    public void Visit(WhileStatementNode node) { }
    public void Visit(DoWhileStatementNode node) { }
    public void Visit(IfStatementNode node) { }
    public void Visit(BindStatementNode node) { }
    public void Visit(BinaryOperationNode node) { }
    public void Visit(UnaryOperationNode node) { }
    public void Visit(ContinueStatementNode node) { }
    public void Visit(BreakStatementNode node) { }
    public void Visit(PrintStatementNode node) { }
    public void Visit(RecordDefinitionNode node) { }
    public void Visit(UnionTypeDefinitionNode node) { }
    public void Visit(EnumMemberNode node) { }
    public void Visit(RecordCreationNode node) { }
    public void Visit(FieldAccessNode node) { }
    public void Visit(SomeExpressionNode node) { }
    public void Visit(NoneExpressionNode node) { }
    public void Visit(OkExpressionNode node) { }
    public void Visit(ErrExpressionNode node) { }
    public void Visit(MatchExpressionNode node) { }
    public void Visit(MatchStatementNode node) { }
    public void Visit(MatchCaseNode node) { }
    public void Visit(WildcardPatternNode node) { }
    public void Visit(VariablePatternNode node) { }
    public void Visit(LiteralPatternNode node) { }
    public void Visit(SomePatternNode node) { }
    public void Visit(NonePatternNode node) { }
    public void Visit(OkPatternNode node) { }
    public void Visit(ErrPatternNode node) { }
    public void Visit(RequiresNode node) { }
    public void Visit(EnsuresNode node) { }
    public void Visit(InvariantNode node) { }
    public void Visit(UsingDirectiveNode node) { }
    public void Visit(ArrayCreationNode node) { }
    public void Visit(ArrayAccessNode node) { }
    public void Visit(ArrayLengthNode node) { }
    public void Visit(ForeachStatementNode node) { }
    // Phase 6 Extended: Collections
    public void Visit(ListCreationNode node) { }
    public void Visit(DictionaryCreationNode node) { }
    public void Visit(KeyValuePairNode node) { }
    public void Visit(SetCreationNode node) { }
    public void Visit(CollectionPushNode node) { }
    public void Visit(DictionaryPutNode node) { }
    public void Visit(CollectionRemoveNode node) { }
    public void Visit(CollectionSetIndexNode node) { }
    public void Visit(CollectionClearNode node) { }
    public void Visit(CollectionInsertNode node) { }
    public void Visit(CollectionContainsNode node) { }
    public void Visit(DictionaryForeachNode node) { }
    public void Visit(CollectionCountNode node) { }
    public void Visit(TypeParameterNode node) { }
    public void Visit(TypeConstraintNode node) { }
    public void Visit(GenericTypeNode node) { }
    public void Visit(ClassFieldNode node) { }
    public void Visit(NewExpressionNode node) { }
    public void Visit(CallExpressionNode node) { }
    public void Visit(ThisExpressionNode node) { }
    public void Visit(BaseExpressionNode node) { }
    public void Visit(PropertyAccessorNode node) { }
    public void Visit(ConstructorInitializerNode node) { }
    public void Visit(AssignmentStatementNode node) { }
    public void Visit(CompoundAssignmentStatementNode node) { }
    public void Visit(UsingStatementNode node) { }
    public void Visit(TryStatementNode node) { }
    public void Visit(CatchClauseNode node) { }
    public void Visit(ThrowStatementNode node) { }
    public void Visit(RethrowStatementNode node) { }
    public void Visit(LambdaParameterNode node) { }
    public void Visit(LambdaExpressionNode node) { }
    public void Visit(DelegateDefinitionNode node) { }
    public void Visit(EventDefinitionNode node) { }
    public void Visit(EventSubscribeNode node) { }
    public void Visit(EventUnsubscribeNode node) { }
    public void Visit(AwaitExpressionNode node) { }
    public void Visit(InterpolatedStringNode node) { }
    public void Visit(InterpolatedStringTextNode node) { }
    public void Visit(InterpolatedStringExpressionNode node) { }
    public void Visit(NullCoalesceNode node) { }
    public void Visit(NullConditionalNode node) { }
    public void Visit(RangeExpressionNode node) { }
    public void Visit(IndexFromEndNode node) { }
    public void Visit(WithExpressionNode node) { }
    public void Visit(WithPropertyAssignmentNode node) { }
    public void Visit(PositionalPatternNode node) { }
    public void Visit(PropertyPatternNode node) { }
    public void Visit(PropertyMatchNode node) { }
    public void Visit(RelationalPatternNode node) { }
    public void Visit(ListPatternNode node) { }
    public void Visit(VarPatternNode node) { }
    public void Visit(ConstantPatternNode node) { }
    public void Visit(ExampleNode node) { }
    public void Visit(IssueNode node) { }
    public void Visit(DependencyNode node) { }
    public void Visit(UsesNode node) { }
    public void Visit(UsedByNode node) { }
    public void Visit(AssumeNode node) { }
    public void Visit(ComplexityNode node) { }
    public void Visit(SinceNode node) { }
    public void Visit(DeprecatedNode node) { }
    public void Visit(BreakingChangeNode node) { }
    public void Visit(DecisionNode node) { }
    public void Visit(RejectedOptionNode node) { }
    public void Visit(ContextNode node) { }
    public void Visit(FileRefNode node) { }
    public void Visit(PropertyTestNode node) { }
    public void Visit(LockNode node) { }
    public void Visit(AuthorNode node) { }
    public void Visit(TaskRefNode node) { }
    public void Visit(CalorAttributeNode node) { }
    public void Visit(QuantifierVariableNode node) { }
    public void Visit(ForallExpressionNode node) { }
    public void Visit(ExistsExpressionNode node) { }
    public void Visit(ImplicationExpressionNode node) { }
    public void Visit(StringOperationNode node) { }
}
