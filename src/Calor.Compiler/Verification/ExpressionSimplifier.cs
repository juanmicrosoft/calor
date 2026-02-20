using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Verification;

/// <summary>
/// Visitor that simplifies contract expressions by applying algebraic transformations.
/// Supports constant folding (int and float), boolean identity, double negation elimination,
/// tautology/contradiction detection, quantifier simplification, De Morgan's laws,
/// arithmetic identities, and commutativity-aware redundancy elimination.
/// </summary>
public sealed class ExpressionSimplifier : IAstVisitor<ExpressionNode>
{
    private readonly DiagnosticBag? _diagnostics;

    /// <summary>
    /// Gets whether any simplification was applied during the visit.
    /// </summary>
    public bool Changed { get; private set; }

    public ExpressionSimplifier(DiagnosticBag? diagnostics = null)
    {
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Simplify an expression node.
    /// </summary>
    public ExpressionNode Simplify(ExpressionNode expr)
    {
        return expr.Accept(this);
    }

    #region Literal Nodes (unchanged)

    public ExpressionNode Visit(IntLiteralNode node) => node;
    public ExpressionNode Visit(StringLiteralNode node) => node;
    public ExpressionNode Visit(BoolLiteralNode node) => node;
    public ExpressionNode Visit(FloatLiteralNode node) => node;
    public ExpressionNode Visit(DecimalLiteralNode node) => node;
    public ExpressionNode Visit(ReferenceNode node) => node;

    #endregion

    #region Binary Operations

    public ExpressionNode Visit(BinaryOperationNode node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);

        // Constant folding for integers
        if (left is IntLiteralNode li && right is IntLiteralNode ri)
        {
            var folded = TryFoldIntegerBinaryOp(node.Span, node.Operator, li.Value, ri.Value);
            if (folded != null)
            {
                Changed = true;
                ReportSimplified(node.Span, "integer constant folded");
                return folded;
            }
        }

        // Constant folding for floats
        if (left is FloatLiteralNode lf && right is FloatLiteralNode rf)
        {
            var folded = TryFoldFloatBinaryOp(node.Span, node.Operator, lf.Value, rf.Value);
            if (folded != null)
            {
                Changed = true;
                ReportSimplified(node.Span, "float constant folded");
                return folded;
            }
        }

        // Mixed int/float constant folding (promote int to float)
        if ((left is IntLiteralNode lmi && right is FloatLiteralNode rmf) ||
            (left is FloatLiteralNode lmf && right is IntLiteralNode rmi))
        {
            double leftVal = left is IntLiteralNode li2 ? li2.Value : ((FloatLiteralNode)left).Value;
            double rightVal = right is IntLiteralNode ri2 ? ri2.Value : ((FloatLiteralNode)right).Value;
            var folded = TryFoldFloatBinaryOp(node.Span, node.Operator, leftVal, rightVal);
            if (folded != null)
            {
                Changed = true;
                ReportSimplified(node.Span, "mixed constant folded");
                return folded;
            }
        }

        // Algebraic identity simplifications
        var algebraic = TrySimplifyAlgebraicIdentity(node.Span, node.Operator, left, right);
        if (algebraic != null)
        {
            Changed = true;
            return algebraic;
        }

        // Boolean simplification
        return node.Operator switch
        {
            BinaryOperator.And => SimplifyAnd(node.Span, left, right),
            BinaryOperator.Or => SimplifyOr(node.Span, left, right),
            BinaryOperator.Equal => SimplifyEqual(node.Span, left, right),
            BinaryOperator.NotEqual => SimplifyNotEqual(node.Span, left, right),
            _ => MaybeNewNode(node, left, right)
        };
    }

    private ExpressionNode? TryFoldIntegerBinaryOp(TextSpan span, BinaryOperator op, int left, int right)
    {
        return op switch
        {
            BinaryOperator.Add => new IntLiteralNode(span, left + right),
            BinaryOperator.Subtract => new IntLiteralNode(span, left - right),
            BinaryOperator.Multiply => new IntLiteralNode(span, left * right),
            BinaryOperator.Divide when right != 0 => new IntLiteralNode(span, left / right),
            BinaryOperator.Modulo when right != 0 => new IntLiteralNode(span, left % right),
            BinaryOperator.LessThan => new BoolLiteralNode(span, left < right),
            BinaryOperator.LessOrEqual => new BoolLiteralNode(span, left <= right),
            BinaryOperator.GreaterThan => new BoolLiteralNode(span, left > right),
            BinaryOperator.GreaterOrEqual => new BoolLiteralNode(span, left >= right),
            BinaryOperator.Equal => new BoolLiteralNode(span, left == right),
            BinaryOperator.NotEqual => new BoolLiteralNode(span, left != right),
            BinaryOperator.BitwiseAnd => new IntLiteralNode(span, left & right),
            BinaryOperator.BitwiseOr => new IntLiteralNode(span, left | right),
            BinaryOperator.BitwiseXor => new IntLiteralNode(span, left ^ right),
            BinaryOperator.LeftShift => new IntLiteralNode(span, left << right),
            BinaryOperator.RightShift => new IntLiteralNode(span, left >> right),
            _ => null
        };
    }

