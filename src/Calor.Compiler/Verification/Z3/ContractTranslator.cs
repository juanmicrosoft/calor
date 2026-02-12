using Calor.Compiler.Ast;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Translates Calor AST expressions to Z3 expressions using bit-vector arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// This translator uses Z3 bit-vectors instead of unbounded integers to correctly model
/// fixed-width arithmetic with wrap-around overflow semantics (two's complement).
/// </para>
/// <para>
/// <b>Supported types:</b> i8, i16, i32, i64, u8, u16, u32, u64, bool
/// </para>
/// <para>
/// <b>Limitation - Narrow type promotion:</b> Unlike C#, which promotes byte/sbyte/short/ushort
/// to int before arithmetic operations, this translator preserves the original bit-width.
/// This means overflow behavior for narrow types may differ from C# runtime behavior.
/// For example: <c>byte a = 200; byte b = 200; int c = a + b;</c> yields 400 in C# (promoted to int),
/// but would wrap to 144 in this translator (8-bit addition).
/// </para>
/// <para>
/// <b>Limitation - Integer literals:</b> All integer literals are treated as signed 32-bit values.
/// Literals outside the 32-bit range may be truncated.
/// </para>
/// </remarks>
public sealed class ContractTranslator
{
    private readonly Context _ctx;
    private readonly Dictionary<string, (Expr Expr, string Type)> _variables = new();
    private readonly Stack<Dictionary<string, (Expr Expr, string Type)>> _scopeStack = new();

    /// <summary>
    /// Tracks metadata for bit-vector expressions (width and signedness).
    /// </summary>
    private readonly Dictionary<Expr, BitVecInfo> _exprInfo = new();

    private record struct BitVecInfo(uint Width, bool IsSigned);

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
    [Obsolete("Use TranslateBitVecExpr instead. ArithExpr uses unbounded integers which don't model overflow correctly.")]
    public ArithExpr? TranslateArithExpr(ExpressionNode node)
    {
        var expr = Translate(node);
        return expr as ArithExpr;
    }

    /// <summary>
    /// Translates a Calor expression to a Z3 bit-vector expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public BitVecExpr? TranslateBitVecExpr(ExpressionNode node)
    {
        var expr = Translate(node);
        return expr as BitVecExpr;
    }

