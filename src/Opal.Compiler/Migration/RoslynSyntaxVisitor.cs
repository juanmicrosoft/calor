using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Opal.Compiler.Ast;
using Opal.Compiler.Parsing;

namespace Opal.Compiler.Migration;

/// <summary>
/// Roslyn syntax visitor that builds OPAL AST nodes from C# syntax.
/// </summary>
public sealed class RoslynSyntaxVisitor : CSharpSyntaxWalker
{
    private readonly ConversionContext _context;
    private readonly List<UsingDirectiveNode> _usings = new();
    private readonly List<InterfaceDefinitionNode> _interfaces = new();
    private readonly List<ClassDefinitionNode> _classes = new();
    private readonly List<FunctionNode> _functions = new();
    private readonly List<StatementNode> _topLevelStatements = new();

    /// <summary>
    /// Gets the top-level statements collected during conversion (C# 9+ feature).
    /// </summary>
    public IReadOnlyList<StatementNode> TopLevelStatements => _topLevelStatements;

    public RoslynSyntaxVisitor(ConversionContext context) : base(SyntaxWalkerDepth.Node)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Converts a C# compilation unit to an OPAL ModuleNode.
    /// </summary>
    public ModuleNode Convert(CompilationUnitSyntax root, string moduleName)
    {
        _usings.Clear();
        _interfaces.Clear();
        _classes.Clear();
        _functions.Clear();
        _topLevelStatements.Clear();

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
                effects: null,
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
            functions,
            new AttributeCollection());
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
        var statement = ConvertStatement(node.Statement);
        if (statement != null)
        {
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

    private ClassDefinitionNode ConvertClass(ClassDeclarationSyntax node)
    {
        var id = _context.GenerateId("c");
        var name = node.Identifier.Text;
        var isAbstract = node.Modifiers.Any(SyntaxKind.AbstractKeyword);
        var isSealed = node.Modifiers.Any(SyntaxKind.SealedKeyword);
        var isPartial = node.Modifiers.Any(SyntaxKind.PartialKeyword);
        var isStatic = node.Modifiers.Any(SyntaxKind.StaticKeyword);
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

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
                if (baseClass == null && !typeName.StartsWith("I") || !char.IsUpper(typeName.ElementAtOrDefault(1)))
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

        var typeParameters = ConvertTypeParameters(node.TypeParameterList);
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
            csharpAttrs);
    }