    private ExpressionNode? TryFoldFloatBinaryOp(TextSpan span, BinaryOperator op, double left, double right)
    {
        return op switch
        {
            BinaryOperator.Add => new FloatLiteralNode(span, left + right),
            BinaryOperator.Subtract => new FloatLiteralNode(span, left - right),
            BinaryOperator.Multiply => new FloatLiteralNode(span, left * right),
            BinaryOperator.Divide when Math.Abs(right) > double.Epsilon => new FloatLiteralNode(span, left / right),
            BinaryOperator.Modulo when Math.Abs(right) > double.Epsilon => new FloatLiteralNode(span, left % right),
            BinaryOperator.Power => new FloatLiteralNode(span, Math.Pow(left, right)),
            BinaryOperator.LessThan => new BoolLiteralNode(span, left < right),
            BinaryOperator.LessOrEqual => new BoolLiteralNode(span, left <= right),
            BinaryOperator.GreaterThan => new BoolLiteralNode(span, left > right),
            BinaryOperator.GreaterOrEqual => new BoolLiteralNode(span, left >= right),
            BinaryOperator.Equal => new BoolLiteralNode(span, Math.Abs(left - right) < double.Epsilon),
            BinaryOperator.NotEqual => new BoolLiteralNode(span, Math.Abs(left - right) >= double.Epsilon),
            _ => null
        };
    }

    /// <summary>
    /// Simplifies algebraic identities like x + 0 → x, x * 1 → x, x - x → 0, etc.
    /// </summary>
    private ExpressionNode? TrySimplifyAlgebraicIdentity(TextSpan span, BinaryOperator op, ExpressionNode left, ExpressionNode right)
    {
        // x + 0 → x, 0 + x → x
        if (op == BinaryOperator.Add)
        {
            if (IsZero(right))
            {
                ReportSimplified(span, "x + 0 -> x");
                return left;
            }
            if (IsZero(left))
            {
                ReportSimplified(span, "0 + x -> x");
                return right;
            }
        }

        // x - 0 → x
        if (op == BinaryOperator.Subtract)
        {
            if (IsZero(right))
            {
                ReportSimplified(span, "x - 0 -> x");
                return left;
            }
            // x - x → 0 (also handles commutative equality like (+ a b) - (+ b a))
            if (AreStructurallyEqualOrCommutative(left, right))
            {
                ReportSimplified(span, "x - x -> 0");
                return new IntLiteralNode(span, 0);
            }
        }

        // x * 1 → x, 1 * x → x
        if (op == BinaryOperator.Multiply)
        {
            if (IsOne(right))
            {
                ReportSimplified(span, "x * 1 -> x");
                return left;
            }
            if (IsOne(left))
            {
                ReportSimplified(span, "1 * x -> x");
                return right;
            }
            // x * 0 → 0, 0 * x → 0
            if (IsZero(right))
            {
                ReportSimplified(span, "x * 0 -> 0");
                return right;
            }
            if (IsZero(left))
            {
                ReportSimplified(span, "0 * x -> 0");
                return left;
            }
        }

        // x / 1 → x
        if (op == BinaryOperator.Divide)
        {
            if (IsOne(right))
            {
                ReportSimplified(span, "x / 1 -> x");
                return left;
            }
            // Note: x / x → 1 is NOT safe in general (x could be 0)
            // We only do this for known non-zero constants
            if (left is IntLiteralNode li && right is IntLiteralNode ri && li.Value == ri.Value && li.Value != 0)
            {
                ReportSimplified(span, "n / n -> 1");
                return new IntLiteralNode(span, 1);
            }
        }

        // x % 1 → 0 (for integers)
        if (op == BinaryOperator.Modulo && IsOne(right) && left is IntLiteralNode or ReferenceNode)
        {
            ReportSimplified(span, "x % 1 -> 0");
            return new IntLiteralNode(span, 0);
        }

        return null;
    }

    private bool IsZero(ExpressionNode node)
    {
        return node switch
        {
            IntLiteralNode i => i.Value == 0,
            FloatLiteralNode f => Math.Abs(f.Value) < double.Epsilon,
            _ => false
        };
    }

    private bool IsOne(ExpressionNode node)
    {
        return node switch
        {
            IntLiteralNode i => i.Value == 1,
            FloatLiteralNode f => Math.Abs(f.Value - 1.0) < double.Epsilon,
            _ => false
        };
    }

    private ExpressionNode SimplifyAnd(TextSpan span, ExpressionNode left, ExpressionNode right)
    {
        // (&& true x) → x
        if (left is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportSimplified(span, "(&& true x) -> x");
            return right;
        }

        // (&& x true) → x
        if (right is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportSimplified(span, "(&& x true) -> x");
            return left;
        }

        // (&& false x) → false
        if (left is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportContradiction(span, "(&& false x) is always false");
            return left;
        }

        // (&& x false) → false
        if (right is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportContradiction(span, "(&& x false) is always false");
            return right;
        }

        // (&& x x) → x (redundant) - also check commutative equality
        if (AreStructurallyEqualOrCommutative(left, right))
        {
            Changed = true;
            ReportSimplified(span, "(&& x x) -> x");
            return left;
        }

        // (&& x (! x)) → false (contradiction)
        if (IsNegationOf(left, right) || IsNegationOf(right, left))
        {
            Changed = true;
            ReportContradiction(span, "(&& x (! x)) is a contradiction");
            return new BoolLiteralNode(span, false);
        }

        return MaybeNewBinaryNode(span, BinaryOperator.And, left, right);
    }

