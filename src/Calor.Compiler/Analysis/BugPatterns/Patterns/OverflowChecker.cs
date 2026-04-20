using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Microsoft.Z3;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

/// <summary>
/// Checks for potential integer overflow in arithmetic operations.
/// Uses Z3 SMT solver to verify if overflow can occur given the path conditions.
/// </summary>
public sealed class OverflowChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public string Name => "OVERFLOW";

    public OverflowChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Check(BoundFunction function, DiagnosticBag diagnostics)
    {
        foreach (var stmt in function.Body)
        {
            CheckStatement(stmt, function, diagnostics, new List<BoundExpression>());
        }
    }

    private void CheckStatement(
        BoundStatement stmt,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                if (bind.Initializer != null)
                {
                    CheckExpression(bind.Initializer, function, diagnostics, pathConditions);
                }
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                {
                    CheckExpression(ret.Expression, function, diagnostics, pathConditions);
                }
                break;

            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                {
                    CheckExpression(arg, function, diagnostics, pathConditions);
                }
                break;

            case BoundIfStatement ifStmt:
                CheckExpression(ifStmt.Condition, function, diagnostics, pathConditions);

                var thenConditions = new List<BoundExpression>(pathConditions) { ifStmt.Condition };
                foreach (var s in ifStmt.ThenBody)
                {
                    CheckStatement(s, function, diagnostics, thenConditions);
                }

                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CheckExpression(elseIf.Condition, function, diagnostics, pathConditions);
                    var elseIfConditions = new List<BoundExpression>(pathConditions) { elseIf.Condition };
                    foreach (var s in elseIf.Body)
                    {
                        CheckStatement(s, function, diagnostics, elseIfConditions);
                    }
                }

                if (ifStmt.ElseBody != null)
                {
                    foreach (var s in ifStmt.ElseBody)
                    {
                        CheckStatement(s, function, diagnostics, pathConditions);
                    }
                }
                break;

            case BoundWhileStatement whileStmt:
                CheckExpression(whileStmt.Condition, function, diagnostics, pathConditions);
                var whileConditions = new List<BoundExpression>(pathConditions) { whileStmt.Condition };
                foreach (var s in whileStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, whileConditions);
                }
                break;

            case BoundForStatement forStmt:
                CheckExpression(forStmt.From, function, diagnostics, pathConditions);
                CheckExpression(forStmt.To, function, diagnostics, pathConditions);
                if (forStmt.Step != null)
                {
                    CheckExpression(forStmt.Step, function, diagnostics, pathConditions);
                }
                foreach (var s in forStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundAssignmentStatement assign:
                CheckExpression(assign.Target, function, diagnostics, pathConditions);
                CheckExpression(assign.Value, function, diagnostics, pathConditions);
                break;

            case BoundCompoundAssignment compound:
                CheckExpression(compound.Target, function, diagnostics, pathConditions);
                CheckExpression(compound.Value, function, diagnostics, pathConditions);
                break;

            case BoundForeachStatement forEach:
                CheckExpression(forEach.Collection, function, diagnostics, pathConditions);
                foreach (var s in forEach.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundDoWhileStatement doWhile:
                CheckExpression(doWhile.Condition, function, diagnostics, pathConditions);
                foreach (var s in doWhile.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundUsingStatement usingStmt:
                CheckExpression(usingStmt.ResourceExpression, function, diagnostics, pathConditions);
                foreach (var s in usingStmt.Body)
                {
                    CheckStatement(s, function, diagnostics, pathConditions);
                }
                break;

            case BoundExpressionStatement exprStmt:
                CheckExpression(exprStmt.Expression, function, diagnostics, pathConditions);
                break;

            case BoundThrowStatement throwStmt:
                if (throwStmt.Expression != null)
                {
                    CheckExpression(throwStmt.Expression, function, diagnostics, pathConditions);
                }
                break;
        }
    }

    private void CheckExpression(
        BoundExpression expr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        switch (expr)
        {
            case BoundBinaryExpression binExpr:
                // Check for overflow-prone operations
                if (IsOverflowProne(binExpr.Operator))
                {
                    CheckOverflow(binExpr, function, diagnostics, pathConditions);
                }

                // Recursively check subexpressions
                CheckExpression(binExpr.Left, function, diagnostics, pathConditions);
                CheckExpression(binExpr.Right, function, diagnostics, pathConditions);
                break;

            case BoundUnaryExpression unaryExpr:
                // Check for negation overflow (e.g., -INT_MIN)
                if (unaryExpr.Operator == Ast.UnaryOperator.Negate)
                {
                    CheckNegationOverflow(unaryExpr, function, diagnostics, pathConditions);
                }
                CheckExpression(unaryExpr.Operand, function, diagnostics, pathConditions);
                break;

            case BoundCallExpression callExpr:
                foreach (var arg in callExpr.Arguments)
                {
                    CheckExpression(arg, function, diagnostics, pathConditions);
                }
                break;

            case BoundConditionalExpression condExpr:
                CheckExpression(condExpr.Condition, function, diagnostics, pathConditions);
                CheckExpression(condExpr.WhenTrue, function, diagnostics, pathConditions);
                CheckExpression(condExpr.WhenFalse, function, diagnostics, pathConditions);
                break;
        }
    }

    private static bool IsOverflowProne(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => true,
            BinaryOperator.Subtract => true,
            BinaryOperator.Multiply => true,
            BinaryOperator.LeftShift => true,
            _ => false
        };
    }

    private void CheckOverflow(
        BoundBinaryExpression binExpr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        // Skip if both operands are small constants (common case)
        if (AreBothSmallConstants(binExpr.Left, binExpr.Right))
        {
            return;
        }

        // Quick heuristics first
        var potentialOverflow = QuickOverflowCheck(binExpr);
        if (potentialOverflow == false)
        {
            return; // Definitely safe
        }

        if (_options.UseZ3Verification)
        {
            var canOverflow = CanOverflowWithZ3(binExpr, function, pathConditions);
            if (canOverflow == true)
            {
                var opName = binExpr.Operator switch
                {
                    BinaryOperator.Add => "addition",
                    BinaryOperator.Subtract => "subtraction",
                    BinaryOperator.Multiply => "multiplication",
                    BinaryOperator.LeftShift => "left shift",
                    _ => "operation"
                };

                diagnostics.ReportWarning(
                    binExpr.Span,
                    DiagnosticCode.IntegerOverflow,
                    $"Potential integer overflow in {opName}");
            }
        }
        else if (potentialOverflow == true && !_options.ReportOnlyVerified)
        {
            // Heuristic-only finding — only show with --all-findings
            diagnostics.ReportInfo(
                binExpr.Span,
                DiagnosticCode.IntegerOverflow,
                "Arithmetic operation may overflow (enable Z3 for precise analysis)");
        }
    }

    private void CheckNegationOverflow(
        BoundUnaryExpression unaryExpr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        // Check for -INT_MIN overflow
        if (unaryExpr.Operand is BoundIntLiteral intLit)
        {
            if (intLit.Value == int.MinValue)
            {
                diagnostics.ReportWarning(
                    unaryExpr.Span,
                    DiagnosticCode.IntegerOverflow,
                    "Negation of INT_MIN causes overflow");
            }
            return;
        }

        if (_options.UseZ3Verification)
        {
            var canOverflow = CanNegationOverflowWithZ3(unaryExpr, function, pathConditions);
            if (canOverflow == true)
            {
                diagnostics.ReportWarning(
                    unaryExpr.Span,
                    DiagnosticCode.IntegerOverflow,
                    "Potential overflow in negation (value may be INT_MIN)");
            }
        }
    }

    private static bool AreBothSmallConstants(BoundExpression left, BoundExpression right)
    {
        var leftValue = GetConstantValue(left);
        var rightValue = GetConstantValue(right);

        if (leftValue == null || rightValue == null)
            return false;

        // Consider "small" as values that won't overflow when operated on
        const int SmallThreshold = 10000;
        return Math.Abs(leftValue.Value) < SmallThreshold &&
               Math.Abs(rightValue.Value) < SmallThreshold;
    }

    private static long? GetConstantValue(BoundExpression expr)
    {
        return expr switch
        {
            BoundIntLiteral intLit => intLit.Value,
            _ => null
        };
    }

    private static bool? QuickOverflowCheck(BoundBinaryExpression binExpr)
    {
        var leftValue = GetConstantValue(binExpr.Left);
        var rightValue = GetConstantValue(binExpr.Right);

        // If both are constants, we can compute exactly
        if (leftValue != null && rightValue != null)
        {
            try
            {
                checked
                {
                    _ = binExpr.Operator switch
                    {
                        BinaryOperator.Add => leftValue.Value + rightValue.Value,
                        BinaryOperator.Subtract => leftValue.Value - rightValue.Value,
                        BinaryOperator.Multiply => leftValue.Value * rightValue.Value,
                        BinaryOperator.LeftShift => leftValue.Value << (int)rightValue.Value,
                        _ => 0
                    };
                }
                return false; // No overflow
            }
            catch (OverflowException)
            {
                return true; // Definitely overflows
            }
        }

        // Can't determine statically
        return null;
    }

    private bool? CanOverflowWithZ3(
        BoundBinaryExpression binExpr,
        BoundFunction function,
        List<BoundExpression> pathConditions)
    {
        try
        {
            using var ctx = new Context();
            var translator = new BoundExpressionTranslator(ctx);

            // Declare parameters
            foreach (var param in function.Symbol.Parameters)
            {
                translator.DeclareVariable(param.Name, param.TypeName);
            }

            // Translate path conditions
            var pathConstraints = new List<BoolExpr>();
            foreach (var condition in pathConditions)
            {
                var translated = translator.TranslateBoolExpr(condition);
                if (translated != null)
                {
                    pathConstraints.Add(translated);
                }
            }

            // Translate operands
            var left = translator.TranslateExpr(binExpr.Left);
            var right = translator.TranslateExpr(binExpr.Right);

            if (left == null || right == null || left is not BitVecExpr bvLeft || right is not BitVecExpr bvRight)
            {
                return null;
            }

            // Check for overflow using Z3's overflow predicates
            var solver = ctx.MkSolver();
            solver.Set("timeout", _options.Z3TimeoutMs);

            foreach (var constraint in pathConstraints)
            {
                solver.Assert(constraint);
            }

            // Create overflow condition based on operation type
            BoolExpr? overflowCondition = binExpr.Operator switch
            {
                BinaryOperator.Add => CreateAddOverflowCondition(ctx, bvLeft, bvRight),
                BinaryOperator.Subtract => CreateSubOverflowCondition(ctx, bvLeft, bvRight),
                BinaryOperator.Multiply => CreateMulOverflowCondition(ctx, bvLeft, bvRight),
                _ => null
            };

            if (overflowCondition == null)
                return null;

            solver.Assert(overflowCondition);

            var status = solver.Check();

            return status switch
            {
                Status.SATISFIABLE => true,
                Status.UNSATISFIABLE => false,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private bool? CanNegationOverflowWithZ3(
        BoundUnaryExpression unaryExpr,
        BoundFunction function,
        List<BoundExpression> pathConditions)
    {
        try
        {
            using var ctx = new Context();
            var translator = new BoundExpressionTranslator(ctx);

            foreach (var param in function.Symbol.Parameters)
            {
                translator.DeclareVariable(param.Name, param.TypeName);
            }

            var pathConstraints = new List<BoolExpr>();
            foreach (var condition in pathConditions)
            {
                var translated = translator.TranslateBoolExpr(condition);
                if (translated != null)
                {
                    pathConstraints.Add(translated);
                }
            }

            var operand = translator.TranslateExpr(unaryExpr.Operand);
            if (operand == null || operand is not BitVecExpr bvOperand)
            {
                return null;
            }

            var solver = ctx.MkSolver();
            solver.Set("timeout", _options.Z3TimeoutMs);

            foreach (var constraint in pathConstraints)
            {
                solver.Assert(constraint);
            }

            // Check if operand can be INT_MIN (which causes overflow when negated)
            var width = bvOperand.SortSize;
            var intMin = ctx.MkBV(1L << ((int)width - 1), width); // 0x80000000 for 32-bit
            solver.Assert(ctx.MkEq(bvOperand, intMin));

            var status = solver.Check();

            return status switch
            {
                Status.SATISFIABLE => true,
                Status.UNSATISFIABLE => false,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static BoolExpr? CreateAddOverflowCondition(Context ctx, BitVecExpr left, BitVecExpr right)
    {
        // Signed overflow for addition: result has different sign than both operands
        // Overflow if: (left > 0 && right > 0 && result <= 0) || (left < 0 && right < 0 && result >= 0)
        var width = left.SortSize;
        var zero = ctx.MkBV(0, width);
        var result = ctx.MkBVAdd(left, right);

        var positiveOverflow = ctx.MkAnd(
            ctx.MkBVSGT(left, zero),
            ctx.MkBVSGT(right, zero),
            ctx.MkBVSLE(result, zero));

        var negativeOverflow = ctx.MkAnd(
            ctx.MkBVSLT(left, zero),
            ctx.MkBVSLT(right, zero),
            ctx.MkBVSGE(result, zero));

        return ctx.MkOr(positiveOverflow, negativeOverflow);
    }

    private static BoolExpr? CreateSubOverflowCondition(Context ctx, BitVecExpr left, BitVecExpr right)
    {
        // Signed overflow for subtraction: left and right have different signs, and result has same sign as right
        var width = left.SortSize;
        var zero = ctx.MkBV(0, width);
        var result = ctx.MkBVSub(left, right);

        // Overflow if: (left > 0 && right < 0 && result <= 0) || (left < 0 && right > 0 && result >= 0)
        var positiveMinusNegativeOverflow = ctx.MkAnd(
            ctx.MkBVSGT(left, zero),
            ctx.MkBVSLT(right, zero),
            ctx.MkBVSLE(result, zero));

        var negativeMinusPositiveOverflow = ctx.MkAnd(
            ctx.MkBVSLT(left, zero),
            ctx.MkBVSGT(right, zero),
            ctx.MkBVSGE(result, zero));

        return ctx.MkOr(positiveMinusNegativeOverflow, negativeMinusPositiveOverflow);
    }

    private static BoolExpr? CreateMulOverflowCondition(Context ctx, BitVecExpr left, BitVecExpr right)
    {
        // For multiplication overflow, use the property that a*b/b != a when overflow occurs
        // This is a simplified check - Z3's bitvector division handles this
        var width = left.SortSize;
        var zero = ctx.MkBV(0, width);

        // If neither operand is zero, check if result/right != left
        var product = ctx.MkBVMul(left, right);
        var quotient = ctx.MkBVSDiv(product, right);

        // right != 0 && quotient != left
        var rightNonZero = ctx.MkNot(ctx.MkEq(right, zero));
        var overflowOccurred = ctx.MkNot(ctx.MkEq(quotient, left));

        return ctx.MkAnd(rightNonZero, overflowOccurred);
    }
}
