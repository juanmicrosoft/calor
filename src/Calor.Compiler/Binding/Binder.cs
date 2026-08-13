using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Binding;

/// <summary>
/// Performs semantic analysis and builds the bound tree.
/// </summary>
public sealed class Binder
{
    internal delegate BoundExpression ExpressionBinder(Binder binder, ExpressionNode expression);

    private static readonly IReadOnlyDictionary<Type, ExpressionBinder> ExpressionBinders =
        new Dictionary<Type, ExpressionBinder>
        {
            [typeof(AddressOfNode)] = static (binder, expression) => binder.BindAddressOf((AddressOfNode)expression),
            [typeof(AnonymousObjectCreationNode)] = static (binder, expression) => binder.BindAnonymousObjectCreation((AnonymousObjectCreationNode)expression),
            [typeof(ArrayAccessNode)] = static (binder, expression) => binder.BindArrayAccess((ArrayAccessNode)expression),
            [typeof(ArrayCreationNode)] = static (binder, expression) => binder.BindArrayCreation((ArrayCreationNode)expression),
            [typeof(ArrayLengthNode)] = static (binder, expression) => binder.BindArrayLength((ArrayLengthNode)expression),
            [typeof(AwaitExpressionNode)] = static (binder, expression) => binder.BindAwaitExpression((AwaitExpressionNode)expression),
            [typeof(BaseExpressionNode)] = static (binder, expression) => binder.BindBaseExpression((BaseExpressionNode)expression),
            [typeof(BinaryOperationNode)] = static (binder, expression) => binder.BindBinaryOperation((BinaryOperationNode)expression),
            [typeof(BoolLiteralNode)] = static (_, expression) => new BoundBoolLiteral(expression.Span, ((BoolLiteralNode)expression).Value),
            [typeof(CallExpressionNode)] = static (binder, expression) => binder.BindCallExpression((CallExpressionNode)expression),
            [typeof(CharOperationNode)] = static (binder, expression) => binder.BindCharOperation((CharOperationNode)expression),
            [typeof(CollectionContainsNode)] = static (binder, expression) => binder.BindCollectionContains((CollectionContainsNode)expression),
            [typeof(CollectionCountNode)] = static (binder, expression) => binder.BindCollectionCount((CollectionCountNode)expression),
            [typeof(ConditionalExpressionNode)] = static (binder, expression) => binder.BindConditionalExpression((ConditionalExpressionNode)expression),
            [typeof(DecimalLiteralNode)] = static (_, expression) => new BoundDecimalLiteral(expression.Span, ((DecimalLiteralNode)expression).Value),
            [typeof(DictionaryCreationNode)] = static (binder, expression) => binder.BindDictionaryCreation((DictionaryCreationNode)expression),
            [typeof(ErrExpressionNode)] = static (binder, expression) => binder.BindErrExpression((ErrExpressionNode)expression),
            [typeof(ExistsExpressionNode)] = static (binder, expression) => binder.BindExistsExpression((ExistsExpressionNode)expression),
            [typeof(ExpressionCallNode)] = static (binder, expression) => binder.BindExpressionCall((ExpressionCallNode)expression),
            [typeof(FallbackExpressionNode)] = static (binder, expression) => binder.BindFallbackInterop((FallbackExpressionNode)expression),
            [typeof(FieldAccessNode)] = static (binder, expression) => binder.BindFieldAccess((FieldAccessNode)expression),
            [typeof(FloatLiteralNode)] = static (binder, expression) => binder.BindFloatLiteral((FloatLiteralNode)expression),
            [typeof(ForallExpressionNode)] = static (binder, expression) => binder.BindForallExpression((ForallExpressionNode)expression),
            [typeof(GenericTypeNode)] = static (binder, expression) => binder.BindGenericType((GenericTypeNode)expression),
            [typeof(ImplicationExpressionNode)] = static (binder, expression) => binder.BindImplicationExpression((ImplicationExpressionNode)expression),
            [typeof(IndexFromEndNode)] = static (binder, expression) => binder.BindIndexFromEnd((IndexFromEndNode)expression),
            [typeof(IntLiteralNode)] = static (binder, expression) => binder.BindIntLiteral((IntLiteralNode)expression),
            [typeof(InterpolatedStringNode)] = static (binder, expression) => binder.BindInterpolatedString((InterpolatedStringNode)expression),
            [typeof(IsPatternNode)] = static (binder, expression) => binder.BindIsPattern((IsPatternNode)expression),
            [typeof(LambdaExpressionNode)] = static (binder, expression) => binder.BindLambdaExpression((LambdaExpressionNode)expression),
            [typeof(ListCreationNode)] = static (binder, expression) => binder.BindListCreation((ListCreationNode)expression),
            [typeof(MatchExpressionNode)] = static (binder, expression) => binder.BindMatchExpression((MatchExpressionNode)expression),
            [typeof(MultiDimArrayAccessNode)] = static (binder, expression) => binder.BindMultiDimArrayAccess((MultiDimArrayAccessNode)expression),
            [typeof(MultiDimArrayCreationNode)] = static (binder, expression) => binder.BindMultiDimArrayCreation((MultiDimArrayCreationNode)expression),
            [typeof(NameOfExpressionNode)] = static (_, expression) => new BoundStringLiteral(expression.Span, ((NameOfExpressionNode)expression).Name),
            [typeof(NewExpressionNode)] = static (binder, expression) => binder.BindNewExpression((NewExpressionNode)expression),
            [typeof(NoneExpressionNode)] = static (binder, expression) => binder.BindNoneExpression((NoneExpressionNode)expression),
            [typeof(NullCoalesceNode)] = static (binder, expression) => binder.BindNullCoalesce((NullCoalesceNode)expression),
            [typeof(NullConditionalNode)] = static (binder, expression) => binder.BindNullConditional((NullConditionalNode)expression),
            [typeof(OkExpressionNode)] = static (binder, expression) => binder.BindOkExpression((OkExpressionNode)expression),
            [typeof(PointerDereferenceNode)] = static (binder, expression) => binder.BindPointerDereference((PointerDereferenceNode)expression),
            [typeof(RangeExpressionNode)] = static (binder, expression) => binder.BindRangeExpression((RangeExpressionNode)expression),
            [typeof(RawCSharpExpressionNode)] = static (binder, expression) => binder.BindRawCSharpInterop((RawCSharpExpressionNode)expression),
            [typeof(RecordCreationNode)] = static (binder, expression) => binder.BindRecordCreation((RecordCreationNode)expression),
            [typeof(ReferenceNode)] = static (binder, expression) => binder.BindReferenceExpression((ReferenceNode)expression),
            [typeof(SelfRefNode)] = static (binder, expression) => binder.BindSelfReference((SelfRefNode)expression),
            [typeof(SetCreationNode)] = static (binder, expression) => binder.BindSetCreation((SetCreationNode)expression),
            [typeof(SizeOfNode)] = static (binder, expression) => binder.BindSizeOf((SizeOfNode)expression),
            [typeof(SomeExpressionNode)] = static (binder, expression) => binder.BindSomeExpression((SomeExpressionNode)expression),
            [typeof(StackAllocNode)] = static (binder, expression) => binder.BindStackAlloc((StackAllocNode)expression),
            [typeof(StringBuilderOperationNode)] = static (binder, expression) => binder.BindStringBuilderOperation((StringBuilderOperationNode)expression),
            [typeof(StringLiteralNode)] = static (binder, expression) => binder.BindStringLiteral((StringLiteralNode)expression),
            [typeof(StringOperationNode)] = static (binder, expression) => binder.BindStringOperation((StringOperationNode)expression),
            [typeof(ThisExpressionNode)] = static (binder, expression) => binder.BindThisExpression((ThisExpressionNode)expression),
            [typeof(ThrowExpressionNode)] = static (binder, expression) => binder.BindThrowExpression((ThrowExpressionNode)expression),
            [typeof(TupleLiteralNode)] = static (binder, expression) => binder.BindTupleLiteral((TupleLiteralNode)expression),
            [typeof(TypeOfExpressionNode)] = static (binder, expression) => binder.BindTypeOf((TypeOfExpressionNode)expression),
            [typeof(TypeOperationNode)] = static (binder, expression) => binder.BindTypeOperation((TypeOperationNode)expression),
            [typeof(UnaryOperationNode)] = static (binder, expression) => binder.BindUnaryOperation((UnaryOperationNode)expression),
            [typeof(WithExpressionNode)] = static (binder, expression) => binder.BindWithExpression((WithExpressionNode)expression),
        };

    internal static IReadOnlyCollection<Type> RegisteredExpressionNodeTypes =>
        ExpressionBinders.Keys.ToArray();

    internal static IReadOnlyDictionary<Type, ExpressionBinder> ExpressionDispatch =>
        ExpressionBinders;

    public int ExpressionsBound { get; private set; }

    private readonly DiagnosticBag _diagnostics;
    private readonly string _sourceIdentity;
    private Scope _scope;
    private readonly Dictionary<AstNode, FunctionSymbol> _functionSymbols = new();
    private readonly Dictionary<ClassDefinitionNode, Scope> _classScopes = new();
    private readonly Dictionary<ClassDefinitionNode, string> _qualifiedClassNames = new();
    private readonly Dictionary<ClassDefinitionNode, SymbolId> _classSymbolIds = new();
    private readonly Dictionary<ClassDefinitionNode, TypeSymbol> _classSymbols = new();
    private readonly Dictionary<string, ClassDefinitionNode> _classesByQualifiedName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ClassDefinitionNode>> _classesBySimpleName = new(StringComparer.Ordinal);
    private readonly Dictionary<SymbolId, Symbol> _symbolsById = new();
    private readonly Dictionary<string, int> _declarationIdOccurrences = new(StringComparer.Ordinal);
    private SymbolId _moduleSymbolId;
    private SymbolId _declarationContext;
    private string? _currentClassName;
    private ClassDefinitionNode? _currentClass;
    private Scope? _currentClassScope;
    private SymbolId _currentClassIdentity;
    private bool _isStaticContext;

    public Binder(DiagnosticBag diagnostics, string? sourceIdentity = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _sourceIdentity = SymbolSourceIdentity.Canonicalize(sourceIdentity);
        _scope = new Scope();
    }

    private IDisposable PushScope(Scope newScope)
    {
        var previous = _scope;
        _scope = newScope;
        return new ScopeRestorer(this, previous);
    }

    private sealed class ScopeRestorer : IDisposable
    {
        private readonly Binder _binder;
        private readonly Scope _previous;
        public ScopeRestorer(Binder binder, Scope previous) { _binder = binder; _previous = previous; }
        public void Dispose() => _binder._scope = _previous;
    }

    private IDisposable PushStaticContext(bool isStatic)
    {
        var previous = _isStaticContext;
        _isStaticContext = isStatic;
        return new StaticContextRestorer(this, previous);
    }

    private sealed class StaticContextRestorer : IDisposable
    {
        private readonly Binder _binder;
        private readonly bool _previous;
        public StaticContextRestorer(Binder binder, bool previous) { _binder = binder; _previous = previous; }
        public void Dispose() => _binder._isStaticContext = _previous;
    }

    private IDisposable PushDeclarationContext(SymbolId context)
    {
        var previousContext = _declarationContext;
        _declarationContext = context;
        return new DeclarationContextRestorer(this, previousContext);
    }

    private sealed class DeclarationContextRestorer : IDisposable
    {
        private readonly Binder _binder;
        private readonly SymbolId _previousContext;

        public DeclarationContextRestorer(
            Binder binder,
            SymbolId previousContext)
        {
            _binder = binder;
            _previousContext = previousContext;
        }

        public void Dispose()
        {
            _binder._declarationContext = _previousContext;
        }
    }

    public BoundModule Bind(ModuleNode module)
    {
        _scope = new Scope();
        _functionSymbols.Clear();
        _classScopes.Clear();
        _qualifiedClassNames.Clear();
        _classSymbolIds.Clear();
        _classSymbols.Clear();
        _classesByQualifiedName.Clear();
        _classesBySimpleName.Clear();
        _symbolsById.Clear();
        _declarationIdOccurrences.Clear();
        _moduleSymbolId = SymbolId.Create("source", _sourceIdentity, "module", module.Id);
        _declarationContext = _moduleSymbolId;

        var functions = new List<BoundFunction>();

        RegisterTopLevelFunctions(module);
        RegisterAdditionalTypes(module);
        foreach (var cls in module.Classes)
            RegisterClassTree(cls, _moduleSymbolId, null);

        foreach (var func in module.Functions)
            functions.Add(BindFunction(func));

        foreach (var cls in module.Classes)
            BindClassMembers(cls, functions);

        return new BoundModule(
            module.Span,
            module.Name,
            functions,
            new Dictionary<SymbolId, Symbol>(_symbolsById));
    }