    private ExpressionNode SimplifyOr(TextSpan span, ExpressionNode left, ExpressionNode right)
    {
        // (|| true x) → true
        if (left is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportTautology(span, "(|| true x) is always true");
            return left;
        }

        // (|| x true) → true
        if (right is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportTautology(span, "(|| x true) is always true");
            return right;
        }

        // (|| false x) → x
        if (left is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportSimplified(span, "(|| false x) -> x");
            return right;
        }

        // (|| x false) → x
        if (right is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportSimplified(span, "(|| x false) -> x");
            return left;
        }

        // (|| x x) → x (redundant) - also check commutative equality
        if (AreStructurallyEqualOrCommutative(left, right))
        {
            Changed = true;
            ReportSimplified(span, "(|| x x) -> x");
            return left;
        }

        // (|| x (! x)) → true (tautology)
        if (IsNegationOf(left, right) || IsNegationOf(right, left))
        {
            Changed = true;
            ReportTautology(span, "(|| x (! x)) is a tautology");
            return new BoolLiteralNode(span, true);
        }

        return MaybeNewBinaryNode(span, BinaryOperator.Or, left, right);
    }

    private ExpressionNode SimplifyEqual(TextSpan span, ExpressionNode left, ExpressionNode right)
    {
        // (== x x) → true - also check commutative equality
        if (AreStructurallyEqualOrCommutative(left, right))
        {
            Changed = true;
            ReportTautology(span, "(== x x) is always true");
            return new BoolLiteralNode(span, true);
        }

        // (== true x) → x, (== x true) → x
        if (left is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportSimplified(span, "(== true x) -> x");
            return right;
        }
        if (right is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportSimplified(span, "(== x true) -> x");
            return left;
        }

        // (== false x) → (! x), (== x false) → (! x)
        if (left is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportSimplified(span, "(== false x) -> (! x)");
            return new UnaryOperationNode(span, UnaryOperator.Not, right);
        }
        if (right is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportSimplified(span, "(== x false) -> (! x)");
            return new UnaryOperationNode(span, UnaryOperator.Not, left);
        }

        return MaybeNewBinaryNode(span, BinaryOperator.Equal, left, right);
    }

    private ExpressionNode SimplifyNotEqual(TextSpan span, ExpressionNode left, ExpressionNode right)
    {
        // (!= x x) → false - also check commutative equality
        if (AreStructurallyEqualOrCommutative(left, right))
        {
            Changed = true;
            ReportContradiction(span, "(!= x x) is always false");
            return new BoolLiteralNode(span, false);
        }

        return MaybeNewBinaryNode(span, BinaryOperator.NotEqual, left, right);
    }

    #endregion

    #region Unary Operations

    public ExpressionNode Visit(UnaryOperationNode node)
    {
        var operand = node.Operand.Accept(this);

        if (node.Operator == UnaryOperator.Not)
        {
            // (! true) → false
            if (operand is BoolLiteralNode { Value: true })
            {
                Changed = true;
                ReportSimplified(node.Span, "(! true) -> false");
                return new BoolLiteralNode(node.Span, false);
            }

            // (! false) → true
            if (operand is BoolLiteralNode { Value: false })
            {
                Changed = true;
                ReportSimplified(node.Span, "(! false) -> true");
                return new BoolLiteralNode(node.Span, true);
            }

            // (! (! x)) → x (double negation)
            if (operand is UnaryOperationNode { Operator: UnaryOperator.Not } inner)
            {
                Changed = true;
                ReportSimplified(node.Span, "(! (! x)) -> x");
                return inner.Operand;
            }

            // De Morgan's Laws
            // (! (&& a b)) → (|| (! a) (! b))
            if (operand is BinaryOperationNode { Operator: BinaryOperator.And } andOp)
            {
                Changed = true;
                ReportSimplified(node.Span, "De Morgan: (! (&& a b)) -> (|| (! a) (! b))");
                return new BinaryOperationNode(node.Span, BinaryOperator.Or,
                    new UnaryOperationNode(node.Span, UnaryOperator.Not, andOp.Left),
                    new UnaryOperationNode(node.Span, UnaryOperator.Not, andOp.Right));
            }

            // (! (|| a b)) → (&& (! a) (! b))
            if (operand is BinaryOperationNode { Operator: BinaryOperator.Or } orOp)
            {
                Changed = true;
                ReportSimplified(node.Span, "De Morgan: (! (|| a b)) -> (&& (! a) (! b))");
                return new BinaryOperationNode(node.Span, BinaryOperator.And,
                    new UnaryOperationNode(node.Span, UnaryOperator.Not, orOp.Left),
                    new UnaryOperationNode(node.Span, UnaryOperator.Not, orOp.Right));
            }
        }

        if (node.Operator == UnaryOperator.Negate)
        {
            // (- INT:n) → INT:(-n)
            if (operand is IntLiteralNode intLit)
            {
                Changed = true;
                ReportSimplified(node.Span, "integer negation folded");
                return new IntLiteralNode(node.Span, -intLit.Value);
            }

            // (- FLOAT:n) → FLOAT:(-n)
            if (operand is FloatLiteralNode floatLit)
            {
                Changed = true;
                ReportSimplified(node.Span, "float negation folded");
                return new FloatLiteralNode(node.Span, -floatLit.Value);
            }

            // (- (- x)) → x (double negation for arithmetic)
            if (operand is UnaryOperationNode { Operator: UnaryOperator.Negate } innerNeg)
            {
                Changed = true;
                ReportSimplified(node.Span, "(- (- x)) -> x");
                return innerNeg.Operand;
            }
        }

        if (node.Operator == UnaryOperator.BitwiseNot)
        {
            // (~ INT:n) → INT:(~n)
            if (operand is IntLiteralNode intLit)
            {
                Changed = true;
                ReportSimplified(node.Span, "bitwise not folded");
                return new IntLiteralNode(node.Span, ~intLit.Value);
            }

            // (~ (~ x)) → x
            if (operand is UnaryOperationNode { Operator: UnaryOperator.BitwiseNot } innerBnot)
            {
                Changed = true;
                ReportSimplified(node.Span, "(~ (~ x)) -> x");
                return innerBnot.Operand;
            }
        }

        return operand == node.Operand ? node : new UnaryOperationNode(node.Span, node.Operator, operand);
    }

