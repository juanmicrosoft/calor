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

    private void RegisterFunction(FunctionNode func)
    {
        _env.EnterScope();

        // Register type parameters first so they can be resolved in parameter types
        foreach (var tp in func.TypeParameters)
        {
            var tpType = new TypeParameterType(tp.Name, tp.Constraints);
            _env.DefineType(tp.Name, tpType);
        }

        var paramTypes = new List<CalorType>();
        foreach (var param in func.Parameters)
        {
            var paramType = ResolveTypeName(param.TypeName, param.Span);
            paramTypes.Add(paramType);
        }

        var returnType = func.Output != null
            ? ResolveTypeName(func.Output.TypeName, func.Output.Span)
            : PrimitiveType.Void;

        _env.ExitScope();

        var funcType = new FunctionType(paramTypes, returnType);
        _env.DefineFunction(func.Name, funcType);
    }

    private void CheckFunction(FunctionNode func)
    {
        _env.EnterScope();

        // Register type parameters in scope
        foreach (var tp in func.TypeParameters)
        {
            var tpType = new TypeParameterType(tp.Name, tp.Constraints);
            _env.DefineType(tp.Name, tpType);
        }

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
                $"FOR 'from' expression must be numeric, got {fromType.Name}");
        }

        if (!IsNumeric(toType))
        {
            _diagnostics.ReportError(forStmt.To.Span, DiagnosticCode.TypeMismatch,
                $"FOR 'to' expression must be numeric, got {toType.Name}");
        }

        if (forStmt.Step != null)
        {
            var stepType = InferExpressionType(forStmt.Step);
            if (!IsNumeric(stepType))
            {
                _diagnostics.ReportError(forStmt.Step.Span, DiagnosticCode.TypeMismatch,
                    $"FOR 'step' expression must be numeric, got {stepType.Name}");
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
        if (!condType.Equals(PrimitiveType.Bool))
        {
            _diagnostics.ReportError(whileStmt.Condition.Span, DiagnosticCode.TypeMismatch,
                $"WHILE condition must be BOOL, got {condType.Name}");
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
        if (!condType.Equals(PrimitiveType.Bool))
        {
            _diagnostics.ReportError(ifStmt.Condition.Span, DiagnosticCode.TypeMismatch,
                $"IF condition must be BOOL, got {condType.Name}");
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
            if (!elseIfCondType.Equals(PrimitiveType.Bool))
            {
                _diagnostics.ReportError(elseIf.Condition.Span, DiagnosticCode.TypeMismatch,
                    $"ELSEIF condition must be BOOL, got {elseIfCondType.Name}");
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
                        $"Cannot assign {initType.Name} to variable of type {varType.Name}");
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
            _diagnostics.ReportError(bind.Span, DiagnosticCode.TypeMismatch,
                "Variable binding requires either a type annotation or an initializer");
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
                if (!guardType.Equals(PrimitiveType.Bool))
                {
                    _diagnostics.ReportError(matchCase.Guard.Span, DiagnosticCode.TypeMismatch,
                        $"Match guard must be BOOL, got {guardType.Name}");
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
                        $"Cannot add {valueType.Name} to {collectionType.Name}, expected {elementType.Name}");
                }
            }
            else
            {
                _diagnostics.ReportError(push.Span, DiagnosticCode.TypeMismatch,
                    $"PUSH operation requires List or HashSet, got {collectionType.Name}");
            }
        }
        else
        {
            _diagnostics.ReportError(push.Span, DiagnosticCode.TypeMismatch,
                $"PUSH operation requires a collection type, got {collectionType.Name}");
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
                    $"Dictionary key type mismatch: expected {expectedKeyType.Name}, got {keyType.Name}");
            }

            if (!IsAssignable(expectedValueType, valueType))
            {
                _diagnostics.ReportError(put.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Dictionary value type mismatch: expected {expectedValueType.Name}, got {valueType.Name}");
            }
        }
        else
        {
            _diagnostics.ReportError(put.Span, DiagnosticCode.TypeMismatch,
                $"PUT operation requires a Dictionary, got {dictType?.Name ?? "unknown"}");
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
                    $"Cannot remove {removeType.Name} from {collectionType.Name}, expected {expectedType.Name}");
            }
        }
        else
        {
            _diagnostics.ReportError(remove.Span, DiagnosticCode.TypeMismatch,
                $"REM operation requires a collection type, got {collectionType.Name}");
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
                $"List index must be numeric, got {indexType.Name}");
        }

        if (collectionType is GenericInstanceType git && git.BaseName == "List" && git.TypeArguments.Count == 1)
        {
            var elementType = git.TypeArguments[0];
            if (!IsAssignable(elementType, valueType))
            {
                _diagnostics.ReportError(setIndex.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Cannot assign {valueType.Name} to list element of type {elementType.Name}");
            }
        }
        else
        {
            _diagnostics.ReportError(setIndex.Span, DiagnosticCode.TypeMismatch,
                $"SETIDX operation requires a List, got {collectionType.Name}");
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
        if (collectionType is not GenericInstanceType git ||
            (git.BaseName != "List" && git.BaseName != "Dictionary" && git.BaseName != "HashSet"))
        {
            _diagnostics.ReportError(clear.Span, DiagnosticCode.TypeMismatch,
                $"CLR operation requires a collection type, got {collectionType.Name}");
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
                $"List index must be numeric, got {indexType.Name}");
        }

        if (collectionType is GenericInstanceType git && git.BaseName == "List" && git.TypeArguments.Count == 1)
        {
            var elementType = git.TypeArguments[0];
            if (!IsAssignable(elementType, valueType))
            {
                _diagnostics.ReportError(insert.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Cannot insert {valueType.Name} into list of type {elementType.Name}");
            }
        }
        else
        {
            _diagnostics.ReportError(insert.Span, DiagnosticCode.TypeMismatch,
                $"INS operation requires a List, got {collectionType.Name}");
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
        else
        {
            _diagnostics.ReportError(dictForeach.Dictionary.Span, DiagnosticCode.TypeMismatch,
                $"EACHKV requires a Dictionary, got {dictType.Name}");

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
                        $"Pattern literal type {litType.Name} does not match expected type {expectedType.Name}");
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
                        $"Some pattern can only match Option types, got {expectedType.Name}");
                }
                break;

            case NonePatternNode nonePat:
                if (expectedType is not OptionType)
                {
                    _diagnostics.ReportError(nonePat.Span, DiagnosticCode.TypeMismatch,
                        $"None pattern can only match Option types, got {expectedType.Name}");
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
                        $"Ok pattern can only match Result types, got {expectedType.Name}");
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
                        $"Err pattern can only match Result types, got {expectedType.Name}");
                }
                break;
            default:
                // Unknown pattern type - report for safety
                _diagnostics.ReportError(pattern.Span, DiagnosticCode.TypeMismatch,
                    $"Unsupported pattern type: {pattern.GetType().Name}");
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
                    $"List element type mismatch: expected {elementType.Name}, got {actualType.Name}");
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
                    $"Dictionary key type mismatch: expected {keyType.Name}, got {actualKeyType.Name}");
            }

            if (!IsAssignable(valueType, actualValueType))
            {
                _diagnostics.ReportError(entry.Value.Span, DiagnosticCode.TypeMismatch,
                    $"Dictionary value type mismatch: expected {valueType.Name}, got {actualValueType.Name}");
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
                    $"Set element type mismatch: expected {elementType.Name}, got {actualType.Name}");
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
                    $"Contains check type mismatch: expected {expectedType.Name}, got {checkType.Name}");
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
                    $"CNT requires a collection type, got {collectionType.Name}");
            }
        }
        else if (collectionType is not ErrorType)
        {
            _diagnostics.ReportError(count.Collection.Span, DiagnosticCode.TypeMismatch,
                $"CNT requires a collection type, got {collectionType.Name}");
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
                        $"Dictionary key type mismatch: expected {expectedKeyType.Name}, got {indexType.Name}");
                }
                return git.TypeArguments[1]; // Return value type
            }
            else
            {
                _diagnostics.ReportError(arrayAccess.Index.Span, DiagnosticCode.TypeMismatch,
                    $"Array/List index must be numeric, got {indexType.Name}");
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

    private CalorType InferReferenceType(ReferenceNode refNode)
    {
        var type = _env.LookupVariable(refNode.Name);
        if (type == null)
        {
            _diagnostics.ReportError(refNode.Span, DiagnosticCode.UndefinedReference,
                $"Undefined variable '{refNode.Name}'");
            return ErrorType.Instance;
        }
        return type;
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
            if (!leftType.Equals(PrimitiveType.Bool) || !rightType.Equals(PrimitiveType.Bool))
            {
                _diagnostics.ReportError(binOp.Span, DiagnosticCode.TypeMismatch,
                    "Logical operators require BOOL operands");
            }
            return PrimitiveType.Bool;
        }

        // Arithmetic operators
        if (!IsNumericType(leftType) || !IsNumericType(rightType))
        {
            if (!(leftType is ErrorType) && !(rightType is ErrorType))
            {
                _diagnostics.ReportError(binOp.Span, DiagnosticCode.TypeMismatch,
                    $"Arithmetic operators require numeric operands, got {leftType.Name} and {rightType.Name}");
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
                        $"Cannot assign {valueType.Name} to field '{fieldAssign.FieldName}' of type {field.Type.Name}");
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
                    $"Unknown field '{field.FieldName}' on type '{recordType.Name}'");
                return ErrorType.Instance;
            }
            return fieldDef.Type;
        }

        _diagnostics.ReportError(field.Span, DiagnosticCode.TypeMismatch,
            $"Cannot access field on non-record type {targetType.Name}");
        return ErrorType.Instance;
    }

    private CalorType InferMatchExpressionType(MatchExpressionNode match)
    {
        var targetType = InferExpressionType(match.Target);

        // Unify the types of all case bodies
        CalorType? unifiedType = null;
        foreach (var matchCase in match.Cases)
        {
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
                        $"Match expression branches have incompatible types: {unifiedType.Name} and {caseType.Name}");
                }
            }
        }

        return unifiedType ?? PrimitiveType.Unit;
    }

    private CalorType ResolveTypeName(string typeName, Parsing.TextSpan span)
    {
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

        // Try user-defined type (includes type parameters in scope)
        var userType = _env.LookupType(typeName);
        if (userType != null)
            return userType;

        _diagnostics.ReportError(span, DiagnosticCode.UndefinedReference,
            $"Unknown type '{typeName}'");
        return ErrorType.Instance;
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

    private static bool IsNumeric(CalorType type)
        => type.Equals(PrimitiveType.Int) || type.Equals(PrimitiveType.Float);

    private static bool IsAssignable(CalorType target, CalorType source)
    {
        if (target.Equals(source)) return true;
        if (source is ErrorType) return true; // Allow error types to be assigned anywhere
        if (target.Equals(PrimitiveType.Float) && source.Equals(PrimitiveType.Int)) return true;
        return false;
    }

    private static bool IsNumericType(CalorType type)
    {
        return type.Equals(PrimitiveType.Int) || type.Equals(PrimitiveType.Float) || type is ErrorType;
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
