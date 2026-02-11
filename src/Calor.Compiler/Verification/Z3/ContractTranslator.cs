using Calor.Compiler.Ast;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Translates Calor AST expressions to Z3 expressions.
/// </summary>
public sealed class ContractTranslator
{
    private readonly Context _ctx;
    private readonly Dictionary<string, (Expr Expr, string Type)> _variables = new();
    private readonly Stack<Dictionary<string, (Expr Expr, string Type)>> _scopeStack = new();

    public ContractTranslator(Context ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    /// <summary>
    /// Declares a variable with the given name and type.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="typeName">Calor type name (i32, bool, etc.).</param>
    /// <returns>True if the type is supported and variable was declared.</returns>
    public bool DeclareVariable(string name, string typeName)
    {
        var expr = CreateVariableForType(name, typeName);
        if (expr == null)
            return false;

        _variables[name] = (expr, typeName);
        return true;
    }

    /// <summary>
    /// Gets all declared variables.
    /// </summary>
    public IReadOnlyDictionary<string, (Expr Expr, string Type)> Variables => _variables;

    /// <summary>
    /// Translates a Calor expression to a Z3 boolean expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public BoolExpr? TranslateBoolExpr(ExpressionNode node)
    {
        var expr = Translate(node);
        return expr as BoolExpr;
    }

    /// <summary>
    /// Translates a Calor expression to a Z3 arithmetic expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public ArithExpr? TranslateArithExpr(ExpressionNode node)
    {
        var expr = Translate(node);
        return expr as ArithExpr;
    }

    /// <summary>
    /// Translates a Calor expression to a Z3 expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public Expr? Translate(ExpressionNode node)
    {
        return node switch
        {
            IntLiteralNode intLit => _ctx.MkInt(intLit.Value),
            BoolLiteralNode boolLit => _ctx.MkBool(boolLit.Value),
            ReferenceNode refNode => TranslateReference(refNode),
            BinaryOperationNode binOp => TranslateBinaryOp(binOp),
            UnaryOperationNode unaryOp => TranslateUnaryOp(unaryOp),
            ConditionalExpressionNode condExpr => TranslateConditional(condExpr),
            ForallExpressionNode forall => TranslateForall(forall),
            ExistsExpressionNode exists => TranslateExists(exists),
            ImplicationExpressionNode impl => TranslateImplication(impl),
            ArrayAccessNode arrayAccess => TranslateArrayAccess(arrayAccess),

            // Unsupported constructs - return null
            StringLiteralNode => null,
            FloatLiteralNode => null,
            CallExpressionNode => null,
            _ => null
        };
    }

    private Expr? TranslateReference(ReferenceNode node)
    {
        if (_variables.TryGetValue(node.Name, out var variable))
            return variable.Expr;

        // Unknown variable - might be a reference to something we don't know about
        return null;
    }

    private Expr? TranslateBinaryOp(BinaryOperationNode node)
    {
        var left = Translate(node.Left);
        var right = Translate(node.Right);

        if (left == null || right == null)
            return null;

        return node.Operator switch
        {
            // Arithmetic operations (require ArithExpr)
            BinaryOperator.Add when left is ArithExpr la && right is ArithExpr ra
                => _ctx.MkAdd(la, ra),
            BinaryOperator.Subtract when left is ArithExpr ls && right is ArithExpr rs
                => _ctx.MkSub(ls, rs),
            BinaryOperator.Multiply when left is ArithExpr lm && right is ArithExpr rm
                => _ctx.MkMul(lm, rm),
            BinaryOperator.Divide when left is ArithExpr ld && right is ArithExpr rd
                => _ctx.MkDiv(ld, rd),
            BinaryOperator.Modulo when left is IntExpr lmod && right is IntExpr rmod
                => _ctx.MkMod(lmod, rmod),

            // Comparison operations (return BoolExpr)
            BinaryOperator.Equal => _ctx.MkEq(left, right),
            BinaryOperator.NotEqual => _ctx.MkNot(_ctx.MkEq(left, right)),
            BinaryOperator.LessThan when left is ArithExpr llt && right is ArithExpr rlt
                => _ctx.MkLt(llt, rlt),
            BinaryOperator.LessOrEqual when left is ArithExpr lle && right is ArithExpr rle
                => _ctx.MkLe(lle, rle),
            BinaryOperator.GreaterThan when left is ArithExpr lgt && right is ArithExpr rgt
                => _ctx.MkGt(lgt, rgt),
            BinaryOperator.GreaterOrEqual when left is ArithExpr lge && right is ArithExpr rge
                => _ctx.MkGe(lge, rge),

            // Logical operations (require BoolExpr)
            BinaryOperator.And when left is BoolExpr land && right is BoolExpr rand
                => _ctx.MkAnd(land, rand),
            BinaryOperator.Or when left is BoolExpr lor && right is BoolExpr ror
                => _ctx.MkOr(lor, ror),

            // Unsupported: Power, Bitwise operations
            _ => null
        };
    }

    private Expr? TranslateUnaryOp(UnaryOperationNode node)
    {
        var operand = Translate(node.Operand);
        if (operand == null)
            return null;

        return node.Operator switch
        {
            UnaryOperator.Not when operand is BoolExpr boolOp => _ctx.MkNot(boolOp),
            UnaryOperator.Negate when operand is ArithExpr arithOp => _ctx.MkUnaryMinus(arithOp),
            _ => null
        };
    }

    private Expr? TranslateConditional(ConditionalExpressionNode node)
    {
        var condition = Translate(node.Condition) as BoolExpr;
        var whenTrue = Translate(node.WhenTrue);
        var whenFalse = Translate(node.WhenFalse);

        if (condition == null || whenTrue == null || whenFalse == null)
            return null;

        return _ctx.MkITE(condition, whenTrue, whenFalse);
    }

    /// <summary>
    /// Pushes the current variable scope onto the stack.
    /// </summary>
    private void PushScope()
    {
        _scopeStack.Push(new Dictionary<string, (Expr, string)>(_variables));
    }

    /// <summary>
    /// Pops and restores the previous variable scope.
    /// </summary>
    private void PopScope()
    {
        var prev = _scopeStack.Pop();
        _variables.Clear();
        foreach (var kvp in prev)
            _variables[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Translates a universal quantifier (forall) expression.
    /// </summary>
    private BoolExpr? TranslateForall(ForallExpressionNode node)
    {
        PushScope();
        try
        {
            var boundVars = new List<Expr>();
            foreach (var bv in node.BoundVariables)
            {
                var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
                if (z3Var == null)
                    return null;
                _variables[bv.Name] = (z3Var, bv.TypeName);
                boundVars.Add(z3Var);
            }

            var body = TranslateBoolExpr(node.Body);
            if (body == null)
                return null;

            return _ctx.MkForall(boundVars.ToArray(), body);
        }
        finally
        {
            PopScope();
        }
    }

    /// <summary>
    /// Translates an existential quantifier (exists) expression.
    /// </summary>
    private BoolExpr? TranslateExists(ExistsExpressionNode node)
    {
        PushScope();
        try
        {
            var boundVars = new List<Expr>();
            foreach (var bv in node.BoundVariables)
            {
                var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
                if (z3Var == null)
                    return null;
                _variables[bv.Name] = (z3Var, bv.TypeName);
                boundVars.Add(z3Var);
            }

            var body = TranslateBoolExpr(node.Body);
            if (body == null)
                return null;

            return _ctx.MkExists(boundVars.ToArray(), body);
        }
        finally
        {
            PopScope();
        }
    }

    /// <summary>
    /// Translates a logical implication expression.
    /// p -> q is equivalent to !p || q
    /// </summary>
    private BoolExpr? TranslateImplication(ImplicationExpressionNode node)
    {
        var ante = TranslateBoolExpr(node.Antecedent);
        var cons = TranslateBoolExpr(node.Consequent);

        if (ante == null || cons == null)
            return null;

        return _ctx.MkImplies(ante, cons);
    }

    /// <summary>
    /// Translates an array access expression.
    /// For Z3, we model arrays as uninterpreted functions.
    /// </summary>
    private Expr? TranslateArrayAccess(ArrayAccessNode node)
    {
        // For array access like arr{i}, we need to model it as an array select
        // First, get or create an array variable for the base array
        if (node.Array is ReferenceNode arrayRef)
        {
            var index = Translate(node.Index);
            if (index == null || index is not ArithExpr indexArith)
                return null;

            // Check if we already have an array variable
            var arrayName = arrayRef.Name;
            if (!_variables.TryGetValue(arrayName, out var arrayVar))
            {
                // Create an array sort: Int -> Int (simplified model)
                var intSort = _ctx.MkIntSort();
                var arraySort = _ctx.MkArraySort(intSort, intSort);
                var arrayExpr = _ctx.MkArrayConst(arrayName, intSort, intSort);
                _variables[arrayName] = (arrayExpr, "array");
                arrayVar = (arrayExpr, "array");
            }

            if (arrayVar.Expr is ArrayExpr arrExpr)
            {
                return _ctx.MkSelect(arrExpr, indexArith);
            }
        }

        return null;
    }

    private Expr? CreateVariableForType(string name, string typeName)
    {
        // Normalize type names
        var normalizedType = NormalizeTypeName(typeName);

        return normalizedType switch
        {
            "i32" or "int" or "i64" or "long" => _ctx.MkIntConst(name),
            "bool" => _ctx.MkBoolConst(name),
            // Unsupported types
            "string" or "str" => null,
            "f32" or "f64" or "float" or "double" => null,
            _ => null
        };
    }

    private static string NormalizeTypeName(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "int32" or "system.int32" => "i32",
            "int64" or "system.int64" => "i64",
            "boolean" or "system.boolean" => "bool",
            "single" or "system.single" => "f32",
            "double" or "system.double" => "f64",
            var t => t
        };
    }
}