    #endregion

    #region Implication

    public ExpressionNode Visit(ImplicationExpressionNode node)
    {
        var ante = node.Antecedent.Accept(this);
        var cons = node.Consequent.Accept(this);

        // (-> false p) → true
        if (ante is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportTautology(node.Span, "(-> false p) is always true");
            return new BoolLiteralNode(node.Span, true);
        }

        // (-> true p) → p
        if (ante is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportSimplified(node.Span, "(-> true p) -> p");
            return cons;
        }

        // (-> p true) → true
        if (cons is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportTautology(node.Span, "(-> p true) is always true");
            return new BoolLiteralNode(node.Span, true);
        }

        // (-> p false) → (! p)
        if (cons is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportSimplified(node.Span, "(-> p false) -> (! p)");
            return new UnaryOperationNode(node.Span, UnaryOperator.Not, ante);
        }

        // (-> p p) → true (reflexive implication) - also check commutative equality
        if (AreStructurallyEqualOrCommutative(ante, cons))
        {
            Changed = true;
            ReportTautology(node.Span, "(-> p p) is always true");
            return new BoolLiteralNode(node.Span, true);
        }

        // (-> (! p) p) → p (double negation introduction)
        if (IsNegationOf(ante, cons))
        {
            Changed = true;
            ReportSimplified(node.Span, "(-> (! p) p) -> p");
            return cons;
        }

        return ante == node.Antecedent && cons == node.Consequent
            ? node
            : new ImplicationExpressionNode(node.Span, ante, cons);
    }

    #endregion

    #region Quantifiers

    public ExpressionNode Visit(ForallExpressionNode node)
    {
        var body = node.Body.Accept(this);

        // (forall (...) true) → true
        if (body is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportTautology(node.Span, "(forall (...) true) is always true");
            return new BoolLiteralNode(node.Span, true);
        }

        // (forall (...) false) → false (only true if domain is empty, but we assume non-empty domains)
        if (body is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportContradiction(node.Span, "(forall (...) false) is always false (assuming non-empty domain)");
            return new BoolLiteralNode(node.Span, false);
        }

        return body == node.Body
            ? node
            : new ForallExpressionNode(node.Span, node.BoundVariables, body);
    }

    public ExpressionNode Visit(ExistsExpressionNode node)
    {
        var body = node.Body.Accept(this);

        // (exists (...) true) → true (assuming non-empty domain)
        if (body is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportTautology(node.Span, "(exists (...) true) is always true (assuming non-empty domain)");
            return new BoolLiteralNode(node.Span, true);
        }

        // (exists (...) false) → false
        if (body is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportContradiction(node.Span, "(exists (...) false) is always false");
            return new BoolLiteralNode(node.Span, false);
        }

        return body == node.Body
            ? node
            : new ExistsExpressionNode(node.Span, node.BoundVariables, body);
    }

    public ExpressionNode Visit(QuantifierVariableNode node) => throw new InvalidOperationException("QuantifierVariableNode should not be visited directly");

    #endregion

    #region Conditional Expression

    public ExpressionNode Visit(ConditionalExpressionNode node)
    {
        var cond = node.Condition.Accept(this);
        var whenTrue = node.WhenTrue.Accept(this);
        var whenFalse = node.WhenFalse.Accept(this);

        // (? true t f) → t
        if (cond is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportSimplified(node.Span, "(? true t f) -> t");
            return whenTrue;
        }

        // (? false t f) → f
        if (cond is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportSimplified(node.Span, "(? false t f) -> f");
            return whenFalse;
        }

        // (? c x x) → x
        if (AreStructurallyEqualOrCommutative(whenTrue, whenFalse))
        {
            Changed = true;
            ReportSimplified(node.Span, "(? c x x) -> x");
            return whenTrue;
        }

        // (? c true false) → c
        if (whenTrue is BoolLiteralNode { Value: true } && whenFalse is BoolLiteralNode { Value: false })
        {
            Changed = true;
            ReportSimplified(node.Span, "(? c true false) -> c");
            return cond;
        }

        // (? c false true) → (! c)
        if (whenTrue is BoolLiteralNode { Value: false } && whenFalse is BoolLiteralNode { Value: true })
        {
            Changed = true;
            ReportSimplified(node.Span, "(? c false true) -> (! c)");
            return new UnaryOperationNode(node.Span, UnaryOperator.Not, cond);
        }

        return cond == node.Condition && whenTrue == node.WhenTrue && whenFalse == node.WhenFalse
            ? node
            : new ConditionalExpressionNode(node.Span, cond, whenTrue, whenFalse);
    }

    #endregion

    #region Expression nodes that recursively simplify children

    public ExpressionNode Visit(ArrayAccessNode node)
    {
        var array = node.Array.Accept(this);
        var index = node.Index.Accept(this);
        return array == node.Array && index == node.Index
            ? node
            : new ArrayAccessNode(node.Span, array, index);
    }

    public ExpressionNode Visit(ArrayLengthNode node)
    {
        var array = node.Array.Accept(this);
        return array == node.Array
            ? node
            : new ArrayLengthNode(node.Span, array);
    }

    public ExpressionNode Visit(FieldAccessNode node)
    {
        var target = node.Target.Accept(this);
        return target == node.Target
            ? node
            : new FieldAccessNode(node.Span, target, node.FieldName);
    }

    public ExpressionNode Visit(SomeExpressionNode node)
    {
        var value = node.Value.Accept(this);
        return value == node.Value
            ? node
            : new SomeExpressionNode(node.Span, value);
    }

