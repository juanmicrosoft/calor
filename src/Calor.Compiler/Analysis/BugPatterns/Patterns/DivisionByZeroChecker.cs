using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Microsoft.Z3;

namespace Calor.Compiler.Analysis.BugPatterns.Patterns;

/// <summary>
/// Checks for potential division by zero.
/// Uses Z3 SMT solver to verify if the divisor can be zero given the path conditions.
/// </summary>
public sealed class DivisionByZeroChecker : IBugPatternChecker
{
    private readonly BugPatternOptions _options;

    public string Name => "DIV_ZERO";

    public DivisionByZeroChecker(BugPatternOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Check(BoundFunction function, DiagnosticBag diagnostics)
    {
        // Walk through all statements and expressions looking for divisions
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
                // Check the condition itself
                CheckExpression(ifStmt.Condition, function, diagnostics, pathConditions);

                // Check then branch with condition as path constraint
                var thenConditions = new List<BoundExpression>(pathConditions) { ifStmt.Condition };
                foreach (var s in ifStmt.ThenBody)
                {
                    CheckStatement(s, function, diagnostics, thenConditions);
                }

                // Check else-if branches
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CheckExpression(elseIf.Condition, function, diagnostics, pathConditions);
                    var elseIfConditions = new List<BoundExpression>(pathConditions) { elseIf.Condition };
                    foreach (var s in elseIf.Body)
                    {
                        CheckStatement(s, function, diagnostics, elseIfConditions);
                    }
                }

                // Check else branch (if present)
                if (ifStmt.ElseBody != null)
                {
                    // In else branch, the condition is negated
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

            default:
                foreach (var expression in BoundNodeHelpers.GetImmediateExpressions(stmt))
                    CheckExpression(expression, function, diagnostics, pathConditions);
                foreach (var statement in BoundNodeHelpers.GetImmediateStatements(stmt))
                    CheckStatement(statement, function, diagnostics, pathConditions);
                break;
        }
    }

    private void CheckExpression(
        BoundExpression expr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        if (expr is BoundBinaryExpression divisionExpr
            && divisionExpr.Operator is BinaryOperator.Divide or BinaryOperator.Modulo)
        {
            var divisor = BoundNodeHelpers.GetDivisor(divisionExpr);
            if (divisor != null)
            {
                CheckDivisor(divisor, divisionExpr, function, diagnostics, pathConditions);
            }
        }

        foreach (var child in BoundNodeHelpers.GetChildExpressions(expr))
            CheckExpression(child, function, diagnostics, pathConditions);
    }

    private void CheckDivisor(
        BoundExpression divisor,
        BoundBinaryExpression divisionExpr,
        BoundFunction function,
        DiagnosticBag diagnostics,
        List<BoundExpression> pathConditions)
    {
        // #762 B5 review C2: numeric casts are zero-preserving — see THROUGH the
        // conversion wrapper or a cast divisor loses its Z3-verified warning and the
        // Calor0926 suggester (the old Cast arm returned the operand bare, so this
        // worked by accident before B5).
        while (divisor is BoundTypeOperationExpression { Operation: TypeOp.Cast } conversion)
            divisor = conversion.Operand;

        // Quick check: literal zero is always a bug
        if (BoundNodeHelpers.IsLiteralZero(divisor))
        {
            diagnostics.ReportError(
                divisionExpr.Span,
                DiagnosticCode.DivisionByZero,
                "Division by literal zero");
            return;
        }

        // Non-zero literal is always safe
        if (BoundNodeHelpers.IsConstant(divisor) && !BoundNodeHelpers.IsLiteralZero(divisor))
        {
            return;
        }

        // Simple constant propagation: if divisor is a variable initialized to a non-zero constant, it's safe
        if (divisor is BoundVariableExpression constVarExpr)
        {
            var initValue = FindVariableInitializer(constVarExpr.Variable, function);
            if (initValue != null && BoundNodeHelpers.IsConstant(initValue) && !BoundNodeHelpers.IsLiteralZero(initValue))
            {
                return;
            }

            // Loop bound tracking: if divisor is a loop variable with lower bound > 0, it's safe
            var lowerBound = FindLoopLowerBound(constVarExpr.Variable, function);
            if (lowerBound is BoundIntLiteral lowerLit && lowerLit.Value > 0)
            {
                return;
            }
        }

        // If Z3 verification is enabled, use SMT solving
        if (_options.UseZ3Verification)
        {
            var canBeZero = CanDivisorBeZero(divisor, function, pathConditions);
            if (canBeZero == true)
            {
                diagnostics.ReportWarning(
                    divisionExpr.Span,
                    DiagnosticCode.DivisionByZero,
                    $"Potential division by zero: divisor can be zero under some conditions");
            }
            else if (canBeZero == null && !_options.ReportOnlyVerified)
            {
                // Unknown - report as info (only when --all-findings is used)
                diagnostics.ReportInfo(
                    divisionExpr.Span,
                    DiagnosticCode.DivisionByZero,
                    "Division by zero check inconclusive (complex expression)");
            }
            // false means proven safe - no diagnostic
        }
        else if (!_options.ReportOnlyVerified)
        {
            // Simple heuristic without Z3: warn if divisor is a variable without obvious guard
            // (only when --all-findings is used — heuristic findings are not verified)
            if (divisor is BoundVariableExpression varExpr)
            {
                // Check if there's a guard in the path conditions
                var hasGuard = HasZeroGuard(varExpr.Variable, pathConditions);
                if (!hasGuard)
                {
                    diagnostics.ReportWarning(
                        divisionExpr.Span,
                        DiagnosticCode.DivisionByZero,
                        $"Potential division by zero: '{varExpr.Variable.Name}' may be zero");
                }
            }
        }
    }

