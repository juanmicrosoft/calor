using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Binding;

/// <summary>
/// Performs semantic analysis and builds the bound tree.
/// </summary>
public sealed class Binder
{
    private readonly DiagnosticBag _diagnostics;
    private Scope _scope;
    private string? _currentClassName;
    private Scope? _currentClassScope;
    private bool _isStaticContext;

    public Binder(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

    public BoundModule Bind(ModuleNode module)
    {
        var functions = new List<BoundFunction>();

        // First pass: register all function symbols in module scope
        foreach (var func in module.Functions)
        {
            var parameters = func.Parameters
                .Select(p => new VariableSymbol(p.Name, p.TypeName, isMutable: false, isParameter: true))
                .ToList();
            var returnType = func.Output?.TypeName ?? "VOID";
            var funcSymbol = new FunctionSymbol(func.Name, returnType, parameters);
            _scope.TryDeclare(funcSymbol);
        }

        // Second pass: bind function bodies
        foreach (var func in module.Functions)
        {
            functions.Add(BindFunction(func));
        }

        // Third pass: bind class member bodies
        foreach (var cls in module.Classes)
            BindClassMembers(cls, functions);

        return new BoundModule(module.Span, module.Name, functions);
    }

    private BoundFunction BindFunction(FunctionNode func)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);

        // Bind parameters
        var parameters = BindParameters(func.Parameters);

        var returnType = func.Output?.TypeName ?? "VOID";
        var functionSymbol = new FunctionSymbol(func.Name, returnType, parameters);

        // Bind body
        var boundBody = BindStatements(func.Body);

        // Extract declared effects for taint analysis
        var declaredEffects = ExtractEffects(func);

