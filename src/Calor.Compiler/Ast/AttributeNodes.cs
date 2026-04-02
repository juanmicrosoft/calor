using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a C#-style attribute in Calor (e.g., [@HttpPost], [@Route("api/[controller]")]).
/// </summary>
public sealed class CalorAttributeNode : AstNode
{
    /// <summary>
    /// The attribute name (e.g., "HttpPost", "Route", "ApiController").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The attribute arguments (positional and named).
    /// </summary>
    public IReadOnlyList<CalorAttributeArgument> Arguments { get; }

    /// <summary>
    /// Optional attribute target (e.g., "return", "assembly", "field", "property", "param", "type", "method", "event").
    /// </summary>
    public string? Target { get; }

    public CalorAttributeNode(TextSpan span, string name, IReadOnlyList<CalorAttributeArgument>? arguments = null, string? target = null)
        : base(span)
    {
        Name = name;
        Arguments = arguments ?? Array.Empty<CalorAttributeArgument>();
        Target = target;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);

    /// <summary>
    /// Returns true if this attribute has no arguments.
    /// </summary>
    public bool HasNoArguments => Arguments.Count == 0;

    /// <summary>
    /// Returns true if this attribute has only positional arguments (no named arguments).
    /// </summary>
    public bool HasOnlyPositionalArguments => Arguments.All(a => a.Name == null);
}

/// <summary>
/// Represents an argument to a C#-style attribute.
/// </summary>
public sealed class CalorAttributeArgument
{
    /// <summary>
    /// The argument name for named arguments (e.g., "PropertyName" in [JsonProperty(PropertyName="foo")]).
    /// Null for positional arguments.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// The argument value. Can be string, int, bool, double, or a type reference.
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Creates a positional argument.
    /// </summary>
    public CalorAttributeArgument(object value)
    {
        Name = null;
        Value = value;
    }

    /// <summary>
    /// Creates a named argument.
    /// </summary>
    public CalorAttributeArgument(string? name, object value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>
    /// Returns true if this is a positional argument.
    /// </summary>
    public bool IsPositional => Name == null;

    /// <summary>
    /// Returns true if this is a named argument.
    /// </summary>
    public bool IsNamed => Name != null;

    /// <summary>
    /// Gets the value formatted as a string for Calor/C# emission.
    /// </summary>
    public string GetFormattedValue() => FormatSingleValue(Value);

    internal static string FormatSingleValue(object value)
    {
        return value switch
        {
            string s => $"\"{EscapeString(s)}\"",
            bool b => b ? "true" : "false",
            int i => i.ToString(),
            long l => l.ToString(),
            double d => d.ToString(),
            float f => f.ToString(),
            // Type reference (typeof)
            Type t => $"typeof({t.Name})",
            // For type name strings that represent typeof expressions
            TypeOfReference tr => $"typeof({tr.TypeName})",
            // nameof() expression (e.g., nameof(value))
            NameOfReference nr => $"nameof({nr.Name})",
            // Member access expression (e.g., AttributeTargets.Method)
            MemberAccessReference ma => ma.Expression,
            // Bitwise binary expression (e.g., A | B, A & B, A ^ B)
            BitwiseBinaryExpression bbe => FormatBitwiseBinary(bbe),
            // Bitwise NOT expression (e.g., ~A)
            BitwiseNotExpression bne => $"~{FormatSingleValue(bne.Operand)}",
            // Char literals — quote them to avoid bare special characters
            char c => $"\"{EscapeString(c.ToString())}\"",
            // Default: treat as identifier/enum value
            _ => value?.ToString() ?? "null"
        };
    }

    private static string FormatBitwiseBinary(BitwiseBinaryExpression expr)
    {
        var left = FormatBitwiseChild(expr.Left, expr.Operator);
        var right = FormatBitwiseChild(expr.Right, expr.Operator);
        var op = expr.Operator switch
        {
            BitwiseOperator.Or => "|",
            BitwiseOperator.Xor => "^",
            BitwiseOperator.And => "&",
            _ => "|"
        };
        return $"{left} {op} {right}";
    }

    private static string FormatBitwiseChild(object child, BitwiseOperator parentOp)
    {
        if (child is BitwiseBinaryExpression childExpr && childExpr.Operator < parentOp)
            return $"({FormatSingleValue(child)})";
        return FormatSingleValue(child);
    }

    private static string EscapeString(string s)
    {
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}

/// <summary>
/// Represents a typeof() reference in an attribute argument.
/// </summary>
public sealed class TypeOfReference
{
    public string TypeName { get; }

    public TypeOfReference(string typeName)
    {
        TypeName = typeName;
    }

    public override string ToString() => $"typeof({TypeName})";
}

/// <summary>
/// Represents a nameof() expression in an attribute argument (e.g., nameof(value)).
/// </summary>
public sealed class NameOfReference
{
    public string Name { get; }

    public NameOfReference(string name)
    {
        Name = name;
    }

    public override string ToString() => $"nameof({Name})";
}

/// <summary>
/// Represents a member access expression in an attribute argument (e.g., AttributeTargets.Method).
/// </summary>
public sealed class MemberAccessReference
{
    public string Expression { get; }

    public MemberAccessReference(string expression)
    {
        Expression = expression;
    }

    public override string ToString() => Expression;
}

/// <summary>
/// Operator for bitwise binary expressions in attribute arguments.
/// Values are ordered by precedence (lowest to highest).
/// </summary>
public enum BitwiseOperator
{
    Or = 0,
    Xor = 1,
    And = 2,
}

/// <summary>
/// Represents a bitwise binary expression in an attribute argument (e.g., A | B, A &amp; B, A ^ B).
/// </summary>
public sealed class BitwiseBinaryExpression
{
    public object Left { get; }
    public BitwiseOperator Operator { get; }
    public object Right { get; }

    public BitwiseBinaryExpression(object left, BitwiseOperator op, object right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public override string ToString() => CalorAttributeArgument.FormatSingleValue(this);
}

/// <summary>
/// Represents a bitwise NOT expression in an attribute argument (e.g., ~AttributeTargets.Class).
/// </summary>
public sealed class BitwiseNotExpression
{
    public object Operand { get; }

    public BitwiseNotExpression(object operand)
    {
        Operand = operand;
    }

    public override string ToString() => $"~{CalorAttributeArgument.FormatSingleValue(Operand)}";
}
