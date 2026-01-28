using Opal.Compiler.Ast;
using Opal.Compiler.Diagnostics;

namespace Opal.Compiler.Binding;

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
                    _diagnostics.ReportError(param.Span, DiagnosticCode.DuplicateDefinition,
                        $"Parameter '{param.Name}' is already defined");
                }
                parameters.Add(paramSymbol);
            }

            var returnType = func.Output?.TypeName ?? "VOID";
            var functionSymbol = new FunctionSymbol(func.Name, returnType, parameters);

            // Bind body
            var boundBody = BindStatements(func.Body);

            return new BoundFunction(func.Span, functionSymbol, boundBody, functionScope);
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
            _ => throw new InvalidOperationException($"Unknown statement type: {stmt.GetType().Name}")
        };
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
            _ => throw new InvalidOperationException($"Unknown expression type: {expr.GetType().Name}")
        };
    }

    private BoundExpression BindReferenceExpression(ReferenceNode refNode)
    {
        var symbol = _scope.Lookup(refNode.Name);

        if (symbol == null)
        {
            _diagnostics.ReportError(refNode.Span, DiagnosticCode.UndefinedReference,
                $"Undefined variable '{refNode.Name}'");
            // Return a dummy variable to continue analysis
            return new BoundVariableExpression(refNode.Span,
                new VariableSymbol(refNode.Name, "INT", false));
        }

        if (symbol is VariableSymbol variable)
        {
            return new BoundVariableExpression(refNode.Span, variable);
        }

        _diagnostics.ReportError(refNode.Span, DiagnosticCode.TypeMismatch,
            $"'{refNode.Name}' is not a variable");
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
}