    private bool? CanDivisorBeZero(
        BoundExpression divisor,
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
                translator.DeclareVariable(param);
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

            // Translate the divisor
            var divisorExpr = translator.TranslateExpr(divisor);
            if (divisorExpr == null)
            {
                return null; // Can't translate - unknown
            }

            // Check if (path conditions && divisor == 0) is satisfiable
            var solver = ctx.MkSolver();
            solver.Set("timeout", _options.Z3TimeoutMs);

            foreach (var constraint in pathConstraints)
            {
                solver.Assert(constraint);
            }

            // Assert divisor == 0
            if (divisorExpr is BitVecExpr bvExpr)
            {
                solver.Assert(ctx.MkEq(bvExpr, ctx.MkBV(0, bvExpr.SortSize)));
            }
            else
            {
                return null; // Not a numeric type
            }

            var status = solver.Check();

            return status switch
            {
                Status.SATISFIABLE => true, // Can be zero
                Status.UNSATISFIABLE => false, // Proven non-zero
                _ => null // Unknown
            };
        }
        catch
        {
            return null; // Error during analysis
        }
    }

    private static bool HasZeroGuard(
        VariableSymbol variable,
        List<BoundExpression> pathConditions)
    {
        // Check if any path condition is of the form "variableName != 0" or "variableName > 0"
        foreach (var condition in pathConditions)
        {
            if (condition is BoundBinaryExpression binExpr)
            {
                // Check for x != 0
                if (binExpr.Operator == BinaryOperator.NotEqual)
                {
                    if (IsVariableAndZero(binExpr.Left, binExpr.Right, variable) ||
                        IsVariableAndZero(binExpr.Right, binExpr.Left, variable))
                    {
                        return true;
                    }
                }

                // Check for x > 0 or x < 0 (both imply non-zero)
                if (binExpr.Operator == BinaryOperator.GreaterThan ||
                    binExpr.Operator == BinaryOperator.LessThan)
                {
                    if (IsVariableAndZero(binExpr.Left, binExpr.Right, variable) ||
                        IsVariableAndZero(binExpr.Right, binExpr.Left, variable))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the initializer expression for a variable declared in the function body.
    /// Searches recursively into if/loop/try blocks.
    /// Returns null if not found or if the variable is reassigned.
    /// </summary>
    private static BoundExpression? FindVariableInitializer(
        VariableSymbol variable,
        BoundFunction function)
    {
        return FindVariableInitializerInStatements(variable, function.Body);
    }

    private static BoundExpression? FindVariableInitializerInStatements(
        VariableSymbol variable,
        IReadOnlyList<BoundStatement> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is BoundBindStatement bind &&
                BoundNodeHelpers.SameSymbol(bind.Variable, variable) &&
                bind.Initializer != null)
            {
                return bind.Initializer;
            }

            if (stmt is BoundIfStatement ifStmt)
            {
                var thenResult = FindVariableInitializerInStatements(variable, ifStmt.ThenBody);
                if (thenResult != null) return thenResult;
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    var elseIfResult = FindVariableInitializerInStatements(variable, elseIf.Body);
                    if (elseIfResult != null) return elseIfResult;
                }
                if (ifStmt.ElseBody != null)
                {
                    var elseResult = FindVariableInitializerInStatements(variable, ifStmt.ElseBody);
                    if (elseResult != null) return elseResult;
                }
            }
            else if (stmt is BoundForStatement forStmt)
            {
                var forResult = FindVariableInitializerInStatements(variable, forStmt.Body);
                if (forResult != null) return forResult;
            }
            else if (stmt is BoundWhileStatement whileStmt)
            {
                var whileResult = FindVariableInitializerInStatements(variable, whileStmt.Body);
                if (whileResult != null) return whileResult;
            }
            else if (stmt is BoundTryStatement tryStmt)
            {
                var tryResult = FindVariableInitializerInStatements(variable, tryStmt.TryBody);
                if (tryResult != null) return tryResult;
                foreach (var catchClause in tryStmt.CatchClauses)
                {
                    var catchResult = FindVariableInitializerInStatements(variable, catchClause.Body);
                    if (catchResult != null) return catchResult;
                }
                if (tryStmt.FinallyBody != null)
                {
                    var finallyResult = FindVariableInitializerInStatements(variable, tryStmt.FinallyBody);
                    if (finallyResult != null) return finallyResult;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the enclosing for-loop for a variable and returns the lower bound if it's a positive constant.
    /// Returns null if not found or if the lower bound is not a positive constant.
    /// </summary>
    private static BoundExpression? FindLoopLowerBound(
        VariableSymbol variable,
        BoundFunction function)
    {
        return FindLoopLowerBoundInStatements(variable, function.Body);
    }

    private static BoundExpression? FindLoopLowerBoundInStatements(
        VariableSymbol variable,
        IReadOnlyList<BoundStatement> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is BoundForStatement forStmt)
            {
                if (BoundNodeHelpers.SameSymbol(forStmt.LoopVariable, variable))
                {
                    return forStmt.From;
                }
                // Check nested statements
                var nested = FindLoopLowerBoundInStatements(variable, forStmt.Body);
                if (nested != null) return nested;
            }
            else if (stmt is BoundIfStatement ifStmt)
            {
                var thenResult = FindLoopLowerBoundInStatements(variable, ifStmt.ThenBody);
                if (thenResult != null) return thenResult;
                if (ifStmt.ElseBody != null)
                {
                    var elseResult = FindLoopLowerBoundInStatements(variable, ifStmt.ElseBody);
                    if (elseResult != null) return elseResult;
                }
            }
            else if (stmt is BoundWhileStatement whileStmt)
            {
                var whileResult = FindLoopLowerBoundInStatements(variable, whileStmt.Body);
                if (whileResult != null) return whileResult;
            }
        }
        return null;
    }

    private static bool IsVariableAndZero(
        BoundExpression maybeVar,
        BoundExpression maybeZero,
        VariableSymbol variable)
    {
        return maybeVar is BoundVariableExpression varExpr &&
               BoundNodeHelpers.SameSymbol(varExpr.Variable, variable) &&
               BoundNodeHelpers.IsLiteralZero(maybeZero);
    }
}

/// <summary>
/// Translates bound expressions to Z3 expressions for bug pattern analysis.
/// </summary>
internal sealed class BoundExpressionTranslator
{
    private readonly Context _ctx;
    private readonly Dictionary<SymbolId, (Expr Expr, string Type)> _variables = new();

    public BoundExpressionTranslator(Context ctx)
    {
        _ctx = ctx;
    }

    public bool DeclareVariable(VariableSymbol variable)
    {
        if (variable.Id.IsNone)
            return false;

        var expr = CreateVariableForType(GetSolverName(variable), variable.TypeName);
        if (expr == null)
            return false;

        _variables[variable.Id] = (expr, variable.TypeName);
        return true;
    }

    public BoolExpr? TranslateBoolExpr(BoundExpression expr)
    {
        return TranslateExpr(expr) as BoolExpr;
    }

    public Expr? TranslateExpr(BoundExpression expr)
    {
        return expr switch
        {
            BoundIntLiteral intLit => _ctx.MkBV(intLit.Value, 32),
            BoundBoolLiteral boolLit => _ctx.MkBool(boolLit.Value),
            BoundVariableExpression varExpr => TranslateVariable(varExpr),
            BoundBinaryExpression binExpr => TranslateBinaryOp(binExpr),
            BoundUnaryExpression unaryExpr => TranslateUnaryOp(unaryExpr),
            BoundCallExpression callExpr => TranslateCall(callExpr),
            _ => null
        };
    }

    private Expr? TranslateCall(BoundCallExpression callExpr)
    {
        var target = callExpr.Target.ToLowerInvariant();

        // math.abs / abs: ite(x >= 0, x, -x)
        if ((target == "math.abs" || target == "abs") && callExpr.Arguments.Count == 1)
        {
            var arg = TranslateExpr(callExpr.Arguments[0]);
            if (arg is BitVecExpr bv)
            {
                var zero = _ctx.MkBV(0, bv.SortSize);
                return _ctx.MkITE(_ctx.MkBVSGE(bv, zero), bv, _ctx.MkBVNeg(bv));
            }
        }

        // math.min / min: ite(x <= y, x, y)
        if ((target == "math.min" || target == "min") && callExpr.Arguments.Count == 2)
        {
            var x = TranslateExpr(callExpr.Arguments[0]);
            var y = TranslateExpr(callExpr.Arguments[1]);
            if (x is BitVecExpr bvX && y is BitVecExpr bvY)
                return _ctx.MkITE(_ctx.MkBVSLE(bvX, bvY), bvX, bvY);
        }

        // math.max / max: ite(x >= y, x, y)
        if ((target == "math.max" || target == "max") && callExpr.Arguments.Count == 2)
        {
            var x = TranslateExpr(callExpr.Arguments[0]);
            var y = TranslateExpr(callExpr.Arguments[1]);
            if (x is BitVecExpr bvX && y is BitVecExpr bvY)
                return _ctx.MkITE(_ctx.MkBVSGE(bvX, bvY), bvX, bvY);
        }

        // math.clamp / clamp: clamp(x, min, max) → max(min, min(x, max))
        if ((target == "math.clamp" || target == "clamp") && callExpr.Arguments.Count == 3)
        {
            var x = TranslateExpr(callExpr.Arguments[0]);
            var lo = TranslateExpr(callExpr.Arguments[1]);
            var hi = TranslateExpr(callExpr.Arguments[2]);
            if (x is BitVecExpr bvX && lo is BitVecExpr bvLo && hi is BitVecExpr bvHi)
            {
                // ite(x < lo, lo, ite(x > hi, hi, x))
                return _ctx.MkITE(
                    _ctx.MkBVSLT(bvX, bvLo), bvLo,
                    _ctx.MkITE(_ctx.MkBVSGT(bvX, bvHi), bvHi, bvX));
            }
        }

        // math.sign / sign: ite(x > 0, 1, ite(x < 0, -1, 0))
        if ((target == "math.sign" || target == "sign") && callExpr.Arguments.Count == 1)
        {
            var arg = TranslateExpr(callExpr.Arguments[0]);
            if (arg is BitVecExpr bv)
            {
                var zero = _ctx.MkBV(0, bv.SortSize);
                var one = _ctx.MkBV(1, bv.SortSize);
                var negOne = _ctx.MkBVNeg(one);
                return _ctx.MkITE(
                    _ctx.MkBVSGT(bv, zero), one,
                    _ctx.MkITE(_ctx.MkBVSLT(bv, zero), negOne, zero));
            }
        }

        // Unknown function — return null (inconclusive)
        return null;
    }

    private Expr? TranslateVariable(BoundVariableExpression varExpr)
    {
        if (_variables.TryGetValue(varExpr.Variable.Id, out var variable))
            return variable.Expr;

        // Try to declare the variable
        if (DeclareVariable(varExpr.Variable))
            return _variables[varExpr.Variable.Id].Expr;

        return null;
    }

    private static string GetSolverName(VariableSymbol variable)
    {
        var suffix = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(variable.Id.Value)))[..12];
        return $"{variable.Name}_{suffix}";
    }

    private Expr? TranslateBinaryOp(BoundBinaryExpression binExpr)
    {
        var left = TranslateExpr(binExpr.Left);
        var right = TranslateExpr(binExpr.Right);

        if (left == null || right == null)
            return null;

        return binExpr.Operator switch
        {
            BinaryOperator.Add when left is BitVecExpr la && right is BitVecExpr ra
                => _ctx.MkBVAdd(la, ra),
            BinaryOperator.Subtract when left is BitVecExpr ls && right is BitVecExpr rs
                => _ctx.MkBVSub(ls, rs),
            BinaryOperator.Multiply when left is BitVecExpr lm && right is BitVecExpr rm
                => _ctx.MkBVMul(lm, rm),
            BinaryOperator.Divide when left is BitVecExpr ld && right is BitVecExpr rd
                => _ctx.MkBVSDiv(ld, rd),
            // C#'s % is remainder (dividend's sign) = bvsrem, not bvsmod — same
            // divergence fixed in ContractTranslator (G1 re-verification M-new).
            BinaryOperator.Modulo when left is BitVecExpr lmod && right is BitVecExpr rmod
                => _ctx.MkBVSRem(lmod, rmod),
            BinaryOperator.Equal
                => _ctx.MkEq(left, right),
            BinaryOperator.NotEqual
                => _ctx.MkNot(_ctx.MkEq(left, right)),
            BinaryOperator.LessThan when left is BitVecExpr llt && right is BitVecExpr rlt
                => _ctx.MkBVSLT(llt, rlt),
            BinaryOperator.LessOrEqual when left is BitVecExpr lle && right is BitVecExpr rle
                => _ctx.MkBVSLE(lle, rle),
            BinaryOperator.GreaterThan when left is BitVecExpr lgt && right is BitVecExpr rgt
                => _ctx.MkBVSGT(lgt, rgt),
            BinaryOperator.GreaterOrEqual when left is BitVecExpr lge && right is BitVecExpr rge
                => _ctx.MkBVSGE(lge, rge),
            BinaryOperator.And when left is BoolExpr land && right is BoolExpr rand
                => _ctx.MkAnd(land, rand),
            BinaryOperator.Or when left is BoolExpr lor && right is BoolExpr ror
                => _ctx.MkOr(lor, ror),
            _ => null
        };
    }

    private Expr? TranslateUnaryOp(BoundUnaryExpression unaryExpr)
    {
        var operand = TranslateExpr(unaryExpr.Operand);
        if (operand == null)
            return null;

        return unaryExpr.Operator switch
        {
            Ast.UnaryOperator.Not when operand is BoolExpr boolOp => _ctx.MkNot(boolOp),
            Ast.UnaryOperator.Negate when operand is BitVecExpr bvOp => _ctx.MkBVNeg(bvOp),
            _ => null
        };
    }

    private Expr? CreateVariableForType(string name, string typeName)
    {
        var normalizedType = typeName.ToLowerInvariant();
        return normalizedType switch
        {
            "i8" or "sbyte" => _ctx.MkBVConst(name, 8),
            "i16" or "short" => _ctx.MkBVConst(name, 16),
            "i32" or "int" => _ctx.MkBVConst(name, 32),
            "i64" or "long" => _ctx.MkBVConst(name, 64),
            // Unsigned types are REFUSED, not half-modeled (guarantees plan D-G2.3;
            // modeled-forms §5): this translator applies signed operators throughout
            // (bvsdiv/bvsrem/bvslt...), so declaring an unsigned variable produced
            // checker verdicts under the wrong semantics (e.g. "byte can be
            // negative"). Refusal routes to the checker's honest no-verdict path.
            "u8" or "byte" or "u16" or "ushort" or "u32" or "uint" or "u64" or "ulong" => null,
            "bool" => _ctx.MkBoolConst(name),
            _ => null
        };
    }
}
