using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a bound variable in a quantifier expression.
/// Example: (i i32) binds variable 'i' of type 'i32'
/// </summary>
public sealed class QuantifierVariableNode : AstNode
{
    /// <summary>
    /// The variable name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The type name of the variable.
    /// </summary>
    public string TypeName { get; }

    public QuantifierVariableNode(TextSpan span, string name, string typeName)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a universal quantification (forall) expression.
/// Example: (forall ((i i32)) (>= arr{i} INT:0))
/// Semantics: For all values of i (of type i32), the body expression must be true.
/// </summary>
public sealed class ForallExpressionNode : ExpressionNode
{
    /// <summary>
    /// The bound variables in the quantifier.
    /// </summary>
    public IReadOnlyList<QuantifierVariableNode> BoundVariables { get; }

    /// <summary>
    /// The body expression that must hold for all values of bound variables.
    /// </summary>
    public ExpressionNode Body { get; }

    public ForallExpressionNode(
        TextSpan span,
        IReadOnlyList<QuantifierVariableNode> boundVariables,
        ExpressionNode body)
        : base(span)
    {
        if (boundVariables == null || boundVariables.Count == 0)
            throw new ArgumentException("Quantifier must have at least one bound variable", nameof(boundVariables));
        BoundVariables = boundVariables;
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an existential quantification (exists) expression.
/// Example: (exists ((i i32)) (== arr{i} target))
/// Semantics: There exists at least one value of i (of type i32) for which the body is true.
/// </summary>
public sealed class ExistsExpressionNode : ExpressionNode
{
    /// <summary>
    /// The bound variables in the quantifier.
    /// </summary>
    public IReadOnlyList<QuantifierVariableNode> BoundVariables { get; }

    /// <summary>
    /// The body expression that must hold for at least one value of bound variables.
    /// </summary>
    public ExpressionNode Body { get; }

    public ExistsExpressionNode(
        TextSpan span,
        IReadOnlyList<QuantifierVariableNode> boundVariables,
        ExpressionNode body)
        : base(span)
    {
        if (boundVariables == null || boundVariables.Count == 0)
            throw new ArgumentException("Quantifier must have at least one bound variable", nameof(boundVariables));
        BoundVariables = boundVariables;
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a logical implication expression.
/// Example: (-> antecedent consequent) means "if antecedent then consequent"
/// Semantics: !antecedent || consequent
/// </summary>
public sealed class ImplicationExpressionNode : ExpressionNode
{
    /// <summary>
    /// The antecedent (left side) of the implication.
    /// </summary>
    public ExpressionNode Antecedent { get; }

    /// <summary>
    /// The consequent (right side) of the implication.
    /// </summary>
    public ExpressionNode Consequent { get; }

    public ImplicationExpressionNode(
        TextSpan span,
        ExpressionNode antecedent,
        ExpressionNode consequent)
        : base(span)
    {
        Antecedent = antecedent ?? throw new ArgumentNullException(nameof(antecedent));
        Consequent = consequent ?? throw new ArgumentNullException(nameof(consequent));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
