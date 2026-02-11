using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// String comparison mode for string operations.
/// Maps to System.StringComparison enum values.
/// </summary>
public enum StringComparisonMode
{
    /// <summary>Ordinal comparison (default). Maps to StringComparison.Ordinal.</summary>
    Ordinal,
    /// <summary>Case-insensitive ordinal comparison. Maps to StringComparison.OrdinalIgnoreCase.</summary>
    IgnoreCase,
    /// <summary>Invariant culture comparison. Maps to StringComparison.InvariantCulture.</summary>
    Invariant,
    /// <summary>Case-insensitive invariant culture comparison. Maps to StringComparison.InvariantCultureIgnoreCase.</summary>
    InvariantIgnoreCase,
}

/// <summary>
/// Helper methods for StringComparisonMode enum.
/// </summary>
public static class StringComparisonModeExtensions
{
    /// <summary>
    /// Parses a keyword string to StringComparisonMode.
    /// </summary>
    public static StringComparisonMode? FromKeyword(string? keyword)
    {
        return keyword?.ToLowerInvariant() switch
        {
            "ordinal" => StringComparisonMode.Ordinal,
            "ignore-case" => StringComparisonMode.IgnoreCase,
            "invariant" => StringComparisonMode.Invariant,
            "invariant-ignore-case" => StringComparisonMode.InvariantIgnoreCase,
            _ => null
        };
    }

    /// <summary>
    /// Converts a StringComparisonMode to its Calor keyword representation.
    /// </summary>
    public static string ToKeyword(this StringComparisonMode mode)
    {
        return mode switch
        {
            StringComparisonMode.Ordinal => "ordinal",
            StringComparisonMode.IgnoreCase => "ignore-case",
            StringComparisonMode.Invariant => "invariant",
            StringComparisonMode.InvariantIgnoreCase => "invariant-ignore-case",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown comparison mode")
        };
    }

    /// <summary>
    /// Converts a StringComparisonMode to its C# StringComparison enum member name.
    /// </summary>
    public static string ToCSharpName(this StringComparisonMode mode)
    {
        return mode switch
        {
            StringComparisonMode.Ordinal => "StringComparison.Ordinal",
            StringComparisonMode.IgnoreCase => "StringComparison.OrdinalIgnoreCase",
            StringComparisonMode.Invariant => "StringComparison.InvariantCulture",
            StringComparisonMode.InvariantIgnoreCase => "StringComparison.InvariantCultureIgnoreCase",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown comparison mode")
        };
    }
}

/// <summary>
/// Represents a keyword argument like :ordinal or :ignore-case.
/// Only valid as arguments to operations that support them.
/// This is an internal node type used during parsing and is not visitable.
/// </summary>
public sealed class KeywordArgNode : ExpressionNode
{
    public string Name { get; }

    public KeywordArgNode(TextSpan span, string name) : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    // KeywordArgNode is internal to parsing - it should be consumed by the parser
    // and not appear in the final AST. These methods should never be called.
    public override void Accept(IAstVisitor visitor) { }
    public override T Accept<T>(IAstVisitor<T> visitor) => default!;
}

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
    Equals,             // (equals s t)      → s.Equals(t)

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

    // Regex operations
    RegexTest,          // (regex-test s p)        → Regex.IsMatch(s, p)
    RegexMatch,         // (regex-match s p)       → Regex.Match(s, p)
    RegexReplace,       // (regex-replace s p r)   → Regex.Replace(s, p, r)
    RegexSplit,         // (regex-split s p)       → Regex.Split(s, p)
}

/// <summary>
/// Represents a native string operation.
/// Examples: (upper s), (contains text "hello"), (substr s 0 5), (contains s "x" :ignore-case)
/// </summary>
public sealed class StringOperationNode : ExpressionNode
{
    public StringOp Operation { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }
    /// <summary>
    /// Optional string comparison mode for operations that support it.
    /// Supported by: Contains, StartsWith, EndsWith, IndexOf, Equals
    /// </summary>
    public StringComparisonMode? ComparisonMode { get; }

    public StringOperationNode(
        TextSpan span,
        StringOp operation,
        IReadOnlyList<ExpressionNode> arguments,
        StringComparisonMode? comparisonMode = null)
        : base(span)
    {
        Operation = operation;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        ComparisonMode = comparisonMode;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);

    /// <summary>
    /// Returns true if this operation supports string comparison modes.
    /// </summary>
    public static bool SupportsComparisonMode(StringOp op)
    {
        return op is StringOp.Contains or StringOp.StartsWith or StringOp.EndsWith
            or StringOp.IndexOf or StringOp.Equals;
    }
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
            "equals" => StringOp.Equals,

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

            // Regex operations
            "regex-test" => StringOp.RegexTest,
            "regex-match" => StringOp.RegexMatch,
            "regex-replace" => StringOp.RegexReplace,
            "regex-split" => StringOp.RegexSplit,

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
            StringOp.Equals => "equals",

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

            // Regex operations
            StringOp.RegexTest => "regex-test",
            StringOp.RegexMatch => "regex-match",
            StringOp.RegexReplace => "regex-replace",
            StringOp.RegexSplit => "regex-split",

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
            StringOp.Equals or
            StringOp.SubstringFrom or
            StringOp.Split or
            StringOp.Join or
            StringOp.PadLeft or
            StringOp.PadRight or
            StringOp.Concat or
            StringOp.Format or
            StringOp.RegexTest or
            StringOp.RegexMatch or
            StringOp.RegexSplit => 2,

            // Three argument operations
            StringOp.Substring or
            StringOp.Replace or
            StringOp.RegexReplace => 3,

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
            StringOp.Equals or
            StringOp.SubstringFrom or
            StringOp.Split or
            StringOp.Join or
            StringOp.RegexTest or
            StringOp.RegexMatch or
            StringOp.RegexSplit => 2,

            // Two or three argument operations
            StringOp.PadLeft or
            StringOp.PadRight => 3, // With optional padding char

            // Three argument operations
            StringOp.Substring or
            StringOp.Replace or
            StringOp.RegexReplace => 3,

            // Variadic operations
            StringOp.Concat or
            StringOp.Format => int.MaxValue,

            _ => 1
        };
    }
}
