using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a precondition contract.
/// §REQUIRES[message="..."] expression
/// </summary>
public sealed class RequiresNode : AstNode
{
    public ExpressionNode Condition { get; }
    public string? Message { get; }
    public AttributeCollection Attributes { get; }

    public RequiresNode(
        TextSpan span,
        ExpressionNode condition,
        string? message,
        AttributeCollection attributes)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Message = message;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents a postcondition contract.
/// §ENSURES[message="..."] expression
/// The special identifier 'result' can be used to refer to the return value.
/// </summary>
public sealed class EnsuresNode : AstNode
{
    public ExpressionNode Condition { get; }
    public string? Message { get; }
    public AttributeCollection Attributes { get; }

    public EnsuresNode(
        TextSpan span,
        ExpressionNode condition,
        string? message,
        AttributeCollection attributes)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Message = message;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents a class/type invariant contract.
/// §INVARIANT[message="..."] expression
/// </summary>
public sealed class InvariantNode : AstNode
{
    public ExpressionNode Condition { get; }
    public string? Message { get; }
    public AttributeCollection Attributes { get; }

    public InvariantNode(
        TextSpan span,
        ExpressionNode condition,
        string? message,
        AttributeCollection attributes)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Message = message;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents an effect declaration on a function.
/// Contains information about what side effects the function may have.
/// </summary>
public sealed class EffectDeclaration
{
    /// <summary>
    /// The type of effect (e.g., "io", "mutation", "allocation", "exception").
    /// </summary>
    public string EffectType { get; }

    /// <summary>
    /// The specific effect value (e.g., "console_write", "file_read").
    /// </summary>
    public string EffectValue { get; }

    public EffectDeclaration(string effectType, string effectValue)
    {
        EffectType = effectType ?? throw new ArgumentNullException(nameof(effectType));
        EffectValue = effectValue ?? throw new ArgumentNullException(nameof(effectValue));
    }

    public override string ToString() => $"{EffectType}={EffectValue}";
}

/// <summary>
/// Known effect types in Calor.
/// </summary>
public static class EffectTypes
{
    /// <summary>
    /// I/O effects (console, file, network).
    /// </summary>
    public const string IO = "io";

    /// <summary>
    /// Memory mutation effects.
    /// </summary>
    public const string Mutation = "mutation";

    /// <summary>
    /// Heap allocation effects.
    /// </summary>
    public const string Allocation = "allocation";

    /// <summary>
    /// Exception throwing effects.
    /// </summary>
    public const string Exception = "exception";

    /// <summary>
    /// Non-deterministic effects (random, time).
    /// </summary>
    public const string Nondeterminism = "nondeterminism";
}

/// <summary>
/// Known I/O effect values.
/// </summary>
public static class IOEffects
{
    public const string ConsoleRead = "console_read";
    public const string ConsoleWrite = "console_write";
    public const string FileRead = "file_read";
    public const string FileWrite = "file_write";
    public const string NetworkRead = "network_read";
    public const string NetworkWrite = "network_write";
}

/// <summary>
/// #1618: a parameter refinement (<c>int:x | (# &gt; 0)</c>) is the precondition form. This
/// projects each refined parameter into the <see cref="RequiresNode"/> pipeline, with <c>#</c>
/// replaced by the parameter's name, so binding, effect checking, verification and code
/// generation see exactly what a <c>requires</c> clause used to give them.
/// It lives in the AST layer because Binding and Effects use it and may not depend on
/// Verification (eng/compiler-components.json).
/// </summary>
public static class RefinementPreconditions
{
    // One projected node per refined parameter. The binder binds these nodes and later passes
    // look the bound calls up by node identity, so every pass must see the same instance.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ParameterNode, RequiresNode>
        Projected = new();

    /// <summary>
    /// The refinements of the entry-readable parameters (not <c>out</c>, whose refinement
    /// constrains writes), then the explicit preconditions.
    /// </summary>
    public static IReadOnlyList<RequiresNode> Effective(
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<RequiresNode> preconditions)
        => parameters
            .Where(parameter => !parameter.Modifier.HasFlag(ParameterModifier.Out)
                && parameter.InlineRefinement != null)
            .Select(For)
            .Concat(preconditions)
            .ToArray();

