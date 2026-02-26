using Calor.Compiler.Ast;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Translates Calor AST expressions to Z3 expressions using bit-vector arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread Safety:</b> This class is NOT thread-safe. Each instance maintains internal state
/// (declared variables, expression metadata, warnings) that is modified during translation.
/// Create a new instance for each verification operation, or synchronize access externally.
/// The Z3 <see cref="Microsoft.Z3.Context"/> passed to the constructor should also be used
/// from a single thread unless Z3 was configured for multi-threaded use.
/// </para>
/// <para>
/// This translator uses Z3 bit-vectors instead of unbounded integers to correctly model
/// fixed-width arithmetic with wrap-around overflow semantics (two's complement).
/// </para>
/// <para>
/// <b>Supported types:</b> i8, i16, i32, i64, u8, u16, u32, u64, bool, string, arrays
/// </para>
/// <para>
/// <b>String support:</b> Uses Z3's native string theory for verification. Supported operations:
/// Length, Contains, StartsWith, EndsWith, Equals, IsNullOrEmpty, IndexOf, Substring, Concat, Replace.
/// </para>
/// <para>
/// <b>Array support:</b> Arrays are modeled with 64-bit indices and typed elements. Each array
/// has an associated <c>$length</c> variable (e.g., <c>arr$length</c>) representing its length
/// as an unsigned 32-bit value.
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
/// <para>
/// <b>Limitation - Null strings:</b> Z3 strings cannot be null - they are always valid sequences.
/// The <c>IsNullOrEmpty</c> operation only checks if the string length equals zero. Code that
/// relies on null string semantics may not verify correctly.
/// </para>
/// <para>
/// <b>Limitation - String comparison modes:</b> The <see cref="StringComparisonMode"/> parameter
/// on string operations is ignored. Z3's string theory uses ordinal comparison only; case-insensitive
/// and culture-aware comparisons are not supported.
/// </para>
/// <para>
/// <b>Limitation - Unsupported string operations:</b> ToUpper, ToLower, Trim, TrimStart, TrimEnd,
/// PadLeft, PadRight, Split, Join, Format, ToString, and all Regex operations return null
/// (marked as Unsupported) because Z3 lacks native support for these operations.
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

    /// <summary>
    /// Tracks metadata for string expressions (nullable flag for future null handling).
    /// </summary>
    private record struct StringInfo(bool IsNullable);
    private readonly Dictionary<Expr, StringInfo> _stringInfo = new();

    /// <summary>
    /// Tracks metadata for array expressions (element type and length expression).
    /// </summary>
    private record struct ArrayInfo(string ElementType, Expr? LengthExpr);
    private readonly Dictionary<string, ArrayInfo> _arrayInfo = new();

    /// <summary>
    /// Collects warnings about features that were silently ignored during translation.
    /// These don't cause translation failure but may result in unexpected verification behavior.
    /// </summary>
    private readonly List<string> _warnings = new();

    /// <summary>
    /// Stack of self-variable names for refinement predicate contexts.
    /// When translating a refinement predicate, # resolves to the variable at the top of this stack.
    /// </summary>
    private readonly Stack<string> _selfVariableStack = new();

    /// <summary>
    /// Gets warnings that were generated during translation.
    /// Warnings indicate features that were silently handled in a potentially unexpected way.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Clears accumulated warnings.
    /// </summary>
    public void ClearWarnings() => _warnings.Clear();

    public ContractTranslator(Context ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    /// <summary>
    /// Pushes a self-variable name for refinement predicate translation.
    /// When SelfRefNode (#) is encountered, it resolves to the variable with this name.
    /// </summary>
    public void PushSelfVariable(string variableName)
    {
        _selfVariableStack.Push(variableName);
    }

    /// <summary>
    /// Pops the current self-variable name.
    /// </summary>
    public void PopSelfVariable()
    {
        if (_selfVariableStack.Count > 0)
            _selfVariableStack.Pop();
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
            ArrayLengthNode arrayLen => TranslateArrayLength(arrayLen),

            // String support using Z3's native string theory
            StringLiteralNode strLit => TrackString(_ctx.MkString(strLit.Value)),
            StringOperationNode strOp => TranslateStringOperation(strOp),

            // Dependent Types: Self-reference in refinement predicates
            SelfRefNode => TranslateSelfRef(),

            // Unsupported constructs - return null
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

    /// <summary>
    /// Translates # (self-reference) by looking up the current self-variable from the stack.
    /// </summary>
    private Expr? TranslateSelfRef()
    {
        if (_selfVariableStack.Count == 0)
            return null;

        var selfName = _selfVariableStack.Peek();
        if (_variables.TryGetValue(selfName, out var variable))
            return variable.Expr;

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
                // Array not declared - create on-demand with default i32 element type
                // This also creates the associated $length variable for consistency
                var arrayExpr = CreateArrayVariable(arrayName, "i32");
                if (arrayExpr == null)
                    return null;
                _variables[arrayName] = (arrayExpr, "array<i32>");
                arrayVar = (arrayExpr, "array<i32>");
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

        // Check for array types (e.g., "i32[]", "int[]", "u8[]")
        if (normalizedType.EndsWith("[]"))
        {
            var elementType = normalizedType[..^2]; // Remove "[]" suffix
            return CreateArrayVariable(name, elementType);
        }

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
            // String type - uses Z3's native string theory
            "string" or "str" => TrackString((SeqExpr)_ctx.MkConst(name, _ctx.StringSort)),
            // Unsupported types
            "f32" or "f64" or "float" or "double" => null,
            _ => null
        };
    }

    /// <summary>
    /// Creates an array variable expression. Called internally from CreateVariableForType.
    /// </summary>
    private Expr? CreateArrayVariable(string name, string elementType)
    {
        var (elementWidth, _) = GetTypeWidthAndSignedness(elementType);
        if (elementWidth == 0)
            return null;

        // Create array sort: BitVec64 (index) -> BitVec[elementWidth] (element)
        var bv64Sort = _ctx.MkBitVecSort(64);
        var elementSort = _ctx.MkBitVecSort(elementWidth);
        var arrayExpr = _ctx.MkArrayConst(name, bv64Sort, elementSort);

        // Create associated length variable (unsigned 32-bit)
        var lengthVarName = $"{name}$length";
        var lengthExpr = TrackBitVec(_ctx.MkBVConst(lengthVarName, 32), 32, isSigned: false);
        _variables[lengthVarName] = (lengthExpr, "u32");

        _arrayInfo[name] = new ArrayInfo(elementType, lengthExpr);
        return arrayExpr;
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
    /// Tracks a string expression with metadata.
    /// </summary>
    private SeqExpr TrackString(SeqExpr expr, bool isNullable = false)
    {
        _stringInfo[expr] = new StringInfo(isNullable);
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

    // ===========================================
    // String Theory Support
    // ===========================================

    /// <summary>
    /// Translates a string operation to a Z3 expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supported operations: Length, Contains, StartsWith, EndsWith, Equals, IsNullOrEmpty,
    /// IndexOf, Substring, SubstringFrom, Concat, Replace.
    /// </para>
    /// <para>
    /// <b>Note:</b> The <see cref="StringOperationNode.ComparisonMode"/> property is ignored.
    /// Z3's string theory only supports ordinal comparison; case-insensitive comparisons
    /// (e.g., <c>:ignore-case</c>) cannot be modeled and will verify as if ordinal comparison
    /// was specified.
    /// </para>
    /// </remarks>
    private Expr? TranslateStringOperation(StringOperationNode node)
    {
        // Emit warning if comparison mode is specified but will be ignored
        if (node.ComparisonMode.HasValue && node.ComparisonMode.Value != StringComparisonMode.Ordinal)
        {
            _warnings.Add(
                $"String operation '{node.Operation}' specifies comparison mode '{node.ComparisonMode.Value}' " +
                "which is ignored during verification. Z3 string theory only supports ordinal comparison; " +
                "case-insensitive or culture-aware comparisons cannot be modeled. " +
                "Verification will use ordinal comparison semantics.");
        }

        return node.Operation switch
        {
            StringOp.Length => TranslateStringLength(node),
            StringOp.Contains => TranslateStringContains(node),
            StringOp.StartsWith => TranslateStringStartsWith(node),
            StringOp.EndsWith => TranslateStringEndsWith(node),
            StringOp.Equals => TranslateStringEquals(node),
            StringOp.IsNullOrEmpty => TranslateStringIsNullOrEmpty(node),
            StringOp.IndexOf => TranslateStringIndexOf(node),
            StringOp.Substring => TranslateStringSubstring(node),
            StringOp.SubstringFrom => TranslateStringSubstringFrom(node),
            StringOp.Concat => TranslateStringConcat(node),
            StringOp.Replace => TranslateStringReplace(node),
            // Unsupported operations - ToUpper, ToLower, Trim, Regex, etc.
            _ => null
        };
    }

    /// <summary>
    /// Translates string length operation: (len s) -> BitVecExpr (32-bit unsigned)
    /// </summary>
    private Expr? TranslateStringLength(StringOperationNode node)
    {
        if (node.Arguments.Count < 1)
            return null;

        var str = Translate(node.Arguments[0]);
        if (str is not SeqExpr seqExpr)
            return null;

        // MkLength returns IntExpr, convert to 32-bit unsigned bit-vector
        var lengthInt = _ctx.MkLength(seqExpr);
        return TrackBitVec(_ctx.MkInt2BV(32, lengthInt), 32, isSigned: false);
    }

    /// <summary>
    /// Translates string contains operation: (contains s "hello") -> BoolExpr
    /// </summary>
    private Expr? TranslateStringContains(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var substr = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || substr is not SeqExpr substrExpr)
            return null;

        return _ctx.MkContains(strExpr, substrExpr);
    }

    /// <summary>
    /// Translates string starts-with operation: (starts s "prefix") -> BoolExpr
    /// Note: Z3's MkPrefixOf takes (prefix, str) - prefix first!
    /// </summary>
    private Expr? TranslateStringStartsWith(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var prefix = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || prefix is not SeqExpr prefixExpr)
            return null;

        // MkPrefixOf takes prefix first, then string
        return _ctx.MkPrefixOf(prefixExpr, strExpr);
    }

    /// <summary>
    /// Translates string ends-with operation: (ends s "suffix") -> BoolExpr
    /// Note: Z3's MkSuffixOf takes (suffix, str) - suffix first!
    /// </summary>
    private Expr? TranslateStringEndsWith(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var suffix = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || suffix is not SeqExpr suffixExpr)
            return null;

        // MkSuffixOf takes suffix first, then string
        return _ctx.MkSuffixOf(suffixExpr, strExpr);
    }

    /// <summary>
    /// Translates string equals operation: (equals s1 s2) -> BoolExpr
    /// </summary>
    private Expr? TranslateStringEquals(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str1 = Translate(node.Arguments[0]);
        var str2 = Translate(node.Arguments[1]);

        if (str1 is not SeqExpr str1Expr || str2 is not SeqExpr str2Expr)
            return null;

        return _ctx.MkEq(str1Expr, str2Expr);
    }

    /// <summary>
    /// Translates string is-null-or-empty operation: (isempty s) -> BoolExpr.
    /// </summary>
    /// <remarks>
    /// <b>Important:</b> Z3 strings cannot be null - they are always valid sequences.
    /// This method only checks if the string length equals zero. Code that passes null
    /// strings to <c>string.IsNullOrEmpty()</c> in C# will behave differently than this
    /// Z3 translation, which cannot distinguish null from empty.
    /// </remarks>
    private Expr? TranslateStringIsNullOrEmpty(StringOperationNode node)
    {
        if (node.Arguments.Count < 1)
            return null;

        var str = Translate(node.Arguments[0]);
        if (str is not SeqExpr seqExpr)
            return null;

        // Check if length equals 0
        var length = _ctx.MkLength(seqExpr);
        return _ctx.MkEq(length, _ctx.MkInt(0));
    }

    /// <summary>
    /// Translates string index-of operation: (indexof s "search") or (indexof s "search" start) -> BitVecExpr (32-bit signed).
    /// Returns -1 if not found.
    /// </summary>
    /// <remarks>
    /// Supports both 2-argument form (searches from index 0) and 3-argument form (searches from specified start index).
    /// </remarks>
    private Expr? TranslateStringIndexOf(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var search = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr || search is not SeqExpr searchExpr)
            return null;

        // Determine start index: use 3rd argument if provided, otherwise 0
        IntExpr startIndex;
        if (node.Arguments.Count >= 3)
        {
            var startArg = Translate(node.Arguments[2]);
            var startInt = ConvertToIntExpr(startArg);
            if (startInt == null)
                return null;
            startIndex = startInt;
        }
        else
        {
            startIndex = _ctx.MkInt(0);
        }

        // MkIndexOf takes (str, search, startIndex)
        var indexInt = _ctx.MkIndexOf(strExpr, searchExpr, startIndex);
        return TrackBitVec(_ctx.MkInt2BV(32, indexInt), 32, isSigned: true);
    }

    /// <summary>
    /// Translates string substring operation: (substr s start len) -> SeqExpr
    /// </summary>
    private Expr? TranslateStringSubstring(StringOperationNode node)
    {
        if (node.Arguments.Count < 3)
            return null;

        var str = Translate(node.Arguments[0]);
        var start = Translate(node.Arguments[1]);
        var len = Translate(node.Arguments[2]);

        if (str is not SeqExpr strExpr)
            return null;

        // Convert BitVec indices to Int
        var startInt = ConvertToIntExpr(start);
        var lenInt = ConvertToIntExpr(len);

        if (startInt == null || lenInt == null)
            return null;

        return TrackString(_ctx.MkExtract(strExpr, startInt, lenInt));
    }

    /// <summary>
    /// Translates string substring-from operation: (substr s start) -> SeqExpr
    /// Gets substring from start to end of string
    /// </summary>
    private Expr? TranslateStringSubstringFrom(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var str = Translate(node.Arguments[0]);
        var start = Translate(node.Arguments[1]);

        if (str is not SeqExpr strExpr)
            return null;

        var startInt = ConvertToIntExpr(start);
        if (startInt == null)
            return null;

        // Length from start to end = total length - start
        var totalLen = _ctx.MkLength(strExpr);
        var remainingLen = _ctx.MkSub(totalLen, startInt) as IntExpr;
        if (remainingLen == null)
            return null;

        return TrackString(_ctx.MkExtract(strExpr, startInt, remainingLen));
    }

    /// <summary>
    /// Translates string concat operation: (concat s1 s2 ...) -> SeqExpr
    /// </summary>
    private Expr? TranslateStringConcat(StringOperationNode node)
    {
        if (node.Arguments.Count < 2)
            return null;

        var strings = new List<SeqExpr>();
        foreach (var arg in node.Arguments)
        {
            var translated = Translate(arg);
            if (translated is not SeqExpr seqExpr)
                return null;
            strings.Add(seqExpr);
        }

        return TrackString(_ctx.MkConcat(strings.ToArray()));
    }

    /// <summary>
    /// Translates string replace operation: (replace s old new) -> SeqExpr
    /// Replaces first occurrence only
    /// </summary>
    private Expr? TranslateStringReplace(StringOperationNode node)
    {
        if (node.Arguments.Count < 3)
            return null;

        var str = Translate(node.Arguments[0]);
        var oldStr = Translate(node.Arguments[1]);
        var newStr = Translate(node.Arguments[2]);

        if (str is not SeqExpr strExpr ||
            oldStr is not SeqExpr oldExpr ||
            newStr is not SeqExpr newExpr)
            return null;

        return TrackString(_ctx.MkReplace(strExpr, oldExpr, newExpr));
    }

    /// <summary>
    /// Converts a bit-vector expression to an IntExpr for Z3 string operations.
    /// </summary>
    private IntExpr? ConvertToIntExpr(Expr? expr)
    {
        if (expr is IntExpr intExpr)
            return intExpr;

        if (expr is BitVecExpr bvExpr)
        {
            // Use signed conversion for signed types, unsigned for unsigned
            return _ctx.MkBV2Int(bvExpr, IsSigned(bvExpr));
        }

        return null;
    }

    // ===========================================
    // Array Theory Enhancement
    // ===========================================

    /// <summary>
    /// Translates array length access: arr.Length -> BitVecExpr (32-bit unsigned)
    /// </summary>
    private Expr? TranslateArrayLength(ArrayLengthNode node)
    {
        if (node.Array is ReferenceNode arrayRef)
        {
            var lengthVarName = $"{arrayRef.Name}$length";

            // Check if we already have a length variable for this array
            if (_variables.TryGetValue(lengthVarName, out var lengthVar))
                return lengthVar.Expr;

            // Create unsigned 32-bit length variable
            var lengthExpr = TrackBitVec(_ctx.MkBVConst(lengthVarName, 32), 32, isSigned: false);
            _variables[lengthVarName] = (lengthExpr, "u32");

            return lengthExpr;
        }
        return null;
    }

    /// <summary>
    /// Declares an array variable with the given name and element type.
    /// Also creates an associated length variable.
    /// </summary>
    /// <param name="name">Array variable name.</param>
    /// <param name="elementType">The type of array elements (e.g., "i32", "u8").</param>
    /// <returns>True if the array was declared successfully.</returns>
    public bool DeclareArrayVariable(string name, string elementType)
    {
        var (elementWidth, elementSigned) = GetTypeWidthAndSignedness(elementType);
        if (elementWidth == 0)
            return false;

        // Create array sort: BitVec64 (index) -> BitVec[elementWidth] (element)
        var bv64Sort = _ctx.MkBitVecSort(64);
        var elementSort = _ctx.MkBitVecSort(elementWidth);
        var arrayExpr = _ctx.MkArrayConst(name, bv64Sort, elementSort);

        _variables[name] = (arrayExpr, $"array<{elementType}>");

        // Create associated length variable (unsigned 32-bit)
        var lengthVarName = $"{name}$length";
        var lengthExpr = TrackBitVec(_ctx.MkBVConst(lengthVarName, 32), 32, isSigned: false);
        _variables[lengthVarName] = (lengthExpr, "u32");

        _arrayInfo[name] = new ArrayInfo(elementType, lengthExpr);
        return true;
    }

    /// <summary>
    /// Gets the bit width and signedness for a type name.
    /// </summary>
    private (uint Width, bool IsSigned) GetTypeWidthAndSignedness(string typeName)
    {
        var normalizedType = NormalizeTypeName(typeName);
        return normalizedType switch
        {
            "i8" or "sbyte" => (8, true),
            "i16" or "short" => (16, true),
            "i32" or "int" => (32, true),
            "i64" or "long" => (64, true),
            "u8" or "byte" => (8, false),
            "u16" or "ushort" => (16, false),
            "u32" or "uint" => (32, false),
            "u64" or "ulong" => (64, false),
            _ => (0, false) // Unsupported type
        };
    }

    // ===========================================
    // Diagnostic Support
    // ===========================================

    /// <summary>
    /// Diagnoses why translation failed for an expression.
    /// Returns a human-readable description of the first unsupported construct found.
    /// </summary>
    /// <param name="node">The expression that failed to translate.</param>
    /// <returns>A diagnostic message, or null if no specific issue was identified.</returns>
    public string? DiagnoseTranslationFailure(ExpressionNode node)
    {
        return DiagnoseNode(node);
    }

    private string? DiagnoseNode(ExpressionNode node)
    {
        return node switch
        {
            FloatLiteralNode f => $"Floating-point literal '{f.Value}' is not supported (Z3 bit-vector theory does not model floats)",
            CallExpressionNode c => $"Function call '{c.Target}' is not supported (only built-in operations are verifiable)",
            ReferenceNode r when !_variables.ContainsKey(r.Name) => $"Unknown variable '{r.Name}'",
            StringOperationNode s => DiagnoseStringOperation(s),
            BinaryOperationNode b => DiagnoseBinaryOp(b),
            UnaryOperationNode u => DiagnoseUnaryOp(u),
            ConditionalExpressionNode c => DiagnoseConditional(c),
            ForallExpressionNode f => DiagnoseForall(f),
            ExistsExpressionNode e => DiagnoseExists(e),
            ImplicationExpressionNode i => DiagnoseImplication(i),
            ArrayAccessNode a => DiagnoseArrayAccess(a),
            ArrayLengthNode l => DiagnoseArrayLength(l),
            _ => $"Unsupported expression type: {node.GetType().Name}"
        };
    }

    private string? DiagnoseStringOperation(StringOperationNode node)
    {
        // Check if this is an unsupported string operation
        var unsupportedOps = new[] {
            StringOp.ToUpper, StringOp.ToLower, StringOp.Trim, StringOp.TrimStart, StringOp.TrimEnd,
            StringOp.PadLeft, StringOp.PadRight, StringOp.Split, StringOp.Join, StringOp.Format,
            StringOp.RegexTest, StringOp.IsNullOrWhiteSpace
        };

        if (unsupportedOps.Contains(node.Operation))
        {
            return $"String operation '{node.Operation}' is not supported (Z3 string theory lacks this operation)";
        }

        // Check arguments recursively
        foreach (var arg in node.Arguments)
        {
            var argResult = Translate(arg);
            if (argResult == null)
            {
                var argDiag = DiagnoseNode(arg);
                if (argDiag != null)
                    return argDiag;
            }
        }

        // Check if arguments have wrong types
        if (node.Arguments.Count > 0)
        {
            var firstArg = Translate(node.Arguments[0]);
            if (firstArg != null && firstArg is not SeqExpr)
            {
                return $"String operation '{node.Operation}' requires a string argument, but got {firstArg.GetType().Name}";
            }
        }

        // Check for operations that require integer arguments (IndexOf start, Substring indices)
        if (node.Operation == StringOp.IndexOf && node.Arguments.Count >= 3)
        {
            var startArg = Translate(node.Arguments[2]);
            if (startArg != null && startArg is not BitVecExpr && startArg is not IntExpr)
            {
                return $"IndexOf start index must be an integer, but got {startArg.GetType().Name}";
            }
        }

        if (node.Operation == StringOp.Substring && node.Arguments.Count >= 3)
        {
            var startArg = Translate(node.Arguments[1]);
            var lenArg = Translate(node.Arguments[2]);
            if (startArg != null && startArg is not BitVecExpr && startArg is not IntExpr)
            {
                return $"Substring start index must be an integer, but got {startArg.GetType().Name}";
            }
            if (lenArg != null && lenArg is not BitVecExpr && lenArg is not IntExpr)
            {
                return $"Substring length must be an integer, but got {lenArg.GetType().Name}";
            }
        }

        if (node.Operation == StringOp.SubstringFrom && node.Arguments.Count >= 2)
        {
            var startArg = Translate(node.Arguments[1]);
            if (startArg != null && startArg is not BitVecExpr && startArg is not IntExpr)
            {
                return $"SubstringFrom start index must be an integer, but got {startArg.GetType().Name}";
            }
        }

        return null;
    }

    private string? DiagnoseBinaryOp(BinaryOperationNode node)
    {
        var left = Translate(node.Left);
        var right = Translate(node.Right);

        if (left == null)
            return DiagnoseNode(node.Left);
        if (right == null)
            return DiagnoseNode(node.Right);

        // Check for type mismatches
        return node.Operator switch
        {
            BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or
            BinaryOperator.Divide or BinaryOperator.Modulo when left is not BitVecExpr || right is not BitVecExpr
                => $"Arithmetic operator '{node.Operator}' requires integer operands, but got {left.GetType().Name} and {right.GetType().Name}",

            BinaryOperator.And or BinaryOperator.Or when left is not BoolExpr || right is not BoolExpr
                => $"Logical operator '{node.Operator}' requires boolean operands, but got {left.GetType().Name} and {right.GetType().Name}",

            BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseOr or BinaryOperator.BitwiseXor or
            BinaryOperator.LeftShift or BinaryOperator.RightShift when left is not BitVecExpr || right is not BitVecExpr
                => $"Bitwise operator '{node.Operator}' requires integer operands, but got {left.GetType().Name} and {right.GetType().Name}",

            _ => null
        };
    }

    private string? DiagnoseUnaryOp(UnaryOperationNode node)
    {
        var operand = Translate(node.Operand);
        if (operand == null)
            return DiagnoseNode(node.Operand);

        return node.Operator switch
        {
            UnaryOperator.Not when operand is not BoolExpr
                => $"Logical NOT requires a boolean operand, but got {operand.GetType().Name}",
            UnaryOperator.Negate when operand is not BitVecExpr
                => $"Negation requires an integer operand, but got {operand.GetType().Name}",
            _ => null
        };
    }

    private string? DiagnoseConditional(ConditionalExpressionNode node)
    {
        var cond = Translate(node.Condition);
        if (cond == null)
            return DiagnoseNode(node.Condition);
        if (cond is not BoolExpr)
            return $"Conditional expression requires boolean condition, but got {cond.GetType().Name}";

        var whenTrue = Translate(node.WhenTrue);
        if (whenTrue == null)
            return DiagnoseNode(node.WhenTrue);

        var whenFalse = Translate(node.WhenFalse);
        if (whenFalse == null)
            return DiagnoseNode(node.WhenFalse);

        // Check for type mismatches between branches
        if (whenTrue.GetType() != whenFalse.GetType())
        {
            // Allow BitVecExpr with different widths (they can be normalized)
            if (whenTrue is BitVecExpr && whenFalse is BitVecExpr)
                return null;

            return $"Conditional branches have incompatible types: '{whenTrue.GetType().Name}' and '{whenFalse.GetType().Name}'";
        }

        return null;
    }

    private string? DiagnoseForall(ForallExpressionNode node)
    {
        foreach (var bv in node.BoundVariables)
        {
            var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
            if (z3Var == null)
                return $"Unsupported type '{bv.TypeName}' for bound variable '{bv.Name}' in forall expression";
        }

        var bodyDiag = DiagnoseNode(node.Body);
        if (bodyDiag != null)
            return bodyDiag;

        return null;
    }

    private string? DiagnoseExists(ExistsExpressionNode node)
    {
        foreach (var bv in node.BoundVariables)
        {
            var z3Var = CreateVariableForType(bv.Name, bv.TypeName);
            if (z3Var == null)
                return $"Unsupported type '{bv.TypeName}' for bound variable '{bv.Name}' in exists expression";
        }

        var bodyDiag = DiagnoseNode(node.Body);
        if (bodyDiag != null)
            return bodyDiag;

        return null;
    }

    private string? DiagnoseImplication(ImplicationExpressionNode node)
    {
        var ante = Translate(node.Antecedent);
        if (ante == null)
            return DiagnoseNode(node.Antecedent);
        if (ante is not BoolExpr)
            return $"Implication antecedent must be boolean, but got {ante.GetType().Name}";

        var cons = Translate(node.Consequent);
        if (cons == null)
            return DiagnoseNode(node.Consequent);
        if (cons is not BoolExpr)
            return $"Implication consequent must be boolean, but got {cons.GetType().Name}";

        return null;
    }

    private string? DiagnoseArrayAccess(ArrayAccessNode node)
    {
        if (node.Array is not ReferenceNode)
        {
            var arrayType = node.Array.GetType().Name;
            return $"Array access requires a simple variable reference, but got '{arrayType}' " +
                   "(computed array expressions like method returns or nested accesses are not supported)";
        }

        var index = Translate(node.Index);
        if (index == null)
            return DiagnoseNode(node.Index);
        if (index is not BitVecExpr)
            return $"Array index must be an integer, but got {index.GetType().Name}";

        return null;
    }

    private string? DiagnoseArrayLength(ArrayLengthNode node)
    {
        if (node.Array is not ReferenceNode)
            return "Array length requires a simple variable reference";

        return null;
    }

    /// <summary>
    /// Diagnoses why a boolean expression translation failed.
    /// This is useful when Translate succeeds but TranslateBoolExpr returns null.
    /// </summary>
    /// <param name="node">The expression that failed to translate to boolean.</param>
    /// <returns>A diagnostic message.</returns>
    public string? DiagnoseBoolExprFailure(ExpressionNode node)
    {
        var expr = Translate(node);
        if (expr == null)
            return DiagnoseTranslationFailure(node);

        if (expr is not BoolExpr)
        {
            return $"Expression must be boolean for verification, but got {expr.GetType().Name}. " +
                   "Boolean expressions include comparisons (==, !=, <, >, <=, >=), " +
                   "logical operations (&&, ||, !), and boolean variables.";
        }

        return null;
    }

    /// <summary>
    /// Gets a description of why a type is not supported.
    /// </summary>
    /// <param name="typeName">The type name that was not supported.</param>
    /// <returns>A diagnostic message.</returns>
    public static string DiagnoseUnsupportedType(string typeName)
    {
        var normalized = typeName.ToLowerInvariant();
        return normalized switch
        {
            "f32" or "f64" or "float" or "double" or "single" or "decimal"
                => $"Type '{typeName}' is not supported (floating-point types cannot be verified with bit-vector theory)",
            "object" or "dynamic"
                => $"Type '{typeName}' is not supported (reference/dynamic types cannot be statically verified)",
            var t when t.Contains("func") || t.Contains("action") || t.Contains("delegate")
                => $"Type '{typeName}' is not supported (function/delegate types cannot be verified)",
            _ => $"Type '{typeName}' is not supported for verification"
        };
    }
}
