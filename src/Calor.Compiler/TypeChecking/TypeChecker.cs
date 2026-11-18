using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.TypeChecking;

/// <summary>
/// Performs type checking and inference on the AST.
/// </summary>
public sealed class TypeChecker
{
    private readonly DiagnosticBag _diagnostics;
    private readonly TypeEnvironment _env;
    private readonly IReadOnlyDictionary<AwaitExpressionNode, Binding.BoundStructuralExpression>? _boundAwaits;
    private readonly IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? _boundSourceEnumConstants;
    private readonly IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? _boundSourceStaticFieldReads;
    private readonly IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? _boundMetadataConstants;
    private readonly IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? _boundMetadataStaticFieldReads;
    private readonly IReadOnlySet<TextSpan>? _boundGenericReceivingFailures;
    private ModuleNode? _module;
    private Analysis.CallGraphAnalysis.OperatorBindings? _sourceBindings;
    internal ContractMode ProjectionContractMode { get; init; } = ContractMode.Debug;
    private readonly Dictionary<IsPatternNode, CalorType> _patternBindingTypes = new();
    private readonly Dictionary<MatchStatementNode, CalorType> _matchTargetTypes = new();
    private readonly HashSet<string> _moduleDeclaredValueTypes = new(StringComparer.OrdinalIgnoreCase);
    private CalorType? _currentReturnType;
    private bool _validateReturnAssignments;
    private bool _suppressContextualDiagnostics;
    private bool _lambdaReturnInvalid;
    private List<CalorType>? _inferredLambdaReturnTypes;
    private int _lambdaBodyDepth;
    private readonly Stack<List<Diagnostic>> _flowCacheDiagnostics = new();

    private readonly Dictionary<AstNode, string> _inferredRefinements =
        new(ReferenceEqualityComparer.Instance);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        ModuleNode, IReadOnlyDictionary<AstNode, string>> InferredRefinementCache = new();

    /// <summary>Nesting depth of speculative inference (overload trials, contextual probes).</summary>
    private int _speculationDepth;

    private readonly record struct SpeculationCheckpoint(
        (int Diagnostics, int Fixes) Diagnostics,
        Dictionary<AstNode, string> Refinements);

    /// <summary>
    /// Starts a speculative inference whose diagnostics AND inferred refinements are rolled back
    /// by <see cref="EndSpeculation"/>. A rejected overload's contextual lambda typing must not
    /// leave its refinement on a lambda parameter for the code generator (#1529 review round 2).
    /// </summary>
    private SpeculationCheckpoint BeginSpeculation()
    {
        _speculationDepth++;
        return new SpeculationCheckpoint(
            _diagnostics.CreateCheckpoint(),
            new Dictionary<AstNode, string>(_inferredRefinements, ReferenceEqualityComparer.Instance));
    }

    private void EndSpeculation(SpeculationCheckpoint checkpoint)
    {
        _diagnostics.RestoreCheckpoint(checkpoint.Diagnostics);
        _inferredRefinements.Clear();
        foreach (var (node, refinement) in checkpoint.Refinements)
            _inferredRefinements[node] = refinement;
        _speculationDepth--;
    }

    public TypeChecker(DiagnosticBag diagnostics)
        : this(diagnostics, null)
    {
    }

    internal TypeChecker(DiagnosticBag diagnostics,
        IReadOnlyDictionary<AwaitExpressionNode, Binding.BoundStructuralExpression>? boundAwaits,
        IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? boundSourceEnumConstants = null,
        IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? boundSourceStaticFieldReads = null,
        IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? boundMetadataConstants = null)
        : this(diagnostics, boundAwaits, boundSourceEnumConstants,
            boundSourceStaticFieldReads, boundMetadataConstants, null)
    {
    }

    internal TypeChecker(DiagnosticBag diagnostics,
        IReadOnlyDictionary<AwaitExpressionNode, Binding.BoundStructuralExpression>? boundAwaits,
        IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? boundSourceEnumConstants,
        IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? boundSourceStaticFieldReads,
        IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? boundMetadataConstants,
        IReadOnlySet<TextSpan>? boundGenericReceivingFailures,
        IReadOnlyDictionary<ExpressionNode, Binding.BoundFieldAccessExpression>? boundMetadataStaticFieldReads = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _env = new TypeEnvironment();
        _boundAwaits = boundAwaits;
        _boundSourceEnumConstants = boundSourceEnumConstants;
        _boundSourceStaticFieldReads = boundSourceStaticFieldReads;
        _boundMetadataConstants = boundMetadataConstants;
        _boundMetadataStaticFieldReads = boundMetadataStaticFieldReads;
        _boundGenericReceivingFailures = boundGenericReceivingFailures;
    }

    /// <summary>
    /// Declarations without a type annotation whose inferred type is a named refinement, mapped
    /// to that refinement's <c>§RTYPE</c> name: unannotated binds (<c>§B{~x} init</c>,
    /// a <see cref="BindStatementNode"/>) and contextually typed lambda parameters (a
    /// <see cref="LambdaParameterNode"/>). Filled by <see cref="Check"/>.
    /// </summary>
    public IReadOnlyDictionary<AstNode, string> InferredRefinements => _inferredRefinements;

