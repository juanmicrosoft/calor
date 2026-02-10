using Calor.Compiler.Ast;

namespace Calor.Compiler.IR;

/// <summary>
/// Lowers Calor AST to Calor Normal Form (CNF).
/// This pass makes evaluation order explicit and introduces temporaries.
/// </summary>
public sealed class CnfLowering
{
    private int _tempCounter = 0;
    private int _labelCounter = 0;
    private readonly List<CnfStatement> _statements = new();
    private readonly Dictionary<string, CnfType> _variableTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Lowers a module to CNF.
    /// </summary>
    public CnfModule LowerModule(ModuleNode module)
    {
        var functions = new List<CnfFunction>();

        foreach (var func in module.Functions)
        {
            functions.Add(LowerFunction(func));
        }

        return new CnfModule(
            module.Id,
            module.Name,
            null, // TODO: Extract semantics version if declared
            functions);
    }

    /// <summary>
    /// Lowers a function to CNF.
    /// </summary>
    public CnfFunction LowerFunction(FunctionNode function)
    {
        _statements.Clear();
        _tempCounter = 0;
        _labelCounter = 0;
        _variableTypes.Clear();

        var parameters = function.Parameters
            .Select(p => new CnfParameter(p.Name, MapType(p.TypeName)))
            .ToList();

        // Register parameter types for reference resolution
        foreach (var param in parameters)
        {
            _variableTypes[param.Name] = param.Type;
        }

        // Lower preconditions
        foreach (var requires in function.Preconditions)
        {
            LowerPrecondition(requires, function.Id);
        }

        // Lower body statements
        foreach (var stmt in function.Body)
        {
            LowerStatement(stmt);
        }

        var returnType = function.Output != null
            ? MapType(function.Output.TypeName)
            : CnfType.Void;

        return new CnfFunction(
            function.Id,
            function.Name,
            parameters,
            returnType,
            new CnfSequence(_statements.ToList()));
    }

    private void LowerPrecondition(RequiresNode requires, string functionId)
    {
        var conditionVar = LowerExpression(requires.Condition);

        var okLabel = NewLabel("precond_ok");
        var failLabel = NewLabel("precond_fail");

        _statements.Add(new CnfBranch(conditionVar, okLabel, failLabel));
        _statements.Add(new CnfLabel(failLabel));

        // Throw ContractViolationException
        var exceptionExpr = new CnfCall(
            "Calor.Runtime.ContractViolationException.Create",
            new CnfExpression[]
            {
                new CnfLiteral(functionId, CnfType.String),
                new CnfLiteral(requires.Message ?? "", CnfType.String),
                new CnfLiteral("Requires", CnfType.String)
            },
            CnfType.Object);

        var exTemp = NewTemp(CnfType.Object);
        _statements.Add(new CnfAssign(exTemp, exceptionExpr, CnfType.Object));
        _statements.Add(new CnfThrow(new CnfVariableRef(exTemp, CnfType.Object)));

        _statements.Add(new CnfLabel(okLabel));
    }

    private void LowerStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case ReturnStatementNode ret:
                LowerReturn(ret);
                break;

            case BindStatementNode bind:
                LowerBind(bind);
                break;

            case IfStatementNode ifStmt:
                LowerIf(ifStmt);
                break;

            case WhileStatementNode whileStmt:
                LowerWhile(whileStmt);
                break;

            case ForStatementNode forStmt:
                LowerFor(forStmt);
                break;

            case CallStatementNode call:
                LowerCallStatement(call);
                break;

            case PrintStatementNode print:
                LowerPrint(print);
                break;

            case AssignmentStatementNode assign:
                LowerAssignment(assign);
                break;

            case TryStatementNode tryStmt:
                LowerTry(tryStmt);
                break;

            case BreakStatementNode:
                // Handled by enclosing loop context
                break;

            case ContinueStatementNode:
                // Handled by enclosing loop context
                break;

