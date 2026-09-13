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
    private readonly Dictionary<IsPatternNode, CalorType> _patternBindingTypes = new();
    private readonly HashSet<string> _moduleDeclaredValueTypes = new(StringComparer.OrdinalIgnoreCase);
    private CalorType? _currentReturnType;
    private bool _validateReturnAssignments;
    private bool _suppressContextualDiagnostics;
    private bool _lambdaReturnInvalid;
    private List<CalorType>? _inferredLambdaReturnTypes;

    public TypeChecker(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _env = new TypeEnvironment();
    }

    public void Check(ModuleNode module)
    {
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

        // Use a simple string representation for the predicate text
        var predicateText = $"#{rtype.BaseTypeName}";
        var refinedType = new RefinedType(baseType, predicateText, rtype.Predicate);
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
    {
        foreach (var tp in func.TypeParameters)
        {
            _env.DefineType(tp.Name, new TypeParameterType(tp.Name, tp.Constraints));
        }

        var open = func.Name.IndexOf('<');
        if (open > 0 && func.Name.EndsWith('>'))
        {
            foreach (var raw in func.Name[(open + 1)..^1].Split(','))
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
    {
        _env.EnterScope();

        // Signature PRE-PASS: resolve quietly. CheckFunction resolves the same annotations again
        // and owns the diagnostics — without this the unresolved-type warning was reported three
        // times for a single `T` (parameter here, return here, parameter again there).
        _suppressDiagnostics = true;

        RegisterTypeParameters(func);

        var paramTypes = new List<CalorType>();
        foreach (var param in func.Parameters)
        {
            var paramType = ResolveTypeName(param.TypeName, param.Span);
            paramTypes.Add(paramType);
        }

        var returnType = func.Output != null
            ? ResolveTypeName(func.Output.TypeName, func.Output.Span)
            : PrimitiveType.Void;

        _suppressDiagnostics = false;
        _env.ExitScope();

        var funcType = new FunctionType(paramTypes, returnType);
        _env.DefineFunction(GetCallableLookupName(func.Name), new FunctionCandidate(
            funcType,
            func.Parameters.Select(parameter => parameter.Name).ToArray(),
            func.Parameters.Select(parameter => parameter.Modifier).ToArray(),
            func.Parameters.Select(parameter => parameter.DefaultValue != null).ToArray(),
            GetTypeParameterNames(func)));
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
            delegateDefinition.Parameters.Select(parameter => parameter.Modifier).ToArray()));
    }

    private static string GetCallableLookupName(string name)
    {
        var open = name.IndexOf('<');
        return open > 0 && name.EndsWith('>') ? name[..open] : name;
    }

    private static IReadOnlyList<string> GetTypeParameterNames(FunctionNode function)
    {
        var names = function.TypeParameters.Select(parameter => parameter.Name).ToList();
        var open = function.Name.IndexOf('<');
        if (open > 0 && function.Name.EndsWith('>'))
        {
            names.AddRange(function.Name[(open + 1)..^1]
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
        var previousReturnType = _currentReturnType;
        var previousSuppressDiagnostics = _suppressDiagnostics;
        _suppressDiagnostics = true;
        _currentReturnType = func.Output != null
            ? ResolveTypeName(func.Output.TypeName, func.Output.Span)
            : PrimitiveType.Void;
        _suppressDiagnostics = previousSuppressDiagnostics;

        // Add parameters to scope
        foreach (var param in func.Parameters)
        {
            var paramType = ResolveTypeName(param.TypeName, param.Span);
            _env.DefineVariable(param.Name, paramType);
        }

        // Check body statements
        foreach (var stmt in func.Body)
        {
            CheckStatement(stmt);
        }

        _currentReturnType = previousReturnType;
        _env.ExitScope();
    }

    private void CheckStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case CallStatementNode call:
                CheckCallStatement(call);
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
            case IfStatementNode ifStmt:
                CheckIfStatement(ifStmt);
                break;
            case BindStatementNode bind:
                CheckBindStatement(bind);
                break;
            case MatchStatementNode match:
                CheckMatchStatement(match);
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

    private void CheckReturnStatement(ReturnStatementNode ret)
    {
        if (ret.Expression != null)
        {
            var returnType = InferExpressionType(ret.Expression, _currentReturnType);
            _inferredLambdaReturnTypes?.Add(returnType);
            if ((_validateReturnAssignments || ret.Expression is CallExpressionNode)
                && _currentReturnType != null
                && !ContainsInferencePlaceholder(_currentReturnType)
                && !ContainsInferencePlaceholder(returnType)
                && !IsAssignable(_currentReturnType, returnType))
            {
                _lambdaReturnInvalid = true;
                if (!_suppressContextualDiagnostics)
                {
                    _diagnostics.ReportError(ret.Expression.Span, DiagnosticCode.TypeMismatch,
                        $"Return type {returnType.SurfaceName} is not assignable to "
                        + _currentReturnType.SurfaceName);
                }
            }
        }
    }

    private void CheckForStatement(ForStatementNode forStmt)
    {
        _env.EnterScope();

        // Loop variable is INT
        _env.DefineVariable(forStmt.VariableName, PrimitiveType.Int);

        var fromType = InferExpressionType(forStmt.From);
        var toType = InferExpressionType(forStmt.To);

        if (!IsNumeric(fromType))
        {
            _diagnostics.ReportError(forStmt.From.Span, DiagnosticCode.TypeMismatch,
                $"FOR 'from' expression must be numeric, got {fromType.SurfaceName}");
        }

        if (!IsNumeric(toType))
        {
            _diagnostics.ReportError(forStmt.To.Span, DiagnosticCode.TypeMismatch,
                $"FOR 'to' expression must be numeric, got {toType.SurfaceName}");
        }

        if (forStmt.Step != null)
        {
            var stepType = InferExpressionType(forStmt.Step);
            if (!IsNumeric(stepType))
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
                if (!IsAssignable(varType, initType))
                {
                    _diagnostics.ReportError(bind.Span, DiagnosticCode.TypeMismatch,
                        $"Cannot assign {initType.SurfaceName} to variable of type {varType.SurfaceName}");
                }
            }
            else
            {
                var initType = InferExpressionType(bind.Initializer);
                varType = initType;
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

        _env.DefineVariable(bind.Name, varType);
    }

    private void CheckMatchStatement(MatchStatementNode match)
    {
        var targetType = InferExpressionType(match.Target);

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
        else if (IsKnownNonCollection(dictType))
        {
            _diagnostics.ReportError(dictForeach.Dictionary.Span, DiagnosticCode.TypeMismatch,
                $"EACHKV requires a Dictionary, got {dictType.SurfaceName}");

            // Define variables with error type to allow body checking to continue
            _env.DefineVariable(dictForeach.KeyName, ErrorType.Instance);
            _env.DefineVariable(dictForeach.ValueName, ErrorType.Instance);
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

            case SomePatternNode somePat:
                if (expectedType is OptionType optType)
                {
                    CheckPattern(somePat.InnerPattern, optType.InnerType, bindVariables);
                }
                else
                {
                    _diagnostics.ReportError(somePat.Span, DiagnosticCode.TypeMismatch,
                        $"Some pattern can only match Option types, got {expectedType.SurfaceName}");
                }
                break;

            case NonePatternNode nonePat:
                if (expectedType is not OptionType)
                {
                    _diagnostics.ReportError(nonePat.Span, DiagnosticCode.TypeMismatch,
                        $"None pattern can only match Option types, got {expectedType.SurfaceName}");
                }
                break;

            case OkPatternNode okPat:
                if (expectedType is ResultType resType)
                {
                    CheckPattern(okPat.InnerPattern, resType.OkType, bindVariables);
                }
                else
                {
                    _diagnostics.ReportError(okPat.Span, DiagnosticCode.TypeMismatch,
                        $"Ok pattern can only match Result types, got {expectedType.SurfaceName}");
                }
                break;

            case ErrPatternNode errPat:
                if (expectedType is ResultType errResType)
                {
                    CheckPattern(errPat.InnerPattern, errResType.ErrType, bindVariables);
                }
                else
                {
                    _diagnostics.ReportError(errPat.Span, DiagnosticCode.TypeMismatch,
                        $"Err pattern can only match Result types, got {expectedType.SurfaceName}");
                }
                break;
            // `§VAR{d}` — the `var d` pattern. Binds like VariablePatternNode; it reached the
            // default arm below and hard-errored, so every switch arm using it was rejected.
            case VarPatternNode varPatNode:
                if (bindVariables)
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
                CheckPattern(andPat.Right, expectedType, bindVariables);
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
                break;

            default:
                // Silent. `CheckPattern` models 7 of the 19 pattern kinds in the AST, and the rest
                // — relational, property, positional, constant, is — are simply not implemented
                // here. Reporting "Unsupported pattern type" made the CHECKER's gap the user's
                // error, and once the checker runs by default that is a hard error on any program
                // using one. Any name a pattern binds and this arm misses will surface as a normal
                // "Undefined variable" at the use site, which is the correct place for it.
                break;
        }
    }

    private CalorType InferExpressionType(ExpressionNode expr, CalorType? expectedType = null)
    {
        return expr switch
        {
            IntLiteralNode => PrimitiveType.Int,
            FloatLiteralNode => PrimitiveType.Float,
            BoolLiteralNode => PrimitiveType.Bool,
            StringLiteralNode => PrimitiveType.String,
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
            BinaryOperationNode binOp => InferBinaryOperationType(binOp),
            SomeExpressionNode some => InferSomeType(some),
            NoneExpressionNode none => InferNoneType(none),
            OkExpressionNode ok => InferOkType(ok),
            ErrExpressionNode err => InferErrType(err),
            RecordCreationNode rec => InferRecordCreationType(rec),
            FieldAccessNode field => InferFieldAccessType(field),
            MatchExpressionNode match => InferMatchExpressionType(match, expectedType),
            NewExpressionNode newExpression => InferNewExpressionType(newExpression),
            CallExpressionNode call => InferCallExpressionType(call),
            ExpressionCallNode call => InferExpressionCallType(call),
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
            TypeOperationNode typeOp => InferTypeOperationType(typeOp),
            IsPatternNode isPattern => InferIsPatternType(isPattern),
            _ => InferUnknownExpressionType(expr)
        };
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

        if (leftType is OptionType)
        {
            _diagnostics.ReportError(coalesce.Left.Span, DiagnosticCode.TypeMismatch,
                $"Null-coalescing does not unwrap runtime {leftType.SurfaceName}; use Option.Unwrap explicitly");
            return ErrorType.Instance;
        }
        if (IsPrimitiveValueType(leftType) || leftType is ResultType
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

            if (IsAssignable(leftType, rightType))
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
        var operandType = InferExpressionType(unary.Operand);
        return unary.Operator switch
        {
            UnaryOperator.Not => PrimitiveType.Bool,
            UnaryOperator.Negate => IsNumericType(operandType) ? operandType : ErrorType.Instance,
            UnaryOperator.BitwiseNot => operandType.Equals(PrimitiveType.Int) ? PrimitiveType.Int : ErrorType.Instance,
            UnaryOperator.PreIncrement or UnaryOperator.PreDecrement
                or UnaryOperator.PostIncrement or UnaryOperator.PostDecrement
                => IsNumericType(operandType) ? operandType : ErrorType.Instance,
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

        var preliminaryTypes = call.Arguments
            .Select(argument => RequiresTargetType(argument)
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
        if (candidates.Length == 0 && functionCandidates.Count > 0
            && (call.Arguments.Any(RequiresTargetType)
                || call.Arguments.Any(argument => argument is ReferenceNode reference
                    && _env.LookupFunctionCandidates(reference.Name).Count > 0)
                || call.ArgumentNames?.Any(name => !string.IsNullOrEmpty(name)) == true
                || call.ArgumentModifiers?.Any(modifier => !string.IsNullOrEmpty(modifier)) == true))
        {
            _diagnostics.ReportError(call.Span, DiagnosticCode.NoMatchingOverload,
                $"No overload of '{call.Target}' matches the supplied argument names, modifiers, and types");
        }
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

    private CalorType InferExpressionCallType(ExpressionCallNode call)
    {
        var targetType = InferExpressionType(call.TargetExpression);
        if (!TryGetDelegateFunctionType(targetType, out var function))
        {
            return ErrorType.Instance;
        }

        var argumentTypes = InferDelegateArgumentTypes(function, call.Arguments, null);
        return ValidateDelegateCall(function, argumentTypes, call.Arguments, null, null, call.Span)
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

        var checkpoint = _diagnostics.CreateCheckpoint();
        var result = InferCallArgument(argument, null);
        _diagnostics.RestoreCheckpoint(checkpoint);
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
            _env.DefineVariable(parameter.Name, declaredType);
            return declaredType;
        }).ToArray();

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
                    _diagnostics.ReportError(lambda.ExpressionBody.Span, DiagnosticCode.TypeMismatch,
                        $"Lambda return type {returnType.SurfaceName} is not assignable to "
                        + expectedFunction.ReturnType.SurfaceName);
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
            if (expectedFunction != null
                && !expectedFunction.ReturnType.Equals(PrimitiveType.Void)
                && !DefinitelyReturns(lambda.StatementBody ?? Array.Empty<StatementNode>()))
            {
                if (!_suppressContextualDiagnostics)
                {
                    _diagnostics.ReportError(lambda.Span, DiagnosticCode.TypeMismatch,
                        $"Lambda targeting {expectedFunction.SurfaceName} must return a value");
                }
                signatureValid = false;
            }
        }
        signatureValid &= !_lambdaReturnInvalid;
        _currentReturnType = previousReturnType;
        _validateReturnAssignments = previousValidateReturnAssignments;
        _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
        _lambdaReturnInvalid = previousLambdaReturnInvalid;
        _inferredLambdaReturnTypes = previousInferredLambdaReturnTypes;
        _env.ExitScope();
        _ = parameterTypes;
        _ = returnType;
        return expectedFunction is not null && signatureValid ? expectedFunction : ErrorType.Instance;
    }

    private static bool IsVoidCompatibleLambdaExpression(
        FunctionType expectedFunction,
        ExpressionNode expression)
        => expectedFunction.ReturnType.Equals(PrimitiveType.Void)
            && expression is CallExpressionNode or ExpressionCallNode or NewExpressionNode;

    private static bool DefinitelyReturns(IReadOnlyList<StatementNode> statements)
        => statements.Any(statement => statement switch
        {
            ReturnStatementNode => true,
            ThrowStatementNode or RethrowStatementNode => true,
            IfStatementNode conditional => DefinitelyReturns(conditional.ThenBody)
                && conditional.ElseIfClauses.All(clause => DefinitelyReturns(clause.Body))
                && conditional.ElseBody != null
                && DefinitelyReturns(conditional.ElseBody),
            MatchStatementNode match => match.Cases.Count > 0
                && match.Cases.Any(matchCase =>
                    matchCase.Pattern is WildcardPatternNode && matchCase.Guard == null)
                && match.Cases.All(matchCase => DefinitelyReturns(matchCase.Body)),
            WhileStatementNode { Condition: BoolLiteralNode { Value: true } } loop
                => !ContainsBreak(loop.Body),
            DoWhileStatementNode { Condition: BoolLiteralNode { Value: true } } loop
                => !ContainsBreak(loop.Body),
            TryStatementNode tryStatement => tryStatement.FinallyBody != null
                && DefinitelyReturns(tryStatement.FinallyBody)
                || DefinitelyReturns(tryStatement.TryBody)
                && tryStatement.CatchClauses.All(clause => DefinitelyReturns(clause.Body)),
            _ => false
        });

    private static bool ContainsBreak(IReadOnlyList<StatementNode> statements)
        => statements.Any(statement => statement switch
        {
            BreakStatementNode => true,
            IfStatementNode conditional => ContainsBreak(conditional.ThenBody)
                || conditional.ElseIfClauses.Any(clause => ContainsBreak(clause.Body))
                || conditional.ElseBody != null && ContainsBreak(conditional.ElseBody),
            MatchStatementNode match => match.Cases.Any(matchCase => ContainsBreak(matchCase.Body)),
            TryStatementNode tryStatement => ContainsBreak(tryStatement.TryBody)
                || tryStatement.CatchClauses.Any(clause => ContainsBreak(clause.Body))
                || tryStatement.FinallyBody != null && ContainsBreak(tryStatement.FinallyBody),
            UsingStatementNode usingStatement => ContainsBreak(usingStatement.Body),
            UnsafeBlockNode unsafeBlock => ContainsBreak(unsafeBlock.Body),
            FixedStatementNode fixedStatement => ContainsBreak(fixedStatement.Body),
            SyncBlockNode syncBlock => ContainsBreak(syncBlock.Body),
            _ => false
        });

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
            else if (!IsAssignable(target, preliminaryTypes[argumentIndex]))
            {
                return false;
            }
            var modifier = candidate.ParameterModifiers[parameterIndex]
                & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In);
            if (modifier != ParameterModifier.None
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
                var diagnosticCheckpoint = _diagnostics.CreateCheckpoint();
                _suppressContextualDiagnostics = true;
                var inferredArgumentType =
                    InferExpressionType(argument.Expression, expectedArgumentType);
                _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
                _diagnostics.RestoreCheckpoint(diagnosticCheckpoint);
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
                var diagnosticCheckpoint = _diagnostics.CreateCheckpoint();
                _suppressContextualDiagnostics = true;
                var inferredArgumentType =
                    InferExpressionType(argument.Expression, contextualParamsArray.ElementType);
                _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
                _diagnostics.RestoreCheckpoint(diagnosticCheckpoint);
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
            else if (!IsAssignable(parameterTypes[i], argument.Type))
            {
                return null;
            }
            else if ((candidate.ParameterModifiers[i]
                    & (ParameterModifier.Ref | ParameterModifier.Out | ParameterModifier.In))
                != ParameterModifier.None
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
                    !IsAssignable(paramsArray.ElementType, argument.Type)))
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
                OptionType option => ContainsInferencePlaceholder(option.InnerType),
                ResultType result => ContainsInferencePlaceholder(result.OkType)
                    || ContainsInferencePlaceholder(result.ErrType),
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
        else if (parameter is ArrayType parameterArray && argument is ArrayType argumentArray)
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

        var checkpoint = _diagnostics.CreateCheckpoint();
        var previousSuppressContextualDiagnostics = _suppressContextualDiagnostics;
        var previousReturnType = _currentReturnType;
        var previousValidateReturnAssignments = _validateReturnAssignments;
        var previousInferredLambdaReturnTypes = _inferredLambdaReturnTypes;
        _suppressContextualDiagnostics = true;
        _currentReturnType = null;
        _validateReturnAssignments = false;
        _inferredLambdaReturnTypes = new List<CalorType>();
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
        _env.ExitScope();
        foreach (var returnType in _inferredLambdaReturnTypes)
            InferTypeArguments(targetTemplate.ReturnType, returnType, substitutions);
        _inferredLambdaReturnTypes = previousInferredLambdaReturnTypes;
        _currentReturnType = previousReturnType;
        _validateReturnAssignments = previousValidateReturnAssignments;
        _suppressContextualDiagnostics = previousSuppressContextualDiagnostics;
        _diagnostics.RestoreCheckpoint(checkpoint);
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
                generic.TypeArguments.Select(argument => SubstituteTypeParameters(argument, substitutions)).ToArray()),
            NullableReferenceType nullable => new NullableReferenceType(
                SubstituteTypeParameters(nullable.ReferentType, substitutions),
                nullable.RequiresTransitionalAssignmentCheck),
            NullableValueType nullable => new NullableValueType(
                SubstituteTypeParameters(nullable.UnderlyingType, substitutions)),
            ArrayType array => new ArrayType(
                SubstituteTypeParameters(array.ElementType, substitutions)),
            FunctionType function => new FunctionType(
                function.ParameterTypes.Select(parameter => SubstituteTypeParameters(parameter, substitutions)).ToArray(),
                SubstituteTypeParameters(function.ReturnType, substitutions),
                function.ParameterNames,
                function.ParameterModifiers),
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
                _diagnostics.ReportError(arguments[argumentIndex].Span, DiagnosticCode.TypeMismatch,
                    $"Argument type {argumentTypes[argumentIndex].SurfaceName} is not assignable to "
                    + function.ParameterTypes[parameterIndex].SurfaceName);
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

        if (trueType is OptionType trueOption && falseType is OptionType falseOption)
        {
            return new OptionType(CommonConditionalType(span, trueOption.InnerType, falseOption.InnerType));
        }

        if (trueType is ResultType trueResult && falseType is ResultType falseResult)
        {
            return new ResultType(
                CommonConditionalType(span, trueResult.OkType, falseResult.OkType),
                CommonConditionalType(span, trueResult.ErrType, falseResult.ErrType));
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
        return type switch
        {
            NullableReferenceType nullable => nullable.ReferentType,
            NullableValueType nullable => nullable.UnderlyingType,
            _ => type
        };
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
        if (refNode.Name == "null")
        {
            return NullType.Instance;
        }

        if (refNode.Name is "default" or "this" or "base" or "value")
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

        if (leftType.Equals(PrimitiveType.Float) || rightType.Equals(PrimitiveType.Float))
        {
            return PrimitiveType.Float;
        }

        return PrimitiveType.Int;
    }

    private CalorType InferSomeType(SomeExpressionNode some)
    {
        var innerType = InferExpressionType(some.Value);
        return new OptionType(innerType);
    }

    private CalorType InferNoneType(NoneExpressionNode none)
    {
        if (none.TypeName != null)
        {
            var innerType = ResolveTypeName(none.TypeName, none.Span);
            return new OptionType(innerType);
        }
        // Type inference needed - return a type variable
        return new OptionType(new TypeVariable());
    }

    private CalorType InferOkType(OkExpressionNode ok)
    {
        var okType = InferExpressionType(ok.Value);
        return new ResultType(okType, new TypeVariable());
    }

    private CalorType InferErrType(ErrExpressionNode err)
    {
        var errType = InferExpressionType(err.Error);
        return new ResultType(new TypeVariable(), errType);
    }

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
        var targetType = InferExpressionType(field.Target);

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
                    caseType = InferExpressionType(ret.Expression, expectedType);
                }
                else
                {
                    caseType = PrimitiveType.Unit;
                }

                if (expectedType != null)
                {
                    if (!reportedTargetMismatch && !IsAssignable(expectedType, caseType))
                    {
                        hasTargetMismatch = true;
                        if (!_suppressContextualDiagnostics)
                        {
                            _diagnostics.ReportError(match.Span, DiagnosticCode.TypeMismatch,
                                $"Match arm type {caseType.SurfaceName} is not assignable to "
                                + expectedType.SurfaceName);
                        }
                        reportedTargetMismatch = true;
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

        if (!_suppressContextualDiagnostics)
        {
            _diagnostics.ReportError(span, DiagnosticCode.TypeMismatch,
                $"Match expression branches have incompatible types: {left.SurfaceName} and {right.SurfaceName}");
        }
        return ErrorType.Instance;
    }

    private CalorType ResolveTypeName(string typeName, Parsing.TextSpan span)
    {
        if (Parsing.AttributeHelper.TryUnwrapNullableAnnotation(typeName, out var referentName))
        {
            var referent = PrimitiveType.FromName(Parsing.AttributeHelper.ToSurfaceSpelling(referentName));
            CalorType? supportedReference = referent is not null
                && (referent.Equals(PrimitiveType.String) || referent.Equals(PrimitiveType.Object))
                    ? referent
                    : _moduleDeclaredReferenceTypes.Contains(referentName)
                        && _env.LookupType(referentName) is ExternalType nominal
                        ? nominal
                        : null;
            if (supportedReference is not null)
            {
                return new NullableReferenceType(supportedReference,
                    typeName.StartsWith("OPTION[inner=", StringComparison.OrdinalIgnoreCase));
            }

            var valueReferent = ResolveNullableValueReferent(referentName);
            if (valueReferent is not null)
            {
                return new NullableValueType(valueReferent);
            }

            // Other annotations retain the existing unsupported/unresolved behavior. In
            // particular, this is not permission to model arbitrary nullable payloads as
            // references or runtime Options.
        }

        // Arrays, in BOTH spellings the compiler produces. `T[]` is what the C# converter and
        // §I/§O annotations emit; `[T]` is the collection-literal spelling in the syntax
        // reference. Neither resolved before, so `§I{[str]:args}` — a documented, working
        // declaration — reported "Unknown type '[str]'".
        if (typeName.EndsWith("[]", StringComparison.Ordinal) && typeName.Length > 2)
        {
            return new ArrayType(ResolveTypeName(typeName[..^2], span));
        }
        if (typeName.Length > 2 && typeName[0] == '[' && typeName[^1] == ']'
            && typeName.IndexOf(',') < 0)
        {
            return new ArrayType(ResolveTypeName(typeName[1..^1], span));
        }

        // Handle generic types with bracket syntax: Option[INT] or Result[INT, STRING]
        var bracketIndex = typeName.IndexOf('[');
        if (bracketIndex > 0 && typeName.EndsWith(']'))
        {
            var baseName = typeName[..bracketIndex];
            var argsStr = typeName[(bracketIndex + 1)..^1];
            var args = SplitGenericArgs(argsStr);

            if (Parsing.AttributeHelper.TryUnwrapRuntimeOption(typeName, out var optionReferent))
            {
                var innerType = ResolveTypeName(optionReferent, span);
                return new OptionType(innerType);
            }

            if (baseName.Equals("Option", StringComparison.OrdinalIgnoreCase) && args.Count == 1)
                return new OptionType(ResolveTypeName(args[0], span));

            if (baseName.Equals("Result", StringComparison.OrdinalIgnoreCase) && args.Count == 2)
            {
                var okType = ResolveTypeName(args[0], span);
                var errType = ResolveTypeName(args[1], span);
                return new ResultType(okType, errType);
            }
        }

        // Handle generic types with angle bracket syntax: List<T>, Dictionary<K, V>
        var angleIndex = typeName.IndexOf('<');
        if (angleIndex > 0 && typeName.EndsWith('>'))
        {
            var baseName = typeName[..angleIndex];
            var argsStr = typeName[(angleIndex + 1)..^1];
            var args = SplitGenericArgs(argsStr);

            // Handle Option<T> with angle brackets
            if (Parsing.AttributeHelper.TryUnwrapRuntimeOption(typeName, out var optionReferent))
            {
                var innerType = ResolveTypeName(optionReferent, span);
                return new OptionType(innerType);
            }

            // Handle Result<T, E> with angle brackets
            if (baseName.Equals("Result", StringComparison.OrdinalIgnoreCase) && args.Count == 2)
            {
                var okType = ResolveTypeName(args[0], span);
                var errType = ResolveTypeName(args[1], span);
                return new ResultType(okType, errType);
            }

            // For other generic types (List<T>, Dictionary<K, V>, etc.),
            // resolve the type arguments and create a GenericInstanceType
            var resolvedArgs = new List<CalorType>();
            foreach (var arg in args)
            {
                resolvedArgs.Add(ResolveTypeName(arg, span));
            }
            return new GenericInstanceType(baseName, resolvedArgs);
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

    private static bool IsDefinitelyNotBool(CalorType type)
        => !type.Equals(PrimitiveType.Bool) && type is not ErrorType && type is not ExternalType
            && type is not NeverType;

    private static bool IsNumeric(CalorType type)
        => type.Equals(PrimitiveType.Int) || type.Equals(PrimitiveType.Float)
        || type.Equals(PrimitiveType.Char) || type.Equals(PrimitiveType.Decimal);

    private static bool IsAssignable(CalorType target, CalorType source)
    {
        if (target.Equals(source)) return true;
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
                return true; // Boxing to object preserves the value; this does not unwrap Option.
            if (source is OptionType or ResultType
                || nullableTarget.ReferentType is ExternalType && source is PrimitiveType)
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
        _variableScopes.Push(new Dictionary<string, CalorType>(StringComparer.OrdinalIgnoreCase));
        _typeScopes.Push(new Dictionary<string, CalorType>(StringComparer.OrdinalIgnoreCase));
    }

    public void EnterScope()
    {
        _variableScopes.Push(new Dictionary<string, CalorType>(StringComparer.OrdinalIgnoreCase));
        _typeScopes.Push(new Dictionary<string, CalorType>(StringComparer.OrdinalIgnoreCase));
    }

    public void ExitScope()
    {
        if (_variableScopes.Count > 1)
            _variableScopes.Pop();
        if (_typeScopes.Count > 1)
            _typeScopes.Pop();
    }

    public void DefineVariable(string name, CalorType type)
    {
        _variableScopes.Peek()[name] = type;
    }

    public CalorType? LookupVariable(string name)
    {
        foreach (var scope in _variableScopes)
        {
            if (scope.TryGetValue(name, out var type))
                return type;
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
        => _functions.TryGetValue(name, out var overloads)
            ? overloads
            : Array.Empty<FunctionCandidate>();
}