    /// <summary>The projected precondition of one refined parameter.</summary>
    public static RequiresNode For(ParameterNode parameter)
        => Projected.GetValue(parameter, refined => new RequiresNode(
            refined.InlineRefinement!.Predicate.Span,
            // A substitution that cannot be modeled leaves # in place (unmodeled), never
            // false: false would manufacture an unsatisfiable precondition set.
            SubstituteSelf(
                refined.InlineRefinement.Predicate,
                new ReferenceNode(refined.InlineRefinement.Predicate.Span, refined.Name, isVerbatim: true)),
            null,
            new AttributeCollection()));

    /// <summary>
    /// Replaces every <c>#</c> with <paramref name="replacement"/>. Unchanged subtrees keep
    /// their identity. A quantifier whose bound variables could capture the replacement is
    /// left as is (its # stays unmodeled); <paramref name="captures"/> decides that, and by
    /// default a reference replacement is captured by a bound variable of the same name.
    /// </summary>
    public static ExpressionNode SubstituteSelf(
        ExpressionNode expression,
        ExpressionNode replacement,
        Func<IReadOnlyList<QuantifierVariableNode>, bool>? captures = null)
    {
        captures ??= bound => replacement is ReferenceNode reference
            && bound.Any(variable => string.Equals(variable.Name, reference.Name, StringComparison.Ordinal));
        return Rewrite(expression);

        ExpressionNode Rewrite(ExpressionNode expr)
        {
            switch (expr)
            {
                case SelfRefNode:
                    return replacement;
                case BinaryOperationNode binary:
                {
                    var left = Rewrite(binary.Left);
                    var right = Rewrite(binary.Right);
                    return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                        ? binary
                        : new BinaryOperationNode(binary.Span, binary.Operator, left, right);
                }
                case UnaryOperationNode unary:
                {
                    var operand = Rewrite(unary.Operand);
                    return ReferenceEquals(operand, unary.Operand)
                        ? unary
                        : new UnaryOperationNode(unary.Span, unary.Operator, operand);
                }
                case ConditionalExpressionNode conditional:
                {
                    var condition = Rewrite(conditional.Condition);
                    var whenTrue = Rewrite(conditional.WhenTrue);
                    var whenFalse = Rewrite(conditional.WhenFalse);
                    return ReferenceEquals(condition, conditional.Condition)
                            && ReferenceEquals(whenTrue, conditional.WhenTrue)
                            && ReferenceEquals(whenFalse, conditional.WhenFalse)
                        ? conditional
                        : new ConditionalExpressionNode(conditional.Span, condition, whenTrue, whenFalse);
                }
                case ArrayAccessNode arrayAccess:
                {
                    var array = Rewrite(arrayAccess.Array);
                    var index = Rewrite(arrayAccess.Index);
                    return ReferenceEquals(array, arrayAccess.Array) && ReferenceEquals(index, arrayAccess.Index)
                        ? arrayAccess
                        : new ArrayAccessNode(arrayAccess.Span, array, index);
                }
                case ArrayLengthNode arrayLength:
                {
                    var array = Rewrite(arrayLength.Array);
                    return ReferenceEquals(array, arrayLength.Array)
                        ? arrayLength
                        : new ArrayLengthNode(arrayLength.Span, array);
                }
                case FieldAccessNode fieldAccess:
                {
                    var target = Rewrite(fieldAccess.Target);
                    return ReferenceEquals(target, fieldAccess.Target)
                        ? fieldAccess
                        : new FieldAccessNode(fieldAccess.Span, target, fieldAccess.FieldName);
                }
                case StringOperationNode stringOperation:
                {
                    var arguments = RewriteAll(stringOperation.Arguments);
                    return arguments == null
                        ? stringOperation
                        : new StringOperationNode(stringOperation.Span, stringOperation.Operation, arguments,
                            stringOperation.ComparisonMode);
                }
                case CharOperationNode character:
                {
                    var arguments = RewriteAll(character.Arguments);
                    return arguments == null
                        ? character
                        : new CharOperationNode(character.Span, character.Operation, arguments);
                }
                case CallExpressionNode call:
                {
                    var arguments = RewriteAll(call.Arguments);
                    return arguments == null
                        ? call
                        : new CallExpressionNode(call.Span, call.Target, arguments, call.ArgumentNames,
                            call.ArgumentModifiers, call.TypeArguments, call.CalleeSpan, call.ReceiverSpan);
                }
                case ExpressionCallNode expressionCall:
                {
                    var target = Rewrite(expressionCall.TargetExpression);
                    var arguments = RewriteAll(expressionCall.Arguments);
                    return ReferenceEquals(target, expressionCall.TargetExpression) && arguments == null
                        ? expressionCall
                        : new ExpressionCallNode(expressionCall.Span, target, arguments ?? expressionCall.Arguments,
                            expressionCall.ArgumentNames, expressionCall.ArgumentModifiers,
                            expressionCall.TypeArguments);
                }
                case NullCoalesceNode coalesce:
                {
                    var left = Rewrite(coalesce.Left);
                    var right = Rewrite(coalesce.Right);
                    return ReferenceEquals(left, coalesce.Left) && ReferenceEquals(right, coalesce.Right)
                        ? coalesce
                        : new NullCoalesceNode(coalesce.Span, left, right);
                }
                case NullConditionalNode nullConditional:
                {
                    var target = Rewrite(nullConditional.Target);
                    return ReferenceEquals(target, nullConditional.Target)
                        ? nullConditional
                        : new NullConditionalNode(nullConditional.Span, target, nullConditional.MemberName);
                }
                case InterpolatedStringNode interpolated:
                {
                    var changed = false;
                    var parts = interpolated.Parts.Select(part =>
                    {
                        if (part is not InterpolatedStringExpressionNode hole)
                            return part;
                        var inner = Rewrite(hole.Expression);
                        if (ReferenceEquals(inner, hole.Expression))
                            return part;
                        changed = true;
                        return new InterpolatedStringExpressionNode(hole.Span, inner, hole.FormatSpecifier,
                            hole.AlignmentClause, hole.Intent, hole.SourceText);
                    }).ToArray();
                    return changed ? new InterpolatedStringNode(interpolated.Span, parts) : interpolated;
                }
                case IsPatternNode isPattern:
                {
                    var operand = Rewrite(isPattern.Operand);
                    return ReferenceEquals(operand, isPattern.Operand)
                        ? isPattern
                        : new IsPatternNode(isPattern.Span, operand, isPattern.TargetType, isPattern.VariableName,
                            isPattern.TargetTypeSpan);
                }
                case TypeOperationNode typeOperation:
                {
                    var operand = Rewrite(typeOperation.Operand);
                    return ReferenceEquals(operand, typeOperation.Operand)
                        ? typeOperation
                        : new TypeOperationNode(typeOperation.Span, typeOperation.Operation, operand,
                            typeOperation.TargetType, typeOperation.TargetTypeSpan);
                }
                case ImplicationExpressionNode implication:
                {
                    var antecedent = Rewrite(implication.Antecedent);
                    var consequent = Rewrite(implication.Consequent);
                    return ReferenceEquals(antecedent, implication.Antecedent)
                            && ReferenceEquals(consequent, implication.Consequent)
                        ? implication
                        : new ImplicationExpressionNode(implication.Span, antecedent, consequent);
                }
                case ForallExpressionNode forall:
                {
                    if (captures(forall.BoundVariables))
                        return forall;
                    var body = Rewrite(forall.Body);
                    return ReferenceEquals(body, forall.Body)
                        ? forall
                        : new ForallExpressionNode(forall.Span, forall.BoundVariables, body);
                }
                case ExistsExpressionNode exists:
                {
                    if (captures(exists.BoundVariables))
                        return exists;
                    var body = Rewrite(exists.Body);
                    return ReferenceEquals(body, exists.Body)
                        ? exists
                        : new ExistsExpressionNode(exists.Span, exists.BoundVariables, body);
                }
                default:
                    return expr;
            }
        }

        // Null when no argument changed, so the caller keeps the original node.
        IReadOnlyList<ExpressionNode>? RewriteAll(IReadOnlyList<ExpressionNode> arguments)
        {
            var rewritten = arguments.Select(Rewrite).ToArray();
            return rewritten.Zip(arguments, ReferenceEquals).All(unchanged => unchanged) ? null : rewritten;
        }
    }
}
