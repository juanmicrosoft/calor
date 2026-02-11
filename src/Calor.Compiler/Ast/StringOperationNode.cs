using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// String operations supported by Calor native string expressions.
/// </summary>
public enum StringOp
{
    // Query operations (return non-string)
    Length,             // (len s)           → s.Length
    Contains,           // (contains s t)    → s.Contains(t)
    StartsWith,         // (starts s t)      → s.StartsWith(t)
    EndsWith,           // (ends s t)        → s.EndsWith(t)
    IndexOf,            // (indexof s t)     → s.IndexOf(t)
    IsNullOrEmpty,      // (isempty s)       → string.IsNullOrEmpty(s)
    IsNullOrWhiteSpace, // (isblank s)       → string.IsNullOrWhiteSpace(s)

    // Transform operations (return string)
    Substring,          // (substr s i n)    → s.Substring(i, n)
    SubstringFrom,      // (substr s i)      → s.Substring(i)
    Replace,            // (replace s a b)   → s.Replace(a, b)
    ToUpper,            // (upper s)         → s.ToUpper()
    ToLower,            // (lower s)         → s.ToLower()
    Trim,               // (trim s)          → s.Trim()
    TrimStart,          // (ltrim s)         → s.TrimStart()
    TrimEnd,            // (rtrim s)         → s.TrimEnd()
    PadLeft,            // (lpad s n)        → s.PadLeft(n)
    PadRight,           // (rpad s n)        → s.PadRight(n)

    // Static operations (various returns)
    Join,               // (join sep items)  → string.Join(sep, items)
    Format,             // (fmt t args...)   → string.Format(t, args)
    Concat,             // (concat a b c)    → string.Concat(a, b, c)
    Split,              // (split s sep)     → s.Split(sep)
    ToString,           // (str x)           → x.ToString()
}

/// <summary>
/// Represents a native string operation.
/// Examples: (upper s), (contains text "hello"), (substr s 0 5)
/// </summary>
public sealed class StringOperationNode : ExpressionNode
{
    public StringOp Operation { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    public StringOperationNode(
        TextSpan span,
        StringOp operation,
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
/// Helper methods for StringOp enum.
/// </summary>
public static class StringOpExtensions
{
    /// <summary>
    /// Parses a string operation name to its enum value.
    /// Returns null if the name is not a recognized string operation.
    /// </summary>
    public static StringOp? FromString(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            // Query operations
            "len" => StringOp.Length,
            "contains" => StringOp.Contains,
            "starts" => StringOp.StartsWith,
            "ends" => StringOp.EndsWith,
            "indexof" => StringOp.IndexOf,
            "isempty" => StringOp.IsNullOrEmpty,
            "isblank" => StringOp.IsNullOrWhiteSpace,

            // Transform operations
            "substr" => StringOp.Substring, // Disambiguate by arg count later
            "replace" => StringOp.Replace,
            "upper" => StringOp.ToUpper,
            "lower" => StringOp.ToLower,
            "trim" => StringOp.Trim,
            "ltrim" => StringOp.TrimStart,
            "rtrim" => StringOp.TrimEnd,
            "lpad" => StringOp.PadLeft,
            "rpad" => StringOp.PadRight,

            // Static operations
            "join" => StringOp.Join,
            "fmt" => StringOp.Format,
            "concat" => StringOp.Concat,
            "split" => StringOp.Split,
            "str" => StringOp.ToString,

            _ => null
        };
    }

    /// <summary>
    /// Converts a StringOp enum value back to its Calor syntax name.
    /// </summary>
    public static string ToCalorName(this StringOp op)
    {
        return op switch
        {
            // Query operations
            StringOp.Length => "len",
            StringOp.Contains => "contains",
            StringOp.StartsWith => "starts",
            StringOp.EndsWith => "ends",
            StringOp.IndexOf => "indexof",
            StringOp.IsNullOrEmpty => "isempty",
            StringOp.IsNullOrWhiteSpace => "isblank",

            // Transform operations
            StringOp.Substring => "substr",
            StringOp.SubstringFrom => "substr",
            StringOp.Replace => "replace",
            StringOp.ToUpper => "upper",
            StringOp.ToLower => "lower",
            StringOp.Trim => "trim",
            StringOp.TrimStart => "ltrim",
            StringOp.TrimEnd => "rtrim",
            StringOp.PadLeft => "lpad",
            StringOp.PadRight => "rpad",

            // Static operations
            StringOp.Join => "join",
            StringOp.Format => "fmt",
            StringOp.Concat => "concat",
            StringOp.Split => "split",
            StringOp.ToString => "str",

            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown string operation")
        };
    }

    /// <summary>
    /// Gets the minimum number of arguments required for the operation.
    /// </summary>
    public static int GetMinArgCount(this StringOp op)
    {
        return op switch
        {
            // Single argument operations
            StringOp.Length or
            StringOp.ToUpper or
            StringOp.ToLower or
            StringOp.Trim or
            StringOp.TrimStart or
            StringOp.TrimEnd or
            StringOp.IsNullOrEmpty or
            StringOp.IsNullOrWhiteSpace or
            StringOp.ToString => 1,

            // Two argument operations
            StringOp.Contains or
            StringOp.StartsWith or
            StringOp.EndsWith or
            StringOp.IndexOf or
            StringOp.SubstringFrom or
            StringOp.Split or
            StringOp.Join or
            StringOp.PadLeft or
            StringOp.PadRight or
            StringOp.Concat or
            StringOp.Format => 2,

            // Three argument operations
            StringOp.Substring or
            StringOp.Replace => 3,

            _ => 1
        };
    }

    /// <summary>
    /// Gets the maximum number of arguments allowed for the operation.
    /// Returns int.MaxValue for variadic operations.
    /// </summary>
    public static int GetMaxArgCount(this StringOp op)
    {
        return op switch
        {
            // Single argument operations
            StringOp.Length or
            StringOp.ToUpper or
            StringOp.ToLower or
            StringOp.Trim or
            StringOp.TrimStart or
            StringOp.TrimEnd or
            StringOp.IsNullOrEmpty or
            StringOp.IsNullOrWhiteSpace or
            StringOp.ToString => 1,

            // Two argument operations
            StringOp.Contains or
            StringOp.StartsWith or
            StringOp.EndsWith or
            StringOp.IndexOf or
            StringOp.SubstringFrom or
            StringOp.Split or
            StringOp.Join => 2,

            // Two or three argument operations
            StringOp.PadLeft or
            StringOp.PadRight => 3, // With optional padding char

            // Three argument operations
            StringOp.Substring or
            StringOp.Replace => 3,

            // Variadic operations
            StringOp.Concat or
            StringOp.Format => int.MaxValue,

            _ => 1
        };
    }
}