            default:
                // Skip unsupported statements for now
                break;
        }
    }

    private void LowerReturn(ReturnStatementNode ret)
    {
        if (ret.Expression != null)
        {
            var value = LowerExpression(ret.Expression);
            _statements.Add(new CnfReturn(value));
        }
        else
        {
            _statements.Add(new CnfReturn());
        }
    }

    private void LowerBind(BindStatementNode bind)
    {
        var type = MapType(bind.TypeName ?? "object");

        if (bind.Initializer != null)
        {
            var value = LowerExpression(bind.Initializer);
            _statements.Add(new CnfAssign(bind.Name, value, type));
        }
        else
        {
            // Default initialization
            var defaultValue = GetDefaultValue(type);
            _statements.Add(new CnfAssign(bind.Name, defaultValue, type));
        }

        _variableTypes[bind.Name] = type;
    }

    private void LowerIf(IfStatementNode ifStmt)
    {
        var thenLabel = NewLabel("then");
        var elseLabel = NewLabel("else");
        var endLabel = NewLabel("endif");

        // Evaluate condition
        var cond = LowerExpression(ifStmt.Condition);
        _statements.Add(new CnfBranch(cond, thenLabel, ifStmt.ElseBody != null || ifStmt.ElseIfClauses.Count > 0 ? elseLabel : endLabel));

        // Then block
        _statements.Add(new CnfLabel(thenLabel));
        foreach (var stmt in ifStmt.ThenBody)
        {
            LowerStatement(stmt);
        }
        _statements.Add(new CnfGoto(endLabel));

        // ElseIf and Else blocks
        if (ifStmt.ElseIfClauses.Count > 0 || ifStmt.ElseBody != null)
        {
            _statements.Add(new CnfLabel(elseLabel));

            foreach (var elseIf in ifStmt.ElseIfClauses)
            {
                var elseIfThenLabel = NewLabel("elseif_then");
                var nextLabel = NewLabel("elseif_next");

                var elseIfCond = LowerExpression(elseIf.Condition);
                _statements.Add(new CnfBranch(elseIfCond, elseIfThenLabel, nextLabel));

                _statements.Add(new CnfLabel(elseIfThenLabel));
                foreach (var stmt in elseIf.Body)
                {
                    LowerStatement(stmt);
                }
                _statements.Add(new CnfGoto(endLabel));

                _statements.Add(new CnfLabel(nextLabel));
            }

            if (ifStmt.ElseBody != null)
            {
                foreach (var stmt in ifStmt.ElseBody)
                {
                    LowerStatement(stmt);
                }
            }
        }

        _statements.Add(new CnfLabel(endLabel));
    }

    private void LowerWhile(WhileStatementNode whileStmt)
    {
        var headerLabel = NewLabel("while_header");
        var bodyLabel = NewLabel("while_body");
        var exitLabel = NewLabel("while_exit");

        _statements.Add(new CnfLabel(headerLabel));

        var cond = LowerExpression(whileStmt.Condition);
        _statements.Add(new CnfBranch(cond, bodyLabel, exitLabel));

        _statements.Add(new CnfLabel(bodyLabel));
        foreach (var stmt in whileStmt.Body)
        {
            LowerStatement(stmt);
        }
        _statements.Add(new CnfGoto(headerLabel));

        _statements.Add(new CnfLabel(exitLabel));
    }

    private void LowerFor(ForStatementNode forStmt)
    {
        var type = CnfType.Int;

        // Initialize loop variable
        var fromValue = LowerExpression(forStmt.From);
        _statements.Add(new CnfAssign(forStmt.VariableName, fromValue, type));

        var headerLabel = NewLabel("for_header");
        var bodyLabel = NewLabel("for_body");
        var exitLabel = NewLabel("for_exit");

        _statements.Add(new CnfLabel(headerLabel));

        // Check condition: var < to
        var toValue = LowerExpression(forStmt.To);
        var condTemp = NewTemp(CnfType.Bool);
        var condOp = new CnfBinaryOp(
            CnfBinaryOperator.LessThan,
            new CnfVariableRef(forStmt.VariableName, type),
            toValue,
            CnfType.Bool);
        _statements.Add(new CnfAssign(condTemp, condOp, CnfType.Bool));
        _statements.Add(new CnfBranch(new CnfVariableRef(condTemp, CnfType.Bool), bodyLabel, exitLabel));

        _statements.Add(new CnfLabel(bodyLabel));
        foreach (var stmt in forStmt.Body)
        {
            LowerStatement(stmt);
        }

        // Increment
        var step = forStmt.Step != null ? LowerExpression(forStmt.Step) : new CnfLiteral(1, CnfType.Int);
        var incTemp = NewTemp(type);
        var incOp = new CnfBinaryOp(
            CnfBinaryOperator.Add,
            new CnfVariableRef(forStmt.VariableName, type),
            step,
            type);
        _statements.Add(new CnfAssign(incTemp, incOp, type));
        _statements.Add(new CnfAssign(forStmt.VariableName, new CnfVariableRef(incTemp, type), type));
        _statements.Add(new CnfGoto(headerLabel));

        _statements.Add(new CnfLabel(exitLabel));
    }

    private void LowerCallStatement(CallStatementNode call)
    {
        var args = new List<CnfExpression>();
        foreach (var arg in call.Arguments)
        {
            args.Add(LowerExpression(arg));
        }

        var callExpr = new CnfCall(call.Target, args, CnfType.Void);
        var temp = NewTemp(CnfType.Void);
        _statements.Add(new CnfAssign(temp, callExpr, CnfType.Void));
    }

    private void LowerPrint(PrintStatementNode print)
    {
        var value = LowerExpression(print.Expression);
        var callExpr = new CnfCall("Console.WriteLine", new[] { value }, CnfType.Void);
        var temp = NewTemp(CnfType.Void);
        _statements.Add(new CnfAssign(temp, callExpr, CnfType.Void));
    }

    private void LowerAssignment(AssignmentStatementNode assign)
    {
        var value = LowerExpression(assign.Value);
        var targetName = assign.Target switch
        {
            ReferenceNode r => r.Name,
            _ => "_unknown_"
        };
        _statements.Add(new CnfAssign(targetName, value, value.Type));
    }

    private void LowerTry(TryStatementNode tryStmt)
    {
        var tryStatements = new List<CnfStatement>();
        var originalStatements = _statements.ToList();
        _statements.Clear();

        foreach (var stmt in tryStmt.TryBody)
        {
            LowerStatement(stmt);
        }
        tryStatements.AddRange(_statements);
        _statements.Clear();

        var catchClauses = new List<CnfCatchClause>();
        foreach (var catchClause in tryStmt.CatchClauses)
        {
            _statements.Clear();
            foreach (var stmt in catchClause.Body)
            {
                LowerStatement(stmt);
            }
            catchClauses.Add(new CnfCatchClause(
                catchClause.ExceptionType,
                catchClause.VariableName,
                new CnfSequence(_statements.ToList())));
        }

        CnfSequence? finallyBody = null;
        if (tryStmt.FinallyBody != null)
        {
            _statements.Clear();
            foreach (var stmt in tryStmt.FinallyBody)
            {
                LowerStatement(stmt);
            }
            finallyBody = new CnfSequence(_statements.ToList());
        }

        _statements.Clear();
        _statements.AddRange(originalStatements);
        _statements.Add(new CnfTry(
            new CnfSequence(tryStatements),
            catchClauses,
            finallyBody));
    }

    /// <summary>
    /// Lowers an expression to atomic form, introducing temporaries as needed.
    /// Returns an atomic expression (literal or variable reference).
    /// </summary>
    private CnfExpression LowerExpression(ExpressionNode expr)
    {
        switch (expr)
        {
            case IntLiteralNode intLit:
                return new CnfLiteral(intLit.Value, CnfType.Int);

            case FloatLiteralNode floatLit:
                return new CnfLiteral(floatLit.Value, CnfType.Double);

            case BoolLiteralNode boolLit:
                return new CnfLiteral(boolLit.Value, CnfType.Bool);

            case StringLiteralNode strLit:
                return new CnfLiteral(strLit.Value, CnfType.String);

            case ReferenceNode refNode:
                var refType = _variableTypes.TryGetValue(refNode.Name, out var tracked) ? tracked : CnfType.Object;
                return new CnfVariableRef(refNode.Name, refType);

            case BinaryOperationNode binOp:
                return LowerBinaryOp(binOp);

            case UnaryOperationNode unaryOp:
                return LowerUnaryOp(unaryOp);

            case CallExpressionNode call:
                return LowerCallExpression(call);

            case ConditionalExpressionNode cond:
                return LowerConditional(cond);

            default:
                // Unsupported expression - return placeholder
                return new CnfLiteral(0, CnfType.Int);
        }
    }

    private CnfExpression LowerBinaryOp(BinaryOperationNode binOp)
    {
        // Handle short-circuit operators specially
        if (binOp.Operator == Ast.BinaryOperator.And)
        {
            return LowerShortCircuitAnd(binOp);
        }

        if (binOp.Operator == Ast.BinaryOperator.Or)
        {
            return LowerShortCircuitOr(binOp);
        }

        // Left-to-right evaluation: lower left first, then right
        var left = LowerExpression(binOp.Left);
        var right = LowerExpression(binOp.Right);

        var op = MapBinaryOperator(binOp.Operator);
        var resultType = GetBinaryResultType(binOp.Operator, left.Type, right.Type);

        var result = new CnfBinaryOp(op, left, right, resultType);
        var temp = NewTemp(resultType);
        _statements.Add(new CnfAssign(temp, result, resultType));

        return new CnfVariableRef(temp, resultType);
    }

    private CnfExpression LowerShortCircuitAnd(BinaryOperationNode binOp)
    {
        // A && B lowered to:
        //   t_result = false
        //   branch A -> then_block, end_block
        // then_block:
        //   t_result = B
        //   goto end_block
        // end_block:

        var resultVar = NewTemp(CnfType.Bool);
        var thenLabel = NewLabel("and_then");
        var endLabel = NewLabel("and_end");

        _statements.Add(new CnfAssign(resultVar, new CnfLiteral(false, CnfType.Bool), CnfType.Bool));

        var leftVal = LowerExpression(binOp.Left);
        _statements.Add(new CnfBranch(leftVal, thenLabel, endLabel));

        _statements.Add(new CnfLabel(thenLabel));
        var rightVal = LowerExpression(binOp.Right);
        _statements.Add(new CnfAssign(resultVar, rightVal, CnfType.Bool));
        _statements.Add(new CnfGoto(endLabel));

        _statements.Add(new CnfLabel(endLabel));

        return new CnfVariableRef(resultVar, CnfType.Bool);
    }

    private CnfExpression LowerShortCircuitOr(BinaryOperationNode binOp)
    {
        // A || B lowered to:
        //   t_result = true
        //   branch A -> end_block, else_block
        // else_block:
        //   t_result = B
        //   goto end_block
        // end_block:

        var resultVar = NewTemp(CnfType.Bool);
        var elseLabel = NewLabel("or_else");
        var endLabel = NewLabel("or_end");

        _statements.Add(new CnfAssign(resultVar, new CnfLiteral(true, CnfType.Bool), CnfType.Bool));

        var leftVal = LowerExpression(binOp.Left);
        _statements.Add(new CnfBranch(leftVal, endLabel, elseLabel));

        _statements.Add(new CnfLabel(elseLabel));
        var rightVal = LowerExpression(binOp.Right);
        _statements.Add(new CnfAssign(resultVar, rightVal, CnfType.Bool));
        _statements.Add(new CnfGoto(endLabel));

        _statements.Add(new CnfLabel(endLabel));

        return new CnfVariableRef(resultVar, CnfType.Bool);
    }

    private CnfExpression LowerUnaryOp(UnaryOperationNode unaryOp)
    {
        var operand = LowerExpression(unaryOp.Operand);
        var op = MapUnaryOperator(unaryOp.Operator);
        var resultType = operand.Type;

        var result = new CnfUnaryOp(op, operand, resultType);
        var temp = NewTemp(resultType);
        _statements.Add(new CnfAssign(temp, result, resultType));

        return new CnfVariableRef(temp, resultType);
    }

    private CnfExpression LowerCallExpression(CallExpressionNode call)
    {
        // Evaluate arguments left-to-right
        var args = new List<CnfExpression>();
        foreach (var arg in call.Arguments)
        {
            args.Add(LowerExpression(arg));
        }

        var callExpr = new CnfCall(call.Target, args, CnfType.Object);
        var temp = NewTemp(CnfType.Object);
        _statements.Add(new CnfAssign(temp, callExpr, CnfType.Object));

        return new CnfVariableRef(temp, CnfType.Object);
    }

    private CnfExpression LowerConditional(ConditionalExpressionNode cond)
    {
        var resultVar = NewTemp(CnfType.Object); // TODO: infer type
        var trueLabel = NewLabel("cond_true");
        var falseLabel = NewLabel("cond_false");
        var endLabel = NewLabel("cond_end");

        var condVal = LowerExpression(cond.Condition);
        _statements.Add(new CnfBranch(condVal, trueLabel, falseLabel));

        _statements.Add(new CnfLabel(trueLabel));
        var trueVal = LowerExpression(cond.WhenTrue);
        _statements.Add(new CnfAssign(resultVar, trueVal, trueVal.Type));
        _statements.Add(new CnfGoto(endLabel));

        _statements.Add(new CnfLabel(falseLabel));
        var falseVal = LowerExpression(cond.WhenFalse);
        _statements.Add(new CnfAssign(resultVar, falseVal, falseVal.Type));
        _statements.Add(new CnfGoto(endLabel));

        _statements.Add(new CnfLabel(endLabel));

        return new CnfVariableRef(resultVar, trueVal.Type);
    }

    private string NewTemp(CnfType type)
    {
        return $"t{++_tempCounter}";
    }

    private string NewLabel(string prefix)
    {
        return $"{prefix}_{++_labelCounter}";
    }

    private static CnfType MapType(string typeName)
    {
        return typeName?.ToUpperInvariant() switch
        {
            "INT" or "I32" => CnfType.Int,
            "I64" or "LONG" => CnfType.Long,
            "F32" or "FLOAT" => CnfType.Float,
            "F64" or "DOUBLE" => CnfType.Double,
            "BOOL" => CnfType.Bool,
            "STRING" => CnfType.String,
            "VOID" => CnfType.Void,
            _ => CnfType.Object
        };
    }

    private static CnfBinaryOperator MapBinaryOperator(Ast.BinaryOperator op)
    {
        return op switch
        {
            Ast.BinaryOperator.Add => CnfBinaryOperator.Add,
            Ast.BinaryOperator.Subtract => CnfBinaryOperator.Subtract,
            Ast.BinaryOperator.Multiply => CnfBinaryOperator.Multiply,
            Ast.BinaryOperator.Divide => CnfBinaryOperator.Divide,
            Ast.BinaryOperator.Modulo => CnfBinaryOperator.Modulo,
            Ast.BinaryOperator.Power => CnfBinaryOperator.Power,
            Ast.BinaryOperator.Equal => CnfBinaryOperator.Equal,
            Ast.BinaryOperator.NotEqual => CnfBinaryOperator.NotEqual,
            Ast.BinaryOperator.LessThan => CnfBinaryOperator.LessThan,
            Ast.BinaryOperator.LessOrEqual => CnfBinaryOperator.LessOrEqual,
            Ast.BinaryOperator.GreaterThan => CnfBinaryOperator.GreaterThan,
            Ast.BinaryOperator.GreaterOrEqual => CnfBinaryOperator.GreaterOrEqual,
            Ast.BinaryOperator.BitwiseAnd => CnfBinaryOperator.BitwiseAnd,
            Ast.BinaryOperator.BitwiseOr => CnfBinaryOperator.BitwiseOr,
            Ast.BinaryOperator.BitwiseXor => CnfBinaryOperator.BitwiseXor,
            Ast.BinaryOperator.LeftShift => CnfBinaryOperator.LeftShift,
            Ast.BinaryOperator.RightShift => CnfBinaryOperator.RightShift,
            // And/Or should be handled specially before reaching here
            _ => throw new InvalidOperationException($"Cannot map operator {op} to CNF")
        };
    }

    private static CnfUnaryOperator MapUnaryOperator(UnaryOperator op)
    {
        return op switch
        {
            UnaryOperator.Negate => CnfUnaryOperator.Negate,
            UnaryOperator.Not => CnfUnaryOperator.Not,
            UnaryOperator.BitwiseNot => CnfUnaryOperator.BitwiseNot,
            _ => throw new InvalidOperationException($"Cannot map operator {op} to CNF")
        };
    }

    private static CnfType GetBinaryResultType(Ast.BinaryOperator op, CnfType leftType, CnfType rightType)
    {
        // Comparison operators always return bool
        if (op is Ast.BinaryOperator.Equal or Ast.BinaryOperator.NotEqual or
            Ast.BinaryOperator.LessThan or Ast.BinaryOperator.LessOrEqual or
            Ast.BinaryOperator.GreaterThan or Ast.BinaryOperator.GreaterOrEqual)
        {
            return CnfType.Bool;
        }

        // For arithmetic, return the wider type
        if (leftType == CnfType.Double || rightType == CnfType.Double)
            return CnfType.Double;
        if (leftType == CnfType.Float || rightType == CnfType.Float)
            return CnfType.Float;
        if (leftType == CnfType.Long || rightType == CnfType.Long)
            return CnfType.Long;

        return CnfType.Int;
    }

    private static CnfLiteral GetDefaultValue(CnfType type)
    {
        return type switch
        {
            CnfType.Int => new CnfLiteral(0, CnfType.Int),
            CnfType.Long => new CnfLiteral(0L, CnfType.Long),
            CnfType.Float => new CnfLiteral(0.0f, CnfType.Float),
            CnfType.Double => new CnfLiteral(0.0, CnfType.Double),
            CnfType.Bool => new CnfLiteral(false, CnfType.Bool),
            CnfType.String => new CnfLiteral("", CnfType.String),
            _ => new CnfLiteral(0, CnfType.Int)
        };
    }
}
