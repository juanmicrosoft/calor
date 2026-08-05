using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.TypeChecking;

/// <summary>
/// Performs type checking and inference on the AST.
/// </summary>
public sealed class TypeChecker
{
    private readonly DiagnosticBag _diagnostics;
    private readonly TypeEnvironment _env;

    public TypeChecker(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _env = new TypeEnvironment();
    }

    public void Check(ModuleNode module)
    {
        // Pass 0: register refinement type definitions
        foreach (var rtype in module.RefinementTypes)
        {
            RegisterRefinementType(rtype);
        }

        // First pass: register all type definitions
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

        if (_env.LookupType(rtype.Name) != null)
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
        _env.DefineFunction(func.Name, funcType);
    }

    private void CheckFunction(FunctionNode func)
    {
        _env.EnterScope();

        RegisterTypeParameters(func);

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
    {
        foreach (var arg in call.Arguments)
        {
            InferExpressionType(arg);
        }
    }

    private void CheckReturnStatement(ReturnStatementNode ret)
    {
        if (ret.Expression != null)
        {
            InferExpressionType(ret.Expression);
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
            var initType = InferExpressionType(bind.Initializer);

            if (bind.TypeName != null)
            {
                varType = ResolveTypeName(bind.TypeName, bind.Span);
                if (!IsAssignable(varType, initType))
                {
                    _diagnostics.ReportError(bind.Span, DiagnosticCode.TypeMismatch,
                        $"Cannot assign {initType.SurfaceName} to variable of type {varType.SurfaceName}");
                }
            }
            else
            {
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

    private void CheckPattern(PatternNode pattern, CalorType expectedType)
    {
        switch (pattern)
        {
            case WildcardPatternNode:
                // Wildcard matches anything
                break;

            case VariablePatternNode varPat:
                _env.DefineVariable(varPat.Name, expectedType);
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
                    CheckPattern(somePat.InnerPattern, optType.InnerType);
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
                    CheckPattern(okPat.InnerPattern, resType.OkType);
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
                    CheckPattern(errPat.InnerPattern, errResType.ErrType);
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
                _env.DefineVariable(varPatNode.Name, expectedType);
                break;

            // `§K{Type:name}` — a type test with an optional binding. The bound name takes the
            // tested type, which the checker may not model; ExternalType is the honest answer.
            case TypePatternNode typePat:
                if (!string.IsNullOrEmpty(typePat.BindingName))
                {
                    _env.DefineVariable(typePat.BindingName!, ResolveTypeName(typePat.TypeName, typePat.Span));
                }
                break;

            // Composites: recurse so nested bindings land in scope.
            case AndPatternNode andPat:
                CheckPattern(andPat.Left, expectedType);
                CheckPattern(andPat.Right, expectedType);
                break;

            case OrPatternNode orPat:
                CheckPattern(orPat.Left, expectedType);
                CheckPattern(orPat.Right, expectedType);
                break;

            case NegatedPatternNode negPat:
                CheckPattern(negPat.Inner, expectedType);
                break;

            case ListPatternNode listPat:
                var elementType = expectedType is ArrayType at ? at.ElementType
                    : expectedType is GenericInstanceType { BaseName: "List", TypeArguments.Count: 1 } lg
                        ? lg.TypeArguments[0]
                        : ErrorType.Instance;
                foreach (var sub in listPat.Patterns)
                {
                    CheckPattern(sub, elementType);
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

    private CalorType InferExpressionType(ExpressionNode expr)
    {
        return expr switch
        {
            IntLiteralNode => PrimitiveType.Int,
            FloatLiteralNode => PrimitiveType.Float,
            BoolLiteralNode => PrimitiveType.Bool,
            StringLiteralNode => PrimitiveType.String,
            ReferenceNode refNode => InferReferenceType(refNode),
            BinaryOperationNode binOp => InferBinaryOperationType(binOp),
            SomeExpressionNode some => InferSomeType(some),
            NoneExpressionNode none => InferNoneType(none),
            OkExpressionNode ok => InferOkType(ok),
            ErrExpressionNode err => InferErrType(err),
            RecordCreationNode rec => InferRecordCreationType(rec),
            FieldAccessNode field => InferFieldAccessType(field),
            MatchExpressionNode match => InferMatchExpressionType(match),
            // Collection expression types
            ListCreationNode list => InferListCreationType(list),
            DictionaryCreationNode dict => InferDictionaryCreationType(dict),
            SetCreationNode set => InferSetCreationType(set),
            CollectionContainsNode contains => InferCollectionContainsType(contains),
            CollectionCountNode count => InferCollectionCountType(count),
            ArrayAccessNode arrayAccess => InferArrayAccessType(arrayAccess),
            TypeOperationNode typeOp => InferTypeOperationType(typeOp),
            _ => ErrorType.Instance
        };
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
        return type.Equals(PrimitiveType.Int)
            || type.Equals(PrimitiveType.Float)
            || type.Equals(PrimitiveType.Bool);
    }

    private CalorType InferReferenceType(ReferenceNode refNode)
    {
        var type = _env.LookupVariable(refNode.Name);
        if (type != null)
        {
            return type;
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
        if (refNode.Name is "default" or "null" or "this" or "base" or "value")
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
        var rightType = InferExpressionType(binOp.Right);

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
            && (leftType.Equals(PrimitiveType.String) || rightType.Equals(PrimitiveType.String)))
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
        if (targetType is ErrorType or ExternalType)
        {
            return ErrorType.Instance;
        }

        _diagnostics.ReportError(field.Span, DiagnosticCode.TypeMismatch,
            $"Cannot access field on non-record type {targetType.SurfaceName}");
        return ErrorType.Instance;
    }

    private CalorType InferMatchExpressionType(MatchExpressionNode match)
    {
        var targetType = InferExpressionType(match.Target);

        // Unify the types of all case bodies
        CalorType? unifiedType = null;
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
                    caseType = InferExpressionType(ret.Expression);
                }
                else
                {
                    caseType = PrimitiveType.Unit;
                }

                if (unifiedType == null)
                {
                    unifiedType = caseType;
                }
                else if (!unifiedType.Equals(caseType) && caseType is not ErrorType && unifiedType is not ErrorType)
                {
                    _diagnostics.ReportError(match.Span, DiagnosticCode.TypeMismatch,
                        $"Match expression branches have incompatible types: {unifiedType.SurfaceName} and {caseType.SurfaceName}");
                }
            }

            _env.ExitScope();
        }

        return unifiedType ?? PrimitiveType.Unit;
    }

    private CalorType ResolveTypeName(string typeName, Parsing.TextSpan span)
    {
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

            if (baseName.Equals("Option", StringComparison.OrdinalIgnoreCase) && args.Count == 1)
            {
                var innerType = ResolveTypeName(args[0], span);
                return new OptionType(innerType);
            }

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
            if (baseName.Equals("Option", StringComparison.OrdinalIgnoreCase) && args.Count == 1)
            {
                var innerType = ResolveTypeName(args[0], span);
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
        => type is not ErrorType && type is not ExternalType;

    private static bool IsDefinitelyNotBool(CalorType type)
        => !type.Equals(PrimitiveType.Bool) && type is not ErrorType && type is not ExternalType;

    private static bool IsNumeric(CalorType type)
        => type.Equals(PrimitiveType.Int) || type.Equals(PrimitiveType.Float)
        || type.Equals(PrimitiveType.Char) || type.Equals(PrimitiveType.Decimal);

    private static bool IsAssignable(CalorType target, CalorType source)
    {
        if (target.Equals(source)) return true;
        if (source is ErrorType) return true; // Allow error types to be assigned anywhere
        // Nothing is known about an unmodeled external type, in either direction.
        if (target is ExternalType || source is ExternalType) return true;
        if (target.Equals(PrimitiveType.Float) && source.Equals(PrimitiveType.Int)) return true;
        // `object` is the top type: anything may be assigned TO it. Deliberately not symmetric —
        // assigning object to a concrete type needs a cast in C#, so accepting it here would
        // green-light code the emitted C# rejects.
        if (target.Equals(PrimitiveType.Object)) return true;
        // char widens to an integer, as in C#. Not the reverse: `i32 -> char` is a narrowing
        // conversion C# requires an explicit cast for.
        if (target.Equals(PrimitiveType.Int) && source.Equals(PrimitiveType.Char)) return true;
        if (target.Equals(PrimitiveType.Float) && source.Equals(PrimitiveType.Char)) return true;
        // Refined type is a subtype of its base type (erasure)
        if (source is RefinedType refinedSource && IsAssignable(target, refinedSource.BaseType)) return true;
        return false;
    }

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
public sealed class TypeEnvironment
{
    private readonly Stack<Dictionary<string, CalorType>> _variableScopes = new();
    private readonly Stack<Dictionary<string, CalorType>> _typeScopes = new();
    private readonly Dictionary<string, CalorType> _globalTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FunctionType> _functions = new(StringComparer.OrdinalIgnoreCase);

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

    public void DefineFunction(string name, FunctionType type)
    {
        _functions[name] = type;
    }

    public FunctionType? LookupFunction(string name)
    {
        return _functions.TryGetValue(name, out var type) ? type : null;
    }
}
