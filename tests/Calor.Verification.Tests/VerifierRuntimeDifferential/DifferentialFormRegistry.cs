using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

internal static class DifferentialFormRegistry
{
    private static readonly TextSpan Span = TextSpan.Empty;
    private static readonly AttributeCollection Attributes = new();

    public static IReadOnlyList<DifferentialForm> Build()
    {
        var forms = new List<DifferentialForm>();

        forms.AddRange(ModeledForms.ScalarTypes.Select(BuildScalarType));
        forms.AddRange(new[] { "i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64" }
            .Select(BuildArrayElementType));
        forms.AddRange(ModeledForms.ExpressionKinds.Select(BuildExpressionKind));
        forms.AddRange(ModeledForms.Operators.Select(BuildBinaryOperator));
        forms.AddRange(ModeledForms.UnaryOperators.Select(BuildUnaryOperator));
        forms.AddRange(ModeledForms.StringOperations.Select(BuildStringOperation));
        forms.Add(BuildOrdinalComparisonMode());
        forms.Add(BuildQuantifierTypePolicy());

        var duplicate = forms.GroupBy(form => form.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate differential form id '{duplicate.Key}'.");

        return forms;
    }

    public static ExpressionNode ApplyIdentityNesting(ExpressionNode condition, int nestingDepth)
    {
        if (nestingDepth is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(nestingDepth));

        var result = condition;
        for (var depth = 1; depth < nestingDepth; depth++)
            result = Bin(BinaryOperator.And, result, Bool(true));
        return result;
    }

    private static DifferentialForm BuildScalarType(string type)
    {
        return new DifferentialForm(
            $"scalar-type:{type}",
            "scalar-type",
            true,
            null,
            IsStringType(type) ? [Z3Verifier.StringModelAssumption] : Array.Empty<string>(),
            polarity =>
            {
                var predicate = BuildScalarTypePredicate(type, Ref("value"));
                return new FormExpression(
                    polarity == CasePolarity.Provable
                        ? predicate
                        : new UnaryOperationNode(Span, UnaryOperator.Not, predicate),
                    [Parameter("value", type)]);
            },
            condition => ContainsReference(condition, "value"));
    }

    private static DifferentialForm BuildArrayElementType(string type)
    {
        return new DifferentialForm(
            $"array-element-type:{type}",
            "array-element-type",
            true,
            null,
            [Z3Verifier.NullableReferenceModelAssumption],
            polarity =>
            {
                var access = new ArrayAccessNode(Span, Ref("values"), Int(0));
                var predicate = BuildIntegerTypePredicate(type, access);
                return new FormExpression(
                    polarity == CasePolarity.Provable
                        ? predicate
                        : new UnaryOperationNode(Span, UnaryOperator.Not, predicate),
                    [Parameter("values", $"{type}[]")]);
            },
            condition => Contains<ArrayAccessNode>(condition));
    }

    private static DifferentialForm BuildExpressionKind(string kind)
    {
        if (kind == nameof(SelfRefNode))
        {
            return new DifferentialForm(
                $"expression-kind:{kind}",
                "expression-kind",
                true,
                null,
                Array.Empty<string>(),
                polarity => new FormExpression(
                    Bin(
                        polarity == CasePolarity.Provable
                            ? BinaryOperator.Equal
                            : BinaryOperator.NotEqual,
                        new SelfRefNode(Span),
                        new SelfRefNode(Span)),
                    [Parameter("__self__", "i32")]),
                condition => Contains<SelfRefNode>(condition));
        }

        return new DifferentialForm(
            $"expression-kind:{kind}",
            "expression-kind",
            true,
            null,
            GetExpressionKindAssumptions(kind),
            polarity => BuildExpressionKindExpression(kind, polarity),
            condition => ContainsKind(condition, kind));
    }

    private static FormExpression BuildExpressionKindExpression(string kind, CasePolarity polarity)
    {
        var provable = polarity == CasePolarity.Provable;
        return kind switch
        {
            nameof(IntLiteralNode) => NoParameters(
                Bin(BinaryOperator.Equal, Int(7), Int(provable ? 7 : 8))),
            nameof(BoolLiteralNode) => NoParameters(Bool(provable)),
            nameof(StringLiteralNode) => NoParameters(
                Bin(BinaryOperator.Equal, Str("ascii"), Str(provable ? "ascii" : "other"))),
            nameof(ReferenceNode) => new FormExpression(
                Bin(provable ? BinaryOperator.Equal : BinaryOperator.NotEqual, Ref("value"), Ref("value")),
                [Parameter("value", "bool")]),
            nameof(BinaryOperationNode) => NoParameters(
                Bin(BinaryOperator.Equal,
                    Bin(BinaryOperator.Add, Int(2), Int(3)),
                    Int(provable ? 5 : 6))),
            nameof(UnaryOperationNode) => NoParameters(
                Bin(BinaryOperator.Equal,
                    new UnaryOperationNode(Span, UnaryOperator.Negate, Int(5)),
                    Int(provable ? -5 : -4))),
            nameof(ConditionalExpressionNode) => NoParameters(
                Bin(BinaryOperator.Equal,
                    new ConditionalExpressionNode(Span, Bool(true), Int(4), Int(9)),
                    Int(provable ? 4 : 9))),
            nameof(ForallExpressionNode) => NoParameters(BuildForall(provable, "i32")),
            nameof(ExistsExpressionNode) => NoParameters(BuildExists(provable, "i32")),
            nameof(ImplicationExpressionNode) => NoParameters(
                new ImplicationExpressionNode(Span, Bool(true), Bool(provable))),
            nameof(ArrayAccessNode) => new FormExpression(
                BuildArrayAccessCondition(provable),
                [Parameter("values", "i32[]")]),
            nameof(ArrayLengthNode) => new FormExpression(
                BuildArrayLengthCondition(provable),
                [Parameter("values", "i32[]")]),
            nameof(FieldAccessNode) => new FormExpression(
                BuildFieldAccessCondition(provable),
                [Parameter("probe", "Probe")]),
            nameof(StringOperationNode) => NoParameters(
                BuildStringOperationCondition(StringOp.Contains, provable)),
            _ => throw new InvalidOperationException(
                $"ModeledForms.ExpressionKinds added '{kind}' without an F-4 generator.")
        };
    }

    private static DifferentialForm BuildBinaryOperator(BinaryOperator op)
    {
        return new DifferentialForm(
            $"binary-operator:{op}",
            "binary-operator",
            true,
            null,
            Array.Empty<string>(),
            polarity => NoParameters(BuildBinaryOperatorCondition(op, polarity == CasePolarity.Provable)),
            condition => ContainsBinaryOperator(condition, op));
    }

    private static DifferentialForm BuildUnaryOperator(UnaryOperator op)
    {
        return new DifferentialForm(
            $"unary-operator:{op}",
            "unary-operator",
            true,
            null,
            Array.Empty<string>(),
            polarity => NoParameters(BuildUnaryOperatorCondition(op, polarity == CasePolarity.Provable)),
            condition => ContainsUnaryOperator(condition, op));
    }

    private static DifferentialForm BuildStringOperation(StringOp op)
    {
        return new DifferentialForm(
            $"string-operation:{op}",
            "string-operation",
            true,
            null,
            [Z3Verifier.StringModelAssumption],
            polarity => NoParameters(
                BuildStringOperationCondition(op, polarity == CasePolarity.Provable)),
            condition => ContainsStringOperation(condition, op));
    }

    private static DifferentialForm BuildOrdinalComparisonMode()
    {
        return new DifferentialForm(
            "string-comparison-mode:Ordinal",
            "string-comparison-mode",
            true,
            null,
            [Z3Verifier.StringModelAssumption],
            polarity => NoParameters(
                BuildStringOperationCondition(
                    StringOp.Contains,
                    polarity == CasePolarity.Provable,
                    StringComparisonMode.Ordinal)),
            condition => ContainsStringComparisonMode(condition, StringComparisonMode.Ordinal));
    }

    private static DifferentialForm BuildQuantifierTypePolicy()
    {
        return new DifferentialForm(
            "quantifier-bound-variable-type:declarable-alias",
            "quantifier-bound-variable-type",
            true,
            null,
            Array.Empty<string>(),
            polarity => NoParameters(
                BuildForall(polarity == CasePolarity.Provable, "Int32")),
            condition => ContainsQuantifierType(condition, "Int32"));
    }

    private static ExpressionNode BuildBinaryOperatorCondition(BinaryOperator op, bool provable)
    {
        return op switch
        {
            BinaryOperator.Add => Eq(Bin(op, Int(2), Int(3)), Int(provable ? 5 : 6)),
            BinaryOperator.Subtract => Eq(Bin(op, Int(7), Int(3)), Int(provable ? 4 : 5)),
            BinaryOperator.Multiply => Eq(Bin(op, Int(3), Int(4)), Int(provable ? 12 : 13)),
            BinaryOperator.Divide => Eq(Bin(op, Int(8), Int(2)), Int(provable ? 4 : 5)),
            BinaryOperator.Modulo => Eq(Bin(op, Int(7), Int(3)), Int(provable ? 1 : 2)),
            BinaryOperator.Equal => Bin(op, Int(1), Int(provable ? 1 : 2)),
            BinaryOperator.NotEqual => Bin(op, Int(1), Int(provable ? 2 : 1)),
            BinaryOperator.LessThan => Bin(op, Int(1), Int(provable ? 2 : 1)),
            BinaryOperator.LessOrEqual => Bin(op, Int(1), Int(provable ? 1 : 0)),
            BinaryOperator.GreaterThan => Bin(op, Int(2), Int(provable ? 1 : 2)),
            BinaryOperator.GreaterOrEqual => Bin(op, Int(2), Int(provable ? 2 : 3)),
            BinaryOperator.And => Bin(op, Bool(provable), Bool(true)),
            BinaryOperator.Or => Bin(op, Bool(false), Bool(provable)),
            BinaryOperator.BitwiseAnd => Eq(Bin(op, Int(6), Int(3)), Int(provable ? 2 : 3)),
            BinaryOperator.BitwiseOr => Eq(Bin(op, Int(4), Int(1)), Int(provable ? 5 : 4)),
            BinaryOperator.BitwiseXor => Eq(Bin(op, Int(7), Int(3)), Int(provable ? 4 : 5)),
            BinaryOperator.LeftShift => Eq(Bin(op, Int(1), Int(1)), Int(provable ? 2 : 1)),
            BinaryOperator.RightShift => Eq(Bin(op, Int(8), Int(1)), Int(provable ? 4 : 8)),
            _ => throw new InvalidOperationException(
                $"ModeledForms.Operators added '{op}' without an F-4 generator.")
        };
    }

    private static ExpressionNode BuildUnaryOperatorCondition(UnaryOperator op, bool provable)
    {
        return op switch
        {
            UnaryOperator.Not => new UnaryOperationNode(Span, op, Bool(!provable)),
            UnaryOperator.Negate => Eq(
                new UnaryOperationNode(Span, op, Int(5)),
                Int(provable ? -5 : -4)),
            _ => throw new InvalidOperationException(
                $"ModeledForms.UnaryOperators added '{op}' without an F-4 generator.")
        };
    }

    private static ExpressionNode BuildStringOperationCondition(
        StringOp op,
        bool provable,
        StringComparisonMode? forcedMode = null)
    {
        StringComparisonMode? OrdinalWhenRequired() =>
            forcedMode ?? (op is StringOp.StartsWith or StringOp.EndsWith or StringOp.IndexOf
                ? StringComparisonMode.Ordinal
                : null);

        return op switch
        {
            StringOp.Length => Eq(StringOpNode(op, [Str("abc")]), Int(provable ? 3 : 4)),
            StringOp.Contains => StringOpNode(
                op,
                [Str("abc"), Str(provable ? "b" : "z")],
                forcedMode),
            StringOp.StartsWith => StringOpNode(
                op,
                [Str("abc"), Str(provable ? "a" : "z")],
                OrdinalWhenRequired()),
            StringOp.EndsWith => StringOpNode(
                op,
                [Str("abc"), Str(provable ? "c" : "z")],
                OrdinalWhenRequired()),
            StringOp.Equals => StringOpNode(
                op,
                [Str("abc"), Str(provable ? "abc" : "ABC")],
                forcedMode),
            StringOp.IsNullOrEmpty => StringOpNode(
                op,
                [Str(provable ? "" : "x")]),
            StringOp.IndexOf => Eq(
                StringOpNode(op, [Str("abc"), Str("b")], OrdinalWhenRequired()),
                Int(provable ? 1 : 2)),
            StringOp.Substring => Eq(
                StringOpNode(op, [Str("abc"), Int(1), Int(1)]),
                Str(provable ? "b" : "x")),
            StringOp.SubstringFrom => Eq(
                StringOpNode(op, [Str("abc"), Int(1)]),
                Str(provable ? "bc" : "x")),
            StringOp.Concat => Eq(
                StringOpNode(op, [Str("a"), Str("b")]),
                Str(provable ? "ab" : "ac")),
            _ => throw new InvalidOperationException(
                $"ModeledForms.StringOperations added '{op}' without an F-4 generator.")
        };
    }

    private static ExpressionNode BuildForall(bool provable, string boundType)
    {
        var i = Ref("i");
        var bounds = Bin(
            BinaryOperator.And,
            Bin(BinaryOperator.GreaterOrEqual, i, Int(0)),
            Bin(BinaryOperator.LessThan, i, Int(3)));
        var consequent = Bin(BinaryOperator.LessThan, i, Int(provable ? 3 : 2));
        return new ForallExpressionNode(
            Span,
            [new QuantifierVariableNode(Span, "i", boundType)],
            new ImplicationExpressionNode(Span, bounds, consequent));
    }

    private static ExpressionNode BuildExists(bool provable, string boundType)
    {
        var i = Ref("i");
        var bounds = Bin(
            BinaryOperator.And,
            Bin(BinaryOperator.GreaterOrEqual, i, Int(0)),
            Bin(BinaryOperator.LessThan, i, Int(3)));
        var witness = Bin(BinaryOperator.Equal, i, Int(provable ? 1 : 4));
        return new ExistsExpressionNode(
            Span,
            [new QuantifierVariableNode(Span, "i", boundType)],
            Bin(BinaryOperator.And, bounds, witness));
    }

    private static ExpressionNode BuildArrayAccessCondition(bool provable)
    {
        var access = new ArrayAccessNode(Span, Ref("values"), Int(0));
        return Bin(
            provable ? BinaryOperator.Equal : BinaryOperator.NotEqual,
            access,
            access);
    }

    private static ExpressionNode BuildArrayLengthCondition(bool provable)
    {
        var length = new ArrayLengthNode(Span, Ref("values"));
        return Bin(
            provable ? BinaryOperator.Equal : BinaryOperator.NotEqual,
            length,
            length);
    }

    private static ExpressionNode BuildFieldAccessCondition(bool provable)
    {
        var field = new FieldAccessNode(Span, Ref("probe"), "Value");
        var predicate = Bin(
            BinaryOperator.And,
            Bin(BinaryOperator.GreaterOrEqual, field, Int(byte.MinValue)),
            Bin(BinaryOperator.LessOrEqual, field, Int(byte.MaxValue)));
        return provable
            ? predicate
            : new UnaryOperationNode(Span, UnaryOperator.Not, predicate);
    }

    private static ExpressionNode BuildScalarTypePredicate(string type, ExpressionNode value)
    {
        return type switch
        {
            "bool" => Bin(
                BinaryOperator.Or,
                value,
                new UnaryOperationNode(Span, UnaryOperator.Not, value)),
            "str" => Bin(
                BinaryOperator.GreaterOrEqual,
                new StringOperationNode(Span, StringOp.Length, [value]),
                Int(0)),
            _ => BuildIntegerTypePredicate(type, value)
        };
    }

    private static ExpressionNode BuildIntegerTypePredicate(string type, ExpressionNode value)
    {
        return type switch
        {
            "i8" => IntegerRange(value, sbyte.MinValue, sbyte.MaxValue),
            "i16" => Bin(
                BinaryOperator.And,
                IntegerRange(value, short.MinValue, short.MaxValue),
                ExactBoundaryExists(type, short.MaxValue)),
            "i32" => Bin(
                BinaryOperator.And,
                IntegerRange(value, int.MinValue, int.MaxValue),
                ExactBoundaryExists(type, short.MaxValue + 1L)),
            "i64" => Bin(
                BinaryOperator.And,
                new ImplicationExpressionNode(
                    Span,
                    Eq(value, Int(-1)),
                    Bin(BinaryOperator.LessThan, value, Int(0))),
                new ImplicationExpressionNode(
                    Span,
                    Eq(value, Int(2)),
                    Bin(
                        BinaryOperator.GreaterThan,
                        Bin(BinaryOperator.Multiply, value, Int(int.MaxValue)),
                        Int(0)))),
            "u8" => IntegerRange(value, byte.MinValue, byte.MaxValue),
            "u16" => Bin(
                BinaryOperator.And,
                IntegerRange(value, ushort.MinValue, ushort.MaxValue),
                ExactBoundaryExists(type, byte.MaxValue + 1L)),
            "u32" => Bin(
                BinaryOperator.And,
                Bin(BinaryOperator.GreaterOrEqual, value, Int(0)),
                new ImplicationExpressionNode(
                    Span,
                    Eq(value, Int(3)),
                    Bin(
                        BinaryOperator.LessThan,
                        Bin(BinaryOperator.Multiply, value, Int(int.MaxValue)),
                        Int(int.MaxValue)))),
            "u64" => Bin(
                BinaryOperator.And,
                Bin(BinaryOperator.GreaterOrEqual, value, Int(0)),
                new ImplicationExpressionNode(
                    Span,
                    Eq(value, Int(3)),
                    Bin(
                        BinaryOperator.GreaterThan,
                        Bin(BinaryOperator.Multiply, value, Int(int.MaxValue)),
                        Int(int.MaxValue)))),
            _ => throw new InvalidOperationException(
                $"No integer sort discriminator is registered for '{type}'.")
        };
    }

    private static ExpressionNode IntegerRange(ExpressionNode value, long minimum, long maximum) =>
        Bin(
            BinaryOperator.And,
            Bin(BinaryOperator.GreaterOrEqual, value, Int(minimum)),
            Bin(BinaryOperator.LessOrEqual, value, Int(maximum)));

    private static ExpressionNode ExactBoundaryExists(string type, long boundary)
    {
        var witness = Ref("sortWitness");
        return new ExistsExpressionNode(
            Span,
            [new QuantifierVariableNode(Span, "sortWitness", type)],
            Bin(
                BinaryOperator.And,
                Bin(
                    BinaryOperator.And,
                    Bin(BinaryOperator.GreaterOrEqual, witness, Int(boundary)),
                    Bin(BinaryOperator.LessThan, witness, Int(boundary + 1))),
                Eq(witness, Int(boundary))));
    }

    private static FormExpression NoParameters(ExpressionNode condition) =>
        new(condition, Array.Empty<ParameterNode>());

    private static bool IsStringType(string type) =>
        type is "str" or "string";

    private static IReadOnlyList<string> GetExpressionKindAssumptions(string kind)
    {
        return kind switch
        {
            nameof(StringLiteralNode) or nameof(StringOperationNode) =>
                [Z3Verifier.StringModelAssumption],
            nameof(ArrayAccessNode) or nameof(ArrayLengthNode) or nameof(FieldAccessNode) =>
                [Z3Verifier.NullableReferenceModelAssumption],
            _ => Array.Empty<string>()
        };
    }

    private static ParameterNode Parameter(string name, string type) =>
        new(Span, name, type, Attributes);

    private static ReferenceNode Ref(string name) => new(Span, name);
    private static IntLiteralNode Int(long value) => new(Span, value);
    private static BoolLiteralNode Bool(bool value) => new(Span, value);
    private static StringLiteralNode Str(string value) => new(Span, value);
    private static BinaryOperationNode Bin(
        BinaryOperator op,
        ExpressionNode left,
        ExpressionNode right) => new(Span, op, left, right);
    private static BinaryOperationNode Eq(ExpressionNode left, ExpressionNode right) =>
        Bin(BinaryOperator.Equal, left, right);
    private static StringOperationNode StringOpNode(
        StringOp op,
        IReadOnlyList<ExpressionNode> arguments,
        StringComparisonMode? mode = null) => new(Span, op, arguments, mode);

    private static bool ContainsKind(ExpressionNode root, string kind) =>
        DescendantsAndSelf(root).Any(node => node.GetType().Name == kind);

    private static bool Contains<T>(ExpressionNode root) where T : ExpressionNode =>
        DescendantsAndSelf(root).Any(node => node is T);

    private static bool ContainsReference(ExpressionNode root, string name) =>
        DescendantsAndSelf(root).OfType<ReferenceNode>().Any(node => node.Name == name);

    private static bool ContainsBinaryOperator(ExpressionNode root, BinaryOperator op) =>
        DescendantsAndSelf(root).OfType<BinaryOperationNode>().Any(node => node.Operator == op);

    private static bool ContainsUnaryOperator(ExpressionNode root, UnaryOperator op) =>
        DescendantsAndSelf(root).OfType<UnaryOperationNode>().Any(node => node.Operator == op);

    private static bool ContainsStringOperation(ExpressionNode root, StringOp op) =>
        DescendantsAndSelf(root).OfType<StringOperationNode>().Any(node => node.Operation == op);

    private static bool ContainsStringComparisonMode(
        ExpressionNode root,
        StringComparisonMode mode) =>
        DescendantsAndSelf(root).OfType<StringOperationNode>()
            .Any(node => node.ComparisonMode == mode);

    private static bool ContainsQuantifierType(ExpressionNode root, string type) =>
        DescendantsAndSelf(root).Any(node => node switch
        {
            ForallExpressionNode forall => forall.BoundVariables.Any(variable => variable.TypeName == type),
            ExistsExpressionNode exists => exists.BoundVariables.Any(variable => variable.TypeName == type),
            _ => false
        });

    private static IEnumerable<ExpressionNode> DescendantsAndSelf(ExpressionNode expression)
    {
        yield return expression;
        foreach (var child in ExpressionChildren(expression))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static IEnumerable<ExpressionNode> ExpressionChildren(ExpressionNode expression)
    {
        return expression switch
        {
            BinaryOperationNode binary => [binary.Left, binary.Right],
            UnaryOperationNode unary => [unary.Operand],
            ConditionalExpressionNode conditional =>
                [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            ForallExpressionNode forall => [forall.Body],
            ExistsExpressionNode exists => [exists.Body],
            ImplicationExpressionNode implication =>
                [implication.Antecedent, implication.Consequent],
            ArrayAccessNode access => [access.Array, access.Index],
            ArrayLengthNode length => [length.Array],
            FieldAccessNode field => [field.Target],
            StringOperationNode stringOperation => stringOperation.Arguments,
            _ => Array.Empty<ExpressionNode>()
        };
    }
}
