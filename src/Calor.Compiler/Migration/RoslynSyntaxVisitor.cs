using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Migration;

/// <summary>
/// Roslyn syntax visitor that builds Calor AST nodes from C# syntax.
/// </summary>
public sealed class RoslynSyntaxVisitor : CSharpSyntaxWalker
{
    private readonly ConversionContext _context;
    private readonly SemanticModel? _semanticModel;
    private readonly List<UsingDirectiveNode> _usings = new();
    private readonly List<InterfaceDefinitionNode> _interfaces = new();
    private readonly List<ClassDefinitionNode> _classes = new();
    private readonly List<EnumDefinitionNode> _enums = new();
    private readonly List<DelegateDefinitionNode> _delegates = new();
    private readonly List<FunctionNode> _functions = new();
    private readonly List<StatementNode> _topLevelStatements = new();
    private readonly List<CSharpInteropBlockNode> _moduleInteropBlocks = new();
    private readonly List<TypePreprocessorBlockNode> _typePreprocessorBlocks = new();
    private bool _insideTypePreprocessorConversion;
    private HashSet<string> _reassignedVariables = new();
    // Tracks active-branch conditional using groups across multiple VisitUsingDirective calls
    private string? _activeConditionalUsingCondition;
    private List<UsingDirectiveNode>? _activeConditionalUsings;

    /// <summary>
    /// Accumulates hoisted statements from expression-level chain decomposition.
    /// When ConvertInvocationExpression encounters a chained call that can't be handled by
    /// native operations, it hoists the inner call to a temp bind and adds it here.
    /// ConvertBlock and VisitGlobalStatement flush these before the containing statement.
    /// </summary>
    private readonly List<StatementNode> _pendingStatements = new();

    /// <summary>
    /// Gets the top-level statements collected during conversion (C# 9+ feature).
    /// </summary>
    public IReadOnlyList<StatementNode> TopLevelStatements => _topLevelStatements;

