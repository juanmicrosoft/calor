using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// StringBuilder operations supported by Calor native expressions.
/// </summary>
public enum StringBuilderOp
{
    // Creation
    New,                // (sb-new)              → new StringBuilder()
                        // (sb-new "init")       → new StringBuilder("init")

    // Modification (return StringBuilder for chaining)
    Append,             // (sb-append b "text")  → b.Append("text")
    AppendLine,         // (sb-appendline b "t") → b.AppendLine("t")
    Insert,             // (sb-insert b i "t")   → b.Insert(i, "t")
    Remove,             // (sb-remove b i len)   → b.Remove(i, len)
    Clear,              // (sb-clear b)          → b.Clear()

    // Query operations
    ToString,           // (sb-tostring b)       → b.ToString()
    Length,             // (sb-length b)         → b.Length
}

/// <summary>
/// Represents a native StringBuilder operation.
/// Examples: (sb-new), (sb-append builder "text"), (sb-tostring builder)
/// </summary>
public sealed class StringBuilderOperationNode : ExpressionNode
{
    public StringBuilderOp Operation { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    public StringBuilderOperationNode(
        TextSpan span,
        StringBuilderOp operation,
        IReadOnlyList<ExpressionNode> arguments)
        : base(span)
    {
        Operation = operation;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Helper methods for StringBuilderOp enum.
/// </summary>
public static class StringBuilderOpExtensions
{
    /// <summary>
    /// Parses a StringBuilder operation name to its enum value.
    /// Returns null if the name is not a recognized StringBuilder operation.
    /// </summary>
    public static StringBuilderOp? FromString(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "sb-new" => StringBuilderOp.New,
            "sb-append" => StringBuilderOp.Append,
            "sb-appendline" => StringBuilderOp.AppendLine,
            "sb-insert" => StringBuilderOp.Insert,
            "sb-remove" => StringBuilderOp.Remove,
            "sb-clear" => StringBuilderOp.Clear,
            "sb-tostring" => StringBuilderOp.ToString,
            "sb-length" => StringBuilderOp.Length,

            _ => null
        };
    }

    /// <summary>
    /// Converts a StringBuilderOp enum value back to its Calor syntax name.
    /// </summary>
    public static string ToCalorName(this StringBuilderOp op)
    {
        return op switch
        {
            StringBuilderOp.New => "sb-new",
            StringBuilderOp.Append => "sb-append",
            StringBuilderOp.AppendLine => "sb-appendline",
            StringBuilderOp.Insert => "sb-insert",
            StringBuilderOp.Remove => "sb-remove",
            StringBuilderOp.Clear => "sb-clear",
            StringBuilderOp.ToString => "sb-tostring",
            StringBuilderOp.Length => "sb-length",

            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown StringBuilder operation")
        };
    }

    /// <summary>
    /// Gets the minimum number of arguments required for the operation.
    /// </summary>
    public static int GetMinArgCount(this StringBuilderOp op)
    {
        return op switch
        {
            // Zero argument operations
            StringBuilderOp.New => 0,

            // Single argument operations
            StringBuilderOp.Clear or
            StringBuilderOp.ToString or
            StringBuilderOp.Length => 1,

            // Two argument operations
            StringBuilderOp.Append or
            StringBuilderOp.AppendLine => 2,

            // Three argument operations
            StringBuilderOp.Insert or
            StringBuilderOp.Remove => 3,

            _ => 0
        };
    }

    /// <summary>
    /// Gets the maximum number of arguments allowed for the operation.
    /// </summary>
    public static int GetMaxArgCount(this StringBuilderOp op)
    {
        return op switch
        {
            // Zero or one argument (optional initial string)
            StringBuilderOp.New => 1,

            // Single argument operations
            StringBuilderOp.Clear or
            StringBuilderOp.ToString or
            StringBuilderOp.Length => 1,

            // Two argument operations
            StringBuilderOp.Append or
            StringBuilderOp.AppendLine => 2,

            // Three argument operations
            StringBuilderOp.Insert or
            StringBuilderOp.Remove => 3,

            _ => 1
        };
    }
}