    public ExpressionNode Visit(NoneExpressionNode node) => node;

    public ExpressionNode Visit(OkExpressionNode node)
    {
        var value = node.Value.Accept(this);
        return value == node.Value
            ? node
            : new OkExpressionNode(node.Span, value);
    }

    public ExpressionNode Visit(ErrExpressionNode node)
    {
        var error = node.Error.Accept(this);
        return error == node.Error
            ? node
            : new ErrExpressionNode(node.Span, error);
    }

    public ExpressionNode Visit(CallExpressionNode node)
    {
        var argsChanged = false;
        var newArgs = new List<ExpressionNode>();
        foreach (var arg in node.Arguments)
        {
            var simplified = arg.Accept(this);
            newArgs.Add(simplified);
            if (!ReferenceEquals(simplified, arg))
                argsChanged = true;
        }
        return argsChanged
            ? new CallExpressionNode(node.Span, node.Target, newArgs)
            : node;
    }

    public ExpressionNode Visit(NewExpressionNode node)
    {
        var argsChanged = false;
        var newArgs = new List<ExpressionNode>();
        foreach (var arg in node.Arguments)
        {
            var simplified = arg.Accept(this);
            newArgs.Add(simplified);
            if (!ReferenceEquals(simplified, arg))
                argsChanged = true;
        }

        var initializersChanged = false;
        var newInitializers = new List<ObjectInitializerAssignment>();
        foreach (var init in node.Initializers)
        {
            var simplified = init.Value.Accept(this);
            if (!ReferenceEquals(simplified, init.Value))
            {
                initializersChanged = true;
                newInitializers.Add(new ObjectInitializerAssignment(init.PropertyName, simplified));
            }
            else
            {
                newInitializers.Add(init);
            }
        }

        return argsChanged || initializersChanged
            ? new NewExpressionNode(node.Span, node.TypeName, node.TypeArguments, newArgs, newInitializers)
            : node;
    }

    public ExpressionNode Visit(AnonymousObjectCreationNode node)
    {
        var changed = false;
        var newInits = new List<ObjectInitializerAssignment>();
        foreach (var init in node.Initializers)
        {
            var simplified = init.Value.Accept(this);
            if (!ReferenceEquals(simplified, init.Value))
            {
                changed = true;
                newInits.Add(new ObjectInitializerAssignment(init.PropertyName, simplified));
            }
            else
            {
                newInits.Add(init);
            }
        }
        return changed ? new AnonymousObjectCreationNode(node.Span, newInits) : node;
    }

    public ExpressionNode Visit(ThisExpressionNode node) => node;
    public ExpressionNode Visit(BaseExpressionNode node) => node;

    public ExpressionNode Visit(CollectionContainsNode node)
    {
        // CollectionName is a string, not an expression, so only simplify KeyOrValue
        var keyOrValue = node.KeyOrValue.Accept(this);
        return keyOrValue == node.KeyOrValue
            ? node
            : new CollectionContainsNode(node.Span, node.CollectionName, keyOrValue, node.Mode);
    }

    public ExpressionNode Visit(CollectionCountNode node)
    {
        var collection = node.Collection.Accept(this);
        return collection == node.Collection
            ? node
            : new CollectionCountNode(node.Span, collection);
    }

    public ExpressionNode Visit(MatchExpressionNode node)
    {
        var target = node.Target.Accept(this);
        var casesChanged = false;
        var newCases = new List<MatchCaseNode>();
        foreach (var c in node.Cases)
        {
            ExpressionNode? guard = null;
            var guardChanged = false;
            if (c.Guard != null)
            {
                guard = c.Guard.Accept(this);
                if (!ReferenceEquals(guard, c.Guard))
                    guardChanged = true;
            }
            // Body is statements, not an expression - cannot simplify here
            if (guardChanged)
            {
                casesChanged = true;
                newCases.Add(new MatchCaseNode(c.Span, c.Pattern, guard, c.Body));
            }
            else
            {
                newCases.Add(c);
            }
        }
        return !ReferenceEquals(target, node.Target) || casesChanged
            ? new MatchExpressionNode(node.Span, node.Id, target, newCases, node.Attributes)
            : node;
    }

    public ExpressionNode Visit(NullCoalesceNode node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        return left == node.Left && right == node.Right
            ? node
            : new NullCoalesceNode(node.Span, left, right);
    }

    public ExpressionNode Visit(NullConditionalNode node)
    {
        var target = node.Target.Accept(this);
        return target == node.Target
            ? node
            : new NullConditionalNode(node.Span, target, node.MemberName);
    }

    public ExpressionNode Visit(RangeExpressionNode node)
    {
        var start = node.Start?.Accept(this);
        var end = node.End?.Accept(this);
        return start == node.Start && end == node.End
            ? node
            : new RangeExpressionNode(node.Span, start, end);
    }

    public ExpressionNode Visit(IndexFromEndNode node)
    {
        var offset = node.Offset.Accept(this);
        return offset == node.Offset
            ? node
            : new IndexFromEndNode(node.Span, offset);
    }

    public ExpressionNode Visit(WithExpressionNode node)
    {
        var target = node.Target.Accept(this);
        var assignmentsChanged = false;
        var newAssignments = new List<WithPropertyAssignmentNode>();
        foreach (var assignment in node.Assignments)
        {
            var value = assignment.Value.Accept(this);
            if (!ReferenceEquals(value, assignment.Value))
            {
                assignmentsChanged = true;
                newAssignments.Add(new WithPropertyAssignmentNode(assignment.Span, assignment.PropertyName, value));
            }
            else
            {
                newAssignments.Add(assignment);
            }
        }
        return !ReferenceEquals(target, node.Target) || assignmentsChanged
            ? new WithExpressionNode(node.Span, target, newAssignments)
            : node;
    }