    private void RegisterTopLevelFunctions(ModuleNode module)
    {
        foreach (var function in module.Functions)
        {
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(_moduleSymbolId, "function", function.Id, function.Name),
                function.Name,
                function.Output?.TypeName ?? "VOID",
                GetCallableTypeParameters(
                    function.Name,
                    function.TypeParameters.Select(parameter => parameter.Name)),
                function.Parameters,
                function.IdentifierSpan,
                function.Visibility,
                containingTypeName: null,
                definitionSpan: function.Span);
            _functionSymbols.Add(function, symbol);

            var lookupName = GetCallableLookupName(function.Name);
            if (!_scope.TryDeclareOverload(lookupName, symbol, out var duplicate))
                ReportDuplicateSignature(function.Span, lookupName, symbol, duplicate);
        }
    }

    private void RegisterAdditionalTypes(ModuleNode module)
    {
        foreach (var @interface in module.Interfaces)
        {
            RegisterTypeSymbol(
                _moduleSymbolId,
                "interface",
                @interface.Id,
                @interface.Name,
                @interface.Name,
                Visibility.Public,
                @interface.IdentifierSpan,
                @interface.Span);
        }

        foreach (var @enum in module.Enums)
        {
            RegisterTypeSymbol(
                _moduleSymbolId,
                "enum",
                @enum.Id,
                @enum.Name,
                @enum.Name,
                @enum.Visibility,
                @enum.IdentifierSpan,
                @enum.Span);
        }

        foreach (var @delegate in module.Delegates)
        {
            RegisterTypeSymbol(
                _moduleSymbolId,
                "delegate",
                @delegate.Id,
                @delegate.Name,
                @delegate.Name,
                Visibility.Public,
                @delegate.IdentifierSpan,
                @delegate.Span);
        }
    }

    private void RegisterTypeSymbol(
        SymbolId parentIdentity,
        string kind,
        string stableAstId,
        string name,
        string qualifiedName,
        Visibility visibility,
        Parsing.TextSpan declarationSpan,
        Parsing.TextSpan definitionSpan)
    {
        TrackSymbol(new TypeSymbol(
            CreateDeclarationId(parentIdentity, kind, stableAstId, name),
            name,
            qualifiedName,
            visibility,
            declarationSpan,
            definitionSpan));
    }

    private void RegisterClassTree(
        ClassDefinitionNode cls,
        SymbolId parentIdentity,
        string? containingTypeName)
    {
        var classIdentity = CreateDeclarationId(parentIdentity, "class", cls.Id, cls.Name);
        var qualifiedClassName = containingTypeName == null
            ? cls.Name
            : $"{containingTypeName}.{cls.Name}";
        var classScope = _scope.CreateChild();
        var classSymbol = new TypeSymbol(
            classIdentity,
            cls.Name,
            qualifiedClassName,
            cls.Visibility,
            cls.IdentifierSpan,
            cls.Span);
        TrackSymbol(classSymbol);

        _classScopes.Add(cls, classScope);
        _qualifiedClassNames.Add(cls, qualifiedClassName);
        _classSymbolIds.Add(cls, classIdentity);
        _classSymbols.Add(cls, classSymbol);
        _classesByQualifiedName[qualifiedClassName] = cls;
        if (!_classesBySimpleName.TryGetValue(cls.Name, out var simpleMatches))
        {
            simpleMatches = new List<ClassDefinitionNode>();
            _classesBySimpleName.Add(cls.Name, simpleMatches);
        }
        simpleMatches.Add(cls);

        var fields = EnumerateFieldRegistrations(cls).ToArray();
        for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            var (field, conditionalAlternative) = fields[fieldIndex];
            var isMutable = !field.Modifiers.HasFlag(MethodModifiers.Readonly);
            var symbol = CreateVariable(
                CreateDeclarationId(classIdentity, "field", stableAstId: null, field.Name),
                field.Name,
                field.TypeName,
                isMutable,
                isParameter: false,
                ParameterModifier.None,
                field.IdentifierSpan,
                visibility: field.Visibility,
                declaringTypeName: qualifiedClassName,
                isField: true,
                isStatic: field.IsStatic,
                conditionalAlternative: conditionalAlternative);
            if (!classScope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    field.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Field '{field.Name}' is already defined in '{qualifiedClassName}'");
            }
        }

        var properties = EnumeratePropertyRegistrations(cls).ToArray();
        for (var propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
        {
            var (property, conditionalAlternative) = properties[propertyIndex];
            var symbol = CreateVariable(
                CreateDeclarationId(classIdentity, "property", property.Id, property.Name),
                property.Name,
                property.TypeName,
                property.Setter != null || property.Initer != null,
                isParameter: false,
                ParameterModifier.None,
                property.IdentifierSpan,
                visibility: property.Visibility,
                declaringTypeName: qualifiedClassName,
                isProperty: true,
                isStatic: property.IsStatic,
                conditionalAlternative: conditionalAlternative);
            if (!classScope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    property.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Property '{property.Name}' is already defined in '{qualifiedClassName}'");
            }
        }

        var methods = EnumerateMethodRegistrations(cls).ToArray();
        for (var methodIndex = 0; methodIndex < methods.Length; methodIndex++)
        {
            var (method, conditionalAlternative) = methods[methodIndex];
            var lookupName = GetCallableLookupName(method.Name);
            var qualifiedLookupName = $"{qualifiedClassName}.{lookupName}";
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(classIdentity, "method", method.Id, method.Name),
                $"{qualifiedClassName}.{method.Name}",
                method.Output?.TypeName ?? "VOID",
                GetCallableTypeParameters(
                    method.Name,
                    method.TypeParameters.Select(parameter => parameter.Name)),
                method.Parameters,
                method.IdentifierSpan,
                method.Visibility,
                qualifiedClassName,
                method.Span,
                conditionalAlternative);
            _functionSymbols.Add(method, symbol);

            if (!classScope.TryDeclareOverload(lookupName, symbol, out var duplicate))
            {
                ReportDuplicateSignature(method.Span, qualifiedLookupName, symbol, duplicate);
                continue;
            }

            if (!_scope.TryDeclareOverload(qualifiedLookupName, symbol, out duplicate))
                ReportDuplicateSignature(method.Span, qualifiedLookupName, symbol, duplicate);
        }

        var constructors = EnumerateConstructorRegistrations(cls).ToArray();
        for (var constructorIndex = 0; constructorIndex < constructors.Length; constructorIndex++)
        {
            var (constructor, conditionalAlternative) = constructors[constructorIndex];
            var constructorName = constructor.IsStatic ? ".cctor" : ".ctor";
            var qualifiedLookupName = $"{qualifiedClassName}.{constructorName}";
            var symbol = CreateFunctionSymbol(
                CreateDeclarationId(classIdentity, "constructor", constructor.Id, constructorName),
                qualifiedLookupName,
                "VOID",
                Array.Empty<string>(),
                constructor.Parameters,
                cls.IdentifierSpan,
                constructor.Visibility,
                qualifiedClassName,
                constructor.Span,
                conditionalAlternative);
            _functionSymbols.Add(constructor, symbol);

            if (!classScope.TryDeclareOverload(qualifiedLookupName, symbol, out var duplicate))
            {
                ReportDuplicateSignature(constructor.Span, qualifiedLookupName, symbol, duplicate);
                continue;
            }

            if (!constructor.IsStatic
                && !classScope.TryDeclareOverload(cls.Name, symbol, out duplicate))
            {
                ReportDuplicateSignature(constructor.Span, cls.Name, symbol, duplicate);
            }
            if (!_scope.TryDeclareOverload(qualifiedLookupName, symbol, out duplicate))
                ReportDuplicateSignature(constructor.Span, qualifiedLookupName, symbol, duplicate);
        }

        foreach (var nested in cls.NestedClasses)
        {
            RegisterClassTree(
                nested,
                classIdentity,
                qualifiedClassName);
        }
        foreach (var nested in cls.NestedInterfaces)
        {
            RegisterTypeSymbol(
                classIdentity,
                "interface",
                nested.Id,
                nested.Name,
                $"{qualifiedClassName}.{nested.Name}",
                Visibility.Public,
                nested.IdentifierSpan,
                nested.Span);
        }
        foreach (var nested in cls.NestedEnums)
        {
            RegisterTypeSymbol(
                classIdentity,
                "enum",
                nested.Id,
                nested.Name,
                $"{qualifiedClassName}.{nested.Name}",
                nested.Visibility,
                nested.IdentifierSpan,
                nested.Span);
        }
        foreach (var nested in cls.NestedDelegates)
        {
            RegisterTypeSymbol(
                classIdentity,
                "delegate",
                nested.Id,
                nested.Name,
                $"{qualifiedClassName}.{nested.Name}",
                Visibility.Public,
                nested.IdentifierSpan,
                nested.Span);
        }
    }

    private static IEnumerable<MemberPreprocessorBlockNode> EnumeratePreprocessorBranches(
        ClassDefinitionNode cls)
    {
        foreach (var block in cls.PreprocessorBlocks)
        {
            for (var branch = block; branch != null; branch = branch.ElseBranch)
                yield return branch;
        }
    }

    private static IEnumerable<ClassFieldNode> EnumerateFields(ClassDefinitionNode cls) =>
        cls.Fields.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Fields));

    private static IEnumerable<(ClassFieldNode Field, ConditionalAlternative? Alternative)>
        EnumerateFieldRegistrations(ClassDefinitionNode cls)
    {
        foreach (var field in cls.Fields)
            yield return (field, null);
        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var field in branch.Fields)
                yield return (field, alternative);
        }
    }

    private static IEnumerable<PropertyNode> EnumerateProperties(ClassDefinitionNode cls) =>
        cls.Properties.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Properties));

    private static IEnumerable<(PropertyNode Property, ConditionalAlternative? Alternative)>
        EnumeratePropertyRegistrations(ClassDefinitionNode cls)
    {
        foreach (var property in cls.Properties)
            yield return (property, null);
        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var property in branch.Properties)
                yield return (property, alternative);
        }
    }

    private static IEnumerable<IndexerNode> EnumerateIndexers(ClassDefinitionNode cls) =>
        cls.Indexers.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Indexers));

    private static IEnumerable<ConstructorNode> EnumerateConstructors(ClassDefinitionNode cls) =>
        cls.Constructors.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Constructors));

    private static IEnumerable<(ConstructorNode Constructor, ConditionalAlternative? Alternative)>
        EnumerateConstructorRegistrations(ClassDefinitionNode cls)
    {
        foreach (var constructor in cls.Constructors)
            yield return (constructor, null);
        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var constructor in branch.Constructors)
                yield return (constructor, alternative);
        }
    }

    private static IEnumerable<MethodNode> EnumerateMethods(ClassDefinitionNode cls) =>
        cls.Methods.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Methods));

    private static IEnumerable<(MethodNode Method, ConditionalAlternative? Alternative)>
        EnumerateMethodRegistrations(ClassDefinitionNode cls)
    {
        foreach (var method in cls.Methods)
            yield return (method, null);

        foreach (var (branch, alternative) in EnumerateConditionalMemberBranches(cls))
        {
            foreach (var method in branch.Methods)
                yield return (method, alternative);
        }
    }

    private static IEnumerable<(MemberPreprocessorBlockNode Branch, ConditionalAlternative Alternative)>
        EnumerateConditionalMemberBranches(ClassDefinitionNode cls)
    {
        for (var blockIndex = 0; blockIndex < cls.PreprocessorBlocks.Count; blockIndex++)
        {
            var groupId = $"{cls.Id}:member-pp:{blockIndex}";
            var branchIndex = 0;
            for (var branch = cls.PreprocessorBlocks[blockIndex];
                 branch != null;
                 branch = branch.ElseBranch)
            {
                yield return (branch, new ConditionalAlternative(groupId, branchIndex++));
            }
        }
    }

    private static IEnumerable<EventDefinitionNode> EnumerateEvents(ClassDefinitionNode cls) =>
        cls.Events.Concat(EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.Events));

    private static IEnumerable<OperatorOverloadNode> EnumerateOperators(ClassDefinitionNode cls) =>
        cls.OperatorOverloads.Concat(
            EnumeratePreprocessorBranches(cls).SelectMany(branch => branch.OperatorOverloads));

    private FunctionSymbol CreateFunctionSymbol(
        SymbolId id,
        string name,
        string returnType,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        Parsing.TextSpan declarationSpan,
        Visibility visibility,
        string? containingTypeName,
        Parsing.TextSpan definitionSpan,
        ConditionalAlternative? conditionalAlternative = null)
    {
        var parameterSymbols = parameters
            .Select((parameter, index) => CreateVariable(
                CreateDeclarationId(id, "parameter", stableAstId: null, parameter.Name),
                parameter.Name,
                parameter.TypeName,
                isMutable: false,
                isParameter: true,
                parameter.Modifier,
                parameter.IdentifierSpan,
                parameter.DefaultValue))
            .ToArray();
        var symbol = new FunctionSymbol(
            id,
            name,
            returnType,
            parameterSymbols,
            typeParameters,
            declarationSpan,
            visibility,
            containingTypeName,
            definitionSpan,
            conditionalAlternative);
        TrackSymbol(symbol);
        return symbol;
    }

    private VariableSymbol CreateVariable(
        SymbolId id,
        string name,
        string typeName,
        bool isMutable,
        bool isParameter,
        ParameterModifier modifier,
        Parsing.TextSpan declarationSpan,
        ExpressionNode? defaultValue = null,
        Visibility visibility = Visibility.Public,
        string? declaringTypeName = null,
        bool isField = false,
        bool isProperty = false,
        bool isStatic = false,
        ConditionalAlternative? conditionalAlternative = null)
    {
        var symbol = new VariableSymbol(
            id,
            name,
            typeName,
            isMutable,
            isParameter,
            modifier,
            declarationSpan,
            defaultValue,
            visibility,
            declaringTypeName,
            isField,
            isProperty,
            isStatic,
            conditionalAlternative);
        TrackSymbol(symbol);
        return symbol;
    }

    private VariableSymbol CreateLocalVariable(
        string name,
        string typeName,
        bool isMutable,
        bool isParameter,
        ParameterModifier modifier,
        Parsing.TextSpan declarationSpan,
        string kind,
        ExpressionNode? defaultValue = null)
    {
        var context = _declarationContext.IsNone ? _moduleSymbolId : _declarationContext;
        return CreateVariable(
            CreateDeclarationId(context, kind, stableAstId: null, name),
            name,
            typeName,
            isMutable,
            isParameter,
            modifier,
            declarationSpan,
            defaultValue);
    }

    private SymbolId CreateDeclarationId(
        SymbolId parent,
        string kind,
        string? stableAstId,
        string fallbackName)
    {
        var baseId = !string.IsNullOrWhiteSpace(stableAstId)
            ? parent.Append(kind, $"ast:{stableAstId}")
            : parent.Append(kind, $"name:{fallbackName}");
        var key = baseId.Value;
        if (!_declarationIdOccurrences.TryGetValue(key, out var occurrence))
        {
            _declarationIdOccurrences.Add(key, 1);
            return baseId;
        }

        _declarationIdOccurrences[key] = occurrence + 1;
        return baseId.Append($"duplicate:{occurrence}");
    }

    private VariableSymbol CreateUnresolvedVariable(ReferenceNode reference)
    {
        return new VariableSymbol(
            SymbolId.None,
            reference.Name,
            "INT",
            isMutable: false,
            isParameter: false,
            ParameterModifier.None,
            reference.Span);
    }

    private void TrackSymbol(Symbol symbol)
    {
        if (!symbol.Id.IsNone)
            _symbolsById.Add(symbol.Id, symbol);
    }

    private void ReportDuplicateSignature(
        Parsing.TextSpan span,
        string lookupName,
        FunctionSymbol symbol,
        FunctionSymbol? duplicate)
    {
        var conflict = duplicate == null ? string.Empty : $" Conflicts with '{duplicate.Id}'.";
        _diagnostics.ReportError(
            span,
            DiagnosticCode.DuplicateFunctionSignature,
            $"Duplicate signature '{lookupName}{FormatSignatureSuffix(symbol)}'.{conflict}");
    }

    private static string FormatSignatureSuffix(FunctionSymbol symbol)
    {
        var generic = symbol.GenericArity == 0 ? string.Empty : $"<{symbol.GenericArity}>";
        var parameters = string.Join(
            ", ",
            symbol.Parameters.Select(parameter =>
                $"{FormatParameterModifier(parameter.Modifier)}{TypeIdentity.CanonicalizeSignature(parameter.TypeName, symbol.TypeParameters)}"));
        return $"{generic}({parameters})";
    }

    private static string FormatParameterModifier(ParameterModifier modifier)
    {
        var callModifier = modifier & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
        return callModifier switch
        {
            ParameterModifier.Ref => "ref ",
            ParameterModifier.Out => "out ",
            ParameterModifier.In => "in ",
            _ when modifier.HasFlag(ParameterModifier.Params) => "params ",
            _ when modifier.HasFlag(ParameterModifier.This) => "this ",
            _ => string.Empty,
        };
    }

    private static string GetCallableLookupName(string declaredName)
    {
        var open = declaredName.LastIndexOf('<');
        return open > 0 && declaredName.EndsWith('>')
            ? declaredName[..open]
            : declaredName;
    }

    private static IReadOnlyList<string> GetCallableTypeParameters(
        string declaredName,
        IEnumerable<string> representedTypeParameters)
    {
        var represented = representedTypeParameters.ToArray();
        if (represented.Length > 0)
            return represented;

        var open = declaredName.LastIndexOf('<');
        if (open <= 0 || !declaredName.EndsWith('>'))
            return Array.Empty<string>();

        return declaredName[(open + 1)..^1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private BoundFunction BindFunction(FunctionNode func)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);
        var functionSymbol = _functionSymbols[func];
        using var _identity = PushDeclarationContext(functionSymbol.Id);

        DeclareParameters(functionSymbol.Parameters, func.Parameters);

        // Bind body
        var boundBody = BindStatements(func.Body);

        // Extract declared effects for taint analysis
        var declaredEffects = ExtractEffects(func);

        return new BoundFunction(func.Span, functionSymbol, boundBody, functionScope, declaredEffects);
    }

    private void DeclareParameters(
        IReadOnlyList<VariableSymbol> symbols,
        IReadOnlyList<ParameterNode> parameters)
    {
        for (var index = 0; index < symbols.Count; index++)
        {
            var symbol = symbols[index];
            if (!_scope.TryDeclare(symbol))
            {
                var suggestedName = GenerateUniqueName(symbol.Name);
                _diagnostics.ReportDuplicateDefinitionWithFix(
                    parameters[index].IdentifierSpan,
                    symbol.Name,
                    suggestedName);
            }
        }
    }

    private List<VariableSymbol> BindParameters(IReadOnlyList<ParameterNode> parameters)
    {
        var result = new List<VariableSymbol>(parameters.Count);
        foreach (var parameter in parameters)
        {
            var symbol = CreateLocalVariable(
                parameter.Name,
                parameter.TypeName,
                isMutable: false,
                isParameter: true,
                parameter.Modifier,
                parameter.IdentifierSpan,
                "parameter",
                parameter.DefaultValue);
            if (!_scope.TryDeclare(symbol))
            {
                var suggestedName = GenerateUniqueName(parameter.Name);
                _diagnostics.ReportDuplicateDefinitionWithFix(
                    parameter.IdentifierSpan,
                    parameter.Name,
                    suggestedName);
            }
            result.Add(symbol);
        }
        return result;
    }

    private IReadOnlyList<BoundStatement> BindStatements(IReadOnlyList<StatementNode> statements)
    {
        var result = new List<BoundStatement>();
        foreach (var stmt in statements)
        {
            var bound = BindStatement(stmt);
            if (bound != null)
            {
                result.Add(bound);
            }
        }
        return result;
    }

    private BoundStatement? BindStatement(StatementNode stmt)
    {
        return stmt switch
        {
            CallStatementNode call => BindCallStatement(call),
            ReturnStatementNode ret => BindReturnStatement(ret),
            ForStatementNode forStmt => BindForStatement(forStmt),
            WhileStatementNode whileStmt => BindWhileStatement(whileStmt),
            IfStatementNode ifStmt => BindIfStatement(ifStmt),
            BindStatementNode bind => BindBindStatement(bind),
            BreakStatementNode breakStmt => new BoundBreakStatement(breakStmt.Span),
            ContinueStatementNode continueStmt => new BoundContinueStatement(continueStmt.Span),
            GotoStatementNode gotoStmt => new BoundGotoStatement(gotoStmt.Span, gotoStmt.Label),
            LabelStatementNode labelStmt => new BoundLabelStatement(labelStmt.Span, labelStmt.Label),
            TryStatementNode tryStmt => BindTryStatement(tryStmt),
            MatchStatementNode matchStmt => BindMatchStatement(matchStmt),
            ProofObligationNode proof => BindProofObligation(proof),
            // Class member body statement types
            AssignmentStatementNode assign => BindAssignmentStatement(assign),
            CompoundAssignmentStatementNode compound => BindCompoundAssignment(compound),
            ForeachStatementNode forEach => BindForeachStatement(forEach),
            UsingStatementNode usingStmt => BindUsingStatement(usingStmt),
            ThrowStatementNode throwStmt => new BoundThrowStatement(throwStmt.Span,
                throwStmt.Exception != null ? BindExpression(throwStmt.Exception) : null),
            RethrowStatementNode rethrow => new BoundThrowStatement(rethrow.Span, null),
            DoWhileStatementNode doWhile => BindDoWhileStatement(doWhile),
            ExpressionStatementNode exprStmt => new BoundExpressionStatement(exprStmt.Span, BindExpression(exprStmt.Expression)),
            YieldReturnStatementNode yieldRet => new BoundReturnStatement(yieldRet.Span,
                yieldRet.Expression != null ? BindExpression(yieldRet.Expression) : null),
            YieldBreakStatementNode => new BoundBreakStatement(stmt.Span),
            SyncBlockNode sync => BindSyncBlock(sync),
            PrintStatementNode print => new BoundExpressionStatement(print.Span,
                BindExpression(print.Expression)),
            // Passthrough nodes — no executable semantics
            FallbackCommentNode => null,
            RawCSharpNode => null,
            PreprocessorDirectiveNode => null,
            EventSubscribeNode => null,
            EventUnsubscribeNode => null,
            // Unknown — explicit unsupported node, NOT null
            _ => BindUnsupportedStatement(stmt)
        };
    }

    private BoundProofObligation BindProofObligation(ProofObligationNode proof)
    {
        var condition = BindExpression(proof.Condition);
        return new BoundProofObligation(proof.Span, proof.Id, proof.Description, condition);
    }

    private BoundCallStatement BindCallStatement(CallStatementNode call)
    {
        var args = BindExpressions(call.Arguments);
        var receiverSymbol = ResolveCallReceiver(
            call.Target,
            call.ReceiverSpan ?? call.CalleeSpan);
        var receiverTypeSymbol = receiverSymbol == null
            ? ResolveCallReceiverType(call.Target)
            : null;
        var resolution = ResolveCall(
            call.Span,
            call.Target,
            args,
            call.ArgumentNames,
            call.ArgumentModifiers,
            call.TypeArguments);
        var (resolvedTypeName, resolvedMethodName) = GetResolvedCallIdentity(
            call.Target,
            receiverSymbol,
            receiverTypeSymbol);

        return new BoundCallStatement(
            call.Span,
            call.Target,
            args,
            resolution.Function,
            call.ArgumentNames,
            call.ArgumentModifiers,
            receiverSymbol,
            resolution.Functions,
            call.CalleeSpan,
            call.ReceiverSpan,
            resolution.Kind == OverloadResolutionKind.Inaccessible,
            call.TypeArguments,
            receiverTypeSymbol,
            resolvedTypeName,
            resolvedMethodName,
            (resolution.Function?.Parameters
                .Select(parameter => parameter.TypeName)
                ?? args.Select(argument => argument.TypeName))
                .ToArray());
    }

    private BoundReturnStatement BindReturnStatement(ReturnStatementNode ret)
    {
        var expr = ret.Expression != null ? BindExpression(ret.Expression) : null;
        return new BoundReturnStatement(ret.Span, expr);
    }

    private BoundForStatement BindForStatement(ForStatementNode forStmt)
    {
        using var _ = PushScope(_scope.CreateChild());

        // Declare loop variable
        var loopVar = CreateLocalVariable(
            forStmt.VariableName,
            "INT",
            isMutable: true,
            isParameter: false,
            ParameterModifier.None,
            forStmt.VariableSpan,
            "for");
        if (!_scope.TryDeclare(loopVar))
        {
            _diagnostics.ReportError(forStmt.Span, DiagnosticCode.DuplicateDefinition,
                $"Variable '{forStmt.VariableName}' is already defined");
        }

        var from = BindExpression(forStmt.From);
        var to = BindExpression(forStmt.To);
        var step = forStmt.Step != null ? BindExpression(forStmt.Step) : null;
        var body = BindStatements(forStmt.Body);

        return new BoundForStatement(forStmt.Span, loopVar, from, to, step, body);
    }

    private BoundWhileStatement BindWhileStatement(WhileStatementNode whileStmt)
    {
        using var _ = PushScope(_scope.CreateChild());

        var condition = BindExpression(whileStmt.Condition);
        var body = BindStatements(whileStmt.Body);

        return new BoundWhileStatement(whileStmt.Span, condition, body);
    }

    private BoundIfStatement BindIfStatement(IfStatementNode ifStmt)
    {
        var condition = BindExpression(ifStmt.Condition);

        IReadOnlyList<BoundStatement> thenBody;
        {
            using var _ = PushScope(_scope.CreateChild());
            thenBody = BindStatements(ifStmt.ThenBody);
        }

        var elseIfClauses = new List<BoundElseIfClause>();
        foreach (var elseIf in ifStmt.ElseIfClauses)
        {
            var elseIfCondition = BindExpression(elseIf.Condition);
            using var _ = PushScope(_scope.CreateChild());
            var elseIfBody = BindStatements(elseIf.Body);
            elseIfClauses.Add(new BoundElseIfClause(elseIf.Span, elseIfCondition, elseIfBody));
        }

        IReadOnlyList<BoundStatement>? elseBody = null;
        if (ifStmt.ElseBody != null)
        {
            using var _ = PushScope(_scope.CreateChild());
            elseBody = BindStatements(ifStmt.ElseBody);
        }

        return new BoundIfStatement(ifStmt.Span, condition, thenBody, elseIfClauses, elseBody);
    }

    private BoundStatement BindBindStatement(BindStatementNode bind)
    {
        BoundExpression? initializer = null;
        string typeName;

        if (bind.Initializer != null)
        {
            initializer = BindExpression(bind.Initializer);
            // Infer type from initializer if not specified
            typeName = bind.TypeName ?? initializer.TypeName;
        }
        else if (bind.TypeName != null)
        {
            typeName = bind.TypeName;
        }
        else
        {
            // §B{name} with neither :type nor initializer: previously
            // silently defaulted to INT (latent bug). Per RFC v0.6 bind
            // inference formalization §3.2, this is now Calor0250.
            // Fall back to "INT" so subsequent binding still produces a
            // usable symbol and we don't cascade NREs through the bound tree.
            _diagnostics.ReportError(bind.Span, DiagnosticCode.BindRequiresTypeOrInitializer,
                $"Binding '{bind.Name}' has no type annotation and no initializer. " +
                "Add either ':type' (e.g. '§B{" + bind.Name + ":i32}') " +
                "or an initializer expression so the binder can infer the type.");
            typeName = "INT";
        }

        var rebindTarget = bind.IsMutable ? FindRebindTarget(bind.Name) : null;
        if (rebindTarget != null)
        {
            if (!CanRebind(rebindTarget))
            {
                _diagnostics.ReportError(
                    bind.IdentifierSpan,
                    DiagnosticCode.BindReassignsImmutable,
                    $"Binding '{bind.Name}' is immutable and cannot be rebound. " +
                    "Declare it mutable at its original declaration or use a new name.");

                var recoveryVariable = CreateLocalVariable(
                    bind.Name,
                    typeName,
                    isMutable: true,
                    isParameter: false,
                    ParameterModifier.None,
                    bind.IdentifierSpan,
                    "invalid-rebind");
                return new BoundBindStatement(bind.Span, recoveryVariable, initializer);
            }

            var annotatedType = bind.TypeName == null
                ? null
                : TypeIdentity.Canonicalize(bind.TypeName);
            var valueType = initializer?.TypeName;
            if ((annotatedType != null
                    && !AreAssignmentCompatible(rebindTarget.TypeName, annotatedType))
                || (valueType != null
                    && !AreAssignmentCompatible(rebindTarget.TypeName, valueType)))
            {
                _diagnostics.ReportError(
                    bind.IdentifierSpan,
                    DiagnosticCode.BindRebindTypeMismatch,
                    $"Mutable binding '{bind.Name}' has type '{rebindTarget.TypeName}' and cannot be " +
                    $"rebound with '{annotatedType ?? valueType}'.");
            }

            if (initializer != null)
            {
                return new BoundAssignmentStatement(
                    bind.Span,
                    new BoundVariableExpression(bind.IdentifierSpan, rebindTarget),
                    initializer);
            }

            _diagnostics.ReportError(
                bind.IdentifierSpan,
                DiagnosticCode.BindRequiresTypeOrInitializer,
                $"Mutable rebind '{bind.Name}' requires an initializer value.");
            return new BoundBindStatement(bind.Span, rebindTarget, initializer);
        }

        var variable = CreateLocalVariable(
            bind.Name,
            typeName,
            bind.IsMutable,
            isParameter: false,
            ParameterModifier.None,
            bind.IdentifierSpan,
            "local");

        if (!_scope.TryDeclare(variable))
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.DuplicateDefinition,
                $"Variable '{bind.Name}' is already defined");
        }

        return new BoundBindStatement(bind.Span, variable, initializer);
    }

    private VariableSymbol? FindRebindTarget(string name)
    {
        if (_scope.Lookup(name) is not VariableSymbol variable)
            return null;

        var classMember = _currentClassScope?.LookupLocal(name);
        return classMember is VariableSymbol member && member.Id == variable.Id
            ? null
            : variable;
    }

    private static bool CanRebind(VariableSymbol variable) =>
        variable.IsMutable
        || (variable.IsParameter
            && !variable.Modifier.HasFlag(ParameterModifier.In));

    private static bool AreAssignmentCompatible(string targetType, string valueType)
    {
        var target = TypeIdentity.Canonicalize(targetType);
        var value = TypeIdentity.Canonicalize(valueType);
        if (target == value || target is "OBJECT" || value is "OBJECT" or "<unresolved>")
            return true;

        return value switch
        {
            "INT[bits=8][signed=true]" => target is
                "INT[bits=16][signed=true]" or "INT" or "LONG"
                or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT[bits=8][signed=false]" => target is
                "INT[bits=16][signed=true]" or "INT[bits=16][signed=false]"
                or "INT" or "UINT" or "LONG" or "ULONG"
                or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT[bits=16][signed=true]" => target is
                "INT" or "LONG" or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT[bits=16][signed=false]" => target is
                "INT" or "UINT" or "LONG" or "ULONG"
                or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "INT" => target is "LONG" or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "UINT" => target is "LONG" or "ULONG" or "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "LONG" or "ULONG" => target is "FLOAT[bits=32]" or "FLOAT" or "DECIMAL",
            "FLOAT[bits=32]" => target is "FLOAT",
            _ => false,
        };
    }

    private BoundExpression BindExpression(ExpressionNode expr)
    {
        ArgumentNullException.ThrowIfNull(expr);
        ExpressionsBound++;
        return ExpressionBinders.TryGetValue(expr.GetType(), out var binder)
            ? binder(this, expr)
            : BindUnsupportedExpression(expr);
    }

    private BoundExpression BindIntLiteral(IntLiteralNode literal)
    {
        var typeName = literal.IsUnsigned
            ? literal.IsLong || literal.UnsignedValue > uint.MaxValue ? "ULONG" : "UINT"
            : literal.IsLong || literal.Value is > int.MaxValue or < int.MinValue ? "LONG" : "INT";
        return new BoundIntLiteral(
            literal.Span,
            literal.Value,
            literal.UnsignedValue,
            literal.IsUnsigned,
            typeName);
    }

    private BoundExpression BindFloatLiteral(FloatLiteralNode literal)
    {
        if (literal.IsDecimal)
            return new BoundDecimalLiteral(literal.Span, (decimal)literal.Value);

        var typeName = literal.IsSingle ? "FLOAT[bits=32]" : "FLOAT";
        var value = literal.IsSingle ? (double)(float)literal.Value : literal.Value;
        return new BoundFloatLiteral(literal.Span, value, typeName);
    }

    private BoundExpression BindStringLiteral(StringLiteralNode literal) =>
        new BoundStringLiteral(
            literal.Span,
            literal.Value,
            literal.IsMultiline,
            literal.IsUtf8);

    private BoundExpression BindThisExpression(ThisExpressionNode expression)
    {
        if (!_isStaticContext && _currentClassName != null)
            return new BoundThisExpression(expression.Span, _currentClassName);

        return BindUnsupportedExpression(
            expression,
            _currentClassName ?? "UNKNOWN",
            reason: _isStaticContext
                ? "'this' is unavailable in a static context"
                : "'this' is unavailable outside an instance member");
    }

    private BoundExpression BindBaseExpression(BaseExpressionNode expression)
    {
        if (!_isStaticContext && _currentClassName != null)
        {
            var baseClass = ResolveBaseClass(_currentClass);
            return new BoundBaseExpression(
                expression.Span,
                baseClass == null ? "OBJECT" : _qualifiedClassNames[baseClass]);
        }

        return BindUnsupportedExpression(
            expression,
            reason: _isStaticContext
                ? "'base' is unavailable in a static context"
                : "'base' is unavailable outside an instance member");
    }

    private BoundExpression BindFieldAccess(FieldAccessNode fieldAccess)
    {
        var target = BindExpression(fieldAccess.Target);
        var resolvedFields = fieldAccess.Target switch
        {
            ThisExpressionNode => ResolveAccessibleMembers(_currentClass, fieldAccess.FieldName),
            BaseExpressionNode => ResolveAccessibleMembers(
                ResolveBaseClass(_currentClass),
                fieldAccess.FieldName),
            _ => Array.Empty<Symbol>(),
        };
        var fields = resolvedFields.OfType<VariableSymbol>().ToArray();
        var fieldTypes = fields
            .Select(field => TypeIdentity.Canonicalize(field.TypeName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new BoundFieldAccessExpression(
            fieldAccess.Span,
            target,
            fieldAccess.FieldName,
            fieldTypes.Length == 1 ? fields[0].TypeName : "OBJECT",
            fields.FirstOrDefault(),
            fieldAccess.FieldNameSpan,
            fields);
    }

    private BoundExpression BindTypeOperation(TypeOperationNode typeOp)
    {
        var operand = BindExpression(typeOp.Operand);
        return new BoundTypeOperationExpression(
            typeOp.Span,
            typeOp.Operation,
            operand,
            typeOp.TargetType);
    }

    private BoundExpression BindIsPattern(IsPatternNode isPattern)
    {
        var operand = BindExpression(isPattern.Operand);
        if (isPattern.VariableName != null)
            DeclarePatternVariable(isPattern.Span, isPattern.VariableName, isPattern.TargetType);
        return new BoundIsPatternExpression(
            isPattern.Span,
            operand,
            isPattern.TargetType,
            isPattern.VariableName);
    }

    private BoundExpression BindNewExpression(NewExpressionNode newExpr)
    {
        var boundArgs = BindExpressions(newExpr.Arguments);
        var boundTypeReference = BindTypeReference(newExpr.TypeReference);
        var resolution = ResolveCall(
            newExpr.Span,
            $"{newExpr.TypeName}..ctor",
            boundArgs,
            argumentNames: null,
            argumentModifiers: null,
            typeArguments: null);
        var initializers = newExpr.Initializers
            .Select(initializer => new BoundObjectInitializer(
                initializer.Value.Span,
                initializer.PropertyName,
                BindExpression(initializer.Value)))
            .ToArray();

        return new BoundNewExpression(
            newExpr.Span,
            newExpr.TypeName,
            newExpr.TypeArguments,
            boundArgs,
            initializers,
            resolution.Function,
            boundTypeReference.ResolvedType,
            newExpr.TypeNameSpan,
            resolution.Functions,
            boundTypeReference);
    }

    private BoundTypeReference BindTypeReference(TypeReferenceNode typeReference)
    {
        var typeArguments = typeReference.TypeArguments
            .Select(BindTypeReference)
            .ToArray();
        return new BoundTypeReference(
            typeReference.Name,
            typeReference.Span,
            string.IsNullOrEmpty(typeReference.Name)
                ? null
                : ResolveTypeSymbol(typeReference.Name),
            typeArguments);
    }

    private BoundExpression BindArrayAccess(ArrayAccessNode arrayAccess)
    {
        var array = BindExpression(arrayAccess.Array);
        var index = BindExpression(arrayAccess.Index);
        return new BoundArrayAccess(arrayAccess.Span, array, index);
    }

    private BoundExpression BindMultiDimArrayAccess(MultiDimArrayAccessNode arrayAccess)
    {
        var array = BindExpression(arrayAccess.Array);
        var indices = BindExpressions(arrayAccess.Indices);
        return new BoundMultiDimArrayAccess(arrayAccess.Span, array, indices);
    }

    private BoundExpression BindArrayCreation(ArrayCreationNode array)
    {
        return new BoundArrayCreation(
            array.Span,
            array.Id,
            array.Name,
            array.ElementType,
            array.Size == null ? null : BindExpression(array.Size),
            BindExpressions(array.Initializer),
            array.Attributes);
    }

    private BoundExpression BindMultiDimArrayCreation(MultiDimArrayCreationNode array)
    {
        return new BoundMultiDimArrayCreation(
            array.Span,
            array.Id,
            array.Name,
            array.ElementType,
            array.Rank,
            BindExpressions(array.DimensionSizes),
            array.Initializer
                .Select(row => (IReadOnlyList<BoundExpression>)BindExpressions(row))
                .ToArray());
    }

    private BoundExpression BindArrayLength(ArrayLengthNode arrayLength)
    {
        var array = BindExpression(arrayLength.Array);
        return new BoundArrayLength(arrayLength.Span, array);
    }

    private BoundExpression BindListCreation(ListCreationNode list)
    {
        return new BoundListCreation(
            list.Span,
            list.Id,
            list.Name,
            list.ElementType,
            BindExpressions(list.Elements),
            list.Attributes);
    }

    private BoundExpression BindDictionaryCreation(DictionaryCreationNode dictionary)
    {
        return new BoundDictionaryCreation(
            dictionary.Span,
            dictionary.Id,
            dictionary.Name,
            dictionary.KeyType,
            dictionary.ValueType,
            dictionary.Entries
                .Select(entry => new BoundPair(
                    BindExpression(entry.Key),
                    BindExpression(entry.Value),
                    entry.Span))
                .ToArray(),
            dictionary.Attributes);
    }

    private BoundExpression BindSetCreation(SetCreationNode set)
    {
        return new BoundSetCreation(
            set.Span,
            set.Id,
            set.Name,
            set.ElementType,
            BindExpressions(set.Elements),
            set.Attributes);
    }

    private BoundExpression BindCollectionContains(CollectionContainsNode contains)
    {
        var collection = BindReferenceExpression(new ReferenceNode(contains.Span, contains.CollectionName));
        var value = BindExpression(contains.KeyOrValue);
        return new BoundCollectionContains(
            contains.Span,
            contains.CollectionName,
            value,
            contains.Mode,
            collection);
    }

    private BoundExpression BindCollectionCount(CollectionCountNode count)
    {
        var collection = BindExpression(count.Collection);
        return new BoundCollectionCount(count.Span, collection);
    }

    private BoundExpression BindRecordCreation(RecordCreationNode record)
    {
        return new BoundRecordCreation(
            record.Span,
            record.TypeName,
            record.Fields
                .Select(field => new BoundNamedValue(
                    field.FieldName,
                    BindExpression(field.Value),
                    field.Span))
                .ToArray());
    }

    private BoundExpression BindAnonymousObjectCreation(AnonymousObjectCreationNode anonymous)
    {
        return new BoundAnonymousObjectCreation(
            anonymous.Span,
            anonymous.Initializers
                .Select(initializer => new BoundNamedValue(
                    initializer.PropertyName,
                    BindExpression(initializer.Value),
                    initializer.Value.Span))
                .ToArray());
    }

    private BoundExpression BindWithExpression(WithExpressionNode withExpression)
    {
        var target = BindExpression(withExpression.Target);
        return new BoundWithExpression(
            withExpression.Span,
            target,
            withExpression.Assignments
                .Select(assignment => new BoundNamedValue(
                    assignment.PropertyName,
                    BindExpression(assignment.Value),
                    assignment.Span))
                .ToArray());
    }

    private BoundExpression BindSomeExpression(SomeExpressionNode some)
    {
        var value = BindExpression(some.Value);
        return new BoundSomeExpression(some.Span, value);
    }

    private BoundExpression BindNoneExpression(NoneExpressionNode none)
    {
        var innerType = string.IsNullOrWhiteSpace(none.TypeName) ? "OBJECT" : none.TypeName!;
        return new BoundNoneLiteral(none.Span, MakeOptionType(innerType));
    }

    private BoundExpression BindOkExpression(OkExpressionNode ok)
    {
        var value = BindExpression(ok.Value);
        return new BoundOkExpression(ok.Span, value);
    }

    private BoundExpression BindErrExpression(ErrExpressionNode err)
    {
        var error = BindExpression(err.Error);
        return new BoundErrExpression(err.Span, error);
    }

    private BoundExpression BindAwaitExpression(AwaitExpressionNode awaitExpression)
    {
        var awaited = BindExpression(awaitExpression.Awaited);
        return Structural(
            awaitExpression,
            UnwrapAwaitedType(awaited.TypeName),
            [awaited],
            new Dictionary<string, object?>
            {
                ["ConfigureAwait"] = awaitExpression.ConfigureAwait,
            });
    }

    private BoundExpression BindNullCoalesce(NullCoalesceNode coalesce)
    {
        var left = BindExpression(coalesce.Left);
        var right = BindExpression(coalesce.Right);
        var leftValueType = UnwrapOptionOrNullable(left.TypeName);
        return Structural(
            coalesce,
            GetCommonType(leftValueType, right.TypeName),
            [left, right],
            deferredChildren: [right]);
    }

    private BoundExpression BindNullConditional(NullConditionalNode conditional)
    {
        var target = BindExpression(conditional.Target);
        return Structural(
            conditional,
            "OBJECT",
            [target],
            new Dictionary<string, object?>
            {
                ["MemberName"] = conditional.MemberName,
                ["TargetType"] = target.TypeName,
            });
    }

    private BoundExpression BindRangeExpression(RangeExpressionNode range)
    {
        var children = new List<BoundExpression>(2);
        if (range.Start != null)
            children.Add(BindExpression(range.Start));
        if (range.End != null)
            children.Add(BindExpression(range.End));

        return new BoundRangeExpression(
            range.Span,
            range.Start == null ? null : children[0],
            range.End == null ? null : children[^1]);
    }

    private BoundExpression BindIndexFromEnd(IndexFromEndNode index)
    {
        var offset = BindExpression(index.Offset);
        return new BoundIndexFromEnd(index.Span, offset);
    }

    private BoundExpression BindTupleLiteral(TupleLiteralNode tuple)
    {
        var elements = BindExpressions(tuple.Elements);
        return new BoundTupleLiteral(tuple.Span, elements);
    }

    private BoundExpression BindTypeOf(TypeOfExpressionNode typeOf)
    {
        return Structural(
            typeOf,
            "TYPE",
            metadata: new Dictionary<string, object?>
            {
                ["OperandType"] = typeOf.TypeName,
            });
    }

    private BoundExpression BindGenericType(GenericTypeNode genericType)
    {
        return new BoundGenericTypeExpression(
            genericType.Span,
            genericType.TypeName,
            genericType.TypeArguments);
    }

    private BoundExpression BindSelfReference(SelfRefNode selfReference) =>
        Structural(selfReference, "OBJECT");

    private BoundExpression BindThrowExpression(ThrowExpressionNode throwExpression)
    {
        var exception = BindExpression(throwExpression.Exception);
        return new BoundThrowExpression(throwExpression.Span, exception);
    }

    private BoundExpression BindInterpolatedString(InterpolatedStringNode interpolated)
    {
        var parts = new List<BoundInterpolatedStringPart>(interpolated.Parts.Count);
        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case InterpolatedStringTextNode text:
                    parts.Add(new BoundInterpolatedStringPart(text.Span, text.Text, null));
                    break;
                case InterpolatedStringExpressionNode expression:
                    parts.Add(new BoundInterpolatedStringPart(
                        expression.Span,
                        null,
                        BindExpression(expression.Expression),
                        expression.FormatSpecifier,
                        expression.AlignmentClause));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown interpolated string part '{part.GetType().Name}'");
            }
        }

        return new BoundInterpolatedStringExpression(interpolated.Span, parts);
    }

    private BoundExpression BindLambdaExpression(LambdaExpressionNode lambda)
    {
        var parentContext = _declarationContext.IsNone ? _moduleSymbolId : _declarationContext;
        var lambdaIdentity = CreateDeclarationId(
            parentContext,
            "lambda",
            lambda.Id,
            "lambda");
        using var _scopeGuard = PushScope(_scope.CreateChild());
        using var _staticGuard = PushStaticContext(lambda.IsStatic);
        using var _identityGuard = PushDeclarationContext(lambdaIdentity);

        var parameters = new List<VariableSymbol>(lambda.Parameters.Count);
        foreach (var parameter in lambda.Parameters)
        {
            var symbol = CreateLocalVariable(
                parameter.Name,
                parameter.TypeName ?? "OBJECT",
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                parameter.IdentifierSpan,
                "parameter");
            if (!_scope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    parameter.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Lambda parameter '{parameter.Name}' is already defined");
            }
            parameters.Add(symbol);
        }

        var expressionBody = lambda.ExpressionBody != null
            ? BindExpression(lambda.ExpressionBody)
            : null;
        var statementBody = lambda.StatementBody != null
            ? BindStatements(lambda.StatementBody)
            : null;
        var returnType = expressionBody?.TypeName
            ?? GetCommonType(CollectReturnTypes(statementBody))
            ?? "VOID";
        var effects = lambda.Effects?.Effects
            .Select(effect => $"{effect.Key}:{effect.Value}")
            .ToArray()
            ?? Array.Empty<string>();

        return new BoundLambdaExpression(
            lambda.Span,
            lambda.Id,
            parameters,
            lambda.Effects,
            effects,
            lambda.Attributes,
            lambda.IsAsync,
            lambda.IsStatic,
            expressionBody,
            statementBody,
            returnType);
    }

    private BoundExpression BindMatchExpression(MatchExpressionNode match)
    {
        var target = BindExpression(match.Target);
        var cases = BindMatchCases(match.Cases);
        var resultType = GetCommonType(
                cases.Where(matchCase => matchCase.Result != null)
                    .Select(matchCase => matchCase.Result!.TypeName))
            ?? "OBJECT";
        return new BoundMatchExpression(
            match.Span,
            match.Id,
            target,
            cases,
            match.Attributes,
            resultType);
    }

    private IReadOnlyList<BoundMatchCase> BindMatchCases(IReadOnlyList<MatchCaseNode> cases)
    {
        var boundCases = new List<BoundMatchCase>(cases.Count);
        foreach (var matchCase in cases)
        {
            using var _ = PushScope(_scope.CreateChild());
            var pattern = BindPattern(matchCase.Pattern);
            var guard = matchCase.Guard != null ? BindExpression(matchCase.Guard) : null;
            var body = BindStatements(matchCase.Body);
            var result = body.LastOrDefault() is BoundReturnStatement { Expression: not null } returnStatement
                ? returnStatement.Expression
                : null;
            boundCases.Add(new BoundMatchCase(
                matchCase.Span,
                pattern,
                matchCase.Pattern is WildcardPatternNode,
                guard,
                body,
                result));
        }
        return boundCases;
    }

    private BoundPattern BindPattern(PatternNode pattern)
    {
        switch (pattern)
        {
            case WildcardPatternNode:
                return Pattern(pattern);
            case VariablePatternNode variable:
                if (!variable.Name.Contains('.', StringComparison.Ordinal))
                    DeclarePatternVariable(variable.IdentifierSpan, variable.Name, "OBJECT");
                return Pattern(
                    variable,
                    metadata: new Dictionary<string, object?>
                    {
                        ["Name"] = variable.Name,
                        ["IsConstant"] = variable.Name.Contains('.', StringComparison.Ordinal),
                    });
            case VarPatternNode variable:
                DeclarePatternVariable(variable.IdentifierSpan, variable.Name, "OBJECT");
                return Pattern(
                    variable,
                    metadata: new Dictionary<string, object?> { ["Name"] = variable.Name });
            case TypePatternNode typePattern:
                if (typePattern.BindingName != null)
                    DeclarePatternVariable(
                        typePattern.BindingSpan ?? typePattern.Span,
                        typePattern.BindingName,
                        typePattern.TypeName);
                return Pattern(
                    typePattern,
                    metadata: new Dictionary<string, object?>
                    {
                        ["TypeName"] = typePattern.TypeName,
                        ["BindingName"] = typePattern.BindingName,
                    });
            case LiteralPatternNode literal:
                return Pattern(literal, expressions: [BindExpression(literal.Literal)]);
            case ConstantPatternNode constant:
                return Pattern(constant, expressions: [BindExpression(constant.Value)]);
            case RelationalPatternNode relational:
                return Pattern(
                    relational,
                    metadata: new Dictionary<string, object?> { ["Operator"] = relational.Operator },
                    expressions: [BindExpression(relational.Value)]);
            case SomePatternNode some:
                return Pattern(some, patterns: [BindPattern(some.InnerPattern)]);
            case NonePatternNode:
                return Pattern(pattern);
            case OkPatternNode ok:
                return Pattern(ok, patterns: [BindPattern(ok.InnerPattern)]);
            case ErrPatternNode err:
                return Pattern(err, patterns: [BindPattern(err.InnerPattern)]);
            case PositionalPatternNode positional:
                return Pattern(
                    positional,
                    metadata: new Dictionary<string, object?> { ["TypeName"] = positional.TypeName },
                    patterns: positional.Patterns.Select(BindPattern).ToArray());
            case PropertyPatternNode property:
                return Pattern(
                    property,
                    metadata: new Dictionary<string, object?>
                    {
                        ["TypeName"] = property.TypeName,
                        ["PropertyNames"] = property.Matches.Select(match => match.PropertyName).ToArray(),
                    },
                    patterns: property.Matches.Select(match => BindPattern(match.Pattern)).ToArray());
            case ListPatternNode list:
                var listPatterns = list.Patterns.Select(BindPattern).ToList();
                if (list.SlicePattern != null)
                    listPatterns.Insert(Math.Min(list.SliceIndex, listPatterns.Count), BindPattern(list.SlicePattern));
                return Pattern(
                    list,
                    metadata: new Dictionary<string, object?>
                    {
                        ["SliceIndex"] = list.SliceIndex,
                        ["HasSlice"] = list.SlicePattern != null,
                    },
                    patterns: listPatterns);
            case NegatedPatternNode negated:
                return Pattern(negated, patterns: [BindPattern(negated.Inner)]);
            case OrPatternNode orPattern:
                return Pattern(orPattern, patterns: [BindPattern(orPattern.Left), BindPattern(orPattern.Right)]);
            case AndPatternNode andPattern:
                return Pattern(andPattern, patterns: [BindPattern(andPattern.Left), BindPattern(andPattern.Right)]);
            default:
                return Pattern(
                    pattern,
                    metadata: new Dictionary<string, object?>
                    {
                        ["AnalysisIncomplete"] = true,
                    });
        }
    }

    private void DeclarePatternVariable(Parsing.TextSpan span, string name, string typeName)
    {
        if (_scope.LookupLocal(name) is VariableSymbol existing
            && string.Equals(existing.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
            return;

        var symbol = CreateLocalVariable(
            name,
            typeName,
            isMutable: false,
            isParameter: false,
            ParameterModifier.None,
            span,
            "pattern");
        if (!_scope.TryDeclare(symbol))
        {
            _diagnostics.ReportError(
                span,
                DiagnosticCode.DuplicateDefinition,
                $"Pattern variable '{name}' is already defined");
        }
    }

    private static BoundPattern Pattern(
        PatternNode pattern,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyList<BoundPattern>? patterns = null,
        IReadOnlyList<BoundExpression>? expressions = null) =>
        new(pattern.Span, pattern.GetType().Name, metadata, patterns, expressions);

    private BoundExpression BindForallExpression(ForallExpressionNode forall) =>
        BindQuantifier(forall, forall.BoundVariables, forall.Body);

    private BoundExpression BindExistsExpression(ExistsExpressionNode exists) =>
        BindQuantifier(exists, exists.BoundVariables, exists.Body);

    private BoundExpression BindQuantifier(
        ExpressionNode quantifier,
        IReadOnlyList<QuantifierVariableNode> variables,
        ExpressionNode body)
    {
        using var _ = PushScope(_scope.CreateChild());
        var symbols = new List<VariableSymbol>(variables.Count);
        foreach (var variable in variables)
        {
            var symbol = CreateLocalVariable(
                variable.Name,
                variable.TypeName,
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                variable.IdentifierSpan,
                "quantifier");
            if (!_scope.TryDeclare(symbol))
            {
                _diagnostics.ReportError(
                    variable.Span,
                    DiagnosticCode.DuplicateDefinition,
                    $"Quantifier variable '{variable.Name}' is already defined");
            }
            symbols.Add(symbol);
        }

        return new BoundQuantifierExpression(
            quantifier.Span,
            quantifier.GetType().Name,
            symbols,
            BindExpression(body));
    }

    private BoundExpression BindImplicationExpression(ImplicationExpressionNode implication)
    {
        var antecedent = BindExpression(implication.Antecedent);
        var consequent = BindExpression(implication.Consequent);
        return Structural(
            implication,
            "BOOL",
            [antecedent, consequent],
            deferredChildren: [consequent]);
    }

    private BoundExpression BindStringOperation(StringOperationNode operation)
    {
        var resultType = operation.Operation switch
        {
            StringOp.Length or StringOp.IndexOf => "INT",
            StringOp.Contains or StringOp.StartsWith or StringOp.EndsWith
                or StringOp.IsNullOrEmpty or StringOp.IsNullOrWhiteSpace
                or StringOp.Equals or StringOp.RegexTest => "BOOL",
            StringOp.Split or StringOp.RegexSplit => "str[]",
            StringOp.RegexMatch => "OBJECT",
            _ => "STRING",
        };
        return Structural(
            operation,
            resultType,
            BindExpressions(operation.Arguments),
            new Dictionary<string, object?>
            {
                ["Operation"] = operation.Operation,
                ["ComparisonMode"] = operation.ComparisonMode,
            });
    }

    private BoundExpression BindCharOperation(CharOperationNode operation)
    {
        var resultType = operation.Operation switch
        {
            CharOp.CharCode => "INT",
            CharOp.IsLetter or CharOp.IsDigit or CharOp.IsWhiteSpace
                or CharOp.IsUpper or CharOp.IsLower => "BOOL",
            _ => "CHAR",
        };
        return Structural(
            operation,
            resultType,
            BindExpressions(operation.Arguments),
            new Dictionary<string, object?> { ["Operation"] = operation.Operation });
    }

    private BoundExpression BindStringBuilderOperation(StringBuilderOperationNode operation)
    {
        var resultType = operation.Operation switch
        {
            StringBuilderOp.ToString => "STRING",
            StringBuilderOp.Length => "INT",
            _ => "StringBuilder",
        };
        return Structural(
            operation,
            resultType,
            BindExpressions(operation.Arguments),
            new Dictionary<string, object?> { ["Operation"] = operation.Operation });
    }

    private BoundExpression BindStackAlloc(StackAllocNode stackAlloc)
    {
        var children = new List<BoundExpression>();
        if (stackAlloc.Size != null)
            children.Add(BindExpression(stackAlloc.Size));
        children.AddRange(BindExpressions(stackAlloc.Initializer));
        return Structural(
            stackAlloc,
            $"Span<{stackAlloc.ElementType}>",
            children,
            new Dictionary<string, object?>
            {
                ["ElementType"] = stackAlloc.ElementType,
                ["HasSize"] = stackAlloc.Size != null,
                ["InitializerCount"] = stackAlloc.Initializer.Count,
            });
    }

    private BoundExpression BindAddressOf(AddressOfNode addressOf)
    {
        var operand = BindExpression(addressOf.Operand);
        return Structural(addressOf, $"{operand.TypeName}*", [operand]);
    }

    private BoundExpression BindPointerDereference(PointerDereferenceNode dereference)
    {
        var operand = BindExpression(dereference.Operand);
        var resultType = operand.TypeName.EndsWith('*')
            ? operand.TypeName[..^1]
            : "OBJECT";
        return Structural(dereference, resultType, [operand]);
    }

    private BoundExpression BindSizeOf(SizeOfNode sizeOf)
    {
        return Structural(
            sizeOf,
            "INT",
            metadata: new Dictionary<string, object?> { ["OperandType"] = sizeOf.TypeName });
    }

    private BoundExpression BindExpressionCall(ExpressionCallNode call)
    {
        var target = BindExpression(call.TargetExpression);
        var arguments = BindExpressions(call.Arguments);
        return new BoundExpressionCall(call.Span, target, arguments);
    }

    private BoundExpression BindFallbackInterop(FallbackExpressionNode fallback)
    {
        ReportAnalysisIncomplete(fallback, $"Interop fallback feature '{fallback.FeatureName}'");
        return new BoundInteropExpression(
            fallback.Span,
            nameof(FallbackExpressionNode),
            fallback.OriginalCSharp,
            "OBJECT",
            metadata: new Dictionary<string, object?>
            {
                ["FeatureName"] = fallback.FeatureName,
                ["Suggestion"] = fallback.Suggestion,
            });
    }

    private BoundExpression BindRawCSharpInterop(RawCSharpExpressionNode raw)
    {
        ReportAnalysisIncomplete(raw, "Raw C# expression is opaque to Calor analysis");
        return new BoundInteropExpression(
            raw.Span,
            nameof(RawCSharpExpressionNode),
            raw.CSharpCode,
            "OBJECT",
            metadata: new Dictionary<string, object?>
            {
                ["CSharpCode"] = raw.CSharpCode,
            });
    }

    private BoundExpression BindReferenceExpression(ReferenceNode refNode)
    {
        var symbols = _scope.LookupAll(refNode.Name);
        if (symbols.Count == 0)
            symbols = ResolveAccessibleMembers(_currentClass, refNode.Name);

        if (symbols.Count == 0)
        {
            var similarName = _scope.FindSimilarName(refNode.Name);
            if (similarName != null)
            {
                // Create a fix to replace the undefined reference with the similar name
                var fix = new SuggestedFix(
                    $"Change to '{similarName}'",
                    TextEdit.Replace(
                        "", // File path will be set from DiagnosticBag._currentFilePath
                        refNode.Span.Line,
                        refNode.Span.Column,
                        refNode.Span.Line,
                        refNode.Span.Column + refNode.Name.Length,
                        similarName));

                _diagnostics.ReportErrorWithFix(refNode.Span, DiagnosticCode.UndefinedReference,
                    $"Undefined variable '{refNode.Name}'. Did you mean '{similarName}'?", fix);
            }
            else
            {
                _diagnostics.ReportError(refNode.Span, DiagnosticCode.UndefinedReference,
                    $"Undefined variable '{refNode.Name}'");
            }
            // Return a dummy variable to continue analysis
            return new BoundVariableExpression(
                refNode.Span,
                CreateUnresolvedVariable(refNode));
        }

        if (symbols.All(symbol => symbol is VariableSymbol))
        {
            var variables = symbols.Cast<VariableSymbol>().ToArray();
            if (variables.Any(variable =>
                    variable.DeclaringTypeName != null
                    && !IsMemberAccessible(variable)))
            {
                _diagnostics.ReportError(
                    refNode.Span,
                    DiagnosticCode.UndefinedReference,
                    $"Member '{refNode.Name}' is not accessible in this context");
                return new BoundVariableExpression(
                    refNode.Span,
                    CreateUnresolvedVariable(refNode));
            }

            if (_isStaticContext
                && variables.Any(variable =>
                    variable.DeclaringTypeName != null
                    && !variable.IsStatic))
            {
                _diagnostics.ReportError(
                    refNode.Span,
                    DiagnosticCode.InstanceMemberInStaticContext,
                    $"Instance member '{refNode.Name}' cannot be accessed by a bare reference in a static context");
                return new BoundVariableExpression(
                    refNode.Span,
                    CreateUnresolvedVariable(refNode));
            }

            return new BoundVariableExpression(refNode.Span, variables[0], variables);
        }

        // Symbol exists but is not a variable - provide helpful fix
        _diagnostics.ReportNotAVariableWithFix(
            refNode.Span,
            refNode.Name,
            symbols.All(symbol => symbol is FunctionSymbol));
        return new BoundVariableExpression(
            refNode.Span,
            CreateUnresolvedVariable(refNode));
    }

    private BoundBinaryExpression BindBinaryOperation(BinaryOperationNode binOp)
    {
        var left = BindExpression(binOp.Left);
        var right = BindExpression(binOp.Right);

        // Determine result type based on operator
        var resultType = GetBinaryOperationResultType(binOp.Operator, left.TypeName, right.TypeName);

        return new BoundBinaryExpression(binOp.Span, binOp.Operator, left, right, resultType);
    }

    private static string GetBinaryOperationResultType(BinaryOperator op, string leftType, string rightType)
    {
        if (op is BinaryOperator.Equal or BinaryOperator.NotEqual
            or BinaryOperator.LessThan or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual
            or BinaryOperator.And or BinaryOperator.Or)
            return "BOOL";

        if (op == BinaryOperator.Add
            && (NormalizeTypeName(leftType) == "STRING" || NormalizeTypeName(rightType) == "STRING"))
            return "STRING";

        if (op is BinaryOperator.LeftShift or BinaryOperator.RightShift)
            return NormalizeTypeName(leftType);

        return GetCommonType(leftType, rightType);
    }

    private BoundUnaryExpression BindUnaryOperation(UnaryOperationNode unaryOp)
    {
        var operand = BindExpression(unaryOp.Operand);
        var resultType = unaryOp.Operator switch
        {
            UnaryOperator.Not => "BOOL",
            UnaryOperator.Negate => operand.TypeName,
            UnaryOperator.BitwiseNot => operand.TypeName,
            _ => operand.TypeName
        };
        return new BoundUnaryExpression(unaryOp.Span, unaryOp.Operator, operand, resultType);
    }

    private BoundCallExpression BindCallExpression(CallExpressionNode callExpr)
    {
        var args = BindExpressions(callExpr.Arguments);
        var receiverSymbol = ResolveCallReceiver(
            callExpr.Target,
            callExpr.ReceiverSpan ?? callExpr.CalleeSpan);
        var receiverTypeSymbol = receiverSymbol == null
            ? ResolveCallReceiverType(callExpr.Target)
            : null;
        var resolution = ResolveCall(
            callExpr.Span,
            callExpr.Target,
            args,
            callExpr.ArgumentNames,
            callExpr.ArgumentModifiers,
            callExpr.TypeArguments);
        var returnType = resolution.ResolvedReturnType ?? "OBJECT";

        var (resolvedTypeName, resolvedMethodName) = GetResolvedCallIdentity(
            callExpr.Target,
            receiverSymbol,
            receiverTypeSymbol);

        return new BoundCallExpression(
            callExpr.Span,
            callExpr.Target,
            args,
            returnType,
            resolvedTypeName,
            resolvedMethodName,
            resolvedParameterTypes: (resolution.Function?.Parameters
                .Select(parameter => parameter.TypeName)
                ?? args.Select(argument => argument.TypeName))
                .ToArray(),
            argumentNames: callExpr.ArgumentNames,
            argumentModifiers: callExpr.ArgumentModifiers,
            typeArguments: callExpr.TypeArguments,
            resolvedSymbol: resolution.Function,
            receiverSymbol: receiverSymbol,
            resolvedSymbols: resolution.Functions,
            calleeSpan: callExpr.CalleeSpan,
            receiverSpan: callExpr.ReceiverSpan,
            isInaccessibleCall: resolution.Kind == OverloadResolutionKind.Inaccessible,
            receiverTypeSymbol: receiverTypeSymbol);
    }

    private (string? TypeName, string? MethodName) GetResolvedCallIdentity(
        string target,
        VariableSymbol? receiverSymbol,
        TypeSymbol? receiverTypeSymbol)
    {
        var lastDot = target.LastIndexOf('.');
        if (lastDot <= 0)
            return (null, null);

        var typePart = target[..lastDot];
        return (
            receiverSymbol != null
                ? Effects.EffectEnforcementPass.MapShortTypeNameToFullName(
                    GetNominalTypeName(receiverSymbol.TypeName))
                : receiverTypeSymbol != null
                    ? receiverTypeSymbol.QualifiedName
                    : !typePart.Contains('.')
                        ? Effects.EffectEnforcementPass.MapShortTypeNameToFullName(typePart)
                        : typePart,
            target[(lastDot + 1)..]);
    }

    private VariableSymbol? ResolveCallReceiver(
        string target,
        Parsing.TextSpan referenceSpan)
    {
        var firstDot = target.IndexOf('.');
        var receiverName = firstDot <= 0 ? target : target[..firstDot];
        var variables = _scope.LookupAll(receiverName)
            .OfType<VariableSymbol>()
            .ToArray();
        if (variables.Length == 0)
            return null;

        if (_isStaticContext
            && variables.Any(variable =>
                variable.DeclaringTypeName != null
                && !variable.IsStatic))
        {
            _diagnostics.ReportError(
                referenceSpan,
                DiagnosticCode.InstanceMemberInStaticContext,
                $"Instance member '{receiverName}' cannot be accessed by a bare reference in a static context");
            return null;
        }

        return variables[0];
    }

    private TypeSymbol? ResolveCallReceiverType(string target)
    {
        var firstDot = target.IndexOf('.');
        if (firstDot <= 0)
            return null;

        var receiverName = target[..firstDot];
        var genericStart = receiverName.IndexOf('<');
        if (genericStart > 0)
            receiverName = receiverName[..genericStart];
        return ResolveTypeSymbol(receiverName);
    }

    private OverloadResolutionResult ResolveCall(
        Parsing.TextSpan span,
        string target,
        IReadOnlyList<BoundExpression> arguments,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers,
        IReadOnlyList<string>? typeArguments)
    {
        var argumentTypes = arguments
            .Select(argument =>
                argument is BoundVariableExpression { Variable.Id.IsNone: true }
                    ? "<unresolved>"
                    : argument.TypeName)
            .ToArray();
        foreach (var lookupName in GetCallLookupNames(target).Distinct(StringComparer.Ordinal))
        {
            var resolution = ResolveAccessibleOverload(
                lookupName,
                argumentTypes,
                argumentNames,
                argumentModifiers,
                typeArguments);
            if (resolution.Kind == OverloadResolutionKind.NotFound)
                continue;

            var hasUnresolvedArguments = argumentTypes.Any(type =>
                string.Equals(type, "<unresolved>", StringComparison.Ordinal));
            if (!hasUnresolvedArguments
                && resolution.Kind == OverloadResolutionKind.NoMatch)
            {
                _diagnostics.ReportError(
                    span,
                    DiagnosticCode.NoMatchingOverload,
                    $"No overload of internal call '{target}' matches " +
                    $"{FormatCallSignature(argumentTypes, argumentModifiers, typeArguments)}. " +
                    $"Candidates: {FormatCandidates(resolution.Candidates)}");
            }
            else if (!hasUnresolvedArguments
                     && resolution.Kind == OverloadResolutionKind.Ambiguous)
            {
                _diagnostics.ReportError(
                    span,
                    DiagnosticCode.AmbiguousOverload,
                    $"Call '{target}{FormatCallSignature(argumentTypes, argumentModifiers, typeArguments)}' " +
                    $"is ambiguous between: {FormatCandidates(resolution.Candidates)}");
            }

            return resolution;
        }

        return OverloadResolutionResult.NotFound();
    }

    private OverloadResolutionResult ResolveAccessibleOverload(
        string lookupName,
        IReadOnlyList<string> argumentTypes,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers,
        IReadOnlyList<string>? typeArguments)
    {
        var candidates = _scope.GetOverloads(lookupName)
            .ToArray();
        if (candidates.Length == 0)
            return OverloadResolutionResult.NotFound();

        var accessibleCandidates = candidates
            .Where(IsFunctionAccessible)
            .ToArray();
        if (accessibleCandidates.Length == 0)
            return OverloadResolutionResult.Inaccessible(candidates);

        var accessibleScope = new Scope();
        foreach (var candidate in accessibleCandidates)
            accessibleScope.TryDeclareOverload(lookupName, candidate, out _);

        return accessibleScope.ResolveOverload(
            lookupName,
            argumentTypes,
            argumentNames,
            argumentModifiers,
            typeArguments,
            GetImplicitConversionCost);
    }

    private int? GetImplicitConversionCost(string parameterType, string argumentType)
    {
        var parameter = TypeIdentity.Canonicalize(parameterType);
        var argument = TypeIdentity.Canonicalize(argumentType);
        if (string.Equals(parameter, argument, StringComparison.Ordinal))
            return 0;
        if (string.Equals(argument, "<unresolved>", StringComparison.Ordinal))
            return null;

        if (parameter == "OBJECT" && argument != "VOID")
            return 50;

        if (IsImplicitNumericConversion(argument, parameter))
            return 10;

        var parameterClass = ResolveClass(parameterType);
        var argumentClass = ResolveClass(argumentType);
        if (parameterClass == null || argumentClass == null)
            return null;

        var distance = 0;
        var current = argumentClass;
        var visited = new HashSet<ClassDefinitionNode>();
        while (visited.Add(current))
        {
            if (ReferenceEquals(current, parameterClass))
                return 20 + distance;

            var baseClass = ResolveBaseClass(current);
            if (baseClass == null)
                break;
            current = baseClass;
            distance++;
        }

        return null;
    }

    private static bool IsImplicitNumericConversion(string from, string to) =>
        (from, to) switch
        {
            ("INT[bits=8][signed=true]", "INT[bits=16][signed=true]"
                or "INT"
                or "LONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT[bits=8][signed=false]", "INT[bits=16][signed=true]"
                or "INT[bits=16][signed=false]"
                or "INT"
                or "UINT"
                or "LONG"
                or "ULONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT[bits=16][signed=true]", "INT"
                or "LONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT[bits=16][signed=false]", "INT"
                or "UINT"
                or "LONG"
                or "ULONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("INT", "LONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("UINT", "LONG"
                or "ULONG"
                or "FLOAT[bits=32]"
                or "FLOAT"
                or "DECIMAL") => true,
            ("LONG", "FLOAT[bits=32]" or "FLOAT" or "DECIMAL") => true,
            ("ULONG", "FLOAT[bits=32]" or "FLOAT" or "DECIMAL") => true,
            ("FLOAT[bits=32]", "FLOAT") => true,
            _ => false,
        };

    private bool IsFunctionAccessible(FunctionSymbol function)
    {
        if (function.ContainingTypeName == null)
            return true;

        return function.Visibility switch
        {
            Visibility.Private =>
                string.Equals(
                    function.ContainingTypeName,
                    _currentClassName,
                    StringComparison.Ordinal),
            Visibility.Protected =>
                _currentClass != null
                && IsSameOrDerivedClass(_currentClass, function.ContainingTypeName),
            _ => true,
        };
    }

    private VariableSymbol? ResolveAccessibleMember(
        ClassDefinitionNode? start,
        string memberName) =>
        ResolveAccessibleMembers(start, memberName).FirstOrDefault() as VariableSymbol;

    private IReadOnlyList<Symbol> ResolveAccessibleMembers(
        ClassDefinitionNode? start,
        string memberName)
    {
        var current = start;
        var visited = new HashSet<ClassDefinitionNode>();
        while (current != null && visited.Add(current))
        {
            var declared = _classScopes[current].LookupAllLocal(memberName);
            if (declared.Count > 0)
            {
                return declared.All(symbol =>
                        symbol is VariableSymbol variable
                        && IsMemberAccessible(variable))
                    ? declared
                    : Array.Empty<Symbol>();
            }

            current = ResolveBaseClass(current);
        }

        return Array.Empty<Symbol>();
    }

    private bool IsMemberAccessible(VariableSymbol member)
    {
        if (member.DeclaringTypeName == null)
            return true;

        return member.Visibility switch
        {
            Visibility.Private =>
                string.Equals(
                    member.DeclaringTypeName,
                    _currentClassName,
                    StringComparison.Ordinal),
            Visibility.Protected =>
                _currentClass != null
                && IsSameOrDerivedClass(_currentClass, member.DeclaringTypeName),
            Visibility.Internal or Visibility.ProtectedInternal => true,
            Visibility.Public => true,
            _ => false,
        };
    }

    private bool IsSameOrDerivedClass(
        ClassDefinitionNode cls,
        string expectedBaseName)
    {
        var current = cls;
        var visited = new HashSet<ClassDefinitionNode>();
        while (visited.Add(current))
        {
            if (string.Equals(
                    _qualifiedClassNames[current],
                    expectedBaseName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            var baseClass = ResolveBaseClass(current);
            if (baseClass == null)
                return false;
            current = baseClass;
        }

        return false;
    }

    private IEnumerable<string> GetCallLookupNames(string target)
    {
        if (target.StartsWith("this.", StringComparison.Ordinal) && _currentClassName != null)
        {
            foreach (var lookupName in EnumerateHierarchyLookupNames(
                         _currentClass,
                         target["this.".Length..]))
            {
                yield return lookupName;
            }
            yield break;
        }

        if (target.StartsWith("base.", StringComparison.Ordinal))
        {
            var baseClass = ResolveBaseClass(_currentClass);
            foreach (var lookupName in EnumerateHierarchyLookupNames(
                         baseClass,
                         target["base.".Length..]))
            {
                yield return lookupName;
            }
            yield break;
        }

        var firstDot = target.IndexOf('.');
        if (firstDot <= 0)
        {
            if (_currentClass != null)
            {
                foreach (var lookupName in EnumerateHierarchyLookupNames(
                             _currentClass,
                             target))
                {
                    yield return lookupName;
                }
            }

            yield return target;
            yield break;
        }

        var receiverName = target[..firstDot];
        if (_scope.Lookup(receiverName) is VariableSymbol receiver)
        {
            var receiverType = GetNominalTypeName(receiver.TypeName);
            var receiverClass = ResolveClass(receiverType);
            if (receiverClass != null)
            {
                foreach (var lookupName in EnumerateHierarchyLookupNames(
                             receiverClass,
                             target[(firstDot + 1)..]))
                {
                    yield return lookupName;
                }
                yield break;
            }
        }

        yield return target;
    }

    private IEnumerable<string> EnumerateHierarchyLookupNames(
        ClassDefinitionNode? start,
        string memberName)
    {
        var current = start;
        var visited = new HashSet<ClassDefinitionNode>();
        while (current != null && visited.Add(current))
        {
            yield return $"{_qualifiedClassNames[current]}.{memberName}";
            current = ResolveBaseClass(current);
        }
    }

    private static string GetNominalTypeName(string typeName)
    {
        var type = typeName.Trim().TrimStart('?');
        var generic = type.IndexOf('<');
        if (generic > 0)
            type = type[..generic];
        var array = type.IndexOf('[');
        if (array > 0)
            type = type[..array];
        return type.TrimEnd('?', '*');
    }

    private TypeSymbol? ResolveTypeSymbol(string typeName)
    {
        var cls = ResolveClass(typeName);
        return cls == null ? null : _classSymbols[cls];
    }

    private ClassDefinitionNode? ResolveBaseClass(ClassDefinitionNode? cls) =>
        cls?.BaseClass is { Length: > 0 } baseClass ? ResolveClass(baseClass) : null;

    private ClassDefinitionNode? ResolveClass(string typeName)
    {
        var nominal = GetNominalTypeName(typeName);
        if (_classesByQualifiedName.TryGetValue(nominal, out var qualified))
            return qualified;

        if (_currentClassName != null)
        {
            var containingSeparator = _currentClassName.LastIndexOf('.');
            if (containingSeparator > 0)
            {
                var nestedCandidate = $"{_currentClassName[..containingSeparator]}.{nominal}";
                if (_classesByQualifiedName.TryGetValue(nestedCandidate, out var nested))
                    return nested;
            }
        }

        return _classesBySimpleName.TryGetValue(nominal, out var matches) && matches.Count == 1
            ? matches[0]
            : null;
    }

    private static string FormatCallSignature(
        IReadOnlyList<string> argumentTypes,
        IReadOnlyList<string?>? argumentModifiers,
        IReadOnlyList<string>? typeArguments)
    {
        var generic = typeArguments == null
            ? string.Empty
            : $"<{string.Join(",", typeArguments.Select(TypeIdentity.Canonicalize))}>";
        var arguments = argumentTypes.Select((type, index) =>
        {
            var modifier = argumentModifiers != null && index < argumentModifiers.Count
                ? argumentModifiers[index]
                : null;
            return $"{(string.IsNullOrWhiteSpace(modifier) ? string.Empty : modifier + " ")}" +
                   TypeIdentity.Canonicalize(type);
        });
        return $"{generic}({string.Join(", ", arguments)})";
    }

    private static string FormatCandidates(IReadOnlyList<FunctionSymbol> candidates) =>
        string.Join(
            ", ",
            candidates.Select(candidate =>
                $"{candidate.DisplaySignature} [{candidate.Id}]"));

    private BoundExpression BindConditionalExpression(ConditionalExpressionNode condExpr)
    {
        var condition = BindExpression(condExpr.Condition);
        var whenTrue = BindExpression(condExpr.WhenTrue);
        var whenFalse = BindExpression(condExpr.WhenFalse);

        return new BoundConditionalExpression(
            condExpr.Span,
            condition,
            whenTrue,
            whenFalse,
            GetCommonType(whenTrue.TypeName, whenFalse.TypeName));
    }

    private static BoundStructuralExpression Structural(
        ExpressionNode expression,
        string typeName,
        IReadOnlyList<BoundExpression>? children = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyList<BoundExpression>? deferredChildren = null)
    {
        return new BoundStructuralExpression(
            expression.Span,
            expression.GetType().Name,
            typeName,
            children,
            metadata,
            deferredChildren);
    }

    private BoundUnsupportedExpression BindUnsupportedExpression(
        ExpressionNode expression,
        string typeName = "OBJECT",
        IReadOnlyList<BoundExpression>? children = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        string reason = "Analysis support is incomplete")
    {
        children ??= BindReflectedExpressionChildren(expression);
        ReportAnalysisIncomplete(expression, reason);
        return new BoundUnsupportedExpression(
            expression.Span,
            expression.GetType().Name,
            typeName,
            children,
            metadata,
            reason);
    }

    private IReadOnlyList<BoundExpression> BindReflectedExpressionChildren(ExpressionNode expression)
    {
        var children = new List<BoundExpression>();
        foreach (var property in expression.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length != 0 || property.Name == nameof(AstNode.Span))
                continue;

            var value = property.GetValue(expression);
            if (value is ExpressionNode child)
            {
                children.Add(BindExpression(child));
            }
            else if (value is IEnumerable<ExpressionNode> expressionChildren)
            {
                children.AddRange(BindExpressions(expressionChildren));
            }
        }
        return children;
    }

    private void ReportAnalysisIncomplete(ExpressionNode expression, string detail)
    {
        var typeName = expression.GetType().Name;
        if (_unsupportedNodeTypes.Add(typeName))
        {
            _diagnostics.ReportInfo(
                expression.Span,
                DiagnosticCode.AnalysisUnsupportedNode,
                $"Expression type '{typeName}' has incomplete analysis support; {detail}");
        }
    }

    private IReadOnlyList<BoundExpression> BindExpressions(IEnumerable<ExpressionNode> expressions) =>
        expressions.Select(BindExpression).ToArray();

    private static string MakeArrayType(string elementType, int rank) =>
        $"{elementType}[{new string(',', Math.Max(0, rank - 1))}]";

    private static string MakeOptionType(string innerType) =>
        $"OPTION[inner={innerType}]";

    private static string MakeResultType(string okType, string errorType) =>
        $"RESULT[ok={okType}][err={errorType}]";

    private static string UnwrapAwaitedType(string typeName)
    {
        foreach (var prefix in new[] { "Task<", "ValueTask<", "TASK<", "VALUETASK<" })
        {
            if (typeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && typeName.EndsWith('>'))
                return typeName[prefix.Length..^1];
        }

        return typeName.Equals("Task", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("ValueTask", StringComparison.OrdinalIgnoreCase)
            ? "VOID"
            : "OBJECT";
    }

    private static string UnwrapOptionOrNullable(string typeName)
    {
        return TypeIdentity.TryUnwrapOptionOrNullable(typeName, out var elementType)
            ? elementType
            : typeName;
    }

    private static string GetIndexedElementType(string typeName)
    {
        const string arrayPrefix = "ARRAY[element=";
        if (typeName.StartsWith(arrayPrefix, StringComparison.OrdinalIgnoreCase)
            && typeName.EndsWith(']'))
            return typeName[arrayPrefix.Length..^1];

        if (typeName.StartsWith('[') && typeName.EndsWith(']') && typeName.Length > 2)
            return typeName[1..^1];

        var bracket = typeName.LastIndexOf('[');
        if (bracket > 0 && typeName.EndsWith(']'))
            return typeName[..bracket];

        var genericStart = typeName.IndexOf('<');
        if (genericStart > 0 && typeName.EndsWith('>'))
        {
            var genericName = typeName[..genericStart];
            var arguments = SplitTopLevelTypeArguments(typeName[(genericStart + 1)..^1]);
            if (genericName.Contains("Dictionary", StringComparison.OrdinalIgnoreCase)
                || genericName.Equals("Dict", StringComparison.OrdinalIgnoreCase))
                return arguments.Count > 1 ? arguments[1] : "OBJECT";
            return arguments.Count > 0 ? arguments[0] : "OBJECT";
        }

        return "OBJECT";
    }

    private static IReadOnlyList<string> SplitTopLevelTypeArguments(string text)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<':
                case '[':
                    depth++;
                    break;
                case '>':
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(text[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }
        result.Add(text[start..].Trim());
        return result;
    }

    private static string GetCommonType(string leftType, string rightType)
    {
        var left = NormalizeTypeName(leftType);
        var right = NormalizeTypeName(rightType);

        if (left == right)
            return left;
        if (left == "NEVER")
            return right;
        if (right == "NEVER")
            return left;
        if (left == "OBJECT" || right == "OBJECT")
            return "OBJECT";

        var leftRank = GetNumericRank(left);
        var rightRank = GetNumericRank(right);
        if (leftRank >= 0 && rightRank >= 0)
        {
            if ((left == "DECIMAL" && right.StartsWith("FLOAT", StringComparison.Ordinal))
                || (right == "DECIMAL" && left.StartsWith("FLOAT", StringComparison.Ordinal)))
                return "OBJECT";
            return leftRank >= rightRank ? left : right;
        }

        return "OBJECT";
    }

    private static string? GetCommonType(IEnumerable<string>? types)
    {
        if (types == null)
            return null;

        using var enumerator = types.GetEnumerator();
        if (!enumerator.MoveNext())
            return null;

        var result = enumerator.Current;
        while (enumerator.MoveNext())
            result = GetCommonType(result, enumerator.Current);
        return NormalizeTypeName(result);
    }

    private static int GetNumericRank(string typeName) => typeName switch
    {
        "INT[bits=8][signed=true]" or "INT[bits=8][signed=false]" => 0,
        "INT[bits=16][signed=true]" or "INT[bits=16][signed=false]" => 1,
        "INT" or "UINT" => 2,
        "LONG" or "ULONG" or "INT[bits=64][signed=true]" or "INT[bits=64][signed=false]" => 3,
        "FLOAT[bits=32]" => 4,
        "FLOAT" => 5,
        "DECIMAL" => 6,
        _ => -1,
    };

    private static string NormalizeTypeName(string typeName)
    {
        var trimmed = typeName.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "i8" or "sbyte" => "INT[bits=8][signed=true]",
            "int[bits=8][signed=true]" => "INT[bits=8][signed=true]",
            "u8" or "byte" => "INT[bits=8][signed=false]",
            "int[bits=8][signed=false]" => "INT[bits=8][signed=false]",
            "i16" or "short" => "INT[bits=16][signed=true]",
            "int[bits=16][signed=true]" => "INT[bits=16][signed=true]",
            "u16" or "ushort" => "INT[bits=16][signed=false]",
            "int[bits=16][signed=false]" => "INT[bits=16][signed=false]",
            "i32" or "int" or "int32" => "INT",
            "int[bits=32][signed=true]" => "INT",
            "u32" or "uint" or "uint32" => "UINT",
            "int[bits=32][signed=false]" => "UINT",
            "i64" or "long" or "int64" => "LONG",
            "int[bits=64][signed=true]" => "LONG",
            "u64" or "ulong" or "uint64" => "ULONG",
            "int[bits=64][signed=false]" => "ULONG",
            "f32" or "single" => "FLOAT[bits=32]",
            "float[bits=32]" => "FLOAT[bits=32]",
            "f64" or "float" or "double" => "FLOAT",
            "dec" or "decimal" => "DECIMAL",
            "str" or "string" => "STRING",
            "bool" or "boolean" => "BOOL",
            "any" or "object" or "unknown" => "OBJECT",
            "never" => "NEVER",
            _ => trimmed,
        };
    }

    private static IEnumerable<string> CollectReturnTypes(IReadOnlyList<BoundStatement>? statements)
    {
        if (statements == null)
            yield break;

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BoundReturnStatement { Expression: not null } returnStatement:
                    yield return returnStatement.Expression.TypeName;
                    break;
                case BoundIfStatement ifStatement:
                    foreach (var type in CollectReturnTypes(ifStatement.ThenBody))
                        yield return type;
                    foreach (var clause in ifStatement.ElseIfClauses)
                        foreach (var type in CollectReturnTypes(clause.Body))
                            yield return type;
                    foreach (var type in CollectReturnTypes(ifStatement.ElseBody))
                        yield return type;
                    break;
                case BoundWhileStatement whileStatement:
                    foreach (var type in CollectReturnTypes(whileStatement.Body))
                        yield return type;
                    break;
                case BoundForStatement forStatement:
                    foreach (var type in CollectReturnTypes(forStatement.Body))
                        yield return type;
                    break;
                case BoundForeachStatement foreachStatement:
                    foreach (var type in CollectReturnTypes(foreachStatement.Body))
                        yield return type;
                    break;
                case BoundDoWhileStatement doWhileStatement:
                    foreach (var type in CollectReturnTypes(doWhileStatement.Body))
                        yield return type;
                    break;
                case BoundUsingStatement usingStatement:
                    foreach (var type in CollectReturnTypes(usingStatement.Body))
                        yield return type;
                    break;
                case BoundTryStatement tryStatement:
                    foreach (var type in CollectReturnTypes(tryStatement.TryBody))
                        yield return type;
                    foreach (var clause in tryStatement.CatchClauses)
                        foreach (var type in CollectReturnTypes(clause.Body))
                            yield return type;
                    foreach (var type in CollectReturnTypes(tryStatement.FinallyBody))
                        yield return type;
                    break;
            }
        }
    }

    /// <summary>
    /// Generates a unique name by appending a number suffix.
    /// </summary>
    private string GenerateUniqueName(string baseName)
    {
        var suffix = 2;
        var candidate = $"{baseName}{suffix}";
        while (_scope.Lookup(candidate) != null)
        {
            suffix++;
            candidate = $"{baseName}{suffix}";
        }
        return candidate;
    }

    private BoundTryStatement BindTryStatement(TryStatementNode tryStmt)
    {
        // Bind try body in its own scope
        IReadOnlyList<BoundStatement> tryBody;
        {
            using var _ = PushScope(_scope.CreateChild());
            tryBody = BindStatements(tryStmt.TryBody);
        }

        // Bind catch clauses
        var catchClauses = new List<BoundCatchClause>();
        foreach (var catchClause in tryStmt.CatchClauses)
        {
            using var _ = PushScope(_scope.CreateChild());

            VariableSymbol? exceptionVar = null;
            if (catchClause.VariableName != null)
            {
                var typeName = catchClause.ExceptionType ?? "Exception";
                exceptionVar = CreateLocalVariable(
                    catchClause.VariableName,
                    typeName,
                    isMutable: false,
                    isParameter: false,
                    ParameterModifier.None,
                    catchClause.VariableSpan ?? catchClause.Span,
                    "catch");
                _scope.TryDeclare(exceptionVar);
            }

            var catchBody = BindStatements(catchClause.Body);

            catchClauses.Add(new BoundCatchClause(
                catchClause.Span,
                catchClause.ExceptionType,
                exceptionVar,
                catchBody));
        }

        // Bind finally body if present
        IReadOnlyList<BoundStatement>? finallyBody = null;
        if (tryStmt.FinallyBody != null && tryStmt.FinallyBody.Count > 0)
        {
            using var _ = PushScope(_scope.CreateChild());
            finallyBody = BindStatements(tryStmt.FinallyBody);
        }

        return new BoundTryStatement(tryStmt.Span, tryBody, catchClauses, finallyBody);
    }

    private BoundMatchStatement BindMatchStatement(MatchStatementNode matchStmt)
    {
        var target = BindExpression(matchStmt.Target);
        return new BoundMatchStatement(matchStmt.Span, target, BindMatchCases(matchStmt.Cases));
    }

    // ===== New statement binders for class member bodies =====

    private readonly HashSet<string> _unsupportedNodeTypes = new();

    private BoundAssignmentStatement BindAssignmentStatement(AssignmentStatementNode assign)
    {
        var target = BindExpression(assign.Target);
        var value = BindExpression(assign.Value);
        return new BoundAssignmentStatement(assign.Span, target, value);
    }

    private BoundCompoundAssignment BindCompoundAssignment(CompoundAssignmentStatementNode compound)
    {
        var target = BindExpression(compound.Target);
        var value = BindExpression(compound.Value);
        return new BoundCompoundAssignment(compound.Span, target, compound.Operator, value);
    }

    private BoundForeachStatement BindForeachStatement(ForeachStatementNode forEach)
    {
        using var _ = PushScope(_scope.CreateChild());

        var collection = BindExpression(forEach.Collection);
        var variableType = string.IsNullOrWhiteSpace(forEach.VariableType)
                           || forEach.VariableType.Equals("var", StringComparison.OrdinalIgnoreCase)
            ? GetIndexedElementType(collection.TypeName)
            : forEach.VariableType;
        var loopVar = CreateLocalVariable(
            forEach.VariableName,
            variableType,
            isMutable: false,
            isParameter: false,
            ParameterModifier.None,
            forEach.VariableSpan,
            "foreach");
        _scope.TryDeclare(loopVar);

        if (forEach.IndexVariableName != null)
        {
            var indexVar = CreateLocalVariable(
                forEach.IndexVariableName,
                "INT",
                isMutable: true,
                isParameter: false,
                ParameterModifier.None,
                forEach.IndexVariableSpan ?? forEach.Span,
                "foreach-index");
            _scope.TryDeclare(indexVar);
        }

        var body = BindStatements(forEach.Body);
        return new BoundForeachStatement(forEach.Span, loopVar, collection, body);
    }

    private BoundUsingStatement BindUsingStatement(UsingStatementNode usingStmt)
    {
        using var _ = PushScope(_scope.CreateChild());

        VariableSymbol? resource = null;
        if (usingStmt.VariableName != null)
        {
            var typeName = usingStmt.VariableType ?? "IDisposable";
            resource = CreateLocalVariable(
                usingStmt.VariableName,
                typeName,
                isMutable: false,
                isParameter: false,
                ParameterModifier.None,
                usingStmt.VariableSpan ?? usingStmt.Span,
                "using");
            _scope.TryDeclare(resource);
        }

        var resourceExpr = BindExpression(usingStmt.Resource);
        var body = BindStatements(usingStmt.Body);
        return new BoundUsingStatement(usingStmt.Span, resource, resourceExpr, body);
    }

    private BoundDoWhileStatement BindDoWhileStatement(DoWhileStatementNode doWhile)
    {
        using var _ = PushScope(_scope.CreateChild());

        var body = BindStatements(doWhile.Body);
        var condition = BindExpression(doWhile.Condition);
        return new BoundDoWhileStatement(doWhile.Span, condition, body);
    }

    private BoundStatement BindSyncBlock(SyncBlockNode sync)
    {
        // Model lock as a using-like scope block — lock semantics (mutual exclusion)
        // are out of scope for dataflow, but the body must be preserved for analysis
        var lockExpr = BindExpression(sync.LockExpression);
        var body = BindStatements(sync.Body);
        return new BoundUsingStatement(sync.Span, null, lockExpr, body);
    }

    private BoundStatement BindUnsupportedStatement(StatementNode stmt)
    {
        var typeName = stmt.GetType().Name;
        if (_unsupportedNodeTypes.Add(typeName))
        {
            _diagnostics.ReportInfo(stmt.Span, DiagnosticCode.AnalysisUnsupportedNode,
                $"Statement type '{typeName}' is not fully supported in analysis; treated as opaque");
        }
        return new BoundUnsupportedStatement(stmt.Span, typeName);
    }

    // ===== Class member binding =====

    private void BindClassMembers(ClassDefinitionNode cls, List<BoundFunction> functions)
    {
        var className = _qualifiedClassNames[cls];
        var classScope = _classScopes[cls];

        using var _ = PushScope(classScope);
        var previousClassName = _currentClassName;
        var previousClass = _currentClass;
        var previousClassScope = _currentClassScope;
        var previousClassIdentity = _currentClassIdentity;
        _currentClassName = className;
        _currentClass = cls;
        _currentClassScope = classScope;
        _currentClassIdentity = _classSymbolIds[cls];

        try
        {
            // Methods
            foreach (var method in EnumerateMethods(cls))
            {
                if (method.IsAbstract || method.IsExtern || method.Body.Count == 0)
                    continue;
                var bound = TryBindMember(() => BindMethod(method, className), method.Span, className, method.Name);
                if (bound != null) functions.Add(bound);
            }

            // Constructors
            foreach (var ctor in EnumerateConstructors(cls))
            {
                if (ctor.Body.Count == 0 && ctor.Initializer == null)
                    continue;
                var memberName = ctor.IsStatic ? ".cctor" : ".ctor";
                var bound = TryBindMember(
                    () => BindConstructor(ctor, className),
                    ctor.Span,
                    className,
                    memberName);
                if (bound != null) functions.Add(bound);
            }

            // Property accessors
            var properties = EnumerateProperties(cls).ToArray();
            for (var propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
            {
                var prop = properties[propertyIndex];
                if (prop.Getter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(
                            prop.Getter,
                            className,
                            prop.Name,
                            prop.TypeName,
                            prop.Id,
                            prop.IdentifierSpan,
                            prop.IsStatic),
                        prop.Getter.Span, className, $"{prop.Name}.get");
                    if (bound != null) functions.Add(bound);
                }
                if (prop.Setter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(
                            prop.Setter,
                            className,
                            prop.Name,
                            prop.TypeName,
                            prop.Id,
                            prop.IdentifierSpan,
                            prop.IsStatic),
                        prop.Setter.Span, className, $"{prop.Name}.set");
                    if (bound != null) functions.Add(bound);
                }
                if (prop.Initer is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(
                            prop.Initer,
                            className,
                            prop.Name,
                            prop.TypeName,
                            prop.Id,
                            prop.IdentifierSpan,
                            prop.IsStatic),
                        prop.Initer.Span, className, $"{prop.Name}.init");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Operator overloads
            var operators = EnumerateOperators(cls).ToArray();
            for (var operatorIndex = 0; operatorIndex < operators.Length; operatorIndex++)
            {
                var op = operators[operatorIndex];
                if (op.Body.Count == 0) continue;
                var bound = TryBindMember(
                    () => BindOperator(op, className, operatorIndex),
                    op.Span,
                    className,
                    $"op_{op.Kind}");
                if (bound != null) functions.Add(bound);
            }

            // Indexer accessors
            var indexers = EnumerateIndexers(cls).ToArray();
            for (var indexerIndex = 0; indexerIndex < indexers.Length; indexerIndex++)
            {
                var ixer = indexers[indexerIndex];
                if (ixer.Getter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindIndexerAccessor(
                            ixer.Getter,
                            ixer.Parameters,
                            className,
                            ixer.TypeName,
                            ixer.Id,
                            indexerIndex),
                        ixer.Getter.Span, className, "this[].get");
                    if (bound != null) functions.Add(bound);
                }
                if (ixer.Setter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindIndexerAccessor(
                            ixer.Setter,
                            ixer.Parameters,
                            className,
                            ixer.TypeName,
                            ixer.Id,
                            indexerIndex),
                        ixer.Setter.Span, className, "this[].set");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Event accessors
            var events = EnumerateEvents(cls).ToArray();
            for (var eventIndex = 0; eventIndex < events.Length; eventIndex++)
            {
                var evt = events[eventIndex];
                if (evt.AddBody != null && evt.AddBody.Count > 0)
                {
                    var bound = TryBindMember(
                        () => BindEventAccessor(
                            evt.AddBody,
                            className,
                            evt.Name,
                            "add",
                            evt.DelegateType,
                            evt.Id,
                            eventIndex,
                            evt.Span),
                        evt.Span, className, $"{evt.Name}.add");
                    if (bound != null) functions.Add(bound);
                }
                if (evt.RemoveBody != null && evt.RemoveBody.Count > 0)
                {
                    var bound = TryBindMember(
                        () => BindEventAccessor(
                            evt.RemoveBody,
                            className,
                            evt.Name,
                            "remove",
                            evt.DelegateType,
                            evt.Id,
                            eventIndex,
                            evt.Span),
                        evt.Span, className, $"{evt.Name}.remove");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Recurse into nested classes — isolate scope so nested classes
            // don't inherit outer class fields (C# semantics: nested classes
            // need explicit reference to access outer instance members)
            foreach (var nested in cls.NestedClasses)
                BindClassMembers(nested, functions);
        }
        finally
        {
            _currentClassName = previousClassName;
            _currentClass = previousClass;
            _currentClassScope = previousClassScope;
            _currentClassIdentity = previousClassIdentity;
        }
    }

    private BoundFunction? TryBindMember(Func<BoundFunction> bind, Parsing.TextSpan span, string className, string memberName)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = bind();
            sw.Stop();

            // Warn if binding took unusually long (not a hard timeout since binding is synchronous)
            if (sw.ElapsedMilliseconds > 5000)
            {
                _diagnostics.ReportWarning(span, DiagnosticCode.AnalysisSkipped,
                    $"Analysis of '{className}.{memberName}' took {sw.ElapsedMilliseconds}ms (slow binding)");
            }

            return result;
        }
        catch (NotSupportedException ex)
        {
            _diagnostics.ReportWarning(span, DiagnosticCode.AnalysisSkipped,
                $"Skipped analysis of '{className}.{memberName}': {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _diagnostics.ReportError(span, DiagnosticCode.AnalysisICE,
                $"Internal error analyzing '{className}.{memberName}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private BoundFunction BindMethod(MethodNode method, string className)
    {
        var functionScope = _scope.CreateChild();
        using var _s = PushScope(functionScope);
        using var _c = PushStaticContext(method.IsStatic);
        var functionSymbol = _functionSymbols[method];
        using var _identity = PushDeclarationContext(functionSymbol.Id);

        DeclareParameters(functionSymbol.Parameters, method.Parameters);
        var boundBody = BindStatements(method.Body);
        var declaredEffects = ExtractMethodEffects(method.Effects);
        return new BoundFunction(method.Span, functionSymbol, boundBody, functionScope,
            declaredEffects, BoundMemberKind.Method, className);
    }

    private BoundFunction BindConstructor(ConstructorNode ctor, string className)
    {
        var functionScope = _scope.CreateChild();
        using var _s = PushScope(functionScope);
        using var _c = PushStaticContext(ctor.IsStatic);
        var functionSymbol = _functionSymbols[ctor];
        using var _identity = PushDeclarationContext(functionSymbol.Id);

        DeclareParameters(functionSymbol.Parameters, ctor.Parameters);

        // Bind constructor initializer (: base(...) / : this(...)) as a call prepended to body.
        // This makes initializer arguments visible to bug pattern checkers (e.g., div-by-zero in base(x / y)).
        // Note: fields set by the chained constructor are NOT tracked (requires interprocedural analysis).
        var boundBody = new List<BoundStatement>();
        if (ctor.Initializer != null)
        {
            var initArgs = BindExpressions(ctor.Initializer.Arguments);
            var baseClass = ResolveBaseClass(_currentClass);
            var initTarget = ctor.Initializer.IsBaseCall
                ? baseClass == null
                    ? "base..ctor"
                    : $"{_qualifiedClassNames[baseClass]}..ctor"
                : $"{className}..ctor";
            var resolution = ResolveCall(
                ctor.Initializer.Span,
                initTarget,
                initArgs,
                argumentNames: null,
                argumentModifiers: null,
                typeArguments: null);
            boundBody.Add(new BoundCallStatement(
                ctor.Initializer.Span,
                initTarget,
                initArgs,
                resolution.Function));
        }
        boundBody.AddRange(BindStatements(ctor.Body));

        return new BoundFunction(ctor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(),
            ctor.IsStatic ? BoundMemberKind.StaticConstructor : BoundMemberKind.Constructor,
            className);
    }

    private BoundFunction BindPropertyAccessor(
        PropertyAccessorNode accessor,
        string className,
        string propName,
        string propType,
        string propertyId,
        Parsing.TextSpan propertyIdentifierSpan,
        bool isStatic)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);
        using var _staticGuard = PushStaticContext(isStatic);
        var propertyIdentity = _currentClassIdentity.Append("property", $"ast:{propertyId}");
        var functionId = propertyIdentity.Append($"accessor:{accessor.Kind}");
        using var _identity = PushDeclarationContext(functionId);

        var parameters = new List<VariableSymbol>();
        var memberKind = BoundMemberKind.PropertyGetter;

        if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
            or PropertyAccessorNode.AccessorKind.Init)
        {
            var valueParam = CreateLocalVariable(
                "value",
                propType,
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                Parsing.TextSpan.Empty,
                "parameter");
            _scope.TryDeclare(valueParam);
            parameters.Add(valueParam);
            memberKind = accessor.Kind == PropertyAccessorNode.AccessorKind.Set
                ? BoundMemberKind.PropertySetter : BoundMemberKind.PropertyInit;
        }

        var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? propType : "VOID";
        var qualifiedName = $"{className}.{propName}.{accessor.Kind.ToString().ToLowerInvariant()}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            returnType,
            parameters,
            declarationSpan: propertyIdentifierSpan,
            visibility: accessor.Visibility ?? Visibility.Public,
            containingTypeName: className,
            definitionSpan: accessor.Span);
        TrackSymbol(functionSymbol);
        var boundBody = BindStatements(accessor.Body);
        return new BoundFunction(accessor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className);
    }

    private BoundFunction BindOperator(
        OperatorOverloadNode op,
        string className,
        int operatorIndex)
    {
        var functionScope = _scope.CreateChild();
        using var _s = PushScope(functionScope);
        using var _c = PushStaticContext(true); // operators are always static in C#
        var functionId = CreateDeclarationId(
            _currentClassIdentity,
            "operator",
            op.Id,
            $"op_{op.Kind}");
        using var _identity = PushDeclarationContext(functionId);

        var parameters = BindParameters(op.Parameters);
        var returnType = op.Output?.TypeName ?? "VOID";
        var qualifiedName = $"{className}.op_{op.Kind}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            returnType,
            parameters,
            declarationSpan: op.Span,
            visibility: op.Visibility,
            containingTypeName: className,
            definitionSpan: op.Span);
        TrackSymbol(functionSymbol);
        var boundBody = BindStatements(op.Body);
        // OperatorOverloadNode has no Effects field — mark as unknown
        var declaredEffects = new List<string> { "*:*" };
        return new BoundFunction(op.Span, functionSymbol, boundBody, functionScope,
            declaredEffects, BoundMemberKind.OperatorOverload, className);
    }

    private BoundFunction BindIndexerAccessor(
        PropertyAccessorNode accessor, IReadOnlyList<ParameterNode> indexerParams,
        string className,
        string indexerType,
        string indexerId,
        int indexerIndex)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);
        var indexerIdentity = _currentClassIdentity.Append("indexer", $"ast:{indexerId}");
        var functionId = indexerIdentity.Append($"accessor:{accessor.Kind}");
        using var _identity = PushDeclarationContext(functionId);

        var parameters = BindParameters(indexerParams);
        var memberKind = BoundMemberKind.IndexerGetter;

        if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
            or PropertyAccessorNode.AccessorKind.Init)
        {
            var valueParam = CreateLocalVariable(
                "value",
                indexerType,
                isMutable: false,
                isParameter: true,
                ParameterModifier.None,
                Parsing.TextSpan.Empty,
                "parameter");
            _scope.TryDeclare(valueParam);
            parameters.Add(valueParam);
            memberKind = BoundMemberKind.IndexerSetter;
        }

        var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? indexerType : "VOID";
        var qualifiedName = $"{className}.this[].{accessor.Kind.ToString().ToLowerInvariant()}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            returnType,
            parameters,
            declarationSpan: accessor.Span,
            visibility: accessor.Visibility ?? Visibility.Public,
            containingTypeName: className,
            definitionSpan: accessor.Span);
        TrackSymbol(functionSymbol);
        var boundBody = BindStatements(accessor.Body);
        return new BoundFunction(accessor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className);
    }

    private BoundFunction BindEventAccessor(
        IReadOnlyList<StatementNode> body, string className, string eventName,
        string accessorKind,
        string delegateType,
        string eventId,
        int eventIndex,
        Parsing.TextSpan span)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);
        var eventIdentity = _currentClassIdentity.Append("event", $"ast:{eventId}");
        var functionId = eventIdentity.Append($"accessor:{accessorKind}");
        using var _identity = PushDeclarationContext(functionId);

        var valueParam = CreateLocalVariable(
            "value",
            delegateType,
            isMutable: false,
            isParameter: true,
            ParameterModifier.None,
            Parsing.TextSpan.Empty,
            "parameter");
        _scope.TryDeclare(valueParam);
        var parameters = new List<VariableSymbol> { valueParam };

        var memberKind = accessorKind == "add" ? BoundMemberKind.EventAdd : BoundMemberKind.EventRemove;
        var qualifiedName = $"{className}.{eventName}.{accessorKind}";
        var functionSymbol = new FunctionSymbol(
            functionId,
            qualifiedName,
            "VOID",
            parameters,
            declarationSpan: span,
            visibility: Visibility.Public,
            containingTypeName: className,
            definitionSpan: span);
        TrackSymbol(functionSymbol);
        var boundBody = BindStatements(body);
        return new BoundFunction(span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className);
    }

    // ===== Effect extraction =====

    /// <summary>
    /// Extracts effect declarations from a function node.
    /// </summary>
    private static IReadOnlyList<string> ExtractEffects(FunctionNode func)
        => ExtractMethodEffects(func.Effects);

    /// <summary>
    /// Extracts effect declarations from an EffectsNode.
    /// Returns effects in "category:value" format (e.g., "io:database_write").
    /// </summary>
    private static IReadOnlyList<string> ExtractMethodEffects(EffectsNode? effectsNode)
    {
        if (effectsNode?.Effects == null || effectsNode.Effects.Count == 0)
            return Array.Empty<string>();

        var effects = new List<string>();
        foreach (var (category, value) in effectsNode.Effects)
        {
            // Store as "category:value" - TaintAnalysis will parse this
            effects.Add($"{category.ToLowerInvariant()}:{value.ToLowerInvariant()}");
        }
        return effects;
    }
}