    /// <summary>
    /// #1529: a local inferred from a refined value carries the refinement. The type checker is
    /// the source of that inferred type, but the emitter and the obligation generator run whether
    /// or not the checker's diagnostics are enabled (<c>--no-type-check</c>). So both ask here: the
    /// module is checked once with a throwaway diagnostic bag and the result is cached per module
    /// instance. A module without <c>§RTYPE</c> declarations has no named refinements, so it is
    /// not checked at all.
    /// </summary>
    public static IReadOnlyDictionary<AstNode, string> InferUnannotatedRefinements(ModuleNode module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.RefinementTypes.Count == 0)
            return new Dictionary<AstNode, string>(ReferenceEqualityComparer.Instance);
        return InferredRefinementCache.GetValue(module, static m =>
        {
            var checker = new TypeChecker(new DiagnosticBag());
            checker.Check(m);
            return new Dictionary<AstNode, string>(
                checker._inferredRefinements, ReferenceEqualityComparer.Instance);
        });
    }

    public void Check(ModuleNode module)
    {
        _module = module;
        _sourceBindings = null;
        Verification.ContractVerifier.VerifyNamedRefinementPredicates(module, _diagnostics);
        _hasNamedRefinements = module.RefinementTypes.Count > 0;
        _patternBindingTypes.Clear();
        _moduleDeclaredValueTypes.Clear();
        foreach (var enumType in DeclaredEnumTypeNames(module.Classes, module.Enums))
            _moduleDeclaredValueTypes.Add(enumType);
        // Pass -1: the module's OWN type declarations, before anything resolves a type name —
        // including a §RTYPE base type. Without this the checker treats a class the user declared
        // eight lines above as an unknown external type and warns that it "may be a typo": a false
        // positive on a program that compiles and runs, which the shipped corpus does not exercise
        // because no sample binds a locally-declared class to a typed §B.
        foreach (var name in ModuleDeclaredTypeNames(module))
        {
            if (_moduleDeclaredTypes.Add(name) && _env.LookupType(name) == null)
            {
                _env.DefineType(name, new ExternalType(name));
            }
        }
        _moduleDeclaredReferenceTypes.Clear();
        _moduleDeclaredReferenceTypes.UnionWith(Parsing.AttributeHelper.GetDeclaredReferenceTypeNames(module));

        // Pass 0: refinement type definitions. These run second so a §RTYPE's base type can name
        // a module class, but RegisterRefinementType rejects an already-defined name — so it is
        // taught to treat a name Pass -1 seeded as available. A refinement sharing a class's name
        // wins it, exactly as it did when the checker knew no module types at all; a §RTYPE
        // duplicating another §RTYPE is still an error.
        foreach (var rtype in module.RefinementTypes)
        {
            RegisterRefinementType(rtype);
        }

        // First pass: register all type definitions
        foreach (var delegateDefinition in module.Delegates)
        {
            RegisterDelegate(delegateDefinition);
        }
        foreach (var func in module.Functions)
        {
            RegisterFunction(func);
        }

        // Second pass: type check function bodies
        foreach (var func in module.Functions)
        {
            CheckFunction(func);
        }

        // Third pass (#1676): class members. Before this the checker never looked inside a
        // §CL, so inside methods only the runtime refinement guards applied. Only the soundness
        // errors are kept: refinement errors (in a module with §RTYPE declarations) and numeric
        // conversions C# rejects between two known types (every module; nested `module`
        // containers are classes too).
        {
            // A base class is resolved only when its simple name names exactly one non-generic
            // module class; otherwise its members stay unknown to the checker (#1676 review).
            // A nested class of the same simple name could be the one C# resolves, so the name
            // must be unique among all classes, nested ones included.
            var nameCounts = AllClasses(module.Classes)
                .GroupBy(cls => cls.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var moduleClasses = module.Classes
                .Where(cls => nameCounts[cls.Name] == 1 && cls.TypeParameters.Count == 0)
                .ToDictionary(cls => cls.Name, StringComparer.Ordinal);
            foreach (var cls in module.Classes)
            {
                CheckClassKeepingOnlyRefinementSoundness(cls, moduleClasses);
            }
        }
    }

    /// <summary>
    /// Checks a class's members and keeps ONLY the soundness errors that produces: refinement
    /// errors (#1676) and numeric conversions C# rejects between two known types (0.30 i1,
    /// reported through the same channel). The checker has no member table: inherited fields, members of an external base
    /// class, events, indexers and user-defined operators are all invisible to it, so its other
    /// diagnostics inside class members are mostly about its own model, not the program. The
    /// binder and Roslyn keep owning those, exactly as before this pass existed.
    /// </summary>
    private static IEnumerable<ClassDefinitionNode> AllClasses(IEnumerable<ClassDefinitionNode> classes)
        => classes.SelectMany(cls => AllClasses(cls.NestedClasses).Prepend(cls));

    private void CheckClassKeepingOnlyRefinementSoundness(
        ClassDefinitionNode cls, IReadOnlyDictionary<string, ClassDefinitionNode> moduleClasses)
    {
        var checkpoint = _diagnostics.CreateCheckpoint();
        CheckClass(cls, moduleClasses);
        var kept = _diagnostics.Skip(checkpoint.Diagnostics)
            .Where(_refinementSoundnessDiagnostics.Contains)
            .ToList();
        _diagnostics.RestoreCheckpoint(checkpoint);
        foreach (var diagnostic in kept)
            _diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Every type name the module itself declares. Modelled as <see cref="ExternalType"/> rather
    /// than a structural type: the checker has no member table, so it can say "this name exists"
    /// but not "this member exists on it". That is exactly the honest claim — it stops the
    /// unknown-type warning without inventing member checking the checker cannot back up.
    /// </summary>
    private static IEnumerable<string> ModuleDeclaredTypeNames(ModuleNode module)
    {
        foreach (var name in DeclaredTypeNames(
            module.Classes, module.Interfaces, module.Enums, module.Delegates))
        {
            yield return name;
        }

        foreach (var it in module.IndexedTypes) yield return it.Name;

        // §PP-wrapped declarations, both branches. A name declared only in the #else arm is
        // still a name the user wrote, and the checker has no preprocessor state — registering
        // both arms is the fail-open direction, and failing open here costs only a suppressed
        // warning, while failing closed reports a working program as a typo.
        foreach (var pp in module.TypePreprocessorBlocks)
        {
            foreach (var name in PreprocessorDeclaredTypeNames(pp)) yield return name;
        }
    }

    private static IEnumerable<string> PreprocessorDeclaredTypeNames(TypePreprocessorBlockNode? pp)
    {
        for (; pp is not null; pp = pp.ElseBranch)
        {
            foreach (var name in DeclaredTypeNames(pp.Classes, pp.Interfaces, pp.Enums, pp.Delegates))
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// The four type-declaring collections, recursing through nested classes. Shared between the
    /// module level and every §PP arm so a declaration cannot be visible in one place and unknown
    /// in the other — the release that shipped this pass found the hand-enumerated version missed
    /// §PP blocks, nested types and §ITYPE, which is the same enumeration failure its own headline
    /// is about.
    /// </summary>
    private static IEnumerable<string> DeclaredTypeNames(
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<EnumDefinitionNode> enums,
        IReadOnlyList<DelegateDefinitionNode> delegates,
        string? enclosing = null)
    {
        foreach (var c in classes)
        {
            var name = Qualify(enclosing, c.Name);
            yield return name;

            // Nested types are registered QUALIFIED (`Outer.Inner`), not bare. Module functions
            // are emitted into a sibling static class, so `§B{i:Inner}` produces C# that csc
            // rejects with CS0246 — registering the bare name would silence a true positive while
            // leaving the spelling that does compile still warning. Release review caught the
            // first cut doing exactly that.
            foreach (var nested in DeclaredTypeNames(
                c.NestedClasses, c.NestedInterfaces, c.NestedEnums, c.NestedDelegates, name))
            {
                yield return nested;
            }
        }

        foreach (var i in interfaces) yield return Qualify(enclosing, i.Name);
        foreach (var e in enums) yield return Qualify(enclosing, e.Name);
        foreach (var d in delegates) yield return Qualify(enclosing, d.Name);
    }

    private static string Qualify(string? enclosing, string name)
        => enclosing is null ? name : $"{enclosing}.{name}";

    private static IEnumerable<string> DeclaredEnumTypeNames(
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<EnumDefinitionNode> enums,
        string? enclosing = null)
    {
        foreach (var enumDefinition in enums)
            yield return Qualify(enclosing, enumDefinition.Name);
        foreach (var classDefinition in classes)
        {
            var name = Qualify(enclosing, classDefinition.Name);
            if (classDefinition.IsStruct)
                yield return name;
            foreach (var nested in DeclaredEnumTypeNames(
                classDefinition.NestedClasses, classDefinition.NestedEnums, name))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Names seeded by Pass -1. Lets RegisterRefinementType tell "this name is another refinement"
    /// (a real duplicate) from "this name is a module class Pass -1 seeded" (a refinement is
    /// allowed to take it, as it could before v0.12).
    /// </summary>
    private readonly HashSet<string> _moduleDeclaredTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _moduleDeclaredReferenceTypes = new(StringComparer.Ordinal);

    /// <summary>Set during the signature pre-pass, which re-resolves annotations CheckFunction
    /// will resolve again — so only the second pass reports.</summary>
    private bool _suppressDiagnostics;

    private void RegisterRefinementType(RefinementTypeNode rtype)
    {
        var baseType = ResolveTypeName(rtype.BaseTypeName, rtype.Span);
        // ExternalType counts as undefined HERE, unlike an interop annotation. A refinement's
        // base type is a Calor-level construct the refinement machinery has to reason about, so
        // "some .NET type we don't model" is not an acceptable answer — `§RTYPE{r:T:no_such_type}`
        // must still be an error even though the general type resolver is now permissive.
        if (baseType is ErrorType or ExternalType)
        {
            _diagnostics.ReportError(rtype.Span, DiagnosticCode.RefinementUndefinedBaseType,
                $"Refinement type '{rtype.Name}' references undefined base type '{rtype.BaseTypeName}'");
            return;
        }

        // A name Pass -1 seeded is available to the FIRST refinement that claims it — that is how
        // a §RTYPE sharing a module type's name kept working across the v0.12 flip. `Remove`
        // consumes the exemption, so a SECOND §RTYPE of the same name is a duplicate again. An
        // earlier revision used `Contains`, which left the exemption standing forever and silently
        // accepted two conflicting refinements — a regression against main, caught by review.
        var seededName = _moduleDeclaredTypes.Remove(rtype.Name);
        if (_env.LookupType(rtype.Name) != null && !seededName)
        {
            _diagnostics.ReportError(rtype.Span, DiagnosticCode.RefinementDuplicateName,
                $"Duplicate refinement type name '{rtype.Name}'");
            return;
        }

        // Named refinements are nominal (#1530): the §RTYPE name is part of the type's identity,
        // so two refinements of one base type never compare equal.
        var predicateText = $"#{rtype.BaseTypeName}";
        var refinedType = new RefinedType(baseType, predicateText, rtype.Predicate, rtype.Name);
        _env.DefineType(rtype.Name, refinedType);
    }

    /// <summary>
    /// Registers a function's type parameters, from BOTH spellings.
    ///
    /// <para><c>§F{...}&lt;T&gt;</c> — after the attribute block — populates
    /// <c>func.TypeParameters</c>. But the spelling the shipped sample and the docs use puts them
    /// inside the name attribute, <c>§F{f001:Identity&lt;T&gt;:pub}</c>, which the parser leaves
    /// embedded in <c>Name</c>. The emitter passes that through and the generated C# is correct,
    /// so the program works — but the checker saw an unresolved <c>T</c> and warned that a
    /// correct, documented generic parameter might be a typo.</para>
    /// </summary>
    private void RegisterTypeParameters(FunctionNode func)
        => RegisterTypeParameters(func.TypeParameters, func.Name);

    private void RegisterTypeParameters(IReadOnlyList<TypeParameterNode> typeParameters, string callableName)
    {
        foreach (var tp in typeParameters)
        {
            _env.DefineType(tp.Name, new TypeParameterType(tp.Name, tp.Constraints));
        }

        var open = callableName.IndexOf('<');
        if (open > 0 && callableName.EndsWith('>'))
        {
            foreach (var raw in callableName[(open + 1)..^1].Split(','))
            {
                var name = raw.Trim();
                if (name.Length > 0)
                {
                    _env.DefineType(name, new TypeParameterType(name, Array.Empty<TypeConstraintNode>()));
                }
            }
        }
    }

    private void RegisterFunction(FunctionNode func)
        => _env.DefineFunction(GetCallableLookupName(func.Name), CreateCandidate(func));

    private FunctionCandidate CreateCandidate(FunctionNode func)
        => CreateCandidate(func.TypeParameters, func.Name, func.Parameters, func.Output, func.IsAsync);

    private FunctionCandidate CreateCandidate(
        IReadOnlyList<TypeParameterNode> typeParameters,
        string callableName,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        bool isAsync = false)
    {
        _env.EnterScope();

        // Signature PRE-PASS: resolve quietly. CheckFunction resolves the same annotations again
        // and owns the diagnostics — without this the unresolved-type warning was reported three
        // times for a single `T` (parameter here, return here, parameter again there).
        var previousSuppressDiagnostics = _suppressDiagnostics;
        _suppressDiagnostics = true;

        RegisterTypeParameters(typeParameters, callableName);

        var paramTypes = new List<CalorType>();
        foreach (var param in parameters)
        {
            var paramType = ResolveParameterType(param);
            paramTypes.Add(paramType);
        }

        var shape = Analysis.ReturnShape.GetAsyncReturnTypes(output?.TypeName ?? "void", isAsync, _module, AsyncCarrierTypeParameters());
        var returnType = ResolveTypeName(shape.CallableType, output?.Span ?? default);

        _suppressDiagnostics = previousSuppressDiagnostics;
        _env.ExitScope();

        var funcType = new FunctionType(paramTypes, returnType);
        return new FunctionCandidate(
            funcType,
            parameters.Select(parameter => parameter.Name).ToArray(),
            parameters.Select(parameter => parameter.Modifier).ToArray(),
            parameters.Select(parameter => parameter.DefaultValue != null).ToArray(),
            GetTypeParameterNames(typeParameters, callableName));
    }

    private void RegisterDelegate(DelegateDefinitionNode delegateDefinition)
    {
        _suppressDiagnostics = true;
        var parameterTypes = delegateDefinition.Parameters
            .Select(parameter => ResolveTypeName(parameter.TypeName, parameter.Span))
            .ToArray();
        var returnType = delegateDefinition.Output != null
            ? ResolveTypeName(delegateDefinition.Output.TypeName, delegateDefinition.Output.Span)
            : PrimitiveType.Void;
        _suppressDiagnostics = false;
        _env.DefineType(delegateDefinition.Name, new FunctionType(
            parameterTypes,
            returnType,
            delegateDefinition.Parameters.Select(parameter => parameter.Name).ToArray(),
            delegateDefinition.Parameters.Select(parameter => parameter.Modifier).ToArray(),
            delegateDefinition.Name));
    }

    private static string GetCallableLookupName(string name)
    {
        var open = name.IndexOf('<');
        return open > 0 && name.EndsWith('>') ? name[..open] : name;
    }

    private static IReadOnlyList<string> GetTypeParameterNames(
        IReadOnlyList<TypeParameterNode> typeParameters, string callableName)
    {
        var names = typeParameters.Select(parameter => parameter.Name).ToList();
        var open = callableName.IndexOf('<');
        if (open > 0 && callableName.EndsWith('>'))
        {
            names.AddRange(callableName[(open + 1)..^1]
                .Split(',')
                .Select(name => name.Trim())
                .Where(name => name.Length > 0));
        }
        return names;
    }

    private void CheckFunction(FunctionNode func)
    {
        _env.EnterScope();
        RegisterTypeParameters(func);
        var previousSuppressDiagnostics = _suppressDiagnostics;
        _suppressDiagnostics = true;
        var shape = Analysis.ReturnShape.GetAsyncReturnTypes(func.Output?.TypeName ?? "void", func.IsAsync, _module, AsyncCarrierTypeParameters());
        var returnType = ResolveTypeName(shape.BodyType, func.Output?.Span ?? func.Span);
        _suppressDiagnostics = previousSuppressDiagnostics;
        var previousValidateReturnAssignments = _validateReturnAssignments;
        _validateReturnAssignments = func.IsAsync;
        CheckCallableBody(func.Parameters, returnType, func.Body);
        _validateReturnAssignments = previousValidateReturnAssignments;
        _env.ExitScope();
    }

    /// <summary>
    /// Checks one callable body: a module function, a local function, or (#1676) a class member.
    /// The caller has already entered a scope and registered the callable's type parameters;
    /// <paramref name="returnType"/> is what a <c>§R</c> in the body must produce.
    /// </summary>
    private void CheckCallableBody(
        IReadOnlyList<ParameterNode> parameters,
        CalorType returnType,
        IReadOnlyList<StatementNode> body,
        Action? defineExtraVariables = null)
    {
        _env.EnterScope();
        var previousReturnType = _currentReturnType;
        _currentReturnType = returnType;

        // Add parameters to scope
        var previousInlineRefinedParameters = _inlineRefinedParameters;
        _inlineRefinedParameters = new HashSet<string>(StringComparer.Ordinal);
        defineExtraVariables?.Invoke();
        foreach (var param in parameters)
        {
            var paramType = ResolveParameterType(param);
            _env.DefineVariable(param.Name, paramType);
            if (param.InlineRefinement != null)
                _inlineRefinedParameters.Add(param.Name);
        }

        // 0.25 F3 (#847): local functions shadow same-named functions in the whole
        // body (and in each other), as in C#.
        _env.EnterLocalFunctions();
        foreach (var local in body.OfType<LocalFunctionStatementNode>())
            _env.DefineLocalFunction(GetCallableLookupName(local.Function.Name), CreateCandidate(local.Function));

        // Check body statements
        foreach (var stmt in body)
        {
            CheckStatement(stmt);
        }

        _env.ExitLocalFunctions();
        _currentReturnType = previousReturnType;
        _inlineRefinedParameters = previousInlineRefinedParameters;
        _env.ExitScope();
    }

    /// <summary>
    /// #1676: checks every member body of a class (methods, constructors, property and indexer
    /// accessors, operators) as the checker checks a module function, so the static refinement
    /// checks (#1529, #1530) apply inside methods too, and the inferred refinements of locals and
    /// lambda parameters there come from the checker like everywhere else. The class's fields and
    /// properties, and those of base classes declared in the module, are in scope as variables.
    /// The class's own methods shadow same-named module functions, as they do in the emitted C#.
    /// Members inside §PP blocks are not visited: the checker has no preprocessor state, and an
    /// inactive arm need not compile.
    /// </summary>
    private void CheckClass(ClassDefinitionNode cls, IReadOnlyDictionary<string, ClassDefinitionNode> moduleClasses)
    {
        var ancestors = new List<ClassDefinitionNode>();
        var visited = new HashSet<ClassDefinitionNode>(ReferenceEqualityComparer.Instance) { cls };
        var baseName = cls.BaseClass;
        while (baseName != null
            && !baseName.Contains('<')
            && moduleClasses.TryGetValue(baseName, out var baseClass)
            && visited.Add(baseClass))
        {
            ancestors.Add(baseClass);
            baseName = baseClass.BaseClass;
        }
        ancestors.Reverse();

        // When the base chain ends in a class the checker cannot see (an external type, a
        // generic or ambiguous name), an inherited member may be what a bare name means, so
        // names from enclosing classes are not visible through this class (#1676 review).
        _env.EnterScope(opaque: baseName != null);
        foreach (var tp in cls.TypeParameters)
            _env.DefineType(tp.Name, new TypeParameterType(tp.Name, tp.Constraints));

        var previousSuppressDiagnostics = _suppressDiagnostics;
        _suppressDiagnostics = true;
        foreach (var ancestor in ancestors)
            DefineClassMemberVariables(ancestor);
        DefineClassMemberVariables(cls);
        _suppressDiagnostics = previousSuppressDiagnostics;

        // A §B{~name} in a member body that names a field declares a new local (the emitter only
        // treats locals and parameters as rebind targets), so the rebind rules must not see the
        // member scope.
        var previousMemberScopeDepth = _memberScopeDepth;
        _memberScopeDepth = _env.ScopeDepth;

        // A bare call names one of the class's own methods. With any base class, a name the
        // class does not declare may be an inherited method (C# member hiding and overloads the
        // checker does not model), so it resolves to nothing rather than to a module function
        // or an enclosing class's method (#1676 review).
        _env.EnterLocalFunctions(opaque: cls.BaseClass != null);
        foreach (var method in cls.Methods)
        {
            _env.DefineLocalFunction(
                GetCallableLookupName(method.Name),
                CreateCandidate(method.TypeParameters, method.Name, method.Parameters, method.Output, method.IsAsync));
        }

        foreach (var constructor in cls.Constructors)
            CheckMemberBody(constructor.Parameters, null, constructor.Body);
        foreach (var method in cls.Methods)
        {
            _env.EnterScope();
            RegisterTypeParameters(method.TypeParameters, method.Name);
            CheckMemberBody(method.Parameters, method.Output, method.Body, method.IsAsync);
            _env.ExitScope();
        }
        foreach (var property in cls.Properties)
        {
            CheckAccessors(
                Array.Empty<ParameterNode>(), property.TypeName, property.TypeNameSpan,
                property.Getter, property.Setter, property.Initer);
        }
        foreach (var indexer in cls.Indexers)
        {
            CheckAccessors(
                indexer.Parameters, indexer.TypeName, indexer.Span,
                indexer.Getter, indexer.Setter, indexer.Initer);
        }
        foreach (var op in cls.OperatorOverloads)
            CheckMemberBody(op.Parameters, op.Output, op.Body);
        // Nested classes see the enclosing class's members and methods, as a C# nested type
        // sees the enclosing type's statics; their own methods shadow them.
        foreach (var nested in cls.NestedClasses)
            CheckClass(nested, moduleClasses);
        _env.ExitLocalFunctions();
        _memberScopeDepth = previousMemberScopeDepth;
        _env.ExitScope();
    }

    /// <summary>
    /// Scope depth of the innermost class's fields and properties, or 0 outside class members.
    /// Variables at or below it are members, never rebind targets.
    /// </summary>
    private int _memberScopeDepth;

    /// <summary>A reference spelled <c>this.name</c> names the member <c>name</c>.</summary>
    private static bool TryGetThisMemberName(string reference, out string memberName)
    {
        memberName = reference.StartsWith("this.", StringComparison.Ordinal) ? reference[5..] : "";
        return memberName.Length > 0 && !memberName.Contains('.');
    }

    /// <summary>The declared type of a member of the innermost class, ignoring locals.</summary>
    private CalorType? LookupThisMember(string name)
        => _memberScopeDepth > 0 ? _env.LookupVariableAtOrBelow(name, _memberScopeDepth) : null;

    private void DefineClassMemberVariables(ClassDefinitionNode cls)
    {
        foreach (var field in cls.Fields)
        {
            var fieldType = ResolveTypeName(field.TypeName, field.TypeNameSpan);
            // A const field is a constant expression in C#: `byte b = Limit` is valid when the
            // value fits, so its value (not computed here) must never read as a narrowing.
            _env.DefineVariable(field.Name, field.Modifiers.HasFlag(MethodModifiers.Const)
                ? AsIntegerConstant(fieldType, null)
                : fieldType);
        }
        foreach (var property in cls.Properties)
            _env.DefineVariable(property.Name, ResolveTypeName(property.TypeName, property.TypeNameSpan));
    }

    private void CheckMemberBody(
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        IReadOnlyList<StatementNode> body,
        bool isAsync = false)
    {
        var previousSuppressDiagnostics = _suppressDiagnostics;
        _suppressDiagnostics = true;
        var shape = Analysis.ReturnShape.GetAsyncReturnTypes(output?.TypeName ?? "void", isAsync, _module, AsyncCarrierTypeParameters());
        var returnType = ResolveTypeName(shape.BodyType, output?.Span ?? default);
        _suppressDiagnostics = previousSuppressDiagnostics;
        var previousValidateReturnAssignments = _validateReturnAssignments;
        _validateReturnAssignments = isAsync;
        CheckCallableBody(parameters, returnType, body);
        _validateReturnAssignments = previousValidateReturnAssignments;
    }

    private void CheckAccessors(
        IReadOnlyList<ParameterNode> parameters,
        string typeName,
        TextSpan typeNameSpan,
        params PropertyAccessorNode?[] accessors)
    {
        var previousSuppressDiagnostics = _suppressDiagnostics;
        _suppressDiagnostics = true;
        var memberType = ResolveTypeName(typeName, typeNameSpan);
        _suppressDiagnostics = previousSuppressDiagnostics;
        foreach (var accessor in accessors)
        {
            if (accessor is null)
                continue;
            // The emitter guards neither an accessor's `value` nor an indexer's parameters, so
            // they are typed as their base types: the checker claims no refinement the
            // generated code does not enforce (#1676 review).
            void DefineAccessorVariables(bool withValue)
            {
                foreach (var parameter in parameters)
                    _env.DefineVariable(parameter.Name, EraseRefinement(ResolveTypeName(parameter.TypeName, parameter.Span)));
                if (withValue)
                    _env.DefineVariable("value", EraseRefinement(memberType));
            }
            if (accessor.Kind == PropertyAccessorNode.AccessorKind.Get)
            {
                CheckCallableBody(Array.Empty<ParameterNode>(), memberType, accessor.Body,
                    () => DefineAccessorVariables(withValue: false));
            }
            else
            {
                CheckCallableBody(Array.Empty<ParameterNode>(), PrimitiveType.Void, accessor.Body,
                    () => DefineAccessorVariables(withValue: true));
            }
        }
    }

    private void CheckStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case CallStatementNode call:
                CheckCallStatement(call);
                break;
            case ExpressionStatementNode expression:
                _ = InferExpressionType(expression.Expression);
                break;
            case LocalFunctionStatementNode local:
                CheckFunction(local.Function);
                break;
            case ReturnStatementNode ret:
                CheckReturnStatement(ret);
                break;
            case ForStatementNode forStmt:
                CheckForStatement(forStmt);
                break;
            case WhileStatementNode whileStmt:
                CheckWhileStatement(whileStmt);
                break;
            case DoWhileStatementNode doWhileStmt:
                CheckStructuralStatement(() => CheckDoWhileStatement(doWhileStmt));
                break;
            case ForeachStatementNode foreachStmt:
                CheckStructuralStatement(() => CheckForeachStatement(foreachStmt));
                break;
            case IfStatementNode ifStmt:
                CheckIfStatement(ifStmt);
                break;
            case BindStatementNode bind:
                CheckBindStatement(bind);
                break;
            case MatchStatementNode match:
                CheckMatchStatement(match);
                break;
            case TryStatementNode tryStatement:
                ValidateCatchFilters(tryStatement);
                CheckStructuralStatement(() => CheckTryStatement(tryStatement));
                break;
            case UsingStatementNode usingStatement:
                CheckStructuralStatement(() => CheckUsingStatement(usingStatement));
                break;
            case UnsafeBlockNode unsafeBlock:
                CheckStructuralStatement(() => CheckScopedStatements(unsafeBlock.Body));
                break;
            case FixedStatementNode fixedStatement:
                CheckStructuralStatement(() => CheckFixedStatement(fixedStatement));
                break;
            case SyncBlockNode syncBlock:
                CheckStructuralStatement(() =>
                {
                    _ = InferExpressionType(syncBlock.LockExpression);
                    CheckScopedStatements(syncBlock.Body);
                });
                break;
            // Collection mutation statements
            case CollectionPushNode push:
                CheckCollectionPushStatement(push);
                break;
            case DictionaryPutNode put:
                CheckDictionaryPutStatement(put);
                break;
            case CollectionRemoveNode remove:
                CheckCollectionRemoveStatement(remove);
                break;
            case CollectionSetIndexNode setIndex:
                CheckCollectionSetIndexStatement(setIndex);
                break;
            case CollectionClearNode clear:
                CheckCollectionClearStatement(clear);
                break;
            case CollectionInsertNode insert:
                CheckCollectionInsertStatement(insert);
                break;
            case DictionaryForeachNode dictForeach:
                CheckDictionaryForeachStatement(dictForeach);
                break;
            case AssignmentStatementNode assignment:
                if (_hasNamedRefinements)
                    CheckRefinedAssignment(assignment);
                else
                    CheckNumericAssignment(assignment.Target, assignment.Value, "an assignment");
                break;
            case CompoundAssignmentStatementNode compound:
                CheckCompoundAssignment(compound);
                break;
            case PreprocessorDirectiveNode preprocessor:
                CheckPreprocessorDirective(preprocessor);
                break;
            case PrintStatementNode print when _hasNamedRefinements:
                // #1676: the printed expression is type-checked for refinement soundness: a call
                // inside §P passing a Nat where a Pos is expected is Calor0202, and a lambda
                // passed to such a call is typed by the delegate parameter, refinement included,
                // so writes to its parameter are guarded. The checker's other diagnostics are not
                // kept: it cannot type much of what §P prints (`s.Length`), and Roslyn does.
                _ = InferKeepingOnlyRefinementSoundnessDiagnostics(print.Expression);
                break;
            default:
                // Other statement types (print, assignment, throw, etc.) are handled elsewhere or need no type checking
                break;
        }
    }

    private void CheckCallStatement(CallStatementNode call)
        => InferCallExpressionType(new CallExpressionNode(
            call.Span,
            call.Target,
            call.Arguments,
            call.ArgumentNames,
            call.ArgumentModifiers,
            call.TypeArguments,
            call.CalleeSpan,
            call.ReceiverSpan));

    private void CheckCallArguments(IReadOnlyList<ExpressionNode> arguments)
    {
        foreach (var arg in arguments)
        {
            InferExpressionType(arg);
        }
    }

    /// <summary>
    /// #1530: named refinements are nominal. A value of one named refinement is never accepted
    /// where a DIFFERENT named refinement is expected (<c>Nat</c> into <c>Pos</c>), even when both
    /// refine the same base type. A plain base-type value (an arithmetic result, a literal) keeps
    /// its existing treatment at §ASSIGN and returns: it is accepted and guarded at runtime.
    /// </summary>
    private static bool IsDistinctNamedRefinement(CalorType target, CalorType source)
        => target is RefinedType { RefinementName: not null }
            && source is RefinedType { RefinementName: not null }
            && !IsAssignable(target, source);

    private void ReportDistinctNamedRefinement(
        Parsing.TextSpan span, CalorType target, CalorType source, string position)
        => ReportRefinementSoundnessError(span,
            $"Cannot use {source.SurfaceName} as {target.SurfaceName} in {position}: named refinement "
            + "types are distinct even over the same base type. Convert explicitly, for example "
            + $"with a function that takes the base type and returns {target.SurfaceName}; its "
            + "return guard checks the value.");

    /// <summary>
    /// Reports an error that protects a refinement (#1529/#1530). Unlike ordinary checker
    /// diagnostics, it survives the flow cache of structural statements (try, foreach, using...),
    /// which otherwise drops the checker's diagnostics there, and it survives the filtered
    /// inference of assignment right-hand sides. Inside a speculative inference it is rolled
    /// back with everything else.
    /// </summary>
    private void ReportRefinementSoundnessError(Parsing.TextSpan span, string message)
    {
        var before = _diagnostics.Count;
        _diagnostics.ReportError(span, DiagnosticCode.TypeMismatch, message);
        _nominalRefinementDiagnostics.Add(_diagnostics.Last());
        _refinementSoundnessDiagnostics.Add(_diagnostics.Last());
        if (_speculationDepth == 0)
            PreserveFlowCacheDiagnosticsSince(before);
    }

    private readonly HashSet<Diagnostic> _nominalRefinementDiagnostics =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Only the errors <see cref="ReportRefinementSoundnessError"/> reports. Unlike
    /// <see cref="_nominalRefinementDiagnostics"/>, it does not include the other diagnostics of
    /// a committed lambda check, so it is the set class members keep (#1676).
    /// </summary>
    private readonly HashSet<Diagnostic> _refinementSoundnessDiagnostics =
        new(ReferenceEqualityComparer.Instance);
    private bool _hasNamedRefinements;

    /// <summary>A lambda's parameters shadow same-named enclosing parameters.</summary>
    private static HashSet<string> WithoutShadowedNames(HashSet<string> names, LambdaExpressionNode lambda)
    {
        if (!lambda.Parameters.Any(parameter => names.Contains(parameter.Name)))
            return names;
        var result = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var parameter in lambda.Parameters)
            result.Remove(parameter.Name);
        return result;
    }
    private HashSet<string> _inlineRefinedParameters = new(StringComparer.Ordinal);

    /// <summary>
    /// Type-checks an expression the checker otherwise leaves alone (an assignment right-hand
    /// side), keeping ONLY the nominal-refinement diagnostics it produces. A call nested there
    /// that passes a Nat where a Pos is expected is still reported, without surfacing any other
    /// diagnostic that this statement kind never produced before.
    /// </summary>
    private CalorType InferKeepingOnlyNominalRefinementDiagnostics(ExpressionNode expression, CalorType? expected)
    {
        var checkpoint = _diagnostics.CreateCheckpoint();
        var type = InferExpressionType(expression, expected);
        var kept = _diagnostics.Skip(checkpoint.Diagnostics)
            .Where(_nominalRefinementDiagnostics.Contains)
            .ToList();
        _diagnostics.RestoreCheckpoint(checkpoint);
        foreach (var diagnostic in kept)
            _diagnostics.Add(diagnostic);
        return type;
    }

    /// <summary>
    /// Type-checks an expression and keeps only the refinement-soundness errors it produces
    /// (#1676): no ordinary diagnostic, not even one from a committed lambda check.
    /// </summary>
    private CalorType InferKeepingOnlyRefinementSoundnessDiagnostics(ExpressionNode expression)
    {
        var checkpoint = _diagnostics.CreateCheckpoint();
        var type = InferExpressionType(expression);
        var kept = _diagnostics.Skip(checkpoint.Diagnostics)
            .Where(_refinementSoundnessDiagnostics.Contains)
            .ToList();
        _diagnostics.RestoreCheckpoint(checkpoint);
        foreach (var diagnostic in kept)
            _diagnostics.Add(diagnostic);
        return type;
    }

    /// <summary>
    /// In a module with §RTYPE declarations, an §ASSIGN right-hand side is type-checked for
    /// refinement soundness only: a value of a different named refinement written into a refined
    /// local is rejected, and so is one nested in the value (a call passing a Nat where a Pos is
    /// expected). Every other assignment diagnostic stays with the binder and Roslyn, as before.
    /// </summary>
    /// <summary>
    /// The declared type of an assignment target the checker can name: a local, a parameter or
    /// a member of the enclosing class (bare or through <c>this</c>). Null for anything else.
    /// </summary>
    private CalorType? LookupAssignmentTargetType(ExpressionNode target) => target switch
    {
        ReferenceNode reference when TryGetThisMemberName(reference.Name, out var memberName)
            => LookupThisMember(memberName),
        ReferenceNode reference => _env.LookupVariable(reference.Name),
        FieldAccessNode { Target: ThisExpressionNode or ReferenceNode { Name: "this" } } thisField
            => LookupThisMember(thisField.FieldName),
        _ => null,
    };

    /// <summary>
    /// An assignment into a numeric local, parameter or member: the right-hand side must convert
    /// implicitly, as in C#. The checker otherwise leaves assignment right-hand sides to C#, so
    /// only the numeric-conversion verdict (and nominal-refinement errors) are kept from it.
    /// </summary>
    private void CheckNumericAssignment(ExpressionNode target, ExpressionNode value, string position)
    {
        if (LookupAssignmentTargetType(target) is not { } targetType
            || EraseRefinement(targetType) is not PrimitiveType numericTarget
            || !HasExactNumericType(numericTarget))
            return;
        var valueType = InferKeepingOnlyNominalRefinementDiagnostics(value, numericTarget);
        if (IsRejectedNumericConversion(numericTarget, valueType))
            ReportNumericConversion(value.Span, numericTarget, valueType, position);
    }

    /// <summary>
    /// C# accepts <c>x op= y</c> when <c>y</c> converts implicitly to the type of <c>x</c> (or the
    /// operator is a shift), even though <c>x op y</c> may be wider (§12.21.4): <c>b += 1</c> on a
    /// byte is valid, <c>f += someDouble</c> on a float is CS0266.
    /// </summary>
    private void CheckCompoundAssignment(CompoundAssignmentStatementNode compound)
    {
        if (compound.Operator is not (CompoundAssignmentOperator.LeftShift
                or CompoundAssignmentOperator.RightShift or CompoundAssignmentOperator.NullCoalesce)
            && LookupAssignmentTargetType(compound.Target) is { } targetType
            && EraseRefinement(targetType) is PrimitiveType numericTarget
            && HasExactNumericType(numericTarget))
        {
            var valueType = InferKeepingOnlyNominalRefinementDiagnostics(compound.Value, null);
            if (IsRejectedNumericConversion(numericTarget, valueType))
                ReportNumericConversion(compound.Value.Span, numericTarget, valueType, "a compound assignment");
            return;
        }
        if (_hasNamedRefinements)
            _ = InferKeepingOnlyNominalRefinementDiagnostics(compound.Value, null);
    }

    private void CheckRefinedAssignment(AssignmentStatementNode assignment)
    {
        if (!_hasNamedRefinements)
            return;

        var targetName = assignment.Target switch
        {
            ReferenceNode reference => reference.Name,
            FieldAccessNode { Target: ThisExpressionNode or ReferenceNode { Name: "this" } } thisField => $"this.{thisField.FieldName}",
            _ => null,
        };
        var targetType = assignment.Target switch
        {
            ReferenceNode reference when TryGetThisMemberName(reference.Name, out var memberName)
                => LookupThisMember(memberName),
            ReferenceNode reference => _env.LookupVariable(reference.Name),
            FieldAccessNode { Target: ThisExpressionNode or ReferenceNode { Name: "this" } } thisField => LookupThisMember(thisField.FieldName),
            _ => null,
        };
        // The expected type is the ERASED target: a base value written into a refined local is
        // still accepted and guarded at run time (a match arm of type i32 into a Nat must not be
        // rejected), while a lambda assigned to a delegate variable still gets its contextual
        // parameter types, refinements included.
        // #1676: a match written into a refined local is inferred against the erased target too,
        // but each arm is also checked against the declared refinement: an arm of a different
        // named refinement (a Pos arm into a Nat) is rejected, while a base-typed arm keeps the
        // guarded path.
        var previousArmRefinement = _matchArmRefinementTarget;
        _matchArmRefinementTarget = assignment.Value is MatchExpressionNode
            && targetType is RefinedType { RefinementName: not null }
                ? (assignment.Value, targetType, $"assignment to '{targetName}'")
                : null;
        CalorType valueType;
        try
        {
            valueType = InferKeepingOnlyNominalRefinementDiagnostics(
                assignment.Value,
                targetType is null ? null : EraseRefinement(targetType));
        }
        finally
        {
            _matchArmRefinementTarget = previousArmRefinement;
        }
        if (targetType != null && IsDistinctNamedRefinement(targetType, valueType))
        {
            ReportDistinctNamedRefinement(assignment.Span, targetType, valueType,
                $"assignment to '{targetName}'");
        }
        else if (targetType != null && IsRejectedNumericConversion(EraseRefinement(targetType), valueType))
        {
            ReportNumericConversion(assignment.Value.Span, EraseRefinement(targetType), valueType,
                "an assignment");
        }
    }

    /// <summary>
    /// The match expression whose arms must each be checked against a declared refinement (a
    /// match assigned to a refined local, #1676), with that refinement and the position used in
    /// the diagnostic. Inherited by matches nested in an arm's result.
    /// </summary>
    private (ExpressionNode Match, CalorType Refinement, string Position)? _matchArmRefinementTarget;

    private void CheckReturnStatement(ReturnStatementNode ret)
    {
        if (ret.Expression is null && _lambdaBodyDepth > 0 && _validateReturnAssignments
            && _currentReturnType is { } expected && !expected.Equals(PrimitiveType.Void)
            && !ContainsInferencePlaceholder(expected))
        {
            _lambdaReturnInvalid = true;
            if (!_suppressContextualDiagnostics)
                _diagnostics.ReportError(ret.Span, DiagnosticCode.TypeMismatch,
                    $"A lambda targeting {expected.SurfaceName} must return a value.");
        }
        if (ret.Expression != null)
        {
            var returnType = InferExpressionType(ret.Expression, _currentReturnType);
            _inferredLambdaReturnTypes?.Add(returnType);
            if (_currentReturnType != null
                && !_suppressContextualDiagnostics
                && !(_validateReturnAssignments || ret.Expression is CallExpressionNode or OverflowContextExpressionNode)
                && IsDistinctNamedRefinement(_currentReturnType, returnType))
            {
                ReportDistinctNamedRefinement(ret.Expression.Span, _currentReturnType, returnType, "a return");
            }
            if ((_validateReturnAssignments || ret.Expression is CallExpressionNode or OverflowContextExpressionNode)
                && _currentReturnType != null
                && !ContainsInferencePlaceholder(_currentReturnType)
                && !ContainsInferencePlaceholder(returnType)
                && !IsAssignable(_currentReturnType, returnType)
                && !(_lambdaBodyDepth == 0
                    && (IsBinderOwnedNullabilityMismatch(_currentReturnType, returnType)
                        || _boundGenericReceivingFailures?.Contains(ret.Expression.Span) == true)))
            {
                _lambdaReturnInvalid = true;
                if (!_suppressContextualDiagnostics)
                {
                    if (IsDistinctNamedRefinement(_currentReturnType, returnType))
                    {
                        ReportDistinctNamedRefinement(ret.Expression.Span, _currentReturnType, returnType, "a return");
                    }
                    else if (IsRejectedNumericConversion(EraseRefinement(_currentReturnType), returnType))
                    {
                        ReportNumericConversion(ret.Expression.Span, EraseRefinement(_currentReturnType),
                            returnType, "a return");
                    }
                    else
                    {
                        _diagnostics.ReportError(ret.Expression.Span, DiagnosticCode.TypeMismatch,
                            $"Return type {returnType.SurfaceName} is not assignable to "
                            + _currentReturnType.SurfaceName);
                    }
                }
            }
            else if (_currentReturnType != null
                && !_suppressContextualDiagnostics
                && !(_validateReturnAssignments || ret.Expression is CallExpressionNode or OverflowContextExpressionNode)
                && IsRejectedNumericConversion(EraseRefinement(_currentReturnType), returnType))
            {
                // Other returns are left to C#, but a numeric conversion between two known types
                // is decided exactly here, at the Calor expression (`return celsius + 273.15` in a
                // float function is f64 → f32).
                ReportNumericConversion(ret.Expression.Span, EraseRefinement(_currentReturnType),
                    returnType, "a return");
            }
        }
    }

    private void CheckForStatement(ForStatementNode forStmt)
    {
        _env.EnterScope();

        _env.DefineVariable(forStmt.VariableName, ResolveTypeName(forStmt.VariableType, forStmt.VariableSpan));

        if (forStmt.Condition != null && forStmt.Update != null)
        {
            InferExpressionType(forStmt.From);
            var conditionType = InferExpressionType(forStmt.Condition);
            if (IsDefinitelyNotBool(conditionType))
                _diagnostics.ReportError(forStmt.Condition.Span, DiagnosticCode.TypeMismatch,
                    $"For condition must be bool, got {conditionType.SurfaceName}");
            CheckStatement(forStmt.Update);
            foreach (var statement in forStmt.Body)
                CheckStatement(statement);
            _env.ExitScope();
            return;
        }

        var fromType = InferExpressionType(forStmt.From);
        var toType = InferExpressionType(forStmt.To);

        if (IsDefinitelyNotNumeric(fromType))
        {
            _diagnostics.ReportError(forStmt.From.Span, DiagnosticCode.TypeMismatch,
                $"FOR 'from' expression must be numeric, got {fromType.SurfaceName}");
        }

        if (IsDefinitelyNotNumeric(toType))
        {
            _diagnostics.ReportError(forStmt.To.Span, DiagnosticCode.TypeMismatch,
                $"FOR 'to' expression must be numeric, got {toType.SurfaceName}");
        }

        if (forStmt.Step != null)
        {
            var stepType = InferExpressionType(forStmt.Step);
            if (IsDefinitelyNotNumeric(stepType))
            {
                _diagnostics.ReportError(forStmt.Step.Span, DiagnosticCode.TypeMismatch,
                    $"FOR 'step' expression must be numeric, got {stepType.SurfaceName}");
            }
        }

        foreach (var stmt in forStmt.Body)
        {
            CheckStatement(stmt);
        }

        _env.ExitScope();
    }

    private void CheckWhileStatement(WhileStatementNode whileStmt)
    {
        var condType = InferExpressionType(whileStmt.Condition);
        if (IsDefinitelyNotBool(condType))
        {
            _diagnostics.ReportError(whileStmt.Condition.Span, DiagnosticCode.TypeMismatch,
                $"WHILE condition must be bool, got {condType.SurfaceName}");
        }

        _env.EnterScope();
        DefineTrueConditionPatternVariables(whileStmt.Condition);
        foreach (var stmt in whileStmt.Body)
        {
            CheckStatement(stmt);
        }
        _env.ExitScope();
    }

    private void CheckDoWhileStatement(DoWhileStatementNode doWhileStmt)
    {
        CheckScopedStatements(doWhileStmt.Body);
        var condType = InferExpressionType(doWhileStmt.Condition);
        if (IsDefinitelyNotBool(condType))
        {
            _diagnostics.ReportError(doWhileStmt.Condition.Span, DiagnosticCode.TypeMismatch,
                $"DO-WHILE condition must be bool, got {condType.SurfaceName}");
        }
    }

    private void CheckTryStatement(TryStatementNode tryStatement)
    {
        CheckScopedStatements(tryStatement.TryBody);
        foreach (var clause in tryStatement.CatchClauses)
        {
            _env.EnterScope();
            if (clause.VariableName != null && clause.ExceptionType != null)
            {
                _env.DefineVariable(
                    clause.VariableName,
                    ResolveTypeName(clause.ExceptionType, clause.Span));
            }
            if (clause.Filter != null)
            {
                var filterType = InferExpressionType(clause.Filter);
                if (IsDefinitelyNotBool(filterType))
                {
                    _diagnostics.ReportError(
                        clause.Filter.Span,
                        DiagnosticCode.TypeMismatch,
                        $"Catch filter must be bool, got {filterType.SurfaceName}");
                }
            }
            foreach (var statement in clause.Body)
                CheckStatement(statement);
            _env.ExitScope();
        }
        if (tryStatement.FinallyBody != null)
            CheckScopedStatements(tryStatement.FinallyBody);
    }

    private void ValidateCatchFilters(TryStatementNode tryStatement)
    {
        foreach (var clause in tryStatement.CatchClauses)
        {
            if (clause.Filter == null)
                continue;
            _env.EnterScope();
            if (clause.VariableName != null && clause.ExceptionType != null)
            {
                _env.DefineVariable(
                    clause.VariableName,
                    ResolveTypeName(clause.ExceptionType, clause.Span));
            }
            var filterType = InferExpressionType(clause.Filter);
            if (IsDefinitelyNotBool(filterType))
            {
                _diagnostics.ReportError(
                    clause.Filter.Span,
                    DiagnosticCode.TypeMismatch,
                    $"Catch filter must be bool, got {filterType.SurfaceName}");
            }
            _env.ExitScope();
        }
    }

    private void CheckStructuralStatement(Action check)
    {
        if (_lambdaBodyDepth > 0)
            check();
        else
            CheckForFlowCache(check);
    }

    private void CheckForFlowCache(Action check)
    {
        var checkpoint = _diagnostics.CreateCheckpoint();
        var previousLambdaReturnInvalid = _lambdaReturnInvalid;
        var inferredReturnCount = _inferredLambdaReturnTypes?.Count ?? 0;
        var preservedDiagnostics = new List<Diagnostic>();
        _flowCacheDiagnostics.Push(preservedDiagnostics);
        try
        {
            check();
        }
        finally
        {
            _flowCacheDiagnostics.Pop();
        }
        _diagnostics.RestoreCheckpoint(checkpoint);
        foreach (var diagnostic in preservedDiagnostics)
            _diagnostics.Add(diagnostic);
        if (_flowCacheDiagnostics.TryPeek(out var parentDiagnostics))
        {
            foreach (var diagnostic in preservedDiagnostics)
            {
                if (!parentDiagnostics.Contains(diagnostic))
                    parentDiagnostics.Add(diagnostic);
            }
        }
        _lambdaReturnInvalid = previousLambdaReturnInvalid;
        if (_inferredLambdaReturnTypes != null
            && _inferredLambdaReturnTypes.Count > inferredReturnCount)
        {
            _inferredLambdaReturnTypes.RemoveRange(
                inferredReturnCount,
                _inferredLambdaReturnTypes.Count - inferredReturnCount);
        }
    }

    private void ReportControlFlowError(TextSpan span, string message)
    {
        var diagnostic = new Diagnostic(
            DiagnosticCode.TypeMismatch,
            message,
            span,
            filePath: _diagnostics.CurrentFilePath);
        _diagnostics.Add(diagnostic);
    }

    private void PreserveFlowCacheDiagnosticsSince(int diagnosticCount)
    {
        if (!_flowCacheDiagnostics.TryPeek(out var preservedDiagnostics))
            return;
        foreach (var diagnostic in _diagnostics.Skip(diagnosticCount))
        {
            if (!preservedDiagnostics.Contains(diagnostic))
                preservedDiagnostics.Add(diagnostic);
        }
    }

    private void CheckUsingStatement(UsingStatementNode usingStatement)
    {
        var resourceType = InferExpressionType(usingStatement.Resource);
        _env.EnterScope();
        if (usingStatement.VariableName != null)
        {
            var variableType = usingStatement.VariableType != null
                ? ResolveTypeName(usingStatement.VariableType, usingStatement.Span)
                : resourceType;
            _env.DefineVariable(usingStatement.VariableName, variableType);
        }
        foreach (var statement in usingStatement.Body)
            CheckStatement(statement);
        _env.ExitScope();
    }

    private void CheckForeachStatement(ForeachStatementNode foreachStatement)
    {
        _ = InferExpressionType(foreachStatement.Collection);
        _env.EnterScope();
        _env.DefineVariable(
            foreachStatement.VariableName,
            ResolveTypeName(foreachStatement.VariableType, foreachStatement.VariableTypeSpan));
        if (foreachStatement.IndexVariableName != null)
            _env.DefineVariable(foreachStatement.IndexVariableName, PrimitiveType.Int);
        foreach (var statement in foreachStatement.Body)
            CheckStatement(statement);
        _env.ExitScope();
    }

    private void CheckFixedStatement(FixedStatementNode fixedStatement)
    {
        _ = InferExpressionType(fixedStatement.Initializer);
        _env.EnterScope();
        _env.DefineVariable(
            fixedStatement.PointerName,
            ResolveTypeName(fixedStatement.PointerType, fixedStatement.PointerTypeSpan));
        foreach (var statement in fixedStatement.Body)
            CheckStatement(statement);
        _env.ExitScope();
    }

    /// <summary>
    /// #1678: checks both branches of a statement-level <c>§PP</c> (<c>#if</c>) block, each in its
    /// own scope (the branches are alternatives). <c>#if</c> opens no C# scope, so a binding a
    /// branch declares is defined in the enclosing scope afterwards (the first branch's type
    /// wins when both declare it).
    /// </summary>
    private void CheckPreprocessorDirective(PreprocessorDirectiveNode preprocessor)
    {
        var declared = new Dictionary<string, CalorType>(StringComparer.Ordinal);
        foreach (var branch in new[] { preprocessor.Body, preprocessor.ElseBody })
        {
            if (branch is null)
                continue;
            _env.EnterScope();
            foreach (var statement in branch)
                CheckStatement(statement);
            foreach (var (name, type) in _env.CurrentScopeVariables)
                declared.TryAdd(name, type);
            _env.ExitScope();
        }
        foreach (var (name, type) in declared)
            _env.DefineVariable(name, type);
    }

    private void CheckScopedStatements(IReadOnlyList<StatementNode> statements)
    {
        _env.EnterScope();
        foreach (var statement in statements)
            CheckStatement(statement);
        _env.ExitScope();
    }

    private void CheckIfStatement(IfStatementNode ifStmt)
    {
        var condType = InferExpressionType(ifStmt.Condition);
        if (IsDefinitelyNotBool(condType))
        {
            _diagnostics.ReportError(ifStmt.Condition.Span, DiagnosticCode.TypeMismatch,
                $"IF condition must be bool, got {condType.SurfaceName}");
        }

        _env.EnterScope();
        DefineTrueConditionPatternVariables(ifStmt.Condition);
        foreach (var stmt in ifStmt.ThenBody)
        {
            CheckStatement(stmt);
        }
        _env.ExitScope();

        foreach (var elseIf in ifStmt.ElseIfClauses)
        {
            var elseIfCondType = InferExpressionType(elseIf.Condition);
            if (IsDefinitelyNotBool(elseIfCondType))
            {
                _diagnostics.ReportError(elseIf.Condition.Span, DiagnosticCode.TypeMismatch,
                    $"ELSEIF condition must be bool, got {elseIfCondType.SurfaceName}");
            }

            _env.EnterScope();
            DefineTrueConditionPatternVariables(elseIf.Condition);
            foreach (var stmt in elseIf.Body)
            {
                CheckStatement(stmt);
            }
            _env.ExitScope();
        }

        if (ifStmt.ElseBody != null)
        {
            _env.EnterScope();
            foreach (var stmt in ifStmt.ElseBody)
            {
                CheckStatement(stmt);
            }
            _env.ExitScope();
        }
    }

    private void CheckBindStatement(BindStatementNode bind)
    {
        CalorType varType;
        CalorType? initializerType = null;

        if (bind.Initializer != null)
        {
            if (bind.TypeName != null)
            {
                varType = ResolveTypeName(bind.TypeName, bind.Span);
                CalorType initType;
                if (bind.Initializer is ReferenceNode functionReference
                    && TryGetDelegateFunctionType(varType, out _)
                    && _env.LookupFunctionCandidates(functionReference.Name) is { Count: > 0 })
                {
                    initType = varType;
                    if (!IsFunctionReferenceAssignable(varType, functionReference.Name))
                    {
                        _diagnostics.ReportError(bind.Span, DiagnosticCode.TypeMismatch,
                            $"Method group '{functionReference.Name}' is not compatible with {varType.SurfaceName}");
                    }
                }
                else
                {
                    initType = InferExpressionType(bind.Initializer, varType);
                }
                initializerType = initType;
                if (IsDistinctNamedRefinement(varType, initType))
                {
                    ReportDistinctNamedRefinement(bind.Span, varType, initType, $"the binding of '{bind.Name}'");
                }
                else if (ClassifyImplicitNumericConversion(EraseRefinement(varType), initType) == false)
                {
                    ReportNumericConversion(bind.Initializer.Span, EraseRefinement(varType), initType,
                        $"the binding of '{bind.Name}'");
                }
                else if (!IsAssignable(varType, initType)
                    && !IsBinderOwnedNullabilityMismatch(varType, initType)
                    && _boundGenericReceivingFailures?.Contains(bind.Initializer.Span) != true)
                {
                    _diagnostics.ReportError(bind.Span, DiagnosticCode.TypeMismatch,
                        $"Cannot assign {initType.SurfaceName} to variable of type {varType.SurfaceName}");
                }
            }
            else
            {
                var initType = InferExpressionType(bind.Initializer);
                initializerType = initType;
                varType = WithoutIntegerConstant(initType);
                // #1529: the inferred local keeps the named refinement; record it so the emitter
                // and obligation generator guard every later write exactly as for §B{x:Nat}.
                // Speculative passes are rolled back (BeginSpeculation), so the last committed
                // inference of this bind decides.
                if (initType is RefinedType { RefinementName: { } refinementName })
                    _inferredRefinements[bind] = refinementName;
                else
                    _inferredRefinements.Remove(bind);
            }
        }
        else if (bind.TypeName != null)
        {
            varType = ResolveTypeName(bind.TypeName, bind.Span);
        }
        else
        {
            // Deliberately silent. `BindValidationPass` owns this condition and reports it as
            // Calor0250 (BindRequiresTypeOrInitializer) — the specific, documented code that
            // carries a quickfix. Reporting it here too meant that turning the checker on
            // REPLACED a precise diagnostic with a vaguer one (Calor0202 TypeMismatch), so the
            // same program produced different codes depending on a flag.
            varType = ErrorType.Instance;
        }

        // A mutable §B of a name already in scope is a reassignment, not a new variable (the
        // emitter emits `x = …`, and keeps the established refinement contract for later writes).
        // Changing a named refinement that way would leave the checker and the runtime guards
        // enforcing different predicates, so it is rejected.
        //
        // The emitter keeps an established refinement contract for every later write, even when
        // the rebind is annotated with the base type (`§B{~x:i32} v` after `§B{~x} a`, a: Nat).
        // So the checker keeps the established refinement as the variable's type too; otherwise
        // a later `§B{~x} p` (p: Pos) would look like a first refinement (#1530 review round 2).
        var existingType = bind.IsMutable ? _env.LookupVariableAbove(bind.Name, _memberScopeDepth) : null;
        if (existingType is RefinedType { RefinementName: not null } established)
        {
            // Check the annotation AND the initializer: `§B{~x:i32} p` (p: Pos) still writes a
            // Pos into the established Nat variable.
            var rebound = varType is RefinedType { RefinementName: not null } annotatedRefinement
                    && !established.Equals(annotatedRefinement)
                ? annotatedRefinement
                : initializerType is RefinedType { RefinementName: not null } initializerRefinement
                    && !established.Equals(initializerRefinement)
                    ? initializerRefinement
                    : null;
            if (rebound != null)
            {
                ReportRefinementSoundnessError(bind.Span,
                    $"Cannot rebind '{bind.Name}' of type {established.SurfaceName} as {rebound.SurfaceName}: "
                    + "a mutable rebind reassigns the existing variable and cannot change its named refinement.");
            }
            varType = established;
        }
        else if (existingType != null
            && varType is RefinedType { RefinementName: not null } added
            && _inlineRefinedParameters.Contains(bind.Name))
        {
            // The emitter keeps a parameter's inline refinement as the established contract, so
            // it would never enforce the named refinement the checker would now claim.
            ReportRefinementSoundnessError(bind.Span,
                $"Cannot rebind parameter '{bind.Name}', which has an inline refinement, as {added.SurfaceName}: "
                + "a mutable rebind reassigns the parameter and keeps its inline refinement.");
            varType = existingType;
        }

        if (!bind.IsMutable)
            _inlineRefinedParameters.Remove(bind.Name);
        _env.DefineVariable(bind.Name, varType);
    }

    private void CheckMatchStatement(MatchStatementNode match)
    {
        var targetType = InferExpressionType(match.Target);
        _matchTargetTypes[match] = targetType;

        foreach (var matchCase in match.Cases)
        {
            _env.EnterScope();
            CheckPattern(matchCase.Pattern, targetType);

            if (matchCase.Guard != null)
            {
                var guardType = InferExpressionType(matchCase.Guard);
                DefineTrueConditionPatternVariables(matchCase.Guard);
                if (IsDefinitelyNotBool(guardType))
                {
                    _diagnostics.ReportError(matchCase.Guard.Span, DiagnosticCode.TypeMismatch,
                        $"Match guard must be bool, got {guardType.SurfaceName}");
                }
            }

            foreach (var stmt in matchCase.Body)
            {
                CheckStatement(stmt);
            }

            _env.ExitScope();
        }
    }

    private void CheckCollectionPushStatement(CollectionPushNode push)
    {
        var collectionType = _env.LookupVariable(push.CollectionName);
        if (collectionType == null)
        {
            _diagnostics.ReportError(push.Span, DiagnosticCode.UndefinedReference,
                $"Undefined collection '{push.CollectionName}'");
            return;
        }

        var valueType = InferExpressionType(push.Value);

        // Check if it's a List<T> or HashSet<T>
        if (collectionType is GenericInstanceType git)
        {
            if ((git.BaseName == "List" || git.BaseName == "HashSet") && git.TypeArguments.Count == 1)
            {
                var elementType = git.TypeArguments[0];
                if (!IsAssignable(elementType, valueType))
                {
                    _diagnostics.ReportError(push.Value.Span, DiagnosticCode.TypeMismatch,
                        $"Cannot add {valueType.SurfaceName} to {collectionType.SurfaceName}, expected {elementType.SurfaceName}");
                }
            }
            else
            {
                // Reachable only for a KNOWN generic that is not a List/HashSet, so no guard is
                // needed here — a GenericInstanceType is never ErrorType or ExternalType. An
                // earlier revision put the guard on this branch, where it was dead code, and left
                // the outer `else` below — the one that actually sees unmodeled receivers —
                // unprotected.
                _diagnostics.ReportError(push.Span, DiagnosticCode.TypeMismatch,
                    $"PUSH operation requires List or HashSet, got {collectionType.SurfaceName}");
            }
        }
        else if (IsKnownNonCollection(collectionType))
        {
            _diagnostics.ReportError(push.Span, DiagnosticCode.TypeMismatch,
                $"PUSH operation requires a collection type, got {collectionType.SurfaceName}");
        }
    }

    private void CheckDictionaryPutStatement(DictionaryPutNode put)
    {
        var dictType = _env.LookupVariable(put.DictionaryName);
        if (dictType == null)
        {
            _diagnostics.ReportError(put.Span, DiagnosticCode.UndefinedReference,
                $"Undefined dictionary '{put.DictionaryName}'");
            return;
        }

        var keyType = InferExpressionType(put.Key);
        var valueType = InferExpressionType(put.Value);

        if (dictType is GenericInstanceType git && git.BaseName == "Dictionary" && git.TypeArguments.Count == 2)
        {
            var expectedKeyType = git.TypeArguments[0];
            var expectedValueType = git.TypeArguments[1];

            if (!IsAssignable(expectedKeyType, keyType))
            {
                _diagnostics.ReportError(put.Key.Span, DiagnosticCode.TypeMismatch,
                    $"Dictionary key type mismatch: expected {expectedKeyType.SurfaceName}, got {keyType.SurfaceName}");
            }

            if (!IsAssignable(expectedValueType, valueType))
            {
                _diagnostics.ReportError(put.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Dictionary value type mismatch: expected {expectedValueType.SurfaceName}, got {valueType.SurfaceName}");
            }
        }
        else if (IsKnownNonCollection(dictType))
        {
            _diagnostics.ReportError(put.Span, DiagnosticCode.TypeMismatch,
                $"PUT operation requires a Dictionary, got {dictType?.SurfaceName ?? "unknown"}");
        }
    }

    private void CheckCollectionRemoveStatement(CollectionRemoveNode remove)
    {
        var collectionType = _env.LookupVariable(remove.CollectionName);
        if (collectionType == null)
        {
            _diagnostics.ReportError(remove.Span, DiagnosticCode.UndefinedReference,
                $"Undefined collection '{remove.CollectionName}'");
            return;
        }

        var removeType = InferExpressionType(remove.KeyOrValue);

        if (collectionType is GenericInstanceType git)
        {
            CalorType? expectedType = null;

            if ((git.BaseName == "List" || git.BaseName == "HashSet") && git.TypeArguments.Count == 1)
            {
                expectedType = git.TypeArguments[0];
            }
            else if (git.BaseName == "Dictionary" && git.TypeArguments.Count == 2)
            {
                expectedType = git.TypeArguments[0]; // Remove by key
            }

            if (expectedType != null && !IsAssignable(expectedType, removeType))
            {
                _diagnostics.ReportError(remove.KeyOrValue.Span, DiagnosticCode.TypeMismatch,
                    $"Cannot remove {removeType.SurfaceName} from {collectionType.SurfaceName}, expected {expectedType.SurfaceName}");
            }
        }
        else if (IsKnownNonCollection(collectionType) && collectionType is not ArrayType)
        {
            _diagnostics.ReportError(remove.Span, DiagnosticCode.TypeMismatch,
                $"REM operation requires a collection type, got {collectionType.SurfaceName}");
        }
    }

    private void CheckCollectionSetIndexStatement(CollectionSetIndexNode setIndex)
    {
        var collectionType = _env.LookupVariable(setIndex.CollectionName);
        if (collectionType == null)
        {
            _diagnostics.ReportError(setIndex.Span, DiagnosticCode.UndefinedReference,
                $"Undefined collection '{setIndex.CollectionName}'");
            return;
        }

        var indexType = InferExpressionType(setIndex.Index);
        var valueType = InferExpressionType(setIndex.Value);

        // Index must be numeric
        if (!IsNumeric(indexType))
        {
            _diagnostics.ReportError(setIndex.Index.Span, DiagnosticCode.TypeMismatch,
                $"List index must be numeric, got {indexType.SurfaceName}");
        }

        if (collectionType is GenericInstanceType git && git.BaseName == "List" && git.TypeArguments.Count == 1)
        {
            var elementType = git.TypeArguments[0];
            if (!IsAssignable(elementType, valueType))
            {
                _diagnostics.ReportError(setIndex.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Cannot assign {valueType.SurfaceName} to list element of type {elementType.SurfaceName}");
            }
        }
        else if (collectionType is ArrayType setArray)
        {
            // Arrays are indexable too. Before arrays resolved at all this branch was unreachable;
            // making them resolve without teaching SETIDX about them turned a working program —
            // including two agent-native benchmark GOLD references — into a hard error.
            if (!IsAssignable(setArray.ElementType, valueType))
            {
                _diagnostics.ReportError(setIndex.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Cannot assign {valueType.SurfaceName} to array element of type {setArray.ElementType.SurfaceName}");
            }
        }
        else if (IsKnownNonCollection(collectionType))
        {
            _diagnostics.ReportError(setIndex.Span, DiagnosticCode.TypeMismatch,
                $"SETIDX operation requires a List or array, got {collectionType.SurfaceName}");
        }
    }

    private void CheckCollectionClearStatement(CollectionClearNode clear)
    {
        var collectionType = _env.LookupVariable(clear.CollectionName);
        if (collectionType == null)
        {
            _diagnostics.ReportError(clear.Span, DiagnosticCode.UndefinedReference,
                $"Undefined collection '{clear.CollectionName}'");
            return;
        }

        // Clear works on any collection type
        if ((collectionType is not GenericInstanceType git ||
             (git.BaseName != "List" && git.BaseName != "Dictionary" && git.BaseName != "HashSet"))
            && IsKnownNonCollection(collectionType))
        {
            _diagnostics.ReportError(clear.Span, DiagnosticCode.TypeMismatch,
                $"CLR operation requires a collection type, got {collectionType.SurfaceName}");
        }
    }

    private void CheckCollectionInsertStatement(CollectionInsertNode insert)
    {
        var collectionType = _env.LookupVariable(insert.CollectionName);
        if (collectionType == null)
        {
            _diagnostics.ReportError(insert.Span, DiagnosticCode.UndefinedReference,
                $"Undefined collection '{insert.CollectionName}'");
            return;
        }

        var indexType = InferExpressionType(insert.Index);
        var valueType = InferExpressionType(insert.Value);

        // Index must be numeric
        if (!IsNumeric(indexType))
        {
            _diagnostics.ReportError(insert.Index.Span, DiagnosticCode.TypeMismatch,
                $"List index must be numeric, got {indexType.SurfaceName}");
        }

        if (collectionType is GenericInstanceType git && git.BaseName == "List" && git.TypeArguments.Count == 1)
        {
            var elementType = git.TypeArguments[0];
            if (!IsAssignable(elementType, valueType))
            {
                _diagnostics.ReportError(insert.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Cannot insert {valueType.SurfaceName} into list of type {elementType.SurfaceName}");
            }
        }
        else if (IsKnownNonCollection(collectionType) && collectionType is not ArrayType)
        {
            // Arrays have no INS — fixed length — but an unmodeled receiver must stay silent.
            _diagnostics.ReportError(insert.Span, DiagnosticCode.TypeMismatch,
                $"INS operation requires a List, got {collectionType.SurfaceName}");
        }
    }

    private void CheckDictionaryForeachStatement(DictionaryForeachNode dictForeach)
    {
        var dictType = InferExpressionType(dictForeach.Dictionary);

        _env.EnterScope();

        if (dictType is GenericInstanceType git && git.BaseName == "Dictionary" && git.TypeArguments.Count == 2)
        {
            var keyType = git.TypeArguments[0];
            var valueType = git.TypeArguments[1];

            // Define loop variables with their types
            _env.DefineVariable(dictForeach.KeyName, keyType);
            _env.DefineVariable(dictForeach.ValueName, valueType);
        }
        else if (dictType is not (GenericInstanceType or ArrayType) && IsKnownNonCollection(dictType))
        {
            _diagnostics.ReportError(dictForeach.Dictionary.Span, DiagnosticCode.TypeMismatch,
                $"Two-name each requires a deconstructable enumerable, got {dictType.SurfaceName}");

            // Define variables with error type to allow body checking to continue
            _env.DefineVariable(dictForeach.KeyName, ErrorType.Instance);
            _env.DefineVariable(dictForeach.ValueName, ErrorType.Instance);
        }
        else
        {
            // Tuple/custom Deconstruct signatures belong to the generated-C#
            // validation boundary, just like other external type members.
            _env.DefineVariable(dictForeach.KeyName, new ExternalType("foreach-deconstruction"));
            _env.DefineVariable(dictForeach.ValueName, new ExternalType("foreach-deconstruction"));
        }

        // Check body statements
        foreach (var stmt in dictForeach.Body)
        {
            CheckStatement(stmt);
        }

        _env.ExitScope();
    }

    private void CheckPattern(PatternNode pattern, CalorType expectedType, bool bindVariables = true)
    {
        switch (pattern)
        {
            case WildcardPatternNode:
                // Wildcard matches anything
                break;

            case VariablePatternNode varPat:
                if (bindVariables)
                {
                    _env.DefineVariable(varPat.Name, expectedType);
                }
                break;

            case LiteralPatternNode litPat:
                var litType = InferExpressionType(litPat.Literal);
                if (!IsAssignable(expectedType, litType))
                {
                    _diagnostics.ReportError(litPat.Span, DiagnosticCode.TypeMismatch,
                        $"Pattern literal type {litType.SurfaceName} does not match expected type {expectedType.SurfaceName}");
                }
                break;

            // `§VAR{d}` — the `var d` pattern. Binds like VariablePatternNode; it reached the
            // default arm below and hard-errored, so every switch arm using it was rejected.
            case VarPatternNode varPatNode:
                if (bindVariables && varPatNode.Name != "_")
                {
                    _env.DefineVariable(varPatNode.Name, expectedType);
                }
                break;

            // `§K{Type:name}` — a type test with an optional binding. The bound name takes the
            // tested type, which the checker may not model; ExternalType is the honest answer.
            case TypePatternNode typePat:
                var typePatternType = InferTypePatternBindingType(typePat.TypeName, typePat.TypeNameSpan, typePat.Span);
                if (bindVariables && !string.IsNullOrEmpty(typePat.BindingName))
                {
                    _env.DefineVariable(typePat.BindingName!, typePatternType);
                }
                break;

            // Composites: recurse so nested bindings land in scope.
            case AndPatternNode andPat:
                CheckPattern(andPat.Left, expectedType, bindVariables);
                CheckPattern(andPat.Right, andPat.Left is TypePatternNode typed
                    ? InferTypePatternBindingType(typed.TypeName, typed.TypeNameSpan, typed.Span)
                    : expectedType, bindVariables);
                break;

            case PropertyPatternNode property:
                var receiverType = property.TypeName is { } testedType
                    ? InferTypePatternBindingType(testedType, property.TypeNameSpan ?? property.Span, property.Span)
                    : expectedType;
                foreach (var member in property.Matches)
                {
                    if (_module != null && Binding.TypeIdentity.TryResolveNativeInstanceMemberType(
                        _module, receiverType.SurfaceName, member.PropertyName, out var memberName))
                    {
                        CheckPattern(member.Pattern, ResolveTypeName(memberName, member.Span), bindVariables);
                    }
                    else
                    {
                        _diagnostics.ReportError(member.Span, DiagnosticCode.UndefinedReference,
                            $"Cannot resolve native property-pattern member '{member.PropertyName}' on type '{receiverType.SurfaceName}'");
                    }
                }
                break;

            case OrPatternNode orPat:
                CheckPattern(orPat.Left, expectedType, bindVariables: false);
                CheckPattern(orPat.Right, expectedType, bindVariables: false);
                break;

            case NegatedPatternNode negPat:
                CheckPattern(negPat.Inner, expectedType, bindVariables: false);
                break;

            case ListPatternNode listPat:
                var elementType = expectedType is ArrayType at ? at.ElementType
                    : expectedType is GenericInstanceType { BaseName: "List", TypeArguments.Count: 1 } lg
                        ? lg.TypeArguments[0]
                        : ErrorType.Instance;
                foreach (var sub in listPat.Patterns)
                {
                    CheckPattern(sub, elementType, bindVariables);
                }
                if (listPat.SlicePattern != null)
                    CheckPattern(listPat.SlicePattern, expectedType is ArrayType ? expectedType : ErrorType.Instance,
                        bindVariables);
                break;

            default:
                // Silent. Some pattern kinds — relational, positional, constant, is — remain unimplemented
                // here. Reporting "Unsupported pattern type" made the CHECKER's gap the user's
                // error, and once the checker runs by default that is a hard error on any program
                // using one. Any name a pattern binds and this arm misses will surface as a normal
                // "Undefined variable" at the use site, which is the correct place for it.
                break;
        }
    }

    private CalorType InferExpressionType(ExpressionNode expr, CalorType? expectedType = null)
    {
        if (_boundSourceEnumConstants?.TryGetValue(expr, out var constant) == true)
            return new ExternalType(constant.Type.DisplayString);
        if (_boundSourceStaticFieldReads?.TryGetValue(expr, out var selectedStaticField) == true)
        {
            var fieldType = ResolveTypeName(selectedStaticField.Type.DisplayString, expr.Span);
            return selectedStaticField.SourceStaticFieldOwner?.Fields.Any(field =>
                    field.Name == selectedStaticField.FieldName
                    && field.Modifiers.HasFlag(MethodModifiers.Const)) == true
                ? AsIntegerConstant(fieldType, null)
                : fieldType;
        }
        if (_boundMetadataConstants?.TryGetValue(expr, out var metadataConstant) == true)
            return AsIntegerConstant(
                ResolveTypeName(metadataConstant.Type.DisplayString, expr.Span),
                metadataConstant.ResolvedMetadataMember is Microsoft.CodeAnalysis.IFieldSymbol
                    { HasConstantValue: true, ConstantValue: { } constantValue }
                    ? constantValue
                    : null);
        if (_boundMetadataStaticFieldReads?.TryGetValue(expr, out var metadataStaticField) == true)
            return ResolveTypeName(metadataStaticField.Type.DisplayString, expr.Span);
        return expr switch
        {
            IntLiteralNode literal => IntegerLiteralType(literal),
            FloatLiteralNode literal => literal.IsDecimal
                ? PrimitiveType.Decimal
                : PrimitiveType.ExactNumeric(literal.IsSingle ? "f32" : "f64"),
            DecimalLiteralNode => PrimitiveType.Decimal,
            BoolLiteralNode => PrimitiveType.Bool,
            StringLiteralNode or NameOfExpressionNode => PrimitiveType.String,
            NullCoalesceNode coalesce => InferNullCoalesceType(coalesce),
            ConditionalExpressionNode conditional => InferConditionalExpressionType(conditional),
            ThrowExpressionNode throwExpression => InferThrowExpressionType(throwExpression),
            CharOperationNode operation => operation.Operation switch
            {
                CharOp.IsLetter or CharOp.IsDigit or CharOp.IsWhiteSpace
                    or CharOp.IsUpper or CharOp.IsLower => PrimitiveType.Bool,
                CharOp.CharCode => PrimitiveType.Int,
                _ => PrimitiveType.Char
            },
            ReferenceNode refNode => InferReferenceType(refNode),
            UnaryOperationNode unary => InferUnaryOperationType(unary),
            OverflowContextExpressionNode overflow => InferExpressionType(overflow.Operand, expectedType),
            BinaryOperationNode binOp => InferBinaryOperationType(binOp),
            RecordCreationNode rec => InferRecordCreationType(rec),
            FieldAccessNode field => InferFieldAccessType(field),
            MatchExpressionNode match => InferMatchExpressionType(match, expectedType),
            NewExpressionNode newExpression => InferNewExpressionType(newExpression),
            CallExpressionNode call => InferCallExpressionType(call),
            ExpressionCallNode call => InferExpressionCallType(call),
            AwaitExpressionNode awaited => InferAwaitType(awaited),
            InterpolatedStringNode interpolated => InferInterpolatedStringType(interpolated),
            LambdaExpressionNode lambda => InferLambdaType(lambda, expectedType),
            ForallExpressionNode forall => InferQuantifierType(forall.BoundVariables, forall.Body),
            ExistsExpressionNode exists => InferQuantifierType(exists.BoundVariables, exists.Body),
            // Collection expression types
            ListCreationNode list => InferListCreationType(list),
            DictionaryCreationNode dict => InferDictionaryCreationType(dict),
            SetCreationNode set => InferSetCreationType(set),
            CollectionContainsNode contains => InferCollectionContainsType(contains),
            CollectionCountNode count => InferCollectionCountType(count),
            ArrayAccessNode arrayAccess => InferArrayAccessType(arrayAccess),
            MultiDimArrayAccessNode arrayAccess => InferMultiDimArrayAccessType(arrayAccess),
            TypeOperationNode typeOp => InferTypeOperationType(typeOp),
            IsPatternNode isPattern => InferIsPatternType(isPattern),
            _ => InferUnknownExpressionType(expr)
        };
    }

    /// <summary>
    /// An integer literal has the C# type the emitter gives it (an unsuffixed literal outside
    /// <c>int</c> is emitted with <c>L</c>), and is a constant: <c>let byte:b = 200</c> is the
    /// implicit constant conversion C# accepts, while <c>let byte:b = 300</c> is CS0031.
    /// </summary>
    private static PrimitiveType IntegerLiteralType(IntLiteralNode literal)
    {
        var width = literal.IsUnsigned
            ? literal.IsLong ? "u64" : "u32"
            : literal.IsLong ? "i64" : "i32";
        Int128 value = literal.IsUnsigned || literal.Sign == IntegerLiteralSign.Positive
            ? literal.Magnitude
            : -(Int128)literal.Magnitude;
        return PrimitiveType.ExactNumeric(width).AsIntegerConstant(value);
    }

    /// <summary>
    /// Marks an integer-typed value as a constant expression. <paramref name="value"/> is the
    /// constant when known (an integral <see cref="object"/> from metadata), else unknown.
    /// </summary>
    private static CalorType AsIntegerConstant(CalorType type, object? value)
    {
        if (type is not PrimitiveType primitive || !primitive.Equals(PrimitiveType.Int))
            return type;
        Int128? known = value switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            _ => null,
        };
        return primitive.AsIntegerConstant(known);
    }

    /// <summary>A local initialized by a constant is not itself a constant in the emitted C#.</summary>
    private static CalorType WithoutIntegerConstant(CalorType type)
        => type is PrimitiveType primitive ? primitive.WithoutIntegerConstant() : type;

    private IEnumerable<string> AsyncCarrierTypeParameters()
        => new[] { "Task", "ValueTask" }.Where(name => _env.LookupType(name) is TypeParameterType);

    private CalorType InferAwaitType(AwaitExpressionNode awaited)
    {
        var carrier = InferExpressionType(awaited.Awaited);
        if (_boundAwaits?.TryGetValue(awaited, out var bound) == true
            && !bound.HasUnknownInferredType
            && bound.Metadata["GetResult"] is Microsoft.CodeAnalysis.IMethodSymbol)
            return ResolveTypeName(bound.Type.DisplayString, awaited.Span);
        if (AttributeHelper.TryGetAsyncResultType(carrier.SurfaceName, _module, out var result))
            return ResolveTypeName(result, awaited.Span);
        if (carrier is ErrorType or ExternalType)
        {
            if (!_suppressDiagnostics && !_suppressContextualDiagnostics)
                _diagnostics.ReportWarning(awaited.Span, DiagnosticCode.UndefinedReference,
                    "The awaited result requires .NET awaiter resolution; generated C# validation checks this expression.");
            return carrier;
        }
        _diagnostics.ReportError(awaited.Span, DiagnosticCode.TypeMismatch,
            $"Type {carrier.SurfaceName} is not a supported awaitable.");
        return ErrorType.Instance;
    }

    private CalorType InferNullCoalesceType(NullCoalesceNode coalesce)
    {
        var leftType = InferExpressionType(coalesce.Left);
        var rightType = InferExpressionType(coalesce.Right);

        if (leftType is NeverType)
        {
            return NeverType.Instance;
        }

        if (leftType is NullType)
        {
            return rightType is NeverType ? NeverType.Instance : rightType;
        }

        if (IsPrimitiveValueType(leftType)
            || leftType.Equals(PrimitiveType.Void) || leftType.Equals(PrimitiveType.Unit))
        {
            _diagnostics.ReportError(coalesce.Left.Span, DiagnosticCode.TypeMismatch,
                $"Null-coalescing requires a reference or nullable value operand, got {leftType.SurfaceName}");
            return ErrorType.Instance;
        }

        if (rightType is ErrorType)
            return ErrorType.Instance;

        if (leftType is NullableReferenceType nullableReference)
        {
            if (rightType is NeverType)
            {
                return nullableReference.ReferentType;
            }

            if (rightType is NullType)
            {
                return nullableReference;
            }

            if (rightType is NullableReferenceType rightNullable
                && IsAssignable(nullableReference.ReferentType, rightNullable.ReferentType))
            {
                return new NullableReferenceType(
                    nullableReference.ReferentType,
                    nullableReference.RequiresTransitionalAssignmentCheck
                        || rightNullable.RequiresTransitionalAssignmentCheck);
            }

            if (IsAssignable(nullableReference.ReferentType, rightType))
            {
                return nullableReference.ReferentType;
            }

            if (rightType is not ErrorType)
            {
                _diagnostics.ReportError(coalesce.Right.Span, DiagnosticCode.TypeMismatch,
                    $"Null-coalescing fallback type {rightType.SurfaceName} is not assignable to {nullableReference.ReferentType.SurfaceName}");
            }
            return ErrorType.Instance;
        }

        if (leftType is NullableValueType nullableValue)
        {
            if (rightType is NeverType)
            {
                return nullableValue.UnderlyingType;
            }

            if (rightType is NullType)
            {
                return nullableValue;
            }

            if (rightType is NullableValueType rightNullable
                && IsAssignable(nullableValue.UnderlyingType, rightNullable.UnderlyingType))
            {
                return nullableValue;
            }
            if (rightType is NullableValueType widerNullable
                && IsAssignable(widerNullable.UnderlyingType, nullableValue.UnderlyingType))
            {
                return widerNullable;
            }

            if (IsAssignable(nullableValue.UnderlyingType, rightType))
            {
                return nullableValue.UnderlyingType;
            }
            if (IsAssignable(rightType, nullableValue.UnderlyingType))
            {
                return rightType;
            }

            if (rightType is not ErrorType)
            {
                _diagnostics.ReportError(coalesce.Right.Span, DiagnosticCode.TypeMismatch,
                    $"Null-coalescing fallback type {rightType.SurfaceName} is not assignable to {nullableValue.UnderlyingType.SurfaceName}");
            }
            return ErrorType.Instance;
        }

        if (leftType is ErrorType || leftType is ExternalType)
        {
            return ErrorType.Instance;
        }

        if (IsObliviousReferenceType(leftType))
        {
            if (rightType is NeverType or NullType)
            {
                return leftType;
            }

            if (rightType is ErrorType)
            {
                return ErrorType.Instance;
            }

            if (IsAssignable(leftType, rightType) || IsBinderOwnedNullabilityMismatch(leftType, rightType))
            {
                return leftType;
            }

            if (IsAssignable(rightType, leftType))
            {
                return rightType;
            }

            _diagnostics.ReportError(coalesce.Right.Span, DiagnosticCode.TypeMismatch,
                $"Null-coalescing fallback type {rightType.SurfaceName} is not assignable to {leftType.SurfaceName}");
            return ErrorType.Instance;
        }

        return ErrorType.Instance;
    }

    private CalorType InferConditionalExpressionType(ConditionalExpressionNode conditional)
    {
        var conditionType = InferExpressionType(conditional.Condition);
        if (IsDefinitelyNotBool(conditionType))
        {
            _diagnostics.ReportError(conditional.Condition.Span, DiagnosticCode.TypeMismatch,
                $"Conditional expression condition must be bool, got {conditionType.SurfaceName}");
        }

        _env.EnterScope();
        DefineTrueConditionPatternVariables(conditional.Condition);
        var trueType = InferExpressionType(conditional.WhenTrue);
        _env.ExitScope();

        var falseType = InferExpressionType(conditional.WhenFalse);
        return CommonConditionalType(conditional.Span, trueType, falseType);
    }

    private CalorType InferThrowExpressionType(ThrowExpressionNode throwExpression)
    {
        var exceptionType = InferExpressionType(throwExpression.Exception);
        if (throwExpression.Exception is NewExpressionNode constructed
            && PrimitiveType.FromName(Parsing.AttributeHelper.ToSurfaceSpelling(constructed.TypeName)) is { } primitive)
            exceptionType = primitive;
        if (!IsSupportedThrowException(throwExpression.Exception, exceptionType))
        {
            _diagnostics.ReportError(throwExpression.Exception.Span, DiagnosticCode.TypeMismatch,
                $"Throw expression requires an exception value, got {exceptionType.SurfaceName}");
        }
        return NeverType.Instance;
    }

    private CalorType InferUnaryOperationType(UnaryOperationNode unary)
    {
        // A named refinement (§RTYPE) erases to its base type for operators, exactly as an inline
        // refinement does (an inline-refined parameter is typed as its base). `-` and `~` yield
        // the BASE type: a refinement does not survive arithmetic. #1515.
        //
        // Increment/decrement of a plain VARIABLE are the exception and keep its declared type.
        // They write back into the refined variable, and that write is guarded (Subtype
        // obligation + runtime check) before it commits, so the value they yield satisfies the
        // refinement. Before #1515 they typed as the error type, so `§B{x:Nat} (inc a)` compiled;
        // it still does. Any other operand (`§IDX` element, field, ...) has no mutation guard —
        // both the emitter and ObligationGenerator only guard a ReferenceNode — so its result
        // gets the base type like any other arithmetic.
        var declaredOperandType = InferExpressionType(unary.Operand);
        var operandType = EraseRefinement(declaredOperandType);
        var incDecResultType = unary.Operand is ReferenceNode ? declaredOperandType : operandType;
        return unary.Operator switch
        {
            UnaryOperator.Not => PrimitiveType.Bool,
            UnaryOperator.Negate => IsNumericType(operandType) ? PromoteUnaryOperand(operandType, negate: true) : ErrorType.Instance,
            UnaryOperator.BitwiseNot => operandType.Equals(PrimitiveType.Int) ? PromoteUnaryOperand(operandType, negate: false) : ErrorType.Instance,
            UnaryOperator.PreIncrement or UnaryOperator.PreDecrement
                or UnaryOperator.PostIncrement or UnaryOperator.PostDecrement
                => IsNumericType(operandType) ? incDecResultType : ErrorType.Instance,
            _ => ErrorType.Instance
        };
    }

    private CalorType InferNewExpressionType(NewExpressionNode newExpression)
    {
        foreach (var argument in newExpression.Arguments)
        {
            InferExpressionType(argument);
        }

        foreach (var initializer in newExpression.Initializers)
        {
            InferExpressionType(initializer.Value);
        }
        foreach (var argument in newExpression.CollectionInitializers.SelectMany(entry => entry.Arguments))
            InferExpressionType(argument);

        return _env.LookupType(newExpression.TypeName) ?? new ExternalType(newExpression.TypeName);
    }

    private CalorType InferCallExpressionType(CallExpressionNode call)
    {
        var functionCandidates = _env.LookupFunctionCandidates(call.Target);
        if (_env.LookupVariable(call.Target) is { } variableType
            && TryGetDelegateFunctionType(variableType, out var variableFunction))
        {
            var delegateArgumentTypes = InferDelegateArgumentTypes(
                variableFunction, call.Arguments, call.ArgumentNames);
            return ValidateDelegateCall(
                    variableFunction,
                    delegateArgumentTypes,
                    call.Arguments,
                    call.ArgumentNames,
                    call.ArgumentModifiers,
                    call.Span)
                ? variableFunction.ReturnType
                : ErrorType.Instance;
        }

        // #943 (RO-DECL-2): `§A{out} _` with no `_` in scope is a discard, typed by the parameter.
        var preliminaryTypes = call.Arguments
            .Select((argument, index) => RequiresTargetType(argument)
                    || argument is ReferenceNode { Name: "_", IsVerbatim: false }
                        && !string.IsNullOrEmpty(call.ArgumentModifiers?.ElementAtOrDefault(index))
                        && _env.LookupVariable("_") == null
                ? ErrorType.Instance
                : InferExpressionType(argument))
            .ToArray();
        var contextualCandidates = functionCandidates
            .Where(candidate => TryResolveKnownArgumentTypes(
                candidate, call, preliminaryTypes, out _, out _))
            .ToArray();
        var expectedArgumentTypes = call.Arguments
            .Select((_, index) => GetConsensusExpectedArgumentType(
                contextualCandidates, call, preliminaryTypes, index))
            .ToArray();
        var argumentTypes = call.Arguments.Select((argument, index) =>
            RequiresTargetType(argument)
                ? InferInitialContextualArgument(
                    argument, expectedArgumentTypes[index], contextualCandidates.Length > 0)
                : preliminaryTypes[index]).ToArray();
        var candidates = functionCandidates
            .Select(candidate => TryResolveCallCandidate(candidate, call, argumentTypes))
            .Where(candidate => candidate != null)
            .Select(candidate => candidate!)
            .ToArray();
        var reportedNominal = candidates.Length == 0 && functionCandidates.Count > 0
            && argumentTypes.Any(type => type is RefinedType { RefinementName: not null })
            && ReportDistinctNamedRefinementArguments(call, functionCandidates, argumentTypes);
        if (!reportedNominal && candidates.Length == 0 && functionCandidates.Count > 0
            && (call.Arguments.Any(RequiresTargetType)
                || call.Arguments.Any(argument => argument is ReferenceNode reference
                    && _env.LookupFunctionCandidates(reference.Name).Count > 0)
                || call.ArgumentNames?.Any(name => !string.IsNullOrEmpty(name)) == true
                || call.ArgumentModifiers?.Any(modifier => !string.IsNullOrEmpty(modifier)) == true))
        {
            _diagnostics.ReportError(call.Span, DiagnosticCode.NoMatchingOverload,
                $"No overload of '{call.Target}' matches the supplied argument names, modifiers, and types");
        }
        if (!reportedNominal && candidates.Length == 0)
            ReportNumericArgumentConversions(call, functionCandidates, argumentTypes);
        var bestCandidates = candidates
            .Where(candidate => !candidates.Any(other =>
                !ReferenceEquals(candidate, other) && IsBetterConversion(other, candidate)))
            .ToArray();
        if (bestCandidates.Length == 1)
            ValidateSelectedContextualArguments(call, bestCandidates[0], expectedArgumentTypes);
        return bestCandidates.Length > 0
            && bestCandidates.All(candidate => candidate.Type.ReturnType.Equals(bestCandidates[0].Type.ReturnType))
                ? bestCandidates[0].Type.ReturnType
                : ErrorType.Instance;
    }

    /// <summary>
    /// A call to the only function of its name, positional and by value, whose arguments fail
    /// to convert: each argument C# would reject as a numeric conversion between two known types
    /// is reported at the argument (<c>Scale(someDouble)</c> into a <c>float</c> parameter). Other
    /// mismatches, overload sets and generic functions stay with C#, as before.
    /// </summary>
    private void ReportNumericArgumentConversions(
        CallExpressionNode call, IReadOnlyList<FunctionCandidate> functionCandidates, IReadOnlyList<CalorType> argumentTypes)
    {
        if (functionCandidates.Count != 1 || _suppressContextualDiagnostics)
            return;
        var candidate = functionCandidates[0];
        var parameterTypes = candidate.Type.ParameterTypes;
        if (candidate.TypeParameterNames.Count != 0
            || parameterTypes.Count != call.Arguments.Count
            || candidate.ParameterModifiers.Any(modifier => modifier != ParameterModifier.None)
            || call.ArgumentNames?.Any(name => !string.IsNullOrEmpty(name)) == true
            || call.ArgumentModifiers?.Any(modifier => !string.IsNullOrEmpty(modifier)) == true)
            return;
        for (var index = 0; index < parameterTypes.Count; index++)
        {
            var parameterType = EraseRefinement(parameterTypes[index]);
            if (IsRejectedNumericConversion(parameterType, argumentTypes[index]))
            {
                ReportNumericConversion(call.Arguments[index].Span, parameterType, argumentTypes[index],
                    $"argument {index + 1} of '{call.Target}'");
            }
        }
    }

    private CalorType InferExpressionCallType(ExpressionCallNode call)
    {
        if (call.TargetExpression is FieldAccessNode member)
        {
            var root = member.Target;
            while (root is FieldAccessNode field)
                root = field.Target;
            // Binding, not capitalization, distinguishes qualified type receivers from missing values.
            if (root is not ReferenceNode reference
                || _env.LookupVariable(reference.Name.Split('.')[0]) != null)
                InferExpressionType(member.Target);
            for (var index = 0; index < call.Arguments.Count; index++)
            {
                var argument = call.Arguments[index];
                if (argument is ReferenceNode { Name: "_", IsVerbatim: false }
                    && !string.IsNullOrEmpty(call.ArgumentModifiers?.ElementAtOrDefault(index))
                    && _env.LookupVariable("_") is null)
                    continue;
                InferExpressionType(argument);
            }
            // Member overloads remain binder-owned, just as for dotted named calls.
            return ErrorType.Instance;
        }
        var targetType = InferExpressionType(call.TargetExpression);
        if (!TryGetDelegateFunctionType(targetType, out var function))
        {
            foreach (var argument in call.Arguments)
                InferExpressionType(argument);
            return ErrorType.Instance;
        }

        var argumentTypes = InferDelegateArgumentTypes(function, call.Arguments, call.ArgumentNames);
        return ValidateDelegateCall(function, argumentTypes, call.Arguments,
            call.ArgumentNames, call.ArgumentModifiers, call.Span)
            ? function.ReturnType
            : ErrorType.Instance;
    }

    private CalorType InferCallArgument(ExpressionNode argument, CalorType? expectedType)
    {
        if (argument is MatchExpressionNode && expectedType == null)
        {
            var previousSuppressContextualDiagnostics = _suppressContextualDiagnostics;
            _suppressContextualDiagnostics = true;
            var result = InferExpressionType(argument);
            _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
            return result;
        }
        if (argument is LambdaExpressionNode && expectedType != null && ContainsTypeParameter(expectedType))
            return ErrorType.Instance;
        return InferExpressionType(argument, expectedType);
    }

    private CalorType InferInitialContextualArgument(
        ExpressionNode argument,
        CalorType? expectedType,
        bool hasContextualCandidates)
    {
        if (expectedType != null || !hasContextualCandidates)
            return InferCallArgument(argument, expectedType);

        var checkpoint = BeginSpeculation();
        var result = InferCallArgument(argument, null);
        EndSpeculation(checkpoint);
        return result;
    }

    private void ValidateSelectedContextualArguments(
        CallExpressionNode call,
        ResolvedCallCandidate candidate,
        IReadOnlyList<CalorType?> initialExpectedTypes)
    {
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            if (RequiresTargetType(call.Arguments[i])
                && (initialExpectedTypes[i] == null
                    || !initialExpectedTypes[i]!.Equals(candidate.ConversionTargets[i])))
            {
                InferExpressionType(call.Arguments[i], candidate.ConversionTargets[i]);
            }
        }
    }

    private static bool RequiresTargetType(ExpressionNode argument)
        => argument is MatchExpressionNode or LambdaExpressionNode;

    private IReadOnlyList<CalorType> InferDelegateArgumentTypes(
        FunctionType function,
        IReadOnlyList<ExpressionNode> arguments,
        IReadOnlyList<string?>? argumentNames)
    {
        var parameterIndices = TryMapDelegateArgumentIndices(function, arguments.Count, argumentNames);
        return arguments.Select((argument, index) =>
        {
            var expectedType = parameterIndices != null
                ? function.ParameterTypes[parameterIndices[index]]
                : null;
            return InferCallArgument(argument, expectedType);
        }).ToArray();
    }

    private CalorType InferInterpolatedStringType(InterpolatedStringNode interpolated)
    {
        foreach (var part in interpolated.Parts.OfType<InterpolatedStringExpressionNode>())
        {
            InferExpressionType(part.Expression);
        }
        return PrimitiveType.String;
    }

    private CalorType InferLambdaType(LambdaExpressionNode lambda, CalorType? expectedType)
    {
        var flowCacheDiagnosticStart = _diagnostics.Count;
        var previousInlineRefinedParameters = _inlineRefinedParameters;
        _inlineRefinedParameters = WithoutShadowedNames(_inlineRefinedParameters, lambda);
        var expectedFunction = expectedType != null
            && TryGetDelegateFunctionType(expectedType, out var function)
                ? function
                : null;
        var signatureValid = true;
        if (expectedFunction != null && lambda.Parameters.Count != expectedFunction.ParameterTypes.Count)
        {
            if (!_suppressContextualDiagnostics)
            {
                _diagnostics.ReportError(lambda.Span, DiagnosticCode.TypeMismatch,
                    $"Lambda has {lambda.Parameters.Count} parameter(s), but target delegate expects "
                    + expectedFunction.ParameterTypes.Count);
            }
            signatureValid = false;
        }
        _env.EnterScope();
        var parameterTypes = lambda.Parameters.Select((parameter, index) =>
        {
            var declaredTypeName = parameter.TypeName;
            var declaredType = declaredTypeName is not null
                ? ResolveTypeName(declaredTypeName, parameter.Span)
                : expectedFunction != null && index < expectedFunction.ParameterTypes.Count
                    ? expectedFunction.ParameterTypes[index]
                    : ErrorType.Instance;
            if (declaredTypeName is not null && expectedFunction != null
                && index < expectedFunction.ParameterTypes.Count
                && !declaredType.Equals(expectedFunction.ParameterTypes[index]))
            {
                if (!_suppressContextualDiagnostics)
                {
                    _diagnostics.ReportError(parameter.Span, DiagnosticCode.TypeMismatch,
                        $"Lambda parameter type {declaredType.SurfaceName} does not match target type "
                        + expectedFunction.ParameterTypes[index].SurfaceName);
                }
                signatureValid = false;
            }
            if (expectedFunction?.ParameterModifiers is { } modifiers && index < modifiers.Count
                && (modifiers[index] & (ParameterModifier.Ref | ParameterModifier.Out)) != 0)
            {
                if (!_suppressContextualDiagnostics)
                    _diagnostics.ReportError(parameter.Span, DiagnosticCode.TypeMismatch,
                        "A lambda parameter without a modifier cannot implement a ref or out delegate parameter.");
                signatureValid = false;
            }
            // #1529 review: a parameter typed by the target delegate carries its refinement
            // into the body, so writes to it must be guarded like a declared refined parameter.
            // The last committed inference decides; speculative ones are rolled back.
            if (declaredTypeName is null && declaredType is RefinedType { RefinementName: { } inferredName })
                _inferredRefinements[parameter] = inferredName;
            else
                _inferredRefinements.Remove(parameter);
            _env.DefineVariable(parameter.Name, declaredType);
            return declaredType;
        }).ToArray();

        var callableTarget = expectedFunction;
        if (lambda.IsAsync && expectedFunction is not null
            && !expectedFunction.ReturnType.Equals(PrimitiveType.Void))
        {
            if (AttributeHelper.TryGetAsyncResultType(expectedFunction.ReturnType.SurfaceName, _module, out var bodyResult))
                expectedFunction = new FunctionType(expectedFunction.ParameterTypes, ResolveTypeName(bodyResult, lambda.Span));
            else
            {
                if (!_suppressContextualDiagnostics)
                    _diagnostics.ReportError(lambda.Span, DiagnosticCode.TypeMismatch,
                        $"Async lambda requires an async carrier return type, not {expectedFunction.ReturnType.SurfaceName}.");
                signatureValid = false;
            }
        }
        var previousReturnType = _currentReturnType;
        var previousValidateReturnAssignments = _validateReturnAssignments;
        var previousSuppressContextualDiagnostics = _suppressContextualDiagnostics;
        var previousLambdaReturnInvalid = _lambdaReturnInvalid;
        var previousInferredLambdaReturnTypes = _inferredLambdaReturnTypes;
        if (expectedFunction == null)
            _suppressContextualDiagnostics = true;
        _lambdaReturnInvalid = false;
        _inferredLambdaReturnTypes = null;
        _currentReturnType = expectedFunction?.ReturnType;
        _validateReturnAssignments = expectedFunction != null;
        CalorType returnType;
        _lambdaBodyDepth++;
        try
        {
            if (lambda.ExpressionBody != null)
            {
                returnType = InferExpressionType(lambda.ExpressionBody, expectedFunction?.ReturnType);
                if (expectedFunction != null && !IsVoidCompatibleLambdaExpression(expectedFunction, lambda.ExpressionBody)
                    && !ContainsInferencePlaceholder(expectedFunction.ReturnType)
                    && !ContainsInferencePlaceholder(returnType)
                    && !IsAssignable(expectedFunction.ReturnType, returnType))
                {
                    if (!_suppressContextualDiagnostics)
                    {
                        // A named-refinement mismatch is a refinement-soundness error, so the
                        // filtered contexts (§P, class members, §TR bodies) keep it (#1676).
                        if (IsDistinctNamedRefinement(expectedFunction.ReturnType, returnType))
                        {
                            ReportDistinctNamedRefinement(
                                lambda.ExpressionBody.Span, expectedFunction.ReturnType, returnType, "a lambda return");
                        }
                        else
                        {
                            _diagnostics.ReportError(lambda.ExpressionBody.Span, DiagnosticCode.TypeMismatch,
                                $"Lambda return type {returnType.SurfaceName} is not assignable to "
                                + expectedFunction.ReturnType.SurfaceName);
                        }
                    }
                    signatureValid = false;
                }
            }
            else
            {
                foreach (var statement in lambda.StatementBody ?? Array.Empty<StatementNode>())
                {
                    CheckStatement(statement);
                }
                returnType = PrimitiveType.Void;
                var definitelyReturns = DefinitelyReturns(
                    lambda.StatementBody ?? Array.Empty<StatementNode>());
                if (expectedFunction != null
                    && !expectedFunction.ReturnType.Equals(PrimitiveType.Void)
                    && !definitelyReturns)
                {
                    if (!_suppressContextualDiagnostics)
                    {
                        _diagnostics.ReportError(lambda.Span, DiagnosticCode.TypeMismatch,
                            $"Lambda targeting {expectedFunction.SurfaceName} must return a value");
                    }
                    signatureValid = false;
                }
            }
        }
        finally
        {
            _lambdaBodyDepth--;
        }
        signatureValid &= !_lambdaReturnInvalid;
        _currentReturnType = previousReturnType;
        _validateReturnAssignments = previousValidateReturnAssignments;
        _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
        _lambdaReturnInvalid = previousLambdaReturnInvalid;
        _inferredLambdaReturnTypes = previousInferredLambdaReturnTypes;
        _env.ExitScope();
        _inlineRefinedParameters = previousInlineRefinedParameters;
        // A committed lambda check keeps its diagnostics: through the flow cache of structural
        // statements, as before, and through the filtered inference of an §ASSIGN right-hand
        // side, so a lambda assigned to a delegate variable is checked as fully as one bound
        // with §B (#1530 review round 3). A speculative check (an overload trial) keeps
        // nothing; EndSpeculation rolls its diagnostics back.
        if (_speculationDepth == 0)
        {
            PreserveFlowCacheDiagnosticsSince(flowCacheDiagnosticStart);
            foreach (var diagnostic in _diagnostics.Skip(flowCacheDiagnosticStart))
                _nominalRefinementDiagnostics.Add(diagnostic);
        }
        _ = parameterTypes;
        _ = returnType;
        return callableTarget is not null && signatureValid ? callableTarget : ErrorType.Instance;
    }

    private static bool IsVoidCompatibleLambdaExpression(
        FunctionType expectedFunction,
        ExpressionNode expression)
        => expectedFunction.ReturnType.Equals(PrimitiveType.Void)
            && expression is CallExpressionNode or ExpressionCallNode or NewExpressionNode;

    private bool DefinitelyReturns(IReadOnlyList<StatementNode> statements)
    {
        var outcomes = AnalyzeControlFlow(
            statements,
            insideMatch: false);
        var invalidTransfers = outcomes.All
            & (ControlFlowOutcome.LoopExit
                | ControlFlowOutcome.Continue
                | ControlFlowOutcome.MatchExit);
        if (invalidTransfers != ControlFlowOutcome.None
            && statements.Count > 0)
        {
            ReportControlFlowError(
                statements[0].Span,
                "break and continue must target an enclosing loop or match");
        }
        return (outcomes.All
            & (ControlFlowOutcome.FallThrough
                | ControlFlowOutcome.LoopExit
                | ControlFlowOutcome.Continue)) == 0;
    }

    [Flags]
    private enum ControlFlowOutcome
    {
        None = 0,
        FallThrough = 1,
        Return = 2,
        LoopExit = 4,
        Continue = 8,
        MatchExit = 16,
        Throw = 32
    }

    private sealed record ControlFlowResolver(
        object Scope,
        Func<HashSet<StatementNode>, ControlFlowResult> Resolve);

    private sealed record ControlFlowResult(
        ControlFlowOutcome Local,
        IReadOnlyDictionary<object, ControlFlowOutcome> Transfers)
    {
        public ControlFlowOutcome All => Transfers.Values.Aggregate(Local, (all, next) => all | next);
    }

    private static ControlFlowResult LocalFlow(ControlFlowOutcome outcomes)
        => new(outcomes, new Dictionary<object, ControlFlowOutcome>());

    private static Dictionary<object, ControlFlowOutcome> MergeTransfers(
        params IReadOnlyDictionary<object, ControlFlowOutcome>[] sources)
    {
        var merged = new Dictionary<object, ControlFlowOutcome>();
        foreach (var source in sources)
        {
            foreach (var (scope, outcomes) in source)
            {
                merged[scope] = merged.GetValueOrDefault(scope) | outcomes;
            }
        }
        return merged;
    }

    private static ControlFlowResult TransferTo(
        ControlFlowOutcome local,
        IReadOnlyDictionary<object, ControlFlowOutcome> existingTransfers,
        ControlFlowResolver resolver,
        ControlFlowResult target)
    {
        var transfers = MergeTransfers(existingTransfers, target.Transfers);
        transfers[resolver.Scope] = transfers.GetValueOrDefault(resolver.Scope) | target.Local;
        return new ControlFlowResult(local, transfers);
    }

    private static ControlFlowResult AbsorbTransfers(
        ControlFlowResult result,
        object scope)
    {
        if (!result.Transfers.TryGetValue(scope, out var outcomes))
            return result;
        var remaining = MergeTransfers(result.Transfers);
        remaining.Remove(scope);
        return new ControlFlowResult(result.Local | outcomes, remaining);
    }

    private ControlFlowResult AnalyzeControlFlow(
        IReadOnlyList<StatementNode> statements,
        bool insideMatch,
        IReadOnlyDictionary<string, ControlFlowResolver>?
            inheritedLabelResolvers = null,
        IReadOnlyDictionary<string, ControlFlowResolver>?
            matchResolvers = null,
        HashSet<StatementNode>? activeStatements = null,
        bool insideCatch = false)
    {
        var scope = new object();
        var labelResolvers = inheritedLabelResolvers != null
            ? new Dictionary<string, ControlFlowResolver>(
                inheritedLabelResolvers, StringComparer.Ordinal)
            : new Dictionary<string, ControlFlowResolver>(
                StringComparer.Ordinal);
        foreach (var item in statements
                     .Select((statement, index) => (statement, index))
                     .Where(item => item.statement is LabelStatementNode)
                     .GroupBy(item => ((LabelStatementNode)item.statement).Label,
                         StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            var targetIndex = item.index;
            labelResolvers[((LabelStatementNode)item.statement).Label] =
                new ControlFlowResolver(
                    scope,
                    active => AnalyzeControlFlowFrom(
                        statements,
                        labelResolvers,
                        matchResolvers,
                        targetIndex,
                        insideMatch,
                        active,
                        insideCatch));
        }
        var result = AnalyzeControlFlowFrom(
            statements,
            labelResolvers,
            matchResolvers,
            startIndex: 0,
            insideMatch,
            activeStatements ?? new HashSet<StatementNode>(),
            insideCatch);
        return AbsorbTransfers(result, scope);
    }

    private ControlFlowResult AnalyzeControlFlowFrom(
        IReadOnlyList<StatementNode> statements,
        IReadOnlyDictionary<string, ControlFlowResolver>
            labelResolvers,
        IReadOnlyDictionary<string, ControlFlowResolver>?
            matchResolvers,
        int startIndex,
        bool insideMatch,
        HashSet<StatementNode> activeStatements,
        bool insideCatch)
    {
        var localOutcomes = ControlFlowOutcome.FallThrough;
        var transfers = new Dictionary<object, ControlFlowOutcome>();
        for (var index = startIndex; index < statements.Count; index++)
        {
            if ((localOutcomes & ControlFlowOutcome.FallThrough) == 0)
                break;
            var statement = statements[index];
            if (!activeStatements.Add(statement))
            {
                return new ControlFlowResult(
                    localOutcomes & ~ControlFlowOutcome.FallThrough,
                    transfers);
            }
            var priorOutcomes = localOutcomes & ~ControlFlowOutcome.FallThrough;
            if (statement is GotoStatementNode
                {
                    CaseLabel: null,
                    IsDefault: false
                } gotoStatement
                && labelResolvers.TryGetValue(gotoStatement.Label, out var labelResolver))
            {
                var target = labelResolver.Resolve(
                    new HashSet<StatementNode>(activeStatements));
                return TransferTo(
                    priorOutcomes,
                    transfers,
                    labelResolver,
                    target);
            }
            if (statement is GotoStatementNode { CaseLabel: not null } gotoCase
                && GetCaseKey(gotoCase.CaseLabel) is { } caseKey
                && matchResolvers?.TryGetValue(caseKey, out var caseResolver) == true)
            {
                var target = caseResolver.Resolve(
                    new HashSet<StatementNode>(activeStatements));
                return TransferTo(
                    priorOutcomes,
                    transfers,
                    caseResolver,
                    target);
            }
            if (statement is GotoStatementNode { IsDefault: true }
                && matchResolvers?.TryGetValue(DefaultCaseKey, out var defaultResolver) == true)
            {
                var target = defaultResolver.Resolve(
                    new HashSet<StatementNode>(activeStatements));
                return TransferTo(
                    priorOutcomes,
                    transfers,
                    defaultResolver,
                    target);
            }
            if (statement is GotoStatementNode { CaseLabel: not null } or
                GotoStatementNode { IsDefault: true })
            {
                ReportControlFlowError(
                    statement.Span,
                    "goto case/default must target an unguarded case in the enclosing match");
            }
            else if (statement is GotoStatementNode)
            {
                ReportControlFlowError(
                    statement.Span,
                    "goto must target a label in the current control-flow scope");
            }
            var statementResult = AnalyzeStatementControlFlow(
                statement,
                insideMatch,
                labelResolvers,
                matchResolvers,
                activeStatements,
                insideCatch);
            localOutcomes = priorOutcomes | statementResult.Local;
            transfers = MergeTransfers(transfers, statementResult.Transfers);
        }
        return new ControlFlowResult(localOutcomes, transfers);
    }

    private const string DefaultCaseKey = "default";

    private static string? GetCaseKey(
        ExpressionNode expression,
        bool normalizeIntegralAsChar = false)
        => expression switch
        {
            IntLiteralNode literal
                when normalizeIntegralAsChar
                    && literal.Sign == IntegerLiteralSign.Positive
                    && literal.Magnitude <= char.MaxValue
                => $"char:{literal.Magnitude}",
            IntLiteralNode literal =>
                $"int:{literal.Sign}:{literal.Magnitude}",
            StringLiteralNode literal => $"string:{literal.Value}",
            BoolLiteralNode literal => $"bool:{literal.Value}",
            FloatLiteralNode literal => $"float:{literal.Value:R}:{literal.IsDecimal}:{literal.IsSingle}",
            DecimalLiteralNode literal => $"decimal:{literal.Value}",
            CharOperationNode
            {
                Operation: CharOp.CharLiteral,
                Arguments: [StringLiteralNode { Value.Length: 1 } literal]
            } => $"char:{(int)literal.Value[0]}",
            ReferenceNode { Name.Length: >= 3 } reference
                when TryParseCharacterCase(reference.Name, out var character)
                => $"char:{(int)character}",
            ReferenceNode { IsNullLiteral: true } => "null",
            ReferenceNode reference => $"reference:{reference.Name}",
            _ => null
        };

    private static string? GetCaseKey(
        PatternNode pattern,
        bool normalizeIntegralAsChar)
        => pattern switch
        {
            LiteralPatternNode literal =>
                GetCaseKey(literal.Literal, normalizeIntegralAsChar),
            ConstantPatternNode constant =>
                GetCaseKey(constant.Value, normalizeIntegralAsChar),
            _ => null
        };

    private static bool TryParseCharacterCase(string text, out char character)
    {
        var expression = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(text);
        if (expression is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal
            && literal.Token.Value is char value)
        {
            character = value;
            return true;
        }
        character = default;
        return false;
    }

    private ControlFlowResult AnalyzeRethrowControlFlow(
        StatementNode rethrow,
        bool insideCatch)
    {
        if (!insideCatch)
        {
            ReportControlFlowError(
                rethrow.Span,
                "rethrow must be used within a catch block");
        }
        return LocalFlow(ControlFlowOutcome.Throw);
    }

    private ControlFlowResult AnalyzeStatementControlFlow(
        StatementNode statement,
        bool insideMatch,
        IReadOnlyDictionary<string, ControlFlowResolver>
            labelResolvers,
        IReadOnlyDictionary<string, ControlFlowResolver>?
            matchResolvers,
        HashSet<StatementNode> activeStatements,
        bool insideCatch)
        => statement switch
        {
            ReturnStatementNode
                => LocalFlow(ControlFlowOutcome.Return),
            ThrowStatementNode { Exception: null } rethrow
                => AnalyzeRethrowControlFlow(rethrow, insideCatch),
            ThrowStatementNode
                => LocalFlow(ControlFlowOutcome.Throw),
            RethrowStatementNode rethrow => AnalyzeRethrowControlFlow(
                rethrow, insideCatch),
            BreakStatementNode => insideMatch
                ? LocalFlow(ControlFlowOutcome.MatchExit)
                : LocalFlow(ControlFlowOutcome.LoopExit),
            ContinueStatementNode => LocalFlow(ControlFlowOutcome.Continue),
            GotoStatementNode { CaseLabel: not null } => insideMatch
                ? LocalFlow(ControlFlowOutcome.MatchExit)
                : LocalFlow(ControlFlowOutcome.LoopExit),
            GotoStatementNode { IsDefault: true } => insideMatch
                ? LocalFlow(ControlFlowOutcome.MatchExit)
                : LocalFlow(ControlFlowOutcome.LoopExit),
            GotoStatementNode => LocalFlow(ControlFlowOutcome.LoopExit),
            IfStatementNode conditional => AnalyzeConditionalControlFlow(
                conditional, insideMatch, labelResolvers, matchResolvers, activeStatements,
                insideCatch),
            MatchStatementNode match => AnalyzeMatchControlFlow(
                match, labelResolvers, activeStatements, insideCatch),
            WhileStatementNode loop => AnalyzeLoopControlFlow(
                loop.Body,
                loop.Condition is BoolLiteralNode { Value: true },
                executesAtLeastOnce: false,
                labelResolvers,
                matchResolvers,
                activeStatements,
                insideCatch),
            DoWhileStatementNode loop => AnalyzeLoopControlFlow(
                loop.Body,
                loop.Condition is BoolLiteralNode { Value: true },
                executesAtLeastOnce: true,
                labelResolvers,
                matchResolvers,
                activeStatements,
                insideCatch),
            ForStatementNode loop => AnalyzeFiniteLoopControlFlow(
                loop.Body, labelResolvers, matchResolvers, activeStatements, insideCatch),
            ForeachStatementNode loop => AnalyzeFiniteLoopControlFlow(
                loop.Body, labelResolvers, matchResolvers, activeStatements, insideCatch),
            DictionaryForeachNode loop => AnalyzeFiniteLoopControlFlow(
                loop.Body, labelResolvers, matchResolvers, activeStatements, insideCatch),
            TryStatementNode tryStatement => AnalyzeTryControlFlow(
                tryStatement, insideMatch, labelResolvers, matchResolvers, activeStatements,
                insideCatch),
            UsingStatementNode usingStatement => AnalyzeControlFlow(
                usingStatement.Body, insideMatch, labelResolvers, matchResolvers,
                new HashSet<StatementNode>(activeStatements), insideCatch),
            UnsafeBlockNode unsafeBlock => AnalyzeControlFlow(
                unsafeBlock.Body, insideMatch, labelResolvers, matchResolvers,
                new HashSet<StatementNode>(activeStatements), insideCatch),
            FixedStatementNode fixedStatement => AnalyzeControlFlow(
                fixedStatement.Body, insideMatch, labelResolvers, matchResolvers,
                new HashSet<StatementNode>(activeStatements), insideCatch),
            SyncBlockNode syncBlock => AnalyzeControlFlow(
                syncBlock.Body, insideMatch, labelResolvers, matchResolvers,
                new HashSet<StatementNode>(activeStatements), insideCatch),
            _ => LocalFlow(ControlFlowOutcome.FallThrough)
        };

    private ControlFlowResult AnalyzeConditionalControlFlow(
        IfStatementNode conditional,
        bool insideMatch,
        IReadOnlyDictionary<string, ControlFlowResolver>
            labelResolvers,
        IReadOnlyDictionary<string, ControlFlowResolver>?
            matchResolvers,
        HashSet<StatementNode> activeStatements,
        bool insideCatch)
    {
        var outcomes = AnalyzeControlFlow(
            conditional.ThenBody, insideMatch, labelResolvers, matchResolvers,
            new HashSet<StatementNode>(activeStatements), insideCatch);
        foreach (var clause in conditional.ElseIfClauses)
        {
            var clauseOutcomes = AnalyzeControlFlow(
                clause.Body, insideMatch, labelResolvers, matchResolvers,
                new HashSet<StatementNode>(activeStatements), insideCatch);
            outcomes = new ControlFlowResult(
                outcomes.Local | clauseOutcomes.Local,
                MergeTransfers(outcomes.Transfers, clauseOutcomes.Transfers));
        }
        var elseOutcomes = conditional.ElseBody != null
            ? AnalyzeControlFlow(
                conditional.ElseBody, insideMatch, labelResolvers, matchResolvers,
                new HashSet<StatementNode>(activeStatements), insideCatch)
            : LocalFlow(ControlFlowOutcome.FallThrough);
        return new ControlFlowResult(
            outcomes.Local | elseOutcomes.Local,
            MergeTransfers(outcomes.Transfers, elseOutcomes.Transfers));
    }

    private ControlFlowResult AnalyzeMatchControlFlow(
        MatchStatementNode match,
        IReadOnlyDictionary<string, ControlFlowResolver>
            labelResolvers,
        HashSet<StatementNode> activeStatements,
        bool insideCatch)
    {
        var scope = new object();
        var matchTargetType = _matchTargetTypes.GetValueOrDefault(match, ErrorType.Instance);
        var normalizeIntegralAsChar = matchTargetType.Equals(PrimitiveType.Char)
            || matchTargetType is NullableValueType nullable
                && nullable.UnderlyingType.Equals(PrimitiveType.Char);
        var normalizeCharAsIntegral = matchTargetType.Equals(PrimitiveType.Int)
            || matchTargetType is NullableValueType nullableIntegral
                && nullableIntegral.UnderlyingType.Equals(PrimitiveType.Int);
        var caseResolvers =
            new Dictionary<string, ControlFlowResolver>(
                StringComparer.Ordinal);
        foreach (var matchCase in match.Cases)
        {
            var key = matchCase.Pattern is WildcardPatternNode && matchCase.Guard == null
                ? DefaultCaseKey
                : matchCase.Guard == null
                    ? GetCaseKey(matchCase.Pattern, normalizeIntegralAsChar)
                    : null;
            if (key == null || caseResolvers.ContainsKey(key))
                continue;
            var targetCase = matchCase;
            var resolver = new ControlFlowResolver(
                scope,
                active => AnalyzeControlFlow(
                    targetCase.Body,
                    insideMatch: true,
                    labelResolvers,
                    caseResolvers,
                    active,
                    insideCatch));
            caseResolvers[key] = resolver;
            if ((normalizeIntegralAsChar || normalizeCharAsIntegral)
                && key.StartsWith("char:", StringComparison.Ordinal)
                && ulong.TryParse(
                    key.AsSpan("char:".Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var characterCode))
            {
                caseResolvers.TryAdd(
                    $"int:{IntegerLiteralSign.Positive}:{characterCode}",
                    resolver);
            }
            if (normalizeCharAsIntegral
                && key.StartsWith(
                    $"int:{IntegerLiteralSign.Positive}:",
                    StringComparison.Ordinal)
                && ulong.TryParse(
                    key.AsSpan($"int:{IntegerLiteralSign.Positive}:".Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var integralCode)
                && integralCode <= char.MaxValue)
            {
                caseResolvers.TryAdd($"char:{integralCode}", resolver);
            }
        }

        var outcomes = LocalFlow(ControlFlowOutcome.None);
        foreach (var matchCase in match.Cases)
        {
            var caseOutcomes = AnalyzeControlFlow(
                    matchCase.Body,
                    insideMatch: true,
                    labelResolvers,
                    caseResolvers,
                    new HashSet<StatementNode>(activeStatements),
                    insideCatch);
            outcomes = new ControlFlowResult(
                outcomes.Local | caseOutcomes.Local,
                MergeTransfers(outcomes.Transfers, caseOutcomes.Transfers));
        }
        outcomes = AbsorbTransfers(outcomes, scope);
        var exhaustive = match.Cases.Count > 0
            && match.Cases.Any(matchCase =>
                matchCase.Pattern is WildcardPatternNode && matchCase.Guard == null);
        var localOutcomes = outcomes.Local;
        if ((localOutcomes & ControlFlowOutcome.MatchExit) != 0)
        {
            localOutcomes = localOutcomes & ~ControlFlowOutcome.MatchExit
                | ControlFlowOutcome.FallThrough;
        }
        if (!exhaustive)
            localOutcomes |= ControlFlowOutcome.FallThrough;
        return new ControlFlowResult(localOutcomes, outcomes.Transfers);
    }

    private ControlFlowResult AnalyzeLoopControlFlow(
        IReadOnlyList<StatementNode> body,
        bool conditionIsAlwaysTrue,
        bool executesAtLeastOnce,
        IReadOnlyDictionary<string, ControlFlowResolver>
            labelResolvers,
        IReadOnlyDictionary<string, ControlFlowResolver>?
            matchResolvers,
        HashSet<StatementNode> activeStatements,
        bool insideCatch)
    {
        var bodyOutcomes = AnalyzeControlFlow(
            body,
            insideMatch: false,
            labelResolvers,
            matchResolvers,
            new HashSet<StatementNode>(activeStatements),
            insideCatch);
        var localOutcomes = bodyOutcomes.Local;
        var outcomes = localOutcomes
            & (ControlFlowOutcome.Return | ControlFlowOutcome.Throw);
        if ((localOutcomes & ControlFlowOutcome.LoopExit) != 0
            || !conditionIsAlwaysTrue
                && (!executesAtLeastOnce
                    || (localOutcomes
                        & (ControlFlowOutcome.FallThrough | ControlFlowOutcome.Continue)) != 0))
        {
            outcomes |= ControlFlowOutcome.FallThrough;
        }
        return new ControlFlowResult(outcomes, bodyOutcomes.Transfers);
    }

    private ControlFlowResult AnalyzeFiniteLoopControlFlow(
        IReadOnlyList<StatementNode> body,
        IReadOnlyDictionary<string, ControlFlowResolver> labelResolvers,
        IReadOnlyDictionary<string, ControlFlowResolver>? matchResolvers,
        HashSet<StatementNode> activeStatements,
        bool insideCatch)
    {
        var bodyOutcomes = AnalyzeControlFlow(
            body,
            insideMatch: false,
            labelResolvers,
            matchResolvers,
            new HashSet<StatementNode>(activeStatements),
            insideCatch);
        var outcomes = ControlFlowOutcome.FallThrough
            | (bodyOutcomes.Local & (ControlFlowOutcome.Return | ControlFlowOutcome.Throw));
        return new ControlFlowResult(outcomes, bodyOutcomes.Transfers);
    }

    private ControlFlowResult AnalyzeTryControlFlow(
        TryStatementNode tryStatement,
        bool insideMatch,
        IReadOnlyDictionary<string, ControlFlowResolver>
            labelResolvers,
        IReadOnlyDictionary<string, ControlFlowResolver>?
            matchResolvers,
        HashSet<StatementNode> activeStatements,
        bool insideCatch)
    {
        var protectedOutcomes = AnalyzeControlFlow(
            tryStatement.TryBody, insideMatch, labelResolvers, matchResolvers,
            new HashSet<StatementNode>(activeStatements), insideCatch);
        foreach (var clause in tryStatement.CatchClauses)
        {
            var catchOutcomes = AnalyzeControlFlow(
                clause.Body, insideMatch, labelResolvers, matchResolvers,
                new HashSet<StatementNode>(activeStatements), insideCatch: true);
            protectedOutcomes = new ControlFlowResult(
                protectedOutcomes.Local | catchOutcomes.Local,
                MergeTransfers(protectedOutcomes.Transfers, catchOutcomes.Transfers));
        }
        if (tryStatement.FinallyBody == null)
            return protectedOutcomes;

        var finallyOutcomes = AnalyzeControlFlow(
            tryStatement.FinallyBody, insideMatch: false,
            inheritedLabelResolvers: null,
            matchResolvers: null,
            activeStatements: new HashSet<StatementNode>(activeStatements),
            insideCatch: false);
        var escapingFinallyOutcomes = finallyOutcomes.Local
            & (ControlFlowOutcome.Return
                | ControlFlowOutcome.Continue
                | ControlFlowOutcome.LoopExit
                | ControlFlowOutcome.MatchExit);
        if (escapingFinallyOutcomes != ControlFlowOutcome.None)
        {
            ReportControlFlowError(
                tryStatement.FinallyBody[0].Span,
                "Control cannot leave a finally block");
            finallyOutcomes = new ControlFlowResult(
                finallyOutcomes.Local & ~escapingFinallyOutcomes
                    | ControlFlowOutcome.FallThrough,
                finallyOutcomes.Transfers);
        }
        if ((finallyOutcomes.Local & ControlFlowOutcome.FallThrough) == 0)
            return finallyOutcomes;
        return new ControlFlowResult(
            protectedOutcomes.Local
                | (finallyOutcomes.Local & ~ControlFlowOutcome.FallThrough),
            MergeTransfers(protectedOutcomes.Transfers, finallyOutcomes.Transfers));
    }

    private CalorType InferQuantifierType(
        IReadOnlyList<QuantifierVariableNode> variables,
        ExpressionNode body)
    {
        _env.EnterScope();
        foreach (var variable in variables)
        {
            _env.DefineVariable(variable.Name, ResolveTypeName(variable.TypeName, variable.Span));
        }
        var bodyType = InferExpressionType(body);
        _env.ExitScope();
        if (IsDefinitelyNotBool(bodyType))
        {
            _diagnostics.ReportError(body.Span, DiagnosticCode.TypeMismatch,
                $"Quantifier body must be bool, got {bodyType.SurfaceName}");
        }
        return PrimitiveType.Bool;
    }

    private CalorType InferUnknownExpressionType(ExpressionNode expression)
    {
        TraverseAstHolder(expression, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return ErrorType.Instance;
    }

    private void TraverseAstHolder(object holder, HashSet<object> visited)
    {
        if (!visited.Add(holder))
            return;

        foreach (var property in holder.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length != 0)
                continue;
            var value = property.GetValue(holder);
            if (value is ExpressionNode child && !ReferenceEquals(child, holder))
            {
                InferExpressionType(child);
            }
            else if (value is AstNode astNode && !ReferenceEquals(astNode, holder))
            {
                TraverseAstHolder(astNode, visited);
            }
            else if (value != null
                && value.GetType().Assembly == typeof(AstNode).Assembly
                && value.GetType().Namespace == typeof(AstNode).Namespace)
            {
                TraverseAstHolder(value, visited);
            }
            else if (value is System.Collections.IEnumerable children and not string)
            {
                foreach (var nested in children)
                {
                    if (nested is ExpressionNode nestedExpression)
                        InferExpressionType(nestedExpression);
                    else if (nested is AstNode nestedAst)
                        TraverseAstHolder(nestedAst, visited);
                    else if (nested != null
                        && nested.GetType().Assembly == typeof(AstNode).Assembly
                        && nested.GetType().Namespace == typeof(AstNode).Namespace)
                        TraverseAstHolder(nested, visited);
                }
            }
        }
    }

    private CalorType? GetConsensusExpectedArgumentType(
        IReadOnlyList<FunctionCandidate> candidates,
        CallExpressionNode call,
        IReadOnlyList<CalorType> preliminaryTypes,
        int argumentIndex)
    {
        CalorType? expected = null;
        var found = false;
        foreach (var candidate in candidates)
        {
            if (!TryResolveKnownArgumentTypes(
                    candidate, call, preliminaryTypes, out var parameterIndices, out var parameterTypes))
                continue;

            var parameterIndex = parameterIndices[argumentIndex];
            var candidateExpected = EffectiveParameterType(
                candidate,
                parameterTypes[parameterIndex],
                parameterIndex,
                preliminaryTypes[argumentIndex],
                RequiresTargetType(call.Arguments[argumentIndex]));
            if (ContainsTypeParameter(candidateExpected))
                return null;
            if (!found)
            {
                expected = candidateExpected;
                found = true;
            }
            else if (!expected!.Equals(candidateExpected))
            {
                return null;
            }
        }
        return found ? expected : null;
    }

    private bool TryResolveKnownArgumentTypes(
        FunctionCandidate candidate,
        CallExpressionNode call,
        IReadOnlyList<CalorType> preliminaryTypes,
        out int[] parameterIndices,
        out CalorType[] parameterTypes)
    {
        parameterTypes = [];
        if (!TryMapCallArgumentIndices(candidate, call, out parameterIndices))
            return false;

        var substitutions = new Dictionary<string, CalorType>(StringComparer.Ordinal);
        for (var argumentIndex = 0; argumentIndex < preliminaryTypes.Count; argumentIndex++)
        {
            if (RequiresTargetType(call.Arguments[argumentIndex]))
                continue;
            var parameterIndex = parameterIndices[argumentIndex];
            var template = EffectiveParameterType(
                candidate,
                candidate.Type.ParameterTypes[parameterIndex],
                parameterIndex,
                preliminaryTypes[argumentIndex],
                forceExpanded: false);
            if (call.Arguments[argumentIndex] is not ReferenceNode reference
                || _env.LookupFunctionCandidates(reference.Name).Count == 0)
                InferTypeArguments(template, preliminaryTypes[argumentIndex], substitutions);
        }

        parameterTypes = candidate.Type.ParameterTypes
            .Select(parameter => SubstituteTypeParameters(parameter, substitutions))
            .ToArray();
        for (var argumentIndex = 0; argumentIndex < preliminaryTypes.Count; argumentIndex++)
        {
            if (RequiresTargetType(call.Arguments[argumentIndex]))
                continue;
            var parameterIndex = parameterIndices[argumentIndex];
            var target = EffectiveParameterType(
                candidate,
                parameterTypes[parameterIndex],
                parameterIndex,
                preliminaryTypes[argumentIndex],
                forceExpanded: false);
            if (call.Arguments[argumentIndex] is ReferenceNode methodGroup
                && _env.LookupFunctionCandidates(methodGroup.Name) is { Count: > 0 } methodCandidates)
            {
                if (!TryGetDelegateFunctionType(target, out var delegateType)
                    || !methodCandidates.Any(method => IsMethodGroupCompatible(delegateType, method)))
                {
                    return false;
                }
            }
            else if (!IsAssignableResolvedMethodArgument(target, preliminaryTypes[argumentIndex]))
            {
                return false;
            }
            var modifier = candidate.ParameterModifiers[parameterIndex]
                & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
            if (modifier != ParameterModifier.None
                && preliminaryTypes[argumentIndex] is not ErrorType
                && !target.Equals(preliminaryTypes[argumentIndex]))
            {
                return false;
            }
        }
        return true;
    }

    private static CalorType EffectiveParameterType(
        FunctionCandidate candidate,
        CalorType parameterType,
        int parameterIndex,
        CalorType argumentType,
        bool forceExpanded)
        => candidate.ParameterModifiers[parameterIndex].HasFlag(ParameterModifier.Params)
            && parameterType is ArrayType array
            && (forceExpanded || !IsAssignable(parameterType, argumentType))
                ? array.ElementType
                : parameterType;

    private static bool TryMapCallArgumentIndices(
        FunctionCandidate candidate,
        CallExpressionNode call,
        out int[] parameterIndices)
    {
        parameterIndices = new int[call.Arguments.Count];
        var assigned = new bool[candidate.Type.ParameterTypes.Count];
        var paramsIndices = candidate.ParameterModifiers
            .Select((modifier, index) => (modifier, index))
            .Where(item => item.modifier.HasFlag(ParameterModifier.Params))
            .Select(item => item.index)
            .ToArray();
        if (paramsIndices.Length > 1)
            return false;
        var paramsIndex = paramsIndices.Length == 1 ? paramsIndices[0] : -1;
        var nextPositional = 0;
        var seenOutOfPositionNamedArgument = false;
        for (var argumentIndex = 0; argumentIndex < call.Arguments.Count; argumentIndex++)
        {
            var argumentName = call.ArgumentNames != null && argumentIndex < call.ArgumentNames.Count
                ? call.ArgumentNames[argumentIndex]
                : null;
            int parameterIndex;
            if (!string.IsNullOrEmpty(argumentName))
            {
                var matches = candidate.ParameterNames
                    .Select((name, index) => (name, index))
                    .Where(item => item.name.Equals(argumentName, StringComparison.Ordinal))
                    .Select(item => item.index)
                    .ToArray();
                if (matches.Length != 1)
                    return false;
                parameterIndex = matches[0];
                while (nextPositional < assigned.Length && assigned[nextPositional])
                    nextPositional++;
                seenOutOfPositionNamedArgument |= parameterIndex != nextPositional;
            }
            else
            {
                if (seenOutOfPositionNamedArgument)
                    return false;
                while (nextPositional < assigned.Length && assigned[nextPositional])
                    nextPositional++;
                parameterIndex = nextPositional < assigned.Length ? nextPositional : paramsIndex;
                if (paramsIndex >= 0 && parameterIndex >= paramsIndex)
                    parameterIndex = paramsIndex;
                else
                    nextPositional++;
            }

            if (parameterIndex < 0 || parameterIndex >= assigned.Length
                || assigned[parameterIndex] && parameterIndex != paramsIndex
                || !ArgumentModifierMatches(
                    candidate.ParameterModifiers[parameterIndex],
                    call.ArgumentModifiers != null && argumentIndex < call.ArgumentModifiers.Count
                        ? call.ArgumentModifiers[argumentIndex]
                        : null))
            {
                return false;
            }
            parameterIndices[argumentIndex] = parameterIndex;
            assigned[parameterIndex] = true;
        }
        return assigned.Select((isAssigned, index) => (isAssigned, index))
            .All(item => item.isAssigned
                || candidate.OptionalParameters[item.index]
                || item.index == paramsIndex);
    }

    /// <summary>
    /// #1530: when no overload accepts the call, and one would if the named-refinement arguments
    /// were unconstrained, those arguments are the cause: a refinement was passed where a
    /// different named refinement is expected. Other overload failures stay with the binder.
    /// </summary>
    private bool ReportDistinctNamedRefinementArguments(
        CallExpressionNode call,
        IReadOnlyList<FunctionCandidate> functionCandidates,
        IReadOnlyList<CalorType> argumentTypes)
    {
        var relaxed = argumentTypes
            .Select(type => type is RefinedType { RefinementName: not null } ? ErrorType.Instance : type)
            .ToArray();
        foreach (var candidate in functionCandidates)
        {
            if (TryResolveCallCandidate(candidate, call, relaxed) is not { } resolved)
                continue;
            for (var i = 0; i < argumentTypes.Count && i < resolved.ConversionTargets.Count; i++)
            {
                if (IsDistinctNamedRefinement(resolved.ConversionTargets[i], argumentTypes[i]))
                {
                    ReportDistinctNamedRefinement(
                        call.Arguments[i].Span,
                        resolved.ConversionTargets[i],
                        argumentTypes[i],
                        $"argument {i + 1} of '{call.Target}'");
                    return true;
                }
            }
        }
        return false;
    }

    private ResolvedCallCandidate? TryResolveCallCandidate(
        FunctionCandidate candidate,
        CallExpressionNode call,
        IReadOnlyList<CalorType> argumentTypes)
    {
        var mappedArguments =
            new (CalorType Type, ExpressionNode Expression, int SourceIndex)?[candidate.Type.ParameterTypes.Count];
        var paramsIndices = candidate.ParameterModifiers
            .Select((modifier, index) => (modifier, index))
            .Where(item => item.modifier.HasFlag(ParameterModifier.Params))
            .Select(item => item.index)
            .ToArray();
        if (paramsIndices.Length > 1)
            return null;
        var paramsIndex = paramsIndices.Length == 1 ? paramsIndices[0] : -1;
        var expandedParamsArguments =
            new List<(CalorType Type, ExpressionNode Expression, int SourceIndex)>();
        var nextPositional = 0;
        var seenOutOfPositionNamedArgument = false;
        for (var argumentIndex = 0; argumentIndex < argumentTypes.Count; argumentIndex++)
        {
            var argumentName = call.ArgumentNames != null && argumentIndex < call.ArgumentNames.Count
                ? call.ArgumentNames[argumentIndex]
                : null;
            int parameterIndex;
            if (!string.IsNullOrEmpty(argumentName))
            {
                var matchingParameters = candidate.ParameterNames
                    .Select((name, index) => (name, index))
                    .Where(item => item.name.Equals(argumentName, StringComparison.Ordinal))
                    .Select(item => item.index)
                    .ToArray();
                if (matchingParameters.Length != 1)
                    return null;
                parameterIndex = matchingParameters[0];
                while (nextPositional < mappedArguments.Length
                    && mappedArguments[nextPositional] != null)
                {
                    nextPositional++;
                }
                seenOutOfPositionNamedArgument |= parameterIndex != nextPositional;
            }
            else
            {
                if (seenOutOfPositionNamedArgument)
                    return null;
                while (nextPositional < mappedArguments.Length
                    && mappedArguments[nextPositional] != null)
                {
                    nextPositional++;
                }
                parameterIndex = nextPositional++;
                if (paramsIndex >= 0 && parameterIndex >= paramsIndex
                    && (parameterIndex >= mappedArguments.Length
                        || argumentTypes.Count - argumentIndex > mappedArguments.Length - paramsIndex
                        || RequiresTargetType(call.Arguments[argumentIndex])
                        || !IsAssignable(candidate.Type.ParameterTypes[paramsIndex], argumentTypes[argumentIndex])))
                {
                    if (call.ArgumentModifiers != null
                        && argumentIndex < call.ArgumentModifiers.Count
                        && !string.IsNullOrEmpty(call.ArgumentModifiers[argumentIndex]))
                    {
                        return null;
                    }
                    expandedParamsArguments.Add(
                        (argumentTypes[argumentIndex], call.Arguments[argumentIndex], argumentIndex));
                    nextPositional = paramsIndex;
                    continue;
                }
            }

            if (parameterIndex < 0 || parameterIndex >= mappedArguments.Length
                || mappedArguments[parameterIndex] != null
                || !ArgumentModifierMatches(
                    candidate.ParameterModifiers[parameterIndex],
                    call.ArgumentModifiers != null && argumentIndex < call.ArgumentModifiers.Count
                        ? call.ArgumentModifiers[argumentIndex]
                        : null))
            {
                return null;
            }

            mappedArguments[parameterIndex] =
                (argumentTypes[argumentIndex], call.Arguments[argumentIndex], argumentIndex);
        }

        if (mappedArguments.Select((argument, index) => (argument, index))
            .Any(item => item.argument == null
                && !candidate.OptionalParameters[item.index]
                && item.index != paramsIndex))
        {
            return null;
        }

        var substitutions = new Dictionary<string, CalorType>(StringComparer.Ordinal);
        if (call.TypeArguments != null)
        {
            if (call.TypeArguments.Count != candidate.TypeParameterNames.Count)
                return null;
            for (var i = 0; i < call.TypeArguments.Count; i++)
            {
                substitutions[candidate.TypeParameterNames[i]] =
                    ResolveTypeName(call.TypeArguments[i], call.Span);
            }
        }

        for (var i = 0; i < mappedArguments.Length; i++)
        {
            if (mappedArguments[i] is { } argument
                && argument.Expression is not LambdaExpressionNode
                && (argument.Expression is not ReferenceNode methodGroup
                    || _env.LookupFunctionCandidates(methodGroup.Name).Count == 0))
            {
                InferTypeArguments(candidate.Type.ParameterTypes[i], argument.Type, substitutions);
            }
        }
        for (var i = 0; i < mappedArguments.Length; i++)
        {
            if (mappedArguments[i] is { Expression: LambdaExpressionNode lambda })
            {
                InferTypeArgumentsFromLambda(
                    candidate.Type.ParameterTypes[i], lambda, substitutions);
            }
        }
        for (var i = 0; i < mappedArguments.Length; i++)
        {
            if (mappedArguments[i] is { Expression: ReferenceNode methodGroup }
                && _env.LookupFunctionCandidates(methodGroup.Name) is { Count: > 0 } methodCandidates)
            {
                InferTypeArgumentsFromMethodGroup(
                    candidate.Type.ParameterTypes[i], methodCandidates, substitutions);
            }
        }
        if (expandedParamsArguments.Count > 0
            && candidate.Type.ParameterTypes[paramsIndex] is ArrayType paramsTemplate)
        {
            foreach (var argument in expandedParamsArguments)
            {
                InferTypeArguments(paramsTemplate.ElementType, argument.Type, substitutions);
            }
        }

        for (var i = 0; i < mappedArguments.Length; i++)
        {
            if (mappedArguments[i] is { } argument
                && RequiresTargetType(argument.Expression))
            {
                var substitutedParameter =
                    SubstituteTypeParameters(candidate.Type.ParameterTypes[i], substitutions);
                var expectedArgumentType = EffectiveParameterType(
                    candidate,
                    substitutedParameter,
                    i,
                    argument.Type,
                    forceExpanded: true);
                var previousSuppressContextualDiagnostics = _suppressContextualDiagnostics;
                var diagnosticCheckpoint = BeginSpeculation();
                _suppressContextualDiagnostics = true;
                var inferredArgumentType =
                    InferExpressionType(argument.Expression, expectedArgumentType);
                _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
                EndSpeculation(diagnosticCheckpoint);
                if (inferredArgumentType is ErrorType)
                    return null;
                mappedArguments[i] =
                    (inferredArgumentType, argument.Expression, argument.SourceIndex);
            }
        }

        var parameterTypes = candidate.Type.ParameterTypes
            .Select(parameter => SubstituteTypeParameters(parameter, substitutions))
            .ToArray();
        if (paramsIndex >= 0 && parameterTypes[paramsIndex] is ArrayType contextualParamsArray)
        {
            for (var i = 0; i < expandedParamsArguments.Count; i++)
            {
                var argument = expandedParamsArguments[i];
                if (!RequiresTargetType(argument.Expression))
                    continue;
                var previousSuppressContextualDiagnostics = _suppressContextualDiagnostics;
                var diagnosticCheckpoint = BeginSpeculation();
                _suppressContextualDiagnostics = true;
                var inferredArgumentType =
                    InferExpressionType(argument.Expression, contextualParamsArray.ElementType);
                _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
                EndSpeculation(diagnosticCheckpoint);
                if (inferredArgumentType is ErrorType)
                    return null;
                expandedParamsArguments[i] =
                    (inferredArgumentType, argument.Expression, argument.SourceIndex);
            }
        }
        var conversionCosts = new int[argumentTypes.Count];
        var conversionTargets = new CalorType[argumentTypes.Count];
        for (var i = 0; i < mappedArguments.Length; i++)
        {
            if (mappedArguments[i] is not { } argument)
                continue;

            conversionTargets[argument.SourceIndex] = parameterTypes[i];
            if (argument.Expression is ReferenceNode methodGroup
                && _env.LookupFunctionCandidates(methodGroup.Name) is { Count: > 0 } methodCandidates)
            {
                if (!TryGetDelegateFunctionType(parameterTypes[i], out var delegateType)
                    || methodCandidates.All(overload => !IsMethodGroupCompatible(delegateType, overload)))
                {
                    return null;
                }
                if (!TryGetMethodGroupConversionCost(
                        delegateType, methodCandidates, out var methodGroupCost))
                {
                    return null;
                }
                conversionCosts[argument.SourceIndex] = methodGroupCost;
            }
            else if (!IsAssignableResolvedMethodArgument(parameterTypes[i], argument.Type))
            {
                return null;
            }
            else if ((candidate.ParameterModifiers[i]
                    & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In))
                != ParameterModifier.None
                && argument.Type is not ErrorType
                && !parameterTypes[i].Equals(argument.Type))
            {
                return null;
            }
            else
            {
                conversionCosts[argument.SourceIndex] =
                    GetImplicitConversionCost(parameterTypes[i], argument.Type);
            }
        }

        if (expandedParamsArguments.Count > 0)
        {
            if (paramsIndex < 0 || parameterTypes[paramsIndex] is not ArrayType paramsArray
                || expandedParamsArguments.Any(argument =>
                    !IsAssignableResolvedMethodArgument(paramsArray.ElementType, argument.Type)))
            {
                return null;
            }
            foreach (var argument in expandedParamsArguments)
            {
                conversionCosts[argument.SourceIndex] =
                    GetImplicitConversionCost(paramsArray.ElementType, argument.Type);
                conversionTargets[argument.SourceIndex] = paramsArray.ElementType;
            }
        }

        return new ResolvedCallCandidate(
            new FunctionType(
                parameterTypes,
                SubstituteTypeParameters(candidate.Type.ReturnType, substitutions)),
            conversionCosts,
            conversionTargets,
            call.Arguments,
            expandedParamsArguments.Count > 0,
            mappedArguments.Select((argument, index) => (argument, index))
                .Count(item => item.argument == null && candidate.OptionalParameters[item.index]));
    }

    private static int GetImplicitConversionCost(CalorType target, CalorType source)
    {
        if (target.Equals(source))
            return 0;
        if (source is ErrorType)
            return 5;
        if (target is NullableValueType nullableTarget)
        {
            var sourceType = source is NullableValueType nullableSource
                ? nullableSource.UnderlyingType
                : source;
            return 1 + GetImplicitConversionCost(nullableTarget.UnderlyingType, sourceType);
        }
        if (target is NullableReferenceType nullableReference)
            return 1 + GetImplicitConversionCost(nullableReference.ReferentType, source);
        if (target.Equals(PrimitiveType.Float) && source.Equals(PrimitiveType.Int)
            || target.Equals(PrimitiveType.Decimal) && source.Equals(PrimitiveType.Int)
            || target.Equals(PrimitiveType.Int) && source.Equals(PrimitiveType.Char))
        {
            return 1;
        }
        if ((target.Equals(PrimitiveType.Float) || target.Equals(PrimitiveType.Decimal))
            && source.Equals(PrimitiveType.Char))
        {
            return 2;
        }
        if (target.Equals(PrimitiveType.Object))
            return source.Equals(PrimitiveType.String) ? 2 : 3;
        return 2;
    }

    private static bool IsBetterConversion(ResolvedCallCandidate left, ResolvedCallCandidate right)
    {
        if (left.ConversionCosts.Count != right.ConversionCosts.Count)
            return false;
        var strictlyBetter = false;
        for (var i = 0; i < left.ConversionCosts.Count; i++)
        {
            if (left.ConversionCosts[i] > right.ConversionCosts[i])
                return false;
            strictlyBetter |= left.ConversionCosts[i] < right.ConversionCosts[i];
            if (left.ConversionCosts[i] == right.ConversionCosts[i]
                && !left.ConversionTargets[i].Equals(right.ConversionTargets[i]))
            {
                if (TryGetDelegateFunctionType(left.ConversionTargets[i], out var leftDelegate)
                    && TryGetDelegateFunctionType(right.ConversionTargets[i], out var rightDelegate))
                {
                    var implicitLambda = left.ArgumentExpressions[i] is LambdaExpressionNode lambda
                        && lambda.Parameters.All(parameter => parameter.TypeName is null);
                    var leftDelegateIsBetter = implicitLambda
                        ? IsBetterImplicitLambdaTarget(leftDelegate, rightDelegate)
                        : IsMoreSpecificDelegateType(leftDelegate, rightDelegate);
                    var rightDelegateIsBetter = implicitLambda
                        ? IsBetterImplicitLambdaTarget(rightDelegate, leftDelegate)
                        : IsMoreSpecificDelegateType(rightDelegate, leftDelegate);
                    if (rightDelegateIsBetter && !leftDelegateIsBetter)
                        return false;
                    strictlyBetter |= leftDelegateIsBetter && !rightDelegateIsBetter;
                    continue;
                }
                var leftTargetIsBetter = IsAssignable(
                    right.ConversionTargets[i], left.ConversionTargets[i]);
                var rightTargetIsBetter = IsAssignable(
                    left.ConversionTargets[i], right.ConversionTargets[i]);
                if (rightTargetIsBetter && !leftTargetIsBetter)
                    return false;
                strictlyBetter |= leftTargetIsBetter && !rightTargetIsBetter;
            }
        }
        if (strictlyBetter)
            return true;
        if (!left.UsesExpandedParams && right.UsesExpandedParams)
            return true;
        if (left.UsesExpandedParams && !right.UsesExpandedParams)
            return false;
        return left.OmittedOptionalCount < right.OmittedOptionalCount;
    }

    private static bool IsMoreSpecificDelegateType(FunctionType left, FunctionType right)
    {
        if (left.ParameterTypes.Count != right.ParameterTypes.Count)
            return false;
        var strictlyMoreSpecific = false;
        for (var i = 0; i < left.ParameterTypes.Count; i++)
        {
            if (!IsAssignable(right.ParameterTypes[i], left.ParameterTypes[i]))
                return false;
            strictlyMoreSpecific |= !left.ParameterTypes[i].Equals(right.ParameterTypes[i]);
        }
        if (!IsAssignable(right.ReturnType, left.ReturnType))
            return false;
        return strictlyMoreSpecific || !left.ReturnType.Equals(right.ReturnType);
    }

    private static bool IsBetterImplicitLambdaTarget(FunctionType left, FunctionType right)
    {
        if (left.ParameterTypes.Count != right.ParameterTypes.Count)
            return false;
        var strictlyBetter = false;
        for (var i = 0; i < left.ParameterTypes.Count; i++)
        {
            if (!IsAssignable(left.ParameterTypes[i], right.ParameterTypes[i]))
                return false;
            strictlyBetter |= !left.ParameterTypes[i].Equals(right.ParameterTypes[i]);
        }
        if (!IsAssignable(right.ReturnType, left.ReturnType))
            return false;
        return strictlyBetter || !left.ReturnType.Equals(right.ReturnType);
    }

    private sealed record ResolvedCallCandidate(
        FunctionType Type,
        IReadOnlyList<int> ConversionCosts,
        IReadOnlyList<CalorType> ConversionTargets,
        IReadOnlyList<ExpressionNode> ArgumentExpressions,
        bool UsesExpandedParams,
        int OmittedOptionalCount);

    private static bool ContainsTypeParameter(CalorType type)
        => type switch
        {
            TypeParameterType => true,
            GenericInstanceType generic => generic.TypeArguments.Any(ContainsTypeParameter),
            NullableReferenceType nullable => ContainsTypeParameter(nullable.ReferentType),
            NullableValueType nullable => ContainsTypeParameter(nullable.UnderlyingType),
            ArrayType array => ContainsTypeParameter(array.ElementType),
            FunctionType function => function.ParameterTypes.Any(ContainsTypeParameter)
                || ContainsTypeParameter(function.ReturnType),
            _ => false
        };

    private static bool ContainsInferencePlaceholder(CalorType type)
        => type is TypeVariable || ContainsTypeParameter(type)
            || type switch
            {
                GenericInstanceType generic => generic.TypeArguments.Any(ContainsInferencePlaceholder),
                NullableReferenceType nullable => ContainsInferencePlaceholder(nullable.ReferentType),
                NullableValueType nullable => ContainsInferencePlaceholder(nullable.UnderlyingType),
                ArrayType array => ContainsInferencePlaceholder(array.ElementType),
                FunctionType function => function.ParameterTypes.Any(ContainsInferencePlaceholder)
                    || ContainsInferencePlaceholder(function.ReturnType),
                _ => false
            };

    private static bool ArgumentModifierMatches(ParameterModifier parameterModifier, string? argumentModifier)
    {
        var expected = parameterModifier & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
        var actual = argumentModifier switch
        {
            null or "" => ParameterModifier.None,
            "ref" => ParameterModifier.Ref,
            "out" => ParameterModifier.Out,
            "in" => ParameterModifier.In,
            _ => (ParameterModifier)(-1)
        };
        return expected == actual;
    }

    private static void InferTypeArguments(
        CalorType parameter,
        CalorType argument,
        IDictionary<string, CalorType> substitutions)
    {
        if (parameter is TypeParameterType typeParameter)
        {
            if (!substitutions.TryGetValue(typeParameter.Name, out var existing))
            {
                substitutions[typeParameter.Name] = argument;
            }
            else if (IsAssignable(existing, argument))
            {
                substitutions[typeParameter.Name] = existing;
            }
            else if (IsAssignable(argument, existing))
            {
                substitutions[typeParameter.Name] = argument;
            }
            else if (TryJoinErasedRefinements(existing, argument, out var erasedJoin))
            {
                substitutions[typeParameter.Name] = erasedJoin;
            }
            else
            {
                substitutions[typeParameter.Name] = ErrorType.Instance;
            }
            return;
        }
        if (parameter is GenericInstanceType parameterGeneric
            && argument is GenericInstanceType argumentGeneric
            && parameterGeneric.BaseName.Equals(argumentGeneric.BaseName, StringComparison.OrdinalIgnoreCase)
            && parameterGeneric.TypeArguments.Count == argumentGeneric.TypeArguments.Count)
        {
            for (var i = 0; i < parameterGeneric.TypeArguments.Count; i++)
                InferTypeArguments(parameterGeneric.TypeArguments[i], argumentGeneric.TypeArguments[i], substitutions);
        }
        else if (parameter is NullableReferenceType parameterNullable
            && argument is NullableReferenceType argumentNullable)
        {
            InferTypeArguments(parameterNullable.ReferentType, argumentNullable.ReferentType, substitutions);
        }
        else if (parameter is NullableValueType parameterValue
            && argument is NullableValueType argumentValue)
        {
            InferTypeArguments(parameterValue.UnderlyingType, argumentValue.UnderlyingType, substitutions);
        }
        else if (parameter is ArrayType parameterArray && argument is ArrayType argumentArray
            && parameterArray.Rank == argumentArray.Rank)
        {
            InferTypeArguments(parameterArray.ElementType, argumentArray.ElementType, substitutions);
        }
    }

    private bool InferTypeArgumentsFromMethodGroup(
        CalorType delegateTemplate,
        IReadOnlyList<FunctionCandidate> methodCandidates,
        IDictionary<string, CalorType> substitutions)
    {
        if (!TryGetDelegateFunctionType(delegateTemplate, out var targetTemplate))
            return false;

        var applicable = new List<(
            FunctionCandidate Method,
            Dictionary<string, CalorType> Substitutions,
            FunctionType Target,
            IReadOnlyList<int> Costs)>();
        foreach (var method in methodCandidates)
        {
            var resolvedMethod = ResolveMethodCandidateAgainstTarget(targetTemplate, method);
            if (resolvedMethod.Type.ParameterTypes.Count != targetTemplate.ParameterTypes.Count)
                continue;

            var trial = new Dictionary<string, CalorType>(substitutions, StringComparer.Ordinal);
            for (var i = 0; i < targetTemplate.ParameterTypes.Count; i++)
            {
                InferTypeArguments(
                    targetTemplate.ParameterTypes[i], resolvedMethod.Type.ParameterTypes[i], trial);
            }
            InferTypeArguments(targetTemplate.ReturnType, resolvedMethod.Type.ReturnType, trial);

            var resolvedTarget = (FunctionType)SubstituteTypeParameters(targetTemplate, trial);
            if (!IsMethodGroupCompatible(resolvedTarget, resolvedMethod))
                continue;
            applicable.Add((resolvedMethod, trial, resolvedTarget,
                GetMethodGroupCandidateCosts(resolvedTarget, resolvedMethod)));
        }
        if (applicable.Count == 0)
            return false;

        var best = applicable.Where((candidate, candidateIndex) => !applicable.Where(
                (_, otherIndex) => otherIndex != candidateIndex).Any(other =>
                IsBetterMethodGroupCandidate(
                    other.Method, other.Costs, candidate.Method, candidate.Costs)))
            .ToArray();
        if (best.Length != 1)
            return false;
        foreach (var (name, type) in best[0].Substitutions)
            substitutions[name] = type;
        return true;
    }

    private void InferTypeArgumentsFromLambda(
        CalorType delegateTemplate,
        LambdaExpressionNode lambda,
        IDictionary<string, CalorType> substitutions)
    {
        if (!TryGetDelegateFunctionType(delegateTemplate, out var targetTemplate)
            || targetTemplate.ParameterTypes.Count != lambda.Parameters.Count)
        {
            return;
        }

        for (var i = 0; i < lambda.Parameters.Count; i++)
        {
            if (lambda.Parameters[i].TypeName is { } declaredTypeName)
            {
                InferTypeArguments(
                    targetTemplate.ParameterTypes[i],
                    ResolveTypeName(declaredTypeName, lambda.Parameters[i].Span),
                    substitutions);
            }
        }

        var checkpoint = BeginSpeculation();
        var previousSuppressContextualDiagnostics = _suppressContextualDiagnostics;
        var previousReturnType = _currentReturnType;
        var previousValidateReturnAssignments = _validateReturnAssignments;
        var previousInferredLambdaReturnTypes = _inferredLambdaReturnTypes;
        _suppressContextualDiagnostics = true;
        _currentReturnType = null;
        _validateReturnAssignments = false;
        _inferredLambdaReturnTypes = new List<CalorType>();
        var previousInlineRefinedParameters = _inlineRefinedParameters;
        _inlineRefinedParameters = WithoutShadowedNames(_inlineRefinedParameters, lambda);
        _env.EnterScope();
        var currentSubstitutions =
            new Dictionary<string, CalorType>(substitutions, StringComparer.Ordinal);
        for (var i = 0; i < lambda.Parameters.Count; i++)
        {
            var parameterType = lambda.Parameters[i].TypeName is { } declaredTypeName
                ? ResolveTypeName(declaredTypeName, lambda.Parameters[i].Span)
                : SubstituteTypeParameters(targetTemplate.ParameterTypes[i], currentSubstitutions);
            _env.DefineVariable(lambda.Parameters[i].Name, parameterType);
        }
        _lambdaBodyDepth++;
        try
        {
            if (lambda.ExpressionBody != null)
            {
                _inferredLambdaReturnTypes.Add(InferExpressionType(lambda.ExpressionBody));
            }
            else
            {
                foreach (var statement in lambda.StatementBody ?? Array.Empty<StatementNode>())
                {
                    CheckStatement(statement);
                }
            }
        }
        finally
        {
            _lambdaBodyDepth--;
        }
        _env.ExitScope();
        _inlineRefinedParameters = previousInlineRefinedParameters;
        foreach (var returnType in _inferredLambdaReturnTypes)
        {
            var callableResult = lambda.IsAsync
                ? new GenericInstanceType("System.Threading.Tasks.Task", [returnType])
                : returnType;
            InferTypeArguments(targetTemplate.ReturnType, callableResult, substitutions);
        }
        _inferredLambdaReturnTypes = previousInferredLambdaReturnTypes;
        _currentReturnType = previousReturnType;
        _validateReturnAssignments = previousValidateReturnAssignments;
        _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
        EndSpeculation(checkpoint);
    }

    private bool TryGetMethodGroupConversionCost(
        FunctionType target,
        IReadOnlyList<FunctionCandidate> candidates,
        out int cost)
    {
        var applicable = candidates
            .Select(candidate => ResolveMethodCandidateAgainstTarget(target, candidate))
            .Where(candidate => IsMethodGroupCompatible(target, candidate))
            .Select(candidate => (
                Candidate: candidate,
                Costs: GetMethodGroupCandidateCosts(target, candidate)))
            .ToArray();
        if (applicable.Length == 0)
        {
            cost = 0;
            return false;
        }
        var best = applicable.Where((candidate, candidateIndex) => !applicable.Where(
                (_, otherIndex) => otherIndex != candidateIndex).Any(other =>
                IsBetterMethodGroupCandidate(
                    other.Candidate, other.Costs, candidate.Candidate, candidate.Costs)))
            .ToArray();
        if (best.Length != 1)
        {
            cost = 0;
            return false;
        }
        cost = best[0].Costs.Sum();
        return true;
    }

    private static FunctionCandidate ResolveMethodCandidateAgainstTarget(
        FunctionType target,
        FunctionCandidate candidate)
    {
        if (candidate.TypeParameterNames.Count == 0)
            return candidate;

        var substitutions = new Dictionary<string, CalorType>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(
            target.ParameterTypes.Count, candidate.Type.ParameterTypes.Count); i++)
        {
            InferTypeArguments(
                candidate.Type.ParameterTypes[i], target.ParameterTypes[i], substitutions);
        }
        if (!ContainsInferencePlaceholder(target.ReturnType))
            InferTypeArguments(candidate.Type.ReturnType, target.ReturnType, substitutions);
        return candidate with
        {
            Type = new FunctionType(
                candidate.Type.ParameterTypes
                    .Select(parameter => SubstituteTypeParameters(parameter, substitutions))
                    .ToArray(),
                SubstituteTypeParameters(candidate.Type.ReturnType, substitutions))
        };
    }

    private static IReadOnlyList<int> GetMethodGroupCandidateCosts(
        FunctionType target,
        FunctionCandidate candidate)
        => target.ParameterTypes.Select((parameter, index) =>
            GetImplicitConversionCost(candidate.Type.ParameterTypes[index], parameter)).ToArray();

    private static bool IsBetterMethodGroupCandidate(
        FunctionCandidate left,
        IReadOnlyList<int> leftCosts,
        FunctionCandidate right,
        IReadOnlyList<int> rightCosts)
    {
        var strictlyBetter = false;
        for (var i = 0; i < leftCosts.Count; i++)
        {
            if (leftCosts[i] > rightCosts[i])
                return false;
            strictlyBetter |= leftCosts[i] < rightCosts[i];
        }
        if (strictlyBetter)
            return true;
        return left.TypeParameterNames.Count == 0 && right.TypeParameterNames.Count > 0;
    }

    private static CalorType SubstituteTypeParameters(
        CalorType type,
        IReadOnlyDictionary<string, CalorType> substitutions)
        => type switch
        {
            TypeParameterType parameter when substitutions.TryGetValue(parameter.Name, out var replacement)
                => replacement,
            GenericInstanceType generic => new GenericInstanceType(
                generic.BaseName,
                generic.TypeArguments.Select(argument => SubstituteTypeParameters(argument, substitutions)).ToArray())
                { NativeDeclaration = generic.NativeDeclaration },
            NullableReferenceType nullable => new NullableReferenceType(
                SubstituteTypeParameters(nullable.ReferentType, substitutions),
                nullable.RequiresTransitionalAssignmentCheck),
            NullableValueType nullable => new NullableValueType(
                SubstituteTypeParameters(nullable.UnderlyingType, substitutions)),
            ArrayType array => new ArrayType(
                SubstituteTypeParameters(array.ElementType, substitutions), array.Rank),
            FunctionType function => new FunctionType(
                function.ParameterTypes.Select(parameter => SubstituteTypeParameters(parameter, substitutions)).ToArray(),
                SubstituteTypeParameters(function.ReturnType, substitutions),
                function.ParameterNames,
                function.ParameterModifiers,
                function.DelegateName),
            _ => type
        };

    private bool ValidateDelegateCall(
        FunctionType function,
        IReadOnlyList<CalorType> argumentTypes,
        IReadOnlyList<ExpressionNode> arguments,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers,
        TextSpan callSpan)
    {
        if (function.ParameterTypes.Count != argumentTypes.Count)
        {
            _diagnostics.ReportError(callSpan, DiagnosticCode.NoMatchingOverload,
                $"Delegate expects {function.ParameterTypes.Count} argument(s), but received {argumentTypes.Count}");
            return false;
        }

        var parameterIndices = TryMapDelegateArgumentIndices(function, argumentTypes.Count, argumentNames);
        if (parameterIndices == null)
        {
            _diagnostics.ReportError(callSpan, DiagnosticCode.NoMatchingOverload,
                "Delegate arguments do not map uniquely to parameters");
            return false;
        }

        var valid = true;
        for (var argumentIndex = 0; argumentIndex < argumentTypes.Count; argumentIndex++)
        {
            var parameterIndex = parameterIndices[argumentIndex];
            var expectedModifier = function.ParameterModifiers != null
                && parameterIndex < function.ParameterModifiers.Count
                    ? function.ParameterModifiers[parameterIndex]
                    : ParameterModifier.None;
            var actualModifier = argumentModifiers != null && argumentIndex < argumentModifiers.Count
                ? argumentModifiers[argumentIndex]
                : null;
            if (!ArgumentModifierMatches(expectedModifier, actualModifier))
            {
                _diagnostics.ReportError(arguments[argumentIndex].Span, DiagnosticCode.NoMatchingOverload,
                    $"Argument modifier does not match delegate parameter {parameterIndex + 1}");
                valid = false;
                continue;
            }
            var requiresIdentity = (expectedModifier
                & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In)) != 0;
            var typeMatches = requiresIdentity
                ? function.ParameterTypes[parameterIndex].Equals(argumentTypes[argumentIndex])
                : IsAssignable(function.ParameterTypes[parameterIndex], argumentTypes[argumentIndex]);
            if (!typeMatches)
            {
                if (IsDistinctNamedRefinement(function.ParameterTypes[parameterIndex], argumentTypes[argumentIndex]))
                {
                    ReportDistinctNamedRefinement(arguments[argumentIndex].Span,
                        function.ParameterTypes[parameterIndex], argumentTypes[argumentIndex],
                        $"delegate argument {argumentIndex + 1}");
                }
                else
                {
                    _diagnostics.ReportError(arguments[argumentIndex].Span, DiagnosticCode.TypeMismatch,
                        $"Argument type {argumentTypes[argumentIndex].SurfaceName} is not assignable to "
                        + function.ParameterTypes[parameterIndex].SurfaceName);
                }
                valid = false;
            }
        }
        return valid;
    }

    private static int[]? TryMapDelegateArgumentIndices(
        FunctionType function,
        int argumentCount,
        IReadOnlyList<string?>? argumentNames)
    {
        if (function.ParameterTypes.Count != argumentCount)
            return null;

        var parameterIndices = new int[argumentCount];
        var assigned = new bool[argumentCount];
        var nextPositional = 0;
        var seenOutOfPositionNamedArgument = false;
        for (var argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
        {
            var name = argumentNames != null && argumentIndex < argumentNames.Count
                ? argumentNames[argumentIndex]
                : null;
            int parameterIndex;
            if (string.IsNullOrEmpty(name))
            {
                if (seenOutOfPositionNamedArgument)
                    return null;
                while (nextPositional < assigned.Length && assigned[nextPositional])
                    nextPositional++;
                parameterIndex = nextPositional++;
            }
            else
            {
                var matches = function.ParameterNames?.Select((parameterName, index) => (parameterName, index))
                    .Where(item => item.parameterName.Equals(name, StringComparison.Ordinal))
                    .Select(item => item.index)
                    .ToArray() ?? Array.Empty<int>();
                if (matches.Length != 1)
                    return null;
                parameterIndex = matches[0];
                while (nextPositional < assigned.Length && assigned[nextPositional])
                    nextPositional++;
                seenOutOfPositionNamedArgument |= parameterIndex != nextPositional;
            }

            if (parameterIndex < 0 || parameterIndex >= assigned.Length || assigned[parameterIndex])
                return null;
            parameterIndices[argumentIndex] = parameterIndex;
            assigned[parameterIndex] = true;
        }
        return parameterIndices;
    }

    private bool IsMethodGroupCompatible(FunctionType target, FunctionCandidate candidate)
    {
        if (target.ParameterTypes.Count != candidate.Type.ParameterTypes.Count)
            return false;
        for (var i = 0; i < target.ParameterTypes.Count; i++)
        {
            var targetModifier = target.ParameterModifiers != null && i < target.ParameterModifiers.Count
                ? target.ParameterModifiers[i] & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In)
                : ParameterModifier.None;
            var candidateModifier = candidate.ParameterModifiers[i]
                & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
            if (targetModifier != candidateModifier)
                return false;
        }

        var substitutions = new Dictionary<string, CalorType>(StringComparer.Ordinal);
        for (var i = 0; i < target.ParameterTypes.Count; i++)
        {
            InferTypeArguments(candidate.Type.ParameterTypes[i], target.ParameterTypes[i], substitutions);
        }

        var candidateParameters = candidate.Type.ParameterTypes
            .Select(parameter => SubstituteTypeParameters(parameter, substitutions))
            .ToArray();
        var candidateReturn = SubstituteTypeParameters(candidate.Type.ReturnType, substitutions);
        return candidateParameters.Zip(target.ParameterTypes)
                .All(pair => IsMethodGroupParameterCompatible(pair.First, pair.Second))
            && IsMethodGroupReturnCompatible(target.ReturnType, candidateReturn);
    }

    private bool IsMethodGroupParameterCompatible(CalorType target, CalorType source)
    {
        if (target.Equals(source))
            return true;
        if (TryGetDelegateFunctionType(target, out var targetDelegate)
            && TryGetDelegateFunctionType(source, out var sourceDelegate))
        {
            return IsDelegateReferenceCompatible(targetDelegate, sourceDelegate);
        }
        if (IsPrimitiveValueType(source)
            || source is NullableValueType
            || _moduleDeclaredValueTypes.Contains(source.Name)
            || IsPrimitiveValueType(target)
            || target is NullableValueType
            || _moduleDeclaredValueTypes.Contains(target.Name))
        {
            return false;
        }
        return IsAssignable(target, source);
    }

    private bool IsDelegateReferenceCompatible(FunctionType target, FunctionType source)
    {
        if (target.DelegateName != null || source.DelegateName != null)
        {
            return target.DelegateName != null
                && target.DelegateName.Equals(source.DelegateName, StringComparison.Ordinal);
        }
        if (target.ParameterTypes.Count != source.ParameterTypes.Count)
            return false;
        for (var i = 0; i < target.ParameterTypes.Count; i++)
        {
            if (!IsMethodGroupParameterCompatible(
                source.ParameterTypes[i], target.ParameterTypes[i]))
            {
                return false;
            }
        }
        return IsMethodGroupReturnCompatible(target.ReturnType, source.ReturnType);
    }

    private bool IsMethodGroupReturnCompatible(CalorType target, CalorType source)
    {
        if (target.Equals(source) || source is NeverType)
            return true;
        if (TryGetDelegateFunctionType(target, out var targetDelegate)
            && TryGetDelegateFunctionType(source, out var sourceDelegate))
        {
            return IsDelegateReferenceCompatible(targetDelegate, sourceDelegate);
        }
        if (source is PrimitiveType primitive
            && !primitive.Equals(PrimitiveType.String)
            && !primitive.Equals(PrimitiveType.Object))
        {
            return false;
        }
        if (source is NullableValueType || _moduleDeclaredValueTypes.Contains(source.Name))
            return false;
        return IsAssignable(target, source);
    }

    private CalorType InferIsPatternType(IsPatternNode isPattern)
    {
        var operandType = InferExpressionType(isPattern.Operand);
        var targetType = isPattern.TargetType == "var"
            ? operandType
            : InferTypePatternBindingType(isPattern.TargetType, isPattern.TargetTypeSpan, isPattern.Span);
        _patternBindingTypes[isPattern] = targetType;
        if (isPattern.TargetType == "var")
            return PrimitiveType.Bool;

        if (!CanPossiblyMatchPattern(operandType, targetType))
        {
            _diagnostics.ReportError(isPattern.Span, DiagnosticCode.TypeMismatch,
                $"Pattern type {targetType.SurfaceName} is not compatible with input type {operandType.SurfaceName}");
        }

        return PrimitiveType.Bool;
    }

    private CalorType CommonConditionalType(Parsing.TextSpan span, CalorType trueType, CalorType falseType)
    {
        if (trueType is NeverType && falseType is NeverType) return NeverType.Instance;
        if (trueType is NeverType) return falseType;
        if (falseType is NeverType) return trueType;
        if (trueType is ErrorType || falseType is ErrorType) return ErrorType.Instance;
        if (trueType.Equals(falseType)) return trueType;
        if (trueType is TypeVariable) return falseType;
        if (falseType is TypeVariable) return trueType;

        if (trueType is GenericInstanceType trueGeneric
            && falseType is GenericInstanceType falseGeneric
            && trueGeneric.TypeArguments.Count == 1 && falseGeneric.TypeArguments.Count == 1
            && Binding.NullabilityChecker.SameGenericDefinition(trueGeneric.BaseName, falseGeneric.BaseName)
            && (trueGeneric.TypeArguments[0].Equals(falseGeneric.TypeArguments[0])
                || IsBinderOwnedNullabilityMismatch(trueGeneric, falseGeneric)))
        {
            return new GenericInstanceType(trueGeneric.BaseName,
                [CommonConditionalType(span, trueGeneric.TypeArguments[0], falseGeneric.TypeArguments[0])]);
        }

        if (TryUnifyNullableReferences(trueType, falseType, out var nullableReference))
        {
            return nullableReference;
        }

        if (TryUnifyNullableValues(trueType, falseType, out var nullableValue))
        {
            return nullableValue;
        }

        if (IsAssignable(trueType, falseType)) return trueType;
        if (IsAssignable(falseType, trueType)) return falseType;
        if (TryJoinErasedRefinements(trueType, falseType, out var erasedJoin)) return erasedJoin;

        // C# permits unlike arms when their enclosing target is object.
        // Preserve a real common type; a narrower target still fails assignment.
        return PrimitiveType.Object;
    }

    private static bool TryUnifyNullableReferences(
        CalorType left,
        CalorType right,
        out NullableReferenceType nullable)
    {
        var leftNullable = left as NullableReferenceType;
        var rightNullable = right as NullableReferenceType;
        var leftReferent = leftNullable?.ReferentType ?? left;
        var rightReferent = rightNullable?.ReferentType ?? right;

        if (left is NullType && (right.Equals(PrimitiveType.String) || right.Equals(PrimitiveType.Object)))
        {
            nullable = new NullableReferenceType(right);
            return true;
        }
        if (right is NullType && (left.Equals(PrimitiveType.String) || left.Equals(PrimitiveType.Object)))
        {
            nullable = new NullableReferenceType(left);
            return true;
        }

        if ((leftNullable != null || rightNullable != null)
            && left is not NullType
            && right is not NullType
            && IsAssignable(leftReferent, rightReferent))
        {
            nullable = new NullableReferenceType(
                leftReferent,
                (leftNullable?.RequiresTransitionalAssignmentCheck ?? false)
                    || (rightNullable?.RequiresTransitionalAssignmentCheck ?? false));
            return true;
        }

        if (leftNullable != null && right is NullType)
        {
            nullable = leftNullable;
            return true;
        }

        if (rightNullable != null && left is NullType)
        {
            nullable = rightNullable;
            return true;
        }

        nullable = null!;
        return false;
    }

    private static bool TryUnifyNullableValues(CalorType left, CalorType right, out NullableValueType nullable)
    {
        var leftNullable = left as NullableValueType;
        var rightNullable = right as NullableValueType;
        var leftUnderlying = leftNullable?.UnderlyingType ?? left;
        var rightUnderlying = rightNullable?.UnderlyingType ?? right;

        if ((leftNullable != null || rightNullable != null)
            && left is not NullType
            && right is not NullType)
        {
            if (IsAssignable(leftUnderlying, rightUnderlying))
            {
                nullable = new NullableValueType(leftUnderlying);
                return true;
            }
            if (IsAssignable(rightUnderlying, leftUnderlying))
            {
                nullable = new NullableValueType(rightUnderlying);
                return true;
            }
        }

        if (leftNullable != null && right is NullType)
        {
            nullable = leftNullable;
            return true;
        }

        if (rightNullable != null && left is NullType)
        {
            nullable = rightNullable;
            return true;
        }

        if (left is NullType && IsPrimitiveValueType(right))
        {
            nullable = new NullableValueType(right);
            return true;
        }

        if (right is NullType && IsPrimitiveValueType(left))
        {
            nullable = new NullableValueType(left);
            return true;
        }

        nullable = null!;
        return false;
    }

    private void DefineTrueConditionPatternVariables(ExpressionNode condition)
    {
        if (condition is BinaryOperationNode { Operator: BinaryOperator.And } conjunction)
        {
            DefineTrueConditionPatternVariables(conjunction.Left);
            DefineTrueConditionPatternVariables(conjunction.Right);
        }
        if (condition is IsPatternNode { VariableName: { Length: > 0 } name } isPattern)
        {
            // The condition was already checked; transfer its type without repeating diagnostics.
            _env.DefineVariable(name, _patternBindingTypes[isPattern]);
        }
    }

    private CalorType InferTypePatternBindingType(
        string typeName,
        Parsing.TextSpan typeNameSpan,
        Parsing.TextSpan fallbackSpan)
    {
        var span = typeNameSpan == Parsing.TextSpan.Empty ? fallbackSpan : typeNameSpan;
        var type = ResolveTypeName(typeName, span);
        type = type switch
        {
            NullableReferenceType nullable => nullable.ReferentType,
            NullableValueType nullable => nullable.UnderlyingType,
            _ => type
        };
        // A refinement erases at run time, so `(is a Pos p)` emits `a is int p`: it tests only
        // the base type and would bind an unchecked value to a Pos-typed variable (#1530 review).
        if (type is RefinedType refined)
        {
            ReportRefinementSoundnessError(span,
                $"A type pattern cannot test the refinement type {refined.SurfaceName}: refinements "
                + $"erase at run time, so only the base type {refined.BaseType.SurfaceName} would be "
                + "tested. Test the base type and check the predicate explicitly.");
            return EraseRefinement(refined);
        }
        return type;
    }

    private static bool CanPossiblyMatchPattern(CalorType inputType, CalorType patternType)
    {
        if (inputType is ErrorType or ExternalType or NeverType or NullType) return true;
        if (patternType is ErrorType or ExternalType or NeverType) return true;
        if (inputType.Equals(PrimitiveType.Object) || patternType.Equals(PrimitiveType.Object)) return true;

        var input = inputType switch
        {
            NullableReferenceType nullable => nullable.ReferentType,
            NullableValueType nullable => nullable.UnderlyingType,
            _ => inputType
        };
        var pattern = patternType switch
        {
            NullableReferenceType nullable => nullable.ReferentType,
            NullableValueType nullable => nullable.UnderlyingType,
            _ => patternType
        };

        // Refinements erase at runtime, so a type test sees only the base types (#1530).
        input = EraseRefinement(input);
        pattern = EraseRefinement(pattern);
        return input.Equals(pattern)
            || IsAssignable(input, pattern)
            || IsAssignable(pattern, input);
    }

    private static bool IsSupportedThrowException(ExpressionNode exception, CalorType exceptionType)
    {
        if (exception is StringLiteralNode or InterpolatedStringNode
            or IntLiteralNode or BoolLiteralNode or FloatLiteralNode
            or DecimalLiteralNode or CharOperationNode)
        {
            return true;
        }

        // Nominal inheritance and unmodeled external expressions are validated
        // by generated C#. A modeled call/new value is not exempt just because
        // of its syntax; e.g. a known str-returning call cannot be thrown.
        return exceptionType is ErrorType or ExternalType or NullType or NeverType;
    }

    private CalorType InferListCreationType(ListCreationNode list)
    {
        var elementType = ResolveTypeName(list.ElementType, list.Span);

        // Validate that all elements match the declared type
        foreach (var element in list.Elements)
        {
            var actualType = InferExpressionType(element);
            if (!IsAssignable(elementType, actualType))
            {
                _diagnostics.ReportError(element.Span, DiagnosticCode.TypeMismatch,
                    $"List element type mismatch: expected {elementType.SurfaceName}, got {actualType.SurfaceName}");
            }
        }

        // Define the variable in the current scope
        var listType = new GenericInstanceType("List", new[] { elementType });
        _env.DefineVariable(list.Name, listType);

        return listType;
    }

    private CalorType InferDictionaryCreationType(DictionaryCreationNode dict)
    {
        var keyType = ResolveTypeName(dict.KeyType, dict.Span);
        var valueType = ResolveTypeName(dict.ValueType, dict.Span);

        // Validate that all entries match the declared types
        foreach (var entry in dict.Entries)
        {
            var actualKeyType = InferExpressionType(entry.Key);
            var actualValueType = InferExpressionType(entry.Value);

            if (!IsAssignable(keyType, actualKeyType))
            {
                _diagnostics.ReportError(entry.Key.Span, DiagnosticCode.TypeMismatch,
                    $"Dictionary key type mismatch: expected {keyType.SurfaceName}, got {actualKeyType.SurfaceName}");
            }

            if (!IsAssignable(valueType, actualValueType))
            {
                _diagnostics.ReportError(entry.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Dictionary value type mismatch: expected {valueType.SurfaceName}, got {actualValueType.SurfaceName}");
            }
        }

        // Define the variable in the current scope
        var dictType = new GenericInstanceType("Dictionary", new[] { keyType, valueType });
        _env.DefineVariable(dict.Name, dictType);

        return dictType;
    }

    private CalorType InferSetCreationType(SetCreationNode set)
    {
        var elementType = ResolveTypeName(set.ElementType, set.Span);

        // Validate that all elements match the declared type
        foreach (var element in set.Elements)
        {
            var actualType = InferExpressionType(element);
            if (!IsAssignable(elementType, actualType))
            {
                _diagnostics.ReportError(element.Span, DiagnosticCode.TypeMismatch,
                    $"Set element type mismatch: expected {elementType.SurfaceName}, got {actualType.SurfaceName}");
            }
        }

        // Define the variable in the current scope
        var setType = new GenericInstanceType("HashSet", new[] { elementType });
        _env.DefineVariable(set.Name, setType);

        return setType;
    }

    private CalorType InferCollectionContainsType(CollectionContainsNode contains)
    {
        var collectionType = _env.LookupVariable(contains.CollectionName);
        if (collectionType == null)
        {
            _diagnostics.ReportError(contains.Span, DiagnosticCode.UndefinedReference,
                $"Undefined collection '{contains.CollectionName}'");
            return PrimitiveType.Bool; // Contains always returns bool
        }

        var checkType = InferExpressionType(contains.KeyOrValue);

        if (collectionType is GenericInstanceType git)
        {
            CalorType? expectedType = null;

            switch (contains.Mode)
            {
                case ContainsMode.Value:
                    // List.Contains or HashSet.Contains
                    if ((git.BaseName == "List" || git.BaseName == "HashSet") && git.TypeArguments.Count == 1)
                    {
                        expectedType = git.TypeArguments[0];
                    }
                    break;

                case ContainsMode.Key:
                    // Dictionary.ContainsKey
                    if (git.BaseName == "Dictionary" && git.TypeArguments.Count == 2)
                    {
                        expectedType = git.TypeArguments[0];
                    }
                    break;

                case ContainsMode.DictValue:
                    // Dictionary.ContainsValue
                    if (git.BaseName == "Dictionary" && git.TypeArguments.Count == 2)
                    {
                        expectedType = git.TypeArguments[1];
                    }
                    break;
            }

            if (expectedType != null && !IsAssignable(expectedType, checkType))
            {
                _diagnostics.ReportError(contains.KeyOrValue.Span, DiagnosticCode.TypeMismatch,
                    $"Contains check type mismatch: expected {expectedType.SurfaceName}, got {checkType.SurfaceName}");
            }
        }

        return PrimitiveType.Bool;
    }

    private CalorType InferCollectionCountType(CollectionCountNode count)
    {
        var collectionType = InferExpressionType(count.Collection);

        // Validate it's a collection type
        if (collectionType is GenericInstanceType git)
        {
            if (git.BaseName != "List" && git.BaseName != "Dictionary" && git.BaseName != "HashSet")
            {
                _diagnostics.ReportError(count.Collection.Span, DiagnosticCode.TypeMismatch,
                    $"CNT requires a collection type, got {collectionType.SurfaceName}");
            }
        }
        else if (collectionType is not ErrorType)
        {
            _diagnostics.ReportError(count.Collection.Span, DiagnosticCode.TypeMismatch,
                $"CNT requires a collection type, got {collectionType.SurfaceName}");
        }

        return PrimitiveType.Int;
    }

    private CalorType InferArrayAccessType(ArrayAccessNode arrayAccess)
    {
        var arrayType = InferExpressionType(arrayAccess.Array);
        var indexType = InferExpressionType(arrayAccess.Index);

        if (arrayAccess.Index is RangeExpressionNode
            && (arrayType is ArrayType || arrayType.Equals(PrimitiveType.String)))
            return arrayType;

        // Index should be numeric for arrays/lists
        if (!IsNumeric(indexType) && indexType is not ErrorType)
        {
            // Could be a dictionary with non-numeric key
            if (arrayType is GenericInstanceType git && git.BaseName == "Dictionary" && git.TypeArguments.Count == 2)
            {
                // For dictionaries, check the key type
                var expectedKeyType = git.TypeArguments[0];
                if (!IsAssignable(expectedKeyType, indexType))
                {
                    _diagnostics.ReportError(arrayAccess.Index.Span, DiagnosticCode.TypeMismatch,
                        $"Dictionary key type mismatch: expected {expectedKeyType.SurfaceName}, got {indexType.SurfaceName}");
                }
                return git.TypeArguments[1]; // Return value type
            }
            else
            {
                _diagnostics.ReportError(arrayAccess.Index.Span, DiagnosticCode.TypeMismatch,
                    $"Array/List index must be numeric, got {indexType.SurfaceName}");
            }
        }

        // Determine element type based on collection type
        if (arrayType is GenericInstanceType git2)
        {
            if (git2.BaseName == "List" && git2.TypeArguments.Count == 1)
            {
                return git2.TypeArguments[0];
            }
            if (git2.BaseName == "Dictionary" && git2.TypeArguments.Count == 2)
            {
                return git2.TypeArguments[1];
            }
        }

        return ErrorType.Instance;
    }

    private CalorType InferMultiDimArrayAccessType(MultiDimArrayAccessNode access)
    {
        var type = InferExpressionType(access.Array);
        foreach (var index in access.Indices)
        {
            var indexType = InferExpressionType(index);
            if (!IsNumeric(indexType) && indexType is not ErrorType)
                _diagnostics.ReportError(index.Span, DiagnosticCode.TypeMismatch,
                    $"Array index must be numeric, got {indexType.SurfaceName}");
        }
        if (type is not ArrayType array)
            return ErrorType.Instance;
        if (array.Rank != access.Indices.Count)
        {
            _diagnostics.ReportError(access.Span, DiagnosticCode.TypeMismatch,
                $"Array of rank {array.Rank} requires {array.Rank} indices, got {access.Indices.Count}");
            return ErrorType.Instance;
        }
        return array.ElementType;
    }

    private CalorType InferTypeOperationType(TypeOperationNode typeOp)
    {
        var targetType = ResolveTypeName(typeOp.TargetType, typeOp.Span);

        if (typeOp.Operation == TypeOp.As && IsValueType(targetType))
        {
            _diagnostics.ReportWarning(typeOp.Span, DiagnosticCode.TypeMismatch,
                $"The 'as' operator cannot be used with value type '{typeOp.TargetType}'. " +
                $"Use '(cast {typeOp.TargetType} ...)' instead.");
        }

        return typeOp.Operation switch
        {
            TypeOp.Is => PrimitiveType.Bool,
            TypeOp.Cast => targetType,
            TypeOp.As => targetType,
            _ => ErrorType.Instance
        };
    }

    private static bool IsValueType(CalorType type)
    {
        return IsPrimitiveValueType(type);
    }

    private static CalorType? ResolveNullableValueReferent(string typeName)
    {
        var referent = PrimitiveType.FromName(Parsing.AttributeHelper.ToSurfaceSpelling(typeName));
        return referent is not null && IsPrimitiveValueType(referent) ? referent : null;
    }

    private static bool IsPrimitiveValueType(CalorType type)
        => type.Equals(PrimitiveType.Int)
            || type.Equals(PrimitiveType.Float)
            || type.Equals(PrimitiveType.Bool)
            || type.Equals(PrimitiveType.Char)
            || type.Equals(PrimitiveType.Decimal);

    private CalorType InferReferenceType(ReferenceNode refNode)
    {
        if (refNode.IsNullLiteral)
            return NullType.Instance;

        // `this.x` inside a class member is the member's declared type (#1676).
        if (TryGetThisMemberName(refNode.Name, out var thisMember) && LookupThisMember(thisMember) is { } memberType)
            return memberType;
        var type = _env.LookupVariable(refNode.Name);
        if (type != null)
        {
            return type;
        }

        var functionType = _env.LookupFunction(refNode.Name);
        if (functionType != null)
        {
            return functionType;
        }
        if (_env.LookupFunctionCandidates(refNode.Name).Count > 0)
        {
            return ErrorType.Instance;
        }

        // A dotted reference whose head is not a local is a MEMBER ACCESS, not a variable:
        // `Math.PI`, `int.MaxValue`, `StringComparison.Ordinal`, `System.Environment.NewLine`.
        // The checker models no BCL surface, so it cannot type these — but reporting them as
        // "Undefined variable" is worse than saying nothing: it rejects programs that compile
        // and run correctly, and the emitter passes the path through to C# verbatim. Yield to
        // the C# compiler, which does know these, rather than inventing a verdict.
        //
        // C# expression keywords that reach the checker as bare references. `default` is the one
        // observed (generic code emits `default`); the others are listed because they arrive by
        // the same route and reporting any of them as an undefined VARIABLE is simply wrong.
        if (!refNode.IsVerbatim && refNode.Name is ("default" or "this" or "base" or "value"))
        {
            return ErrorType.Instance;
        }

        // Deliberately NOT applied to a bare identifier: `Undefined variable 'x'` is a real and
        // useful error, and this must not weaken it.
        if (refNode.Name.Contains('.'))
        {
            // Both cases — an unknown head (`Math.PI`) and a known local's member
            // (`someLocal.Length`) — are unmodeled here, so they get the same answer. Reporting
            // the second as an unknown MEMBER was considered and rejected: without a member table
            // the checker cannot separate `n.NoSuchField` from `n.ToString`, and `ToString` is
            // common enough that reporting would reintroduce exactly the false-positive class
            // this change set removes.
            return ErrorType.Instance;
        }

        _diagnostics.ReportError(refNode.Span, DiagnosticCode.UndefinedReference,
            $"Undefined variable '{refNode.Name}'");
        return ErrorType.Instance;
    }

    private CalorType InferBinaryOperationType(BinaryOperationNode binOp)
    {
        var leftType = InferExpressionType(binOp.Left);
        CalorType rightType;
        if (binOp.Operator == BinaryOperator.And)
        {
            _env.EnterScope();
            DefineTrueConditionPatternVariables(binOp.Left);
            rightType = InferExpressionType(binOp.Right);
            _env.ExitScope();
        }
        else
            rightType = InferExpressionType(binOp.Right);

        if (_module is not null)
        {
            _sourceBindings ??= Analysis.CallGraphAnalysis.ResolveOperatorCalls(
                _module, _diagnostics, ProjectionContractMode);
            if (_sourceBindings.ResultTypes.TryGetValue(binOp, out var operatorResult))
                return ResolveTypeName(operatorResult, binOp.Span);
        }

        // Operators see a named refinement (§RTYPE) as its base type, as they already see an
        // inline-refined parameter. The result is the base type, never the refinement: `NatInt -
        // NatInt` can be negative. Refinement checks on a result written back into a refined
        // variable or return are the obligation engine's job and are unchanged. #1515.
        leftType = EraseRefinement(leftType);
        rightType = EraseRefinement(rightType);

        // Comparison operators return BOOL
        if (binOp.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
            or BinaryOperator.LessThan or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual)
        {
            return PrimitiveType.Bool;
        }

        // Logical operators require BOOL operands
        if (binOp.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            if (IsDefinitelyNotBool(leftType) || IsDefinitelyNotBool(rightType))
            {
                _diagnostics.ReportError(binOp.Span, DiagnosticCode.TypeMismatch,
                    "Logical operators require bool operands");
            }
            return PrimitiveType.Bool;
        }

        if (binOp.Operator is BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseOr or BinaryOperator.BitwiseXor
            && leftType.Equals(PrimitiveType.Bool) && rightType.Equals(PrimitiveType.Bool))
            return PrimitiveType.Bool;

        // `+` on strings is CONCATENATION, exactly as in the emitted C#. Rejecting it as
        // "requires numeric operands" made the checker refuse a working, documented program —
        // the MCP primer's own §M{m3:Files} module among them.
        if (binOp.Operator == BinaryOperator.Add
            && (IsStringReference(leftType) || IsStringReference(rightType)))
        {
            // C#'s string + T binds for any T via ToString(), and that is what the emitter
            // produces, so the other operand is unconstrained.
            return PrimitiveType.String;
        }

        // C# has no implicit conversion between `decimal` and the binary floating types, in
        // either direction — `decimal + double` is CS0019. Both are "numeric", so the family
        // check has to be separate or the checker accepts a program the emitted C# rejects.
        if ((leftType.Equals(PrimitiveType.Decimal) && rightType.Equals(PrimitiveType.Float))
            || (leftType.Equals(PrimitiveType.Float) && rightType.Equals(PrimitiveType.Decimal)))
        {
            _diagnostics.ReportError(binOp.Span, DiagnosticCode.TypeMismatch,
                $"Cannot mix {leftType.SurfaceName} and {rightType.SurfaceName} in arithmetic: " +
                "C# has no implicit conversion between decimal and floating-point types");
            return ErrorType.Instance;
        }

        // Arithmetic operators
        if (!IsNumericType(leftType) || !IsNumericType(rightType))
        {
            if (!(leftType is ErrorType) && !(rightType is ErrorType))
            {
                _diagnostics.ReportError(binOp.Span, DiagnosticCode.TypeMismatch,
                    $"Arithmetic operators require numeric operands, got {leftType.SurfaceName} and {rightType.SurfaceName}");
            }
            return ErrorType.Instance;
        }

        // `**` lowers to Math.Pow, which only exists for double.
        if (binOp.Operator == BinaryOperator.Power
            && !leftType.Equals(PrimitiveType.Decimal) && !rightType.Equals(PrimitiveType.Decimal))
        {
            return PrimitiveType.ExactNumeric("f64");
        }

        if (leftType.Equals(PrimitiveType.Float) || rightType.Equals(PrimitiveType.Float))
        {
            return PromoteFloatingOperands(leftType, rightType);
        }

        // decimal op (decimal | integer | char) is decimal in C#. Falling through to Int typed
        // `decimal + decimal` as i32, which let `§B{x:i32} (+ a b)` pass the checker (and fail in
        // Roslyn) and let a nested `(+ (+ a b) someDouble)` dodge the decimal/float check above.
        // Reached far more often once named decimal refinements stopped being rejected (#1515).
        if (leftType.Equals(PrimitiveType.Decimal) || rightType.Equals(PrimitiveType.Decimal))
        {
            // Only when BOTH sides are modeled. Against an unmodeled operand (ErrorType) C# may
            // pick a user-defined operator with any result type (`decimal + Box -> double`), so
            // claiming decimal would invent a decimal/float conflict. Yield to Roslyn instead.
            return leftType is ErrorType || rightType is ErrorType
                ? ErrorType.Instance
                : PrimitiveType.Decimal;
        }

        return PromoteIntegerOperands(binOp.Operator, leftType, rightType);
    }

    /// <summary>
    /// C# binary numeric promotion (§12.4.7.3) when a floating operand is involved: double if
    /// either side is double, otherwise float (every integer type converts to float). An operand
    /// of unknown width or an unmodeled operand gives the width-less <see cref="PrimitiveType.Float"/>.
    /// </summary>
    private static CalorType PromoteFloatingOperands(CalorType left, CalorType right)
    {
        var sawUnknown = false;
        foreach (var operand in new[] { left, right })
        {
            if (operand is not PrimitiveType primitive)
            {
                sawUnknown = true;
                continue;
            }
            if (!primitive.Equals(PrimitiveType.Float))
                continue;
            if (primitive.NumericWidth == "f64")
                return PrimitiveType.ExactNumeric("f64");
            if (primitive.NumericWidth == null)
                sawUnknown = true;
        }
        return sawUnknown ? PrimitiveType.Float : PrimitiveType.ExactNumeric("f32");
    }

    /// <summary>
    /// C# binary numeric promotion over integer operands (§12.4.7.3), and the shift rule (the
    /// result is the promoted LEFT operand, §12.11). Gives the width-less
    /// <see cref="PrimitiveType.Int"/> whenever the C# result depends on what the checker does not
    /// track: an unknown width, an unmodeled operand, or a constant operand beside <c>uint</c> or
    /// <c>ulong</c> (C# then converts the constant, so <c>u + 1</c> is <c>uint</c>). Two constant
    /// operands give a constant of unknown value, which never reports a narrowing.
    /// </summary>
    private static CalorType PromoteIntegerOperands(BinaryOperator op, CalorType left, CalorType right)
    {
        if (left is not PrimitiveType leftPrimitive || right is not PrimitiveType rightPrimitive)
            return PrimitiveType.Int;
        var leftWidth = IntegerPromotionWidth(leftPrimitive);
        var rightWidth = IntegerPromotionWidth(rightPrimitive);
        if (leftWidth is null || rightWidth is null)
            return PrimitiveType.Int;
        var bothConstant = leftPrimitive.IsIntegerConstant && rightPrimitive.IsIntegerConstant;

        string? width;
        if (op is BinaryOperator.LeftShift or BinaryOperator.RightShift)
        {
            width = PromoteUnaryIntegerWidth(leftWidth);
        }
        else
        {
            var anyConstant = leftPrimitive.IsIntegerConstant || rightPrimitive.IsIntegerConstant;
            static bool IsSigned(string width) => width is "i8" or "i16" or "i32" or "i64";
            if (leftWidth == "u64" || rightWidth == "u64")
                width = anyConstant || IsSigned(leftWidth) || IsSigned(rightWidth) ? null : "u64";
            else if (leftWidth == "i64" || rightWidth == "i64")
                width = "i64";
            else if (leftWidth == "u32" || rightWidth == "u32")
                width = anyConstant ? null : IsSigned(leftWidth) || IsSigned(rightWidth) ? "i64" : "u32";
            else
                width = "i32";
        }

        if (width is null)
            return PrimitiveType.Int;
        var result = PrimitiveType.ExactNumeric(width);
        return bothConstant ? result.AsIntegerConstant(null) : result;
    }

    /// <summary>
    /// The C# result type of unary <c>-</c> or <c>~</c> (§12.9.3, §12.9.5): operands narrower than
    /// int (char included) promote to int, and <c>-</c> on a uint is a long. A constant operand
    /// gives a width-less result, which never reports a narrowing (<c>let sbyte:s = -(128)</c>
    /// is a valid constant conversion the checker does not fold).
    /// </summary>
    private static CalorType PromoteUnaryOperand(CalorType operand, bool negate)
    {
        if (operand is not PrimitiveType primitive)
            return operand;
        if (primitive.IsIntegerConstant)
            return PrimitiveType.Int;
        if (primitive.Equals(PrimitiveType.Float) || primitive.Equals(PrimitiveType.Decimal))
            return primitive;
        if (IntegerPromotionWidth(primitive) is not { } width)
            return primitive.Equals(PrimitiveType.Int) ? PrimitiveType.Int : primitive;
        return width switch
        {
            "u32" when negate => PrimitiveType.ExactNumeric("i64"),
            "u64" when negate => PrimitiveType.Int,
            _ => PrimitiveType.ExactNumeric(PromoteUnaryIntegerWidth(width)),
        };
    }

    /// <summary>The integer width an operand promotes from: char behaves as ushort.</summary>
    private static string? IntegerPromotionWidth(PrimitiveType operand)
        => operand.Equals(PrimitiveType.Char) ? "u16"
            : operand.Equals(PrimitiveType.Int) ? operand.NumericWidth
            : null;

    /// <summary>Unary numeric promotion (§12.4.7.2): the types narrower than int become int.</summary>
    private static string PromoteUnaryIntegerWidth(string width)
        => width is "i8" or "u8" or "i16" or "u16" ? "i32" : width;

    private CalorType InferRecordCreationType(RecordCreationNode rec)
    {
        var type = _env.LookupType(rec.TypeName);
        if (type == null)
        {
            _diagnostics.ReportError(rec.Span, DiagnosticCode.UndefinedReference,
                $"Undefined type '{rec.TypeName}'");
            return ErrorType.Instance;
        }

        if (type is RecordType recordType)
        {
            foreach (var fieldAssign in rec.Fields)
            {
                var field = recordType.GetField(fieldAssign.FieldName);
                if (field == null)
                {
                    _diagnostics.ReportError(fieldAssign.Span, DiagnosticCode.UndefinedReference,
                        $"Unknown field '{fieldAssign.FieldName}' on type '{rec.TypeName}'");
                    continue;
                }

                var valueType = InferExpressionType(fieldAssign.Value);
                if (!IsAssignable(field.Type, valueType))
                {
                    _diagnostics.ReportError(fieldAssign.Span, DiagnosticCode.TypeMismatch,
                        $"Cannot assign {valueType.SurfaceName} to field '{fieldAssign.FieldName}' of type {field.Type.SurfaceName}");
                }
            }
        }

        return type;
    }

    private CalorType InferFieldAccessType(FieldAccessNode field)
    {
        // `this.x` inside a class member is the member's declared type (#1676).
        if (field.Target is ThisExpressionNode or ReferenceNode { Name: "this" } && LookupThisMember(field.FieldName) is { } memberType)
            return memberType;
        var targetType = InferExpressionType(field.Target);
        if (targetType == PrimitiveType.String && field.FieldName == "Length")
            return PrimitiveType.Int;

        if (targetType is RecordType recordType)
        {
            var fieldDef = recordType.GetField(field.FieldName);
            if (fieldDef == null)
            {
                _diagnostics.ReportError(field.Span, DiagnosticCode.UndefinedReference,
                    $"Unknown field '{field.FieldName}' on type '{recordType.SurfaceName}'");
                return ErrorType.Instance;
            }
            return fieldDef.Type;
        }

        if (field.FieldName == "Length"
            && (targetType is ArrayType || targetType.Equals(PrimitiveType.String)))
            return PrimitiveType.Int;

        if (_module != null && Binding.TypeIdentity.TryResolveNativeInstanceMemberType(
            _module, targetType.SurfaceName, field.FieldName, out var nativeMember))
            return ResolveTypeName(nativeMember, field.Span);

        if (_module != null && targetType is GenericInstanceType { NativeDeclaration: null })
        {
            _sourceBindings ??= Analysis.CallGraphAnalysis.ResolveOperatorCalls(
                _module, _diagnostics, ProjectionContractMode);
            if (_sourceBindings.MetadataMemberTypes.TryGetValue(field, out var selected))
                return ResolveTypeName(selected.ResultType.ToDisplayString(), field.Span);
        }

        // Same rule as the bool conditions: only complain when the receiver's type is actually
        // known. An unmodeled receiver — a `new` expression, an external call result — is the
        // checker's blind spot, not the program's error, and C# resolves the member itself.
        if (targetType is ErrorType or ExternalType or NullableReferenceType)
        {
            return ErrorType.Instance;
        }

        _diagnostics.ReportError(field.Span, DiagnosticCode.TypeMismatch,
            $"Cannot access field on non-record type {targetType.SurfaceName}");
        return ErrorType.Instance;
    }

    private CalorType InferMatchExpressionType(MatchExpressionNode match, CalorType? expectedType)
    {
        // Consumed here: only this match, and matches nested in its arms' results, are checked
        // against the declared refinement; the target and guards are ordinary expressions.
        var armRefinement = _matchArmRefinementTarget is { } pending && ReferenceEquals(pending.Match, match)
            ? pending
            : ((ExpressionNode Match, CalorType Refinement, string Position)?)null;
        _matchArmRefinementTarget = null;
        var targetType = InferExpressionType(match.Target);

        // Unify the types of all case bodies
        CalorType? unifiedType = null;
        var reportedTargetMismatch = false;
        var hasTargetMismatch = false;
        foreach (var matchCase in match.Cases)
        {
            // Each arm gets its own scope with the pattern's bindings in it, exactly as
            // CheckMatchStatement does. This was missing entirely: an arm like
            // `§K §VAR{d} §WHEN (> d 0) → d` had `d` unbound, so both the guard and the body
            // reported `Undefined variable 'd'`. Harmless while the checker was opt-out;
            // a hard error on the default path once it is on.
            _env.EnterScope();
            CheckPattern(matchCase.Pattern, targetType);

            if (matchCase.Guard != null)
            {
                var guardType = InferExpressionType(matchCase.Guard);
                DefineTrueConditionPatternVariables(matchCase.Guard);
                if (IsDefinitelyNotBool(guardType))
                {
                    _diagnostics.ReportError(matchCase.Guard.Span, DiagnosticCode.TypeMismatch,
                        $"Match guard must be bool, got {guardType.SurfaceName}");
                }
            }

            if (matchCase.Body.Count > 0)
            {
                var lastStmt = matchCase.Body[matchCase.Body.Count - 1];
                CalorType caseType;
                if (lastStmt is ReturnStatementNode ret && ret.Expression != null)
                {
                    var previousArmRefinement = _matchArmRefinementTarget;
                    _matchArmRefinementTarget = armRefinement is { } nested
                        && ret.Expression is MatchExpressionNode
                            ? nested with { Match = ret.Expression }
                            : null;
                    try
                    {
                        caseType = InferExpressionType(ret.Expression, expectedType);
                    }
                    finally
                    {
                        _matchArmRefinementTarget = previousArmRefinement;
                    }
                    if (armRefinement is { } target
                        && !_suppressContextualDiagnostics
                        && IsDistinctNamedRefinement(target.Refinement, caseType))
                    {
                        ReportDistinctNamedRefinement(
                            ret.Expression.Span, target.Refinement, caseType, $"a match arm of the {target.Position}");
                    }
                }
                else
                {
                    caseType = PrimitiveType.Unit;
                }

                if (expectedType != null)
                {
                    // #1676: a named-refinement mismatch is a refinement-soundness error, so it
                    // survives the flow cache of a §TR body like every other one. It is checked
                    // on every arm: an earlier ordinary mismatch (dropped there) must not hide it.
                    var distinctRefinement = IsDistinctNamedRefinement(expectedType, caseType);
                    if (distinctRefinement && !_suppressContextualDiagnostics)
                    {
                        var armSpan = lastStmt is ReturnStatementNode { Expression: { } armResult }
                            ? armResult.Span
                            : match.Span;
                        ReportDistinctNamedRefinement(armSpan, expectedType, caseType, "a match arm");
                    }
                    if (!reportedTargetMismatch && !IsAssignable(expectedType, caseType))
                    {
                        hasTargetMismatch = true;
                        if (!_suppressContextualDiagnostics && !distinctRefinement)
                        {
                            _diagnostics.ReportError(match.Span, DiagnosticCode.TypeMismatch,
                                $"Match arm type {caseType.SurfaceName} is not assignable to "
                                + expectedType.SurfaceName);
                        }
                        reportedTargetMismatch = true;
                    }
                    else if (distinctRefinement)
                    {
                        hasTargetMismatch = true;
                    }
                    unifiedType = expectedType;
                }
                else
                {
                    unifiedType = unifiedType == null
                        ? caseType
                        : CommonMatchType(match.Span, unifiedType, caseType);
                }
            }

            _env.ExitScope();
        }

        return hasTargetMismatch ? ErrorType.Instance : unifiedType ?? PrimitiveType.Unit;
    }

    private CalorType CommonMatchType(Parsing.TextSpan span, CalorType left, CalorType right)
    {
        if (left is NeverType) return right;
        if (right is NeverType) return left;
        if (left is ErrorType || right is ErrorType) return ErrorType.Instance;
        if (left.Equals(right)) return left;
        if (TryUnifyNullableReferences(left, right, out var nullableReference))
            return nullableReference;
        if (TryUnifyNullableValues(left, right, out var nullableValue))
            return nullableValue;
        if (IsAssignable(left, right)) return left;
        if (IsAssignable(right, left)) return right;
        if (TryJoinErasedRefinements(left, right, out var erasedJoin)) return erasedJoin;

        if (!_suppressContextualDiagnostics)
        {
            _diagnostics.ReportError(span, DiagnosticCode.TypeMismatch,
                $"Match expression branches have incompatible types: {left.SurfaceName} and {right.SurfaceName}");
        }
        return ErrorType.Instance;
    }

    private CalorType ResolveParameterType(ParameterNode parameter)
    {
        if (_module is not null)
        {
            _sourceBindings ??= Analysis.CallGraphAnalysis.ResolveOperatorCalls(
                _module, _diagnostics, ProjectionContractMode);
            if (_sourceBindings.ParameterTypes.TryGetValue(parameter, out var type))
            {
                if (type is Microsoft.CodeAnalysis.INamedTypeSymbol { DelegateInvokeMethod: not null })
                {
                    return ResolveBoundSignatureType(type, parameter.Span);
                }
                var resolved = ResolveTypeName(parameter.TypeName, parameter.Span);
                // A lookalike or shadowing declaration is not the standard CLR delegate.
                return resolved is FunctionType
                    || resolved is GenericInstanceType generic && TryGetDelegateFunctionType(generic, out _)
                    ? new ExternalType(parameter.TypeName) : resolved;
            }
        }
        return ResolveTypeName(parameter.TypeName, parameter.Span);
    }

    private CalorType ResolveBoundSignatureType(
        Microsoft.CodeAnalysis.ITypeSymbol type,
        Parsing.TextSpan span)
    {
        var name = type.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
        if (type is not Microsoft.CodeAnalysis.INamedTypeSymbol { DelegateInvokeMethod: { } invoke } named)
            return ResolveTypeName(name, span);

        // Keep native refinement annotations, which erase in the C# binding image.
        if (named.Locations.Any(location => location.IsInSource)
            && _env.LookupType(name.Replace("global::", "", StringComparison.Ordinal)) is FunctionType native)
            return native;
        // Only actual core-library delegates get structural variance, including nested signatures.
        var standardDelegate = named.Name is "Action" or "Func"
            && Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
                named.OriginalDefinition,
                named.BaseType?.ContainingAssembly.GetTypeByMetadataName($"System.{named.MetadataName}"));
        CalorType ResolveComponent(Microsoft.CodeAnalysis.ITypeSymbol component)
            => standardDelegate
                ? ResolveBoundSignatureType(component, span)
                : ResolveTypeName(component.ToDisplayString(
                    Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat), span);
        return new FunctionType(
            invoke.Parameters.Select(item => ResolveComponent(item.Type)).ToArray(),
            ResolveComponent(invoke.ReturnType),
            invoke.Parameters.Select(item => item.Name).ToArray(),
            invoke.Parameters.Select(item => item.RefKind switch
            {
                Microsoft.CodeAnalysis.RefKind.Ref => ParameterModifier.Ref,
                Microsoft.CodeAnalysis.RefKind.Out => ParameterModifier.Out,
                Microsoft.CodeAnalysis.RefKind.In => ParameterModifier.In,
                _ => ParameterModifier.None
            }).ToArray(),
            standardDelegate ? null : $"{named.ContainingAssembly.Identity}:{name}");
    }

    private CalorType ResolveTypeName(string typeName, Parsing.TextSpan span)
    {
        if (typeName.StartsWith("global::", StringComparison.Ordinal))
            typeName = typeName[8..];
        if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(typeName, out var referentName))
        {
            var referentSurface = Parsing.AttributeHelper.ToSurfaceSpelling(referentName);
            var referent = PrimitiveType.FromName(referentSurface);
            CalorType? supportedReference = referent is not null
                && (referent.Equals(PrimitiveType.String) || referent.Equals(PrimitiveType.Object))
                    ? referent
                    : _moduleDeclaredReferenceTypes.Contains(referentName)
                        && _env.LookupType(referentName) is ExternalType nominal
                        ? nominal
                        : null;
            if (supportedReference is null
                && _module != null
                && Binding.TypeIdentity.ResolveNativeClass(_module, referentSurface) is { IsStruct: false })
                supportedReference = ResolveTypeName(referentSurface, span);
            if (supportedReference is null
                && (Binding.TypeIdentity.TrySplitNativeArray(referentSurface, out _, out _)
                    || referentSurface.Length > 2 && referentSurface[0] == '['
                        && referentSurface[^1] == ']' && !referentSurface.Contains(',')))
                supportedReference = ResolveTypeName(referentSurface, span) as ArrayType;
            if (supportedReference is not null)
            {
                return new NullableReferenceType(supportedReference,
                    typeName.StartsWith("NULLABLE[inner=", StringComparison.OrdinalIgnoreCase));
            }

            var valueReferent = ResolveNullableValueReferent(referentName);
            if (valueReferent is not null)
            {
                return new NullableValueType(valueReferent);
            }

            // Other annotations retain the existing unsupported/unresolved behavior. In
            // particular, this is not permission to model arbitrary nullable payloads as
            // references.
        }

        // Arrays, in BOTH spellings the compiler produces. `T[]` is what the C# converter and
        // §I/§O annotations emit; `[T]` is the collection-literal spelling in the syntax
        // reference. Neither resolved before, so `§I{[str]:args}` — a documented, working
        // declaration — reported "Unknown type '[str]'".
        if (Binding.TypeIdentity.TrySplitNativeArray(typeName, out var arrayElement, out var arrayRank))
        {
            return new ArrayType(ResolveTypeName(arrayElement, span), arrayRank);
        }
        if (typeName.Length > 2 && typeName[0] == '[' && typeName[^1] == ']'
            && typeName.IndexOf(',') < 0)
        {
            return new ArrayType(ResolveTypeName(typeName[1..^1], span));
        }

        // Handle generic types with bracket syntax.
        var bracketIndex = typeName.IndexOf('[');
        if (bracketIndex > 0 && typeName.EndsWith(']')
            && !typeName.AsSpan(bracketIndex + 1).Contains('='))
        {
            var baseName = typeName[..bracketIndex];
            var argsStr = typeName[(bracketIndex + 1)..^1];
            var args = SplitGenericArgs(argsStr);

            return new GenericInstanceType(AttributeHelper.ResolveAsyncCarrierName(baseName, _module),
                args.Select(arg => ResolveTypeName(arg, span)).ToList())
            {
                NativeDeclaration = _module == null ? null : Binding.TypeIdentity.ResolveNativeClass(_module, typeName)
            };
        }

        // Handle generic types with angle bracket syntax: List<T>, Dictionary<K, V>
        var angleIndex = typeName.IndexOf('<');
        if (angleIndex > 0 && typeName.EndsWith('>'))
        {
            var baseName = typeName[..angleIndex];
            var argsStr = typeName[(angleIndex + 1)..^1];
            var args = SplitGenericArgs(argsStr);

            // For generic types (List<T>, Dictionary<K, V>, etc.),
            // resolve the type arguments and create a GenericInstanceType
            var resolvedArgs = new List<CalorType>();
            foreach (var arg in args)
            {
                resolvedArgs.Add(ResolveTypeName(arg, span));
            }
            return new GenericInstanceType(AttributeHelper.ResolveAsyncCarrierName(baseName, _module), resolvedArgs)
            {
                NativeDeclaration = _module == null ? null : Binding.TypeIdentity.ResolveNativeClass(_module, typeName)
            };
        }

        // Try primitive type
        var primitive = PrimitiveType.FromName(typeName);
        if (primitive != null)
            return primitive;

        // Sized numerics arrive EXPANDED — `INT[bits=64][signed=true]`, `FLOAT[bits=32]` — not as
        // the `i64`/`f32` the user wrote, so the lookup above cannot match them. Normalize through
        // the same surface-spelling helper the diagnostics use and try once more. Without this the
        // sized spellings fall through to ExternalType, which is assignable to anything, and the
        // checker silently accepts `§B{x:i64} "a"`.
        var surface = Parsing.AttributeHelper.ToSurfaceSpelling(typeName);
        if (!string.Equals(surface, typeName, StringComparison.Ordinal))
        {
            // RE-ENTER the resolver rather than only retrying FromName. Arrays arrive expanded
            // too (`ARRAY[element=STRING]`), so a FromName-only retry left `[str]` falling through
            // to ExternalType — which is assignable from anything, so `§B{a:[str]} xs` with
            // `xs: [i32]` was silently accepted. The recursion is bounded: ToSurfaceSpelling is
            // idempotent, and the guard above stops the second pass from recursing again.
            return ResolveTypeName(surface, span);
        }

        // Try user-defined type (includes type parameters in scope)
        var userType = _env.LookupType(typeName);
        if (userType != null)
            return userType;
        var carrierName = AttributeHelper.ResolveAsyncCarrierName(typeName, _module);
        if (carrierName is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
            return new GenericInstanceType(carrierName, Array.Empty<CalorType>());
        if (carrierName != typeName && AttributeHelper.TryGetAsyncResultType(carrierName, null, out _))
            return ResolveTypeName(carrierName, span);

        // #741: surface-spell the echoed name. A sized numeric type reaches here as its
        // expanded internal form (e.g. `INT[bits=64][signed=true]`, `FLOAT[bits=32]`),
        // which would leak `INT`/`[bits=` — route it through ToSurfaceSpelling so the
        // message reads `i64`/`f32`. (That these sized types are not yet *resolved* by the
        // opt-in TypeChecker — so a valid `i64` binding still gets this spurious "Unknown
        // type" — is a separate pre-existing gap outside this spelling change: the opt-in
        // TypeChecker does not model sized numeric widths.)
        // Unresolved. Reported as a WARNING, not an error, and not silently either.
        //
        // An error is wrong: the checker models no BCL surface, so it cannot distinguish "a .NET
        // type reached through interop" from "a typo", and erroring rejects working programs —
        // that is most of what this change set fixes. But silence is also wrong: on
        // `calor_check`, `calor_refine` and `calor -i/-o` NOTHING else compiles the generated C#,
        // so a misspelt type would simply vanish rather than resurface as CS0246 later.
        //
        // A heuristic was tried first — treat lower-case names as Calor typos, PascalCase as
        // external — and it is recorded here as rejected rather than quietly dropped: it missed
        // the obvious case (`Strng` is PascalCase) while firing on working programs, because some
        // trailing-member-access shapes reach this resolver with a local's name in the type
        // position. Warning on everything is weaker per-case and honest about which.
        var spelled = Parsing.AttributeHelper.ToSurfaceSpelling(typeName);
        if (_suppressDiagnostics)
        {
            return new ExternalType(Parsing.AttributeHelper.ToSurfaceSpelling(typeName));
        }

        _diagnostics.ReportWarning(span, DiagnosticCode.UndefinedReference,
            $"Type '{spelled}' is not known to the Calor type checker. If it is a .NET type used " +
            "through interop this is expected and the C# compiler will check it; if it is a typo, " +
            "nothing else will catch it on this path.");
        return new ExternalType(spelled);
    }

    /// <summary>
    /// Splits generic type arguments, respecting nested angle brackets.
    /// For example: "str, List&lt;T&gt;" splits to ["str", "List&lt;T&gt;"]
    /// </summary>
    private static List<string> SplitGenericArgs(string argsStr)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var c in argsStr)
        {
            if (c == '<' || c == '[') depth++;
            else if (c == '>' || c == ']') depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString().Trim());

        return args;
    }

    /// <summary>
    /// True when the type is not usable as a bool CONDITION — and known well enough to say so.
    ///
    /// <para><c>ErrorType</c> is excluded deliberately. It means "the checker could not determine
    /// this", which is routine: it models no BCL surface, so <c>§C{File.Exists}</c> and any other
    /// external call yields it. Reporting "condition must be bool, got &lt;error&gt;" turns the
    /// checker's own ignorance into the user's error, and it cascades — one unmodeled call
    /// produced errors at every downstream use. The arithmetic check already suppressed this;
    /// the condition and field-access checks did not, which is why enabling the checker rejected
    /// the MCP primer, two shipped benchmarks and the syntax exemplar.</para>
    /// </summary>
    /// <summary>
    /// True when the checker genuinely knows this type and it is not a collection it models.
    /// The <c>ErrorType</c>/<c>ExternalType</c> exclusion is the same rule the bool-condition and
    /// field-access checks follow: an unmodeled receiver is the checker's blind spot, not the
    /// user's error.
    /// </summary>
    private static bool IsKnownNonCollection(CalorType type)
        => type is not ErrorType && type is not ExternalType
            && type is not NullableReferenceType { ReferentType: ExternalType };

    private static bool IsStringReference(CalorType type) =>
        type.Equals(PrimitiveType.String)
        || type is NullableReferenceType nullable && nullable.ReferentType.Equals(PrimitiveType.String);

    private static bool IsObliviousReferenceType(CalorType type)
        => type.Equals(PrimitiveType.String) || type.Equals(PrimitiveType.Object);

    /// <summary>
    /// Strips named-refinement wrappers (§RTYPE) down to the base type. Refinements erase at
    /// runtime, so every operator and type-family test treats a refined value as its base. #1515.
    /// </summary>
    private static CalorType EraseRefinement(CalorType type)
    {
        while (type is RefinedType refined)
            type = refined.BaseType;
        return type;
    }

    /// <summary>
    /// Joins two types when at least one is a named refinement and neither is assignable to the
    /// other, for example <c>Nat</c> and <c>Pos</c> over <c>i32</c>. Named refinements are
    /// nominal (#1530), so the join holds neither predicate: it is the common erased base type.
    /// </summary>
    private static bool TryJoinErasedRefinements(CalorType left, CalorType right, out CalorType joined)
    {
        joined = ErrorType.Instance;
        if (left is not RefinedType && right is not RefinedType)
            return false;
        var erasedLeft = EraseRefinement(left);
        var erasedRight = EraseRefinement(right);
        if (IsAssignable(erasedLeft, erasedRight))
        {
            joined = erasedLeft;
            return true;
        }
        if (IsAssignable(erasedRight, erasedLeft))
        {
            joined = erasedRight;
            return true;
        }
        return false;
    }

    private static bool IsDefinitelyNotBool(CalorType type)
        => IsDefinitelyNotBoolErased(EraseRefinement(type));

    private static bool IsDefinitelyNotBoolErased(CalorType type)
        => !type.Equals(PrimitiveType.Bool) && type is not ErrorType && type is not ExternalType
            && type is not NeverType;

    private static bool IsNumeric(CalorType type)
        => IsNumericErased(EraseRefinement(type));

    private static bool IsDefinitelyNotNumeric(CalorType type)
        => EraseRefinement(type) is not (ErrorType or ExternalType or NeverType)
            && !IsNumeric(type);

    private static bool IsNumericErased(CalorType type)
        => type.Equals(PrimitiveType.Int) || type.Equals(PrimitiveType.Float)
        || type.Equals(PrimitiveType.Char) || type.Equals(PrimitiveType.Decimal);

    private static bool IsAssignableResolvedMethodArgument(CalorType target, CalorType source)
        => IsAssignable(target, source)
            || IsBinderOwnedNullabilityMismatch(target, source);

    private static bool IsBinderOwnedNullabilityMismatch(CalorType target, CalorType source)
    {
        if (target.Equals(PrimitiveType.String)
            && source is NullableReferenceType nullable
            && nullable.ReferentType.Equals(PrimitiveType.String))
            return true;

        var targetReferent = target is NullableReferenceType targetNullable
            ? targetNullable.ReferentType : target;
        var sourceReferent = source is NullableReferenceType sourceNullable
            ? sourceNullable.ReferentType : source;
        if (targetReferent is ArrayType targetArray && sourceReferent is ArrayType sourceArray)
            return HaveSameArrayRuntimeType(targetArray, sourceArray);
        return targetReferent is GenericInstanceType
            && sourceReferent is GenericInstanceType
            && Binding.Scope.HasNullableStringAnnotationDifference(target.SurfaceName, source.SurfaceName);
    }

    private static bool IsAssignable(CalorType target, CalorType source)
    {
        if (ClassifyImplicitNumericConversion(target, source) is { } numericConversion)
            return numericConversion;
        if (target.Equals(source)) return true;
        if (target is GenericInstanceType targetGeneric && source is GenericInstanceType sourceGeneric
            && Binding.NullabilityChecker.SameGenericDefinition(targetGeneric.BaseName, sourceGeneric.BaseName)
            && targetGeneric.TypeArguments.SequenceEqual(sourceGeneric.TypeArguments))
            return true;
        if (target is ArrayType targetArray && source is ArrayType sourceArray)
            return HaveSameArrayRuntimeType(targetArray, sourceArray);
        if (source is NeverType) return true;
        if (source is ErrorType) return true; // Allow error types to be assigned anywhere
        if (source is NullType)
        {
            // Reference nullability remains analysis-only; introducing a real
            // null type must not activate the previously accepted literal flow.
            return target is NullableReferenceType or NullableValueType or ExternalType or ErrorType
                || target.Equals(PrimitiveType.String) || target.Equals(PrimitiveType.Object);
        }
        if (target is NullableReferenceType nullableTarget)
        {
            if (source is NullableReferenceType nullableSource)
                return IsAssignable(nullableTarget.ReferentType, nullableSource.ReferentType);
            if (source is NullType)
                return true;
            if (nullableTarget.ReferentType.Equals(PrimitiveType.Object))
                return true;
            if (nullableTarget.ReferentType is ExternalType && source is PrimitiveType)
                return false;
            return IsAssignable(nullableTarget.ReferentType, source);
        }
        if (target is NullableValueType nullableValueTarget)
        {
            return source is NullableValueType nullableValueSource
                ? IsAssignable(nullableValueTarget.UnderlyingType, nullableValueSource.UnderlyingType)
                : IsAssignable(nullableValueTarget.UnderlyingType, source);
        }
        if (TryGetDelegateFunctionType(target, out var targetFunction) && source is FunctionType sourceFunction)
            return targetFunction.Equals(sourceFunction);
        if (target is GenericInstanceType { BaseName: "System.Threading.Tasks.Task", TypeArguments.Count: 0 }
            && source is GenericInstanceType { BaseName: "System.Threading.Tasks.Task", TypeArguments.Count: 1 })
            return true;
        // Nothing is known about an unmodeled external type, in either direction.
        if (target is ExternalType || source is ExternalType) return true;
        if (target.Equals(PrimitiveType.Float) && source.Equals(PrimitiveType.Int)) return true;
        // `object` is the top type: anything may be assigned TO it. Deliberately not symmetric —
        // assigning object to a concrete type needs a cast in C#, so accepting it here would
        // green-light code the emitted C# rejects.
        if (target.Equals(PrimitiveType.Object)) return true;
        if (source is NullableReferenceType nullableReference)
        {
            return !nullableReference.RequiresTransitionalAssignmentCheck
                && IsAssignable(target, nullableReference.ReferentType);
        }
        if (source is NullableValueType)
        {
            return false;
        }

        // char widens to an integer, as in C#. Not the reverse: `i32 -> char` is a narrowing
        // conversion C# requires an explicit cast for.
        if (target.Equals(PrimitiveType.Int) && source.Equals(PrimitiveType.Char)) return true;
        if (target.Equals(PrimitiveType.Float) && source.Equals(PrimitiveType.Char)) return true;
        if (target.Equals(PrimitiveType.Decimal) && source.Equals(PrimitiveType.Char)) return true;
        if (target.Equals(PrimitiveType.Decimal) && source.Equals(PrimitiveType.Int)) return true;
        // Refined type is a subtype of its base type (erasure)
        if (source is RefinedType refinedSource && IsAssignable(target, refinedSource.BaseType)) return true;
        return false;
    }

    /// <summary>
    /// C#'s implicit numeric conversions (§10.2.3) and implicit constant expression conversions
    /// (§10.2.11) between primitives whose exact widths are known. Returns <c>null</c> when this
    /// rule does not decide — a width is unknown, or the pair crosses families (int → float,
    /// float → int, decimal), which <see cref="IsAssignable"/> already decides.
    /// </summary>
    /// <remarks>
    /// Equality collapses every integer width into one <see cref="PrimitiveType.Int"/> and both
    /// binary floating types into one <see cref="PrimitiveType.Float"/>, so without this rule
    /// <c>let float:x = someDouble</c> and <c>let int:y = someLong</c> passed the checker and
    /// failed only as C# CS0266 (and, inside the same function, as an "unresolved" operator
    /// charged <c>unknown</c>: Calor0410). An unknown width never rejects: the canonical
    /// <see cref="PrimitiveType.Int"/> and <see cref="PrimitiveType.Float"/> type values whose
    /// width the checker does not track.
    /// </remarks>
    internal static bool? ClassifyImplicitNumericConversion(CalorType target, CalorType source)
    {
        if (target is not PrimitiveType targetPrimitive || source is not PrimitiveType sourcePrimitive
            || targetPrimitive.NumericWidth is not { } targetWidth)
            return null;

        // char → ushort, int, uint, long, ulong (and the floating and decimal types, decided by
        // IsAssignable). Not to sbyte, byte or short, and a char constant has no constant
        // conversion.
        if (sourcePrimitive.Equals(PrimitiveType.Char))
            return targetPrimitive.Equals(PrimitiveType.Int)
                ? targetWidth is "u16" or "i32" or "u32" or "i64" or "u64"
                : null;

        if (sourcePrimitive.NumericWidth is not { } sourceWidth)
            return null;
        var targetIsFloat = targetPrimitive.Equals(PrimitiveType.Float);
        var sourceIsFloat = sourcePrimitive.Equals(PrimitiveType.Float);
        if (targetIsFloat && sourceIsFloat)
            return sourceWidth == "f32" || targetWidth == "f64";
        if (targetIsFloat || sourceIsFloat)
            return null;

        if (IsImplicitIntegerConversion(sourceWidth, targetWidth))
            return true;
        if (!sourcePrimitive.IsIntegerConstant)
            return false;
        // A constant whose value the checker does not compute is left to C#.
        if (sourcePrimitive.IntegerConstantValue is not { } value)
            return true;
        return sourceWidth switch
        {
            "i32" => targetWidth is "i8" or "u8" or "i16" or "u16" or "u32" or "u64"
                && FitsIntegerWidth(value, targetWidth),
            "i64" => targetWidth == "u64" && value >= 0,
            _ => false,
        };
    }

    private static (bool Signed, int Bits)? IntegerWidthShape(string width) => width switch
    {
        "i8" => (true, 8),
        "u8" => (false, 8),
        "i16" => (true, 16),
        "u16" => (false, 16),
        "i32" => (true, 32),
        "u32" => (false, 32),
        "i64" => (true, 64),
        "u64" => (false, 64),
        _ => null,
    };

    /// <summary>
    /// C# §10.2.3 between integer types: a wider type of the same signedness, or a wider signed
    /// type for an unsigned source. Never signed → unsigned.
    /// </summary>
    private static bool IsImplicitIntegerConversion(string sourceWidth, string targetWidth)
    {
        if (sourceWidth == targetWidth)
            return true;
        if (IntegerWidthShape(sourceWidth) is not { } source || IntegerWidthShape(targetWidth) is not { } target)
            return false;
        if (source.Signed && !target.Signed)
            return false;
        return target.Bits > source.Bits;
    }

    private static bool FitsIntegerWidth(Int128 value, string width) => width switch
    {
        "i8" => value >= sbyte.MinValue && value <= sbyte.MaxValue,
        "u8" => value >= byte.MinValue && value <= byte.MaxValue,
        "i16" => value >= short.MinValue && value <= short.MaxValue,
        "u16" => value >= ushort.MinValue && value <= ushort.MaxValue,
        "i32" => value >= int.MinValue && value <= int.MaxValue,
        "u32" => value >= uint.MinValue && value <= uint.MaxValue,
        "i64" => value >= long.MinValue && value <= long.MaxValue,
        "u64" => value >= ulong.MinValue && value <= ulong.MaxValue,
        _ => false,
    };

    /// <summary>
    /// True when C# rejects the implicit conversion between two numeric primitives whose exact
    /// types the checker knows: a narrowing (<c>double</c> → <c>float</c>, <c>long</c> →
    /// <c>int</c>), a floating value into an integer, or <c>decimal</c> ↔ <c>double</c>. Used at
    /// the positions the checker did not otherwise validate (returns, assignments, compound
    /// assignments, call arguments): an unknown width or an unmodeled operand never reports.
    /// </summary>
    private static bool IsRejectedNumericConversion(CalorType target, CalorType source)
        => target is PrimitiveType targetPrimitive && source is PrimitiveType sourcePrimitive
            && HasExactNumericType(targetPrimitive) && HasExactNumericType(sourcePrimitive)
            && !IsAssignable(targetPrimitive, sourcePrimitive);

    private static bool HasExactNumericType(PrimitiveType type)
        => type.NumericWidth != null || type.Equals(PrimitiveType.Decimal) || type.Equals(PrimitiveType.Char);

    /// <summary>The C# keyword for a numeric primitive, for the cast in a diagnostic's hint.</summary>
    private static string NumericCSharpKeyword(PrimitiveType type)
    {
        if (type.Equals(PrimitiveType.Decimal)) return "decimal";
        if (type.Equals(PrimitiveType.Char)) return "char";
        return type.NumericWidth switch
        {
            "i8" => "sbyte",
            "u8" => "byte",
            "i16" => "short",
            "u16" => "ushort",
            "u32" => "uint",
            "i64" => "long",
            "u64" => "ulong",
            "f32" => "float",
            "f64" => "double",
            _ => "int",
        };
    }

    /// <summary>
    /// Reports a numeric conversion C# would reject (CS0266/CS0664), at the Calor expression.
    /// Kept like a refinement-soundness error: it survives the flow cache of structural
    /// statements and the class-member pass that keeps only soundness errors, because the
    /// verdict is exact (both types are known) and C# would reject the program anyway.
    /// </summary>
    private void ReportNumericConversion(Parsing.TextSpan span, CalorType target, CalorType source, string position)
    {
        var sourcePrimitive = (PrimitiveType)source;
        var sourceKeyword = NumericCSharpKeyword(sourcePrimitive);
        var targetKeyword = NumericCSharpKeyword((PrimitiveType)target);
        var targetWidth = ((PrimitiveType)target).NumericWidth;
        var reason = sourcePrimitive is { IsIntegerConstant: true, IntegerConstantValue: { } value }
            && (sourcePrimitive.NumericWidth == "i32"
                    && targetWidth is "i8" or "u8" or "i16" or "u16" or "u32" or "u64"
                || sourcePrimitive.NumericWidth == "i64" && targetWidth == "u64")
                ? $"the constant {value} is outside the range of {targetKeyword}"
                : ClassifyImplicitNumericConversion(target, source) == false
                    ? "the conversion narrows and can lose information"
                    : $"C# has no implicit conversion from {sourceKeyword} to {targetKeyword}";
        ReportRefinementSoundnessError(span,
            $"Cannot implicitly convert {sourceKeyword} to {targetKeyword} in {position}: {reason}. "
            + $"Write an explicit cast, such as ({targetKeyword})value, or change the declared type.");
    }

    private static bool HaveSameArrayRuntimeType(ArrayType target, ArrayType source)
    {
        if (target.Rank != source.Rank)
            return false;
        var targetElement = target.ElementType is NullableReferenceType targetNullable
            ? targetNullable.ReferentType
            : target.ElementType;
        var sourceElement = source.ElementType is NullableReferenceType sourceNullable
            ? sourceNullable.ReferentType
            : source.ElementType;
        return targetElement is ArrayType nestedTarget && sourceElement is ArrayType nestedSource
            ? HaveSameArrayRuntimeType(nestedTarget, nestedSource)
            : targetElement.Equals(sourceElement);
    }

    private bool IsFunctionReferenceAssignable(CalorType target, string name)
    {
        if (!TryGetDelegateFunctionType(target, out var targetFunction))
            return false;

        return TryGetMethodGroupConversionCost(
            targetFunction, _env.LookupFunctionCandidates(name), out _);
    }

    private static bool TryGetDelegateFunctionType(CalorType type, out FunctionType function)
    {
        if (type is FunctionType direct)
        {
            function = direct;
            return true;
        }

        if (type is GenericInstanceType generic
            && generic.BaseName.Equals("Func", StringComparison.OrdinalIgnoreCase)
            && generic.TypeArguments.Count >= 1)
        {
            var parameterCount = generic.TypeArguments.Count - 1;
            function = new FunctionType(
                generic.TypeArguments.Take(parameterCount).ToArray(),
                generic.TypeArguments[^1],
                StandardDelegateParameterNames(parameterCount, "arg"));
            return true;
        }

        if (type is GenericInstanceType action
            && action.BaseName.Equals("Action", StringComparison.OrdinalIgnoreCase))
        {
            function = new FunctionType(
                action.TypeArguments,
                PrimitiveType.Void,
                StandardDelegateParameterNames(action.TypeArguments.Count, "obj"));
            return true;
        }

        function = null!;
        return false;
    }

    private static IReadOnlyList<string> StandardDelegateParameterNames(int parameterCount, string singleName)
        => parameterCount switch
        {
            0 => Array.Empty<string>(),
            1 => [singleName],
            _ => Enumerable.Range(1, parameterCount).Select(index => $"arg{index}").ToArray()
        };

    private static bool IsNumericType(CalorType type)
    {
        // `char` participates in arithmetic and comparison, promoting to int (C# §12.4.7).
        // `decimal` is numeric too — it simply does not convert implicitly to/from double.
        return type.Equals(PrimitiveType.Int) || type.Equals(PrimitiveType.Float)
            || type.Equals(PrimitiveType.Char) || type.Equals(PrimitiveType.Decimal)
            || type is ErrorType;
    }
}

/// <summary>
/// Manages type bindings during type checking.
/// </summary>
public sealed record FunctionCandidate(
    FunctionType Type,
    IReadOnlyList<string> ParameterNames,
    IReadOnlyList<ParameterModifier> ParameterModifiers,
    IReadOnlyList<bool> OptionalParameters,
    IReadOnlyList<string> TypeParameterNames);

public sealed class TypeEnvironment
{
    private readonly Stack<Dictionary<string, CalorType>> _variableScopes = new();
    private readonly Stack<Dictionary<string, CalorType>> _typeScopes = new();
    private readonly Dictionary<string, CalorType> _globalTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FunctionCandidate>> _functions = new(StringComparer.OrdinalIgnoreCase);

    public TypeEnvironment()
    {
        // Variable names are case-sensitive, as in the binder and the emitted C#: a field `n`
        // and a property `N` are two members (#1676 review). Type names stay case-insensitive.
        _variableScopes.Push(new Dictionary<string, CalorType>(StringComparer.Ordinal));
        _typeScopes.Push(new Dictionary<string, CalorType>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Pushes a variable scope. An opaque scope ends variable lookup: a name not found in it, or
    /// in the scopes opened after it, is not looked up in the scopes before it.
    /// </summary>
    public void EnterScope(bool opaque = false)
    {
        _variableScopes.Push(new Dictionary<string, CalorType>(StringComparer.Ordinal));
        _opaqueVariableScopes.Push(opaque);
        _typeScopes.Push(new Dictionary<string, CalorType>(StringComparer.OrdinalIgnoreCase));
    }

    public void ExitScope()
    {
        if (_variableScopes.Count > 1)
        {
            _variableScopes.Pop();
            _opaqueVariableScopes.Pop();
        }
        if (_typeScopes.Count > 1)
            _typeScopes.Pop();
    }

    private readonly Stack<bool> _opaqueVariableScopes = new(new[] { false });

    public void DefineVariable(string name, CalorType type)
    {
        _variableScopes.Peek()[name] = type;
    }

    /// <summary>#1678: the variables defined in the innermost scope.</summary>
    public IReadOnlyDictionary<string, CalorType> CurrentScopeVariables => _variableScopes.Peek();

    public CalorType? LookupVariable(string name)
    {
        foreach (var (scope, opaque) in _variableScopes.Zip(_opaqueVariableScopes))
        {
            if (scope.TryGetValue(name, out var type))
                return type;
            if (opaque)
                return null;
        }
        return null;
    }

    /// <summary>The number of open variable scopes, including the global one.</summary>
    public int ScopeDepth => _variableScopes.Count;

    /// <summary>
    /// Looks a variable up only in the scopes opened after the first <paramref name="depth"/>
    /// scopes, so a name found only in an outer scope (a class member) is not found.
    /// </summary>
    public CalorType? LookupVariableAtOrBelow(string name, int depth)
    {
        var scopeDepth = _variableScopes.Count;
        foreach (var (scope, opaque) in _variableScopes.Zip(_opaqueVariableScopes))
        {
            if (scopeDepth <= depth && scope.TryGetValue(name, out var type))
                return type;
            if (scopeDepth <= depth && opaque)
                return null;
            scopeDepth--;
        }
        return null;
    }

    public CalorType? LookupVariableAbove(string name, int depth)
    {
        var scopeDepth = _variableScopes.Count;
        foreach (var scope in _variableScopes)
        {
            if (scopeDepth <= depth)
                return null;
            if (scope.TryGetValue(name, out var type))
                return type;
            scopeDepth--;
        }
        return null;
    }

    public void DefineType(string name, CalorType type)
    {
        // Type parameters are scoped, other types are global
        if (type is TypeParameterType)
        {
            _typeScopes.Peek()[name] = type;
        }
        else
        {
            _globalTypes[name] = type;
        }
    }

    public CalorType? LookupType(string name)
    {
        // Check scoped types first (type parameters)
        foreach (var scope in _typeScopes)
        {
            if (scope.TryGetValue(name, out var type))
                return type;
        }
        // Then check global types
        return _globalTypes.TryGetValue(name, out var globalType) ? globalType : null;
    }

    public void DefineFunction(string name, FunctionCandidate candidate)
    {
        if (!_functions.TryGetValue(name, out var overloads))
        {
            overloads = new List<FunctionCandidate>();
            _functions[name] = overloads;
        }
        overloads.Add(candidate);
    }

    public void DefineFunction(string name, FunctionType type)
        => DefineFunction(name, new FunctionCandidate(
            type,
            Enumerable.Range(0, type.ParameterTypes.Count).Select(index => $"arg{index}").ToArray(),
            Enumerable.Repeat(ParameterModifier.None, type.ParameterTypes.Count).ToArray(),
            Enumerable.Repeat(false, type.ParameterTypes.Count).ToArray(),
            Array.Empty<string>()));

    public FunctionType? LookupFunction(string name)
    {
        var overloads = LookupFunctionCandidates(name);
        return overloads.Count == 1 ? overloads[0].Type : null;
    }

    public IReadOnlyList<FunctionCandidate> LookupFunctionCandidates(string name)
    {
        foreach (var (scope, opaque) in _localFunctions.Zip(_opaqueLocalFunctions))
        {
            if (scope.TryGetValue(name, out var locals))
                return locals;
            if (opaque)
                return Array.Empty<FunctionCandidate>();
        }
        return _functions.TryGetValue(name, out var overloads)
            ? overloads
            : Array.Empty<FunctionCandidate>();
    }

    // 0.25 F3 (#847): local functions visible in the callable bodies being checked.
    private readonly Stack<Dictionary<string, List<FunctionCandidate>>> _localFunctions = new();

    /// <summary>
    /// Pushes a callable scope. An opaque one ends lookup: a name it does not declare resolves to
    /// no candidates instead of to an enclosing scope's or the module's functions.
    /// </summary>
    public void EnterLocalFunctions(bool opaque = false)
    {
        _localFunctions.Push(new(StringComparer.Ordinal));
        _opaqueLocalFunctions.Push(opaque);
    }

    public void ExitLocalFunctions()
    {
        _localFunctions.Pop();
        _opaqueLocalFunctions.Pop();
    }

    private readonly Stack<bool> _opaqueLocalFunctions = new();

    public void DefineLocalFunction(string name, FunctionCandidate candidate)
    {
        var scope = _localFunctions.Peek();
        if (!scope.TryGetValue(name, out var overloads))
            scope[name] = overloads = new List<FunctionCandidate>();
        overloads.Add(candidate);
    }
}