    public ExpressionNode Visit(LambdaExpressionNode node)
    {
        // Lambda bodies might contain expressions that can be simplified
        if (node.ExpressionBody != null)
        {
            var body = node.ExpressionBody.Accept(this);
            return body == node.ExpressionBody
                ? node
                : new LambdaExpressionNode(node.Span, node.Id, node.Parameters, node.Effects, node.IsAsync, body, node.StatementBody, node.Attributes);
        }
        // Statement bodies are not simplified (would require statement visitor)
        return node;
    }

    public ExpressionNode Visit(AwaitExpressionNode node)
    {
        var awaited = node.Awaited.Accept(this);
        return awaited == node.Awaited
            ? node
            : new AwaitExpressionNode(node.Span, awaited, node.ConfigureAwait);
    }

    public ExpressionNode Visit(RecordCreationNode node)
    {
        var fieldsChanged = false;
        var newFields = new List<FieldAssignmentNode>();
        foreach (var field in node.Fields)
        {
            var value = field.Value.Accept(this);
            if (!ReferenceEquals(value, field.Value))
            {
                fieldsChanged = true;
                newFields.Add(new FieldAssignmentNode(field.Span, field.FieldName, value));
            }
            else
            {
                newFields.Add(field);
            }
        }
        return fieldsChanged
            ? new RecordCreationNode(node.Span, node.TypeName, newFields)
            : node;
    }

    public ExpressionNode Visit(InterpolatedStringNode node)
    {
        var partsChanged = false;
        var newParts = new List<InterpolatedStringPartNode>();
        foreach (var part in node.Parts)
        {
            if (part is InterpolatedStringExpressionNode exprPart)
            {
                var simplified = exprPart.Expression.Accept(this);
                if (!ReferenceEquals(simplified, exprPart.Expression))
                {
                    partsChanged = true;
                    newParts.Add(new InterpolatedStringExpressionNode(exprPart.Span, simplified));
                }
                else
                {
                    newParts.Add(part);
                }
            }
            else
            {
                newParts.Add(part);
            }
        }
        return partsChanged
            ? new InterpolatedStringNode(node.Span, newParts)
            : node;
    }

    public ExpressionNode Visit(ArrayCreationNode node)
    {
        var sizeChanged = false;
        ExpressionNode? newSize = node.Size;
        if (node.Size != null)
        {
            newSize = node.Size.Accept(this);
            sizeChanged = !ReferenceEquals(newSize, node.Size);
        }

        var initializerChanged = false;
        var newInitializer = new List<ExpressionNode>();
        foreach (var init in node.Initializer)
        {
            var simplified = init.Accept(this);
            newInitializer.Add(simplified);
            if (!ReferenceEquals(simplified, init))
                initializerChanged = true;
        }

        return sizeChanged || initializerChanged
            ? new ArrayCreationNode(node.Span, node.Id, node.Name, node.ElementType, newSize, newInitializer, node.Attributes)
            : node;
    }

    public ExpressionNode Visit(ListCreationNode node)
    {
        var elementsChanged = false;
        var newElements = new List<ExpressionNode>();
        foreach (var elem in node.Elements)
        {
            var simplified = elem.Accept(this);
            newElements.Add(simplified);
            if (!ReferenceEquals(simplified, elem))
                elementsChanged = true;
        }
        return elementsChanged
            ? new ListCreationNode(node.Span, node.Id, node.Name, node.ElementType, newElements, node.Attributes)
            : node;
    }

    public ExpressionNode Visit(DictionaryCreationNode node)
    {
        var entriesChanged = false;
        var newEntries = new List<KeyValuePairNode>();
        foreach (var entry in node.Entries)
        {
            var key = entry.Key.Accept(this);
            var value = entry.Value.Accept(this);
            if (!ReferenceEquals(key, entry.Key) || !ReferenceEquals(value, entry.Value))
            {
                entriesChanged = true;
                newEntries.Add(new KeyValuePairNode(entry.Span, key, value));
            }
            else
            {
                newEntries.Add(entry);
            }
        }
        return entriesChanged
            ? new DictionaryCreationNode(node.Span, node.Id, node.Name, node.KeyType, node.ValueType, newEntries, node.Attributes)
            : node;
    }

    public ExpressionNode Visit(SetCreationNode node)
    {
        var elementsChanged = false;
        var newElements = new List<ExpressionNode>();
        foreach (var elem in node.Elements)
        {
            var simplified = elem.Accept(this);
            newElements.Add(simplified);
            if (!ReferenceEquals(simplified, elem))
                elementsChanged = true;
        }
        return elementsChanged
            ? new SetCreationNode(node.Span, node.Id, node.Name, node.ElementType, newElements, node.Attributes)
            : node;
    }

    #endregion

    #region Helper methods

    private BinaryOperationNode MaybeNewBinaryNode(TextSpan span, BinaryOperator op, ExpressionNode left, ExpressionNode right)
    {
        return new BinaryOperationNode(span, op, left, right);
    }

    private ExpressionNode MaybeNewNode(BinaryOperationNode original, ExpressionNode left, ExpressionNode right)
    {
        return left == original.Left && right == original.Right
            ? original
            : new BinaryOperationNode(original.Span, original.Operator, left, right);
    }

