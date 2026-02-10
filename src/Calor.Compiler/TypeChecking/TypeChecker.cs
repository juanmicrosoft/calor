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
        var paramTypes = new List<CalorType>();
        foreach (var param in func.Parameters)
        {
            var paramType = ResolveTypeName(param.TypeName, param.Span);
            paramTypes.Add(paramType);
        }

        var returnType = func.Output != null
            ? ResolveTypeName(func.Output.TypeName, func.Output.Span)
            : PrimitiveType.Void;

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
            _ => ErrorType.Instance
        };
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

        // For now, return Unit type for match expressions
        // A more complete implementation would unify the types of all case bodies
        return PrimitiveType.Unit;
    }

    private CalorType ResolveTypeName(string typeName, Parsing.TextSpan span)
    {
        // Handle generic types like Option[INT] or Result[INT, STRING]
        var bracketIndex = typeName.IndexOf('[');
        if (bracketIndex > 0)
        {
            var baseName = typeName[..bracketIndex];
            var argsStr = typeName[(bracketIndex + 1)..^1];
            var args = argsStr.Split(',').Select(a => a.Trim()).ToList();

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

        // Try primitive type
        var primitive = PrimitiveType.FromName(typeName);
        if (primitive != null)
            return primitive;

        // Try user-defined type
        var userType = _env.LookupType(typeName);
        if (userType != null)
            return userType;

        _diagnostics.ReportError(span, DiagnosticCode.UndefinedReference,
            $"Unknown type '{typeName}'");
        return ErrorType.Instance;
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