        return new BoundFunction(func.Span, functionSymbol, boundBody, functionScope, declaredEffects);
    }

    private List<VariableSymbol> BindParameters(IReadOnlyList<ParameterNode> parameters)
    {
        var result = new List<VariableSymbol>();
        foreach (var param in parameters)
        {
            var paramSymbol = new VariableSymbol(param.Name, param.TypeName, isMutable: false, isParameter: true);
            if (!_scope.TryDeclare(paramSymbol))
            {
                var suggestedName = GenerateUniqueName(param.Name);
                _diagnostics.ReportDuplicateDefinitionWithFix(param.Span, param.Name, suggestedName);
            }
            result.Add(paramSymbol);
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
        var args = new List<BoundExpression>();
        foreach (var arg in call.Arguments)
        {
            args.Add(BindExpression(arg));
        }

        return new BoundCallStatement(call.Span, call.Target, args);
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
        var loopVar = new VariableSymbol(forStmt.VariableName, "INT", isMutable: true);
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

    private BoundBindStatement BindBindStatement(BindStatementNode bind)
    {
        var typeName = bind.TypeName ?? "INT"; // Default to INT if not specified
        BoundExpression? initializer = null;

        if (bind.Initializer != null)
        {
            initializer = BindExpression(bind.Initializer);
            // Infer type from initializer if not specified
            if (bind.TypeName == null)
            {
                typeName = initializer.TypeName;
            }
        }

        var variable = new VariableSymbol(bind.Name, typeName, bind.IsMutable);

        if (!_scope.TryDeclare(variable))
        {
            _diagnostics.ReportError(bind.Span, DiagnosticCode.DuplicateDefinition,
                $"Variable '{bind.Name}' is already defined");
        }

        return new BoundBindStatement(bind.Span, variable, initializer);
    }

    private BoundExpression BindExpression(ExpressionNode expr)
    {
        return expr switch
        {
            IntLiteralNode intLit => new BoundIntLiteral(intLit.Span, intLit.Value),
            StringLiteralNode strLit => new BoundStringLiteral(strLit.Span, strLit.Value),
            BoolLiteralNode boolLit => new BoundBoolLiteral(boolLit.Span, boolLit.Value),
            FloatLiteralNode floatLit => new BoundFloatLiteral(floatLit.Span, floatLit.Value),
            DecimalLiteralNode decLit => new BoundFloatLiteral(decLit.Span, (double)decLit.Value),
            ReferenceNode refNode => BindReferenceExpression(refNode),
            BinaryOperationNode binOp => BindBinaryOperation(binOp),
            UnaryOperationNode unaryOp => BindUnaryOperation(unaryOp),
            CallExpressionNode callExpr => BindCallExpression(callExpr),
            ConditionalExpressionNode condExpr => BindConditionalExpression(condExpr),
            NameOfExpressionNode nameOf => new BoundStringLiteral(nameOf.Span, nameOf.Name),
            NoneExpressionNode none => new BoundNoneLiteral(none.Span, none.TypeName),
            // Class member expression types
            ThisExpressionNode thisExpr => _isStaticContext
                ? BindFallbackExpression(thisExpr) // 'this' not valid in static context
                : new BoundThisExpression(thisExpr.Span, _currentClassName ?? "UNKNOWN"),
            BaseExpressionNode baseExpr => new BoundBaseExpression(baseExpr.Span),
            FieldAccessNode fieldAccess => BindFieldAccess(fieldAccess),
            NewExpressionNode newExpr => BindNewExpression(newExpr),
            TypeOperationNode typeOp => BindTypeOperation(typeOp),
            IsPatternNode isPattern => BindIsPattern(isPattern),
            _ => BindFallbackExpression(expr)
        };
    }

    private BoundExpression BindFieldAccess(FieldAccessNode fieldAccess)
    {
        var target = BindExpression(fieldAccess.Target);

        // If accessing via 'this', resolve the field name from CLASS scope directly
        // (not _scope which is the method scope — that would let parameters shadow fields)
        if (fieldAccess.Target is ThisExpressionNode && _currentClassScope != null)
        {
            var symbol = _currentClassScope.LookupLocal(fieldAccess.FieldName);
            if (symbol is VariableSymbol varSymbol)
                return new BoundVariableExpression(fieldAccess.Span, varSymbol);
        }

        return new BoundFieldAccessExpression(fieldAccess.Span, target, fieldAccess.FieldName, "OBJECT");
    }

    private BoundExpression BindTypeOperation(TypeOperationNode typeOp)
    {
        var operand = BindExpression(typeOp.Operand);
        return typeOp.Operation switch
        {
            // Cast: bind inner expression and return it — the value is preserved,
            // type changes to TargetType. This prevents (cast f64 nonZeroExpr)
            // from becoming BoundIntLiteral(0) via the fallback path.
            TypeOp.Cast => operand,
            // Is: result is always BOOL
            TypeOp.Is => new BoundBoolLiteral(typeOp.Span, true),
            // As: result has the target type (nullable), bind inner
            TypeOp.As => operand,
            _ => BindFallbackExpression(typeOp)
        };
    }

    private BoundExpression BindIsPattern(IsPatternNode isPattern)
    {
        BindExpression(isPattern.Operand); // bind for side effects
        return new BoundBoolLiteral(isPattern.Span, true);
    }

    private BoundExpression BindNewExpression(NewExpressionNode newExpr)
    {
        var boundArgs = new List<BoundExpression>();
        foreach (var arg in newExpr.Arguments)
            boundArgs.Add(BindExpression(arg));

        // Also bind object initializer value expressions so they're visible to checkers
        // (e.g., new Foo { P = 1 / x } — the division must be analyzed)
        foreach (var init in newExpr.Initializers)
            BindExpression(init.Value);

        return new BoundNewExpression(newExpr.Span, newExpr.TypeName, boundArgs);
    }

    private BoundExpression BindReferenceExpression(ReferenceNode refNode)
    {
        var symbol = _scope.Lookup(refNode.Name);

        if (symbol == null)
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
            return new BoundVariableExpression(refNode.Span,
                new VariableSymbol(refNode.Name, "INT", false));
        }

        if (symbol is VariableSymbol variable)
        {
            return new BoundVariableExpression(refNode.Span, variable);
        }

        // Symbol exists but is not a variable - provide helpful fix
        _diagnostics.ReportNotAVariableWithFix(refNode.Span, refNode.Name, symbol is FunctionSymbol);
        return new BoundVariableExpression(refNode.Span,
            new VariableSymbol(refNode.Name, "INT", false));
    }

    private BoundBinaryExpression BindBinaryOperation(BinaryOperationNode binOp)
    {
        var left = BindExpression(binOp.Left);
        var right = BindExpression(binOp.Right);

        // Determine result type based on operator
        var resultType = GetBinaryOperationResultType(binOp.Operator, left.TypeName, right.TypeName);

        return new BoundBinaryExpression(binOp.Span, binOp.Operator, left, right, resultType);
    }

    private string GetBinaryOperationResultType(BinaryOperator op, string leftType, string rightType)
    {
        // Comparison operators always return BOOL
        if (op is BinaryOperator.Equal or BinaryOperator.NotEqual
            or BinaryOperator.LessThan or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual
            or BinaryOperator.And or BinaryOperator.Or)
        {
            return "BOOL";
        }

        // Arithmetic operators return the wider type
        if (leftType == "FLOAT" || rightType == "FLOAT")
        {
            return "FLOAT";
        }

        return leftType;
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
        var args = new List<BoundExpression>();
        foreach (var arg in callExpr.Arguments)
        {
            args.Add(BindExpression(arg));
        }

        // Try arity-aware lookup first (resolves overloaded sibling methods)
        string returnType;
        var funcSymbol = _scope.LookupByArity(callExpr.Target, args.Count);
        if (funcSymbol != null)
        {
            returnType = funcSymbol.ReturnType;
        }
        else
        {
            var symbol = _scope.Lookup(callExpr.Target);
            returnType = symbol is FunctionSymbol fs ? fs.ReturnType : "INT";
        }

        // Populate structured type info for effect resolution
        string? resolvedTypeName = null;
        string? resolvedMethodName = null;
        var lastDot = callExpr.Target.LastIndexOf('.');
        if (lastDot > 0)
        {
            resolvedMethodName = callExpr.Target[(lastDot + 1)..];
            var typePart = callExpr.Target[..lastDot];
            resolvedTypeName = !typePart.Contains('.')
                ? Effects.EffectEnforcementPass.MapShortTypeNameToFullName(typePart)
                : typePart;
        }

        return new BoundCallExpression(callExpr.Span, callExpr.Target, args, returnType,
            resolvedTypeName, resolvedMethodName);
    }

    private BoundExpression BindConditionalExpression(ConditionalExpressionNode condExpr)
    {
        var condition = BindExpression(condExpr.Condition);
        var whenTrue = BindExpression(condExpr.WhenTrue);
        var whenFalse = BindExpression(condExpr.WhenFalse);

        return new BoundConditionalExpression(condExpr.Span, condition, whenTrue, whenFalse);
    }

    private BoundExpression BindFallbackExpression(ExpressionNode expr)
    {
        // Return an opaque expression for unsupported types.
        // CRITICAL: Do NOT return BoundIntLiteral(0) — that causes the division-by-zero
        // checker to report false positives for every unhandled expression used as a divisor
        // (e.g., cast expressions, array length, string operations, indexers).
        // Instead, return a call expression with an opaque target that no checker will
        // confuse with a zero literal or constant.
        return new BoundCallExpression(expr.Span, $"<unsupported:{expr.GetType().Name}>",
            Array.Empty<BoundExpression>(), "OBJECT");
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
                exceptionVar = new VariableSymbol(catchClause.VariableName, typeName, isMutable: false);
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

        var cases = new List<BoundMatchCase>();
        foreach (var matchCase in matchStmt.Cases)
        {
            using var _caseScope = PushScope(_scope.CreateChild());

            // Bind pattern (if not a wildcard)
            BoundExpression? pattern = null;
            var isDefault = false;

            // Check for wildcard pattern
            if (matchCase.Pattern is WildcardPatternNode)
            {
                isDefault = true;
            }
            else if (matchCase.Pattern is LiteralPatternNode literalPattern)
            {
                pattern = BindExpression(literalPattern.Literal);
            }
            else if (matchCase.Pattern is ConstantPatternNode constantPattern)
            {
                pattern = BindExpression(constantPattern.Value);
            }
            else if (matchCase.Pattern is VariablePatternNode varPattern)
            {
                // Variable pattern captures a value - treat as default for now
                // TODO: Add variable binding to scope
                isDefault = true;
            }
            else
            {
                // For other pattern types, mark as default for now
                isDefault = true;
            }

            // Bind guard if present
            BoundExpression? guard = null;
            if (matchCase.Guard != null)
            {
                guard = BindExpression(matchCase.Guard);
            }

            var body = BindStatements(matchCase.Body);

            cases.Add(new BoundMatchCase(matchCase.Span, pattern, isDefault, guard, body));
        }

        return new BoundMatchStatement(matchStmt.Span, target, cases);
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

        var loopVar = new VariableSymbol(forEach.VariableName, forEach.VariableType, isMutable: false);
        _scope.TryDeclare(loopVar);

        if (forEach.IndexVariableName != null)
        {
            var indexVar = new VariableSymbol(forEach.IndexVariableName, "INT", isMutable: true);
            _scope.TryDeclare(indexVar);
        }

        var collection = BindExpression(forEach.Collection);
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
            resource = new VariableSymbol(usingStmt.VariableName, typeName, isMutable: false);
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

    private Scope CreateClassScope(ClassDefinitionNode cls)
    {
        var classScope = _scope.CreateChild();
        foreach (var field in cls.Fields)
        {
            var isMutable = !field.Modifiers.HasFlag(MethodModifiers.Readonly);
            var fieldSymbol = new VariableSymbol(field.Name, field.TypeName, isMutable: isMutable);
            classScope.TryDeclare(fieldSymbol);
        }
        return classScope;
    }

    private static void RegisterClassMembers(ClassDefinitionNode cls, Scope classScope)
    {
        // Register methods (with overload support)
        foreach (var method in cls.Methods)
        {
            var parameters = method.Parameters
                .Select(p => new VariableSymbol(p.Name, p.TypeName, isMutable: false, isParameter: true))
                .ToList();
            var returnType = method.Output?.TypeName ?? "VOID";
            classScope.DeclareOverload(new FunctionSymbol(method.Name, returnType, parameters));
        }

        // Register constructors (resolvable as class name)
        foreach (var ctor in cls.Constructors)
        {
            var parameters = ctor.Parameters
                .Select(p => new VariableSymbol(p.Name, p.TypeName, isMutable: false, isParameter: true))
                .ToList();
            classScope.DeclareOverload(new FunctionSymbol(cls.Name, "VOID", parameters));
        }

        // Register properties as symbols (so property access resolves)
        foreach (var prop in cls.Properties)
        {
            classScope.TryDeclare(new VariableSymbol(prop.Name, prop.TypeName, isMutable: prop.Setter != null || prop.Initer != null));
        }
    }

    private void BindClassMembers(ClassDefinitionNode cls, List<BoundFunction> functions)
    {
        var className = cls.Name;
        var classScope = CreateClassScope(cls);
        RegisterClassMembers(cls, classScope);

        using var _ = PushScope(classScope);
        var previousClassName = _currentClassName;
        var previousClassScope = _currentClassScope;
        _currentClassName = className;
        _currentClassScope = classScope;

        try
        {
            // Methods
            foreach (var method in cls.Methods)
            {
                if (method.IsAbstract || method.IsExtern || method.Body.Count == 0)
                    continue;
                var bound = TryBindMember(() => BindMethod(method, className), method.Span, className, method.Name);
                if (bound != null) functions.Add(bound);
            }

            // Constructors
            foreach (var ctor in cls.Constructors)
            {
                if (ctor.Body.Count == 0) continue;
                var bound = TryBindMember(() => BindConstructor(ctor, className), ctor.Span, className, ".ctor");
                if (bound != null) functions.Add(bound);
            }

            // Property accessors
            foreach (var prop in cls.Properties)
            {
                if (prop.Getter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(prop.Getter, className, prop.Name, prop.TypeName),
                        prop.Getter.Span, className, $"{prop.Name}.get");
                    if (bound != null) functions.Add(bound);
                }
                if (prop.Setter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(prop.Setter, className, prop.Name, prop.TypeName),
                        prop.Setter.Span, className, $"{prop.Name}.set");
                    if (bound != null) functions.Add(bound);
                }
                if (prop.Initer is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindPropertyAccessor(prop.Initer, className, prop.Name, prop.TypeName),
                        prop.Initer.Span, className, $"{prop.Name}.init");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Operator overloads
            foreach (var op in cls.OperatorOverloads)
            {
                if (op.Body.Count == 0) continue;
                var bound = TryBindMember(() => BindOperator(op, className), op.Span, className, $"op_{op.Kind}");
                if (bound != null) functions.Add(bound);
            }

            // Indexer accessors
            foreach (var ixer in cls.Indexers)
            {
                if (ixer.Getter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindIndexerAccessor(ixer.Getter, ixer.Parameters, className, ixer.TypeName),
                        ixer.Getter.Span, className, "this[].get");
                    if (bound != null) functions.Add(bound);
                }
                if (ixer.Setter is { IsAutoImplemented: false })
                {
                    var bound = TryBindMember(
                        () => BindIndexerAccessor(ixer.Setter, ixer.Parameters, className, ixer.TypeName),
                        ixer.Setter.Span, className, "this[].set");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Event accessors
            foreach (var evt in cls.Events)
            {
                if (evt.AddBody != null && evt.AddBody.Count > 0)
                {
                    var bound = TryBindMember(
                        () => BindEventAccessor(evt.AddBody, className, evt.Name, "add", evt.DelegateType, evt.Span),
                        evt.Span, className, $"{evt.Name}.add");
                    if (bound != null) functions.Add(bound);
                }
                if (evt.RemoveBody != null && evt.RemoveBody.Count > 0)
                {
                    var bound = TryBindMember(
                        () => BindEventAccessor(evt.RemoveBody, className, evt.Name, "remove", evt.DelegateType, evt.Span),
                        evt.Span, className, $"{evt.Name}.remove");
                    if (bound != null) functions.Add(bound);
                }
            }

            // Recurse into nested classes — isolate scope so nested classes
            // don't inherit outer class fields (C# semantics: nested classes
            // need explicit reference to access outer instance members)
            foreach (var nested in cls.NestedClasses)
            {
                // Temporarily restore to module scope (parent of class scope)
                var outerScope = _scope;
                _scope = classScope.Parent!;
                BindClassMembers(nested, functions);
                _scope = outerScope;
            }
        }
        finally
        {
            _currentClassName = previousClassName;
            _currentClassScope = previousClassScope;
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

        var parameters = BindParameters(method.Parameters);
        var returnType = method.Output?.TypeName ?? "VOID";
        var qualifiedName = $"{className}.{method.Name}";
        var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
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

        var parameters = BindParameters(ctor.Parameters);
        var qualifiedName = $"{className}..ctor";
        var functionSymbol = new FunctionSymbol(qualifiedName, "VOID", parameters);

        // Bind constructor initializer (: base(...) / : this(...)) as a call prepended to body.
        // This makes initializer arguments visible to bug pattern checkers (e.g., div-by-zero in base(x / y)).
        // Note: fields set by the chained constructor are NOT tracked (requires interprocedural analysis).
        var boundBody = new List<BoundStatement>();
        if (ctor.Initializer != null)
        {
            var initArgs = new List<BoundExpression>();
            foreach (var arg in ctor.Initializer.Arguments)
                initArgs.Add(BindExpression(arg));
            var initTarget = ctor.Initializer.IsBaseCall ? "base..ctor" : $"{className}..ctor";
            boundBody.Add(new BoundCallStatement(ctor.Initializer.Span, initTarget, initArgs));
        }
        boundBody.AddRange(BindStatements(ctor.Body));

        return new BoundFunction(ctor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), BoundMemberKind.Constructor, className);
    }

    private BoundFunction BindPropertyAccessor(
        PropertyAccessorNode accessor, string className, string propName, string propType)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);

        var parameters = new List<VariableSymbol>();
        var memberKind = BoundMemberKind.PropertyGetter;

        if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
            or PropertyAccessorNode.AccessorKind.Init)
        {
            var valueParam = new VariableSymbol("value", propType, isMutable: false, isParameter: true);
            _scope.TryDeclare(valueParam);
            parameters.Add(valueParam);
            memberKind = accessor.Kind == PropertyAccessorNode.AccessorKind.Set
                ? BoundMemberKind.PropertySetter : BoundMemberKind.PropertyInit;
        }

        var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? propType : "VOID";
        var qualifiedName = $"{className}.{propName}.{accessor.Kind.ToString().ToLowerInvariant()}";
        var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
        var boundBody = BindStatements(accessor.Body);
        return new BoundFunction(accessor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className);
    }

    private BoundFunction BindOperator(OperatorOverloadNode op, string className)
    {
        var functionScope = _scope.CreateChild();
        using var _s = PushScope(functionScope);
        using var _c = PushStaticContext(true); // operators are always static in C#

        var parameters = BindParameters(op.Parameters);
        var returnType = op.Output?.TypeName ?? "VOID";
        var qualifiedName = $"{className}.op_{op.Kind}";
        var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
        var boundBody = BindStatements(op.Body);
        // OperatorOverloadNode has no Effects field — mark as unknown
        var declaredEffects = new List<string> { "*:*" };
        return new BoundFunction(op.Span, functionSymbol, boundBody, functionScope,
            declaredEffects, BoundMemberKind.OperatorOverload, className);
    }

    private BoundFunction BindIndexerAccessor(
        PropertyAccessorNode accessor, IReadOnlyList<ParameterNode> indexerParams,
        string className, string indexerType)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);

        var parameters = BindParameters(indexerParams);
        var memberKind = BoundMemberKind.IndexerGetter;

        if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
            or PropertyAccessorNode.AccessorKind.Init)
        {
            var valueParam = new VariableSymbol("value", indexerType, isMutable: false, isParameter: true);
            _scope.TryDeclare(valueParam);
            parameters.Add(valueParam);
            memberKind = BoundMemberKind.IndexerSetter;
        }

        var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? indexerType : "VOID";
        var qualifiedName = $"{className}.this[].{accessor.Kind.ToString().ToLowerInvariant()}";
        var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
        var boundBody = BindStatements(accessor.Body);
        return new BoundFunction(accessor.Span, functionSymbol, boundBody, functionScope,
            Array.Empty<string>(), memberKind, className);
    }

    private BoundFunction BindEventAccessor(
        IReadOnlyList<StatementNode> body, string className, string eventName,
        string accessorKind, string delegateType, Parsing.TextSpan span)
    {
        var functionScope = _scope.CreateChild();
        using var _ = PushScope(functionScope);

        var valueParam = new VariableSymbol("value", delegateType, isMutable: false, isParameter: true);
        _scope.TryDeclare(valueParam);
        var parameters = new List<VariableSymbol> { valueParam };

        var memberKind = accessorKind == "add" ? BoundMemberKind.EventAdd : BoundMemberKind.EventRemove;
        var qualifiedName = $"{className}.{eventName}.{accessorKind}";
        var functionSymbol = new FunctionSymbol(qualifiedName, "VOID", parameters);
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