    /// <summary>
    /// Checks if two expressions are structurally equal (same shape and values).
    /// </summary>
    private bool AreStructurallyEqual(ExpressionNode a, ExpressionNode b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        if (a.GetType() != b.GetType()) return false;

        return (a, b) switch
        {
            (IntLiteralNode ia, IntLiteralNode ib) => ia.Value == ib.Value,
            (BoolLiteralNode ba, BoolLiteralNode bb) => ba.Value == bb.Value,
            (StringLiteralNode sa, StringLiteralNode sb) => sa.Value == sb.Value,
            (FloatLiteralNode fa, FloatLiteralNode fb) => Math.Abs(fa.Value - fb.Value) < double.Epsilon,
            (ReferenceNode ra, ReferenceNode rb) => ra.Name == rb.Name,
            (UnaryOperationNode ua, UnaryOperationNode ub) =>
                ua.Operator == ub.Operator && AreStructurallyEqual(ua.Operand, ub.Operand),
            (BinaryOperationNode ba, BinaryOperationNode bb) =>
                ba.Operator == bb.Operator &&
                AreStructurallyEqual(ba.Left, bb.Left) &&
                AreStructurallyEqual(ba.Right, bb.Right),
            (ImplicationExpressionNode ia, ImplicationExpressionNode ib) =>
                AreStructurallyEqual(ia.Antecedent, ib.Antecedent) &&
                AreStructurallyEqual(ia.Consequent, ib.Consequent),
            (ArrayAccessNode aa, ArrayAccessNode ab) =>
                AreStructurallyEqual(aa.Array, ab.Array) &&
                AreStructurallyEqual(aa.Index, ab.Index),
            (ArrayLengthNode ala, ArrayLengthNode alb) =>
                AreStructurallyEqual(ala.Array, alb.Array),
            (FieldAccessNode fa, FieldAccessNode fb) =>
                fa.FieldName == fb.FieldName && AreStructurallyEqual(fa.Target, fb.Target),
            (ConditionalExpressionNode ca, ConditionalExpressionNode cb) =>
                AreStructurallyEqual(ca.Condition, cb.Condition) &&
                AreStructurallyEqual(ca.WhenTrue, cb.WhenTrue) &&
                AreStructurallyEqual(ca.WhenFalse, cb.WhenFalse),
            (ForallExpressionNode fa, ForallExpressionNode fb) =>
                AreQuantifierVariablesEqual(fa.BoundVariables, fb.BoundVariables) &&
                AreStructurallyEqual(fa.Body, fb.Body),
            (ExistsExpressionNode ea, ExistsExpressionNode eb) =>
                AreQuantifierVariablesEqual(ea.BoundVariables, eb.BoundVariables) &&
                AreStructurallyEqual(ea.Body, eb.Body),
            (SomeExpressionNode sa, SomeExpressionNode sb) =>
                AreStructurallyEqual(sa.Value, sb.Value),
            (NoneExpressionNode, NoneExpressionNode) => true,
            (OkExpressionNode oa, OkExpressionNode ob) =>
                AreStructurallyEqual(oa.Value, ob.Value),
            (ErrExpressionNode ea, ErrExpressionNode eb) =>
                AreStructurallyEqual(ea.Error, eb.Error),
            (ThisExpressionNode, ThisExpressionNode) => true,
            (BaseExpressionNode, BaseExpressionNode) => true,
            (CallExpressionNode ca, CallExpressionNode cb) =>
                ca.Target == cb.Target && AreArgumentsEqual(ca.Arguments, cb.Arguments),
            (CollectionCountNode cca, CollectionCountNode ccb) =>
                AreStructurallyEqual(cca.Collection, ccb.Collection),
            (CollectionContainsNode cca, CollectionContainsNode ccb) =>
                cca.CollectionName == ccb.CollectionName &&
                cca.Mode == ccb.Mode &&
                AreStructurallyEqual(cca.KeyOrValue, ccb.KeyOrValue),
            (NullCoalesceNode na, NullCoalesceNode nb) =>
                AreStructurallyEqual(na.Left, nb.Left) &&
                AreStructurallyEqual(na.Right, nb.Right),
            (NullConditionalNode na, NullConditionalNode nb) =>
                na.MemberName == nb.MemberName && AreStructurallyEqual(na.Target, nb.Target),
            (IndexFromEndNode ia, IndexFromEndNode ib) =>
                AreStructurallyEqual(ia.Offset, ib.Offset),
            _ => false
        };
    }