    /// <summary>
    /// Translates a Calor expression to a Z3 expression.
    /// Returns null if the expression contains unsupported constructs.
    /// </summary>
    public Expr? Translate(ExpressionNode node)
    {
        return node switch
        {
            IntLiteralNode intLit => TrackBitVec(_ctx.MkBV(intLit.Value, 32), 32, isSigned: true),
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
            // Arithmetic operations (require BitVecExpr for fixed-width semantics)
            // Add, Sub, Mul are the same for signed/unsigned (two's complement)
            BinaryOperator.Add when left is BitVecExpr la && right is BitVecExpr ra
                => ApplyBitVecBinaryOp(la, ra, _ctx.MkBVAdd),
            BinaryOperator.Subtract when left is BitVecExpr ls && right is BitVecExpr rs
                => ApplyBitVecBinaryOp(ls, rs, _ctx.MkBVSub),
            BinaryOperator.Multiply when left is BitVecExpr lm && right is BitVecExpr rm
                => ApplyBitVecBinaryOp(lm, rm, _ctx.MkBVMul),

            // Division and modulo need signed/unsigned variants
            BinaryOperator.Divide when left is BitVecExpr ld && right is BitVecExpr rd
                => ApplyDivModOp(ld, rd, _ctx.MkBVSDiv, _ctx.MkBVUDiv),
            BinaryOperator.Modulo when left is BitVecExpr lmod && right is BitVecExpr rmod
                => ApplyDivModOp(lmod, rmod, _ctx.MkBVSMod, _ctx.MkBVURem),

            // Comparison operations (return BoolExpr) - need signed/unsigned variants
            BinaryOperator.Equal => MkEqNormalized(left, right),
            BinaryOperator.NotEqual => _ctx.MkNot(MkEqNormalized(left, right)),
            BinaryOperator.LessThan when left is BitVecExpr llt && right is BitVecExpr rlt
                => ApplySignedComparison(llt, rlt, _ctx.MkBVSLT, _ctx.MkBVULT),
            BinaryOperator.LessOrEqual when left is BitVecExpr lle && right is BitVecExpr rle
                => ApplySignedComparison(lle, rle, _ctx.MkBVSLE, _ctx.MkBVULE),
            BinaryOperator.GreaterThan when left is BitVecExpr lgt && right is BitVecExpr rgt
                => ApplySignedComparison(lgt, rgt, _ctx.MkBVSGT, _ctx.MkBVUGT),
            BinaryOperator.GreaterOrEqual when left is BitVecExpr lge && right is BitVecExpr rge
                => ApplySignedComparison(lge, rge, _ctx.MkBVSGE, _ctx.MkBVUGE),

            // Logical operations (require BoolExpr)
            BinaryOperator.And when left is BoolExpr land && right is BoolExpr rand
                => _ctx.MkAnd(land, rand),
            BinaryOperator.Or when left is BoolExpr lor && right is BoolExpr ror
                => _ctx.MkOr(lor, ror),

            // Bitwise operations (require BitVecExpr)
            BinaryOperator.BitwiseAnd when left is BitVecExpr bl && right is BitVecExpr br
                => ApplyBitVecBinaryOp(bl, br, _ctx.MkBVAND),
            BinaryOperator.BitwiseOr when left is BitVecExpr bol && right is BitVecExpr bor
                => ApplyBitVecBinaryOp(bol, bor, _ctx.MkBVOR),
            BinaryOperator.BitwiseXor when left is BitVecExpr bxl && right is BitVecExpr bxr
                => ApplyBitVecBinaryOp(bxl, bxr, _ctx.MkBVXOR),
            BinaryOperator.LeftShift when left is BitVecExpr shl && right is BitVecExpr shr
                => ApplyBitVecBinaryOp(shl, shr, _ctx.MkBVSHL),
            // Right shift: use arithmetic (signed) or logical (unsigned) shift
            BinaryOperator.RightShift when left is BitVecExpr ashl && right is BitVecExpr ashr
                => IsSigned(left)
                    ? ApplyBitVecBinaryOp(ashl, ashr, _ctx.MkBVASHR)
                    : ApplyBitVecBinaryOp(ashl, ashr, _ctx.MkBVLSHR),

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
            UnaryOperator.Negate when operand is BitVecExpr bvOp => _ctx.MkBVNeg(bvOp),
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
    /// For Z3, we model arrays as uninterpreted functions with 64-bit indices.
    /// </summary>
    private Expr? TranslateArrayAccess(ArrayAccessNode node)
    {
        // For array access like arr{i}, we need to model it as an array select
        // First, get or create an array variable for the base array
        if (node.Array is ReferenceNode arrayRef)
        {
            var index = Translate(node.Index);
            if (index == null || index is not BitVecExpr indexBv)
                return null;

            // Check if we already have an array variable
            var arrayName = arrayRef.Name;
            if (!_variables.TryGetValue(arrayName, out var arrayVar))
            {
                // Create an array sort: BitVec64 -> BitVec32 (64-bit index, 32-bit elements)
                // Using 64-bit indices to support both i32 and i64 index types without truncation
                var bv64Sort = _ctx.MkBitVecSort(64);
                var bv32Sort = _ctx.MkBitVecSort(32);
                var arrayExpr = _ctx.MkArrayConst(arrayName, bv64Sort, bv32Sort);
                _variables[arrayName] = (arrayExpr, "array");
                arrayVar = (arrayExpr, "array");
            }

            if (arrayVar.Expr is ArrayExpr arrExpr)
            {
                // Extend index to 64-bit for array access (sign or zero extend based on signedness)
                BitVecExpr normalizedIndex;
                if (indexBv.SortSize == 64)
                {
                    normalizedIndex = indexBv;
                }
                else if (IsSigned(indexBv))
                {
                    normalizedIndex = _ctx.MkSignExt(64 - indexBv.SortSize, indexBv);
                }
                else
                {
                    normalizedIndex = _ctx.MkZeroExt(64 - indexBv.SortSize, indexBv);
                }
                return _ctx.MkSelect(arrExpr, normalizedIndex);
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
            // Signed integer types
            "i8" or "sbyte" => TrackBitVec(_ctx.MkBVConst(name, 8), 8, isSigned: true),
            "i16" or "short" => TrackBitVec(_ctx.MkBVConst(name, 16), 16, isSigned: true),
            "i32" or "int" => TrackBitVec(_ctx.MkBVConst(name, 32), 32, isSigned: true),
            "i64" or "long" => TrackBitVec(_ctx.MkBVConst(name, 64), 64, isSigned: true),

            // Unsigned integer types
            "u8" or "byte" => TrackBitVec(_ctx.MkBVConst(name, 8), 8, isSigned: false),
            "u16" or "ushort" => TrackBitVec(_ctx.MkBVConst(name, 16), 16, isSigned: false),
            "u32" or "uint" => TrackBitVec(_ctx.MkBVConst(name, 32), 32, isSigned: false),
            "u64" or "ulong" => TrackBitVec(_ctx.MkBVConst(name, 64), 64, isSigned: false),

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
            // Signed types
            "int8" or "system.sbyte" => "i8",
            "int16" or "system.int16" => "i16",
            "int32" or "system.int32" => "i32",
            "int64" or "system.int64" => "i64",

            // Unsigned types
            "uint8" or "system.byte" => "u8",
            "uint16" or "system.uint16" => "u16",
            "uint32" or "system.uint32" => "u32",
            "uint64" or "system.uint64" => "u64",

            "boolean" or "system.boolean" => "bool",
            "single" or "system.single" => "f32",
            "double" or "system.double" => "f64",
            var t => t
        };
    }

    /// <summary>
    /// Tracks the bit-width and signedness of a bit-vector expression.
    /// </summary>
    private BitVecExpr TrackBitVec(BitVecExpr expr, uint width, bool isSigned)
    {
        _exprInfo[expr] = new BitVecInfo(width, isSigned);
        return expr;
    }

    /// <summary>
    /// Gets the info for a bit-vector expression.
    /// Defaults to signed 32-bit if not tracked (e.g., for integer literals).
    /// </summary>
    private BitVecInfo GetBitVecInfo(Expr expr) => expr switch
    {
        BitVecExpr bv when _exprInfo.TryGetValue(bv, out var info) => info,
        BitVecExpr bv => new BitVecInfo(bv.SortSize, IsSigned: true), // Default to signed
        _ => new BitVecInfo(32u, IsSigned: true)
    };

    /// <summary>
    /// Determines if an expression is signed.
    /// </summary>
    private bool IsSigned(Expr expr) => GetBitVecInfo(expr).IsSigned;

    /// <summary>
    /// Determines if unsigned comparison should be used.
    /// For mixed signed/unsigned, use unsigned if one operand is unsigned and the other
    /// is a non-negative literal (matches C# implicit conversion behavior).
    /// </summary>
    private bool ShouldUseUnsignedComparison(Expr left, Expr right)
    {
        var leftSigned = IsSigned(left);
        var rightSigned = IsSigned(right);

        // Both unsigned: use unsigned comparison
        if (!leftSigned && !rightSigned)
            return true;

        // Both signed: use signed comparison
        if (leftSigned && rightSigned)
            return false;

        // Mixed: use unsigned if the signed operand is a non-negative literal
        // This matches C# semantics where non-negative int literals can compare with uint
        if (!leftSigned && rightSigned && IsNonNegativeLiteral(right))
            return true;
        if (leftSigned && !rightSigned && IsNonNegativeLiteral(left))
            return true;

        // Default to signed for safety
        return false;
    }

    /// <summary>
    /// Checks if an expression is a non-negative literal value.
    /// </summary>
    private bool IsNonNegativeLiteral(Expr expr)
    {
        if (expr is BitVecNum num)
        {
            // For signed interpretation, check if the high bit is 0
            // A non-negative signed value has its MSB = 0
            var width = num.SortSize;
            var value = num.BigInteger;
            var maxPositive = System.Numerics.BigInteger.Pow(2, (int)width - 1) - 1;
            return value >= 0 && value <= maxPositive;
        }
        return false;
    }

    /// <summary>
    /// Normalizes two bit-vector expressions to the same width.
    /// Uses sign extension for signed types, zero extension for unsigned.
    /// </summary>
    private (BitVecExpr Left, BitVecExpr Right) NormalizeBitVecWidths(BitVecExpr left, BitVecExpr right)
    {
        var leftWidth = left.SortSize;
        var rightWidth = right.SortSize;

        if (leftWidth == rightWidth)
            return (left, right);

        var leftSigned = IsSigned(left);
        var rightSigned = IsSigned(right);

        if (leftWidth < rightWidth)
        {
            var extended = leftSigned
                ? _ctx.MkSignExt(rightWidth - leftWidth, left)
                : _ctx.MkZeroExt(rightWidth - leftWidth, left);
            return (extended, right);
        }
        else
        {
            var extended = rightSigned
                ? _ctx.MkSignExt(leftWidth - rightWidth, right)
                : _ctx.MkZeroExt(leftWidth - rightWidth, right);
            return (left, extended);
        }
    }

    /// <summary>
    /// Applies a binary bit-vector operation with width normalization.
    /// </summary>
    private BitVecExpr ApplyBitVecBinaryOp(BitVecExpr left, BitVecExpr right, Func<BitVecExpr, BitVecExpr, BitVecExpr> op)
    {
        var (normalizedLeft, normalizedRight) = NormalizeBitVecWidths(left, right);
        var result = op(normalizedLeft, normalizedRight);
        // Result inherits signedness: unsigned only if both operands are unsigned
        var resultSigned = IsSigned(left) || IsSigned(right);
        return TrackBitVec(result, normalizedLeft.SortSize, resultSigned);
    }

    /// <summary>
    /// Applies a signed or unsigned comparison operation with width normalization.
    /// </summary>
    private BoolExpr ApplySignedComparison(BitVecExpr left, BitVecExpr right,
        Func<BitVecExpr, BitVecExpr, BoolExpr> signedOp,
        Func<BitVecExpr, BitVecExpr, BoolExpr> unsignedOp)
    {
        var (normalizedLeft, normalizedRight) = NormalizeBitVecWidths(left, right);
        var op = ShouldUseUnsignedComparison(left, right) ? unsignedOp : signedOp;
        return op(normalizedLeft, normalizedRight);
    }

    /// <summary>
    /// Applies a division or modulo operation, choosing signed or unsigned variant.
    /// </summary>
    private BitVecExpr ApplyDivModOp(BitVecExpr left, BitVecExpr right,
        Func<BitVecExpr, BitVecExpr, BitVecExpr> signedOp,
        Func<BitVecExpr, BitVecExpr, BitVecExpr> unsignedOp)
    {
        var (normalizedLeft, normalizedRight) = NormalizeBitVecWidths(left, right);
        var useUnsigned = ShouldUseUnsignedComparison(left, right);
        var op = useUnsigned ? unsignedOp : signedOp;
        var result = op(normalizedLeft, normalizedRight);
        return TrackBitVec(result, normalizedLeft.SortSize, !useUnsigned);
    }

    /// <summary>
    /// Creates an equality expression, normalizing bit-vector widths if needed.
    /// </summary>
    private BoolExpr MkEqNormalized(Expr left, Expr right)
    {
        if (left is BitVecExpr bvLeft && right is BitVecExpr bvRight)
        {
            var (normalizedLeft, normalizedRight) = NormalizeBitVecWidths(bvLeft, bvRight);
            return _ctx.MkEq(normalizedLeft, normalizedRight);
        }
        return _ctx.MkEq(left, right);
    }
}
