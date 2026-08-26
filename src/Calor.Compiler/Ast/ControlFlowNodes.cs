using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a FOR loop.
/// §FOR[id=xxx][var=i][from=0][to=100][step=1]
/// </summary>
public sealed class ForStatementNode : StatementNode
{
    public string Id { get; }
    public string VariableName { get; }
    public TextSpan VariableSpan { get; }
    public ExpressionNode From { get; }
    public ExpressionNode To { get; }
    public ExpressionNode? Step { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public AttributeCollection Attributes { get; }

    public ForStatementNode(
        TextSpan span,
        string id,
        string variableName,
        ExpressionNode from,
        ExpressionNode to,
        ExpressionNode? step,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes,
        TextSpan? variableSpan = null)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        VariableName = variableName ?? throw new ArgumentNullException(nameof(variableName));
        VariableSpan = variableSpan ?? span;
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Step = step;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents a WHILE loop.
/// §WHILE[id=xxx]
/// </summary>
public sealed class WhileStatementNode : StatementNode
{
    public string Id { get; }
    public ExpressionNode Condition { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public AttributeCollection Attributes { get; }

    public WhileStatementNode(
        TextSpan span,
        string id,
        ExpressionNode condition,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents a DO-WHILE loop (body executes at least once).
/// §DO[id=xxx]...§/DO[id=xxx] condition
/// </summary>
public sealed class DoWhileStatementNode : StatementNode
{
    public string Id { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public ExpressionNode Condition { get; }
    public AttributeCollection Attributes { get; }

    public DoWhileStatementNode(
        TextSpan span,
        string id,
        IReadOnlyList<StatementNode> body,
        ExpressionNode condition,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents an IF statement with optional ELSEIF and ELSE branches.
/// §IF[id=xxx]
/// </summary>
public sealed class IfStatementNode : StatementNode
{
    public string Id { get; }
    public ExpressionNode Condition { get; }
    public IReadOnlyList<StatementNode> ThenBody { get; }
    public IReadOnlyList<ElseIfClauseNode> ElseIfClauses { get; }
    public IReadOnlyList<StatementNode>? ElseBody { get; }
    public AttributeCollection Attributes { get; }

    public IfStatementNode(
        TextSpan span,
        string id,
        ExpressionNode condition,
        IReadOnlyList<StatementNode> thenBody,
        IReadOnlyList<ElseIfClauseNode> elseIfClauses,
        IReadOnlyList<StatementNode>? elseBody,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        ThenBody = thenBody ?? throw new ArgumentNullException(nameof(thenBody));
        ElseIfClauses = elseIfClauses ?? throw new ArgumentNullException(nameof(elseIfClauses));
        ElseBody = elseBody;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents an ELSEIF clause within an IF statement.
/// §ELSEIF
/// </summary>
public sealed class ElseIfClauseNode : AstNode
{
    public ExpressionNode Condition { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public ElseIfClauseNode(
        TextSpan span,
        ExpressionNode condition,
        IReadOnlyList<StatementNode> body)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }


}

/// <summary>
/// Represents a variable binding/declaration.
/// §BIND[name=x][type=INT] expression
/// </summary>
public sealed class BindStatementNode : StatementNode
{
    public string Name { get; }
    public TextSpan IdentifierSpan { get; }
    public string? TypeName { get; }
    public TextSpan TypeNameSpan { get; }
    public bool IsMutable { get; }
    public ExpressionNode? Initializer { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// Optional effect row annotating the bound type, written same-line-adjacent to
    /// the header group: <c>§B{f:Func&lt;i32,i32&gt;} §E{cw} &lt;init&gt;</c>.
    /// Position 7 of docs/design/effect-rows-in-the-type-system.md §3.3. A <c>§E</c>
    /// on a later line is Calor0405 — the statement loop has no <c>§E</c> arm.
    /// </summary>
    public EffectsNode? Row { get; }

    public BindStatementNode(
        TextSpan span,
        string name,
        string? typeName,
        bool isMutable,
        ExpressionNode? initializer,
        AttributeCollection attributes,
        TextSpan? identifierSpan = null,
        TextSpan? typeNameSpan = null,
        EffectsNode? row = null)
        : base(span)
    {
        Row = row;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IdentifierSpan = identifierSpan ?? span;
        TypeName = typeName;
        TypeNameSpan = typeNameSpan ?? TextSpan.Empty;
        IsMutable = isMutable;
        Initializer = initializer;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }


}

/// <summary>
/// Represents a binary operation.
/// §OP[kind=ADD] left right
/// </summary>
public sealed class BinaryOperationNode : ExpressionNode
{
    public BinaryOperator Operator { get; }
    public ExpressionNode Left { get; }
    public ExpressionNode Right { get; }

    public BinaryOperationNode(
        TextSpan span,
        BinaryOperator op,
        ExpressionNode left,
        ExpressionNode right)
        : base(span)
    {
        Operator = op;
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }


}

/// <summary>
/// Binary operators supported by Calor.
/// </summary>
public enum BinaryOperator
{
    // Arithmetic
    Add,        // +
    Subtract,   // -
    Multiply,   // *
    Divide,     // /
    Modulo,     // %
    Power,      // **

    // Comparison
    Equal,          // ==
    NotEqual,       // !=
    LessThan,       // <
    LessOrEqual,    // <=
    GreaterThan,    // >
    GreaterOrEqual, // >=

    // Logical
    And,    // &&
    Or,     // ||

    // Bitwise
    BitwiseAnd,     // &
    BitwiseOr,      // |
    BitwiseXor,     // ^
    LeftShift,      // <<
    RightShift,     // >>
}

/// <summary>
/// Helper methods for BinaryOperator.
/// </summary>
/// <summary>
/// Represents a continue statement to skip to next iteration of a loop.
/// §CONTINUE
/// </summary>
public sealed class ContinueStatementNode : StatementNode
{
    public ContinueStatementNode(TextSpan span) : base(span) { }


}

/// <summary>
/// Represents a break statement to exit a loop.
/// §BREAK
/// </summary>
public sealed class BreakStatementNode : StatementNode
{
    public BreakStatementNode(TextSpan span) : base(span) { }


}

/// <summary>
/// Represents a goto statement to jump to a label, case, or default.
/// §GOTO{labelName}       — goto label;
/// §GOTO{CASE:expression} — goto case expression;
/// §GOTO{DEFAULT}         — goto default;
/// </summary>
public sealed class GotoStatementNode : StatementNode
{
    public string Label { get; }
    public ExpressionNode? CaseLabel { get; init; }
    public bool IsDefault { get; init; }
    public GotoStatementNode(TextSpan span, string label) : base(span) { Label = label; }


}

/// <summary>
/// Represents a label definition.
/// §LABEL{labelName}
/// </summary>
public sealed class LabelStatementNode : StatementNode
{
    public string Label { get; }
    public LabelStatementNode(TextSpan span, string label) : base(span) { Label = label; }


}

/// <summary>
/// Represents a yield return statement.
/// §YIELD expression
/// </summary>
public sealed class YieldReturnStatementNode : StatementNode
{
    public ExpressionNode? Expression { get; }

    public YieldReturnStatementNode(TextSpan span, ExpressionNode? expression)
        : base(span)
    {
        Expression = expression;
    }


}

/// <summary>
/// Represents a yield break statement.
/// §YBRK
/// </summary>
public sealed class YieldBreakStatementNode : StatementNode
{
    public YieldBreakStatementNode(TextSpan span) : base(span) { }


}

public static class BinaryOperatorExtensions
{
    public static BinaryOperator? FromString(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "ADD" or "+" => BinaryOperator.Add,
            "SUB" or "SUBTRACT" or "-" => BinaryOperator.Subtract,
            "MUL" or "MULTIPLY" or "*" => BinaryOperator.Multiply,
            "DIV" or "DIVIDE" or "/" => BinaryOperator.Divide,
            "MOD" or "MODULO" or "%" => BinaryOperator.Modulo,
            "POW" or "POWER" or "**" => BinaryOperator.Power,
            "EQ" or "EQUAL" or "==" => BinaryOperator.Equal,
            "NEQ" or "NOTEQUAL" or "NE" or "!=" => BinaryOperator.NotEqual,
            "LT" or "LESSTHAN" or "<" => BinaryOperator.LessThan,
            "LTE" or "LE" or "LESSOREQUAL" or "<=" => BinaryOperator.LessOrEqual,
            "GT" or "GREATERTHAN" or ">" => BinaryOperator.GreaterThan,
            "GTE" or "GE" or "GREATEROREQUAL" or ">=" => BinaryOperator.GreaterOrEqual,
            "AND" or "&&" => BinaryOperator.And,
            "OR" or "||" => BinaryOperator.Or,
            "BAND" or "BITWISEAND" or "&" => BinaryOperator.BitwiseAnd,
            "BOR" or "BITWISEOR" or "|" => BinaryOperator.BitwiseOr,
            "BXOR" or "BITWISEXOR" or "^" => BinaryOperator.BitwiseXor,
            "SHL" or "LEFTSHIFT" or "<<" => BinaryOperator.LeftShift,
            "SHR" or "RIGHTSHIFT" or ">>" => BinaryOperator.RightShift,
            _ => null
        };
    }

    public static string ToCSharpOperator(this BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Power => "**", // Will need Math.Pow in emitter
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterOrEqual => ">=",
            BinaryOperator.And => "&&",
            BinaryOperator.Or => "||",
            BinaryOperator.BitwiseAnd => "&",
            BinaryOperator.BitwiseOr => "|",
            BinaryOperator.BitwiseXor => "^",
            BinaryOperator.LeftShift => "<<",
            BinaryOperator.RightShift => ">>",
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }
}
