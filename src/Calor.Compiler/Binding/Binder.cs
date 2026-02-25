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

    public Binder(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _scope = new Scope();
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

        return new BoundModule(module.Span, module.Name, functions);
    }

    private BoundFunction BindFunction(FunctionNode func)
    {
        var functionScope = _scope.CreateChild();
        var previousScope = _scope;
        _scope = functionScope;

        try
        {
            // Bind parameters
            var parameters = new List<VariableSymbol>();
            foreach (var param in func.Parameters)
            {
                var paramSymbol = new VariableSymbol(param.Name, param.TypeName, isMutable: false, isParameter: true);
                if (!_scope.TryDeclare(paramSymbol))
                {
                    var suggestedName = GenerateUniqueName(param.Name);
                    _diagnostics.ReportDuplicateDefinitionWithFix(param.Span, param.Name, suggestedName);
                }
                parameters.Add(paramSymbol);
            }

            var returnType = func.Output?.TypeName ?? "VOID";
            var functionSymbol = new FunctionSymbol(func.Name, returnType, parameters);

            // Bind body
            var boundBody = BindStatements(func.Body);

            // Extract declared effects for taint analysis
            var declaredEffects = ExtractEffects(func);

            return new BoundFunction(func.Span, functionSymbol, boundBody, functionScope, declaredEffects);
        }
        finally
        {
            _scope = previousScope;
        }
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
            TryStatementNode tryStmt => BindTryStatement(tryStmt),
            MatchStatementNode matchStmt => BindMatchStatement(matchStmt),
            ProofObligationNode proof => BindProofObligation(proof),
            _ => throw new InvalidOperationException($"Unknown statement type: {stmt.GetType().Name}")
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
        var loopScope = _scope.CreateChild();
        var previousScope = _scope;
        _scope = loopScope;

        try
        {
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
        finally
        {
            _scope = previousScope;
        }
    }

    private BoundWhileStatement BindWhileStatement(WhileStatementNode whileStmt)
    {
        var loopScope = _scope.CreateChild();
        var previousScope = _scope;
        _scope = loopScope;

        try
        {
            var condition = BindExpression(whileStmt.Condition);
            var body = BindStatements(whileStmt.Body);

            return new BoundWhileStatement(whileStmt.Span, condition, body);
        }
        finally
        {
            _scope = previousScope;
        }
    }

    private BoundIfStatement BindIfStatement(IfStatementNode ifStmt)
    {
        var condition = BindExpression(ifStmt.Condition);

        var thenScope = _scope.CreateChild();
        var previousScope = _scope;
        _scope = thenScope;
        var thenBody = BindStatements(ifStmt.ThenBody);
        _scope = previousScope;

        var elseIfClauses = new List<BoundElseIfClause>();
        foreach (var elseIf in ifStmt.ElseIfClauses)
        {
            var elseIfCondition = BindExpression(elseIf.Condition);
            var elseIfScope = _scope.CreateChild();
            _scope = elseIfScope;
            var elseIfBody = BindStatements(elseIf.Body);
            _scope = previousScope;
            elseIfClauses.Add(new BoundElseIfClause(elseIf.Span, elseIfCondition, elseIfBody));
        }

        IReadOnlyList<BoundStatement>? elseBody = null;
        if (ifStmt.ElseBody != null)
        {
            var elseScope = _scope.CreateChild();
            _scope = elseScope;
            elseBody = BindStatements(ifStmt.ElseBody);
            _scope = previousScope;
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
            ReferenceNode refNode => BindReferenceExpression(refNode),
            BinaryOperationNode binOp => BindBinaryOperation(binOp),
            UnaryOperationNode unaryOp => BindUnaryOperation(unaryOp),
            CallExpressionNode callExpr => BindCallExpression(callExpr),
            ConditionalExpressionNode condExpr => BindConditionalExpression(condExpr),
            NameOfExpressionNode nameOf => new BoundStringLiteral(nameOf.Span, nameOf.Name),
            _ => BindFallbackExpression(expr)
        };
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

        // Look up function symbol to determine return type
        var symbol = _scope.Lookup(callExpr.Target);
        var returnType = symbol is FunctionSymbol funcSym ? funcSym.ReturnType : "INT";

        return new BoundCallExpression(callExpr.Span, callExpr.Target, args, returnType);
    }

    private BoundExpression BindConditionalExpression(ConditionalExpressionNode condExpr)
    {
        var condition = BindExpression(condExpr.Condition);
        var whenTrue = BindExpression(condExpr.WhenTrue);
        var whenFalse = BindExpression(condExpr.WhenFalse);

        // Return the type of the true branch (both should match, but we don't enforce here)
        return whenTrue;
    }

    private BoundExpression BindFallbackExpression(ExpressionNode expr)
    {
        // For expression types not yet fully supported in binding,
        // report a diagnostic and return a placeholder
        _diagnostics.ReportError(expr.Span, DiagnosticCode.TypeMismatch,
            $"Unsupported expression type in binding: {expr.GetType().Name}");
        return new BoundIntLiteral(expr.Span, 0);
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
        var tryScope = _scope.CreateChild();
        var previousScope = _scope;
        _scope = tryScope;
        var tryBody = BindStatements(tryStmt.TryBody);
        _scope = previousScope;

        // Bind catch clauses
        var catchClauses = new List<BoundCatchClause>();
        foreach (var catchClause in tryStmt.CatchClauses)
        {
            var catchScope = _scope.CreateChild();
            _scope = catchScope;

            VariableSymbol? exceptionVar = null;
            if (catchClause.VariableName != null)
            {
                var typeName = catchClause.ExceptionType ?? "Exception";
                exceptionVar = new VariableSymbol(catchClause.VariableName, typeName, isMutable: false);
                _scope.TryDeclare(exceptionVar);
            }

            var catchBody = BindStatements(catchClause.Body);
            _scope = previousScope;

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
            var finallyScope = _scope.CreateChild();
            _scope = finallyScope;
            finallyBody = BindStatements(tryStmt.FinallyBody);
            _scope = previousScope;
        }

        return new BoundTryStatement(tryStmt.Span, tryBody, catchClauses, finallyBody);
    }

    private BoundMatchStatement BindMatchStatement(MatchStatementNode matchStmt)
    {
        var target = BindExpression(matchStmt.Target);

        var cases = new List<BoundMatchCase>();
        foreach (var matchCase in matchStmt.Cases)
        {
            var caseScope = _scope.CreateChild();
            var previousScope = _scope;
            _scope = caseScope;

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
            _scope = previousScope;

            cases.Add(new BoundMatchCase(matchCase.Span, pattern, isDefault, guard, body));
        }

        return new BoundMatchStatement(matchStmt.Span, target, cases);
    }

    /// <summary>
    /// Extracts effect declarations from a function node.
    /// Returns effects in "category:value" format (e.g., "io:database_write").
    /// </summary>
    private static IReadOnlyList<string> ExtractEffects(FunctionNode func)
    {
        if (func.Effects?.Effects == null || func.Effects.Effects.Count == 0)
            return Array.Empty<string>();

        var effects = new List<string>();
        foreach (var (category, value) in func.Effects.Effects)
        {
            // Store as "category:value" - TaintAnalysis will parse this
            effects.Add($"{category.ToLowerInvariant()}:{value.ToLowerInvariant()}");
        }
        return effects;
    }
}
