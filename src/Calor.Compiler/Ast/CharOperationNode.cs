using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Char operations supported by Calor native char expressions.
/// </summary>
public enum CharOp
{
    // Literal
    CharLiteral,        // (char-lit "X")        → 'X'

    // Extraction
    CharAt,             // (char-at s i)         → s[i]
    CharCode,           // (char-code c)         → (int)c
    CharFromCode,       // (char-from-code n)    → (char)n

    // Classification (return bool)
    IsLetter,           // (is-letter c)         → char.IsLetter(c)
    IsDigit,            // (is-digit c)          → char.IsDigit(c)
    IsWhiteSpace,       // (is-whitespace c)     → char.IsWhiteSpace(c)
    IsUpper,            // (is-upper c)          → char.IsUpper(c)
    IsLower,            // (is-lower c)          → char.IsLower(c)

    // Transformation (return char)
    ToUpperChar,        // (char-upper c)        → char.ToUpper(c)
    ToLowerChar,        // (char-lower c)        → char.ToLower(c)
}

/// <summary>
/// Represents a native char operation.
/// Examples: (char-at s 0), (is-letter c), (char-upper c)
/// </summary>
public sealed class CharOperationNode : ExpressionNode
{
    public CharOp Operation { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    public CharOperationNode(
        TextSpan span,
        CharOp operation,
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
/// Helper methods for CharOp enum.
/// </summary>
public static class CharOpExtensions
{
    /// <summary>
    /// Parses a char operation name to its enum value.
    /// Returns null if the name is not a recognized char operation.
    /// </summary>
    public static CharOp? FromString(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            // Literal
            "char-lit" => CharOp.CharLiteral,

            // Extraction
            "char-at" => CharOp.CharAt,
            "char-code" => CharOp.CharCode,
            "char-from-code" => CharOp.CharFromCode,

            // Classification
            "is-letter" => CharOp.IsLetter,
            "is-digit" => CharOp.IsDigit,
            "is-whitespace" => CharOp.IsWhiteSpace,
            "is-upper" => CharOp.IsUpper,
            "is-lower" => CharOp.IsLower,

            // Transformation
            "char-upper" => CharOp.ToUpperChar,
            "char-lower" => CharOp.ToLowerChar,

            _ => null
        };
    }

    /// <summary>
    /// Converts a CharOp enum value back to its Calor syntax name.
    /// </summary>
    public static string ToCalorName(this CharOp op)
    {
        return op switch
        {
            // Literal
            CharOp.CharLiteral => "char-lit",

            // Extraction
            CharOp.CharAt => "char-at",
            CharOp.CharCode => "char-code",
            CharOp.CharFromCode => "char-from-code",

            // Classification
            CharOp.IsLetter => "is-letter",
            CharOp.IsDigit => "is-digit",
            CharOp.IsWhiteSpace => "is-whitespace",
            CharOp.IsUpper => "is-upper",
            CharOp.IsLower => "is-lower",

            // Transformation
            CharOp.ToUpperChar => "char-upper",
            CharOp.ToLowerChar => "char-lower",

            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown char operation")
        };
    }

    /// <summary>
    /// Gets the minimum number of arguments required for the operation.
    /// </summary>
    public static int GetMinArgCount(this CharOp op)
    {
        return op switch
        {
            // Single argument operations
            CharOp.CharLiteral or
            CharOp.CharCode or
            CharOp.CharFromCode or
            CharOp.IsLetter or
            CharOp.IsDigit or
            CharOp.IsWhiteSpace or
            CharOp.IsUpper or
            CharOp.IsLower or
            CharOp.ToUpperChar or
            CharOp.ToLowerChar => 1,

            // Two argument operations
            CharOp.CharAt => 2,

            _ => 1
        };
    }

    /// <summary>
    /// Gets the maximum number of arguments allowed for the operation.
    /// </summary>
    public static int GetMaxArgCount(this CharOp op)
    {
        return op switch
        {
            // Single argument operations
            CharOp.CharLiteral or
            CharOp.CharCode or
            CharOp.CharFromCode or
            CharOp.IsLetter or
            CharOp.IsDigit or
            CharOp.IsWhiteSpace or
            CharOp.IsUpper or
            CharOp.IsLower or
            CharOp.ToUpperChar or
            CharOp.ToLowerChar => 1,

            // Two argument operations
            CharOp.CharAt => 2,

            _ => 1
        };
    }
}
