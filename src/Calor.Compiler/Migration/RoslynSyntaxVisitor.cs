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
    private HashSet<string> _reassignedVariables = new();

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
        _reassignedVariables = CollectReassignedVariables(root);

        // Visit all nodes
        Visit(root);

        var moduleId = _context.GenerateId("m");

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
            _delegates,
            functions,
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            null);
    }

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name != null)
        {
            var namespaceName = node.Name.ToString();
            var isStatic = node.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
            var alias = node.Alias?.Name.ToString();

            _usings.Add(new UsingDirectiveNode(
                GetTextSpan(node),
                namespaceName,
                alias,
                isStatic));
        }

        base.VisitUsingDirective(node);
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
        _context.RecordFeatureUsage("interface");
        _context.EnterType(node.Identifier.Text);

        var id = _context.GenerateId("i");
        var name = node.Identifier.Text;
        var baseInterfaces = node.BaseList?.Types
            .Select(t => t.Type.ToString())
            .ToList() ?? new List<string>();
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        var methods = new List<MethodSignatureNode>();
        foreach (var member in node.Members)
        {
            if (member is MethodDeclarationSyntax methodSyntax)
            {
                methods.Add(ConvertMethodSignature(methodSyntax));
            }
        }

        var interfaceNode = new InterfaceDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            baseInterfaces,
            methods,
            new AttributeCollection(),
            csharpAttrs);

        _interfaces.Add(interfaceNode);
        _context.Stats.InterfacesConverted++;
        _context.IncrementConverted();
        _context.ExitType();
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("class");
        _context.EnterType(node.Identifier.Text);

        var classNode = ConvertClass(node);
        _classes.Add(classNode);
        _context.Stats.ClassesConverted++;
        _context.IncrementConverted();
        _context.ExitType();
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("record");
        _context.EnterType(node.Identifier.Text);

        // Treat records as classes for conversion
        var classNode = ConvertRecord(node);
        _classes.Add(classNode);
        _context.Stats.ClassesConverted++;
        _context.IncrementConverted();
        _context.ExitType();
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("struct");
        _context.EnterType(node.Identifier.Text);

        // Treat structs as classes for conversion
        var classNode = ConvertStruct(node);
        _classes.Add(classNode);
        _context.Stats.ClassesConverted++;
        _context.IncrementConverted();
        _context.ExitType();
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("enum");

        var id = _context.GenerateId("e");
        var name = node.Identifier.Text;

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
            var memberValue = member.EqualsValue?.Value.ToString();
            members.Add(new EnumMemberNode(GetTextSpan(member), memberName, memberValue));
        }

        var enumNode = new EnumDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            underlyingType,
            members,
            new AttributeCollection());

        _enums.Add(enumNode);
        _context.Stats.EnumsConverted++;
        _context.IncrementConverted();
    }

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("delegate");

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
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var events = new List<EventDefinitionNode>();

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
                case ConstructorDeclarationSyntax ctorSyntax:
                    constructors.Add(ConvertConstructor(ctorSyntax));
                    break;
                case MethodDeclarationSyntax methodSyntax:
                    methods.Add(ConvertMethod(methodSyntax));
                    break;
                case OperatorDeclarationSyntax opDecl:
                    methods.Add(ConvertOperator(opDecl));
                    break;
                case ConversionOperatorDeclarationSyntax convDecl:
                    methods.Add(ConvertConversionOperator(convDecl));
                    break;
                case EventFieldDeclarationSyntax eventSyntax:
                    events.AddRange(ConvertEventFields(eventSyntax));
                    break;
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
            new AttributeCollection(),
            csharpAttrs,
            visibility: visibility);
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
            foreach (var param in node.ParameterList.Parameters)
            {
                var propId = _context.GenerateId("p");
                var propName = param.Identifier.Text;
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
                case ConstructorDeclarationSyntax ctorSyntax:
                    constructors.Add(ConvertConstructor(ctorSyntax));
                    break;
                case MethodDeclarationSyntax methodSyntax:
                    methods.Add(ConvertMethod(methodSyntax));
                    break;
                case OperatorDeclarationSyntax opDecl:
                    methods.Add(ConvertOperator(opDecl));
                    break;
                case ConversionOperatorDeclarationSyntax convDecl:
                    methods.Add(ConvertConversionOperator(convDecl));
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
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>(),
            visibility: visibility);
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
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var events = new List<EventDefinitionNode>();

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
                case ConstructorDeclarationSyntax ctorSyntax:
                    constructors.Add(ConvertConstructor(ctorSyntax));
                    break;
                case MethodDeclarationSyntax methodSyntax:
                    methods.Add(ConvertMethod(methodSyntax));
                    break;
                case OperatorDeclarationSyntax opDecl:
                    methods.Add(ConvertOperator(opDecl));
                    break;
                case ConversionOperatorDeclarationSyntax convDecl:
                    methods.Add(ConvertConversionOperator(convDecl));
                    break;
                case EventFieldDeclarationSyntax eventSyntax:
                    events.AddRange(ConvertEventFields(eventSyntax));
                    break;
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
            new AttributeCollection(),
            csharpAttrs,
            isStruct: true,
            isReadOnly: isReadOnly,
            visibility: visibility);
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
        var name = node.Identifier.Text;
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

        var returnType = TypeMapper.CSharpToCalor(returnTypeStr);
        var output = returnType != "void" ? new OutputNode(GetTextSpan(node.ReturnType), returnType) : null;
        var body = ConvertMethodBody(node.Body, node.ExpressionBody);
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

    private PropertyNode ConvertProperty(PropertyDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("property");

        var name = node.Identifier.Text;
        var typeName = TypeMapper.CSharpToCalor(node.Type.ToString());
        var visibility = GetVisibility(node.Modifiers);
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

    private IReadOnlyList<StatementNode> ConvertAccessorBody(AccessorDeclarationSyntax accessor)
    {
        if (accessor.Body != null)
        {
            return ConvertBlock(accessor.Body);
        }
        else if (accessor.ExpressionBody != null)
        {
            return new List<StatementNode>
            {
                new ReturnStatementNode(
                    GetTextSpan(accessor.ExpressionBody),
                    ConvertExpression(accessor.ExpressionBody.Expression))
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

    private IReadOnlyList<StatementNode> ConvertBlock(BlockSyntax block)
    {
        var statements = new List<StatementNode>();

        foreach (var statement in block.Statements)
        {
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
                statements.AddRange(DecomposeChainedLocalDeclaration(chainDecl));
                FlushPendingStatements(statements);
                continue;
            }

            // Handle chained method calls in expression statements (e.g., a.Where(...).ToList())
            // Skip chains handled by native operations (string, StringBuilder, regex, char)
            if (statement is ExpressionStatementSyntax exprStmt
                && exprStmt.Expression is InvocationExpressionSyntax chainExpr
                && IsChainedInvocation(chainExpr)
                && !WouldChainUseNativeOps(chainExpr))
            {
                statements.AddRange(DecomposeChainedExpressionStatement(exprStmt));
                FlushPendingStatements(statements);
                continue;
            }

            // Handle chained method calls in return statements (e.g., return items.Where(...).First())
            // Skip chains handled by native operations (string, StringBuilder, regex, char)
            if (statement is ReturnStatementSyntax returnStmt
                && returnStmt.Expression is InvocationExpressionSyntax returnChain
                && IsChainedInvocation(returnChain)
                && !WouldChainUseNativeOps(returnChain))
            {
                statements.AddRange(DecomposeChainedReturnStatement(returnStmt));
                FlushPendingStatements(statements);
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

            // Handle local functions by hoisting to module-level §F functions
            if (statement is LocalFunctionStatementSyntax localFunc)
            {
                _context.RecordFeatureUsage("local-function");
                var hoisted = ConvertLocalFunction(localFunc);
                _functions.Add(hoisted);
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
            UsingStatementSyntax usingStmt => ConvertUsingStatement(usingStmt),
            YieldStatementSyntax yieldStmt => ConvertYieldStatement(yieldStmt),
            LockStatementSyntax lockStmt => ConvertLockStatement(lockStmt),
            _ => HandleUnsupportedStatement(statement)
        };
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
        _context.RecordFeatureUsage("lock");
        _context.IncrementConverted();

        // Calor doesn't have a lock construct, so we preserve the body statements
        // with a comment annotation indicating the lock
        var bodyStatements = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

        // Add body statements to pending so they're emitted inline
        foreach (var stmt in bodyStatements)
        {
            _pendingStatements.Add(stmt);
        }

        // Return a fallback comment node indicating the lock
        var lockExpr = node.Expression.ToString();
        return new FallbackCommentNode(GetTextSpan(node), $"lock({lockExpr})", "lock", "Lock semantics are preserved but lock keyword is not emitted");
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

            // Handle null-coalescing assignment: x ??= y → if (== x null) { x = y }
            if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            {
                _context.RecordFeatureUsage("null-coalescing-assignment");
                var target = ConvertExpression(assignment.Left);
                var value = ConvertExpression(assignment.Right);
                var nullCheck = new BinaryOperationNode(
                    GetTextSpan(node),
                    BinaryOperator.Equal,
                    target,
                    new ReferenceNode(GetTextSpan(node), "null"));
                var assignStmt = new AssignmentStatementNode(
                    GetTextSpan(node),
                    ConvertExpression(assignment.Left),
                    value);
                return new IfStatementNode(
                    GetTextSpan(node),
                    _context.GenerateId("if"),
                    nullCheck,
                    new List<StatementNode> { assignStmt },
                    Array.Empty<ElseIfClauseNode>(),
                    null,
                    new AttributeCollection());
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
                new AttributeCollection());
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
            && memberAccess.Expression is InvocationExpressionSyntax;
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

            if (memberAccess.Expression is InvocationExpressionSyntax inner)
            {
                // Intermediate step — target comes from previous chain step
                steps.Add((null, methodName, args, span));
                current = inner;
            }
            else
            {
                // Base of the chain — has a concrete target
                var baseTarget = memberAccess.Expression.ToString();
                steps.Add((baseTarget, methodName, args, span));
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
            var tempName = _context.GenerateId("_chain");

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
            var tempName = _context.GenerateId("_chain");

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
                var tempName = _context.GenerateId("_chain");
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

        // Collect any pending statements from declaration pattern variable bindings
        // These need to be injected at the START of the then-body, not before the if
        var patternBindings = new List<StatementNode>();
        if (_pendingStatements.Count > 0)
        {
            patternBindings.AddRange(_pendingStatements);
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

        return new IfStatementNode(
            GetTextSpan(node),
            id,
            condition,
            thenBody,
            elseIfClauses,
            elseBody,
            new AttributeCollection());
    }

    private ForStatementNode ConvertForStatement(ForStatementSyntax node)
    {
        _context.RecordFeatureUsage("for");
        _context.IncrementConverted();

        var id = _context.GenerateId("for");

        // Try to extract standard for loop pattern: for (var i = from; i <= to; i += step)
        var varName = "i";
        ExpressionNode from = new IntLiteralNode(TextSpan.Empty, 0);
        ExpressionNode to = new IntLiteralNode(TextSpan.Empty, 10);
        ExpressionNode? step = null;

        // Extract variable name and initial value from declaration
        if (node.Declaration?.Variables.Count > 0)
        {
            var decl = node.Declaration.Variables[0];
            varName = decl.Identifier.Text;
            if (decl.Initializer != null)
            {
                from = ConvertExpression(decl.Initializer.Value);
            }
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

        // Extract step from incrementors
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

        return new ForStatementNode(
            GetTextSpan(node),
            id,
            varName,
            from,
            to,
            step,
            body,
            new AttributeCollection());
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

        foreach (var section in node.Sections)
        {
            foreach (var label in section.Labels)
            {
                PatternNode pattern = label switch
                {
                    CaseSwitchLabelSyntax caseLabel => new LiteralPatternNode(
                        GetTextSpan(caseLabel),
                        ConvertExpression(caseLabel.Value)),
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

        foreach (var arm in node.Arms)
        {
            var pattern = ConvertPattern(arm.Pattern);
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

            // Binary patterns: and, or (emit as raw text for now)
            BinaryPatternSyntax binaryPattern =>
                HandleUnsupportedPattern(binaryPattern, "binary pattern (and/or)"),

            // Unary pattern: not null
            UnaryPatternSyntax unaryPattern =>
                HandleUnsupportedPattern(unaryPattern, "unary pattern (not)"),

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

        return expression switch
        {
            LiteralExpressionSyntax literal => ConvertLiteral(literal),
            IdentifierNameSyntax identifier => new ReferenceNode(GetTextSpan(identifier), identifier.Identifier.ValueText),
            BinaryExpressionSyntax binary => ConvertBinaryExpression(binary),
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
            PredefinedTypeSyntax predefined => new ReferenceNode(GetTextSpan(predefined), predefined.Keyword.Text),
            DeclarationExpressionSyntax declExpr => ConvertDeclarationExpression(declExpr),
            _ => CreateFallbackExpression(expression, "unknown-expression")
        };
    }

    private ExpressionNode ConvertLiteral(LiteralExpressionSyntax literal)
    {
        return literal.Kind() switch
        {
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is int intVal =>
                new IntLiteralNode(GetTextSpan(literal), intVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is double doubleVal =>
                new FloatLiteralNode(GetTextSpan(literal), doubleVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is float floatVal =>
                new FloatLiteralNode(GetTextSpan(literal), floatVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is decimal decVal =>
                new DecimalLiteralNode(GetTextSpan(literal), decVal),
            SyntaxKind.NumericLiteralExpression when literal.Token.Value is long longVal =>
                new IntLiteralNode(GetTextSpan(literal), (int)longVal),
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

        // Handle null-coalescing operator: x ?? y → (if (== x null) y x)
        if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            _context.RecordFeatureUsage("null-coalescing");
            var left = ConvertExpression(binary.Left);
            var right = ConvertExpression(binary.Right);
            var nullCheck = new BinaryOperationNode(
                GetTextSpan(binary),
                BinaryOperator.Equal,
                left,
                new ReferenceNode(GetTextSpan(binary), "null"));
            return new ConditionalExpressionNode(GetTextSpan(binary), nullCheck, right, left);
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
            DeclarationPatternSyntax declPattern =>
                ConvertDeclarationPattern(isPattern, left, declPattern),
            _ =>
                // For other patterns, create a fallback expression
                CreateFallbackExpression(isPattern, "complex-is-pattern")
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
                // Spread elements like ..otherArray - not supported in Calor
                return CreateFallbackExpression(collection, "collection-spread");
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
        // Convert target-typed new: new() or new(args)
        // Use "default" for parameterless, otherwise emit NewExpressionNode with "object" placeholder
        if (implicitNew.ArgumentList == null || implicitNew.ArgumentList.Arguments.Count == 0)
        {
            return new ReferenceNode(GetTextSpan(implicitNew), "default");
        }

        _context.RecordFeatureUsage("target-typed-new");
        _context.IncrementConverted();
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
        return new NewExpressionNode(GetTextSpan(implicitNew), "object", new List<string>(), args, initializers);
    }

    private ExpressionNode ConvertThrowExpression(ThrowExpressionSyntax throwExpr)
    {
        _context.RecordFeatureUsage("throw-expression");
        _context.IncrementConverted();
        var inner = ConvertExpression(throwExpr.Expression);
        if (inner is NewExpressionNode newExpr && newExpr.Arguments.Count > 0)
            return new ErrExpressionNode(GetTextSpan(throwExpr), newExpr.Arguments[0]);
        return new ErrExpressionNode(GetTextSpan(throwExpr), inner);
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

    private UnaryOperationNode ConvertPrefixUnaryExpression(PrefixUnaryExpressionSyntax prefix)
    {
        var operand = ConvertExpression(prefix.Operand);
        var op = prefix.OperatorToken.Text;
        var unaryOp = UnaryOperatorExtensions.FromString(op) ?? UnaryOperator.Negate;

        return new UnaryOperationNode(GetTextSpan(prefix), unaryOp, operand);
    }

    private ExpressionNode ConvertPostfixUnaryExpression(PostfixUnaryExpressionSyntax postfix)
    {
        var operand = ConvertExpression(postfix.Operand);

        // Handle null-forgiving operator (!) - just return the operand since Calor doesn't have this concept
        if (postfix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            return operand;
        }

        // Convert postfix ++ and -- to UnaryOperationNode
        if (postfix.OperatorToken.IsKind(SyntaxKind.PlusPlusToken))
        {
            return new UnaryOperationNode(GetTextSpan(postfix), UnaryOperator.PostIncrement, operand);
        }
        if (postfix.OperatorToken.IsKind(SyntaxKind.MinusMinusToken))
        {
            return new UnaryOperationNode(GetTextSpan(postfix), UnaryOperator.PostDecrement, operand);
        }

        // For other postfix operators, use fallback
        return CreateFallbackExpression(postfix, "postfix-operator");
    }

    private ExpressionNode ConvertInvocationExpression(InvocationExpressionSyntax invocation)
    {
        var target = invocation.Expression.ToString();
        var args = invocation.ArgumentList.Arguments
            .Select(a => ConvertExpression(a.Expression))
            .ToList();

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
                var tempName = _context.GenerateId("_chain");
                _pendingStatements.Add(new BindStatementNode(
                    span, tempName, null, false, innerConverted, new AttributeCollection()));
                return new CallExpressionNode(span, $"{tempName}.{methodName}", args);
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

        // Convert nameof(x) to string literal "x"
        if (target == "nameof" && invocation.ArgumentList.Arguments.Count == 1)
        {
            var argText = invocation.ArgumentList.Arguments[0].Expression.ToString();
            // nameof returns the last identifier part (e.g., nameof(obj.Prop) => "Prop")
            var lastDot = argText.LastIndexOf('.');
            var nameText = lastDot >= 0 ? argText.Substring(lastDot + 1) : argText;
            return new StringLiteralNode(GetTextSpan(invocation), nameText);
        }

        return new CallExpressionNode(GetTextSpan(invocation), target, args);
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

        // Convert string.Empty to empty string literal ""
        if (memberName == "Empty")
        {
            var targetStr = memberAccess.Expression.ToString();
            if (targetStr == "string" || targetStr == "String")
            {
                return new StringLiteralNode(span, "");
            }
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
            else if (typeName == "Dictionary" && typeArgs.Count == 2 && !hasCtorArgs)
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
        // Ternary is converted to a conditional expression: (? cond then else)
        var condition = ConvertExpression(conditional.Condition);
        var whenTrue = ConvertExpression(conditional.WhenTrue);
        var whenFalse = ConvertExpression(conditional.WhenFalse);

        return new ConditionalExpressionNode(
            GetTextSpan(conditional),
            condition,
            whenTrue,
            whenFalse);
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

    private ArrayCreationNode ConvertArrayCreation(ArrayCreationExpressionSyntax arrayCreation)
    {
        var id = _context.GenerateId("arr");
        var name = _context.GenerateId("arr");
        var elementType = TypeMapper.CSharpToCalor(arrayCreation.Type.ElementType.ToString());

        ExpressionNode? size = null;
        var initializer = new List<ExpressionNode>();

        if (arrayCreation.Type.RankSpecifiers.Count > 0)
        {
            var rank = arrayCreation.Type.RankSpecifiers[0];
            if (rank.Sizes.Count > 0 && rank.Sizes[0] is ExpressionSyntax sizeExpr)
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
        var array = ConvertExpression(elementAccess.Expression);
        var index = ConvertExpression(elementAccess.ArgumentList.Arguments[0].Expression);
        var span = GetTextSpan(elementAccess);

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
                exprBody = ConvertExpression(lambda.ExpressionBody);
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
            new AttributeCollection());
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
            .Select(p => new TypeParameterNode(
                GetTextSpan(p),
                p.Identifier.Text,
                constraintMap.TryGetValue(p.Identifier.Text, out var constraints)
                    ? constraints
                    : Array.Empty<TypeConstraintNode>()))
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
                return new ParameterNode(
                    GetTextSpan(p),
                    p.Identifier.ValueText,
                    TypeMapper.CSharpToCalor(p.Type?.ToString() ?? "any"),
                    modifier,
                    new AttributeCollection(),
                    Array.Empty<CalorAttributeNode>());
            })
            .ToList();
    }

    private static Visibility GetVisibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
            return Visibility.Public;
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
    private FallbackCommentNode CreateFallbackStatement(SyntaxNode node, string featureName)
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

        return new FallbackCommentNode(GetTextSpan(node), node.ToString(), featureName, suggestion);
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

                result.Add(new CalorAttributeNode(GetTextSpan(attr), name, args));
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
            _ => expression.ToString()
        };
    }
}