    private bool AreQuantifierVariablesEqual(IReadOnlyList<QuantifierVariableNode> a, IReadOnlyList<QuantifierVariableNode> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Name != b[i].Name || a[i].TypeName != b[i].TypeName)
                return false;
        }
        return true;
    }

    private bool AreArgumentsEqual(IReadOnlyList<ExpressionNode> a, IReadOnlyList<ExpressionNode> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!AreStructurallyEqual(a[i], b[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if two expressions are structurally equal, considering commutativity
    /// for operators like &&, ||, +, *, ==, !=.
    /// </summary>
    private bool AreStructurallyEqualOrCommutative(ExpressionNode a, ExpressionNode b)
    {
        if (AreStructurallyEqual(a, b)) return true;

        // Check commutative binary operators
        if (a is BinaryOperationNode ba && b is BinaryOperationNode bb &&
            ba.Operator == bb.Operator && IsCommutative(ba.Operator))
        {
            // Check if left-right swapped
            return AreStructurallyEqual(ba.Left, bb.Right) &&
                   AreStructurallyEqual(ba.Right, bb.Left);
        }

        return false;
    }

    private static bool IsCommutative(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => true,
            BinaryOperator.Multiply => true,
            BinaryOperator.And => true,
            BinaryOperator.Or => true,
            BinaryOperator.Equal => true,
            BinaryOperator.NotEqual => true,
            BinaryOperator.BitwiseAnd => true,
            BinaryOperator.BitwiseOr => true,
            BinaryOperator.BitwiseXor => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if 'a' is the negation of 'b' (i.e., a = (! b)).
    /// </summary>
    private bool IsNegationOf(ExpressionNode a, ExpressionNode b)
    {
        return a is UnaryOperationNode { Operator: UnaryOperator.Not } neg &&
               AreStructurallyEqualOrCommutative(neg.Operand, b);
    }

    private void ReportTautology(TextSpan span, string message)
    {
        _diagnostics?.ReportInfo(span, DiagnosticCode.ContractTautology, message);
    }

    private void ReportContradiction(TextSpan span, string message)
    {
        _diagnostics?.ReportWarning(span, DiagnosticCode.ContractContradiction, message);
    }

    private void ReportSimplified(TextSpan span, string message)
    {
        _diagnostics?.ReportInfo(span, DiagnosticCode.ContractSimplified, message);
    }

    #endregion

    #region Non-expression node stubs (required by interface but not used for expression simplification)

    public ExpressionNode Visit(ModuleNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(FunctionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ParameterNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CallStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ReturnStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ForStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(WhileStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(DoWhileStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(IfStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(BindStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ContinueStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(BreakStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(PrintStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(RecordDefinitionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(UnionTypeDefinitionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(EnumDefinitionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(EnumMemberNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(EnumExtensionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(MatchStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(MatchCaseNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(WildcardPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(VariablePatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(LiteralPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(SomePatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(NonePatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(OkPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ErrPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(RequiresNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(EnsuresNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(InvariantNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(UsingDirectiveNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ForeachStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(KeyValuePairNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CollectionPushNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(DictionaryPutNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CollectionRemoveNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CollectionSetIndexNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CollectionClearNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CollectionInsertNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(DictionaryForeachNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(TypeParameterNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(TypeConstraintNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(GenericTypeNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(InterfaceDefinitionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(MethodSignatureNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ClassDefinitionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ClassFieldNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(MethodNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(PropertyNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(PropertyAccessorNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ConstructorNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ConstructorInitializerNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(AssignmentStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CompoundAssignmentStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(UsingStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(TryStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CatchClauseNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ThrowStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(RethrowStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(LambdaParameterNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(DelegateDefinitionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(EventDefinitionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(EventSubscribeNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(EventUnsubscribeNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(InterpolatedStringTextNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(InterpolatedStringExpressionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(WithPropertyAssignmentNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(PositionalPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(PropertyPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(PropertyMatchNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(RelationalPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ListPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(VarPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ConstantPatternNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ExampleNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(IssueNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(DependencyNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(UsesNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(UsedByNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(AssumeNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ComplexityNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(SinceNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(DeprecatedNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(BreakingChangeNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(DecisionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(RejectedOptionNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(ContextNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(FileRefNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(PropertyTestNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(LockNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(AuthorNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(TaskRefNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(CalorAttributeNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(StringOperationNode node)
    {
        // Simplify arguments but preserve the string operation and comparison mode
        var simplifiedArgs = node.Arguments.Select(a => a.Accept(this)).ToList();
        return new StringOperationNode(node.Span, node.Operation, simplifiedArgs, node.ComparisonMode);
    }

    public ExpressionNode Visit(CharOperationNode node)
    {
        // Simplify arguments but preserve the char operation
        var simplifiedArgs = node.Arguments.Select(a => a.Accept(this)).ToList();
        return new CharOperationNode(node.Span, node.Operation, simplifiedArgs);
    }

    public ExpressionNode Visit(TypeOperationNode node)
    {
        var simplifiedOperand = node.Operand.Accept(this);
        return new TypeOperationNode(node.Span, node.Operation, simplifiedOperand, node.TargetType);
    }

    public ExpressionNode Visit(IsPatternNode node)
    {
        var simplifiedOperand = node.Operand.Accept(this);
        return ReferenceEquals(simplifiedOperand, node.Operand)
            ? node
            : new IsPatternNode(node.Span, simplifiedOperand, node.TargetType, node.VariableName);
    }

    public ExpressionNode Visit(StringBuilderOperationNode node)
    {
        // Simplify arguments but preserve the StringBuilder operation
        var simplifiedArgs = node.Arguments.Select(a => a.Accept(this)).ToList();
        return new StringBuilderOperationNode(node.Span, node.Operation, simplifiedArgs);
    }

    // Fallback nodes - cannot be simplified, just return as-is
    public ExpressionNode Visit(FallbackExpressionNode node) => node;
    public ExpressionNode Visit(FallbackCommentNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(TypeOfExpressionNode node) => node;
    public ExpressionNode Visit(ExpressionCallNode node)
    {
        var newTarget = node.TargetExpression.Accept(this);
        var argsChanged = false;
        var newArgs = new List<ExpressionNode>();
        foreach (var arg in node.Arguments)
        {
            var simplified = arg.Accept(this);
            newArgs.Add(simplified);
            if (!ReferenceEquals(simplified, arg))
                argsChanged = true;
        }
        return !ReferenceEquals(newTarget, node.TargetExpression) || argsChanged
            ? new ExpressionCallNode(node.Span, newTarget, newArgs)
            : node;
    }
    public ExpressionNode Visit(ExpressionStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(YieldReturnStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(YieldBreakStatementNode node) => throw new InvalidOperationException();
    public ExpressionNode Visit(RawCSharpNode node) => throw new InvalidOperationException();

    #endregion
}