    public RoslynSyntaxVisitor(ConversionContext context, SemanticModel? semanticModel = null) : base(SyntaxWalkerDepth.Node)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _semanticModel = semanticModel;
    }

    /// <summary>
    /// Converts a C# compilation unit to an Calor ModuleNode.
    /// </summary>
    public ModuleNode Convert(CompilationUnitSyntax root, string moduleName)
    {
        _usings.Clear();
        _interfaces.Clear();
        _classes.Clear();
        _enums.Clear();
        _delegates.Clear();
        _functions.Clear();
        _topLevelStatements.Clear();
        _moduleInteropBlocks.Clear();
        _typePreprocessorBlocks.Clear();
        _activeConditionalUsingCondition = null;
        _activeConditionalUsings = null;
        _reassignedVariables = CollectReassignedVariables(root);

        // Scan for module-level #if blocks wrapping type declarations
        // (disabled by preprocessor — Roslyn excludes them from the tree)
        ScanModuleLevelPreprocessorBlocks(root);

        // Visit all nodes
        Visit(root);

        // Flush any remaining conditional usings
        FlushActiveConditionalUsings();

        // Use a fixed module ID — each file produces exactly one module, and generating
        // the ID after Visit(root) would give inconsistent IDs like m044 due to the
        // shared counter being incremented by inner node conversions.
        var moduleId = "m001";

        // If there are top-level statements (C# 9+ feature), wrap them in a synthetic main function
        var functions = _functions.ToList();
        if (_topLevelStatements.Count > 0)
        {
            var mainFunction = new FunctionNode(
                span: GetTextSpan(root),
                id: "main",
                name: "Main",
                visibility: Visibility.Public,
                parameters: new List<ParameterNode>(),
                output: null, // void return type
                effects: InferEffectsFromBody(_topLevelStatements),
                body: _topLevelStatements,
                attributes: new AttributeCollection());

            functions.Add(mainFunction);
            _context.Stats.MethodsConverted++;
        }

        return new ModuleNode(
            GetTextSpan(root),
            moduleId,
            moduleName,
            _usings,
            _interfaces,
            _classes,
            _enums,
            Array.Empty<EnumExtensionNode>(),
            _delegates,
            functions,
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null,
            _moduleInteropBlocks.Count > 0 ? _moduleInteropBlocks.ToList() : null,
            refinementTypes: null,
            indexedTypes: null,
            typePreprocessorBlocks: _typePreprocessorBlocks.Count > 0 ? _typePreprocessorBlocks.ToList() : null);
    }

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name != null)
        {
            var namespaceName = node.Name.ToString();
            var isStatic = node.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
            var alias = node.Alias?.Name.ToString();

            var usingNode = new UsingDirectiveNode(
                GetTextSpan(node),
                namespaceName,
                alias,
                isStatic);

            // Check for #if start in leading trivia
            var ifCondition = GetLeadingIfCondition(node);
            if (ifCondition != null)
            {
                // Start a new conditional using group
                FlushActiveConditionalUsings();
                _activeConditionalUsingCondition = ifCondition;
                _activeConditionalUsings = new List<UsingDirectiveNode> { usingNode };
            }
            else if (_activeConditionalUsings != null)
            {
                // Inside an active conditional group — add to it
                _activeConditionalUsings.Add(usingNode);
            }
            else
            {
                _usings.Add(usingNode);
            }

            // Check for #endif in trailing trivia — closes the group
            if (_activeConditionalUsings != null && HasTrailingEndif(node))
            {
                FlushActiveConditionalUsings();
            }
        }

        base.VisitUsingDirective(node);
    }

    /// <summary>
    /// Flushes any accumulated active-branch conditional usings into a TypePreprocessorBlockNode.
    /// </summary>
    private void FlushActiveConditionalUsings()
    {
        if (_activeConditionalUsings != null && _activeConditionalUsings.Count > 0 && _activeConditionalUsingCondition != null)
        {
            _context.RecordFeatureUsage("conditional-using");
            _context.RecordFeatureUsage("preprocessor-directive");
            var ppNode = new TypePreprocessorBlockNode(
                TextSpan.Empty,
                _activeConditionalUsingCondition,
                Array.Empty<ClassDefinitionNode>(),
                Array.Empty<InterfaceDefinitionNode>(),
                Array.Empty<EnumDefinitionNode>(),
                Array.Empty<DelegateDefinitionNode>(),
                usings: _activeConditionalUsings);
            _typePreprocessorBlocks.Add(ppNode);
        }
        _activeConditionalUsingCondition = null;
        _activeConditionalUsings = null;
    }

    /// <summary>
    /// Gets the #if condition from a node's leading trivia, if present.
    /// </summary>
    private static string? GetLeadingIfCondition(SyntaxNode node)
    {
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia) &&
                trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
            {
                return ifDir.Condition.ToString();
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if a using directive has #endif in its trailing trivia.
    /// </summary>
    private static bool HasTrailingEndif(UsingDirectiveSyntax node)
    {
        // Check both the node's trailing trivia and the semicolon token's trailing trivia
        if (node.GetTrailingTrivia().Any(t => t.IsKind(SyntaxKind.EndIfDirectiveTrivia)))
            return true;
        if (node.SemicolonToken.TrailingTrivia.Any(t => t.IsKind(SyntaxKind.EndIfDirectiveTrivia)))
            return true;
        return false;
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        _context.EnterNamespace(node.Name.ToString());
        base.VisitNamespaceDeclaration(node);
        _context.ExitNamespace();
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        _context.EnterNamespace(node.Name.ToString());
        base.VisitFileScopedNamespaceDeclaration(node);
        _context.ExitNamespace();
    }

    public override void VisitGlobalStatement(GlobalStatementSyntax node)
    {
        _context.RecordFeatureUsage("top-level-statement");
        _pendingStatements.Clear();

        // Handle chained method calls in local declarations (e.g., var x = a.Where(...).First())
        // Skip chains handled by native operations (string, StringBuilder, regex, char)
        if (node.Statement is LocalDeclarationStatementSyntax chainDecl
            && chainDecl.Declaration.Variables.Count == 1
            && chainDecl.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax chainInit
            && IsChainedInvocation(chainInit)
            && !WouldChainUseNativeOps(chainInit))
        {
            foreach (var stmt in DecomposeChainedLocalDeclaration(chainDecl))
            {
                _topLevelStatements.Add(stmt);
            }
            FlushPendingStatements(_topLevelStatements);
            _context.IncrementConverted();
            return;
        }

        // Handle chained method calls in expression statements
        // Skip chains handled by native operations (string, StringBuilder, regex, char)
        if (node.Statement is ExpressionStatementSyntax exprStmt
            && exprStmt.Expression is InvocationExpressionSyntax chainExpr
            && IsChainedInvocation(chainExpr)
            && !WouldChainUseNativeOps(chainExpr))
        {
            foreach (var stmt in DecomposeChainedExpressionStatement(exprStmt))
            {
                _topLevelStatements.Add(stmt);
            }
            FlushPendingStatements(_topLevelStatements);
            return;
        }

        var statement = ConvertStatement(node.Statement);
        if (statement != null)
        {
            // Flush any hoisted temp binds from expression-level chains
            FlushPendingStatements(_topLevelStatements);
            _topLevelStatements.Add(statement);
            _context.IncrementConverted();
        }
        // Don't call base - we've fully handled this node
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        // Check for #if wrapping entire type declaration
        if (!_insideTypePreprocessorConversion)
        {
            var ppCondition = GetTypePreprocessorCondition(node);
            if (ppCondition != null)
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                WrapTypeInPreprocessorBlock(node, ppCondition);
                return;
            }
        }

        _context.RecordFeatureUsage("interface");
        _context.EnterType(node.Identifier.Text);
        try
        {
            var interfaceNode = ConvertInterface(node);
            _interfaces.Add(interfaceNode);
            _context.Stats.InterfacesConverted++;
            _context.IncrementConverted();
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _moduleInteropBlocks.Add(CreateInteropBlock(node, "interface", InteropMemberKind.Class));
        }
        finally
        {
            _context.ExitType();
        }
    }

    private InterfaceDefinitionNode ConvertInterface(InterfaceDeclarationSyntax node)
    {
        var id = _context.GenerateId("i");
        var name = node.Identifier.Text;
        var baseInterfaces = node.BaseList?.Types
            .Select(t => t.Type.ToString())
            .ToList() ?? new List<string>();
        var csharpAttrs = ConvertAttributes(node.AttributeLists);
        var typeParameters = ConvertTypeParameters(node.TypeParameterList, node.ConstraintClauses);

        var methods = new List<MethodSignatureNode>();
        var properties = new List<PropertyNode>();
        var indexers = new List<IndexerNode>();
        foreach (var member in node.Members)
        {
            if (member is MethodDeclarationSyntax methodSyntax)
            {
                methods.Add(ConvertMethodSignature(methodSyntax));
            }
            else if (member is PropertyDeclarationSyntax propertySyntax)
            {
                properties.Add(ConvertProperty(propertySyntax));
            }
            else if (member is IndexerDeclarationSyntax indexerSyntax)
            {
                indexers.Add(ConvertIndexer(indexerSyntax));
            }
            else
            {
                _context.AddWarning(
                    $"Dropped unsupported interface member of kind '{member.Kind()}' in interface '{name}'",
                    feature: "unsupported-member",
                    line: member.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                _context.Stats.MembersDropped++;
            }
        }

        return new InterfaceDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            baseInterfaces,
            typeParameters,
            methods,
            properties,
            new AttributeCollection(),
            csharpAttrs,
            indexers: indexers.Count > 0 ? indexers : null);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (!_insideTypePreprocessorConversion)
        {
            var ppCondition = GetTypePreprocessorCondition(node);
            if (ppCondition != null)
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                WrapTypeInPreprocessorBlock(node, ppCondition);
                return;
            }
        }

        _context.RecordFeatureUsage("class");
        _context.EnterType(node.Identifier.Text);
        try
        {
            var classNode = ConvertClass(node);
            _classes.Add(classNode);
            _context.Stats.ClassesConverted++;
            _context.IncrementConverted();
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _moduleInteropBlocks.Add(CreateInteropBlock(node, "class", InteropMemberKind.Class));
        }
        finally
        {
            _context.ExitType();
        }
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        if (!_insideTypePreprocessorConversion)
        {
            var ppCondition = GetTypePreprocessorCondition(node);
            if (ppCondition != null)
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                WrapTypeInPreprocessorBlock(node, ppCondition);
                return;
            }
        }

        _context.RecordFeatureUsage("record");
        _context.EnterType(node.Identifier.Text);
        try
        {
            var classNode = ConvertRecord(node);
            _classes.Add(classNode);
            _context.Stats.ClassesConverted++;
            _context.IncrementConverted();
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _moduleInteropBlocks.Add(CreateInteropBlock(node, "record", InteropMemberKind.Class));
        }
        finally
        {
            _context.ExitType();
        }
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        if (!_insideTypePreprocessorConversion)
        {
            var ppCondition = GetTypePreprocessorCondition(node);
            if (ppCondition != null)
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                WrapTypeInPreprocessorBlock(node, ppCondition);
                return;
            }
        }

        _context.RecordFeatureUsage("struct");
        _context.EnterType(node.Identifier.Text);
        try
        {
            var classNode = ConvertStruct(node);
            _classes.Add(classNode);
            _context.Stats.ClassesConverted++;
            _context.IncrementConverted();
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _moduleInteropBlocks.Add(CreateInteropBlock(node, "struct", InteropMemberKind.Class));
        }
        finally
        {
            _context.ExitType();
        }
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        if (!_insideTypePreprocessorConversion)
        {
            var ppCondition = GetTypePreprocessorCondition(node);
            if (ppCondition != null)
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                WrapTypeInPreprocessorBlock(node, ppCondition);
                return;
            }
        }

        _context.RecordFeatureUsage("enum");
        try
        {
            var id = _context.GenerateId("e");
            var name = node.Identifier.Text;
            var csharpAttrs = ConvertAttributes(node.AttributeLists);

            // Get the underlying type if specified (e.g., : byte, : int)
            string? underlyingType = null;
            if (node.BaseList?.Types.Count > 0)
            {
                var baseTypeName = node.BaseList.Types.First().Type.ToString();
                underlyingType = TypeMapper.CSharpToCalor(baseTypeName);
            }

            // Convert enum members
            var members = new List<EnumMemberNode>();
            foreach (var member in node.Members)
            {
                var memberName = member.Identifier.Text;
                var memberValue = GetEnumMemberValueText(member.EqualsValue);
                var memberAttrs = ConvertAttributes(member.AttributeLists);
                members.Add(new EnumMemberNode(GetTextSpan(member), memberName, memberValue, memberAttrs));
            }

            var visibility = GetVisibility(node.Modifiers, Visibility.Internal);

            var enumNode = new EnumDefinitionNode(
                GetTextSpan(node),
                id,
                name,
                underlyingType,
                members,
                new AttributeCollection(),
                csharpAttrs,
                visibility);

            _enums.Add(enumNode);
            _context.Stats.EnumsConverted++;
            _context.IncrementConverted();
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _moduleInteropBlocks.Add(CreateInteropBlock(node, "enum", InteropMemberKind.Other));
        }
    }

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("delegate");
        try
        {
            var id = _context.GenerateId("del");
            var name = node.Identifier.Text;

            // Convert parameters
            var parameters = ConvertParameters(node.ParameterList);

            // Convert return type
            var returnType = TypeMapper.CSharpToCalor(node.ReturnType.ToString());
            var output = returnType != "void" ? new OutputNode(GetTextSpan(node.ReturnType), returnType) : null;

            var delegateNode = new DelegateDefinitionNode(
                GetTextSpan(node),
                id,
                name,
                parameters,
                output,
                effects: null,
                new AttributeCollection());

            _delegates.Add(delegateNode);
            _context.IncrementConverted();
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _moduleInteropBlocks.Add(CreateInteropBlock(node, "delegate", InteropMemberKind.Other));
        }
    }

    private ClassDefinitionNode ConvertClass(ClassDeclarationSyntax node)
    {
        var id = _context.GenerateId("c");
        var name = node.Identifier.Text;
        var isAbstract = node.Modifiers.Any(SyntaxKind.AbstractKeyword);
        var isSealed = node.Modifiers.Any(SyntaxKind.SealedKeyword);
        var isPartial = node.Modifiers.Any(SyntaxKind.PartialKeyword);
        var isStatic = node.Modifiers.Any(SyntaxKind.StaticKeyword);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);
        var defaultVis = node.Parent is TypeDeclarationSyntax ? Visibility.Private : Visibility.Internal;
        var visibility = GetVisibility(node.Modifiers, defaultVis);

        if (isPartial) _context.RecordFeatureUsage("partial-class");
        if (isStatic) _context.RecordFeatureUsage("static-class");

        string? baseClass = null;
        var interfaces = new List<string>();

        if (node.BaseList != null)
        {
            foreach (var baseType in node.BaseList.Types)
            {
                var typeName = baseType.Type.ToString();
                // First non-interface base type is the base class
                if (baseClass == null && (!typeName.StartsWith("I") || !char.IsUpper(typeName.ElementAtOrDefault(1))))
                {
                    // Simple heuristic: interfaces typically start with 'I'
                    if (typeName.StartsWith("I") && typeName.Length > 1 && char.IsUpper(typeName[1]))
                    {
                        interfaces.Add(typeName);
                    }
                    else
                    {
                        baseClass = typeName;
                    }
                }
                else
                {
                    interfaces.Add(typeName);
                }
            }
        }

        var typeParameters = ConvertTypeParameters(node.TypeParameterList, node.ConstraintClauses);
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var indexers = new List<IndexerNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var events = new List<EventDefinitionNode>();
        var operatorOverloads = new List<OperatorOverloadNode>();

        // Convert C# 12 primary constructor parameters to readonly fields
        // and synthesize a constructor that assigns them.
        if (node.ParameterList != null)
        {
            _context.RecordFeatureUsage("primary-constructor");
            var explicitNames = CollectExplicitMemberNames(node.Members);
            var synthesizedFieldNames = new List<string>();
            foreach (var param in node.ParameterList.Parameters)
            {
                var fieldName = param.Identifier.Text;
                if (explicitNames.Contains(fieldName))
                    continue;
                synthesizedFieldNames.Add(fieldName);
                var fieldTypeName = TypeMapper.CSharpToCalor(param.Type?.ToString() ?? "any");
                var fieldCsharpAttrs = ConvertAttributes(param.AttributeLists);

                fields.Add(new ClassFieldNode(
                    GetTextSpan(param),
                    fieldName,
                    fieldTypeName,
                    Visibility.Private,
                    MethodModifiers.Readonly,
                    defaultValue: null,
                    new AttributeCollection(),
                    fieldCsharpAttrs));
            }

            // Synthesize constructor that assigns primary ctor params to fields
            var ctorId = _context.GenerateId("ctor");
            var ctorParams = ConvertParameters(node.ParameterList);
            var ctorBody = new List<StatementNode>();
            foreach (var fieldName in synthesizedFieldNames)
            {
                var span = GetTextSpan(node.ParameterList);
                ctorBody.Add(new AssignmentStatementNode(
                    span,
                    new FieldAccessNode(span, new ThisExpressionNode(span), fieldName),
                    new ReferenceNode(span, fieldName)));
            }

            ConstructorInitializerNode? initializer = null;
            if (node.BaseList != null)
            {
                foreach (var baseType in node.BaseList.Types)
                {
                    if (baseType is PrimaryConstructorBaseTypeSyntax primaryBase)
                    {
                        var baseArgs = primaryBase.ArgumentList.Arguments
                            .Select(a => ConvertExpression(a.Expression))
                            .ToList();
                        initializer = new ConstructorInitializerNode(
                            GetTextSpan(primaryBase),
                            isBaseCall: true,
                            baseArgs);
                        break;
                    }
                }
            }

            constructors.Add(new ConstructorNode(
                GetTextSpan(node.ParameterList),
                ctorId,
                visibility,
                ctorParams,
                preconditions: Array.Empty<RequiresNode>(),
                initializer,
                ctorBody,
                new AttributeCollection(),
                Array.Empty<CalorAttributeNode>(),
                isStatic: false));
        }

        var interopBlocks = new List<CSharpInteropBlockNode>();
        var preprocessorBlocks = new List<MemberPreprocessorBlockNode>();
        var nestedClasses = new List<ClassDefinitionNode>();
        var nestedInterfaces = new List<InterfaceDefinitionNode>();
        var nestedEnums = new List<EnumDefinitionNode>();
        var nestedDelegates = new List<DelegateDefinitionNode>();

        // Extract member-level preprocessor regions
        var ppRegions = ExtractMemberPreprocessorRegions(node);
        // Index regions by start — use first-wins for safety if duplicates exist
        var ppRegionsByStart = new Dictionary<int, PreprocessorRegion>();
        var ppCoveredIndices = new HashSet<int>();
        foreach (var region in ppRegions)
        {
            if (region.ActiveStart < region.ActiveEnd)
                ppRegionsByStart.TryAdd(region.ActiveStart, region);
            for (int idx = region.ActiveStart; idx < region.ActiveEnd; idx++)
                ppCoveredIndices.Add(idx);
        }

        // Handle PP regions with no active members (all disabled) before the loop
        foreach (var emptyRegion in ppRegions.Where(r => r.ActiveStart == r.ActiveEnd))
        {
            _context.RecordFeatureUsage("preprocessor-directive");
            preprocessorBlocks.Add(BuildMemberPreprocessorNode(TextSpan.Empty, emptyRegion.Branches,
                new List<ClassFieldNode>(), new List<PropertyNode>(), new List<ConstructorNode>(),
                new List<MethodNode>(), new List<EventDefinitionNode>(), new List<OperatorOverloadNode>()));
        }

        for (int memberIndex = 0; memberIndex < node.Members.Count; memberIndex++)
        {
            // Handle preprocessor regions that cover parsed members
            if (ppRegionsByStart.TryGetValue(memberIndex, out var ppRegion))
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                var span = GetTextSpan(node.Members[ppRegion.ActiveStart]);

                // Convert active members
                var ppFields = new List<ClassFieldNode>();
                var ppProperties = new List<PropertyNode>();
                var ppConstructors = new List<ConstructorNode>();
                var ppMethods = new List<MethodNode>();
                var ppEvents = new List<EventDefinitionNode>();
                var ppOperatorOverloads = new List<OperatorOverloadNode>();

                for (int bi = ppRegion.ActiveStart; bi < ppRegion.ActiveEnd; bi++)
                {
                    try
                    {
                        ConvertClassMember(node.Members[bi], ppFields, ppProperties, ppConstructors, ppMethods, ppEvents, ppOperatorOverloads);
                    }
                    catch (Exception) when (_context.ShouldPreserveCSharp)
                    {
                        // Skip unconvertible members in PP blocks
                    }
                }

                preprocessorBlocks.Add(BuildMemberPreprocessorNode(span, ppRegion.Branches,
                    ppFields, ppProperties, ppConstructors, ppMethods, ppEvents, ppOperatorOverloads));

                memberIndex = ppRegion.ActiveEnd - 1; // loop will increment
                continue;
            }

            if (ppCoveredIndices.Contains(memberIndex))
                continue;

            var member = node.Members[memberIndex];

            // Handle nested type declarations
            if (member is ClassDeclarationSyntax nestedClass)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    _context.EnterType(nestedClass.Identifier.Text);
                    nestedClasses.Add(ConvertClass(nestedClass));
                    _context.ExitType();
                }
                catch (Exception) when (_context.ShouldPreserveCSharp)
                {
                    _context.ExitType();
                    interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other));
                }
                continue;
            }
            if (member is StructDeclarationSyntax nestedStruct)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    _context.EnterType(nestedStruct.Identifier.Text);
                    nestedClasses.Add(ConvertStruct(nestedStruct));
                    _context.ExitType();
                }
                catch (Exception) when (_context.ShouldPreserveCSharp)
                {
                    _context.ExitType();
                    interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other));
                }
                continue;
            }
            if (member is RecordDeclarationSyntax nestedRecord)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    _context.EnterType(nestedRecord.Identifier.Text);
                    nestedClasses.Add(ConvertRecord(nestedRecord));
                    _context.ExitType();
                }
                catch (Exception) when (_context.ShouldPreserveCSharp)
                {
                    _context.ExitType();
                    interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other));
                }
                continue;
            }
            if (member is InterfaceDeclarationSyntax nestedIface)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    nestedInterfaces.Add(ConvertInterface(nestedIface));
                }
                catch (Exception) when (_context.ShouldPreserveCSharp)
                {
                    interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other));
                }
                continue;
            }
            if (member is EnumDeclarationSyntax nestedEnum)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    var nestedId = _context.GenerateId("e");
                    var nestedName = nestedEnum.Identifier.Text;
                    string? nestedUnderlying = null;
                    if (nestedEnum.BaseList?.Types.Count > 0)
                        nestedUnderlying = TypeMapper.CSharpToCalor(nestedEnum.BaseList.Types.First().Type.ToString());
                    var nestedMembers = nestedEnum.Members
                        .Select(m => new EnumMemberNode(GetTextSpan(m), m.Identifier.Text, GetEnumMemberValueText(m.EqualsValue), ConvertAttributes(m.AttributeLists)))
                        .ToList();
                    var nestedVis = GetVisibility(nestedEnum.Modifiers, Visibility.Private);
                    var nestedAttrs = ConvertAttributes(nestedEnum.AttributeLists);
                    nestedEnums.Add(new EnumDefinitionNode(GetTextSpan(nestedEnum), nestedId, nestedName,
                        nestedUnderlying, nestedMembers, new AttributeCollection(), nestedAttrs, nestedVis));
                }
                catch (Exception) when (_context.ShouldPreserveCSharp)
                {
                    interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other));
                }
                continue;
            }
            if (member is DelegateDeclarationSyntax nestedDel)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    var savedDelegates = _delegates.ToList();
                    _delegates.Clear();
                    VisitDelegateDeclaration(nestedDel);
                    nestedDelegates.AddRange(_delegates);
                    _delegates.Clear();
                    _delegates.AddRange(savedDelegates);
                }
                catch (Exception) when (_context.ShouldPreserveCSharp)
                {
                    interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other));
                }
                continue;
            }

            try
            {
                ConvertClassMember(member, fields, properties, constructors, methods, events, operatorOverloads, indexers);
            }
            catch (Exception) when (_context.ShouldPreserveCSharp)
            {
                var kind = member switch
                {
                    MethodDeclarationSyntax => InteropMemberKind.Method,
                    PropertyDeclarationSyntax => InteropMemberKind.Property,
                    IndexerDeclarationSyntax => InteropMemberKind.Property,
                    FieldDeclarationSyntax => InteropMemberKind.Field,
                    ConstructorDeclarationSyntax => InteropMemberKind.Constructor,
                    EventFieldDeclarationSyntax => InteropMemberKind.Event,
                    EventDeclarationSyntax => InteropMemberKind.Event,
                    _ => InteropMemberKind.Other
                };
                interopBlocks.Add(CreateInteropBlock(member, null, kind));
            }
        }

        return new ClassDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            isAbstract,
            isSealed,
            isPartial,
            isStatic,
            baseClass,
            interfaces,
            typeParameters,
            fields,
            properties,
            constructors,
            methods,
            events,
            operatorOverloads,
            new AttributeCollection(),
            csharpAttrs,
            visibility: visibility,
            interopBlocks: interopBlocks.Count > 0 ? interopBlocks : null,
            preprocessorBlocks: preprocessorBlocks.Count > 0 ? preprocessorBlocks : null,
            nestedClasses: nestedClasses.Count > 0 ? nestedClasses : null,
            nestedInterfaces: nestedInterfaces.Count > 0 ? nestedInterfaces : null,
            nestedEnums: nestedEnums.Count > 0 ? nestedEnums : null,
            indexers: indexers.Count > 0 ? indexers : null,
            nestedDelegates: nestedDelegates.Count > 0 ? nestedDelegates : null);
    }

    private static HashSet<string> CollectExplicitMemberNames(SyntaxList<MemberDeclarationSyntax> members)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                        names.Add(variable.Identifier.Text);
                    break;
                case PropertyDeclarationSyntax prop:
                    names.Add(prop.Identifier.Text);
                    break;
            }
        }
        return names;
    }

    private void ConvertClassMember(
        MemberDeclarationSyntax member,
        List<ClassFieldNode> fields,
        List<PropertyNode> properties,
        List<ConstructorNode> constructors,
        List<MethodNode> methods,
        List<EventDefinitionNode> events,
        List<OperatorOverloadNode> operatorOverloads,
        List<IndexerNode>? indexers = null)
    {
        switch (member)
        {
            case FieldDeclarationSyntax fieldSyntax:
                fields.AddRange(ConvertFields(fieldSyntax));
                break;
            case PropertyDeclarationSyntax propertySyntax:
                properties.Add(ConvertProperty(propertySyntax));
                break;
            case IndexerDeclarationSyntax indexerSyntax:
                indexers?.Add(ConvertIndexer(indexerSyntax));
                break;
            case ConstructorDeclarationSyntax ctorSyntax:
                constructors.Add(ConvertConstructor(ctorSyntax));
                break;
            case MethodDeclarationSyntax methodSyntax:
                methods.Add(ConvertMethod(methodSyntax));
                break;
            case OperatorDeclarationSyntax opSyntax:
                operatorOverloads.Add(ConvertOperatorOverload(opSyntax));
                break;
            case ConversionOperatorDeclarationSyntax convSyntax:
                operatorOverloads.Add(ConvertConversionOperatorOverload(convSyntax));
                break;
            case EventFieldDeclarationSyntax eventSyntax:
                events.AddRange(ConvertEventFields(eventSyntax));
                break;
            case EventDeclarationSyntax eventDeclSyntax:
                events.Add(ConvertEventDeclaration(eventDeclSyntax));
                break;
            case ClassDeclarationSyntax nestedClass:
                _classes.Add(ConvertClass(nestedClass));
                _context.Stats.ClassesConverted++;
                break;
            case StructDeclarationSyntax nestedStruct:
                VisitStructDeclaration(nestedStruct);
                break;
            case InterfaceDeclarationSyntax nestedInterface:
                VisitInterfaceDeclaration(nestedInterface);
                break;
            case EnumDeclarationSyntax nestedEnum:
                VisitEnumDeclaration(nestedEnum);
                break;
            default:
                if (_context.ShouldPreserveCSharp)
                {
                    throw new NotSupportedException($"Unsupported member type: {member.Kind()}");
                }
                var typeName = (member.Parent as TypeDeclarationSyntax)?.Identifier.Text ?? "unknown";
                _context.AddWarning(
                    $"Dropped unsupported class member of kind '{member.Kind()}' in type '{typeName}'",
                    feature: "unsupported-member",
                    line: member.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                _context.Stats.MembersDropped++;
                break;
        }
    }

    private ClassDefinitionNode ConvertRecord(RecordDeclarationSyntax node)
    {
        var id = _context.GenerateId("r");
        var name = node.Identifier.Text;
        var defaultVis = node.Parent is TypeDeclarationSyntax ? Visibility.Private : Visibility.Internal;
        var visibility = GetVisibility(node.Modifiers, defaultVis);

        var typeParameters = ConvertTypeParameters(node.TypeParameterList, node.ConstraintClauses);
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();

        // Convert primary constructor parameters to properties
        if (node.ParameterList != null)
        {
            var explicitNames = CollectExplicitMemberNames(node.Members);
            foreach (var param in node.ParameterList.Parameters)
            {
                var propName = param.Identifier.Text;
                if (explicitNames.Contains(propName))
                    continue;
                var propId = _context.GenerateId("p");
                var typeName = TypeMapper.CSharpToCalor(param.Type?.ToString() ?? "any");

                properties.Add(new PropertyNode(
                    GetTextSpan(param),
                    propId,
                    propName,
                    typeName,
                    Visibility.Public,
                    getter: null,
                    setter: null,
                    initer: null,
                    defaultValue: param.Default != null ? ConvertExpression(param.Default.Value) : null,
                    new AttributeCollection()));
            }
        }

        var operatorOverloads = new List<OperatorOverloadNode>();
        var indexers = new List<IndexerNode>();

        foreach (var member in node.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax fieldSyntax:
                    fields.AddRange(ConvertFields(fieldSyntax));
                    break;
                case PropertyDeclarationSyntax propertySyntax:
                    properties.Add(ConvertProperty(propertySyntax));
                    break;
                case IndexerDeclarationSyntax indexerSyntax:
                    indexers.Add(ConvertIndexer(indexerSyntax));
                    break;
                case ConstructorDeclarationSyntax ctorSyntax:
                    constructors.Add(ConvertConstructor(ctorSyntax));
                    break;
                case MethodDeclarationSyntax methodSyntax:
                    methods.Add(ConvertMethod(methodSyntax));
                    break;
                case OperatorDeclarationSyntax opSyntax:
                    operatorOverloads.Add(ConvertOperatorOverload(opSyntax));
                    break;
                case ConversionOperatorDeclarationSyntax convSyntax:
                    operatorOverloads.Add(ConvertConversionOperatorOverload(convSyntax));
                    break;
            }
        }

        return new ClassDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            isAbstract: false,
            isSealed: true,
            isPartial: false,
            isStatic: false,
            baseClass: null,
            implementedInterfaces: new List<string>(),
            typeParameters,
            fields,
            properties,
            constructors,
            methods,
            Array.Empty<EventDefinitionNode>(),
            operatorOverloads,
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>(),
            visibility: visibility,
            indexers: indexers.Count > 0 ? indexers : null);
    }

    private ClassDefinitionNode ConvertStruct(StructDeclarationSyntax node)
    {
        var id = _context.GenerateId("s");
        var name = node.Identifier.Text;
        var isReadOnly = node.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
        var isPartial = node.Modifiers.Any(SyntaxKind.PartialKeyword);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);
        var defaultVis = node.Parent is TypeDeclarationSyntax ? Visibility.Private : Visibility.Internal;
        var visibility = GetVisibility(node.Modifiers, defaultVis);

        if (isReadOnly)
            _context.RecordFeatureUsage("readonly-struct");
        if (isPartial)
            _context.RecordFeatureUsage("partial-class");

        var typeParameters = ConvertTypeParameters(node.TypeParameterList, node.ConstraintClauses);
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var indexers = new List<IndexerNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var operatorOverloads = new List<OperatorOverloadNode>();
        var events = new List<EventDefinitionNode>();

        // Convert C# 12 primary constructor parameters to readonly fields.
        // Note: structs cannot have base class calls, so no base call synthesis is needed here
        // (unlike ConvertClass which synthesizes a ConstructorNode for PrimaryConstructorBaseTypeSyntax).
        if (node.ParameterList != null)
        {
            _context.RecordFeatureUsage("primary-constructor");
            var explicitNames = CollectExplicitMemberNames(node.Members);
            var synthesizedFieldNames = new List<string>();
            foreach (var param in node.ParameterList.Parameters)
            {
                var fieldName = param.Identifier.Text;
                if (explicitNames.Contains(fieldName))
                    continue;
                synthesizedFieldNames.Add(fieldName);
                var fieldTypeName = TypeMapper.CSharpToCalor(param.Type?.ToString() ?? "any");
                var fieldCsharpAttrs = ConvertAttributes(param.AttributeLists);

                fields.Add(new ClassFieldNode(
                    GetTextSpan(param),
                    fieldName,
                    fieldTypeName,
                    Visibility.Private,
                    MethodModifiers.Readonly,
                    defaultValue: null,
                    new AttributeCollection(),
                    fieldCsharpAttrs));
            }

            // Synthesize constructor that assigns primary ctor params to fields
            var ctorId = _context.GenerateId("ctor");
            var ctorParams = ConvertParameters(node.ParameterList);
            var ctorBody = new List<StatementNode>();
            foreach (var fieldName in synthesizedFieldNames)
            {
                var span = GetTextSpan(node.ParameterList);
                ctorBody.Add(new AssignmentStatementNode(
                    span,
                    new FieldAccessNode(span, new ThisExpressionNode(span), fieldName),
                    new ReferenceNode(span, fieldName)));
            }

            constructors.Add(new ConstructorNode(
                GetTextSpan(node.ParameterList),
                ctorId,
                visibility,
                ctorParams,
                preconditions: Array.Empty<RequiresNode>(),
                initializer: null,
                ctorBody,
                new AttributeCollection(),
                Array.Empty<CalorAttributeNode>(),
                isStatic: false));
        }

        var interopBlocks = new List<CSharpInteropBlockNode>();
        var preprocessorBlocks = new List<MemberPreprocessorBlockNode>();
        var nestedClasses = new List<ClassDefinitionNode>();
        var nestedInterfaces = new List<InterfaceDefinitionNode>();
        var nestedEnums = new List<EnumDefinitionNode>();
        var nestedDelegates = new List<DelegateDefinitionNode>();

        // Extract member-level preprocessor regions
        var ppRegions = ExtractMemberPreprocessorRegions(node);
        var ppRegionsByStart = new Dictionary<int, PreprocessorRegion>();
        var ppCoveredIndices = new HashSet<int>();
        foreach (var region in ppRegions)
        {
            if (region.ActiveStart < region.ActiveEnd)
                ppRegionsByStart.TryAdd(region.ActiveStart, region);
            for (int idx = region.ActiveStart; idx < region.ActiveEnd; idx++)
                ppCoveredIndices.Add(idx);
        }

        // Handle PP regions with no active members (all disabled)
        foreach (var emptyRegion in ppRegions.Where(r => r.ActiveStart == r.ActiveEnd))
        {
            _context.RecordFeatureUsage("preprocessor-directive");
            preprocessorBlocks.Add(BuildMemberPreprocessorNode(TextSpan.Empty, emptyRegion.Branches,
                new List<ClassFieldNode>(), new List<PropertyNode>(), new List<ConstructorNode>(),
                new List<MethodNode>(), new List<EventDefinitionNode>(), new List<OperatorOverloadNode>()));
        }

        for (int memberIndex = 0; memberIndex < node.Members.Count; memberIndex++)
        {
            if (ppRegionsByStart.TryGetValue(memberIndex, out var ppRegion))
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                var span = GetTextSpan(node.Members[ppRegion.ActiveStart]);

                var ppFields = new List<ClassFieldNode>();
                var ppProperties = new List<PropertyNode>();
                var ppConstructors = new List<ConstructorNode>();
                var ppMethods = new List<MethodNode>();
                var ppEvents = new List<EventDefinitionNode>();
                var ppOperatorOverloads = new List<OperatorOverloadNode>();

                for (int bi = ppRegion.ActiveStart; bi < ppRegion.ActiveEnd; bi++)
                {
                    try
                    {
                        ConvertClassMember(node.Members[bi], ppFields, ppProperties, ppConstructors, ppMethods, ppEvents, ppOperatorOverloads);
                    }
                    catch (Exception) when (_context.ShouldPreserveCSharp)
                    {
                        // Skip unconvertible members in PP blocks
                    }
                }

                preprocessorBlocks.Add(BuildMemberPreprocessorNode(span, ppRegion.Branches,
                    ppFields, ppProperties, ppConstructors, ppMethods, ppEvents, ppOperatorOverloads));

                memberIndex = ppRegion.ActiveEnd - 1; // loop will increment
                continue;
            }

            if (ppCoveredIndices.Contains(memberIndex))
                continue;

            var member = node.Members[memberIndex];

            // Handle nested type declarations in struct
            if (member is ClassDeclarationSyntax nc)
            {
                try { _context.RecordFeatureUsage("nested-type"); _context.EnterType(nc.Identifier.Text); nestedClasses.Add(ConvertClass(nc)); _context.ExitType(); }
                catch (Exception) when (_context.ShouldPreserveCSharp) { _context.ExitType(); interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other)); }
                continue;
            }
            if (member is StructDeclarationSyntax ns)
            {
                try { _context.RecordFeatureUsage("nested-type"); _context.EnterType(ns.Identifier.Text); nestedClasses.Add(ConvertStruct(ns)); _context.ExitType(); }
                catch (Exception) when (_context.ShouldPreserveCSharp) { _context.ExitType(); interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other)); }
                continue;
            }
            if (member is InterfaceDeclarationSyntax ni)
            {
                try { _context.RecordFeatureUsage("nested-type"); nestedInterfaces.Add(ConvertInterface(ni)); }
                catch (Exception) when (_context.ShouldPreserveCSharp) { interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other)); }
                continue;
            }
            if (member is EnumDeclarationSyntax ne)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    var nestedId = _context.GenerateId("e");
                    var nestedName = ne.Identifier.Text;
                    string? nestedUnderlying = null;
                    if (ne.BaseList?.Types.Count > 0)
                        nestedUnderlying = TypeMapper.CSharpToCalor(ne.BaseList.Types.First().Type.ToString());
                    var nestedMembers = ne.Members
                        .Select(m => new EnumMemberNode(GetTextSpan(m), m.Identifier.Text, GetEnumMemberValueText(m.EqualsValue), ConvertAttributes(m.AttributeLists)))
                        .ToList();
                    var nestedVis = GetVisibility(ne.Modifiers, Visibility.Private);
                    var nestedAttrs = ConvertAttributes(ne.AttributeLists);
                    nestedEnums.Add(new EnumDefinitionNode(GetTextSpan(ne), nestedId, nestedName,
                        nestedUnderlying, nestedMembers, new AttributeCollection(), nestedAttrs, nestedVis));
                }
                catch (Exception) when (_context.ShouldPreserveCSharp) { interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other)); }
                continue;
            }
            if (member is DelegateDeclarationSyntax nd)
            {
                try
                {
                    _context.RecordFeatureUsage("nested-type");
                    var savedDelegates = _delegates.ToList();
                    _delegates.Clear();
                    VisitDelegateDeclaration(nd);
                    nestedDelegates.AddRange(_delegates);
                    _delegates.Clear();
                    _delegates.AddRange(savedDelegates);
                }
                catch (Exception) when (_context.ShouldPreserveCSharp) { interopBlocks.Add(CreateInteropBlock(member, null, InteropMemberKind.Other)); }
                continue;
            }

            try
            {
                ConvertClassMember(member, fields, properties, constructors, methods, events, operatorOverloads, indexers);
            }
            catch (Exception) when (_context.ShouldPreserveCSharp)
            {
                var kind = member switch
                {
                    MethodDeclarationSyntax => InteropMemberKind.Method,
                    PropertyDeclarationSyntax => InteropMemberKind.Property,
                    IndexerDeclarationSyntax => InteropMemberKind.Property,
                    FieldDeclarationSyntax => InteropMemberKind.Field,
                    ConstructorDeclarationSyntax => InteropMemberKind.Constructor,
                    EventFieldDeclarationSyntax => InteropMemberKind.Event,
                    EventDeclarationSyntax => InteropMemberKind.Event,
                    _ => InteropMemberKind.Other
                };
                interopBlocks.Add(CreateInteropBlock(member, null, kind));
            }
        }

        var interfaces = node.BaseList?.Types
            .Select(t => t.Type.ToString())
            .ToList() ?? new List<string>();

        return new ClassDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            isAbstract: false,
            isSealed: false,
            isPartial: isPartial,
            isStatic: false,
            baseClass: null,
            interfaces,
            typeParameters,
            fields,
            properties,
            constructors,
            methods,
            events,
            operatorOverloads,
            new AttributeCollection(),
            csharpAttrs,
            isStruct: true,
            isReadOnly: isReadOnly,
            visibility: visibility,
            interopBlocks: interopBlocks.Count > 0 ? interopBlocks : null,
            preprocessorBlocks: preprocessorBlocks.Count > 0 ? preprocessorBlocks : null,
            nestedClasses: nestedClasses.Count > 0 ? nestedClasses : null,
            nestedInterfaces: nestedInterfaces.Count > 0 ? nestedInterfaces : null,
            nestedEnums: nestedEnums.Count > 0 ? nestedEnums : null,
            indexers: indexers.Count > 0 ? indexers : null,
            nestedDelegates: nestedDelegates.Count > 0 ? nestedDelegates : null);
    }

    private MethodSignatureNode ConvertMethodSignature(MethodDeclarationSyntax node)
    {
        var id = _context.GenerateId("m");
        var name = node.Identifier.Text;
        var typeParameters = ConvertTypeParameters(node.TypeParameterList, node.ConstraintClauses);
        var parameters = ConvertParameters(node.ParameterList);
        var returnType = TypeMapper.CSharpToCalor(node.ReturnType.ToString());
        var output = returnType != "void" ? new OutputNode(GetTextSpan(node.ReturnType), returnType) : null;
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        return new MethodSignatureNode(
            GetTextSpan(node),
            id,
            name,
            typeParameters,
            parameters,
            output,
            effects: null,
            new AttributeCollection(),
            csharpAttrs);
    }

    private MethodNode ConvertMethod(MethodDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("method");
        _context.EnterMethod(node.Identifier.Text);
        _reassignedVariables = CollectReassignedVariables(node);

        var id = _context.GenerateId("m");
        // Preserve explicit interface qualifier (e.g., IDisposable.Dispose)
        var name = node.ExplicitInterfaceSpecifier != null
            ? $"{node.ExplicitInterfaceSpecifier.Name}.{node.Identifier.Text}"
            : node.Identifier.Text;
        var visibility = GetVisibility(node.Modifiers);
        var modifiers = GetMethodModifiers(node.Modifiers);
        var typeParameters = ConvertTypeParameters(node.TypeParameterList, node.ConstraintClauses);
        var parameters = ConvertParameters(node.ParameterList);

        // Check for async modifier
        var isAsync = node.Modifiers.Any(SyntaxKind.AsyncKeyword);
        var returnTypeStr = node.ReturnType.ToString();

        // For async methods, unwrap Task<T> -> T
        if (isAsync)
        {
            returnTypeStr = UnwrapTaskType(returnTypeStr);
            _context.RecordFeatureUsage("async-method");
        }

        // Track extern methods (P/Invoke)
        if (modifiers.HasFlag(MethodModifiers.Extern))
        {
            _context.RecordFeatureUsage("extern-method");
        }

        var returnType = TypeMapper.CSharpToCalor(returnTypeStr);
        var output = returnType != "void" ? new OutputNode(GetTextSpan(node.ReturnType), returnType) : null;
        var body = modifiers.HasFlag(MethodModifiers.Extern)
            ? Array.Empty<StatementNode>()  // extern methods have no body
            : ConvertMethodBody(node.Body, node.ExpressionBody);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        _context.Stats.MethodsConverted++;
        _context.IncrementConverted();
        _context.ExitMethod();

        return new MethodNode(
            GetTextSpan(node),
            id,
            name,
            visibility,
            modifiers,
            typeParameters,
            parameters,
            output,
            effects: InferEffectsFromBody(body),
            preconditions: Array.Empty<RequiresNode>(),
            postconditions: Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection(),
            csharpAttrs,
            isAsync);
    }

    private static readonly Dictionary<string, string> OperatorTokenToCilName = new()
    {
        ["+"] = "op_Addition",
        ["-"] = "op_Subtraction",
        ["*"] = "op_Multiply",
        ["/"] = "op_Division",
        ["%"] = "op_Modulus",
        ["=="] = "op_Equality",
        ["!="] = "op_Inequality",
        ["<"] = "op_LessThan",
        [">"] = "op_GreaterThan",
        ["<="] = "op_LessThanOrEqual",
        [">="] = "op_GreaterThanOrEqual",
        ["!"] = "op_LogicalNot",
        ["&"] = "op_BitwiseAnd",
        ["|"] = "op_BitwiseOr",
        ["^"] = "op_ExclusiveOr",
    };

    private MethodNode ConvertOperator(OperatorDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("operator-overload");
        var opToken = node.OperatorToken.Text;
        var paramCount = node.ParameterList.Parameters.Count;

        if (opToken == "==" || opToken == "!=")
            _context.RecordFeatureUsage("equals-operator");

        // Disambiguate unary vs binary for +/-
        string cilName;
        if (opToken == "-" && paramCount == 1)
            cilName = "op_UnaryNegation";
        else if (opToken == "+" && paramCount == 1)
            cilName = "op_UnaryPlus";
        else
            cilName = OperatorTokenToCilName.TryGetValue(opToken, out var name)
                ? name
                : $"op_Unknown_{opToken}";

        var id = _context.GenerateId("m");
        var parameters = ConvertParameters(node.ParameterList);
        var returnType = TypeMapper.CSharpToCalor(node.ReturnType.ToString());
        var output = returnType != "void" ? new OutputNode(GetTextSpan(node.ReturnType), returnType) : null;
        var body = ConvertMethodBody(node.Body, node.ExpressionBody);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        _context.Stats.MethodsConverted++;
        _context.IncrementConverted();

        return new MethodNode(
            GetTextSpan(node),
            id,
            cilName,
            Visibility.Public,
            MethodModifiers.Static,
            Array.Empty<TypeParameterNode>(),
            parameters,
            output,
            effects: InferEffectsFromBody(body),
            preconditions: Array.Empty<RequiresNode>(),
            postconditions: Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection(),
            csharpAttrs);
    }

    private MethodNode ConvertConversionOperator(ConversionOperatorDeclarationSyntax node)
    {
        var isImplicit = node.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword);
        _context.RecordFeatureUsage(isImplicit ? "implicit-conversion" : "explicit-conversion");

        var cilName = isImplicit ? "op_Implicit" : "op_Explicit";

        var id = _context.GenerateId("m");
        var parameters = ConvertParameters(node.ParameterList);
        var returnType = TypeMapper.CSharpToCalor(node.Type.ToString());
        var output = new OutputNode(GetTextSpan(node.Type), returnType);
        var body = ConvertMethodBody(node.Body, node.ExpressionBody);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        _context.Stats.MethodsConverted++;
        _context.IncrementConverted();

        return new MethodNode(
            GetTextSpan(node),
            id,
            cilName,
            Visibility.Public,
            MethodModifiers.Static,
            Array.Empty<TypeParameterNode>(),
            parameters,
            output,
            effects: InferEffectsFromBody(body),
            preconditions: Array.Empty<RequiresNode>(),
            postconditions: Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection(),
            csharpAttrs);
    }

    private ConstructorNode ConvertConstructor(ConstructorDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("constructor");
        _reassignedVariables = CollectReassignedVariables(node);

        var id = _context.GenerateId("ctor");
        var isStatic = node.Modifiers.Any(SyntaxKind.StaticKeyword);
        var visibility = GetVisibility(node.Modifiers);
        var parameters = ConvertParameters(node.ParameterList);
        var body = ConvertMethodBody(node.Body, node.ExpressionBody);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        ConstructorInitializerNode? initializer = null;
        if (node.Initializer != null)
        {
            var isBase = node.Initializer.ThisOrBaseKeyword.IsKind(SyntaxKind.BaseKeyword);
            var args = node.Initializer.ArgumentList.Arguments
                .Select(a => ConvertExpression(a.Expression))
                .ToList();

            // Hoist §NEW, §LAM, §ARR from constructor initializer args — parser can't handle them nested
            HoistComplexArguments(args);

            initializer = new ConstructorInitializerNode(
                GetTextSpan(node.Initializer),
                isBase,
                args);
        }

        _context.IncrementConverted();

        return new ConstructorNode(
            GetTextSpan(node),
            id,
            visibility,
            parameters,
            preconditions: Array.Empty<RequiresNode>(),
            initializer,
            body,
            new AttributeCollection(),
            csharpAttrs,
            isStatic);
    }

    private OperatorOverloadNode ConvertOperatorOverload(OperatorDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("operator-overload");

        var id = _context.GenerateId("op");
        var operatorToken = node.OperatorToken.Text;
        var visibility = GetVisibility(node.Modifiers);
        var parameters = ConvertParameters(node.ParameterList);
        var returnType = TypeMapper.CSharpToCalor(node.ReturnType.ToString());
        var output = new OutputNode(GetTextSpan(node.ReturnType), returnType);
        var body = ConvertMethodBody(node.Body, node.ExpressionBody);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        var kind = OperatorOverloadNode.ResolveOperatorKind(operatorToken, parameters.Count);

        _context.IncrementConverted();

        return new OperatorOverloadNode(
            GetTextSpan(node),
            id,
            operatorToken,
            kind,
            visibility,
            parameters,
            output,
            preconditions: Array.Empty<RequiresNode>(),
            postconditions: Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection(),
            csharpAttrs);
    }

    private OperatorOverloadNode ConvertConversionOperatorOverload(ConversionOperatorDeclarationSyntax node)
    {
        var isImplicit = node.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword);
        var operatorToken = isImplicit ? "implicit" : "explicit";

        _context.RecordFeatureUsage(isImplicit ? "implicit-conversion" : "explicit-conversion");

        var id = _context.GenerateId("op");
        var visibility = GetVisibility(node.Modifiers);
        var parameters = ConvertParameters(node.ParameterList);
        var returnType = TypeMapper.CSharpToCalor(node.Type.ToString());
        var output = new OutputNode(GetTextSpan(node.Type), returnType);
        var body = ConvertMethodBody(node.Body, node.ExpressionBody);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        var kind = isImplicit ? OperatorOverloadKind.Implicit : OperatorOverloadKind.Explicit;

        _context.IncrementConverted();

        return new OperatorOverloadNode(
            GetTextSpan(node),
            id,
            operatorToken,
            kind,
            visibility,
            parameters,
            output,
            preconditions: Array.Empty<RequiresNode>(),
            postconditions: Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection(),
            csharpAttrs);
    }

    private IReadOnlyList<ClassFieldNode> ConvertFields(FieldDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("field");

        var fields = new List<ClassFieldNode>();
        var visibility = GetVisibility(node.Modifiers);
        var typeName = TypeMapper.CSharpToCalor(node.Declaration.Type.ToString());
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        var modifiers = MethodModifiers.None;
        if (node.Modifiers.Any(SyntaxKind.ConstKeyword))
            modifiers |= MethodModifiers.Const;
        if (node.Modifiers.Any(SyntaxKind.StaticKeyword))
            modifiers |= MethodModifiers.Static;
        if (node.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
            modifiers |= MethodModifiers.Readonly;
        if (node.Modifiers.Any(SyntaxKind.RequiredKeyword))
            modifiers |= MethodModifiers.Required;
        if (node.Modifiers.Any(SyntaxKind.VolatileKeyword))
            modifiers |= MethodModifiers.Volatile;

        foreach (var variable in node.Declaration.Variables)
        {
            var defaultValue = variable.Initializer != null
                ? ConvertExpression(variable.Initializer.Value)
                : null;

            fields.Add(new ClassFieldNode(
                GetTextSpan(variable),
                variable.Identifier.ValueText,
                typeName,
                visibility,
                modifiers,
                defaultValue,
                new AttributeCollection(),
                csharpAttrs));

            _context.Stats.FieldsConverted++;
            _context.IncrementConverted();
        }

        return fields;
    }

    private IReadOnlyList<EventDefinitionNode> ConvertEventFields(EventFieldDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("event-definition");

        var events = new List<EventDefinitionNode>();
        var visibility = GetVisibility(node.Modifiers);
        var delegateType = TypeMapper.CSharpToCalor(node.Declaration.Type.ToString());
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        foreach (var variable in node.Declaration.Variables)
        {
            var id = _context.GenerateId("evt");

            events.Add(new EventDefinitionNode(
                GetTextSpan(variable),
                id,
                variable.Identifier.ValueText,
                visibility,
                delegateType,
                new AttributeCollection()));

            _context.IncrementConverted();
        }

        return events;
    }

    private EventDefinitionNode ConvertEventDeclaration(EventDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("event-definition");

        var id = _context.GenerateId("evt");
        var name = node.Identifier.ValueText;
        var visibility = GetVisibility(node.Modifiers);
        var delegateType = TypeMapper.CSharpToCalor(node.Type.ToString());

        IReadOnlyList<StatementNode>? addBody = null;
        IReadOnlyList<StatementNode>? removeBody = null;

        if (node.AccessorList != null)
        {
            foreach (var accessor in node.AccessorList.Accessors)
            {
                if (accessor.Keyword.IsKind(SyntaxKind.AddKeyword))
                {
                    addBody = ConvertAccessorBody(accessor);
                }
                else if (accessor.Keyword.IsKind(SyntaxKind.RemoveKeyword))
                {
                    removeBody = ConvertAccessorBody(accessor);
                }
            }
        }

        _context.IncrementConverted();

        return new EventDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            visibility,
            delegateType,
            new AttributeCollection(),
            addBody,
            removeBody);
    }

    private PropertyNode ConvertProperty(PropertyDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("property");

        var name = node.Identifier.Text;
        var typeName = TypeMapper.CSharpToCalor(node.Type.ToString());
        var defaultVis = node.Parent is InterfaceDeclarationSyntax ? Visibility.Public : Visibility.Private;
        var visibility = GetVisibility(node.Modifiers, defaultVis);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        PropertyAccessorNode? initer = null;

        var isAutoProperty = node.AccessorList != null &&
            node.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null);

        if (node.AccessorList != null)
        {
            foreach (var accessor in node.AccessorList.Accessors)
            {
                var accessorVisibility = accessor.Modifiers.Any()
                    ? GetVisibility(accessor.Modifiers)
                    : visibility;
                var accessorAttrs = ConvertAttributes(accessor.AttributeLists);

                if (accessor.Keyword.IsKind(SyntaxKind.GetKeyword))
                {
                    getter = new PropertyAccessorNode(
                        GetTextSpan(accessor),
                        PropertyAccessorNode.AccessorKind.Get,
                        accessorVisibility,
                        preconditions: Array.Empty<RequiresNode>(),
                        body: ConvertAccessorBody(accessor),
                        new AttributeCollection(),
                        accessorAttrs);
                }
                else if (accessor.Keyword.IsKind(SyntaxKind.SetKeyword))
                {
                    setter = new PropertyAccessorNode(
                        GetTextSpan(accessor),
                        PropertyAccessorNode.AccessorKind.Set,
                        accessorVisibility,
                        preconditions: Array.Empty<RequiresNode>(),
                        body: ConvertAccessorBody(accessor),
                        new AttributeCollection(),
                        accessorAttrs);
                }
                else if (accessor.Keyword.IsKind(SyntaxKind.InitKeyword))
                {
                    initer = new PropertyAccessorNode(
                        GetTextSpan(accessor),
                        PropertyAccessorNode.AccessorKind.Init,
                        accessorVisibility,
                        preconditions: Array.Empty<RequiresNode>(),
                        body: ConvertAccessorBody(accessor),
                        new AttributeCollection(),
                        accessorAttrs);
                }
            }
        }
        else if (node.ExpressionBody != null)
        {
            // Expression-bodied property (getter only)
            getter = new PropertyAccessorNode(
                GetTextSpan(node),
                PropertyAccessorNode.AccessorKind.Get,
                visibility,
                preconditions: Array.Empty<RequiresNode>(),
                body: new List<StatementNode>
                {
                    new ReturnStatementNode(
                        GetTextSpan(node.ExpressionBody),
                        ConvertExpression(node.ExpressionBody.Expression))
                },
                new AttributeCollection());
        }

        var defaultValue = node.Initializer != null
            ? ConvertExpression(node.Initializer.Value)
            : null;

        _context.Stats.PropertiesConverted++;
        _context.IncrementConverted();

        var propId = _context.GenerateId("p");
        var modifiers = GetMethodModifiers(node.Modifiers);
        return new PropertyNode(
            GetTextSpan(node),
            propId,
            name,
            typeName,
            visibility,
            modifiers,
            getter,
            setter,
            initer,
            defaultValue,
            new AttributeCollection(),
            csharpAttrs);
    }

    private IndexerNode ConvertIndexer(IndexerDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("indexer");

        var typeName = TypeMapper.CSharpToCalor(node.Type.ToString());
        var defaultVis = node.Parent is InterfaceDeclarationSyntax ? Visibility.Public : Visibility.Private;
        var visibility = GetVisibility(node.Modifiers, defaultVis);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        // Convert parameters (indexers use BracketedParameterListSyntax)
        var parameters = node.ParameterList.Parameters
            .Select(p =>
            {
                var modifier = ParameterModifier.None;
                if (p.Modifiers.Any(SyntaxKind.RefKeyword)) modifier |= ParameterModifier.Ref;
                if (p.Modifiers.Any(SyntaxKind.InKeyword)) modifier |= ParameterModifier.In;
                if (p.Modifiers.Any(SyntaxKind.ParamsKeyword)) modifier |= ParameterModifier.Params;
                ExpressionNode? defaultValue = null;
                if (p.Default != null)
                {
                    defaultValue = ConvertExpression(p.Default.Value);
                }
                var paramAttrs = ConvertAttributes(p.AttributeLists);
                return new ParameterNode(
                    GetTextSpan(p),
                    p.Identifier.ValueText,
                    TypeMapper.CSharpToCalor(p.Type?.ToString() ?? "any"),
                    modifier,
                    new AttributeCollection(),
                    paramAttrs,
                    defaultValue);
            })
            .ToList() as IReadOnlyList<ParameterNode>;

        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        PropertyAccessorNode? initer = null;

        if (node.AccessorList != null)
        {
            foreach (var accessor in node.AccessorList.Accessors)
            {
                var accessorVisibility = accessor.Modifiers.Any()
                    ? GetVisibility(accessor.Modifiers)
                    : visibility;
                var accessorAttrs = ConvertAttributes(accessor.AttributeLists);

                if (accessor.Keyword.IsKind(SyntaxKind.GetKeyword))
                {
                    getter = new PropertyAccessorNode(
                        GetTextSpan(accessor),
                        PropertyAccessorNode.AccessorKind.Get,
                        accessorVisibility,
                        preconditions: Array.Empty<RequiresNode>(),
                        body: ConvertAccessorBody(accessor),
                        new AttributeCollection(),
                        accessorAttrs);
                }
                else if (accessor.Keyword.IsKind(SyntaxKind.SetKeyword))
                {
                    setter = new PropertyAccessorNode(
                        GetTextSpan(accessor),
                        PropertyAccessorNode.AccessorKind.Set,
                        accessorVisibility,
                        preconditions: Array.Empty<RequiresNode>(),
                        body: ConvertAccessorBody(accessor),
                        new AttributeCollection(),
                        accessorAttrs);
                }
                else if (accessor.Keyword.IsKind(SyntaxKind.InitKeyword))
                {
                    initer = new PropertyAccessorNode(
                        GetTextSpan(accessor),
                        PropertyAccessorNode.AccessorKind.Init,
                        accessorVisibility,
                        preconditions: Array.Empty<RequiresNode>(),
                        body: ConvertAccessorBody(accessor),
                        new AttributeCollection(),
                        accessorAttrs);
                }
            }
        }
        else if (node.ExpressionBody != null)
        {
            // Expression-bodied indexer (getter only)
            getter = new PropertyAccessorNode(
                GetTextSpan(node),
                PropertyAccessorNode.AccessorKind.Get,
                visibility,
                preconditions: Array.Empty<RequiresNode>(),
                body: new List<StatementNode>
                {
                    new ReturnStatementNode(
                        GetTextSpan(node.ExpressionBody),
                        ConvertExpression(node.ExpressionBody.Expression))
                },
                new AttributeCollection());
        }

        _context.Stats.PropertiesConverted++;
        _context.IncrementConverted();

        var indexerId = _context.GenerateId("ix");
        var modifiers = GetMethodModifiers(node.Modifiers);
        return new IndexerNode(
            GetTextSpan(node),
            indexerId,
            typeName,
            visibility,
            modifiers,
            parameters,
            getter,
            setter,
            initer,
            new AttributeCollection(),
            csharpAttrs);
    }

    private IReadOnlyList<StatementNode> ConvertAccessorBody(AccessorDeclarationSyntax accessor)
    {
        if (accessor.Body != null)
        {
            return ConvertBlock(accessor.Body);
        }
        else if (accessor.ExpressionBody != null)
        {
            var span = GetTextSpan(accessor.ExpressionBody);
            // Setters/init accessors: expression is a statement (assignment, method call, etc.)
            if (accessor.Keyword.IsKind(SyntaxKind.SetKeyword) || accessor.Keyword.IsKind(SyntaxKind.InitKeyword))
            {
                // Handle tuple deconstruction: set => (a, b) = (x, y)
                if (accessor.ExpressionBody.Expression is AssignmentExpressionSyntax tupleAssign
                    && tupleAssign.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && tupleAssign.Left is TupleExpressionSyntax leftTuple
                    && tupleAssign.Right is TupleExpressionSyntax rightTuple
                    && leftTuple.Arguments.Count == rightTuple.Arguments.Count)
                {
                    var stmts = new List<StatementNode>();
                    for (int i = 0; i < leftTuple.Arguments.Count; i++)
                    {
                        stmts.Add(new AssignmentStatementNode(span,
                            ConvertExpression(leftTuple.Arguments[i].Expression),
                            ConvertExpression(rightTuple.Arguments[i].Expression)));
                    }
                    return stmts;
                }
                var stmt = ConvertExpressionToStatement(accessor.ExpressionBody.Expression, span);
                return stmt != null ? new List<StatementNode> { stmt } : Array.Empty<StatementNode>();
            }
            // Getters: expression is a return value
            return new List<StatementNode>
            {
                new ReturnStatementNode(span, ConvertExpression(accessor.ExpressionBody.Expression))
            };
        }
        return Array.Empty<StatementNode>();
    }

    private IReadOnlyList<StatementNode> ConvertMethodBody(BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody)
    {
        if (body != null)
        {
            return ConvertBlock(body);
        }
        else if (expressionBody != null)
        {
            // Check if expression body is an assignment (e.g., void Method() => _field = value)
            if (expressionBody.Expression is AssignmentExpressionSyntax exprAssign)
            {
                var target = ConvertExpression(exprAssign.Left);
                var value = ConvertExpression(exprAssign.Right);
                return new List<StatementNode> { new AssignmentStatementNode(GetTextSpan(expressionBody), target, value) };
            }
            return new List<StatementNode>
            {
                new ReturnStatementNode(
                    GetTextSpan(expressionBody),
                    ConvertExpression(expressionBody.Expression))
            };
        }
        return Array.Empty<StatementNode>();
    }

    /// <summary>
    /// Converts a C# local function to a module-level §F function.
    /// Local functions are hoisted out of the containing method body since
    /// Calor doesn't have nested function declarations.
    /// </summary>
    private FunctionNode ConvertLocalFunction(LocalFunctionStatementSyntax node)
    {
        var id = _context.GenerateId("f");
        var name = node.Identifier.ValueText;
        var parameters = ConvertParameters(node.ParameterList);

        var isAsync = node.Modifiers.Any(SyntaxKind.AsyncKeyword);
        var returnTypeStr = node.ReturnType.ToString();

        if (isAsync)
        {
            returnTypeStr = UnwrapTaskType(returnTypeStr);
        }

        var returnType = TypeMapper.CSharpToCalor(returnTypeStr);
        var output = returnType != "void" ? new OutputNode(GetTextSpan(node.ReturnType), returnType) : null;
        var body = ConvertMethodBody(node.Body, node.ExpressionBody);

        _context.Stats.MethodsConverted++;
        _context.IncrementConverted();

        return new FunctionNode(
            GetTextSpan(node),
            id,
            name,
            Visibility.Private,
            Array.Empty<TypeParameterNode>(),
            parameters,
            output,
            effects: InferEffectsFromBody(body),
            Array.Empty<RequiresNode>(),
            Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection(),
            Array.Empty<ExampleNode>(),
            Array.Empty<IssueNode>(),
            null, null,
            Array.Empty<AssumeNode>(),
            null, null, null,
            Array.Empty<BreakingChangeNode>(),
            Array.Empty<PropertyTestNode>(),
            null, null, null,
            isAsync);
    }

    /// <summary>
    /// A single branch within a preprocessor region (#if, #elif, or #else).
    /// </summary>
    private sealed class PreprocessorBranch
    {
        /// <summary>The condition for #if/#elif, or null for #else.</summary>
        public string? Condition;
        /// <summary>Disabled text if this branch is inactive, null if active.</summary>
        public string? DisabledText;
        /// <summary>True if this branch contains the parsed (active) statements.</summary>
        public bool IsActive;
    }

    /// <summary>
    /// Represents a preprocessor region extracted from Roslyn trivia.
    /// Contains an ordered list of branches (#if, then #elif(s), then optional #else).
    /// </summary>
    private sealed class PreprocessorRegion
    {
        /// <summary>Start index in block.Statements for active (parsed) statements.</summary>
        public int ActiveStart;
        /// <summary>End index (exclusive) in block.Statements for active statements.</summary>
        public int ActiveEnd;
        /// <summary>All branches in order: [0] is #if, [1..n-1] are #elif, last may be #else (Condition==null).</summary>
        public List<PreprocessorBranch> Branches = new();

        /// <summary>The #if condition (shortcut for Branches[0].Condition).</summary>
        public string Condition => Branches.Count > 0 ? Branches[0].Condition ?? "" : "";
    }

    /// <summary>
    /// Extracts preprocessor directive regions (#if/#elif/#else/#endif) from block trivia.
    ///
    /// Roslyn trivia layout depends on which branch is active:
    /// - First branch active (#if defined): #if on statement trivia, inactive branches on close brace
    /// - Middle branch active (#elif defined): #if+disabled+#elif on statement trivia, rest on close brace
    /// - Last branch active (#else, no symbols): #if+disabled+[#elif+disabled]*+#else on statement trivia
    /// - Nothing active (no #else, no symbols): everything on close brace
    /// </summary>
    private static List<PreprocessorRegion> ExtractPreprocessorRegions(BlockSyntax block)
    {
        var regions = new List<PreprocessorRegion>();
        var statements = block.Statements;

        // Scan all statement leading trivia for top-level #if directives
        for (int i = 0; i < statements.Count; i++)
        {
            var leadingTrivia = statements[i].GetLeadingTrivia().ToList();

            // Check if this statement has a #if directive in its trivia
            bool hasIf = leadingTrivia.Any(t => t.IsKind(SyntaxKind.IfDirectiveTrivia));
            if (!hasIf) continue;

            // Collect branches from statement leading trivia
            // These are the inactive branches BEFORE the active branch
            var stmtBranches = new List<PreprocessorBranch>();
            string? ifCondition = null;
            string? currentDisabled = null;
            string? currentCondition = null;
            int triviaDepth = 0;

            foreach (var trivia in leadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                {
                    if (triviaDepth == 0 && ifCondition == null)
                    {
                        if (trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
                            ifCondition = ifDir.Condition.ToString();
                        currentCondition = ifCondition;
                        currentDisabled = null;
                    }
                    else
                    {
                        triviaDepth++;
                    }
                }
                else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                {
                    if (triviaDepth > 0) { triviaDepth--; continue; }
                }
                else if (triviaDepth > 0)
                {
                    continue;
                }
                else if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia) && ifCondition != null)
                {
                    stmtBranches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                    if (trivia.GetStructure() is ElifDirectiveTriviaSyntax elifDir)
                        currentCondition = elifDir.Condition.ToString();
                    else
                        currentCondition = "ELIF";
                    currentDisabled = null;
                }
                else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia) && ifCondition != null)
                {
                    stmtBranches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                    currentCondition = null; // #else
                    currentDisabled = null;
                }
                else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia) && ifCondition != null && triviaDepth == 0)
                {
                    currentDisabled = trivia.ToString();
                }
            }

            if (ifCondition == null) continue;

            // Find where this region ends: scan forward for matching #endif
            int endIdx = statements.Count;
            int scanDepth = 1;
            for (int j = i; j < statements.Count; j++)
            {
                var jTrivia = j == i ? leadingTrivia : statements[j].GetLeadingTrivia().ToList();
                bool skipFirst = (j == i);
                foreach (var trivia in jTrivia)
                {
                    if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                    {
                        if (skipFirst) { skipFirst = false; continue; }
                        scanDepth++;
                    }
                    else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                    {
                        scanDepth--;
                        if (scanDepth == 0)
                        {
                            endIdx = j;
                            goto foundEnd;
                        }
                    }
                }
            }

            // Collect branches from close brace trivia (inactive branches AFTER active)
            {
                var closeBraceTrivia = block.CloseBraceToken.LeadingTrivia.ToList();
                int braceDepth = 0;
                foreach (var trivia in closeBraceTrivia)
                {
                    if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                    {
                        braceDepth++;
                    }
                    else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                    {
                        if (braceDepth > 0) { braceDepth--; continue; }
                        scanDepth--;
                        if (scanDepth == 0)
                        {
                            endIdx = statements.Count;
                            break;
                        }
                    }
                    else if (braceDepth > 0)
                    {
                        continue;
                    }
                    else if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia))
                    {
                        // Close previous branch (which was active — the parsed statements)
                        stmtBranches.Add(new PreprocessorBranch
                        {
                            Condition = currentCondition,
                            DisabledText = null, // active branch
                            IsActive = true
                        });
                        if (trivia.GetStructure() is ElifDirectiveTriviaSyntax elifDir)
                            currentCondition = elifDir.Condition.ToString();
                        else
                            currentCondition = "ELIF";
                        currentDisabled = null;
                    }
                    else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia))
                    {
                        stmtBranches.Add(new PreprocessorBranch
                        {
                            Condition = currentCondition,
                            DisabledText = null,
                            IsActive = true
                        });
                        currentCondition = null;
                        currentDisabled = null;
                    }
                    else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                    {
                        currentDisabled = trivia.ToString();
                    }
                }
            }
            foundEnd:

            // The last accumulated branch is the active one (the parsed statements)
            // unless we already added it when processing close brace #elif/#else
            bool activeAlreadyAdded = stmtBranches.Any(b => b.IsActive);
            if (!activeAlreadyAdded)
            {
                stmtBranches.Add(new PreprocessorBranch
                {
                    Condition = currentCondition,
                    DisabledText = null,
                    IsActive = true
                });
            }
            else if (currentDisabled != null)
            {
                // There's a trailing disabled branch after the active one
                stmtBranches.Add(new PreprocessorBranch
                {
                    Condition = currentCondition,
                    DisabledText = currentDisabled,
                    IsActive = false
                });
            }

            regions.Add(new PreprocessorRegion
            {
                ActiveStart = i,
                ActiveEnd = endIdx,
                Branches = stmtBranches
            });

            // Advance past the region. Use Math.Max to prevent infinite loops
            // when endIdx == i (zero-width regions from adjacent #if blocks).
            i = Math.Max(i, endIdx - 1);
        }

        // Check close brace for #if with no parsed statements (everything disabled)
        {
            var closeBraceTrivia = block.CloseBraceToken.LeadingTrivia.ToList();
            string? ifCondition = null;
            string? currentCond = null;
            string? currentDis = null;
            var branches = new List<PreprocessorBranch>();
            int braceDepth = 0;

            foreach (var trivia in closeBraceTrivia)
            {
                if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                {
                    if (braceDepth == 0 && ifCondition == null)
                    {
                        if (trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
                            ifCondition = ifDir.Condition.ToString();
                        currentCond = ifCondition;
                        currentDis = null;
                    }
                    braceDepth++;
                }
                else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                {
                    braceDepth--;
                    if (braceDepth == 0 && ifCondition != null)
                    {
                        branches.Add(new PreprocessorBranch
                        {
                            Condition = currentCond,
                            DisabledText = currentDis,
                            IsActive = false
                        });
                        regions.Add(new PreprocessorRegion
                        {
                            ActiveStart = statements.Count,
                            ActiveEnd = statements.Count,
                            Branches = branches
                        });
                        ifCondition = null;
                        branches = new List<PreprocessorBranch>();
                    }
                }
                else if (braceDepth != 1 || ifCondition == null)
                {
                    continue;
                }
                else if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia))
                {
                    branches.Add(new PreprocessorBranch
                    {
                        Condition = currentCond,
                        DisabledText = currentDis,
                        IsActive = false
                    });
                    if (trivia.GetStructure() is ElifDirectiveTriviaSyntax elifDir)
                        currentCond = elifDir.Condition.ToString();
                    else
                        currentCond = "ELIF";
                    currentDis = null;
                }
                else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia))
                {
                    branches.Add(new PreprocessorBranch
                    {
                        Condition = currentCond,
                        DisabledText = currentDis,
                        IsActive = false
                    });
                    currentCond = null;
                    currentDis = null;
                }
                else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                {
                    currentDis = trivia.ToString();
                }
            }
        }

        return regions;
    }

    /// <summary>
    /// Parses disabled text (from an inactive preprocessor branch) as C# statements
    /// and converts them to Calor statement nodes.
    /// </summary>
    private List<StatementNode> ConvertDisabledText(string disabledText)
    {
        // Wrap the disabled text in a method body to parse it as statements
        var wrapper = $"class _PP {{ void _M() {{ {disabledText} }} }}";
        try
        {
            var tree = CSharpSyntaxTree.ParseText(wrapper);
            var root = tree.GetCompilationUnitRoot();
            var method = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();
            if (method?.Body == null) return new List<StatementNode>();

            // Merge reassigned variables from disabled text so mutability is correct
            var disabledReassigned = CollectReassignedVariables(method.Body);
            foreach (var name in disabledReassigned)
                _reassignedVariables.Add(name);

            var results = new List<StatementNode>();
            foreach (var stmt in method.Body.Statements)
            {
                var converted = ConvertStatement(stmt);
                if (converted != null) results.Add(converted);
            }
            return results;
        }
        catch
        {
            // If we can't parse the disabled text, emit it as a raw comment
            return new List<StatementNode>
            {
                _context.PassthroughOnError
                    ? new RawCSharpNode(TextSpan.Empty, disabledText.Trim())
                    : new FallbackCommentNode(TextSpan.Empty, disabledText.Trim(), "preprocessor-disabled", "Disabled preprocessor text could not be parsed")
            };
        }
    }

    /// <summary>
    /// Builds a (potentially nested) PreprocessorDirectiveNode from an ordered list of branches.
    /// For #if/#elif/#else, #elif branches are represented as nested §PP in the else body:
    /// #if A / #elif B / #else → §PP{A} body §PPE §PP{B} body §PPE else §/PP{B} §/PP{A}
    /// </summary>
    private PreprocessorDirectiveNode BuildPreprocessorNode(
        TextSpan span,
        List<PreprocessorBranch> branches,
        List<StatementNode> activeStatements)
    {
        if (branches.Count == 0)
        {
            // Shouldn't happen, but safety
            return new PreprocessorDirectiveNode(span, "UNKNOWN", activeStatements, null);
        }

        // Convert each branch's body
        var convertedBodies = new List<(string? condition, List<StatementNode> body)>();
        foreach (var branch in branches)
        {
            if (branch.IsActive)
            {
                convertedBodies.Add((branch.Condition, activeStatements));
            }
            else if (branch.DisabledText != null)
            {
                convertedBodies.Add((branch.Condition, ConvertDisabledText(branch.DisabledText)));
            }
            else
            {
                convertedBodies.Add((branch.Condition, new List<StatementNode>()));
            }
        }

        // Build nested structure from right to left:
        // Start with the last branch and work backwards, nesting each #elif as §PP in the else
        List<StatementNode>? currentElse = null;

        for (int idx = convertedBodies.Count - 1; idx >= 1; idx--)
        {
            var (condition, body) = convertedBodies[idx];
            if (condition == null)
            {
                // #else branch — becomes the innermost else
                currentElse = body;
            }
            else
            {
                // #elif branch — wrap in a new PreprocessorDirectiveNode
                var elifNode = new PreprocessorDirectiveNode(span, condition, body, currentElse);
                currentElse = new List<StatementNode> { elifNode };
            }
        }

        // Build the outermost #if node
        var (ifCondition, ifBody) = convertedBodies[0];
        return new PreprocessorDirectiveNode(span, ifCondition ?? "IF", ifBody, currentElse);
    }

    /// <summary>
    /// Extracts preprocessor regions from member-level trivia on a type declaration.
    /// Adapts the same logic as ExtractPreprocessorRegions(BlockSyntax) but for type members.
    /// </summary>
    private static List<PreprocessorRegion> ExtractMemberPreprocessorRegions(TypeDeclarationSyntax typeDecl)
    {
        var regions = new List<PreprocessorRegion>();
        var members = typeDecl.Members;

        for (int i = 0; i < members.Count; i++)
        {
            var leadingTrivia = members[i].GetLeadingTrivia().ToList();

            bool hasIf = leadingTrivia.Any(t => t.IsKind(SyntaxKind.IfDirectiveTrivia));
            if (!hasIf) continue;

            var stmtBranches = new List<PreprocessorBranch>();
            string? ifCondition = null;
            string? currentDisabled = null;
            string? currentCondition = null;
            int triviaDepth = 0;

            foreach (var trivia in leadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                {
                    if (triviaDepth == 0 && ifCondition == null)
                    {
                        if (trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
                            ifCondition = ifDir.Condition.ToString();
                        currentCondition = ifCondition;
                        currentDisabled = null;
                    }
                    else
                    {
                        triviaDepth++;
                    }
                }
                else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                {
                    if (triviaDepth > 0) { triviaDepth--; continue; }
                }
                else if (triviaDepth > 0)
                {
                    continue;
                }
                else if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia) && ifCondition != null)
                {
                    stmtBranches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                    if (trivia.GetStructure() is ElifDirectiveTriviaSyntax elifDir)
                        currentCondition = elifDir.Condition.ToString();
                    else
                        currentCondition = "ELIF";
                    currentDisabled = null;
                }
                else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia) && ifCondition != null)
                {
                    stmtBranches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                    currentCondition = null;
                    currentDisabled = null;
                }
                else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia) && ifCondition != null && triviaDepth == 0)
                {
                    currentDisabled = trivia.ToString();
                }
            }

            if (ifCondition == null) continue;

            // Find where this region ends
            int endIdx = members.Count;
            int scanDepth = 1;
            for (int j = i; j < members.Count; j++)
            {
                var jTrivia = j == i ? leadingTrivia : members[j].GetLeadingTrivia().ToList();
                bool skipFirst = (j == i);
                foreach (var trivia in jTrivia)
                {
                    if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                    {
                        if (skipFirst) { skipFirst = false; continue; }
                        scanDepth++;
                    }
                    else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                    {
                        scanDepth--;
                        if (scanDepth == 0)
                        {
                            endIdx = j;
                            goto memberFoundEnd;
                        }
                    }
                }
            }

            // Check close brace trivia
            {
                var closeBraceTrivia = typeDecl.CloseBraceToken.LeadingTrivia.ToList();
                int braceDepth = 0;
                foreach (var trivia in closeBraceTrivia)
                {
                    if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                    {
                        braceDepth++;
                    }
                    else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                    {
                        if (braceDepth > 0) { braceDepth--; continue; }
                        scanDepth--;
                        if (scanDepth == 0)
                        {
                            endIdx = members.Count;
                            break;
                        }
                    }
                    else if (braceDepth > 0)
                    {
                        continue;
                    }
                    else if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia))
                    {
                        stmtBranches.Add(new PreprocessorBranch
                        {
                            Condition = currentCondition,
                            DisabledText = null,
                            IsActive = true
                        });
                        if (trivia.GetStructure() is ElifDirectiveTriviaSyntax elifDir)
                            currentCondition = elifDir.Condition.ToString();
                        else
                            currentCondition = "ELIF";
                        currentDisabled = null;
                    }
                    else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia))
                    {
                        stmtBranches.Add(new PreprocessorBranch
                        {
                            Condition = currentCondition,
                            DisabledText = null,
                            IsActive = true
                        });
                        currentCondition = null;
                        currentDisabled = null;
                    }
                    else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                    {
                        currentDisabled = trivia.ToString();
                    }
                }
            }
            memberFoundEnd:

            // If there's accumulated disabled text that hasn't been added as a branch
            // (no #elif/#else transition happened), add it now
            if (stmtBranches.Count == 0 && currentDisabled != null)
            {
                // Simple #if with all disabled text, no #elif/#else
                stmtBranches.Add(new PreprocessorBranch
                {
                    Condition = currentCondition,
                    DisabledText = currentDisabled,
                    IsActive = false
                });
            }
            else
            {
                bool activeAlreadyAdded = stmtBranches.Any(b => b.IsActive);
                if (!activeAlreadyAdded)
                {
                    stmtBranches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = null,
                        IsActive = true
                    });
                }
                else if (currentDisabled != null)
                {
                    stmtBranches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                }
            }

            regions.Add(new PreprocessorRegion
            {
                ActiveStart = i,
                ActiveEnd = endIdx,
                Branches = stmtBranches
            });

            // Advance past the region; when endIdx == i (no active members), stay at i
            // so the for loop increment advances to i+1
            i = Math.Max(i, endIdx - 1);
        }

        // Check close brace for #if with no parsed members (everything disabled)
        {
            var closeBraceTrivia = typeDecl.CloseBraceToken.LeadingTrivia.ToList();
            string? ifCondition = null;
            string? currentCond = null;
            string? currentDis = null;
            var branches = new List<PreprocessorBranch>();
            int braceDepth = 0;

            foreach (var trivia in closeBraceTrivia)
            {
                if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
                {
                    if (braceDepth == 0 && ifCondition == null)
                    {
                        if (trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
                            ifCondition = ifDir.Condition.ToString();
                        currentCond = ifCondition;
                        currentDis = null;
                    }
                    braceDepth++;
                }
                else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
                {
                    braceDepth--;
                    if (braceDepth == 0 && ifCondition != null)
                    {
                        branches.Add(new PreprocessorBranch
                        {
                            Condition = currentCond,
                            DisabledText = currentDis,
                            IsActive = false
                        });
                        regions.Add(new PreprocessorRegion
                        {
                            ActiveStart = members.Count,
                            ActiveEnd = members.Count,
                            Branches = branches
                        });
                        ifCondition = null;
                        branches = new List<PreprocessorBranch>();
                    }
                }
                else if (braceDepth != 1 || ifCondition == null)
                {
                    continue;
                }
                else if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia))
                {
                    branches.Add(new PreprocessorBranch
                    {
                        Condition = currentCond,
                        DisabledText = currentDis,
                        IsActive = false
                    });
                    if (trivia.GetStructure() is ElifDirectiveTriviaSyntax elifDir)
                        currentCond = elifDir.Condition.ToString();
                    else
                        currentCond = "ELIF";
                    currentDis = null;
                }
                else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia))
                {
                    branches.Add(new PreprocessorBranch
                    {
                        Condition = currentCond,
                        DisabledText = currentDis,
                        IsActive = false
                    });
                    currentCond = null;
                    currentDis = null;
                }
                else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                {
                    currentDis = trivia.ToString();
                }
            }
        }

        return regions;
    }

    /// <summary>
    /// Parses disabled text (from an inactive preprocessor branch) as class members
    /// and converts them to typed member lists for MemberPreprocessorBlockNode.
    /// </summary>
    private (List<ClassFieldNode> fields, List<PropertyNode> properties, List<ConstructorNode> constructors,
             List<MethodNode> methods, List<EventDefinitionNode> events, List<OperatorOverloadNode> operatorOverloads)
        ConvertDisabledMemberText(string disabledText)
    {
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var events = new List<EventDefinitionNode>();
        var operatorOverloads = new List<OperatorOverloadNode>();

        var wrapper = $"class _PP {{ {disabledText} }}";
        try
        {
            var tree = CSharpSyntaxTree.ParseText(wrapper);
            var root = tree.GetCompilationUnitRoot();
            var classDecl = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();
            if (classDecl == null) return (fields, properties, constructors, methods, events, operatorOverloads);

            foreach (var member in classDecl.Members)
            {
                try
                {
                    ConvertClassMember(member, fields, properties, constructors, methods, events, operatorOverloads);
                }
                catch
                {
                    // Preserve unconvertible members as a method with fallback comment body
                    var fallbackId = _context.GenerateId("m");
                    var fallbackBody = new List<StatementNode>
                    {
                        _context.PassthroughOnError
                            ? new RawCSharpNode(TextSpan.Empty, member.ToString().Trim())
                            : new FallbackCommentNode(TextSpan.Empty, member.ToString().Trim(), "preprocessor-disabled",
                                "Member in disabled preprocessor branch could not be converted")
                    };
                    methods.Add(new MethodNode(TextSpan.Empty, fallbackId, $"_PP_Fallback_{fallbackId}",
                        Visibility.Private, MethodModifiers.None,
                        Array.Empty<TypeParameterNode>(), Array.Empty<ParameterNode>(),
                        null, null, Array.Empty<RequiresNode>(), Array.Empty<EnsuresNode>(),
                        fallbackBody, new AttributeCollection()));
                }
            }
        }
        catch
        {
            // If the disabled text can't be parsed at all, preserve it as a fallback method
            var fallbackId = _context.GenerateId("m");
            var fallbackBody = new List<StatementNode>
            {
                _context.PassthroughOnError
                    ? new RawCSharpNode(TextSpan.Empty, disabledText.Trim())
                    : new FallbackCommentNode(TextSpan.Empty, disabledText.Trim(), "preprocessor-disabled",
                        "Disabled preprocessor text could not be parsed as class members")
            };
            methods.Add(new MethodNode(TextSpan.Empty, fallbackId, $"_PP_Fallback_{fallbackId}",
                Visibility.Private, MethodModifiers.None,
                Array.Empty<TypeParameterNode>(), Array.Empty<ParameterNode>(),
                null, null, Array.Empty<RequiresNode>(), Array.Empty<EnsuresNode>(),
                fallbackBody, new AttributeCollection()));
        }

        return (fields, properties, constructors, methods, events, operatorOverloads);
    }

    /// <summary>
    /// Builds a (potentially nested) MemberPreprocessorBlockNode from an ordered list of branches.
    /// Same right-to-left nesting strategy as BuildPreprocessorNode.
    /// </summary>
    private MemberPreprocessorBlockNode BuildMemberPreprocessorNode(
        TextSpan span,
        List<PreprocessorBranch> branches,
        List<ClassFieldNode> activeFields,
        List<PropertyNode> activeProperties,
        List<ConstructorNode> activeConstructors,
        List<MethodNode> activeMethods,
        List<EventDefinitionNode> activeEvents,
        List<OperatorOverloadNode> activeOperatorOverloads)
    {
        if (branches.Count == 0)
        {
            return new MemberPreprocessorBlockNode(span, "UNKNOWN",
                activeFields, activeProperties, activeConstructors,
                activeMethods, activeEvents, activeOperatorOverloads);
        }

        // Convert each branch's body
        var convertedBodies = new List<(string? condition, List<ClassFieldNode> fields, List<PropertyNode> properties,
            List<ConstructorNode> constructors, List<MethodNode> methods,
            List<EventDefinitionNode> events, List<OperatorOverloadNode> operatorOverloads)>();

        foreach (var branch in branches)
        {
            if (branch.IsActive)
            {
                convertedBodies.Add((branch.Condition, activeFields, activeProperties,
                    activeConstructors, activeMethods, activeEvents, activeOperatorOverloads));
            }
            else if (branch.DisabledText != null)
            {
                var (fields, properties, constructors, methods, events, operatorOverloads) =
                    ConvertDisabledMemberText(branch.DisabledText);
                convertedBodies.Add((branch.Condition, fields, properties, constructors, methods, events, operatorOverloads));
            }
            else
            {
                convertedBodies.Add((branch.Condition, new List<ClassFieldNode>(), new List<PropertyNode>(),
                    new List<ConstructorNode>(), new List<MethodNode>(),
                    new List<EventDefinitionNode>(), new List<OperatorOverloadNode>()));
            }
        }

        // Build nested structure from right to left
        MemberPreprocessorBlockNode? currentElse = null;

        for (int idx = convertedBodies.Count - 1; idx >= 1; idx--)
        {
            var (condition, fields, properties, constructors, methods, events, operatorOverloads) = convertedBodies[idx];
            if (condition == null)
            {
                // #else branch — becomes the innermost else (empty condition)
                currentElse = new MemberPreprocessorBlockNode(span, "",
                    fields, properties, constructors, methods, events, operatorOverloads);
            }
            else
            {
                // #elif branch — wrap in a new MemberPreprocessorBlockNode
                var elifNode = new MemberPreprocessorBlockNode(span, condition,
                    fields, properties, constructors, methods, events, operatorOverloads, currentElse);
                currentElse = elifNode;
            }
        }

        // Build the outermost #if node
        var first = convertedBodies[0];
        return new MemberPreprocessorBlockNode(span, first.condition ?? "IF",
            first.fields, first.properties, first.constructors,
            first.methods, first.events, first.operatorOverloads, currentElse);
    }

    /// <summary>
    /// Scans the compilation unit for top-level #if directives that wrap entire type declarations.
    /// When Roslyn encounters #if with a false condition, it excludes the type from the syntax tree
    /// and stores the disabled text as trivia. This method finds those blocks and converts them.
    /// </summary>
    private void ScanModuleLevelPreprocessorBlocks(CompilationUnitSyntax root)
    {
        // Collect all trivia from the compilation unit
        var allTrivia = root.DescendantTrivia(descendIntoTrivia: true).ToList();

        string? currentCondition = null;
        var branches = new List<(string? condition, string? disabledText)>();
        int depth = 0;
        bool inTopLevelIf = false;

        for (int i = 0; i < allTrivia.Count; i++)
        {
            var trivia = allTrivia[i];

            if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
            {
                if (!inTopLevelIf && depth == 0)
                {
                    // Check if this is a module-level #if (not inside a type/method)
                    var parentToken = trivia.Token;
                    var parentNode = parentToken.Parent;
                    bool isModuleLevel = parentNode is CompilationUnitSyntax
                        || parentNode is BaseNamespaceDeclarationSyntax
                        || (parentNode?.Parent is CompilationUnitSyntax)
                        || (parentNode?.Parent is BaseNamespaceDeclarationSyntax);

                    if (isModuleLevel && trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
                    {
                        currentCondition = ifDir.Condition.ToString();
                        inTopLevelIf = true;
                        continue;
                    }
                }
                if (inTopLevelIf) depth++;
            }
            else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
            {
                if (inTopLevelIf && depth > 0) { depth--; continue; }
                if (inTopLevelIf && depth == 0)
                {
                    // End of our top-level #if — build the block if we have disabled text
                    if (branches.Count > 0 || currentCondition != null)
                    {
                        BuildModuleLevelPreprocessorBlock(currentCondition, branches);
                    }
                    inTopLevelIf = false;
                    currentCondition = null;
                    branches.Clear();
                }
            }
            else if (inTopLevelIf && depth == 0)
            {
                if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia))
                {
                    var elifCond = (trivia.GetStructure() as ElifDirectiveTriviaSyntax)?.Condition.ToString();
                    branches.Add((currentCondition, null));
                    currentCondition = elifCond;
                }
                else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia))
                {
                    branches.Add((currentCondition, null));
                    currentCondition = null; // #else has no condition
                }
                else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                {
                    // Accumulate disabled text for the current branch
                    var text = trivia.ToString();
                    if (branches.Count > 0)
                    {
                        var last = branches[^1];
                        branches[^1] = (last.condition, (last.disabledText ?? "") + text);
                    }
                    else
                    {
                        // First branch (the #if condition itself) — store as first branch
                        branches.Add((currentCondition, text));
                        currentCondition = "__active__"; // marker — this branch has been stored
                    }
                }
            }
        }
    }

    /// <summary>
    /// Builds a TypePreprocessorBlockNode from module-level preprocessor branches
    /// where the code was disabled by Roslyn.
    /// </summary>
    private void BuildModuleLevelPreprocessorBlock(
        string? condition,
        List<(string? condition, string? disabledText)> branches)
    {
        if (branches.Count == 0) return;

        var convertedBranches = new List<(string condition, List<ClassDefinitionNode> classes,
            List<InterfaceDefinitionNode> interfaces, List<EnumDefinitionNode> enums,
            List<DelegateDefinitionNode> delegates, List<UsingDirectiveNode> usings)>();

        foreach (var (branchCondition, disabledText) in branches)
        {
            var cond = branchCondition ?? "";
            if (cond == "__active__") cond = condition ?? "IF";

            if (!string.IsNullOrEmpty(disabledText))
            {
                var (classes, interfaces, enums, delegates, usings) = ConvertDisabledTypeText(disabledText);
                if (classes.Count > 0 || interfaces.Count > 0 || enums.Count > 0 || delegates.Count > 0 || usings.Count > 0)
                {
                    convertedBranches.Add((cond, classes, interfaces, enums, delegates, usings));
                }
            }
        }

        if (convertedBranches.Count == 0) return;

        // Build nested structure right-to-left
        TypePreprocessorBlockNode? currentElse = null;
        for (int idx = convertedBranches.Count - 1; idx >= 1; idx--)
        {
            var (cond, classes, interfaces, enums, delegates, usings) = convertedBranches[idx];
            currentElse = new TypePreprocessorBlockNode(TextSpan.Empty, cond,
                classes, interfaces, enums, delegates, currentElse, usings);
        }

        var first = convertedBranches[0];
        var ppNode = new TypePreprocessorBlockNode(TextSpan.Empty, first.condition,
            first.classes, first.interfaces, first.enums, first.delegates, currentElse, first.usings);

        _typePreprocessorBlocks.Add(ppNode);
        _context.RecordFeatureUsage("preprocessor-directive");
    }

    /// <summary>
    /// Wraps a type declaration that has #if trivia into a TypePreprocessorBlockNode.
    /// Saves/restores the type collection lists and captures the active branch.
    /// </summary>
    private void WrapTypeInPreprocessorBlock(MemberDeclarationSyntax typeDecl, string condition)
    {
        var span = GetTextSpan(typeDecl);

        // Save current type lists
        var savedClasses = _classes.ToList();
        var savedInterfaces = _interfaces.ToList();
        var savedEnums = _enums.ToList();
        var savedDelegates = _delegates.ToList();

        _classes.Clear();
        _interfaces.Clear();
        _enums.Clear();
        _delegates.Clear();

        // Convert the active type (it's inside the #if's active branch)
        _insideTypePreprocessorConversion = true;
        try
        {
            switch (typeDecl)
            {
                case ClassDeclarationSyntax cls:
                    VisitClassDeclaration(cls);
                    break;
                case RecordDeclarationSyntax rec:
                    VisitRecordDeclaration(rec);
                    break;
                case StructDeclarationSyntax str:
                    VisitStructDeclaration(str);
                    break;
                case InterfaceDeclarationSyntax iface:
                    VisitInterfaceDeclaration(iface);
                    break;
                case EnumDeclarationSyntax en:
                    VisitEnumDeclaration(en);
                    break;
                case DelegateDeclarationSyntax del:
                    VisitDelegateDeclaration(del);
                    break;
            }
        }
        finally
        {
            _insideTypePreprocessorConversion = false;
        }

        // Capture what was added
        var activeClasses = _classes.ToList();
        var activeInterfaces = _interfaces.ToList();
        var activeEnums = _enums.ToList();
        var activeDelegates = _delegates.ToList();

        // Restore saved lists
        _classes.Clear();
        _classes.AddRange(savedClasses);
        _interfaces.Clear();
        _interfaces.AddRange(savedInterfaces);
        _enums.Clear();
        _enums.AddRange(savedEnums);
        _delegates.Clear();
        _delegates.AddRange(savedDelegates);

        // Extract disabled branches from trivia
        var branches = ExtractTypePreprocessorBranches(typeDecl);

        var ppNode = BuildTypePreprocessorNode(span, condition,
            activeClasses, activeInterfaces, activeEnums, activeDelegates, branches);

        _typePreprocessorBlocks.Add(ppNode);
    }

    /// <summary>
    /// Checks if a type declaration is wrapped in a top-level #if directive.
    /// Returns the condition string if found, null otherwise.
    /// </summary>
    private static string? GetTypePreprocessorCondition(MemberDeclarationSyntax typeDecl)
    {
        var leadingTrivia = typeDecl.GetLeadingTrivia();
        string? condition = null;
        foreach (var trivia in leadingTrivia)
        {
            if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia) &&
                trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
            {
                condition = ifDir.Condition.ToString();
            }
        }

        // Verify there's a matching #endif in the trailing trivia
        if (condition != null)
        {
            var trailingTrivia = typeDecl.GetTrailingTrivia();
            bool hasEndif = trailingTrivia.Any(t => t.IsKind(SyntaxKind.EndIfDirectiveTrivia));
            if (!hasEndif)
            {
                // Check if #endif is attached to the next token or the closing brace
                var lastToken = typeDecl.GetLastToken();
                var trailingOfLast = lastToken.TrailingTrivia;
                hasEndif = trailingOfLast.Any(t => t.IsKind(SyntaxKind.EndIfDirectiveTrivia));
            }
            if (!hasEndif)
                return null; // Not a complete wrapping #if
        }

        return condition;
    }

    /// <summary>
    /// Extracts disabled branch text and conditions from the trivia of a #if-wrapped type declaration.
    /// Returns a list of (condition, disabledText) pairs for #elif/#else branches.
    /// </summary>
    private static List<PreprocessorBranch> ExtractTypePreprocessorBranches(MemberDeclarationSyntax typeDecl)
    {
        var branches = new List<PreprocessorBranch>();
        var allTrivia = typeDecl.DescendantTrivia(descendIntoTrivia: true).ToList();

        string? currentCondition = null;
        string? currentDisabled = null;
        bool foundIf = false;
        int depth = 0;

        foreach (var trivia in allTrivia)
        {
            if (trivia.IsKind(SyntaxKind.IfDirectiveTrivia))
            {
                if (!foundIf)
                {
                    foundIf = true;
                    if (trivia.GetStructure() is IfDirectiveTriviaSyntax ifDir)
                        currentCondition = ifDir.Condition.ToString();
                }
                else
                {
                    depth++;
                }
            }
            else if (trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia))
            {
                if (depth > 0) { depth--; continue; }
                // End of our top-level #if — record any remaining branch
                if (currentDisabled != null || currentCondition == null)
                {
                    branches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                }
                break;
            }
            else if (depth > 0)
            {
                continue;
            }
            else if (trivia.IsKind(SyntaxKind.ElifDirectiveTrivia))
            {
                // Save current disabled text for the previous branch
                if (currentDisabled != null)
                {
                    branches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                }
                if (trivia.GetStructure() is ElifDirectiveTriviaSyntax elifDir)
                    currentCondition = elifDir.Condition.ToString();
                else
                    currentCondition = "ELIF";
                currentDisabled = null;
            }
            else if (trivia.IsKind(SyntaxKind.ElseDirectiveTrivia))
            {
                if (currentDisabled != null)
                {
                    branches.Add(new PreprocessorBranch
                    {
                        Condition = currentCondition,
                        DisabledText = currentDisabled,
                        IsActive = false
                    });
                }
                currentCondition = null; // #else has no condition
                currentDisabled = null;
            }
            else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia) && foundIf && depth == 0)
            {
                currentDisabled = (currentDisabled ?? "") + trivia.ToString();
            }
        }

        return branches;
    }

    /// <summary>
    /// Converts disabled type text (from preprocessor branches) into type definition nodes.
    /// Parses the text as a C# compilation unit and converts any type declarations found.
    /// </summary>
    private (List<ClassDefinitionNode> classes, List<InterfaceDefinitionNode> interfaces,
             List<EnumDefinitionNode> enums, List<DelegateDefinitionNode> delegates,
             List<UsingDirectiveNode> usings)
        ConvertDisabledTypeText(string disabledText)
    {
        var classes = new List<ClassDefinitionNode>();
        var interfaces = new List<InterfaceDefinitionNode>();
        var enums = new List<EnumDefinitionNode>();
        var delegates = new List<DelegateDefinitionNode>();
        var usings = new List<UsingDirectiveNode>();

        try
        {
            var tree = CSharpSyntaxTree.ParseText(disabledText);
            var root = tree.GetCompilationUnitRoot();

            // Extract using directives
            foreach (var usingDir in root.Usings)
            {
                if (usingDir.Name != null)
                {
                    var namespaceName = usingDir.Name.ToString();
                    var isStatic = usingDir.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
                    var alias = usingDir.Alias?.Name.ToString();
                    usings.Add(new UsingDirectiveNode(TextSpan.Empty, namespaceName, alias, isStatic));
                }
            }

            foreach (var member in root.Members)
            {
                try
                {
                    switch (member)
                    {
                        case ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax:
                            // Save and restore state
                            var savedClasses = _classes.ToList();
                            _classes.Clear();
                            if (member is ClassDeclarationSyntax topClass) VisitClassDeclaration(topClass);
                            else if (member is RecordDeclarationSyntax topRecord) VisitRecordDeclaration(topRecord);
                            else if (member is StructDeclarationSyntax topStruct) VisitStructDeclaration(topStruct);
                            classes.AddRange(_classes);
                            _classes.Clear();
                            _classes.AddRange(savedClasses);
                            break;
                        case InterfaceDeclarationSyntax ifaceDecl:
                            var savedInterfaces = _interfaces.ToList();
                            _interfaces.Clear();
                            VisitInterfaceDeclaration(ifaceDecl);
                            interfaces.AddRange(_interfaces);
                            _interfaces.Clear();
                            _interfaces.AddRange(savedInterfaces);
                            break;
                        case EnumDeclarationSyntax enumDecl:
                            var savedEnums = _enums.ToList();
                            _enums.Clear();
                            VisitEnumDeclaration(enumDecl);
                            enums.AddRange(_enums);
                            _enums.Clear();
                            _enums.AddRange(savedEnums);
                            break;
                        case DelegateDeclarationSyntax delDecl:
                            var savedDelegates = _delegates.ToList();
                            _delegates.Clear();
                            VisitDelegateDeclaration(delDecl);
                            delegates.AddRange(_delegates);
                            _delegates.Clear();
                            _delegates.AddRange(savedDelegates);
                            break;
                    }
                }
                catch
                {
                    // Skip unconvertible types in disabled branches
                }
            }

            // Also check inside namespace declarations
            foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                foreach (var member in ns.Members)
                {
                    try
                    {
                        switch (member)
                        {
                            case ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax:
                                var savedClasses2 = _classes.ToList();
                                _classes.Clear();
                                if (member is ClassDeclarationSyntax nsClass) VisitClassDeclaration(nsClass);
                                else if (member is RecordDeclarationSyntax nsRecord) VisitRecordDeclaration(nsRecord);
                                else if (member is StructDeclarationSyntax nsStruct) VisitStructDeclaration(nsStruct);
                                classes.AddRange(_classes);
                                _classes.Clear();
                                _classes.AddRange(savedClasses2);
                                break;
                            case InterfaceDeclarationSyntax ifaceDecl2:
                                var savedInterfaces2 = _interfaces.ToList();
                                _interfaces.Clear();
                                VisitInterfaceDeclaration(ifaceDecl2);
                                interfaces.AddRange(_interfaces);
                                _interfaces.Clear();
                                _interfaces.AddRange(savedInterfaces2);
                                break;
                            case EnumDeclarationSyntax enumDecl2:
                                var savedEnums2 = _enums.ToList();
                                _enums.Clear();
                                VisitEnumDeclaration(enumDecl2);
                                enums.AddRange(_enums);
                                _enums.Clear();
                                _enums.AddRange(savedEnums2);
                                break;
                            case DelegateDeclarationSyntax delDecl2:
                                var savedDelegates2 = _delegates.ToList();
                                _delegates.Clear();
                                VisitDelegateDeclaration(delDecl2);
                                delegates.AddRange(_delegates);
                                _delegates.Clear();
                                _delegates.AddRange(savedDelegates2);
                                break;
                        }
                    }
                    catch
                    {
                        // Skip unconvertible types
                    }
                }
            }
        }
        catch
        {
            // If text can't be parsed, return empty lists
        }

        return (classes, interfaces, enums, delegates, usings);
    }

    /// <summary>
    /// Builds a TypePreprocessorBlockNode with active types and disabled branches.
    /// </summary>
    private TypePreprocessorBlockNode BuildTypePreprocessorNode(
        TextSpan span,
        string condition,
        List<ClassDefinitionNode> activeClasses,
        List<InterfaceDefinitionNode> activeInterfaces,
        List<EnumDefinitionNode> activeEnums,
        List<DelegateDefinitionNode> activeDelegates,
        List<PreprocessorBranch> disabledBranches)
    {
        if (disabledBranches.Count == 0)
        {
            return new TypePreprocessorBlockNode(span, condition,
                activeClasses, activeInterfaces, activeEnums, activeDelegates);
        }

        // Convert disabled branches and build right-to-left nested structure
        var convertedBodies = new List<(string? condition, List<ClassDefinitionNode> classes,
            List<InterfaceDefinitionNode> interfaces, List<EnumDefinitionNode> enums,
            List<DelegateDefinitionNode> delegates, List<UsingDirectiveNode> usings)>();

        // First entry is the active branch (no conditional usings)
        convertedBodies.Add((condition, activeClasses, activeInterfaces, activeEnums, activeDelegates, new List<UsingDirectiveNode>()));

        foreach (var branch in disabledBranches)
        {
            if (branch.DisabledText != null)
            {
                var (classes, interfaces, enums, delegates, usings) = ConvertDisabledTypeText(branch.DisabledText);
                convertedBodies.Add((branch.Condition, classes, interfaces, enums, delegates, usings));
            }
            else
            {
                convertedBodies.Add((branch.Condition, new List<ClassDefinitionNode>(),
                    new List<InterfaceDefinitionNode>(), new List<EnumDefinitionNode>(),
                    new List<DelegateDefinitionNode>(), new List<UsingDirectiveNode>()));
            }
        }

        TypePreprocessorBlockNode? currentElse = null;
        for (int idx = convertedBodies.Count - 1; idx >= 1; idx--)
        {
            var (cond, classes, interfaces, enums, delegates, usings) = convertedBodies[idx];
            if (cond == null)
            {
                currentElse = new TypePreprocessorBlockNode(span, "",
                    classes, interfaces, enums, delegates, usings: usings);
            }
            else
            {
                currentElse = new TypePreprocessorBlockNode(span, cond,
                    classes, interfaces, enums, delegates, currentElse, usings);
            }
        }

        var first = convertedBodies[0];
        return new TypePreprocessorBlockNode(span, first.condition ?? "IF",
            first.classes, first.interfaces, first.enums, first.delegates, currentElse, first.usings);
    }

    private IReadOnlyList<StatementNode> ConvertBlock(BlockSyntax block)
    {
        var statements = new List<StatementNode>();

        // Extract preprocessor regions from trivia
        var ppRegions = ExtractPreprocessorRegions(block);
        var ppRegionsByStart = new Dictionary<int, PreprocessorRegion>();
        var ppCoveredIndices = new HashSet<int>();
        foreach (var region in ppRegions)
        {
            ppRegionsByStart.TryAdd(region.ActiveStart, region);
            for (int idx = region.ActiveStart; idx < region.ActiveEnd; idx++)
                ppCoveredIndices.Add(idx);
        }

        for (int stmtIndex = 0; stmtIndex < block.Statements.Count; stmtIndex++)
        {
            // Handle preprocessor regions
            if (ppRegionsByStart.TryGetValue(stmtIndex, out var ppRegion))
            {
                _context.RecordFeatureUsage("preprocessor-directive");
                var span = ppRegion.ActiveStart < block.Statements.Count
                    ? GetTextSpan(block.Statements[ppRegion.ActiveStart])
                    : TextSpan.Empty;

                // Convert active (parsed) body statements
                var activeStatements = new List<StatementNode>();
                for (int bi = ppRegion.ActiveStart; bi < ppRegion.ActiveEnd; bi++)
                {
                    var ppConverted = ConvertStatement(block.Statements[bi]);
                    if (ppConverted != null) activeStatements.Add(ppConverted);
                }

                statements.Add(BuildPreprocessorNode(span, ppRegion.Branches, activeStatements));

                // Skip past the region
                stmtIndex = ppRegion.ActiveEnd - 1; // loop will increment
                continue;
            }

            // Skip indices covered by a preprocessor region (shouldn't happen due to skip above, but safety)
            if (ppCoveredIndices.Contains(stmtIndex))
                continue;

            var statement = block.Statements[stmtIndex];

            // Clear pending statements before each statement conversion.
            // Expression-level chain hoisting in ConvertInvocationExpression may add
            // temp bind statements here; they must be flushed before the containing statement.
            _pendingStatements.Clear();

            // Handle local declarations with multiple variables specially
            if (statement is LocalDeclarationStatementSyntax localDecl && localDecl.Declaration.Variables.Count > 1)
            {
                statements.AddRange(ConvertLocalDeclarationMultiple(localDecl));
                FlushPendingStatements(statements);
                continue;
            }

            // Handle chained method calls in local declarations (e.g., var x = a.Where(...).First())
            // Skip chains handled by native operations (string, StringBuilder, regex, char)
            if (statement is LocalDeclarationStatementSyntax chainDecl
                && chainDecl.Declaration.Variables.Count == 1
                && chainDecl.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax chainInit
                && IsChainedInvocation(chainInit)
                && !WouldChainUseNativeOps(chainInit))
            {
                // CollectChainSteps may hoist lambdas to _pendingStatements;
                // flush them before the chain binds so they are defined in order.
                var localChainResults = DecomposeChainedLocalDeclaration(chainDecl);
                FlushPendingStatements(statements);
                statements.AddRange(localChainResults);
                continue;
            }

            // Handle chained method calls in expression statements (e.g., a.Where(...).ToList())
            // Skip chains handled by native operations (string, StringBuilder, regex, char)
            if (statement is ExpressionStatementSyntax exprStmt
                && exprStmt.Expression is InvocationExpressionSyntax chainExpr
                && IsChainedInvocation(chainExpr)
                && !WouldChainUseNativeOps(chainExpr))
            {
                // CollectChainSteps may hoist lambdas to _pendingStatements;
                // flush them before the chain binds so they are defined in order.
                var exprChainResults = DecomposeChainedExpressionStatement(exprStmt);
                FlushPendingStatements(statements);
                statements.AddRange(exprChainResults);
                continue;
            }

            // Handle chained method calls in return statements (e.g., return items.Where(...).First())
            // Skip chains handled by native operations (string, StringBuilder, regex, char)
            if (statement is ReturnStatementSyntax returnStmt
                && returnStmt.Expression is InvocationExpressionSyntax returnChain
                && IsChainedInvocation(returnChain)
                && !WouldChainUseNativeOps(returnChain))
            {
                // CollectChainSteps may hoist lambdas to _pendingStatements;
                // flush them before the chain binds so they are defined in order.
                var chainResults = DecomposeChainedReturnStatement(returnStmt);
                FlushPendingStatements(statements);
                statements.AddRange(chainResults);
                continue;
            }

            // Handle tuple deconstruction assignments: (_a, _b) = (x, y) → §ASSIGN _a x, §ASSIGN _b y
            if (statement is ExpressionStatementSyntax tupleStmt
                && tupleStmt.Expression is AssignmentExpressionSyntax tupleAssign
                && tupleAssign.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && tupleAssign.Left is TupleExpressionSyntax leftTuple
                && tupleAssign.Right is TupleExpressionSyntax rightTuple
                && leftTuple.Arguments.Count == rightTuple.Arguments.Count)
            {
                _context.RecordFeatureUsage("tuple-deconstruction");
                for (int i = 0; i < leftTuple.Arguments.Count; i++)
                {
                    var leftExpr = ConvertExpression(leftTuple.Arguments[i].Expression);
                    var rightExpr = ConvertExpression(rightTuple.Arguments[i].Expression);
                    statements.Add(new AssignmentStatementNode(
                        GetTextSpan(tupleStmt),
                        leftExpr,
                        rightExpr));
                }
                FlushPendingStatements(statements);
                continue;
            }

            // Handle var (a, b) = expr → bind a temp, then §B for each variable
            // Roslyn represents this as: AssignmentExpression(DeclarationExpression(ParenthesizedVariableDesignation), expr)
            if (statement is ExpressionStatementSyntax deconstructStmt
                && deconstructStmt.Expression is AssignmentExpressionSyntax deconstructAssign
                && deconstructAssign.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && deconstructAssign.Left is DeclarationExpressionSyntax deconstructDecl
                && deconstructDecl.Designation is ParenthesizedVariableDesignationSyntax parenDesignation)
            {
                _context.RecordFeatureUsage("tuple-deconstruction");
                _context.IncrementConverted();
                var rhs = ConvertExpression(deconstructAssign.Right);
                var tempName = _context.GenerateId("_tup");
                var span = GetTextSpan(deconstructStmt);
                // Bind the tuple to a temp: §B _tup expr
                statements.Add(new BindStatementNode(span, tempName, null, false, rhs, new AttributeCollection()));
                // Bind each variable: §B a _tup.Item1, §B b _tup.Item2
                for (int i = 0; i < parenDesignation.Variables.Count; i++)
                {
                    var designation = parenDesignation.Variables[i];
                    if (designation is SingleVariableDesignationSyntax singleVar)
                    {
                        var varName = singleVar.Identifier.Text;
                        statements.Add(new BindStatementNode(span, varName, null, false,
                            new FieldAccessNode(span, new ReferenceNode(span, tempName), $"Item{i + 1}"),
                            new AttributeCollection()));
                    }
                }
                FlushPendingStatements(statements);
                continue;
            }

            // Handle local functions by hoisting to module-level §F functions
            if (statement is LocalFunctionStatementSyntax localFunc)
            {
                _context.RecordFeatureUsage("local-function");
                var hoisted = ConvertLocalFunction(localFunc);
                _functions.Add(hoisted);
                FlushPendingStatements(statements);
                continue;
            }

            // Handle lock statements: comment before body (correct semantic order)
            if (statement is LockStatementSyntax lockStmt)
            {
                statements.AddRange(ConvertLockStatements(lockStmt));
                FlushPendingStatements(statements);
                continue;
            }

            // Handle checked/unchecked statements: comment before body
            if (statement is CheckedStatementSyntax checkedStmt)
            {
                statements.AddRange(ConvertCheckedStatements(checkedStmt));
                FlushPendingStatements(statements);
                continue;
            }

            // Handle for statements: initializers before while (for non-standard patterns)
            if (statement is ForStatementSyntax forStmt)
            {
                statements.AddRange(ConvertForStatements(forStmt));
                FlushPendingStatements(statements);
                continue;
            }

            var converted = ConvertStatement(statement);
            if (converted != null)
            {
                // Flush any hoisted temp binds from expression-level chains BEFORE the statement
                FlushPendingStatements(statements);
                statements.Add(converted);
            }
        }

        // Handle preprocessor regions with no parsed statements (entirely disabled text on close brace)
        if (ppRegionsByStart.TryGetValue(block.Statements.Count, out var trailingPP))
        {
            _context.RecordFeatureUsage("preprocessor-directive");
            statements.Add(BuildPreprocessorNode(TextSpan.Empty, trailingPP.Branches, new List<StatementNode>()));
        }

        return statements;
    }

    /// <summary>
    /// Flushes any pending hoisted statements (from expression-level chain decomposition)
    /// into the target list, then clears the pending list.
    /// </summary>
    private void FlushPendingStatements(List<StatementNode> target)
    {
        if (_pendingStatements.Count > 0)
        {
            target.AddRange(_pendingStatements);
            _pendingStatements.Clear();
        }
    }

    private StatementNode? ConvertStatement(StatementSyntax statement)
    {
        _context.Stats.StatementsConverted++;

        try
        {
            return statement switch
            {
                ReturnStatementSyntax returnStmt => ConvertReturnStatement(returnStmt),
                ExpressionStatementSyntax exprStmt => ConvertExpressionStatement(exprStmt),
                LocalDeclarationStatementSyntax localDecl => ConvertLocalDeclaration(localDecl),
                IfStatementSyntax ifStmt => ConvertIfStatement(ifStmt),
                ForStatementSyntax forStmt => ConvertForStatement(forStmt),
                ForEachStatementSyntax forEachStmt => ConvertForEachStatement(forEachStmt),
                WhileStatementSyntax whileStmt => ConvertWhileStatement(whileStmt),
                DoStatementSyntax doStmt => ConvertDoWhileStatement(doStmt),
                TryStatementSyntax tryStmt => ConvertTryStatement(tryStmt),
                ThrowStatementSyntax throwStmt => ConvertThrowStatement(throwStmt),
                BlockSyntax blockStmt => ConvertBlockAsStatement(blockStmt),
                SwitchStatementSyntax switchStmt => ConvertSwitchStatement(switchStmt),
                BreakStatementSyntax breakStmt => ConvertBreakStatement(breakStmt),
                ContinueStatementSyntax continueStmt => ConvertContinueStatement(continueStmt),
                GotoStatementSyntax gotoStmt => ConvertGotoStatement(gotoStmt),
                LabeledStatementSyntax labeledStmt => ConvertLabeledStatement(labeledStmt),
                UsingStatementSyntax usingStmt => ConvertUsingStatement(usingStmt),
                YieldStatementSyntax yieldStmt => ConvertYieldStatement(yieldStmt),
                LockStatementSyntax lockStmt => ConvertLockStatement(lockStmt),
                CheckedStatementSyntax checkedStmt => ConvertCheckedStatement(checkedStmt),
                UnsafeStatementSyntax unsafeStmt => ConvertUnsafeStatement(unsafeStmt),
                FixedStatementSyntax fixedStmt => ConvertFixedStatement(fixedStmt),
                _ => HandleUnsupportedStatement(statement)
            };
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _context.IncrementSkipped();
            return new RawCSharpNode(GetTextSpan(statement), statement.ToFullString());
        }
    }

    private StatementNode? HandleUnsupportedStatement(StatementSyntax statement)
    {
        var featureName = statement.Kind().ToString().Replace("Statement", "").ToLowerInvariant();
        return HandleUnsupportedStatement(statement, featureName);
    }

    private StatementNode? HandleUnsupportedStatement(StatementSyntax statement, string featureName)
    {
        var lineSpan = statement.GetLocation().GetLineSpan();
        _context.AddWarning(
            $"Unsupported statement: {featureName}",
            feature: featureName,
            line: lineSpan.StartLinePosition.Line + 1,
            column: lineSpan.StartLinePosition.Character + 1);
        _context.IncrementSkipped();

        // In Interop mode, preserve unsupported statements as raw C# passthrough
        if (_context.ShouldPreserveCSharp)
        {
            return new RawCSharpNode(GetTextSpan(statement), statement.ToFullString());
        }

        // Return a fallback comment node instead of null
        return CreateFallbackStatement(statement, featureName);
    }

    private ReturnStatementNode ConvertReturnStatement(ReturnStatementSyntax node)
    {
        var expr = node.Expression != null ? ConvertExpression(node.Expression) : null;
        _context.IncrementConverted();
        return new ReturnStatementNode(GetTextSpan(node), expr);
    }

    private ContinueStatementNode ConvertContinueStatement(ContinueStatementSyntax node)
    {
        _context.RecordFeatureUsage("continue");
        _context.IncrementConverted();
        return new ContinueStatementNode(GetTextSpan(node));
    }

    private BreakStatementNode ConvertBreakStatement(BreakStatementSyntax node)
    {
        _context.RecordFeatureUsage("break");
        _context.IncrementConverted();
        return new BreakStatementNode(GetTextSpan(node));
    }

    private StatementNode? ConvertGotoStatement(GotoStatementSyntax node)
    {
        _context.RecordFeatureUsage("goto");
        _context.IncrementConverted();

        if (node.CaseOrDefaultKeyword.IsKind(SyntaxKind.DefaultKeyword))
        {
            return new GotoStatementNode(GetTextSpan(node), "") { IsDefault = true };
        }

        if (node.CaseOrDefaultKeyword.IsKind(SyntaxKind.CaseKeyword))
        {
            var caseExpr = ConvertExpression(node.Expression!);
            return new GotoStatementNode(GetTextSpan(node), "") { CaseLabel = caseExpr };
        }

        var label = node.Expression?.ToString() ?? "";
        return new GotoStatementNode(GetTextSpan(node), label);
    }

    private StatementNode? ConvertLabeledStatement(LabeledStatementSyntax node)
    {
        _context.RecordFeatureUsage("labeled-statement");
        _context.IncrementConverted();
        var label = node.Identifier.Text;
        return new LabelStatementNode(GetTextSpan(node), label);
    }

    private StatementNode ConvertYieldStatement(YieldStatementSyntax node)
    {
        _context.RecordFeatureUsage("yield-return");
        _context.IncrementConverted();

        if (node.ReturnOrBreakKeyword.IsKind(SyntaxKind.BreakKeyword))
        {
            return new YieldBreakStatementNode(GetTextSpan(node));
        }

        var expr = node.Expression != null ? ConvertExpression(node.Expression) : null;
        return new YieldReturnStatementNode(GetTextSpan(node), expr);
    }

    private StatementNode ConvertLockStatement(LockStatementSyntax node)
    {
        _context.RecordFeatureUsage("lock-statement");
        _context.IncrementConverted();

        var id = _context.GenerateId("sync");
        var lockExpr = ConvertExpression(node.Expression);
        var bodyStatements = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        return new SyncBlockNode(GetTextSpan(node), id, lockExpr, bodyStatements);
    }

    private List<StatementNode> ConvertLockStatements(LockStatementSyntax node)
    {
        // Single-element list for backward compat with multi-statement dispatch
        return new List<StatementNode> { ConvertLockStatement(node) };
    }

    private StatementNode ConvertCheckedStatement(CheckedStatementSyntax node)
    {
        // Delegate to multi-statement version; return first element for backward compat
        var results = ConvertCheckedStatements(node);
        return results[0];
    }

    private List<StatementNode> ConvertCheckedStatements(CheckedStatementSyntax node)
    {
        _context.RecordFeatureUsage("checked-block");
        _context.IncrementConverted();

        var result = new List<StatementNode>();

        // Comment first (correct semantic order: annotation before body)
        var keyword = node.IsKind(SyntaxKind.CheckedStatement) ? "checked" : "unchecked";
        if (_context.PassthroughOnError)
        {
            result.Add(new RawCSharpNode(GetTextSpan(node), node.ToFullString()));
            return result;
        }
        result.Add(new FallbackCommentNode(GetTextSpan(node), keyword, "checked-block",
            "Checked/unchecked semantics stripped; handle overflow manually if needed"));

        // Then body statements
        var bodyStatements = ConvertBlock(node.Block);
        result.AddRange(bodyStatements);

        return result;
    }

    /// <summary>
    /// Converts a bare ExpressionSyntax to a StatementNode.
    /// Used for for-loop initializers and incrementors which are expressions, not expression statements.
    /// </summary>
    private StatementNode? ConvertExpressionToStatement(ExpressionSyntax expr, TextSpan span)
    {
        if (expr is AssignmentExpressionSyntax assignment)
        {
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return new AssignmentStatementNode(span,
                    ConvertExpression(assignment.Left),
                    ConvertExpression(assignment.Right));
            }
            // Compound assignments (+=, -=, etc.)
            var compOp = assignment.Kind() switch
            {
                SyntaxKind.AddAssignmentExpression => CompoundAssignmentOperator.Add,
                SyntaxKind.SubtractAssignmentExpression => CompoundAssignmentOperator.Subtract,
                SyntaxKind.MultiplyAssignmentExpression => CompoundAssignmentOperator.Multiply,
                SyntaxKind.DivideAssignmentExpression => CompoundAssignmentOperator.Divide,
                SyntaxKind.ModuloAssignmentExpression => CompoundAssignmentOperator.Modulo,
                _ => (CompoundAssignmentOperator?)null
            };
            if (compOp != null)
            {
                return new CompoundAssignmentStatementNode(span,
                    ConvertExpression(assignment.Left),
                    compOp.Value,
                    ConvertExpression(assignment.Right));
            }
        }
        // Handle i++, i--, ++i, --i as compound assignments
        if (expr is PostfixUnaryExpressionSyntax postfix)
        {
            var target = ConvertExpression(postfix.Operand);
            var op = postfix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken)
                ? CompoundAssignmentOperator.Add
                : CompoundAssignmentOperator.Subtract;
            return new CompoundAssignmentStatementNode(span, target, op, new IntLiteralNode(span, 1));
        }
        if (expr is PrefixUnaryExpressionSyntax prefix
            && (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            var target = ConvertExpression(prefix.Operand);
            var op = prefix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken)
                ? CompoundAssignmentOperator.Add
                : CompoundAssignmentOperator.Subtract;
            return new CompoundAssignmentStatementNode(span, target, op, new IntLiteralNode(span, 1));
        }
        // Handle throw expressions: set => throw new NotSupportedException()
        if (expr is ThrowExpressionSyntax throwExpr)
        {
            var exception = throwExpr.Expression != null ? ConvertExpression(throwExpr.Expression) : null;
            return new ThrowStatementNode(span, exception);
        }
        // Fallback: convert expression and wrap as a discarded bind
        var exprNode = ConvertExpression(expr);
        return new BindStatementNode(span, "_", null, false, exprNode, new AttributeCollection());
    }

    private StatementNode ConvertExpressionStatement(ExpressionStatementSyntax node)
    {
        var expr = node.Expression;
        _context.IncrementConverted();

        // Handle assignment expressions
        if (expr is AssignmentExpressionSyntax assignment)
        {
            // Handle element access assignments (indexer assignments) - convert to collection operations
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is ElementAccessExpressionSyntax elementAccess)
            {
                var collectionName = elementAccess.Expression.ToString();
                if (elementAccess.ArgumentList.Arguments.Count == 1)
                {
                    var indexOrKey = ConvertExpression(elementAccess.ArgumentList.Arguments[0].Expression);
                    var value = ConvertExpression(assignment.Right);

                    // Determine if this is a list (numeric index) or dictionary (key-based)
                    var firstArg = elementAccess.ArgumentList.Arguments[0].Expression;
                    if (firstArg is LiteralExpressionSyntax literal &&
                        literal.Kind() == SyntaxKind.NumericLiteralExpression)
                    {
                        // Numeric index - use CollectionSetIndexNode (§SETIDX)
                        _context.RecordFeatureUsage("collection-setindex");
                        return new CollectionSetIndexNode(
                            GetTextSpan(node),
                            collectionName,
                            indexOrKey,
                            value);
                    }
                    else
                    {
                        // String or other key - use DictionaryPutNode (§PUT)
                        _context.RecordFeatureUsage("dictionary-put");
                        return new DictionaryPutNode(
                            GetTextSpan(node),
                            collectionName,
                            indexOrKey,
                            value);
                    }
                }
            }

            // Check if this looks like an event subscription vs compound assignment
            // Event handlers typically use method references or lambdas, while compound
            // assignments use numeric/value expressions
            var rightIsHandler = assignment.Right is IdentifierNameSyntax ||
                                 assignment.Right is MemberAccessExpressionSyntax ||
                                 assignment.Right is LambdaExpressionSyntax;

            // Handle event subscription (+=) - only for event-like patterns
            if (assignment.IsKind(SyntaxKind.AddAssignmentExpression))
            {
                if (rightIsHandler && LooksLikeEventTarget(assignment.Left))
                {
                    _context.RecordFeatureUsage("event-subscribe");
                    return new EventSubscribeNode(
                        GetTextSpan(node),
                        ConvertExpression(assignment.Left),
                        ConvertExpression(assignment.Right));
                }
                else
                {
                    // Compound assignment (+=)
                    _context.RecordFeatureUsage("compound-assignment");
                    return new CompoundAssignmentStatementNode(
                        GetTextSpan(node),
                        ConvertExpression(assignment.Left),
                        CompoundAssignmentOperator.Add,
                        ConvertExpression(assignment.Right));
                }
            }

            // Handle event unsubscription (-=) - only for event-like patterns
            if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
            {
                if (rightIsHandler && LooksLikeEventTarget(assignment.Left))
                {
                    _context.RecordFeatureUsage("event-unsubscribe");
                    return new EventUnsubscribeNode(
                        GetTextSpan(node),
                        ConvertExpression(assignment.Left),
                        ConvertExpression(assignment.Right));
                }
                else
                {
                    // Compound assignment (-=)
                    _context.RecordFeatureUsage("compound-assignment");
                    return new CompoundAssignmentStatementNode(
                        GetTextSpan(node),
                        ConvertExpression(assignment.Left),
                        CompoundAssignmentOperator.Subtract,
                        ConvertExpression(assignment.Right));
                }
            }

            // Handle other compound assignments (*=, /=, %=, etc.)
            if (assignment.IsKind(SyntaxKind.MultiplyAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.Multiply,
                    ConvertExpression(assignment.Right));
            }

            if (assignment.IsKind(SyntaxKind.DivideAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.Divide,
                    ConvertExpression(assignment.Right));
            }

            if (assignment.IsKind(SyntaxKind.ModuloAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.Modulo,
                    ConvertExpression(assignment.Right));
            }

            if (assignment.IsKind(SyntaxKind.AndAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.BitwiseAnd,
                    ConvertExpression(assignment.Right));
            }

            if (assignment.IsKind(SyntaxKind.OrAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.BitwiseOr,
                    ConvertExpression(assignment.Right));
            }

            if (assignment.IsKind(SyntaxKind.ExclusiveOrAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.BitwiseXor,
                    ConvertExpression(assignment.Right));
            }

            if (assignment.IsKind(SyntaxKind.LeftShiftAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.LeftShift,
                    ConvertExpression(assignment.Right));
            }

            if (assignment.IsKind(SyntaxKind.RightShiftAssignmentExpression))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.RightShift,
                    ConvertExpression(assignment.Right));
            }

            // Handle null-coalescing assignment: x ??= y → CompoundAssignment(NullCoalesce)
            if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            {
                _context.RecordFeatureUsage("null-coalescing-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    CompoundAssignmentOperator.NullCoalesce,
                    ConvertExpression(assignment.Right));
            }

            return new AssignmentStatementNode(
                GetTextSpan(node),
                ConvertExpression(assignment.Left),
                ConvertExpression(assignment.Right));
        }

        // Handle await expressions - create a bind statement with _ as the variable to discard result
        if (expr is AwaitExpressionSyntax awaitExpr)
        {
            _context.RecordFeatureUsage("async-await");
            var awaited = ConvertExpression(awaitExpr.Expression);
            var awaitNode = new AwaitExpressionNode(GetTextSpan(node), awaited, null);

            // Create a bind statement with discard pattern for await statements without assignment
            return new BindStatementNode(
                GetTextSpan(node),
                "_",
                null, // no type
                false, // not mutable
                awaitNode,
                new AttributeCollection());
        }

        // Handle invocation expressions (method calls)
        if (expr is InvocationExpressionSyntax invocation)
        {
            var target = invocation.Expression.ToString();
            var args = invocation.ArgumentList.Arguments
                .Select(a => ConvertExpression(a.Expression))
                .ToList();

            // Hoist §NEW arguments to temp bindings — the parser cannot handle §NEW nested inside §C
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] is NewExpressionNode newNode)
                {
                    var tempName = _context.GenerateId("_new", newNode.TypeName);
                    _pendingStatements.Add(new BindStatementNode(
                        args[i].Span, tempName, null, false, args[i], new AttributeCollection()));
                    args[i] = new ReferenceNode(args[i].Span, tempName);
                }
            }

            var hasNamedArgs = invocation.ArgumentList.Arguments.Any(a => a.NameColon != null);
            var stmtArgNames = hasNamedArgs
                ? invocation.ArgumentList.Arguments
                    .Select(a => a.NameColon?.Name.Identifier.Text)
                    .ToList()
                : null;

            var stmtArgModifiers = ExtractArgumentModifiers(invocation.ArgumentList.Arguments);

            // Check for Console.WriteLine as special case
            if (target == "Console.WriteLine" || target == "System.Console.WriteLine")
            {
                if (args.Count == 1)
                {
                    return new PrintStatementNode(GetTextSpan(node), args[0], isWriteLine: true);
                }
            }
            else if (target == "Console.Write" || target == "System.Console.Write")
            {
                if (args.Count == 1)
                {
                    return new PrintStatementNode(GetTextSpan(node), args[0], isWriteLine: false);
                }
            }

            return new CallStatementNode(
                GetTextSpan(node),
                target,
                fallible: false,
                args,
                new AttributeCollection(),
                stmtArgNames,
                stmtArgModifiers);
        }

        // Handle postfix increment/decrement as compound assignment statements
        if (expr is PostfixUnaryExpressionSyntax postfix)
        {
            if (postfix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(postfix.Operand),
                    CompoundAssignmentOperator.Add,
                    new IntLiteralNode(GetTextSpan(node), 1));
            }
            if (postfix.OperatorToken.IsKind(SyntaxKind.MinusMinusToken))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(postfix.Operand),
                    CompoundAssignmentOperator.Subtract,
                    new IntLiteralNode(GetTextSpan(node), 1));
            }
        }

        // Handle prefix increment/decrement as compound assignment statements
        if (expr is PrefixUnaryExpressionSyntax prefix)
        {
            if (prefix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(prefix.Operand),
                    CompoundAssignmentOperator.Add,
                    new IntLiteralNode(GetTextSpan(node), 1));
            }
            if (prefix.OperatorToken.IsKind(SyntaxKind.MinusMinusToken))
            {
                _context.RecordFeatureUsage("compound-assignment");
                return new CompoundAssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(prefix.Operand),
                    CompoundAssignmentOperator.Subtract,
                    new IntLiteralNode(GetTextSpan(node), 1));
            }
        }

        // Default: wrap as call statement
        return new CallStatementNode(
            GetTextSpan(node),
            expr.ToString(),
            fallible: false,
            Array.Empty<ExpressionNode>(),
            new AttributeCollection());
    }

    /// <summary>
    /// Scans a syntax scope to find all variable names that are reassigned
    /// (via assignment expressions or increment/decrement operators).
    /// </summary>
    private static HashSet<string> CollectReassignedVariables(SyntaxNode scope)
    {
        var reassigned = new HashSet<string>();
        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is IdentifierNameSyntax id)
                reassigned.Add(id.Identifier.ValueText);
        }
        foreach (var unary in scope.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
        {
            if (unary.Operand is IdentifierNameSyntax id)
                reassigned.Add(id.Identifier.ValueText);
        }
        foreach (var unary in scope.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
        {
            if (unary.Operand is IdentifierNameSyntax id)
                reassigned.Add(id.Identifier.ValueText);
        }
        return reassigned;
    }

    private BindStatementNode ConvertLocalDeclaration(LocalDeclarationStatementSyntax node)
    {
        _context.IncrementConverted();

        var variable = node.Declaration.Variables.First();
        var name = variable.Identifier.ValueText;
        var typeName = node.Declaration.Type.IsVar
            ? null
            : TypeMapper.CSharpToCalor(node.Declaration.Type.ToString());
        var isMutable = _reassignedVariables.Contains(name);
        var initializer = variable.Initializer != null
            ? ConvertExpression(variable.Initializer.Value)
            : null;

        return new BindStatementNode(
            GetTextSpan(node),
            name,
            typeName,
            isMutable,
            initializer,
            new AttributeCollection());
    }

    private IReadOnlyList<BindStatementNode> ConvertLocalDeclarationMultiple(LocalDeclarationStatementSyntax node)
    {
        var results = new List<BindStatementNode>();
        var typeName = node.Declaration.Type.IsVar
            ? null
            : TypeMapper.CSharpToCalor(node.Declaration.Type.ToString());

        foreach (var variable in node.Declaration.Variables)
        {
            _context.IncrementConverted();
            var name = variable.Identifier.ValueText;
            var isMutable = _reassignedVariables.Contains(name);
            var initializer = variable.Initializer != null
                ? ConvertExpression(variable.Initializer.Value)
                : null;

            results.Add(new BindStatementNode(
                GetTextSpan(node),
                name,
                typeName,
                isMutable,
                initializer,
                new AttributeCollection()));
        }

        return results;
    }

    /// <summary>
    /// Checks if an expression is a chained method invocation (e.g., a.Where(...).First()).
    /// </summary>
    private static bool IsChainedInvocation(ExpressionSyntax expression)
    {
        return expression is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && (memberAccess.Expression is InvocationExpressionSyntax
                || memberAccess.Expression is ElementAccessExpressionSyntax);
    }

    /// <summary>
    /// Checks if a chained invocation would be handled by native operations (string, StringBuilder,
    /// regex, char) in ConvertInvocationExpression. If so, decomposition should be skipped to
    /// preserve the native operation output.
    ///
    /// COUPLING NOTE: This heuristic mirrors the native-op detection in ConvertInvocationExpression
    /// (lines ~2489-2568). The two must stay aligned:
    ///   - StringBuilder detection here (line ~1456) ↔ TryGetStringBuilderOperation calls (lines ~2491-2514)
    ///   - String method list here (line ~1462) ↔ TryGetStringOperation (line ~2501)
    ///   - Static string/Regex/Char patterns here ↔ static checks (lines ~2532-2568)
    /// If a new native op category is added to ConvertInvocationExpression, add a corresponding
    /// check here, or those chains will be incorrectly decomposed instead of using native ops.
    /// See also: CSharpToCalorConversionTests.StringBuilderChainPreservesNativeOps_* tests.
    /// </summary>
    private static bool WouldChainUseNativeOps(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var methodName = memberAccess.Name.Identifier.Text;
        var targetStr = memberAccess.Expression.ToString();

        // StringBuilder operations: target contains "StringBuilder" or starts with "sb"/"_sb"
        if (targetStr.Contains("StringBuilder") || targetStr.StartsWith("sb") || targetStr.StartsWith("_sb"))
        {
            return methodName is "Append" or "AppendLine" or "Insert" or "Remove" or "Clear" or "ToString";
        }

        // String methods
        if (methodName is "Contains" or "StartsWith" or "EndsWith" or "IndexOf" or "Replace"
            or "Trim" or "TrimStart" or "TrimEnd" or "ToUpper" or "ToLower" or "Substring"
            or "Split" or "Join" or "PadLeft" or "PadRight" or "ToString" or "ToCharArray"
            or "Insert" or "Remove" or "Length" or "IsNullOrEmpty" or "IsNullOrWhiteSpace")
        {
            return true;
        }

        // Static string methods
        if (targetStr.StartsWith("string.") || targetStr.StartsWith("String."))
            return true;

        // Regex methods
        if (targetStr.StartsWith("Regex.") || targetStr.Contains("RegularExpressions.Regex."))
            return true;

        // Char methods
        if (targetStr.StartsWith("char.") || targetStr.StartsWith("Char."))
            return true;

        return false;
    }

    /// <summary>
    /// Collects all steps in a method chain from innermost to outermost.
    /// For a.Where(...).Select(...).First(), returns:
    ///   [("a", "Where", args1), (null, "Select", args2), (null, "First", args3)]
    /// where null target means "use previous step's result".
    /// </summary>
    private List<(string? baseTarget, string methodName, List<ExpressionNode> args, TextSpan span)> CollectChainSteps(
        InvocationExpressionSyntax invocation)
    {
        var steps = new List<(string? baseTarget, string methodName, List<ExpressionNode> args, TextSpan span)>();
        var current = invocation;

        while (current.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var args = current.ArgumentList.Arguments
                .Select(a => ConvertExpression(a.Expression))
                .ToList();
            var span = GetTextSpan(current);

            // Hoist lambda arguments to temp bindings — prevents §LAM nesting inside §C
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] is LambdaExpressionNode)
                {
                    var tempName = _context.GenerateId("_lam", methodName);                    _pendingStatements.Add(new BindStatementNode(
                        span, tempName, null, false, args[i], new AttributeCollection()));
                    args[i] = new ReferenceNode(args[i].Span, tempName);
                }
            }

            if (memberAccess.Expression is InvocationExpressionSyntax inner)
            {
                // Intermediate step — target comes from previous chain step
                steps.Add((null, methodName, args, span));
                current = inner;
            }
            else
            {
                // Base of the chain — has a concrete target
                // If the base is an object creation (new Foo()), hoist to a temp bind
                // so the chain decomposition produces §NEW + bind instead of raw text
                if (memberAccess.Expression is ObjectCreationExpressionSyntax
                    || memberAccess.Expression is ImplicitObjectCreationExpressionSyntax)
                {
                    var newConverted = ConvertExpression(memberAccess.Expression);
                    var newTypeHint = ExtractTypeHint(memberAccess.Expression);
                    var tempName = _context.GenerateId("_new", newTypeHint);
                    _pendingStatements.Add(new BindStatementNode(
                        span, tempName, null, false, newConverted, new AttributeCollection()));
                    steps.Add((tempName, methodName, args, span));
                }
                else
                {
                    var baseTarget = memberAccess.Expression.ToString();
                    steps.Add((baseTarget, methodName, args, span));
                }
                break;
            }
        }

        // Reverse so innermost (base) is first
        steps.Reverse();
        return steps;
    }

    /// <summary>
    /// Decomposes a chained invocation in a local declaration into multiple bind statements.
    /// var result = products.Where(...).First()
    /// becomes:
    ///   var _chain1 = products.Where(...)
    ///   var result = _chain1.First()
    /// </summary>
    private IReadOnlyList<StatementNode> DecomposeChainedLocalDeclaration(LocalDeclarationStatementSyntax node)
    {
        _context.RecordFeatureUsage("linq-method-chain");

        var variable = node.Declaration.Variables.First();
        var finalName = variable.Identifier.ValueText;
        var finalTypeName = node.Declaration.Type.IsVar
            ? null
            : TypeMapper.CSharpToCalor(node.Declaration.Type.ToString());
        var finalIsMutable = _reassignedVariables.Contains(finalName);

        var chainInvocation = (InvocationExpressionSyntax)variable.Initializer!.Value;
        var steps = CollectChainSteps(chainInvocation);

        return EmitChainSteps(steps, finalName, finalTypeName, finalIsMutable, GetTextSpan(node));
    }

    /// <summary>
    /// Decomposes a chained invocation in an expression statement into bind + call statements.
    /// products.Where(...).ToList()
    /// becomes:
    ///   var _chain1 = products.Where(...)
    ///   _chain1.ToList()
    /// </summary>
    private IReadOnlyList<StatementNode> DecomposeChainedExpressionStatement(ExpressionStatementSyntax node)
    {
        _context.RecordFeatureUsage("linq-method-chain");
        _context.IncrementConverted();

        var chainInvocation = (InvocationExpressionSyntax)node.Expression;
        var steps = CollectChainSteps(chainInvocation);

        if (steps.Count < 2)
        {
            // Not actually chained, convert normally
            return new[] { ConvertExpressionStatement(node) };
        }

        var results = new List<StatementNode>();
        var span = GetTextSpan(node);
        string? prevTempName = null;

        // Emit all steps except the last as bind statements
        for (int i = 0; i < steps.Count - 1; i++)
        {
            var step = steps[i];
            var target = i == 0 ? step.baseTarget! : prevTempName!;
            var tempName = _context.GenerateId("_chain", step.methodName);

            var callExpr = new CallExpressionNode(step.span, $"{target}.{step.methodName}", step.args);
            results.Add(new BindStatementNode(span, tempName, null, false, callExpr, new AttributeCollection()));
            prevTempName = tempName;
        }

        // Last step becomes a call statement (no assignment, expression statement)
        var lastStep = steps[^1];
        var lastTarget = prevTempName!;
        results.Add(new CallStatementNode(
            span,
            $"{lastTarget}.{lastStep.methodName}",
            false,
            lastStep.args,
            new AttributeCollection()));

        return results;
    }

    /// <summary>
    /// Decomposes a chained invocation in a return statement into bind statements + final return.
    /// return products.Where(...).First()
    /// becomes:
    ///   var _chain1 = products.Where(...)
    ///   return _chain1.First()
    /// </summary>
    private IReadOnlyList<StatementNode> DecomposeChainedReturnStatement(ReturnStatementSyntax node)
    {
        _context.RecordFeatureUsage("linq-method-chain");
        _context.IncrementConverted();

        var chainInvocation = (InvocationExpressionSyntax)node.Expression!;
        var steps = CollectChainSteps(chainInvocation);

        if (steps.Count < 2)
        {
            // Not actually chained, convert normally
            return new[] { ConvertReturnStatement(node) };
        }

        var results = new List<StatementNode>();
        var span = GetTextSpan(node);
        string? prevTempName = null;

        // Emit all steps except the last as bind statements
        for (int i = 0; i < steps.Count - 1; i++)
        {
            var step = steps[i];
            var target = i == 0 ? step.baseTarget! : prevTempName!;
            var tempName = _context.GenerateId("_chain", step.methodName);

            var callExpr = new CallExpressionNode(step.span, $"{target}.{step.methodName}", step.args);
            results.Add(new BindStatementNode(span, tempName, null, false, callExpr, new AttributeCollection()));
            prevTempName = tempName;
        }

        // Last step becomes the return value
        var lastStep = steps[^1];
        var lastTarget = prevTempName!;
        var lastCallExpr = new CallExpressionNode(lastStep.span, $"{lastTarget}.{lastStep.methodName}", lastStep.args);
        results.Add(new ReturnStatementNode(span, lastCallExpr));

        return results;
    }

    /// <summary>
    /// Emits bind statements for chain steps, with the final step using the provided name and type.
    /// </summary>
    private IReadOnlyList<StatementNode> EmitChainSteps(
        List<(string? baseTarget, string methodName, List<ExpressionNode> args, TextSpan span)> steps,
        string finalName,
        string? finalTypeName,
        bool finalIsMutable,
        TextSpan statementSpan)
    {
        if (steps.Count < 2)
        {
            // Not actually chained — fall back to single bind
            var step = steps[0];
            var callExpr = new CallExpressionNode(step.span, $"{step.baseTarget}.{step.methodName}", step.args);
            return new[]
            {
                new BindStatementNode(statementSpan, finalName, finalTypeName, finalIsMutable, callExpr, new AttributeCollection())
            };
        }

        var results = new List<StatementNode>();
        string? prevTempName = null;

        for (int i = 0; i < steps.Count; i++)
        {
            _context.IncrementConverted();
            var step = steps[i];
            var target = i == 0 ? step.baseTarget! : prevTempName!;
            var isLast = i == steps.Count - 1;

            var callExpr = new CallExpressionNode(step.span, $"{target}.{step.methodName}", step.args);

            if (isLast)
            {
                // Final step uses the original variable name and type
                results.Add(new BindStatementNode(
                    statementSpan, finalName, finalTypeName, finalIsMutable, callExpr, new AttributeCollection()));
            }
            else
            {
                // Intermediate step uses a generated temp name with no type
                var tempName = _context.GenerateId("_chain", step.methodName);
                results.Add(new BindStatementNode(
                    statementSpan, tempName, null, false, callExpr, new AttributeCollection()));
                prevTempName = tempName;
            }
        }

        return results;
    }

    private IfStatementNode ConvertIfStatement(IfStatementSyntax node)
    {
        _context.RecordFeatureUsage("if");
        _context.IncrementConverted();

        var id = _context.GenerateId("if");
        var condition = ConvertExpression(node.Condition);

        // Separate pending statements into two categories:
        // 1. Chain bindings (_chain*, _cast*) → must go BEFORE the if (condition depends on them)
        // 2. Pattern variable bindings (from `is` patterns) → go inside then-body
        var patternBindings = new List<StatementNode>();
        var chainBindings = new List<StatementNode>();
        if (_pendingStatements.Count > 0)
        {
            foreach (var stmt in _pendingStatements)
            {
                if (stmt is BindStatementNode bind && (bind.Name.StartsWith("_chain") || bind.Name.StartsWith("_cast") || bind.Name.StartsWith("_pre")))
                    chainBindings.Add(stmt);
                else
                    patternBindings.Add(stmt);
            }
            _pendingStatements.Clear();
        }

        var thenBody = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        // Inject pattern variable bindings at the start of the then-body
        if (patternBindings.Count > 0)
        {
            var combined = new List<StatementNode>(patternBindings);
            combined.AddRange(thenBody);
            thenBody = combined;
        }

        var elseIfClauses = new List<ElseIfClauseNode>();
        IReadOnlyList<StatementNode>? elseBody = null;

        var currentElse = node.Else;
        while (currentElse != null)
        {
            if (currentElse.Statement is IfStatementSyntax elseIfStmt)
            {
                var elseIfCondition = ConvertExpression(elseIfStmt.Condition);
                var elseIfBody = elseIfStmt.Statement is BlockSyntax elseIfBlock
                    ? ConvertBlock(elseIfBlock)
                    : new List<StatementNode> { ConvertStatement(elseIfStmt.Statement)! };

                elseIfClauses.Add(new ElseIfClauseNode(
                    GetTextSpan(elseIfStmt),
                    elseIfCondition,
                    elseIfBody));

                currentElse = elseIfStmt.Else;
            }
            else
            {
                elseBody = currentElse.Statement is BlockSyntax elseBlock
                    ? ConvertBlock(elseBlock)
                    : new List<StatementNode> { ConvertStatement(currentElse.Statement)! };
                currentElse = null;
            }
        }

        // Restore chain bindings to _pendingStatements AFTER all body conversions
        // (body ConvertBlock calls clear _pendingStatements, so we must restore last)
        // ConvertBlock's FlushPendingStatements will emit these before the if statement
        _pendingStatements.AddRange(chainBindings);

        return new IfStatementNode(
            GetTextSpan(node),
            id,
            condition,
            thenBody,
            elseIfClauses,
            elseBody,
            new AttributeCollection());
    }

    /// <summary>
    /// Single-statement entry point for ConvertStatement switch dispatch.
    /// Used when for-loops appear as non-block bodies (e.g., if (x) for (...) ...).
    /// ConvertBlock uses ConvertForStatements directly for correct ordering.
    /// </summary>
    private StatementNode ConvertForStatement(ForStatementSyntax node)
    {
        var results = ConvertForStatements(node);
        for (int i = 0; i < results.Count - 1; i++)
            _pendingStatements.Add(results[i]);
        return results[results.Count - 1];
    }

    private List<StatementNode> ConvertForStatements(ForStatementSyntax node)
    {
        _context.RecordFeatureUsage("for");
        _context.IncrementConverted();

        var id = _context.GenerateId("for");
        var span = GetTextSpan(node);
        var result = new List<StatementNode>();

        // Try to extract standard for loop pattern: for (var i = from; i <= to; i += step)
        var varName = (string?)null;
        ExpressionNode? from = null;
        ExpressionNode? to = null;
        ExpressionNode? step = null;
        bool isStandard = true;

        // Extract variable name and initial value from declaration
        if (node.Declaration?.Variables.Count > 0)
        {
            if (node.Declaration.Variables.Count > 1)
            {
                isStandard = false; // Multiple variables — non-standard
            }
            var decl = node.Declaration.Variables[0];
            varName = decl.Identifier.Text;
            if (decl.Initializer != null)
            {
                from = ConvertExpression(decl.Initializer.Value);
            }
        }
        else
        {
            isStandard = false; // No variable declaration — non-standard
        }

        // Extract upper bound from condition
        if (node.Condition is BinaryExpressionSyntax binExpr)
        {
            to = ConvertExpression(binExpr.Right);

            // Calor loops are inclusive (<=), so adjust for exclusive C# bounds
            if (binExpr.OperatorToken.IsKind(SyntaxKind.LessThanToken))
            {
                to = new BinaryOperationNode(TextSpan.Empty, Ast.BinaryOperator.Subtract, to, new IntLiteralNode(TextSpan.Empty, 1));
            }
            else if (binExpr.OperatorToken.IsKind(SyntaxKind.GreaterThanToken))
            {
                to = new BinaryOperationNode(TextSpan.Empty, Ast.BinaryOperator.Add, to, new IntLiteralNode(TextSpan.Empty, 1));
            }
        }
        else
        {
            isStandard = false; // No binary condition — non-standard
        }

        // Extract step from incrementors
        if (node.Incrementors.Count > 1)
        {
            isStandard = false; // Multiple incrementors — non-standard
        }
        if (node.Incrementors.Count > 0)
        {
            var incrementor = node.Incrementors[0];
            if (incrementor is PostfixUnaryExpressionSyntax postfix)
            {
                step = postfix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken)
                    ? new IntLiteralNode(TextSpan.Empty, 1)
                    : new IntLiteralNode(TextSpan.Empty, -1);
            }
            else if (incrementor is PrefixUnaryExpressionSyntax prefix)
            {
                step = prefix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken)
                    ? new IntLiteralNode(TextSpan.Empty, 1)
                    : new IntLiteralNode(TextSpan.Empty, -1);
            }
            else if (incrementor is AssignmentExpressionSyntax assignment)
            {
                step = ConvertExpression(assignment.Right);
            }
        }

        var body = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        // Fall back to while loop for non-standard for patterns
        if (!isStandard)
        {
            var whileBody = new List<StatementNode>();

            // Prepend initializer(s) directly to result (before the while loop)
            if (node.Declaration != null)
            {
                foreach (var variable in node.Declaration.Variables)
                {
                    var initExpr = variable.Initializer != null
                        ? ConvertExpression(variable.Initializer.Value)
                        : new ReferenceNode(span, "default");
                    result.Add(new BindStatementNode(
                        span, variable.Identifier.Text,
                        TypeMapper.CSharpToCalor(node.Declaration.Type.ToString()),
                        true, initExpr, new AttributeCollection()));
                }
            }
            else if (node.Initializers.Count > 0)
            {
                foreach (var init in node.Initializers)
                {
                    var initStmt = ConvertExpressionToStatement(init, span);
                    if (initStmt != null) result.Add(initStmt);
                }
            }

            // Body + incrementors
            whileBody.AddRange(body);
            foreach (var inc in node.Incrementors)
            {
                var incStmt = ConvertExpressionToStatement(inc, span);
                if (incStmt != null) whileBody.Add(incStmt);
            }

            var condition = node.Condition != null
                ? ConvertExpression(node.Condition)
                : new BoolLiteralNode(span, true);

            result.Add(new WhileStatementNode(span, id, condition, whileBody, new AttributeCollection()));
            return result;
        }

        result.Add(new ForStatementNode(
            span,
            id,
            varName ?? "i",
            from ?? new IntLiteralNode(TextSpan.Empty, 0),
            to ?? new IntLiteralNode(TextSpan.Empty, 10),
            step,
            body,
            new AttributeCollection()));
        return result;
    }

    private ForeachStatementNode ConvertForEachStatement(ForEachStatementSyntax node)
    {
        _context.RecordFeatureUsage("foreach");
        _context.IncrementConverted();

        var id = _context.GenerateId("each");
        var varType = TypeMapper.CSharpToCalor(node.Type.ToString());
        var varName = node.Identifier.Text;
        var collection = ConvertExpression(node.Expression);
        var body = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        return new ForeachStatementNode(
            GetTextSpan(node),
            id,
            varName,
            varType,
            collection,
            body,
            new AttributeCollection());
    }

    private WhileStatementNode ConvertWhileStatement(WhileStatementSyntax node)
    {
        _context.RecordFeatureUsage("while");
        _context.IncrementConverted();

        var id = _context.GenerateId("while");
        var condition = ConvertExpression(node.Condition);
        var body = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        return new WhileStatementNode(
            GetTextSpan(node),
            id,
            condition,
            body,
            new AttributeCollection());
    }

    private DoWhileStatementNode ConvertDoWhileStatement(DoStatementSyntax node)
    {
        _context.RecordFeatureUsage("do-while");
        _context.IncrementConverted();

        var id = _context.GenerateId("do");
        var condition = ConvertExpression(node.Condition);
        var body = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        return new DoWhileStatementNode(
            GetTextSpan(node),
            id,
            body,
            condition,
            new AttributeCollection());
    }

    private TryStatementNode ConvertTryStatement(TryStatementSyntax node)
    {
        _context.RecordFeatureUsage("try-catch");
        _context.IncrementConverted();

        var id = _context.GenerateId("try");
        var tryBody = ConvertBlock(node.Block);
        var catches = node.Catches.Select(ConvertCatchClause).ToList();
        var finallyBody = node.Finally != null ? ConvertBlock(node.Finally.Block) : null;

        return new TryStatementNode(
            GetTextSpan(node),
            id,
            tryBody,
            catches,
            finallyBody,
            new AttributeCollection());
    }

    private CatchClauseNode ConvertCatchClause(CatchClauseSyntax node)
    {
        var exceptionType = node.Declaration?.Type.ToString();
        var varName = node.Declaration?.Identifier.Text;
        var filter = node.Filter?.FilterExpression != null
            ? ConvertExpression(node.Filter.FilterExpression)
            : null;
        var body = ConvertBlock(node.Block);

        return new CatchClauseNode(
            GetTextSpan(node),
            exceptionType,
            varName,
            filter,
            body,
            new AttributeCollection());
    }

    private UsingStatementNode ConvertUsingStatement(UsingStatementSyntax node)
    {
        _context.RecordFeatureUsage("using-statement");
        _context.IncrementConverted();

        string? variableName = null;
        string? variableType = null;
        ExpressionNode resource;

        // Handle using with declaration: using (var reader = new StreamReader(...))
        if (node.Declaration != null)
        {
            variableType = node.Declaration.Type.IsVar
                ? null
                : TypeMapper.CSharpToCalor(node.Declaration.Type.ToString());

            if (node.Declaration.Variables.Count > 0)
            {
                var variable = node.Declaration.Variables[0];
                variableName = variable.Identifier.ValueText;
                resource = variable.Initializer != null
                    ? ConvertExpression(variable.Initializer.Value)
                    : new ReferenceNode(GetTextSpan(variable), variableName);
            }
            else
            {
                resource = new ReferenceNode(GetTextSpan(node), "unknown");
            }
        }
        // Handle using with expression: using (expression)
        else if (node.Expression != null)
        {
            resource = ConvertExpression(node.Expression);
        }
        else
        {
            resource = new ReferenceNode(GetTextSpan(node), "unknown");
        }

        var body = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        return new UsingStatementNode(
            GetTextSpan(node),
            _context.GenerateId("use"),
            variableName,
            variableType,
            resource,
            body);
    }

    private ThrowStatementNode ConvertThrowStatement(ThrowStatementSyntax node)
    {
        _context.IncrementConverted();

        var exception = node.Expression != null
            ? ConvertExpression(node.Expression)
            : null;

        return new ThrowStatementNode(GetTextSpan(node), exception);
    }

    private StatementNode ConvertBlockAsStatement(BlockSyntax block)
    {
        var statements = ConvertBlock(block);
        // Return the first statement or a placeholder
        if (statements.Count > 0)
        {
            return statements[0];
        }
        return new CallStatementNode(
            GetTextSpan(block),
            "noop",
            fallible: false,
            Array.Empty<ExpressionNode>(),
            new AttributeCollection());
    }

    private MatchStatementNode ConvertSwitchStatement(SwitchStatementSyntax node)
    {
        _context.RecordFeatureUsage("switch");
        _context.IncrementConverted();

        var id = _context.GenerateId("match");
        var target = ConvertExpression(node.Expression);
        var cases = new List<MatchCaseNode>();

        // Infer enum type prefix from qualified case labels
        var enumTypePrefix = InferEnumTypePrefixFromSwitchSections(node.Sections);

        foreach (var section in node.Sections)
        {
            foreach (var label in section.Labels)
            {
                PatternNode pattern = label switch
                {
                    CaseSwitchLabelSyntax caseLabel => ConvertCaseLabelWithEnumPrefix(caseLabel, enumTypePrefix),
                    DefaultSwitchLabelSyntax => new WildcardPatternNode(GetTextSpan(label)),
                    _ => new WildcardPatternNode(GetTextSpan(label))
                };

                var body = section.Statements
                    .Where(s => !(s is BreakStatementSyntax))
                    .Select(ConvertStatement)
                    .Where(s => s != null)
                    .Cast<StatementNode>()
                    .ToList();

                cases.Add(new MatchCaseNode(GetTextSpan(section), pattern, guard: null, body));
            }
        }

        return new MatchStatementNode(GetTextSpan(node), id, target, cases, new AttributeCollection());
    }

    private MatchExpressionNode ConvertSwitchExpression(SwitchExpressionSyntax node)
    {
        _context.RecordFeatureUsage("switch-expression");
        _context.IncrementConverted();

        var id = _context.GenerateId("match");
        var target = ConvertExpression(node.GoverningExpression);
        var cases = new List<MatchCaseNode>();

        // Infer the enum type prefix from qualified constant patterns (e.g., MyEnum.Value1).
        // This is used to qualify bare identifiers (e.g., Value1 from 'using static MyEnum')
        // so the generated C# compiles without needing the using static directive.
        var enumTypePrefix = InferEnumTypePrefixFromArms(node.Arms);

        foreach (var arm in node.Arms)
        {
            var pattern = ConvertSwitchArmPattern(arm.Pattern, enumTypePrefix);
            ExpressionNode? guard = arm.WhenClause != null
                ? ConvertExpression(arm.WhenClause.Condition)
                : null;

            var body = new List<StatementNode>
            {
                new ReturnStatementNode(GetTextSpan(arm.Expression), ConvertExpression(arm.Expression))
            };

            cases.Add(new MatchCaseNode(GetTextSpan(arm), pattern, guard, body));
        }

        return new MatchExpressionNode(GetTextSpan(node), id, target, cases, new AttributeCollection());
    }

    /// <summary>
    /// Converts a case label, qualifying bare identifiers with an enum type prefix if available.
    /// </summary>
    private PatternNode ConvertCaseLabelWithEnumPrefix(CaseSwitchLabelSyntax caseLabel, string? enumTypePrefix)
    {
        if (enumTypePrefix != null
            && caseLabel.Value is IdentifierNameSyntax identifier
            && identifier.Identifier.Text.Length > 0
            && char.IsUpper(identifier.Identifier.Text[0]))
        {
            var qualifiedName = $"{enumTypePrefix}.{identifier.Identifier.Text}";
            return new LiteralPatternNode(GetTextSpan(caseLabel), new ReferenceNode(GetTextSpan(caseLabel), qualifiedName));
        }

        return new LiteralPatternNode(GetTextSpan(caseLabel), ConvertExpression(caseLabel.Value));
    }

    /// <summary>
    /// Infers the enum type prefix from qualified case labels in switch statement sections.
    /// </summary>
    private static string? InferEnumTypePrefixFromSwitchSections(SyntaxList<SwitchSectionSyntax> sections)
    {
        foreach (var section in sections)
        {
            foreach (var label in section.Labels)
            {
                if (label is CaseSwitchLabelSyntax { Value: MemberAccessExpressionSyntax memberAccess })
                {
                    return memberAccess.Expression.ToString();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Infers the enum type prefix from qualified member access patterns in switch arms.
    /// If any arm uses EnumType.Value, returns "EnumType"; otherwise null.
    /// </summary>
    private static string? InferEnumTypePrefixFromArms(SeparatedSyntaxList<SwitchExpressionArmSyntax> arms)
    {
        foreach (var arm in arms)
        {
            if (arm.Pattern is ConstantPatternSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
            {
                // Found a qualified reference like EnumType.Value — extract the type
                return memberAccess.Expression.ToString();
            }
        }
        return null;
    }

    /// <summary>
    /// Converts a switch arm pattern, optionally qualifying bare identifiers with an enum type prefix.
    /// </summary>
    private PatternNode ConvertSwitchArmPattern(PatternSyntax pattern, string? enumTypePrefix)
    {
        // If the pattern is a bare identifier constant and we have an inferred enum type, qualify it
        if (enumTypePrefix != null
            && pattern is ConstantPatternSyntax { Expression: IdentifierNameSyntax identifier }
            && char.IsUpper(identifier.Identifier.Text[0]))
        {
            var qualifiedName = $"{enumTypePrefix}.{identifier.Identifier.Text}";
            return new LiteralPatternNode(GetTextSpan(pattern), new ReferenceNode(GetTextSpan(pattern), qualifiedName));
        }

        return ConvertPattern(pattern);
    }

    private PatternNode ConvertPattern(PatternSyntax pattern)
    {
        var span = GetTextSpan(pattern);

        return pattern switch
        {
            // Discard pattern: _
            DiscardPatternSyntax => new WildcardPatternNode(span),

            // Constant pattern: 1, "hello", null
            ConstantPatternSyntax constant => new LiteralPatternNode(span, ConvertExpression(constant.Expression)),

            // Var pattern: var x
            VarPatternSyntax varPattern when varPattern.Designation is SingleVariableDesignationSyntax single =>
                new VarPatternNode(span, single.Identifier.Text),

            // Declaration pattern: string s, Type name
            DeclarationPatternSyntax declPattern when declPattern.Designation is SingleVariableDesignationSyntax singleDecl =>
                new VarPatternNode(span, singleDecl.Identifier.Text),

            // Relational pattern: > 0, < 100, >= 10, <= 50
            RelationalPatternSyntax relPattern => ConvertRelationalPattern(relPattern),

            // Type pattern: string, int (without variable)
            TypePatternSyntax typePattern =>
                new LiteralPatternNode(span, new ReferenceNode(span, typePattern.Type.ToString())),

            // Property pattern: { Length: > 5 }
            RecursivePatternSyntax recursivePattern => ConvertRecursivePattern(recursivePattern),

            // Binary patterns: and, or
            BinaryPatternSyntax binaryPattern => ConvertBinaryPattern(binaryPattern),

            // Unary pattern: not X
            UnaryPatternSyntax { OperatorToken.Text: "not" } unaryPattern =>
                new NegatedPatternNode(span, ConvertPattern(unaryPattern.Pattern)),

            // Parenthesized pattern: (pattern)
            ParenthesizedPatternSyntax parenPattern => ConvertPattern(parenPattern.Pattern),

            // Default fallback: use wildcard to ensure valid Calor
            _ => HandleUnsupportedPattern(pattern, "unknown-pattern")
        };
    }

    private RelationalPatternNode ConvertRelationalPattern(RelationalPatternSyntax relPattern)
    {
        var span = GetTextSpan(relPattern);
        var value = ConvertExpression(relPattern.Expression);

        // Convert C# operator token to Calor operator string
        var opString = relPattern.OperatorToken.Kind() switch
        {
            SyntaxKind.LessThanToken => "lt",
            SyntaxKind.LessThanEqualsToken => "lte",
            SyntaxKind.GreaterThanToken => "gt",
            SyntaxKind.GreaterThanEqualsToken => "gte",
            _ => relPattern.OperatorToken.Text
        };

        return new RelationalPatternNode(span, opString, value);
    }

    private PatternNode ConvertBinaryPattern(BinaryPatternSyntax binaryPattern)
    {
        var span = GetTextSpan(binaryPattern);
        var left = ConvertPattern(binaryPattern.Left);
        var right = ConvertPattern(binaryPattern.Right);

        return binaryPattern.OperatorToken.Kind() switch
        {
            SyntaxKind.OrKeyword => new OrPatternNode(span, left, right),
            SyntaxKind.AndKeyword => new AndPatternNode(span, left, right),
            _ => HandleUnsupportedPattern(binaryPattern, $"binary pattern ({binaryPattern.OperatorToken.Text})")
        };
    }

    private PatternNode ConvertRecursivePattern(RecursivePatternSyntax pattern)
    {
        var span = GetTextSpan(pattern);

        // Handle property pattern: { Length: > 5 }
        if (pattern.PropertyPatternClause != null)
        {
            var typeName = pattern.Type?.ToString();
            var matches = new List<PropertyMatchNode>();

            foreach (var subpattern in pattern.PropertyPatternClause.Subpatterns)
            {
                if (subpattern.NameColon != null)
                {
                    var propName = subpattern.NameColon.Name.Identifier.Text;
                    var propPattern = ConvertPattern(subpattern.Pattern);
                    matches.Add(new PropertyMatchNode(GetTextSpan(subpattern), propName, propPattern));
                }
            }

            return new PropertyPatternNode(span, typeName, matches);
        }

        // Handle positional pattern: Point(x, y)
        if (pattern.PositionalPatternClause != null)
        {
            var typeName = pattern.Type?.ToString() ?? "";
            var patterns = pattern.PositionalPatternClause.Subpatterns
                .Select(sp => ConvertPattern(sp.Pattern))
                .ToList();

            return new PositionalPatternNode(span, typeName, patterns);
        }

        // Fallback: type pattern with no destructuring
        if (pattern.Type != null)
        {
            var designation = pattern.Designation as SingleVariableDesignationSyntax;
            if (designation != null)
            {
                return new VarPatternNode(span, designation.Identifier.Text);
            }
            // Type-only pattern (e.g., "string" in "case string:") - emit as type reference
            return new LiteralPatternNode(span, new ReferenceNode(span, pattern.Type.ToString()));
        }

        // Complex recursive pattern without clear type - use wildcard fallback
        return HandleUnsupportedPattern(pattern, "complex-recursive-pattern");
    }

    private PatternNode HandleUnsupportedPattern(PatternSyntax pattern, string description)
    {
        var span = GetTextSpan(pattern);
        var lineSpan = pattern.GetLocation().GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        var suggestion = "Simplify pattern or use if-else with explicit conditions";

        _context.AddWarning(
            $"Unsupported pattern [{description}]: will match any value (wildcard)",
            feature: description,
            line: line,
            column: lineSpan.StartLinePosition.Character + 1);

        // Record for explanation output
        _context.RecordUnsupportedFeature(description, pattern.ToString(), line, suggestion);

        // Emit as wildcard pattern - this is valid Calor but changes semantics
        // The original pattern is lost, so the case will match more broadly
        return new WildcardPatternNode(span);
    }

    private ExpressionNode ConvertExpression(ExpressionSyntax expression)
    {
        _context.Stats.ExpressionsConverted++;

        try
        {
            return expression switch
            {
                LiteralExpressionSyntax literal => ConvertLiteral(literal),
                IdentifierNameSyntax identifier => new ReferenceNode(GetTextSpan(identifier), identifier.Identifier.ValueText),
                BinaryExpressionSyntax binary => ConvertBinaryExpression(binary),
                PrefixUnaryExpressionSyntax addrOf when addrOf.IsKind(SyntaxKind.AddressOfExpression) => ConvertAddressOfExpression(addrOf),
                PrefixUnaryExpressionSyntax deref when deref.IsKind(SyntaxKind.PointerIndirectionExpression) => ConvertPointerDereferenceExpression(deref),
                PrefixUnaryExpressionSyntax indexFromEnd when indexFromEnd.IsKind(SyntaxKind.IndexExpression) => ConvertIndexFromEndExpression(indexFromEnd),
                PrefixUnaryExpressionSyntax prefix => ConvertPrefixUnaryExpression(prefix),
                PostfixUnaryExpressionSyntax postfix => ConvertPostfixUnaryExpression(postfix),
                ParenthesizedExpressionSyntax paren => ConvertExpression(paren.Expression),
                InvocationExpressionSyntax invocation => ConvertInvocationExpression(invocation),
                MemberAccessExpressionSyntax memberAccess => ConvertMemberAccessExpression(memberAccess),
                ObjectCreationExpressionSyntax objCreation => ConvertObjectCreation(objCreation),
                ThisExpressionSyntax => new ThisExpressionNode(GetTextSpan(expression)),
                BaseExpressionSyntax => new BaseExpressionNode(GetTextSpan(expression)),
                ConditionalExpressionSyntax conditional => ConvertConditionalExpression(conditional),
                ArrayCreationExpressionSyntax arrayCreation => ConvertArrayCreation(arrayCreation),
                ImplicitArrayCreationExpressionSyntax implicitArray => ConvertImplicitArrayCreation(implicitArray),
                ElementAccessExpressionSyntax elementAccess => ConvertElementAccess(elementAccess),
                LambdaExpressionSyntax lambda => ConvertLambdaExpression(lambda),
                AwaitExpressionSyntax awaitExpr => ConvertAwaitExpression(awaitExpr),
                InterpolatedStringExpressionSyntax interpolated => ConvertInterpolatedString(interpolated),
                ConditionalAccessExpressionSyntax condAccess => ConvertConditionalAccess(condAccess),
                CastExpressionSyntax cast => ConvertCastExpression(cast),
                IsPatternExpressionSyntax isPattern => ConvertIsPatternExpression(isPattern),
                CollectionExpressionSyntax collection => ConvertCollectionExpression(collection),
                ImplicitObjectCreationExpressionSyntax implicitNew => ConvertImplicitObjectCreation(implicitNew),
                SwitchExpressionSyntax switchExpr => ConvertSwitchExpression(switchExpr),
                ThrowExpressionSyntax throwExpr => ConvertThrowExpression(throwExpr),
                DefaultExpressionSyntax defaultExpr => ConvertDefaultExpression(defaultExpr),
                AnonymousObjectCreationExpressionSyntax anonObj => ConvertAnonymousObjectCreation(anonObj),
                QueryExpressionSyntax queryExpr => ConvertQueryExpression(queryExpr),
                InitializerExpressionSyntax initExpr => ConvertInitializerExpression(initExpr),
                TypeOfExpressionSyntax typeOf => new TypeOfExpressionNode(GetTextSpan(typeOf), TypeMapper.CSharpToCalor(typeOf.Type.ToString())),
                GenericNameSyntax generic => new ReferenceNode(GetTextSpan(generic),
                    $"{generic.Identifier.Text}<{string.Join(", ", generic.TypeArgumentList.Arguments.Select(a => TypeMapper.CSharpToCalor(a.ToString())))}>"),
                PredefinedTypeSyntax predefined => new ReferenceNode(GetTextSpan(predefined), predefined.Keyword.Text),
                DeclarationExpressionSyntax declExpr => ConvertDeclarationExpression(declExpr),
                AssignmentExpressionSyntax assignExpr => ConvertAssignmentExpression(assignExpr),
                TupleExpressionSyntax tupleExpr => ConvertTupleExpression(tupleExpr),
                StackAllocArrayCreationExpressionSyntax stackAlloc => ConvertStackAllocExpression(stackAlloc),
                ImplicitStackAllocArrayCreationExpressionSyntax implicitStackAlloc => ConvertImplicitStackAllocExpression(implicitStackAlloc),
                SizeOfExpressionSyntax sizeOf => ConvertSizeOfExpression(sizeOf),
                RangeExpressionSyntax rangeExpr => ConvertRangeExpression(rangeExpr),
                WithExpressionSyntax withExpr => ConvertWithExpression(withExpr),
                CheckedExpressionSyntax checkedExpr => ConvertExpression(checkedExpr.Expression),
                _ => CreateFallbackExpression(expression, "unknown-expression")
            };
        }
        catch (Exception) when (_context.ShouldPreserveCSharp)
        {
            _context.IncrementSkipped();
            return new FallbackExpressionNode(GetTextSpan(expression), expression.ToFullString(), "conversion-error", null);
        }
    }

    private ExpressionNode ConvertLiteral(LiteralExpressionSyntax literal)
    {
        return literal.Kind() switch
        {
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is int intVal =>
                CreateIntLiteralNode(literal, intVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is double doubleVal =>
                new FloatLiteralNode(GetTextSpan(literal), doubleVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is float floatVal =>
                new FloatLiteralNode(GetTextSpan(literal), floatVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is decimal decVal =>
                new DecimalLiteralNode(GetTextSpan(literal), decVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is long longVal =>
                CreateIntLiteralNode(literal, longVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is uint uintVal =>
                new IntLiteralNode(GetTextSpan(literal), uintVal,
                    literal.Token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase),
                    isUnsigned: true, uintVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is ulong ulongVal =>
                new IntLiteralNode(GetTextSpan(literal), unchecked((long)ulongVal),
                    literal.Token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase),
                    isUnsigned: true, ulongVal),
            SyntaxKind.StringLiteralExpression =>
                new StringLiteralNode(GetTextSpan(literal), literal.Token.ValueText),
            SyntaxKind.CharacterLiteralExpression =>
                new StringLiteralNode(GetTextSpan(literal), literal.Token.ValueText),
            SyntaxKind.TrueLiteralExpression =>
                new BoolLiteralNode(GetTextSpan(literal), true),
            SyntaxKind.FalseLiteralExpression =>
                new BoolLiteralNode(GetTextSpan(literal), false),
            SyntaxKind.NullLiteralExpression =>
                new ReferenceNode(GetTextSpan(literal), "null"),
            SyntaxKind.DefaultLiteralExpression =>
                new ReferenceNode(GetTextSpan(literal), "default"),
            _ => CreateFallbackExpression(literal, "unknown-literal")
        };
    }

    private IntLiteralNode CreateIntLiteralNode(LiteralExpressionSyntax literal, long value)
    {
        var isHex = literal.Token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (isHex)
            return new IntLiteralNode(GetTextSpan(literal), value, isHex: true, isUnsigned: false, (ulong)value);
        return new IntLiteralNode(GetTextSpan(literal), value);
    }

    private ExpressionNode ConvertBinaryExpression(BinaryExpressionSyntax binary)
    {
        if (binary.IsKind(SyntaxKind.AsExpression))
        {
            _context.RecordFeatureUsage("as");
            var left = ConvertExpression(binary.Left);
            var typeName = TypeMapper.CSharpToCalor(binary.Right.ToString());
            return new TypeOperationNode(GetTextSpan(binary), TypeOp.As, left, typeName);
        }
        if (binary.IsKind(SyntaxKind.IsExpression))
        {
            _context.RecordFeatureUsage("is");
            var left = ConvertExpression(binary.Left);
            var typeName = TypeMapper.CSharpToCalor(binary.Right.ToString());
            return new TypeOperationNode(GetTextSpan(binary), TypeOp.Is, left, typeName);
        }

        // Handle null-coalescing with throw: x ?? throw new E(args) → hoist null guard, return x
        if (binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Right is ThrowExpressionSyntax throwExpr)
        {
            _context.RecordFeatureUsage("null-coalescing-throw");
            _context.IncrementConverted();
            var left = ConvertExpression(binary.Left);
            var exceptionExpr = ConvertExpression(throwExpr.Expression);

            // If the left side is not a simple reference (e.g., a method call),
            // hoist it to a temp variable to avoid double evaluation.
            var valueRef = left;
            if (left is not ReferenceNode)
            {
                var tempName = _context.GenerateId("_nct");
                _pendingStatements.Add(new BindStatementNode(
                    left.Span, tempName, null, false, left, new AttributeCollection()));
                valueRef = new ReferenceNode(left.Span, tempName);
            }

            var nullCheck = new BinaryOperationNode(
                GetTextSpan(binary), BinaryOperator.Equal,
                valueRef, new ReferenceNode(GetTextSpan(binary), "null"));
            var throwStmt = new ThrowStatementNode(GetTextSpan(throwExpr), exceptionExpr);
            var guard = new IfStatementNode(
                GetTextSpan(binary),
                _context.GenerateId("if"),
                nullCheck,
                new List<StatementNode> { throwStmt },
                Array.Empty<ElseIfClauseNode>(),
                null,
                new AttributeCollection());

            _pendingStatements.Add(guard);
            return valueRef;
        }

        // Handle null-coalescing operator: x ?? y → (?? x y)
        if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            _context.RecordFeatureUsage("null-coalescing");
            var left = ConvertExpression(binary.Left);
            var right = ConvertExpression(binary.Right);

            // If the left side is a NullConditionalNode (e.g., value?.Length.ToString()),
            // hoist it to a temp bind to avoid emitting ?.  inside a Lisp expression,
            // which the parser cannot handle.
            if (left is NullConditionalNode)
            {
                var tempId = _context.GenerateId("_nc", "Tmp");
                _pendingStatements.Add(new BindStatementNode(
                    GetTextSpan(binary), tempId, null, false, left, new AttributeCollection()));
                left = new ReferenceNode(GetTextSpan(binary), tempId);
            }

            return new NullCoalesceNode(GetTextSpan(binary), left, right);
        }

        var leftExpr = ConvertExpression(binary.Left);
        var rightExpr = ConvertExpression(binary.Right);
        var op = binary.OperatorToken.Text;
        var binaryOp = BinaryOperatorExtensions.FromString(op) ?? BinaryOperator.Add;

        return new BinaryOperationNode(GetTextSpan(binary), binaryOp, leftExpr, rightExpr);
    }

    private ExpressionNode ConvertIsPatternExpression(IsPatternExpressionSyntax isPattern)
    {
        // Convert "x is null" to "(== x null)"
        // Convert "x is not null" to "(!= x null)"
        var left = ConvertExpression(isPattern.Expression);

        return isPattern.Pattern switch
        {
            ConstantPatternSyntax constant =>
                // "x is null" or "x is value"
                new BinaryOperationNode(
                    GetTextSpan(isPattern),
                    BinaryOperator.Equal,
                    left,
                    ConvertExpression(constant.Expression)),
            UnaryPatternSyntax { OperatorToken.Text: "not", Pattern: ConstantPatternSyntax notConstant } =>
                // "x is not null" or "x is not value"
                new BinaryOperationNode(
                    GetTextSpan(isPattern),
                    BinaryOperator.NotEqual,
                    left,
                    ConvertExpression(notConstant.Expression)),
            TypePatternSyntax typePattern =>
                // "x is SomeType" - convert to type operation
                new TypeOperationNode(GetTextSpan(isPattern), TypeOp.Is, left,
                    TypeMapper.CSharpToCalor(typePattern.Type.ToString())),
            UnaryPatternSyntax { OperatorToken.Text: "not", Pattern: TypePatternSyntax notType } =>
                // "x is not SomeType" - negate type check
                new UnaryOperationNode(GetTextSpan(isPattern), UnaryOperator.Not,
                    new TypeOperationNode(GetTextSpan(isPattern), TypeOp.Is, left,
                        TypeMapper.CSharpToCalor(notType.Type.ToString()))),
            DeclarationPatternSyntax declPattern =>
                ConvertDeclarationPattern(isPattern, left, declPattern),
            RelationalPatternSyntax relPattern =>
                // "x is > 5" → "(> x 5)"
                ConvertPatternToExpression(relPattern, left),
            BinaryPatternSyntax binaryPattern =>
                // "x is > 5 or < 3" → "(|| (> x 5) (< x 3))"
                ConvertPatternToExpression(binaryPattern, left),
            UnaryPatternSyntax { OperatorToken.Text: "not" } unaryPattern =>
                // "x is not > 5" → "(! (> x 5))"
                ConvertPatternToExpression(unaryPattern, left),
            _ =>
                // For other patterns, create a fallback expression
                CreateFallbackExpression(isPattern, "complex-is-pattern")
        };
    }

    /// <summary>
    /// Converts a pattern used in an <c>is</c> expression into an equivalent boolean expression.
    /// For example, <c>x is > 5 or < 3</c> becomes <c>(|| (> x 5) (< x 3))</c>.
    /// </summary>
    private ExpressionNode ConvertPatternToExpression(PatternSyntax pattern, ExpressionNode subject)
    {
        return pattern switch
        {
            RelationalPatternSyntax relPattern =>
                new BinaryOperationNode(
                    GetTextSpan(relPattern),
                    relPattern.OperatorToken.Kind() switch
                    {
                        SyntaxKind.LessThanToken => BinaryOperator.LessThan,
                        SyntaxKind.LessThanEqualsToken => BinaryOperator.LessOrEqual,
                        SyntaxKind.GreaterThanToken => BinaryOperator.GreaterThan,
                        SyntaxKind.GreaterThanEqualsToken => BinaryOperator.GreaterOrEqual,
                        _ => BinaryOperator.Equal
                    },
                    subject,
                    ConvertExpression(relPattern.Expression)),
            BinaryPatternSyntax binaryPattern =>
                new BinaryOperationNode(
                    GetTextSpan(binaryPattern),
                    binaryPattern.OperatorToken.Kind() switch
                    {
                        SyntaxKind.OrKeyword => BinaryOperator.Or,
                        _ => BinaryOperator.And
                    },
                    ConvertPatternToExpression(binaryPattern.Left, subject),
                    ConvertPatternToExpression(binaryPattern.Right, subject)),
            UnaryPatternSyntax { OperatorToken.Text: "not" } unaryPattern =>
                new UnaryOperationNode(
                    GetTextSpan(unaryPattern),
                    UnaryOperator.Not,
                    ConvertPatternToExpression(unaryPattern.Pattern, subject)),
            ConstantPatternSyntax constant =>
                new BinaryOperationNode(
                    GetTextSpan(constant),
                    BinaryOperator.Equal,
                    subject,
                    ConvertExpression(constant.Expression)),
            ParenthesizedPatternSyntax parenPattern =>
                ConvertPatternToExpression(parenPattern.Pattern, subject),
            _ =>
                CreateFallbackExpression(pattern, "complex-is-pattern")
        };
    }

    /// <summary>
    /// Converts `out var x` or `out Type x` declaration expressions.
    /// Hoists a variable declaration via _pendingStatements, returns a reference to the variable.
    /// </summary>
    private ExpressionNode ConvertDeclarationExpression(DeclarationExpressionSyntax declExpr)
    {
        _context.RecordFeatureUsage("out-var");

        if (declExpr.Designation is SingleVariableDesignationSyntax singleVar)
        {
            var varName = singleVar.Identifier.Text;
            var typeName = declExpr.Type.IsVar
                ? (string?)null
                : TypeMapper.CSharpToCalor(declExpr.Type.ToString());

            // Hoist a variable declaration: §B{varName:Type} default
            _pendingStatements.Add(new BindStatementNode(
                GetTextSpan(declExpr),
                varName,
                typeName,
                isMutable: true,
                initializer: null,
                new AttributeCollection()));

            return new ReferenceNode(GetTextSpan(declExpr), varName);
        }

        // Discard pattern: `out _`
        if (declExpr.Designation is DiscardDesignationSyntax)
        {
            return new ReferenceNode(GetTextSpan(declExpr), "_");
        }

        // Parenthesized variable designation: var (a, b) in expression context
        if (declExpr.Designation is ParenthesizedVariableDesignationSyntax parenVar)
        {
            _context.RecordFeatureUsage("tuple-deconstruction");
            // Hoist individual variable declarations
            foreach (var variable in parenVar.Variables)
            {
                if (variable is SingleVariableDesignationSyntax sv)
                {
                    _pendingStatements.Add(new BindStatementNode(
                        GetTextSpan(declExpr),
                        sv.Identifier.Text,
                        null,
                        isMutable: true,
                        initializer: null,
                        new AttributeCollection()));
                }
            }
            // Return a reference to the first variable as the expression value
            var firstName = parenVar.Variables.OfType<SingleVariableDesignationSyntax>().FirstOrDefault()?.Identifier.Text ?? "_";
            return new ReferenceNode(GetTextSpan(declExpr), firstName);
        }

        return CreateFallbackExpression(declExpr, "complex-declaration");
    }

    /// <summary>
    /// Converts "x is SomeType varName" to a type check expression and hoists a variable binding.
    /// The type check (is) is returned as the expression value for use in conditions.
    /// The variable binding (§B{varName} (cast SomeType x)) is hoisted via _pendingStatements
    /// so it appears before the containing statement.
    /// </summary>
    private ExpressionNode ConvertDeclarationPattern(
        IsPatternExpressionSyntax isPattern,
        ExpressionNode left,
        DeclarationPatternSyntax declPattern)
    {
        var calorType = TypeMapper.CSharpToCalor(declPattern.Type.ToString());

        // Hoist a variable binding: §B{varName} (cast Type expr)
        if (declPattern.Designation is SingleVariableDesignationSyntax singleVar)
        {
            var varName = singleVar.Identifier.Text;
            var castExpr = new TypeOperationNode(GetTextSpan(isPattern), TypeOp.Cast, left, calorType);
            _pendingStatements.Add(new BindStatementNode(
                GetTextSpan(isPattern),
                varName,
                calorType,
                isMutable: false,
                castExpr,
                new AttributeCollection()));
        }

        // Return the type check as the expression
        return new TypeOperationNode(GetTextSpan(isPattern), TypeOp.Is, left, calorType);
    }

    private ExpressionNode ConvertCollectionExpression(CollectionExpressionSyntax collection)
    {
        // Convert C# 12 collection expressions: [] or [1, 2, 3]
        // Empty collection: output as empty list creation
        // Using "default" was wrong because default for reference types is null, not empty collection
        if (collection.Elements.Count == 0)
        {
            var emptyId = _context.GenerateId("list");
            var emptyName = _context.GenerateId("list");
            return new ListCreationNode(
                GetTextSpan(collection),
                emptyId,
                emptyName,
                "object",
                Array.Empty<ExpressionNode>(),
                new AttributeCollection());
        }

        // Convert collection expression to ArrayCreationNode
        // This allows proper round-tripping through Calor
        var id = _context.GenerateId("arr");
        var name = _context.GenerateId("arr");

        var initializer = new List<ExpressionNode>();
        string? elementType = null;

        foreach (var element in collection.Elements)
        {
            if (element is ExpressionElementSyntax exprElement)
            {
                var converted = ConvertExpression(exprElement.Expression);
                initializer.Add(converted);

                // Try to infer element type from first element
                if (elementType == null)
                {
                    elementType = InferTypeFromExpression(exprElement.Expression);
                }
            }
            else if (element is SpreadElementSyntax spread)
            {
                // Spread-only collection [..expr] → convert to expr.ToList()
                if (collection.Elements.Count == 1)
                {
                    _context.RecordFeatureUsage("collection-spread");
                    var spreadTarget = spread.Expression.ToString();
                    return new CallExpressionNode(GetTextSpan(collection),
                        $"{spreadTarget}.ToList", Array.Empty<ExpressionNode>());
                }
                // Mixed spread [1, 2, ..expr] — not yet supported
                return CreateFallbackExpression(collection, "collection-spread-mixed");
            }
        }

        // Default to "any" if we can't infer the type
        elementType ??= "any";

        return new ArrayCreationNode(
            GetTextSpan(collection),
            id,
            name,
            elementType,
            null, // no explicit size
            initializer,
            new AttributeCollection());
    }

    private string? InferTypeFromExpression(ExpressionSyntax expr)
    {
        return expr switch
        {
            LiteralExpressionSyntax literal => literal.Kind() switch
            {
                SyntaxKind.StringLiteralExpression => "str",
                SyntaxKind.NumericLiteralExpression when literal.Token.Value is int => "i32",
                SyntaxKind.NumericLiteralExpression when literal.Token.Value is long => "i64",
                SyntaxKind.NumericLiteralExpression when literal.Token.Value is float => "f32",
                SyntaxKind.NumericLiteralExpression when literal.Token.Value is double => "f64",
                SyntaxKind.NumericLiteralExpression when literal.Token.Value is decimal => "decimal",
                SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression => "bool",
                SyntaxKind.CharacterLiteralExpression => "char",
                _ => null
            },
            _ => null
        };
    }

    private ExpressionNode ConvertImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax implicitNew)
    {
        // Try to infer the target type from the surrounding syntax context
        var inferredType = InferTargetType(implicitNew);

        // Convert target-typed new: new() or new(args)
        if (implicitNew.ArgumentList == null || implicitNew.ArgumentList.Arguments.Count == 0)
        {
            if (inferredType != null)
            {
                _context.RecordFeatureUsage("target-typed-new");
                _context.IncrementConverted();
                var inits = new List<ObjectInitializerAssignment>();
                if (implicitNew.Initializer != null)
                {
                    foreach (var expr in implicitNew.Initializer.Expressions)
                    {
                        if (expr is AssignmentExpressionSyntax assignment)
                            inits.Add(new ObjectInitializerAssignment(
                                assignment.Left.ToString(), ConvertExpression(assignment.Right)));
                    }
                }
                return new NewExpressionNode(GetTextSpan(implicitNew), inferredType, new List<string>(), new List<ExpressionNode>(), inits);
            }
            return new ReferenceNode(GetTextSpan(implicitNew), "default");
        }

        _context.RecordFeatureUsage("target-typed-new");
        _context.IncrementConverted();
        var typeName = inferredType ?? "object";
        var args = implicitNew.ArgumentList.Arguments
            .Select(a => ConvertExpression(a.Expression)).ToList();
        var initializers = new List<ObjectInitializerAssignment>();
        if (implicitNew.Initializer != null)
        {
            foreach (var expr in implicitNew.Initializer.Expressions)
            {
                if (expr is AssignmentExpressionSyntax assignment)
                    initializers.Add(new ObjectInitializerAssignment(
                        assignment.Left.ToString(), ConvertExpression(assignment.Right)));
            }
        }
        return new NewExpressionNode(GetTextSpan(implicitNew), typeName, new List<string>(), args, initializers);
    }

    /// <summary>
    /// Tries to infer the target type for a target-typed new() expression
    /// by walking up the syntax tree to the declaring context.
    /// </summary>
    private string? InferTargetType(ImplicitObjectCreationExpressionSyntax implicitNew)
    {
        var parent = implicitNew.Parent;

        // Case 1: Variable declaration: Type x = new();
        if (parent is EqualsValueClauseSyntax equalsValue)
        {
            if (equalsValue.Parent is VariableDeclaratorSyntax declarator
                && declarator.Parent is VariableDeclarationSyntax declaration
                && !declaration.Type.IsVar)
            {
                return TypeMapper.CSharpToCalor(declaration.Type.ToString());
            }
            // Property initializer: Type Prop { get; } = new();
            if (equalsValue.Parent is PropertyDeclarationSyntax property)
            {
                return TypeMapper.CSharpToCalor(property.Type.ToString());
            }
        }

        // Case 2: Assignment: x = new();
        if (parent is AssignmentExpressionSyntax)
        {
            // Can't infer type from assignment without semantic model
            return null;
        }

        // Case 3a: Throw statement — throw new("msg");
        // Case 3b: Throw expression — x ?? throw new("msg")
        if (parent is ThrowStatementSyntax or ThrowExpressionSyntax)
        {
            return "Exception";
        }

        // Case 4: Parameter default value — void Foo(Type x = new())
        if (parent is EqualsValueClauseSyntax evc
            && evc.Parent is ParameterSyntax parameter
            && parameter.Type != null)
        {
            return TypeMapper.CSharpToCalor(parameter.Type.ToString());
        }

        // Case 5: Cast expression — (Type)new()
        if (parent is CastExpressionSyntax cast)
        {
            return TypeMapper.CSharpToCalor(cast.Type.ToString());
        }

        // Case 6: Return statement in method — use method return type
        if (parent is ReturnStatementSyntax || parent is ArrowExpressionClauseSyntax)
        {
            // Walk up the syntax tree to find the enclosing member declaration.
            // Bail out if we cross a lambda/local function boundary to avoid
            // inferring the outer method's return type instead of the inner scope's.
            var ancestor = parent;
            while (ancestor != null)
            {
                switch (ancestor)
                {
                    case ParenthesizedLambdaExpressionSyntax:
                    case SimpleLambdaExpressionSyntax:
                    case AnonymousMethodExpressionSyntax:
                        return null; // can't infer across lambda/delegate boundary without semantic model
                    case LocalFunctionStatementSyntax localFunc:
                    {
                        var rt = localFunc.ReturnType.ToString();
                        if (localFunc.Modifiers.Any(SyntaxKind.AsyncKeyword))
                            rt = UnwrapTaskType(rt);
                        return rt is "void" or "var" ? null : TypeMapper.CSharpToCalor(rt);
                    }
                    case MethodDeclarationSyntax method:
                    {
                        var rt = method.ReturnType.ToString();
                        if (method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                            rt = UnwrapTaskType(rt);
                        return rt is "void" or "var" ? null : TypeMapper.CSharpToCalor(rt);
                    }
                    case PropertyDeclarationSyntax property:
                        return TypeMapper.CSharpToCalor(property.Type.ToString());
                    case OperatorDeclarationSyntax op:
                        return TypeMapper.CSharpToCalor(op.ReturnType.ToString());
                    case ConversionOperatorDeclarationSyntax:
                        return null; // conversion operators don't have a named return type to infer from
                }
                ancestor = ancestor.Parent;
            }
            return null;
        }

        return null;
    }

    private ExpressionNode ConvertThrowExpression(ThrowExpressionSyntax throwExpr)
    {
        _context.RecordFeatureUsage("throw-expression");
        _context.IncrementConverted();
        var inner = ConvertExpression(throwExpr.Expression);
        return new ThrowExpressionNode(GetTextSpan(throwExpr), inner);
    }

    private ExpressionNode ConvertDefaultExpression(DefaultExpressionSyntax defaultExpr)
    {
        _context.RecordFeatureUsage("default-expression");
        _context.IncrementConverted();
        var typeName = defaultExpr.Type.ToString();
        return typeName switch
        {
            "int" or "Int32" or "long" or "Int64" or "short" or "byte" => new IntLiteralNode(GetTextSpan(defaultExpr), 0),
            "double" or "float" or "decimal" or "Double" or "Single" => new FloatLiteralNode(GetTextSpan(defaultExpr), 0.0),
            "bool" or "Boolean" => new BoolLiteralNode(GetTextSpan(defaultExpr), false),
            "string" or "String" => new ReferenceNode(GetTextSpan(defaultExpr), "null"),
            _ => new ReferenceNode(GetTextSpan(defaultExpr), "default")
        };
    }

    private ExpressionNode ConvertTupleExpression(TupleExpressionSyntax tuple)
    {
        _context.RecordFeatureUsage("tuple-literal");
        _context.IncrementConverted();
        var elements = tuple.Arguments
            .Select(a => ConvertExpression(a.Expression))
            .ToList();
        return new TupleLiteralNode(GetTextSpan(tuple), elements);
    }

    private ExpressionNode ConvertAssignmentExpression(AssignmentExpressionSyntax assignment)
    {
        // Handle assignment expressions in expression context (e.g., chained: a = b = value)
        // Hoist the assignment to _pendingStatements and return the assigned value
        _context.IncrementConverted();
        var target = ConvertExpression(assignment.Left);
        var value = ConvertExpression(assignment.Right);

        _pendingStatements.Add(new AssignmentStatementNode(
            GetTextSpan(assignment), target, value));

        // Return the value so chained assignments work: a = (b = value) → assign b value, then return value for a
        return value;
    }

    private UnaryOperationNode ConvertPrefixUnaryExpression(PrefixUnaryExpressionSyntax prefix)
    {
        var operand = ConvertExpression(prefix.Operand);
        var op = prefix.OperatorToken.Text;
        var unaryOp = UnaryOperatorExtensions.FromString(op) ?? UnaryOperator.Negate;

        return new UnaryOperationNode(GetTextSpan(prefix), unaryOp, operand);
    }

    private IndexFromEndNode ConvertIndexFromEndExpression(PrefixUnaryExpressionSyntax node)
    {
        _context.RecordFeatureUsage("index-from-end");
        _context.IncrementConverted();
        var operand = ConvertExpression(node.Operand);
        return new IndexFromEndNode(GetTextSpan(node), operand);
    }

    private RangeExpressionNode ConvertRangeExpression(RangeExpressionSyntax rangeExpr)
    {
        _context.RecordFeatureUsage("range-expression");
        _context.IncrementConverted();
        var start = rangeExpr.LeftOperand != null ? ConvertExpression(rangeExpr.LeftOperand) : null;
        var end = rangeExpr.RightOperand != null ? ConvertExpression(rangeExpr.RightOperand) : null;
        return new RangeExpressionNode(GetTextSpan(rangeExpr), start, end);
    }

    private WithExpressionNode ConvertWithExpression(WithExpressionSyntax withExpr)
    {
        _context.RecordFeatureUsage("with-expression");
        _context.IncrementConverted();
        var target = ConvertExpression(withExpr.Expression);
        var assignments = new List<WithPropertyAssignmentNode>();

        foreach (var expr in withExpr.Initializer.Expressions)
        {
            if (expr is AssignmentExpressionSyntax assignment)
            {
                var propName = assignment.Left.ToString();
                var value = ConvertExpression(assignment.Right);
                assignments.Add(new WithPropertyAssignmentNode(GetTextSpan(expr), propName, value));
            }
            else
            {
                // Unexpected expression in with-initializer; convert as a named assignment using the expression text
                var value = ConvertExpression(expr);
                assignments.Add(new WithPropertyAssignmentNode(GetTextSpan(expr), expr.ToString(), value));
            }
        }

        return new WithExpressionNode(GetTextSpan(withExpr), target, assignments);
    }

    private ExpressionNode ConvertPostfixUnaryExpression(PostfixUnaryExpressionSyntax postfix)
    {
        var operand = ConvertExpression(postfix.Operand);

        // Handle null-forgiving operator (!) - just return the operand since Calor doesn't have this concept
        if (postfix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            return operand;
        }

        // Convert postfix ++ and -- when used as sub-expressions:
        // Hoist the increment to a pending statement and return the pre-increment value
        if (postfix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken))
        {
            // Save original value, then increment via pending statement
            var tempName = _context.GenerateId("_pre");
            _pendingStatements.Add(new BindStatementNode(
                GetTextSpan(postfix), tempName, null, false, operand, new AttributeCollection()));
            _pendingStatements.Add(new AssignmentStatementNode(
                GetTextSpan(postfix), operand,
                new BinaryOperationNode(GetTextSpan(postfix), BinaryOperator.Add,
                    new ReferenceNode(GetTextSpan(postfix), tempName),
                    new IntLiteralNode(GetTextSpan(postfix), 1))));
            return new ReferenceNode(GetTextSpan(postfix), tempName);
        }
        if (postfix.OperatorToken.IsKind(SyntaxKind.MinusMinusToken))
        {
            var tempName = _context.GenerateId("_pre");
            _pendingStatements.Add(new BindStatementNode(
                GetTextSpan(postfix), tempName, null, false, operand, new AttributeCollection()));
            _pendingStatements.Add(new AssignmentStatementNode(
                GetTextSpan(postfix), operand,
                new BinaryOperationNode(GetTextSpan(postfix), BinaryOperator.Subtract,
                    new ReferenceNode(GetTextSpan(postfix), tempName),
                    new IntLiteralNode(GetTextSpan(postfix), 1))));
            return new ReferenceNode(GetTextSpan(postfix), tempName);
        }

        // For other postfix operators, use fallback
        return CreateFallbackExpression(postfix, "postfix-operator");
    }

    /// <summary>
    /// Hoists complex expression nodes (§NEW, §LAM, §ARR) from argument lists to temp bindings.
    /// The parser cannot handle these nested inside §C or constructor initializer arguments.
    /// </summary>
    private void HoistComplexArguments(List<ExpressionNode> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is NewExpressionNode newNode)
            {
                var tempName = _context.GenerateId("_new", newNode.TypeName);
                _pendingStatements.Add(new BindStatementNode(
                    args[i].Span, tempName, null, false, args[i], new AttributeCollection()));
                args[i] = new ReferenceNode(args[i].Span, tempName);
            }
            else if (args[i] is LambdaExpressionNode)
            {
                var tempName = _context.GenerateId("_lam");
                _pendingStatements.Add(new BindStatementNode(
                    args[i].Span, tempName, null, false, args[i], new AttributeCollection()));
                args[i] = new ReferenceNode(args[i].Span, tempName);
            }
            else if (args[i] is ArrayCreationNode arrNode)
            {
                var tempName = _context.GenerateId("_arr", arrNode.ElementType);
                _pendingStatements.Add(new BindStatementNode(
                    args[i].Span, tempName, null, false, args[i], new AttributeCollection()));
                args[i] = new ReferenceNode(args[i].Span, tempName);
            }
        }
    }

    private static IReadOnlyList<string?>? ExtractArgumentModifiers(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var modifiers = arguments
            .Select(a => a.RefKindKeyword.IsKind(SyntaxKind.None) ? null : a.RefKindKeyword.ValueText)
            .ToList();
        return modifiers.Any(m => m != null) ? modifiers : null;
    }

    private ExpressionNode ConvertInvocationExpression(InvocationExpressionSyntax invocation)
    {
        var target = invocation.Expression.ToString();
        var args = invocation.ArgumentList.Arguments
            .Select(a => ConvertExpression(a.Expression))
            .ToList();

        // Hoist §NEW, §LAM, §ARR arguments to temp bindings — the parser cannot handle them nested inside §C
        HoistComplexArguments(args);

        var hasNamedArgs = invocation.ArgumentList.Arguments.Any(a => a.NameColon != null);
        var argNames = hasNamedArgs
            ? invocation.ArgumentList.Arguments
                .Select(a => a.NameColon?.Name.Identifier.Text)
                .ToList()
            : null;

        var argModifiers = ExtractArgumentModifiers(invocation.ArgumentList.Arguments);

        // Extract explicit generic type arguments from the invocation.
        // For member access like list.Cast<int>(), the Name is GenericNameSyntax.
        // For direct calls like GenericMethod<int>(), the expression itself is GenericNameSyntax.
        List<string>? typeArguments = null;
        if (invocation.Expression is MemberAccessExpressionSyntax ma && ma.Name is GenericNameSyntax genericMethodName)
        {
            typeArguments = genericMethodName.TypeArgumentList.Arguments
                .Select(a => TypeMapper.CSharpToCalor(a.ToString()))
                .ToList();
            // Rebuild target without the type arguments (e.g., "list.Cast" instead of "list.Cast<int>")
            target = ma.Expression + "." + genericMethodName.Identifier.Text;
        }
        else if (invocation.Expression is GenericNameSyntax directGenericName)
        {
            typeArguments = directGenericName.TypeArgumentList.Arguments
                .Select(a => TypeMapper.CSharpToCalor(a.ToString()))
                .ToList();
            target = directGenericName.Identifier.Text;
        }

        // Try to convert common string methods to native StringOperationNode
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var targetExpr = ConvertExpression(memberAccess.Expression);
            var targetStr = memberAccess.Expression.ToString();
            var span = GetTextSpan(invocation);

            // Try StringBuilder instance methods first (heuristic: target looks like StringBuilder)
            // This prevents sb.ToString() from matching StringOp.ToString
            if (targetStr.Contains("StringBuilder") || targetStr.StartsWith("sb") || targetStr.StartsWith("_sb"))
            {
                var sbOp = TryGetStringBuilderOperation(methodName, targetExpr, args, span);
                if (sbOp != null)
                {
                    _context.RecordFeatureUsage("native-stringbuilder-op");
                    return sbOp;
                }
            }

            var stringOp = TryGetStringOperation(methodName, targetExpr, args, span);
            if (stringOp != null)
            {
                _context.RecordFeatureUsage("native-string-op");
                return stringOp;
            }

            // Try StringBuilder instance methods (for other naming patterns)
            var sbOp2 = TryGetStringBuilderOperation(methodName, targetExpr, args, span);
            if (sbOp2 != null)
            {
                _context.RecordFeatureUsage("native-stringbuilder-op");
                return sbOp2;
            }

            // Handle cast-then-call pattern: ((SomeType)expr).Method()
            // Hoist the cast to a temp bind so the method call has a clean target.
            if (memberAccess.Expression is CastExpressionSyntax castExpr
                || (memberAccess.Expression is ParenthesizedExpressionSyntax parenExpr
                    && parenExpr.Expression is CastExpressionSyntax))
            {
                var castConverted = targetExpr; // already converted via ConvertExpression above
                var castTypeHint = ExtractCastTypeHint(memberAccess.Expression);
                var tempName = _context.GenerateId("_cast", castTypeHint);
                _pendingStatements.Add(new BindStatementNode(
                    span, tempName, null, false, castConverted, new AttributeCollection()));
                return new CallExpressionNode(span, $"{tempName}.{methodName}", args, null, null, typeArguments);
            }

            // Handle chained method calls (e.g., products.GroupBy(...).Select(...))
            // Hoist inner call to a temp bind so the outer call has a clean target.
            // The temp bind is added to _pendingStatements which ConvertBlock flushes
            // before the containing statement.
            // CAVEAT: When the containing statement is a loop (while/for), the hoisted bind
            // is emitted once before the loop rather than re-evaluated per iteration. This is
            // semantically correct for LINQ's lazy IEnumerable chains (Where/Select/etc.
            // return deferred iterators), but would change behavior for eagerly-evaluated
            // chains. In practice this is acceptable — the alternative was non-functional Calor.
            // NOTE: Native ops (string, StringBuilder, regex, char) above may already have
            // returned, so this only fires for non-native chains. Statement-level decomposition
            // in ConvertBlock uses WouldChainUseNativeOps to stay aligned — see its doc comment.
            if (memberAccess.Expression is InvocationExpressionSyntax innerInvocation)
            {
                _context.RecordFeatureUsage("linq-method");
                var innerConverted = ConvertInvocationExpression(innerInvocation);
                var innerMethodHint = ExtractInnerMethodName(innerInvocation);
                var tempName = _context.GenerateId("_chain", innerMethodHint);
                _pendingStatements.Add(new BindStatementNode(
                    span, tempName, null, false, innerConverted, new AttributeCollection()));
                return new CallExpressionNode(span, $"{tempName}.{methodName}", args, null, null, typeArguments);
            }

            // Handle indexer-then-call pattern: words[0].Method(...)
            // Hoist the element access to a temp bind so the method call has a clean target.
            if (memberAccess.Expression is ElementAccessExpressionSyntax elementAccess)
            {
                var elementConverted = ConvertExpression(elementAccess);
                var tempName = _context.GenerateId("_elem");
                _pendingStatements.Add(new BindStatementNode(
                    span, tempName, null, false, elementConverted, new AttributeCollection()));
                return new CallExpressionNode(span, $"{tempName}.{methodName}", args, null, null, typeArguments);
            }

            // Handle new-then-call pattern: new Foo(...).Method(...)
            // Hoist the §NEW to a temp bind so the method call has a clean target.
            if (memberAccess.Expression is ObjectCreationExpressionSyntax
                || memberAccess.Expression is ImplicitObjectCreationExpressionSyntax)
            {
                var newConverted = targetExpr; // already converted via ConvertExpression above
                var newTypeHint = ExtractTypeHint(memberAccess.Expression);
                var tempName = _context.GenerateId("_new", newTypeHint);
                _pendingStatements.Add(new BindStatementNode(
                    span, tempName, null, false, newConverted, new AttributeCollection()));
                return new CallExpressionNode(span, $"{tempName}.{methodName}", args, null, null, typeArguments);
            }
        }

        // Check for static string methods like string.IsNullOrEmpty
        if (target.StartsWith("string."))
        {
            var methodName = target.Substring(7); // Remove "string." prefix
            var span = GetTextSpan(invocation);
            var staticStringOp = TryGetStaticStringOperation(methodName, args, span);
            if (staticStringOp != null)
            {
                _context.RecordFeatureUsage("native-string-op");
                return staticStringOp;
            }
        }

        // Check for static Regex methods like Regex.IsMatch
        if (target.StartsWith("Regex.") || target.StartsWith("System.Text.RegularExpressions.Regex."))
        {
            var methodName = target.Contains(".") ? target.Substring(target.LastIndexOf('.') + 1) : target;
            var span = GetTextSpan(invocation);
            var regexOp = TryGetRegexOperation(methodName, args, span);
            if (regexOp != null)
            {
                _context.RecordFeatureUsage("native-regex-op");
                return regexOp;
            }
        }

        // Check for static char methods like char.IsLetter
        if (target.StartsWith("char.") || target.StartsWith("Char."))
        {
            var methodName = target.Substring(target.IndexOf('.') + 1);
            var span = GetTextSpan(invocation);
            var charOp = TryGetStaticCharOperation(methodName, args, span);
            if (charOp != null)
            {
                _context.RecordFeatureUsage("native-char-op");
                return charOp;
            }
        }

        // Track Parallel.For/ForEach/Invoke usage
        if (target.StartsWith("Parallel.") || target.StartsWith("System.Threading.Tasks.Parallel."))
        {
            _context.RecordFeatureUsage("parallel");
        }

        // Track PLINQ .AsParallel() usage
        if (target.EndsWith(".AsParallel") || target.EndsWith(".AsOrdered") || target.EndsWith(".AsUnordered")
            || target.EndsWith(".WithDegreeOfParallelism") || target.EndsWith(".WithCancellation"))
        {
            _context.RecordFeatureUsage("plinq");
        }

        // Convert nameof(x) to NameOfExpressionNode
        if (target == "nameof" && invocation.ArgumentList.Arguments.Count == 1)
        {
            _context.RecordFeatureUsage("nameof");
            _context.IncrementConverted();
            var argText = invocation.ArgumentList.Arguments[0].Expression.ToString();
            return new NameOfExpressionNode(GetTextSpan(invocation), argText);
        }

        return new CallExpressionNode(GetTextSpan(invocation), target, args, argNames, argModifiers, typeArguments);
    }

    private StringOperationNode? TryGetRegexOperation(
        string methodName,
        List<ExpressionNode> args,
        TextSpan span)
    {
        return methodName switch
        {
            "IsMatch" when args.Count == 2 => new StringOperationNode(span, StringOp.RegexTest, args),
            "Match" when args.Count == 2 => new StringOperationNode(span, StringOp.RegexMatch, args),
            "Replace" when args.Count == 3 => new StringOperationNode(span, StringOp.RegexReplace, args),
            "Split" when args.Count == 2 => new StringOperationNode(span, StringOp.RegexSplit, args),
            _ => null
        };
    }

    private CharOperationNode? TryGetStaticCharOperation(
        string methodName,
        List<ExpressionNode> args,
        TextSpan span)
    {
        if (args.Count != 1) return null;

        return methodName switch
        {
            "IsLetter" => new CharOperationNode(span, CharOp.IsLetter, args),
            "IsDigit" => new CharOperationNode(span, CharOp.IsDigit, args),
            "IsWhiteSpace" => new CharOperationNode(span, CharOp.IsWhiteSpace, args),
            "IsUpper" => new CharOperationNode(span, CharOp.IsUpper, args),
            "IsLower" => new CharOperationNode(span, CharOp.IsLower, args),
            "ToUpper" => new CharOperationNode(span, CharOp.ToUpperChar, args),
            "ToLower" => new CharOperationNode(span, CharOp.ToLowerChar, args),
            _ => null
        };
    }

    private StringBuilderOperationNode? TryGetStringBuilderOperation(
        string methodName,
        ExpressionNode target,
        List<ExpressionNode> args,
        TextSpan span)
    {
        // Build argument list with target as first argument
        var allArgs = new List<ExpressionNode> { target };
        allArgs.AddRange(args);

        return methodName switch
        {
            "Append" when args.Count == 1 => new StringBuilderOperationNode(span, StringBuilderOp.Append, allArgs),
            "AppendLine" when args.Count == 1 => new StringBuilderOperationNode(span, StringBuilderOp.AppendLine, allArgs),
            "Insert" when args.Count == 2 => new StringBuilderOperationNode(span, StringBuilderOp.Insert, allArgs),
            "Remove" when args.Count == 2 => new StringBuilderOperationNode(span, StringBuilderOp.Remove, allArgs),
            "Clear" when args.Count == 0 => new StringBuilderOperationNode(span, StringBuilderOp.Clear, new[] { target }),
            "ToString" when args.Count == 0 => new StringBuilderOperationNode(span, StringBuilderOp.ToString, new[] { target }),
            _ => null
        };
    }

    private StringOperationNode? TryGetStringOperation(
        string methodName,
        ExpressionNode target,
        List<ExpressionNode> args,
        TextSpan span)
    {
        // Build argument list with target as first argument (excluding StringComparison arg)
        var allArgs = new List<ExpressionNode> { target };

        // Check for StringComparison overloads
        StringComparisonMode? comparisonMode = null;
        var regularArgs = args;
        if (args.Count >= 1)
        {
            // Check if last arg is a StringComparison enum
            var lastArg = args[^1];
            // Handle StringComparison as ReferenceNode or FieldAccessNode
            string? comparisonName = lastArg switch
            {
                ReferenceNode refNode when refNode.Name.StartsWith("StringComparison.") => refNode.Name,
                FieldAccessNode fieldAccess when fieldAccess.FieldName is "Ordinal" or "OrdinalIgnoreCase"
                    or "InvariantCulture" or "InvariantCultureIgnoreCase" =>
                    $"StringComparison.{fieldAccess.FieldName}",
                _ => null
            };

            if (comparisonName != null)
            {
                comparisonMode = ParseStringComparisonFromRef(comparisonName);
                if (comparisonMode != null)
                {
                    regularArgs = args.Take(args.Count - 1).ToList();
                }
            }
        }

        allArgs.AddRange(regularArgs);

        return methodName switch
        {
            // Query operations (with optional comparison mode)
            "Contains" when regularArgs.Count == 1 => new StringOperationNode(span, StringOp.Contains, allArgs, comparisonMode),
            "StartsWith" when regularArgs.Count == 1 => new StringOperationNode(span, StringOp.StartsWith, allArgs, comparisonMode),
            "EndsWith" when regularArgs.Count == 1 => new StringOperationNode(span, StringOp.EndsWith, allArgs, comparisonMode),
            "IndexOf" when regularArgs.Count == 1 => new StringOperationNode(span, StringOp.IndexOf, allArgs, comparisonMode),
            "Equals" when regularArgs.Count == 1 => new StringOperationNode(span, StringOp.Equals, allArgs, comparisonMode),

            // Transform operations
            "Substring" when args.Count == 1 => new StringOperationNode(span, StringOp.SubstringFrom, allArgs),
            "Substring" when args.Count == 2 => new StringOperationNode(span, StringOp.Substring, allArgs),
            "Replace" when args.Count == 2 => new StringOperationNode(span, StringOp.Replace, allArgs),
            "ToUpper" when args.Count == 0 => new StringOperationNode(span, StringOp.ToUpper, new[] { target }),
            "ToLower" when args.Count == 0 => new StringOperationNode(span, StringOp.ToLower, new[] { target }),
            "Trim" when args.Count == 0 => new StringOperationNode(span, StringOp.Trim, new[] { target }),
            "TrimStart" when args.Count == 0 => new StringOperationNode(span, StringOp.TrimStart, new[] { target }),
            "TrimEnd" when args.Count == 0 => new StringOperationNode(span, StringOp.TrimEnd, new[] { target }),
            "PadLeft" when args.Count >= 1 => new StringOperationNode(span, StringOp.PadLeft, allArgs),
            "PadRight" when args.Count >= 1 => new StringOperationNode(span, StringOp.PadRight, allArgs),
            "Split" when args.Count == 1 => new StringOperationNode(span, StringOp.Split, allArgs),
            "ToString" when args.Count == 0 => new StringOperationNode(span, StringOp.ToString, new[] { target }),

            _ => null
        };
    }

    private StringComparisonMode? ParseStringComparisonFromRef(string refName)
    {
        return refName switch
        {
            "StringComparison.Ordinal" => StringComparisonMode.Ordinal,
            "StringComparison.OrdinalIgnoreCase" => StringComparisonMode.IgnoreCase,
            "StringComparison.InvariantCulture" => StringComparisonMode.Invariant,
            "StringComparison.InvariantCultureIgnoreCase" => StringComparisonMode.InvariantIgnoreCase,
            // CurrentCulture variants are not supported - return null to skip native conversion
            _ => null
        };
    }

    private StringOperationNode? TryGetStaticStringOperation(
        string methodName,
        List<ExpressionNode> args,
        TextSpan span)
    {
        // Check for StringComparison overloads on static methods
        StringComparisonMode? comparisonMode = null;
        var regularArgs = args;
        if (args.Count >= 1)
        {
            var lastArg = args[^1];
            // Handle StringComparison as ReferenceNode or FieldAccessNode
            string? comparisonName = lastArg switch
            {
                ReferenceNode refNode when refNode.Name.StartsWith("StringComparison.") => refNode.Name,
                FieldAccessNode fieldAccess when fieldAccess.FieldName is "Ordinal" or "OrdinalIgnoreCase"
                    or "InvariantCulture" or "InvariantCultureIgnoreCase" =>
                    $"StringComparison.{fieldAccess.FieldName}",
                _ => null
            };

            if (comparisonName != null)
            {
                comparisonMode = ParseStringComparisonFromRef(comparisonName);
                if (comparisonMode != null)
                {
                    regularArgs = args.Take(args.Count - 1).ToList();
                }
            }
        }

        return methodName switch
        {
            "IsNullOrEmpty" when args.Count == 1 => new StringOperationNode(span, StringOp.IsNullOrEmpty, args),
            "IsNullOrWhiteSpace" when args.Count == 1 => new StringOperationNode(span, StringOp.IsNullOrWhiteSpace, args),
            "Join" when args.Count == 2 => new StringOperationNode(span, StringOp.Join, args),
            "Concat" when args.Count >= 2 => new StringOperationNode(span, StringOp.Concat, args),
            "Format" when args.Count >= 2 => new StringOperationNode(span, StringOp.Format, args),
            "Equals" when regularArgs.Count == 2 => new StringOperationNode(span, StringOp.Equals, regularArgs, comparisonMode),
            _ => null
        };
    }

    private ExpressionNode ConvertMemberAccessExpression(MemberAccessExpressionSyntax memberAccess)
    {
        var target = ConvertExpression(memberAccess.Expression);
        var memberName = memberAccess.Name.Identifier.Text;
        var span = GetTextSpan(memberAccess);

        // Convert string.Empty and System.String.Empty to empty string literal ""
        if (memberName == "Empty")
        {
            var targetStr = memberAccess.Expression.ToString();
            if (targetStr is "string" or "String" or "System.String")
            {
                return new StringLiteralNode(span, "");
            }
        }

        // Handle generic static member access: GenericType<T>.Member → GenericType<T>.Member
        // e.g., EqualityComparer<int>.Default, Array.Empty<string>()
        if (memberAccess.Expression is GenericNameSyntax genericName)
        {
            var typeName = genericName.Identifier.Text;
            var typeArgs = string.Join(", ", genericName.TypeArgumentList.Arguments.Select(a => TypeMapper.CSharpToCalor(a.ToString())));
            return new ReferenceNode(span, $"{typeName}<{typeArgs}>.{memberName}");
        }

        // Convert primitive type static members (int.MaxValue, byte.MinValue, etc.)
        if (memberName is "MaxValue" or "MinValue" && memberAccess.Expression is PredefinedTypeSyntax predefinedType)
        {
            var keyword = predefinedType.Keyword.Text;
            switch (keyword)
            {
                case "int" when memberName == "MaxValue":
                    return new IntLiteralNode(span, int.MaxValue);
                case "int" when memberName == "MinValue":
                    return new IntLiteralNode(span, int.MinValue);
                case "byte" when memberName == "MaxValue":
                    return new IntLiteralNode(span, byte.MaxValue);
                case "byte" when memberName == "MinValue":
                    return new IntLiteralNode(span, byte.MinValue);
                case "short" when memberName == "MaxValue":
                    return new IntLiteralNode(span, short.MaxValue);
                case "short" when memberName == "MinValue":
                    return new IntLiteralNode(span, short.MinValue);
                default:
                    // For long, float, double, etc. — pass through as reference
                    return new ReferenceNode(span, $"{keyword}.{memberName}");
            }
        }

        // Convert string.Length to native string operation
        // Note: We can't reliably detect if target is a string without type info,
        // but Length is commonly used on strings so we'll optimistically convert it.
        // The generated Calor will still work since (len s) maps to s.Length.
        if (memberName == "Length")
        {
            // Check if it looks like a string context (heuristic: not array access pattern)
            var targetStr = memberAccess.Expression.ToString();
            if (!targetStr.Contains("["))
            {
                // Heuristic: if the target looks like a StringBuilder (contains "StringBuilder" or starts with "sb")
                // use sb-length, otherwise use len (string length)
                if (targetStr.Contains("StringBuilder") || targetStr.StartsWith("sb"))
                {
                    _context.RecordFeatureUsage("native-stringbuilder-op");
                    return new StringBuilderOperationNode(span, StringBuilderOp.Length, new[] { target });
                }
                _context.RecordFeatureUsage("native-string-op");
                return new StringOperationNode(span, StringOp.Length, new[] { target });
            }
        }

        return new FieldAccessNode(span, target, memberName);
    }

    private ExpressionNode ConvertObjectCreation(ObjectCreationExpressionSyntax objCreation)
    {
        var typeName = objCreation.Type.ToString();
        var typeArgs = new List<string>();

        if (objCreation.Type is GenericNameSyntax genericName)
        {
            typeName = genericName.Identifier.Text;
            typeArgs = genericName.TypeArgumentList.Arguments
                .Select(a => TypeMapper.CSharpToCalor(a.ToString()))
                .ToList();

            // Check for collection types and convert to appropriate nodes
            // Skip collection-specific converters when constructor args are present,
            // as they only handle initializer elements and would drop the arguments.
            var hasCtorArgs = objCreation.ArgumentList?.Arguments.Count > 0;

            if (typeName == "List" && typeArgs.Count == 1 && !hasCtorArgs)
            {
                return ConvertListCreation(objCreation, typeArgs[0]);
            }
            else if (IsDictionaryType(typeName) && typeArgs.Count == 2 && !hasCtorArgs)
            {
                return ConvertDictionaryCreation(objCreation, typeArgs[0], typeArgs[1]);
            }
            else if (typeName == "HashSet" && typeArgs.Count == 1 && !hasCtorArgs)
            {
                return ConvertHashSetCreation(objCreation, typeArgs[0]);
            }
        }

        var args = objCreation.ArgumentList?.Arguments
            .Select(a => ConvertExpression(a.Expression))
            .ToList() ?? new List<ExpressionNode>();

        // Convert StringBuilder to native operations
        if (typeName == "StringBuilder" || typeName == "System.Text.StringBuilder")
        {
            _context.RecordFeatureUsage("native-stringbuilder-op");
            return new StringBuilderOperationNode(GetTextSpan(objCreation), StringBuilderOp.New, args);
        }

        // Handle object initializer
        var initializers = new List<ObjectInitializerAssignment>();
        if (objCreation.Initializer != null)
        {
            _context.RecordFeatureUsage("object-initializer");
            foreach (var expr in objCreation.Initializer.Expressions)
            {
                if (expr is AssignmentExpressionSyntax assignment)
                {
                    var propName = assignment.Left.ToString();
                    var value = ConvertExpression(assignment.Right);
                    initializers.Add(new ObjectInitializerAssignment(propName, value));
                }
            }
        }

        return new NewExpressionNode(GetTextSpan(objCreation), typeName, typeArgs, args, initializers);
    }

    private AnonymousObjectCreationNode ConvertAnonymousObjectCreation(AnonymousObjectCreationExpressionSyntax anonObj)
    {
        _context.RecordFeatureUsage("anonymous-type");
        var initializers = new List<ObjectInitializerAssignment>();

        foreach (var init in anonObj.Initializers)
        {
            var name = init.NameEquals?.Name.Identifier.Text ?? init.Expression.ToString();
            var value = ConvertExpression(init.Expression);
            initializers.Add(new ObjectInitializerAssignment(name, value));
        }

        return new AnonymousObjectCreationNode(GetTextSpan(anonObj), initializers);
    }

    /// <summary>
    /// Desugars LINQ query syntax to equivalent method chain calls using proper AST nodes.
    /// from x in collection where cond select proj → collection.Where(x => cond).Select(x => proj)
    /// </summary>
    private ExpressionNode ConvertQueryExpression(QueryExpressionSyntax query)
    {
        _context.RecordFeatureUsage("linq-query");
        var span = GetTextSpan(query);

        // Start with the from clause's collection
        var rangeVar = query.FromClause.Identifier.Text;
        var currentExpr = ConvertExpression(query.FromClause.Expression);

        var body = query.Body;
        while (body != null)
        {
            // Process body clauses (where, orderby, let, join, additional from)
            // Track the last join variable so we can fold the select into the join's result selector
            string? lastJoinVar = null;
            foreach (var clause in body.Clauses)
            {
                lastJoinVar = null; // Reset — only set if the LAST clause is a join

                switch (clause)
                {
                    case WhereClauseSyntax whereClause:
                    {
                        var condition = ConvertExpression(whereClause.Condition);
                        var lambda = MakeLinqLambda(span, rangeVar, condition);
                        currentExpr = MakeChainedCall(span, currentExpr, "Where", new ExpressionNode[] { lambda });
                        break;
                    }
                    case OrderByClauseSyntax orderByClause:
                    {
                        var isFirst = true;
                        foreach (var ordering in orderByClause.Orderings)
                        {
                            var keyExpr = ConvertExpression(ordering.Expression);
                            var isDescending = ordering.AscendingOrDescendingKeyword.IsKind(SyntaxKind.DescendingKeyword);

                            string methodName;
                            if (isFirst)
                                methodName = isDescending ? "OrderByDescending" : "OrderBy";
                            else
                                methodName = isDescending ? "ThenByDescending" : "ThenBy";

                            var lambda = MakeLinqLambda(span, rangeVar, keyExpr);
                            currentExpr = MakeChainedCall(span, currentExpr, methodName, new ExpressionNode[] { lambda });
                            isFirst = false;
                        }
                        break;
                    }
                    case LetClauseSyntax letClause:
                    {
                        // let v = expr → .Select(x => new { x, v = expr })
                        var letVar = letClause.Identifier.Text;
                        var letExpr = ConvertExpression(letClause.Expression);
                        var anonObj = new AnonymousObjectCreationNode(span, new List<ObjectInitializerAssignment>
                        {
                            new ObjectInitializerAssignment(rangeVar, new ReferenceNode(span, rangeVar)),
                            new ObjectInitializerAssignment(letVar, letExpr)
                        });
                        var lambda = MakeLinqLambda(span, rangeVar, anonObj);
                        currentExpr = MakeChainedCall(span, currentExpr, "Select", new ExpressionNode[] { lambda });
                        // After let, the range variable becomes the anonymous type
                        // but for simplicity we keep the same range var name
                        break;
                    }
                    case JoinClauseSyntax joinClause:
                    {
                        var joinVar = joinClause.Identifier.Text;
                        var joinCollection = ConvertExpression(joinClause.InExpression);
                        var leftKey = ConvertExpression(joinClause.LeftExpression);
                        var rightKey = ConvertExpression(joinClause.RightExpression);
                        // Default result selector: anonymous object with both range vars.
                        // This will be replaced if a select clause immediately follows.
                        var resultProjection = new AnonymousObjectCreationNode(span, new List<ObjectInitializerAssignment>
                        {
                            new ObjectInitializerAssignment(rangeVar, new ReferenceNode(span, rangeVar)),
                            new ObjectInitializerAssignment(joinVar, new ReferenceNode(span, joinVar))
                        });

                        var outerKeyLambda = MakeLinqLambda(span, rangeVar, leftKey);
                        var innerKeyLambda = MakeLinqLambda(span, joinVar, rightKey);
                        var resultLambda = MakeLinqLambda2(span, rangeVar, joinVar, resultProjection);

                        currentExpr = MakeChainedCall(span, currentExpr, "Join", new ExpressionNode[]
                        {
                            joinCollection, outerKeyLambda, innerKeyLambda, resultLambda
                        });
                        lastJoinVar = joinVar;
                        break;
                    }
                    case FromClauseSyntax additionalFrom:
                    {
                        // Additional from → SelectMany
                        var innerVar = additionalFrom.Identifier.Text;
                        var innerCollection = ConvertExpression(additionalFrom.Expression);
                        var lambda = MakeLinqLambda(span, rangeVar, innerCollection);
                        currentExpr = MakeChainedCall(span, currentExpr, "SelectMany", new ExpressionNode[] { lambda });
                        rangeVar = innerVar;
                        break;
                    }
                }
            }

            // Process the terminal select or group clause
            if (body.SelectOrGroup is SelectClauseSyntax selectClause)
            {
                var projection = ConvertExpression(selectClause.Expression);

                if (lastJoinVar != null
                    && currentExpr is CallExpressionNode joinCall
                    && joinCall.Arguments.Count == 4)
                {
                    // Fold the select projection into the Join's result selector (4th arg),
                    // matching how C# compiles join...select into a single .Join() call.
                    var newResultLambda = MakeLinqLambda2(span, rangeVar, lastJoinVar, projection);
                    currentExpr = new CallExpressionNode(span, joinCall.Target, new ExpressionNode[]
                    {
                        joinCall.Arguments[0], joinCall.Arguments[1], joinCall.Arguments[2], newResultLambda
                    });
                }
                else if (projection is not ReferenceNode refNode || refNode.Name != rangeVar)
                {
                    // Only add Select if projection is not just the range variable
                    var lambda = MakeLinqLambda(span, rangeVar, projection);
                    currentExpr = MakeChainedCall(span, currentExpr, "Select", new ExpressionNode[] { lambda });
                }
            }
            else if (body.SelectOrGroup is GroupClauseSyntax groupClause)
            {
                var byExpr = ConvertExpression(groupClause.ByExpression);
                var lambda = MakeLinqLambda(span, rangeVar, byExpr);
                currentExpr = MakeChainedCall(span, currentExpr, "GroupBy", new ExpressionNode[] { lambda });
            }

            // Handle continuation (into g ...)
            if (body.Continuation != null)
            {
                rangeVar = body.Continuation.Identifier.Text;
                body = body.Continuation.Body;
            }
            else
            {
                body = null;
            }
        }

        return currentExpr;
    }

    /// <summary>
    /// Creates a single-parameter lambda expression node for LINQ operations.
    /// </summary>
    private LambdaExpressionNode MakeLinqLambda(TextSpan span, string paramName, ExpressionNode body)
    {
        var id = _context.GenerateId("lam");
        var parameters = new List<LambdaParameterNode>
        {
            new LambdaParameterNode(span, paramName, null)
        };
        return new LambdaExpressionNode(span, id, parameters, effects: null, isAsync: false,
            expressionBody: body, statementBody: null, attributes: new AttributeCollection());
    }

    /// <summary>
    /// Creates a two-parameter lambda expression node for LINQ join result selectors.
    /// </summary>
    private LambdaExpressionNode MakeLinqLambda2(TextSpan span, string param1, string param2, ExpressionNode body)
    {
        var id = _context.GenerateId("lam");
        var parameters = new List<LambdaParameterNode>
        {
            new LambdaParameterNode(span, param1, null),
            new LambdaParameterNode(span, param2, null)
        };
        return new LambdaExpressionNode(span, id, parameters, effects: null, isAsync: false,
            expressionBody: body, statementBody: null, attributes: new AttributeCollection());
    }

    /// <summary>
    /// Creates a chained method call node (e.g., collection.Where(...)).
    /// The receiver expression is emitted to Calor to form the target string.
    /// </summary>
    private CallExpressionNode MakeChainedCall(TextSpan span, ExpressionNode receiver, string methodName, IReadOnlyList<ExpressionNode> arguments)
    {
        var receiverCalor = receiver.Accept(new CalorEmitter());
        var target = $"({receiverCalor}).{methodName}";
        return new CallExpressionNode(span, target, arguments);
    }

    private ListCreationNode ConvertListCreation(ObjectCreationExpressionSyntax objCreation, string elementType)
    {
        var id = _context.GenerateId("list");
        var elements = new List<ExpressionNode>();

        if (objCreation.Initializer != null)
        {
            _context.RecordFeatureUsage("list-initializer");
            foreach (var expr in objCreation.Initializer.Expressions)
            {
                elements.Add(ConvertExpression(expr));
            }
        }

        return new ListCreationNode(
            GetTextSpan(objCreation),
            id,
            id,
            elementType,
            elements,
            new AttributeCollection());
    }

    private static bool IsDictionaryType(string typeName) =>
        typeName is "Dictionary" or "SortedDictionary" or "ConcurrentDictionary"
            or "FrozenDictionary" or "ImmutableDictionary" or "ImmutableSortedDictionary";

    private DictionaryCreationNode ConvertDictionaryCreation(ObjectCreationExpressionSyntax objCreation, string keyType, string valueType)
    {
        var id = _context.GenerateId("dict");
        var entries = new List<KeyValuePairNode>();

        if (objCreation.Initializer != null)
        {
            _context.RecordFeatureUsage("dictionary-initializer");
            foreach (var expr in objCreation.Initializer.Expressions)
            {
                if (expr is InitializerExpressionSyntax kvInit &&
                    kvInit.Expressions.Count == 2)
                {
                    // { key, value } syntax
                    var key = ConvertExpression(kvInit.Expressions[0]);
                    var value = ConvertExpression(kvInit.Expressions[1]);
                    entries.Add(new KeyValuePairNode(GetTextSpan(expr), key, value));
                }
                else if (expr is AssignmentExpressionSyntax assignment)
                {
                    // [key] = value syntax
                    ExpressionNode key;
                    if (assignment.Left is ImplicitElementAccessSyntax implicitAccess &&
                        implicitAccess.ArgumentList.Arguments.Count > 0)
                    {
                        key = ConvertExpression(implicitAccess.ArgumentList.Arguments[0].Expression);
                    }
                    else
                    {
                        key = ConvertExpression(assignment.Left);
                    }
                    var value = ConvertExpression(assignment.Right);
                    entries.Add(new KeyValuePairNode(GetTextSpan(expr), key, value));
                }
            }
        }

        return new DictionaryCreationNode(
            GetTextSpan(objCreation),
            id,
            id,
            keyType,
            valueType,
            entries,
            new AttributeCollection());
    }

    private SetCreationNode ConvertHashSetCreation(ObjectCreationExpressionSyntax objCreation, string elementType)
    {
        var id = _context.GenerateId("set");
        var elements = new List<ExpressionNode>();

        if (objCreation.Initializer != null)
        {
            _context.RecordFeatureUsage("hashset-initializer");
            foreach (var expr in objCreation.Initializer.Expressions)
            {
                elements.Add(ConvertExpression(expr));
            }
        }

        return new SetCreationNode(
            GetTextSpan(objCreation),
            id,
            id,
            elementType,
            elements,
            new AttributeCollection());
    }

    private ExpressionNode ConvertConditionalExpression(ConditionalExpressionSyntax conditional)
    {
        var span = GetTextSpan(conditional);

        // Handle ternary with throw in false branch: flag ? value : throw new E(...)
        // Hoist to: if (!flag) throw new E(...); return value;
        if (conditional.WhenFalse is ThrowExpressionSyntax throwFalse)
        {
            _context.RecordFeatureUsage("ternary-throw");
            _context.IncrementConverted();
            var condition = ConvertExpression(conditional.Condition);
            var exceptionExpr = ConvertExpression(throwFalse.Expression);

            var negatedCondition = new UnaryOperationNode(span, UnaryOperator.Not, condition);
            var throwStmt = new ThrowStatementNode(GetTextSpan(throwFalse), exceptionExpr);
            var guard = new IfStatementNode(
                span,
                _context.GenerateId("if"),
                negatedCondition,
                new List<StatementNode> { throwStmt },
                Array.Empty<ElseIfClauseNode>(),
                null,
                new AttributeCollection());

            _pendingStatements.Add(guard);
            return ConvertExpression(conditional.WhenTrue);
        }

        // Handle ternary with throw in true branch: flag ? throw new E(...) : value
        // Hoist to: if (flag) throw new E(...); return value;
        if (conditional.WhenTrue is ThrowExpressionSyntax throwTrue)
        {
            _context.RecordFeatureUsage("ternary-throw");
            _context.IncrementConverted();
            var condition = ConvertExpression(conditional.Condition);
            var exceptionExpr = ConvertExpression(throwTrue.Expression);

            var throwStmt = new ThrowStatementNode(GetTextSpan(throwTrue), exceptionExpr);
            var guard = new IfStatementNode(
                span,
                _context.GenerateId("if"),
                condition,
                new List<StatementNode> { throwStmt },
                Array.Empty<ElseIfClauseNode>(),
                null,
                new AttributeCollection());

            _pendingStatements.Add(guard);
            return ConvertExpression(conditional.WhenFalse);
        }

        // Deeply nested ternaries (depth > 2) → decompose into if/else with a result variable
        if (CountConditionalDepth(conditional) > 2)
        {
            _context.RecordFeatureUsage("ternary-decompose");
            _context.IncrementConverted();
            var resultName = _context.GenerateId("_tern", "Result");

            _pendingStatements.Add(new BindStatementNode(
                span, resultName, null, true, null, new AttributeCollection()));

            EmitConditionalAsIfElse(conditional, span, resultName);

            return new ReferenceNode(span, resultName);
        }

        // Standard ternary: (? cond then else)
        var cond = ConvertExpression(conditional.Condition);
        var whenTrue = ConvertExpression(conditional.WhenTrue);
        var whenFalse = ConvertExpression(conditional.WhenFalse);

        return new ConditionalExpressionNode(span, cond, whenTrue, whenFalse);
    }

    private static ExpressionSyntax UnwrapParens(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
            expr = paren.Expression;
        return expr;
    }

    private static int CountConditionalDepth(ConditionalExpressionSyntax node)
    {
        var trueDepth = UnwrapParens(node.WhenTrue) is ConditionalExpressionSyntax trueChild
            ? CountConditionalDepth(trueChild) : 0;
        var falseDepth = UnwrapParens(node.WhenFalse) is ConditionalExpressionSyntax falseChild
            ? CountConditionalDepth(falseChild) : 0;
        return 1 + Math.Max(trueDepth, falseDepth);
    }

    private void EmitConditionalAsIfElse(ConditionalExpressionSyntax node, TextSpan span, string resultName)
    {
        var condition = ConvertExpression(node.Condition);
        var thenBody = BuildConditionalBranch(UnwrapParens(node.WhenTrue), span, resultName);
        var elseBody = BuildConditionalBranch(UnwrapParens(node.WhenFalse), span, resultName);

        _pendingStatements.Add(new IfStatementNode(
            span,
            _context.GenerateId("if"),
            condition,
            thenBody,
            Array.Empty<ElseIfClauseNode>(),
            elseBody,
            new AttributeCollection()));
    }

    private List<StatementNode> BuildConditionalBranch(ExpressionSyntax branch, TextSpan span, string resultName)
    {
        if (UnwrapParens(branch) is ConditionalExpressionSyntax nested)
        {
            var nestedCondition = ConvertExpression(nested.Condition);
            var nestedThen = new List<StatementNode>
            {
                new AssignmentStatementNode(span, new ReferenceNode(span, resultName), ConvertExpression(nested.WhenTrue))
            };
            var nestedElse = new List<StatementNode>
            {
                new AssignmentStatementNode(span, new ReferenceNode(span, resultName), ConvertExpression(nested.WhenFalse))
            };
            return new List<StatementNode>
            {
                new IfStatementNode(span, _context.GenerateId("if"), nestedCondition, nestedThen,
                    Array.Empty<ElseIfClauseNode>(), nestedElse, new AttributeCollection())
            };
        }

        return new List<StatementNode>
        {
            new AssignmentStatementNode(span, new ReferenceNode(span, resultName), ConvertExpression(branch))
        };
    }

    private ExpressionNode ConvertCastExpression(CastExpressionSyntax cast)
    {
        _context.RecordFeatureUsage("cast");
        var targetType = cast.Type.ToString();
        var innerExpr = ConvertExpression(cast.Expression);
        var span = GetTextSpan(cast);

        // Convert char casts to native char operations
        // Use heuristics to avoid incorrect conversions:
        // - (int)c where c is a single character variable → char-code
        // - (char)n where n is a numeric variable/literal → char-from-code
        var sourceExprStr = cast.Expression.ToString();

        if (targetType == "int" || targetType == "Int32")
        {
            // Only convert to char-code if the source looks like a char:
            // - Single character variable names (c, ch, character, etc.)
            // - Char literals ('a')
            // - String indexer (s[0])
            // - Explicitly typed char expressions
            if (LooksLikeCharExpression(cast.Expression, sourceExprStr))
            {
                _context.RecordFeatureUsage("native-char-op");
                return new CharOperationNode(span, CharOp.CharCode, new[] { innerExpr });
            }
        }
        else if (targetType == "char" || targetType == "Char")
        {
            // Only convert to char-from-code if the source looks like an int:
            // - Numeric literals (65)
            // - Variables with numeric-sounding names (n, num, code, charCode, etc.)
            // - Arithmetic expressions
            if (LooksLikeIntExpression(cast.Expression, sourceExprStr))
            {
                _context.RecordFeatureUsage("native-char-op");
                return new CharOperationNode(span, CharOp.CharFromCode, new[] { innerExpr });
            }
        }

        // Fall back to type cast operation for ambiguous cases
        var calorType = TypeMapper.CSharpToCalor(targetType);
        return new TypeOperationNode(span, TypeOp.Cast, innerExpr, calorType);
    }

    private static bool LooksLikeCharExpression(ExpressionSyntax expr, string exprStr)
    {
        // Char literals
        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.CharacterLiteralExpression))
            return true;

        // String indexer: s[0], str[i], etc.
        if (expr is ElementAccessExpressionSyntax)
            return true;

        // Common char variable names
        var lowerExpr = exprStr.ToLowerInvariant();
        if (lowerExpr is "c" or "ch" or "char" or "character" or "letter" or "digit")
            return true;

        // Variables starting with 'c' followed by uppercase (cChar, cValue, etc.)
        if (exprStr.Length >= 2 && exprStr[0] == 'c' && char.IsUpper(exprStr[1]))
            return true;

        return false;
    }

    private static bool LooksLikeIntExpression(ExpressionSyntax expr, string exprStr)
    {
        // Numeric literals
        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression))
            return true;

        // Arithmetic expressions
        if (expr is BinaryExpressionSyntax)
            return true;

        // Common int variable names
        var lowerExpr = exprStr.ToLowerInvariant();
        if (lowerExpr is "n" or "i" or "num" or "code" or "charcode" or "ascii" or "value" or "index")
            return true;

        return false;
    }

    private ExpressionNode ConvertArrayCreation(ArrayCreationExpressionSyntax arrayCreation)
    {
        // Detect multidimensional arrays (int[,], int[,,])
        if (arrayCreation.Type.RankSpecifiers.Count > 0)
        {
            var rankSpec = arrayCreation.Type.RankSpecifiers[0];
            if (rankSpec.Rank > 1)
            {
                return ConvertMultiDimArrayCreation(arrayCreation, rankSpec.Rank);
            }
        }

        var elementType = TypeMapper.CSharpToCalor(arrayCreation.Type.ElementType.ToString());
        var id = _context.GenerateId("arr", elementType);
        var name = _context.GenerateId("arr", elementType);

        ExpressionNode? size = null;
        var initializer = new List<ExpressionNode>();

        if (arrayCreation.Type.RankSpecifiers.Count > 0)
        {
            var rank = arrayCreation.Type.RankSpecifiers[0];
            if (rank.Sizes.Count > 0 && rank.Sizes[0] is ExpressionSyntax sizeExpr
                && sizeExpr is not OmittedArraySizeExpressionSyntax)
            {
                size = ConvertExpression(sizeExpr);
            }
        }

        if (arrayCreation.Initializer != null)
        {
            initializer = arrayCreation.Initializer.Expressions
                .Select(ConvertExpression)
                .ToList();
        }

        return new ArrayCreationNode(GetTextSpan(arrayCreation), id, name, elementType, size, initializer, new AttributeCollection());
    }

    private ArrayCreationNode ConvertImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax implicitArray)
    {
        var id = _context.GenerateId("arr");
        var initializer = implicitArray.Initializer.Expressions
            .Select(ConvertExpression)
            .ToList();

        // Try declared type first, fall back to inferring from first element
        var elementType = TryGetDeclaredArrayElementType(implicitArray) ?? InferElementType(initializer);

        return new ArrayCreationNode(GetTextSpan(implicitArray), id, id, elementType, null, initializer, new AttributeCollection());
    }

    private ExpressionNode ConvertInitializerExpression(InitializerExpressionSyntax initExpr)
    {
        if (initExpr.Kind() != SyntaxKind.ArrayInitializerExpression)
            return CreateFallbackExpression(initExpr, "unsupported-initializer");

        _context.RecordFeatureUsage("array-initializer");

        var id = _context.GenerateId("arr");
        var initializer = initExpr.Expressions
            .Select(ConvertExpression)
            .ToList();

        // Try declared type first, fall back to inferring from first element
        var elementType = TryGetDeclaredArrayElementType(initExpr) ?? InferElementType(initializer);

        return new ArrayCreationNode(GetTextSpan(initExpr), id, id, elementType, null, initializer, new AttributeCollection());
    }

    /// <summary>
    /// Tries to infer the type of a lambda parameter using the semantic model.
    /// Returns null if the semantic model is unavailable or the type cannot be resolved.
    /// </summary>
    private string? TryInferLambdaParameterType(ParameterSyntax parameter)
    {
        if (_semanticModel == null) return null;

        try
        {
            var symbol = _semanticModel.GetDeclaredSymbol(parameter);
            if (symbol is IParameterSymbol paramSymbol && paramSymbol.Type != null
                && paramSymbol.Type.SpecialType != SpecialType.System_Object)
            {
                return TypeMapper.CSharpToCalor(paramSymbol.Type.ToDisplayString());
            }
        }
        catch
        {
            // Semantic model queries can fail if compilation has errors; fall through gracefully
        }

        return null;
    }

    private static string? TryGetDeclaredArrayElementType(SyntaxNode node)
    {
        // Walk up: InitializerExpression -> EqualsValueClause -> VariableDeclarator -> VariableDeclaration
        if (node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } })
        {
            var typeStr = declaration.Type.ToString();
            // Handle single-dimensional arrays: "double[]"
            if (typeStr.EndsWith("[]"))
            {
                var csharpElement = typeStr[..^2];
                return TypeMapper.CSharpToCalor(csharpElement);
            }
            // Handle multi-dimensional arrays: "int[,]", "int[,,]", etc.
            var bracketStart = typeStr.IndexOf('[');
            if (bracketStart > 0 && typeStr.EndsWith("]"))
            {
                var csharpElement = typeStr[..bracketStart];
                return TypeMapper.CSharpToCalor(csharpElement);
            }
        }
        return null;
    }


    private static string InferElementType(List<ExpressionNode> elements)
    {
        if (elements.Count == 0) return "object";
        return elements[0] switch
        {
            IntLiteralNode => "i32",
            FloatLiteralNode => "f64",
            DecimalLiteralNode => "decimal",
            StringLiteralNode => "str",
            BoolLiteralNode => "bool",
            _ => "object"
        };
    }

    private ExpressionNode ConvertElementAccess(ElementAccessExpressionSyntax elementAccess)
    {
        var span = GetTextSpan(elementAccess);
        var array = ConvertExpression(elementAccess.Expression);

        // Multidimensional array access: grid[1, 2] has multiple arguments
        if (elementAccess.ArgumentList.Arguments.Count > 1)
        {
            _context.RecordFeatureUsage("multidim-array");
            var indices = elementAccess.ArgumentList.Arguments
                .Select(a => ConvertExpression(a.Expression))
                .ToList();
            return new MultiDimArrayAccessNode(span, array, indices);
        }

        var index = ConvertExpression(elementAccess.ArgumentList.Arguments[0].Expression);

        // Only use char-at when the target is a string literal (e.g. "hello"[0])
        // Default to §IDX (ArrayAccess) — array/list indexing is far more common
        if (elementAccess.Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            _context.RecordFeatureUsage("native-char-op");
            return new CharOperationNode(span, CharOp.CharAt, new List<ExpressionNode> { array, index });
        }

        return new ArrayAccessNode(span, array, index);
    }

    private LambdaExpressionNode ConvertLambdaExpression(LambdaExpressionSyntax lambda)
    {
        _context.RecordFeatureUsage("lambda");

        var id = _context.GenerateId("lam");
        var parameters = new List<LambdaParameterNode>();
        var isAsync = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
        var isStatic = lambda.Modifiers.Any(SyntaxKind.StaticKeyword);

        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                parameters.Add(new LambdaParameterNode(
                    GetTextSpan(simple.Parameter),
                    simple.Parameter.Identifier.Text,
                    TryInferLambdaParameterType(simple.Parameter)));
                break;

            case ParenthesizedLambdaExpressionSyntax paren:
                foreach (var param in paren.ParameterList.Parameters)
                {
                    var typeName = param.Type != null
                        ? TypeMapper.CSharpToCalor(param.Type.ToString())
                        : TryInferLambdaParameterType(param);
                    parameters.Add(new LambdaParameterNode(
                        GetTextSpan(param),
                        param.Identifier.Text,
                        typeName));
                }
                break;
        }

        ExpressionNode? exprBody = null;
        List<StatementNode>? stmtBody = null;

        if (lambda.ExpressionBody != null)
        {
            // Check if expression body is an assignment (e.g., x => obj.Prop = x)
            if (lambda.ExpressionBody is AssignmentExpressionSyntax lambdaAssign)
            {
                var assignTarget = ConvertExpression(lambdaAssign.Left);
                var assignValue = ConvertExpression(lambdaAssign.Right);
                stmtBody = new List<StatementNode>
                {
                    new AssignmentStatementNode(GetTextSpan(lambdaAssign), assignTarget, assignValue)
                };
            }
            else
            {
                // Save/restore _pendingStatements so chains inside the lambda body
                // don't hoist temp binds outside the lambda scope.
                var savedPending = new List<StatementNode>(_pendingStatements);
                _pendingStatements.Clear();

                exprBody = ConvertExpression(lambda.ExpressionBody);

                // If the expression body produced pending statements (e.g., chain hoisting),
                // convert the expression body to a statement body with those binds prepended.
                if (_pendingStatements.Count > 0)
                {
                    stmtBody = new List<StatementNode>(_pendingStatements);
                    stmtBody.Add(new ReturnStatementNode(exprBody.Span, exprBody));
                    exprBody = null;
                }

                _pendingStatements.Clear();
                _pendingStatements.AddRange(savedPending);
            }
        }
        else if (lambda.Body is BlockSyntax block)
        {
            stmtBody = ConvertBlock(block).ToList();
        }

        return new LambdaExpressionNode(
            GetTextSpan(lambda),
            id,
            parameters,
            effects: null,
            isAsync,
            exprBody,
            stmtBody,
            new AttributeCollection(),
            isStatic);
    }

    private AwaitExpressionNode ConvertAwaitExpression(AwaitExpressionSyntax awaitExpr)
    {
        _context.RecordFeatureUsage("async-await");

        var awaited = ConvertExpression(awaitExpr.Expression);
        return new AwaitExpressionNode(GetTextSpan(awaitExpr), awaited, null);
    }

    private InterpolatedStringNode ConvertInterpolatedString(InterpolatedStringExpressionSyntax interpolated)
    {
        _context.RecordFeatureUsage("string-interpolation");

        var parts = new List<InterpolatedStringPartNode>();

        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    parts.Add(new InterpolatedStringTextNode(GetTextSpan(text), text.TextToken.Text));
                    break;

                case InterpolationSyntax interp:
                    var formatSpec = interp.FormatClause?.FormatStringToken.Text;
                    var alignmentClause = interp.AlignmentClause?.Value.ToString();
                    parts.Add(new InterpolatedStringExpressionNode(
                        GetTextSpan(interp),
                        ConvertExpression(interp.Expression),
                        formatSpec,
                        alignmentClause));
                    break;
            }
        }

        return new InterpolatedStringNode(GetTextSpan(interpolated), parts);
    }

    private NullConditionalNode ConvertConditionalAccess(ConditionalAccessExpressionSyntax condAccess)
    {
        _context.RecordFeatureUsage("null-conditional");

        var target = ConvertExpression(condAccess.Expression);

        // When WhenNotNull is a method call (e.g., obj?.Method(x)),
        // decompose and convert args through the AST pipeline
        if (condAccess.WhenNotNull is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberBindingExpressionSyntax memberBinding)
        {
            _context.RecordFeatureUsage("null-conditional-method");
            var methodName = memberBinding.Name.Identifier.Text;
            var convertedArgs = invocation.ArgumentList.Arguments
                .Select(a => ConvertExpression(a.Expression));
            var csharpEmitter = new CSharpEmitter();
            var argsStr = string.Join(", ", convertedArgs.Select(a => a.Accept(csharpEmitter)));
            return new NullConditionalNode(GetTextSpan(condAccess), target, $"{methodName}({argsStr})");
        }

        // WhenNotNull is a MemberBindingExpression which starts with '.' (e.g., ".Status")
        // We need to strip the leading dot since the emitter adds its own "?."
        var memberName = condAccess.WhenNotNull.ToString();
        if (memberName.StartsWith("."))
        {
            memberName = memberName.Substring(1);
        }

        return new NullConditionalNode(GetTextSpan(condAccess), target, memberName);
    }

    private IReadOnlyList<TypeParameterNode> ConvertTypeParameters(
        TypeParameterListSyntax? typeParamList,
        SyntaxList<TypeParameterConstraintClauseSyntax>? constraintClauses = null)
    {
        if (typeParamList == null)
            return Array.Empty<TypeParameterNode>();

        _context.RecordFeatureUsage("generics");

        // Build a map of type parameter name -> constraints
        var constraintMap = new Dictionary<string, List<TypeConstraintNode>>();
        if (constraintClauses.HasValue)
        {
            foreach (var clause in constraintClauses.Value)
            {
                var paramName = clause.Name.Identifier.Text;
                var constraints = new List<TypeConstraintNode>();

                foreach (var constraint in clause.Constraints)
                {
                    var constraintNode = ConvertTypeConstraint(constraint);
                    if (constraintNode != null)
                    {
                        constraints.Add(constraintNode);
                    }
                }

                if (constraints.Count > 0)
                {
                    constraintMap[paramName] = constraints;
                    _context.RecordFeatureUsage("generic-constraints");
                }
            }
        }

        return typeParamList.Parameters
            .Select(p =>
            {
                var variance = p.VarianceKeyword.IsKind(SyntaxKind.OutKeyword) ? Ast.VarianceKind.Out
                    : p.VarianceKeyword.IsKind(SyntaxKind.InKeyword) ? Ast.VarianceKind.In
                    : Ast.VarianceKind.None;
                return new TypeParameterNode(
                    GetTextSpan(p),
                    p.Identifier.Text,
                    constraintMap.TryGetValue(p.Identifier.Text, out var constraints)
                        ? constraints
                        : Array.Empty<TypeConstraintNode>(),
                    variance);
            })
            .ToList();
    }

    private TypeConstraintNode? ConvertTypeConstraint(TypeParameterConstraintSyntax constraint)
    {
        var span = GetTextSpan(constraint);

        return constraint switch
        {
            ClassOrStructConstraintSyntax classOrStruct =>
                classOrStruct.ClassOrStructKeyword.IsKind(SyntaxKind.ClassKeyword)
                    ? new TypeConstraintNode(span, TypeConstraintKind.Class)
                    : new TypeConstraintNode(span, TypeConstraintKind.Struct),

            ConstructorConstraintSyntax =>
                new TypeConstraintNode(span, TypeConstraintKind.New),

            // C# 8+ 'notnull' constraint: where T : notnull
            // In Roslyn, this comes through as TypeConstraintSyntax with type text "notnull"
            TypeConstraintSyntax typeConstraint when typeConstraint.Type.ToString() == "notnull" =>
                new TypeConstraintNode(span, TypeConstraintKind.NotNull),

            TypeConstraintSyntax typeConstraint =>
                new TypeConstraintNode(span, TypeConstraintKind.TypeName, typeConstraint.Type.ToString()),

            DefaultConstraintSyntax =>
                // 'default' constraint (C# 9+) - no direct Calor equivalent, skip
                null,

            _ => null
        };
    }

    private IReadOnlyList<ParameterNode> ConvertParameters(ParameterListSyntax paramList)
    {
        return paramList.Parameters
            .Select(p =>
            {
                var modifier = ParameterModifier.None;
                if (p.Modifiers.Any(SyntaxKind.ThisKeyword)) modifier |= ParameterModifier.This;
                if (p.Modifiers.Any(SyntaxKind.RefKeyword)) modifier |= ParameterModifier.Ref;
                if (p.Modifiers.Any(SyntaxKind.OutKeyword)) modifier |= ParameterModifier.Out;
                if (p.Modifiers.Any(SyntaxKind.InKeyword)) modifier |= ParameterModifier.In;
                if (p.Modifiers.Any(SyntaxKind.ParamsKeyword)) modifier |= ParameterModifier.Params;
                ExpressionNode? defaultValue = null;
                if (p.Default != null)
                {
                    defaultValue = ConvertExpression(p.Default.Value);
                    _context.RecordFeatureUsage("default-parameter");
                }
                var paramAttrs = ConvertAttributes(p.AttributeLists);
                return new ParameterNode(
                    GetTextSpan(p),
                    p.Identifier.ValueText,
                    TypeMapper.CSharpToCalor(p.Type?.ToString() ?? "any"),
                    modifier,
                    new AttributeCollection(),
                    paramAttrs,
                    defaultValue);
            })
            .ToList();
    }

    /// <summary>
    /// Gets the source text for an enum member's value, preserving hex notation and other
    /// literal representations that would be lost by calling Value.ToString().
    /// </summary>
    private static string? GetEnumMemberValueText(EqualsValueClauseSyntax? equalsValue)
    {
        if (equalsValue == null)
            return null;

        // Use source text for literals to preserve hex notation (0xFF vs 255)
        if (equalsValue.Value is LiteralExpressionSyntax literal)
            return literal.Token.Text;

        // For compound expressions (bitwise OR, shifts, etc.), use ToString()
        return equalsValue.Value.ToString();
    }

    private static Visibility GetVisibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
            return Visibility.Public;
        // Check compound modifiers before individual ones
        if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.InternalKeyword))
            return Visibility.ProtectedInternal;
        if (modifiers.Any(SyntaxKind.InternalKeyword))
            return Visibility.Internal;
        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
            return Visibility.Protected;
        return Visibility.Private;
    }

    private static Visibility GetVisibility(SyntaxTokenList modifiers, Visibility defaultVisibility)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
            return Visibility.Public;
        // Check compound modifiers before individual ones
        if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.InternalKeyword))
            return Visibility.ProtectedInternal;
        if (modifiers.Any(SyntaxKind.InternalKeyword))
            return Visibility.Internal;
        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
            return Visibility.Protected;
        if (modifiers.Any(SyntaxKind.PrivateKeyword))
            return Visibility.Private;
        return defaultVisibility;
    }

    /// <summary>
    /// Walks already-converted AST statements and infers effects.
    /// Returns an EffectsNode if any effects are found, null otherwise.
    /// </summary>
    private static EffectsNode? InferEffectsFromBody(IReadOnlyList<StatementNode> body)
    {
        var effects = new Dictionary<string, string>();
        InferEffectsFromStatements(body, effects);
        if (effects.Count == 0)
            return null;
        return new EffectsNode(new TextSpan(0, 0, 0, 0), effects);
    }

    private static void InferEffectsFromStatements(IEnumerable<StatementNode> statements, Dictionary<string, string> effects)
    {
        foreach (var stmt in statements)
        {
            InferEffectsFromStatement(stmt, effects);
        }
    }

    /// <summary>
    /// Adds an effect value to a category, appending comma-separated if the category already has a value.
    /// </summary>
    private static void AddEffect(Dictionary<string, string> effects, string category, string value)
    {
        if (effects.TryGetValue(category, out var existing))
        {
            // Check if this value is already present (avoid duplicates)
            var existingValues = existing.Split(',');
            if (!existingValues.Contains(value, StringComparer.Ordinal))
            {
                effects[category] = existing + "," + value;
            }
        }
        else
        {
            effects[category] = value;
        }
    }

    private static void InferEffectsFromStatement(StatementNode statement, Dictionary<string, string> effects)
    {
        switch (statement)
        {
            case PrintStatementNode:
                AddEffect(effects, "io", "console_write");
                break;
            case ThrowStatementNode:
            case RethrowStatementNode:
                AddEffect(effects, "exception", "intentional");
                break;
            case CallStatementNode call:
                InferEffectsFromCallTarget(call.Target, effects);
                foreach (var arg in call.Arguments)
                    InferEffectsFromExpression(arg, effects);
                break;
            case IfStatementNode ifStmt:
                InferEffectsFromStatements(ifStmt.ThenBody, effects);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                    InferEffectsFromStatements(elseIf.Body, effects);
                if (ifStmt.ElseBody != null)
                    InferEffectsFromStatements(ifStmt.ElseBody, effects);
                InferEffectsFromExpression(ifStmt.Condition, effects);
                break;
            case ForStatementNode forStmt:
                InferEffectsFromStatements(forStmt.Body, effects);
                break;
            case WhileStatementNode whileStmt:
                InferEffectsFromStatements(whileStmt.Body, effects);
                InferEffectsFromExpression(whileStmt.Condition, effects);
                break;
            case DoWhileStatementNode doWhileStmt:
                InferEffectsFromStatements(doWhileStmt.Body, effects);
                InferEffectsFromExpression(doWhileStmt.Condition, effects);
                break;
            case ForeachStatementNode foreachStmt:
                InferEffectsFromStatements(foreachStmt.Body, effects);
                break;
            case TryStatementNode tryStmt:
                InferEffectsFromStatements(tryStmt.TryBody, effects);
                foreach (var catchClause in tryStmt.CatchClauses)
                    InferEffectsFromStatements(catchClause.Body, effects);
                if (tryStmt.FinallyBody != null)
                    InferEffectsFromStatements(tryStmt.FinallyBody, effects);
                break;
            case MatchStatementNode matchStmt:
                foreach (var matchCase in matchStmt.Cases)
                    InferEffectsFromStatements(matchCase.Body, effects);
                break;
            case BindStatementNode bind:
                if (bind.Initializer != null)
                    InferEffectsFromExpression(bind.Initializer, effects);
                break;
            case ReturnStatementNode ret:
                if (ret.Expression != null)
                    InferEffectsFromExpression(ret.Expression, effects);
                break;
            case AssignmentStatementNode assign:
                InferEffectsFromExpression(assign.Value, effects);
                break;
        }
    }

    private static void InferEffectsFromExpression(ExpressionNode expr, Dictionary<string, string> effects)
    {
        switch (expr)
        {
            case CallExpressionNode callExpr:
                InferEffectsFromCallTarget(callExpr.Target, effects);
                foreach (var arg in callExpr.Arguments)
                    InferEffectsFromExpression(arg, effects);
                break;
            case BinaryOperationNode binOp:
                InferEffectsFromExpression(binOp.Left, effects);
                InferEffectsFromExpression(binOp.Right, effects);
                break;
            case ConditionalExpressionNode condExpr:
                InferEffectsFromExpression(condExpr.Condition, effects);
                InferEffectsFromExpression(condExpr.WhenTrue, effects);
                InferEffectsFromExpression(condExpr.WhenFalse, effects);
                break;
            case MatchExpressionNode matchExpr:
                foreach (var matchCase in matchExpr.Cases)
                    InferEffectsFromStatements(matchCase.Body, effects);
                break;
        }
    }

    private static void InferEffectsFromCallTarget(string target, Dictionary<string, string> effects)
    {
        var effectInfo = EffectChecker.TryGetKnownEffect(target);
        if (effectInfo != null)
        {
            var category = effectInfo.Kind switch
            {
                EffectKind.IO => "io",
                EffectKind.Mutation => "mutation",
                EffectKind.Nondeterminism => "nondeterminism",
                EffectKind.Exception => "exception",
                EffectKind.Memory => "memory",
                _ => "unknown"
            };
            AddEffect(effects, category, effectInfo.Value);
        }
    }

    // --- Unsafe/Low-Level conversions ---

    private ExpressionNode ConvertStackAllocExpression(StackAllocArrayCreationExpressionSyntax stackAlloc)
    {
        _context.RecordFeatureUsage("stackalloc");
        var elementType = TypeMapper.CSharpToCalor(stackAlloc.Type is ArrayTypeSyntax arrayType
            ? arrayType.ElementType.ToString()
            : stackAlloc.Type.ToString());
        ExpressionNode? size = null;
        var initializer = new List<ExpressionNode>();

        if (stackAlloc.Type is ArrayTypeSyntax at && at.RankSpecifiers.Count > 0)
        {
            var rank = at.RankSpecifiers[0];
            if (rank.Sizes.Count > 0 && rank.Sizes[0] is ExpressionSyntax sizeExpr
                && sizeExpr is not OmittedArraySizeExpressionSyntax)
            {
                size = ConvertExpression(sizeExpr);
            }
        }

        if (stackAlloc.Initializer != null)
        {
            initializer = stackAlloc.Initializer.Expressions
                .Select(ConvertExpression)
                .ToList();
        }

        return new StackAllocNode(GetTextSpan(stackAlloc), elementType, size, initializer);
    }

    private ExpressionNode ConvertImplicitStackAllocExpression(ImplicitStackAllocArrayCreationExpressionSyntax implicitStackAlloc)
    {
        _context.RecordFeatureUsage("stackalloc");
        var initializer = implicitStackAlloc.Initializer.Expressions
            .Select(ConvertExpression)
            .ToList();
        // Infer type from first element or use "i32" as default
        return new StackAllocNode(GetTextSpan(implicitStackAlloc), "i32", null, initializer);
    }

    private ExpressionNode ConvertSizeOfExpression(SizeOfExpressionSyntax sizeOf)
    {
        _context.RecordFeatureUsage("unsafe");
        var typeName = TypeMapper.CSharpToCalor(sizeOf.Type.ToString());
        return new SizeOfNode(GetTextSpan(sizeOf), typeName);
    }

    private ExpressionNode ConvertAddressOfExpression(PrefixUnaryExpressionSyntax addrOf)
    {
        _context.RecordFeatureUsage("pointer");
        var operand = ConvertExpression(addrOf.Operand);
        return new AddressOfNode(GetTextSpan(addrOf), operand);
    }

    private ExpressionNode ConvertPointerDereferenceExpression(PrefixUnaryExpressionSyntax deref)
    {
        _context.RecordFeatureUsage("pointer");
        var operand = ConvertExpression(deref.Operand);
        return new PointerDereferenceNode(GetTextSpan(deref), operand);
    }

    private StatementNode ConvertUnsafeStatement(UnsafeStatementSyntax unsafeStmt)
    {
        _context.RecordFeatureUsage("unsafe");
        var id = _context.GenerateId("unsafe");
        var body = ConvertBlock(unsafeStmt.Block);
        return new UnsafeBlockNode(GetTextSpan(unsafeStmt), id, body);
    }

    private StatementNode ConvertFixedStatement(FixedStatementSyntax fixedStmt)
    {
        _context.RecordFeatureUsage("fixed");
        var id = _context.GenerateId("fixed");

        // Parse the declaration from fixed (type* name = expr)
        var decl = fixedStmt.Declaration;
        var pointerType = TypeMapper.CSharpToCalor(decl.Type.ToString());
        var variable = decl.Variables[0];
        var pointerName = variable.Identifier.Text;
        var initializer = variable.Initializer != null
            ? ConvertExpression(variable.Initializer.Value)
            : new ReferenceNode(GetTextSpan(fixedStmt), "null");

        var body = fixedStmt.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(fixedStmt.Statement)! };

        return new FixedStatementNode(GetTextSpan(fixedStmt), id, pointerName, pointerType, initializer, body);
    }

    // --- Multidimensional array conversions ---

    private ExpressionNode ConvertMultiDimArrayCreation(ArrayCreationExpressionSyntax arrayCreation, int rank)
    {
        _context.RecordFeatureUsage("multidim-array");
        var id = _context.GenerateId("arr2d");
        var name = _context.GenerateId("arr2d");
        var elementType = TypeMapper.CSharpToCalor(arrayCreation.Type.ElementType.ToString());

        var dimensionSizes = new List<ExpressionNode>();
        var initializer = new List<IReadOnlyList<ExpressionNode>>();

        if (arrayCreation.Type.RankSpecifiers.Count > 0)
        {
            var rankSpec = arrayCreation.Type.RankSpecifiers[0];
            foreach (var sizeExpr in rankSpec.Sizes)
            {
                if (sizeExpr is not OmittedArraySizeExpressionSyntax)
                    dimensionSizes.Add(ConvertExpression(sizeExpr));
            }
        }

        if (arrayCreation.Initializer != null)
        {
            foreach (var rowExpr in arrayCreation.Initializer.Expressions)
            {
                if (rowExpr is InitializerExpressionSyntax rowInit)
                {
                    var row = rowInit.Expressions.Select(ConvertExpression).ToList();
                    initializer.Add(row);
                }
                else
                {
                    initializer.Add(new List<ExpressionNode> { ConvertExpression(rowExpr) });
                }
            }
        }

        return new MultiDimArrayCreationNode(GetTextSpan(arrayCreation), id, name, elementType, rank, dimensionSizes, initializer);
    }

    private static MethodModifiers GetMethodModifiers(SyntaxTokenList modifiers)
    {
        var result = MethodModifiers.None;

        if (modifiers.Any(SyntaxKind.VirtualKeyword))
            result |= MethodModifiers.Virtual;
        if (modifiers.Any(SyntaxKind.OverrideKeyword))
            result |= MethodModifiers.Override;
        if (modifiers.Any(SyntaxKind.AbstractKeyword))
            result |= MethodModifiers.Abstract;
        if (modifiers.Any(SyntaxKind.SealedKeyword))
            result |= MethodModifiers.Sealed;
        if (modifiers.Any(SyntaxKind.StaticKeyword))
            result |= MethodModifiers.Static;
        if (modifiers.Any(SyntaxKind.RequiredKeyword))
            result |= MethodModifiers.Required;
        if (modifiers.Any(SyntaxKind.PartialKeyword))
            result |= MethodModifiers.Partial;
        if (modifiers.Any(SyntaxKind.ExternKeyword))
            result |= MethodModifiers.Extern;
        if (modifiers.Any(SyntaxKind.UnsafeKeyword))
            result |= MethodModifiers.Unsafe;

        return result;
    }

    /// <summary>
    /// Unwraps Task/ValueTask types to get the underlying return type.
    /// Task&lt;T&gt; -> T, Task -> void, ValueTask&lt;T&gt; -> T, ValueTask -> void
    /// </summary>
    private static string UnwrapTaskType(string typeName)
    {
        if (typeName.StartsWith("Task<", StringComparison.Ordinal) && typeName.EndsWith(">"))
            return typeName.Substring(5, typeName.Length - 6);
        if (typeName == "Task")
            return "void";
        if (typeName.StartsWith("ValueTask<", StringComparison.Ordinal) && typeName.EndsWith(">"))
            return typeName.Substring(10, typeName.Length - 11);
        if (typeName == "ValueTask")
            return "void";
        return typeName;
    }

    private static TextSpan GetTextSpan(SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        return new TextSpan(
            node.SpanStart,
            node.Span.Length,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    /// <summary>
    /// Creates a fallback expression node for unsupported expressions.
    /// Records the unsupported feature for explanation output.
    /// </summary>
    private FallbackExpressionNode CreateFallbackExpression(SyntaxNode node, string featureName)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        var suggestion = FeatureSupport.GetWorkaround(featureName);

        _context.RecordUnsupportedFeature(featureName, node.ToString(), line, suggestion);

        // Also populate the issues list so fallback nodes are visible in conversion results
        if (_context.GracefulFallback)
        {
            _context.AddWarning(
                $"Unsupported feature [{featureName}] replaced with fallback: {(node.ToString().Length > 80 ? node.ToString().Substring(0, 77) + "..." : node.ToString())}",
                feature: featureName, line: line, suggestion: suggestion);
        }

        return new FallbackExpressionNode(GetTextSpan(node), node.ToString(), featureName, suggestion);
    }

    /// <summary>
    /// Creates a fallback comment node for unsupported statements.
    /// Records the unsupported feature for explanation output.
    /// </summary>
    private StatementNode CreateFallbackStatement(SyntaxNode node, string featureName)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        var suggestion = FeatureSupport.GetWorkaround(featureName);

        _context.RecordUnsupportedFeature(featureName, node.ToString(), line, suggestion);

        // Also populate the issues list so fallback nodes are visible in conversion results
        if (_context.GracefulFallback)
        {
            _context.AddWarning(
                $"Unsupported feature [{featureName}] replaced with fallback: {(node.ToString().Length > 80 ? node.ToString().Substring(0, 77) + "..." : node.ToString())}",
                feature: featureName, line: line, suggestion: suggestion);
        }

        // When PassthroughOnError is enabled, wrap in §CSHARP block instead of TODO comment
        if (_context.PassthroughOnError)
        {
            return new RawCSharpNode(GetTextSpan(node), node.ToFullString());
        }

        return new FallbackCommentNode(GetTextSpan(node), node.ToString(), featureName, suggestion);
    }

    /// <summary>
    /// Creates a CSharpInteropBlockNode wrapping an unsupported member's source code.
    /// Used in Interop conversion mode.
    /// </summary>
    private CSharpInteropBlockNode CreateInteropBlock(SyntaxNode node, string? featureName, InteropMemberKind kind)
    {
        // Use ToString() (without trivia) when the node lives inside a namespace.
        // ToFullString() can capture leading trivia that bleeds namespace context,
        // causing duplicate namespace wrappers since the module tag already provides one.
        var sourceCode = node.Parent is BaseNamespaceDeclarationSyntax
            ? node.ToString()
            : node.ToFullString();
        var lineSpan = node.GetLocation().GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;

        // Determine feature name from syntax kind if not provided
        featureName ??= node.Kind().ToString().Replace("Declaration", "").Replace("Syntax", "").ToLowerInvariant();

        var reason = $"Unsupported {kind}: {(node.ToString().Length > 80 ? node.ToString().Substring(0, 77) + "..." : node.ToString())}";

        _context.RecordUnsupportedFeature(featureName, node.ToString(), line);
        _context.Stats.InteropBlocksEmitted++;
        _context.AddInfo($"C# interop block preserved for [{featureName}]", feature: featureName, line: line);

        return new CSharpInteropBlockNode(GetTextSpan(node), sourceCode, featureName, reason, kind);
    }

    /// <summary>
    /// Heuristic to determine if an expression looks like an event target.
    /// Event targets are typically member accesses ending with an event-like name
    /// (often capitalized, like Click, Changed, etc.) rather than simple fields.
    /// </summary>
    private static bool LooksLikeEventTarget(ExpressionSyntax expr)
    {
        // Simple heuristic: if it's a member access and the member name looks like an event
        // (PascalCase, often ending in common event suffixes), treat it as an event
        if (expr is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;
            // Common event naming patterns - PascalCase names are often events
            // This is a heuristic; we don't have type information in this phase
            return char.IsUpper(memberName.FirstOrDefault()) &&
                   (memberName.EndsWith("Changed") ||
                    memberName.EndsWith("Click") ||
                    memberName.EndsWith("Event") ||
                    memberName.EndsWith("Handler") ||
                    memberName.EndsWith("Completed") ||
                    memberName.EndsWith("Started") ||
                    memberName.EndsWith("Finished") ||
                    memberName.EndsWith("Raised") ||
                    memberName.EndsWith("Occurred") ||
                    memberName.EndsWith("Triggered") ||
                    memberName.EndsWith("Request") ||
                    memberName.EndsWith("Response") ||
                    memberName.Contains("Event"));
        }

        return false;
    }

    /// <summary>
    /// Converts C# attributes to Calor attributes.
    /// </summary>
    private IReadOnlyList<CalorAttributeNode> ConvertAttributes(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var result = new List<CalorAttributeNode>();

        foreach (var attrList in attributeLists)
        {
            // Extract the attribute target (e.g., "return", "assembly", "field")
            var target = attrList.Target?.Identifier.Text;

            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                var args = new List<CalorAttributeArgument>();

                if (attr.ArgumentList != null)
                {
                    foreach (var arg in attr.ArgumentList.Arguments)
                    {
                        var argName = arg.NameEquals?.Name.ToString();
                        var value = ConvertAttributeValue(arg.Expression);

                        if (argName != null)
                        {
                            args.Add(new CalorAttributeArgument(argName, value));
                        }
                        else
                        {
                            args.Add(new CalorAttributeArgument(value));
                        }
                    }
                }

                result.Add(new CalorAttributeNode(GetTextSpan(attr), name, args, target));

                // Track COM interop and P/Invoke attribute usage
                if (name is "ComImport" or "Guid" or "InterfaceType" or "CoClass"
                    or "ComVisible" or "ProgId" or "ClassInterface")
                {
                    _context.RecordFeatureUsage("com-interop");
                }
                else if (name is "DllImport" or "MarshalAs" or "StructLayout" or "FieldOffset")
                {
                    _context.RecordFeatureUsage("dllimport");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Converts an attribute argument expression to an object value.
    /// </summary>
    private object ConvertAttributeValue(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => literal.Token.Value ?? literal.Token.Text,
            TypeOfExpressionSyntax typeOf => new TypeOfReference(typeOf.Type.ToString()),
            MemberAccessExpressionSyntax memberAccess => new MemberAccessReference(memberAccess.ToString()),
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.BitwiseOrExpression)
                => ConvertBitwiseBinaryExpression(binary, BitwiseOperator.Or),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.BitwiseAndExpression)
                => ConvertBitwiseBinaryExpression(binary, BitwiseOperator.And),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.ExclusiveOrExpression)
                => ConvertBitwiseBinaryExpression(binary, BitwiseOperator.Xor),
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.BitwiseNotExpression)
                => new BitwiseNotExpression(ConvertAttributeValue(prefix.Operand)),
            ParenthesizedExpressionSyntax paren => ConvertAttributeValue(paren.Expression),
            InvocationExpressionSyntax invocation
                when invocation.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" }
                => new NameOfReference(invocation.ArgumentList.Arguments[0].Expression.ToString()),
            _ => expression.ToString()
        };
    }

    private BitwiseBinaryExpression ConvertBitwiseBinaryExpression(BinaryExpressionSyntax binary, BitwiseOperator op)
    {
        var left = ConvertAttributeValue(binary.Left);
        var right = ConvertAttributeValue(binary.Right);
        return new BitwiseBinaryExpression(left, op, right);
    }

    /// <summary>
    /// Extracts the type name from an object creation expression for use as a naming hint.
    /// </summary>
    private static string ExtractTypeHint(ExpressionSyntax expression)
    {
        return expression switch
        {
            ObjectCreationExpressionSyntax objCreation => objCreation.Type.ToString().Split('.').Last().Split('<').First(),
            ImplicitObjectCreationExpressionSyntax => "",
            _ => ""
        };
    }

    /// <summary>
    /// Extracts the target type from a cast expression for use as a naming hint.
    /// </summary>
    private static string ExtractCastTypeHint(ExpressionSyntax expression)
    {
        if (expression is CastExpressionSyntax cast)
            return cast.Type.ToString().Split('.').Last().Split('<').First();
        if (expression is ParenthesizedExpressionSyntax paren && paren.Expression is CastExpressionSyntax innerCast)
            return innerCast.Type.ToString().Split('.').Last().Split('<').First();
        return "";
    }

    /// <summary>
    /// Extracts the method name from an invocation expression for use as a naming hint.
    /// </summary>
    private static string ExtractInnerMethodName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.Text;
        if (invocation.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.Text;
        return "";
    }
}