    private ClassDefinitionNode ConvertRecord(RecordDeclarationSyntax node)
    {
        var id = _context.GenerateId("r");
        var name = node.Identifier.Text;

        var typeParameters = ConvertTypeParameters(node.TypeParameterList);
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
                var typeName = TypeMapper.CSharpToOpal(param.Type?.ToString() ?? "any");

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
            }
        }

        return new ClassDefinitionNode(
            GetTextSpan(node),
            id,
            name,
            isAbstract: false,
            isSealed: true,
            baseClass: null,
            implementedInterfaces: new List<string>(),
            typeParameters,
            fields,
            properties,
            constructors,
            methods,
            new AttributeCollection());
    }

    private ClassDefinitionNode ConvertStruct(StructDeclarationSyntax node)
    {
        var id = _context.GenerateId("s");
        var name = node.Identifier.Text;

        var typeParameters = ConvertTypeParameters(node.TypeParameterList);
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();

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
            isSealed: true,
            baseClass: null,
            interfaces,
            typeParameters,
            fields,
            properties,
            constructors,
            methods,
            new AttributeCollection());
    }

    private MethodSignatureNode ConvertMethodSignature(MethodDeclarationSyntax node)
    {
        var id = _context.GenerateId("m");
        var name = node.Identifier.Text;
        var typeParameters = ConvertTypeParameters(node.TypeParameterList);
        var parameters = ConvertParameters(node.ParameterList);
        var returnType = TypeMapper.CSharpToOpal(node.ReturnType.ToString());
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

        var id = _context.GenerateId("m");
        var name = node.Identifier.Text;
        var visibility = GetVisibility(node.Modifiers);
        var modifiers = GetMethodModifiers(node.Modifiers);
        var typeParameters = ConvertTypeParameters(node.TypeParameterList);
        var parameters = ConvertParameters(node.ParameterList);
        var returnType = TypeMapper.CSharpToOpal(node.ReturnType.ToString());
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
            effects: null,
            preconditions: Array.Empty<RequiresNode>(),
            postconditions: Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection(),
            csharpAttrs);
    }

    private ConstructorNode ConvertConstructor(ConstructorDeclarationSyntax node)
    {
        _context.RecordFeatureUsage("constructor");

        var id = _context.GenerateId("ctor");
        var visibility = GetVisibility(node.Modifiers);
        var parameters = ConvertParameters(node.ParameterList);
        var body = node.Body != null ? ConvertBlock(node.Body) : new List<StatementNode>();
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
        var typeName = TypeMapper.CSharpToOpal(node.Declaration.Type.ToString());
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        foreach (var variable in node.Declaration.Variables)
        {
            var defaultValue = variable.Initializer != null
                ? ConvertExpression(variable.Initializer.Value)
                : null;

            fields.Add(new ClassFieldNode(
                GetTextSpan(variable),
                variable.Identifier.Text,
                typeName,
                visibility,
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
        var delegateType = TypeMapper.CSharpToOpal(node.Declaration.Type.ToString());
        var csharpAttrs = ConvertAttributes(node.AttributeLists);

        foreach (var variable in node.Declaration.Variables)
        {
            var id = _context.GenerateId("evt");

            events.Add(new EventDefinitionNode(
                GetTextSpan(variable),
                id,
                variable.Identifier.Text,
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
        var typeName = TypeMapper.CSharpToOpal(node.Type.ToString());
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
        return new PropertyNode(
            GetTextSpan(node),
            propId,
            name,
            typeName,
            visibility,
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
            return new List<StatementNode>
            {
                new ReturnStatementNode(
                    GetTextSpan(expressionBody),
                    ConvertExpression(expressionBody.Expression))
            };
        }
        return Array.Empty<StatementNode>();
    }

    private IReadOnlyList<StatementNode> ConvertBlock(BlockSyntax block)
    {
        var statements = new List<StatementNode>();

        foreach (var statement in block.Statements)
        {
            var converted = ConvertStatement(statement);
            if (converted != null)
            {
                statements.Add(converted);
            }
        }

        return statements;
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
            _ => HandleUnsupportedStatement(statement)
        };
    }

    private StatementNode? HandleUnsupportedStatement(StatementSyntax statement)
    {
        return HandleUnsupportedStatement(statement, $"statement type: {statement.Kind()}");
    }

    private StatementNode? HandleUnsupportedStatement(StatementSyntax statement, string description)
    {
        var lineSpan = statement.GetLocation().GetLineSpan();
        _context.AddWarning(
            $"Unsupported {description}",
            line: lineSpan.StartLinePosition.Line + 1,
            column: lineSpan.StartLinePosition.Character + 1);
        _context.IncrementSkipped();
        return null;
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

    private StatementNode ConvertExpressionStatement(ExpressionStatementSyntax node)
    {
        var expr = node.Expression;
        _context.IncrementConverted();

        // Handle assignment expressions
        if (expr is AssignmentExpressionSyntax assignment)
        {
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

        // Default: wrap as call statement
        return new CallStatementNode(
            GetTextSpan(node),
            expr.ToString(),
            fallible: false,
            Array.Empty<ExpressionNode>(),
            new AttributeCollection());
    }

    private BindStatementNode ConvertLocalDeclaration(LocalDeclarationStatementSyntax node)
    {
        _context.IncrementConverted();

        var variable = node.Declaration.Variables.First();
        var name = variable.Identifier.Text;
        var typeName = node.Declaration.Type.IsVar
            ? null
            : TypeMapper.CSharpToOpal(node.Declaration.Type.ToString());
        var isMutable = !node.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
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

    private IfStatementNode ConvertIfStatement(IfStatementSyntax node)
    {
        _context.RecordFeatureUsage("if");
        _context.IncrementConverted();

        var id = _context.GenerateId("if");
        var condition = ConvertExpression(node.Condition);
        var thenBody = node.Statement is BlockSyntax block
            ? ConvertBlock(block)
            : new List<StatementNode> { ConvertStatement(node.Statement)! };

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
        var varType = TypeMapper.CSharpToOpal(node.Type.ToString());
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
                : TypeMapper.CSharpToOpal(node.Declaration.Type.ToString());

            if (node.Declaration.Variables.Count > 0)
            {
                var variable = node.Declaration.Variables[0];
                variableName = variable.Identifier.Text;
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

    private ExpressionNode ConvertExpression(ExpressionSyntax expression)
    {
        _context.Stats.ExpressionsConverted++;

        return expression switch
        {
            LiteralExpressionSyntax literal => ConvertLiteral(literal),
            IdentifierNameSyntax identifier => new ReferenceNode(GetTextSpan(identifier), identifier.Identifier.Text),
            BinaryExpressionSyntax binary => ConvertBinaryExpression(binary),
            PrefixUnaryExpressionSyntax prefix => ConvertPrefixUnaryExpression(prefix),
            ParenthesizedExpressionSyntax paren => ConvertExpression(paren.Expression),
            InvocationExpressionSyntax invocation => ConvertInvocationExpression(invocation),
            MemberAccessExpressionSyntax memberAccess => ConvertMemberAccessExpression(memberAccess),
            ObjectCreationExpressionSyntax objCreation => ConvertObjectCreation(objCreation),
            ThisExpressionSyntax => new ThisExpressionNode(GetTextSpan(expression)),
            BaseExpressionSyntax => new BaseExpressionNode(GetTextSpan(expression)),
            ConditionalExpressionSyntax conditional => ConvertConditionalExpression(conditional),
            ArrayCreationExpressionSyntax arrayCreation => ConvertArrayCreation(arrayCreation),
            ElementAccessExpressionSyntax elementAccess => ConvertElementAccess(elementAccess),
            LambdaExpressionSyntax lambda => ConvertLambdaExpression(lambda),
            AwaitExpressionSyntax awaitExpr => ConvertAwaitExpression(awaitExpr),
            InterpolatedStringExpressionSyntax interpolated => ConvertInterpolatedString(interpolated),
            ConditionalAccessExpressionSyntax condAccess => ConvertConditionalAccess(condAccess),
            CastExpressionSyntax cast => ConvertExpression(cast.Expression), // Just use inner expression
            _ => new ReferenceNode(GetTextSpan(expression), expression.ToString())
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
            _ => new ReferenceNode(GetTextSpan(literal), literal.ToString())
        };
    }

    private BinaryOperationNode ConvertBinaryExpression(BinaryExpressionSyntax binary)
    {
        var left = ConvertExpression(binary.Left);
        var right = ConvertExpression(binary.Right);
        var op = binary.OperatorToken.Text;
        var binaryOp = BinaryOperatorExtensions.FromString(op) ?? BinaryOperator.Add;

        return new BinaryOperationNode(GetTextSpan(binary), binaryOp, left, right);
    }

    private UnaryOperationNode ConvertPrefixUnaryExpression(PrefixUnaryExpressionSyntax prefix)
    {
        var operand = ConvertExpression(prefix.Operand);
        var op = prefix.OperatorToken.Text;
        var unaryOp = UnaryOperatorExtensions.FromString(op) ?? UnaryOperator.Negate;

        return new UnaryOperationNode(GetTextSpan(prefix), unaryOp, operand);
    }

    private ExpressionNode ConvertInvocationExpression(InvocationExpressionSyntax invocation)
    {
        var target = invocation.Expression.ToString();
        var args = invocation.ArgumentList.Arguments
            .Select(a => ConvertExpression(a.Expression))
            .ToList();

        return new CallExpressionNode(GetTextSpan(invocation), target, args);
    }

    private ExpressionNode ConvertMemberAccessExpression(MemberAccessExpressionSyntax memberAccess)
    {
        var target = ConvertExpression(memberAccess.Expression);
        var memberName = memberAccess.Name.Identifier.Text;

        return new FieldAccessNode(GetTextSpan(memberAccess), target, memberName);
    }

    private NewExpressionNode ConvertObjectCreation(ObjectCreationExpressionSyntax objCreation)
    {
        var typeName = objCreation.Type.ToString();
        var typeArgs = new List<string>();

        if (objCreation.Type is GenericNameSyntax genericName)
        {
            typeName = genericName.Identifier.Text;
            typeArgs = genericName.TypeArgumentList.Arguments
                .Select(a => TypeMapper.CSharpToOpal(a.ToString()))
                .ToList();
        }

        var args = objCreation.ArgumentList?.Arguments
            .Select(a => ConvertExpression(a.Expression))
            .ToList() ?? new List<ExpressionNode>();

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

    private ExpressionNode ConvertConditionalExpression(ConditionalExpressionSyntax conditional)
    {
        // Ternary is converted to if-then-else expression using Match
        var condition = ConvertExpression(conditional.Condition);
        var whenTrue = ConvertExpression(conditional.WhenTrue);
        var whenFalse = ConvertExpression(conditional.WhenFalse);

        var id = _context.GenerateId("cond");

        // Create a match expression
        var trueBranch = new MatchCaseNode(
            GetTextSpan(conditional.WhenTrue),
            new LiteralPatternNode(TextSpan.Empty, new BoolLiteralNode(TextSpan.Empty, true)),
            guard: null,
            new List<StatementNode> { new ReturnStatementNode(TextSpan.Empty, whenTrue) });

        var falseBranch = new MatchCaseNode(
            GetTextSpan(conditional.WhenFalse),
            new WildcardPatternNode(TextSpan.Empty),
            guard: null,
            new List<StatementNode> { new ReturnStatementNode(TextSpan.Empty, whenFalse) });

        return new MatchExpressionNode(
            GetTextSpan(conditional),
            id,
            condition,
            new List<MatchCaseNode> { trueBranch, falseBranch },
            new AttributeCollection());
    }

    private ArrayCreationNode ConvertArrayCreation(ArrayCreationExpressionSyntax arrayCreation)
    {
        var id = _context.GenerateId("arr");
        var name = _context.GenerateId("arr");
        var elementType = TypeMapper.CSharpToOpal(arrayCreation.Type.ElementType.ToString());

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

    private ArrayAccessNode ConvertElementAccess(ElementAccessExpressionSyntax elementAccess)
    {
        var array = ConvertExpression(elementAccess.Expression);
        var index = ConvertExpression(elementAccess.ArgumentList.Arguments[0].Expression);

        return new ArrayAccessNode(GetTextSpan(elementAccess), array, index);
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
                    null));
                break;

            case ParenthesizedLambdaExpressionSyntax paren:
                foreach (var param in paren.ParameterList.Parameters)
                {
                    parameters.Add(new LambdaParameterNode(
                        GetTextSpan(param),
                        param.Identifier.Text,
                        param.Type != null ? TypeMapper.CSharpToOpal(param.Type.ToString()) : null));
                }
                break;
        }

        ExpressionNode? exprBody = null;
        List<StatementNode>? stmtBody = null;

        if (lambda.ExpressionBody != null)
        {
            exprBody = ConvertExpression(lambda.ExpressionBody);
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
                    parts.Add(new InterpolatedStringExpressionNode(
                        GetTextSpan(interp),
                        ConvertExpression(interp.Expression)));
                    break;
            }
        }

        return new InterpolatedStringNode(GetTextSpan(interpolated), parts);
    }

    private NullConditionalNode ConvertConditionalAccess(ConditionalAccessExpressionSyntax condAccess)
    {
        _context.RecordFeatureUsage("null-conditional");

        var target = ConvertExpression(condAccess.Expression);

        // WhenNotNull is a MemberBindingExpression which starts with '.' (e.g., ".Status")
        // We need to strip the leading dot since the emitter adds its own "?."
        var memberName = condAccess.WhenNotNull.ToString();
        if (memberName.StartsWith("."))
        {
            memberName = memberName.Substring(1);
        }

        return new NullConditionalNode(GetTextSpan(condAccess), target, memberName);
    }

    private IReadOnlyList<TypeParameterNode> ConvertTypeParameters(TypeParameterListSyntax? typeParamList)
    {
        if (typeParamList == null)
            return Array.Empty<TypeParameterNode>();

        _context.RecordFeatureUsage("generics");

        return typeParamList.Parameters
            .Select(p => new TypeParameterNode(
                GetTextSpan(p),
                p.Identifier.Text,
                Array.Empty<TypeConstraintNode>()))
            .ToList();
    }

    private IReadOnlyList<ParameterNode> ConvertParameters(ParameterListSyntax paramList)
    {
        return paramList.Parameters
            .Select(p => new ParameterNode(
                GetTextSpan(p),
                p.Identifier.Text,
                TypeMapper.CSharpToOpal(p.Type?.ToString() ?? "any"),
                new AttributeCollection()))
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
    /// Converts C# attributes to OPAL attributes.
    /// </summary>
    private IReadOnlyList<OpalAttributeNode> ConvertAttributes(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var result = new List<OpalAttributeNode>();

        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                var args = new List<OpalAttributeArgument>();

                if (attr.ArgumentList != null)
                {
                    foreach (var arg in attr.ArgumentList.Arguments)
                    {
                        var argName = arg.NameEquals?.Name.ToString();
                        var value = ConvertAttributeValue(arg.Expression);

                        if (argName != null)
                        {
                            args.Add(new OpalAttributeArgument(argName, value));
                        }
                        else
                        {
                            args.Add(new OpalAttributeArgument(value));
                        }
                    }
                }

                result.Add(new OpalAttributeNode(GetTextSpan(attr), name, args));
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
            MemberAccessExpressionSyntax memberAccess => memberAccess.ToString(),
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => expression.ToString()
        };
    }
}
