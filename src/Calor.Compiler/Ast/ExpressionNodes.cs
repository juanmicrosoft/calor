using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Base class for expression nodes.
/// </summary>
public abstract class ExpressionNode : AstNode
{
    protected ExpressionNode(TextSpan span) : base(span) { }
}

/// <summary>
/// Represents an integer literal.
/// INT:42
/// </summary>
public sealed class IntLiteralNode : ExpressionNode
{
    public long Value { get; }
    public ulong Magnitude { get; }
    public IntegerLiteralSign Sign { get; }
    public IntegerLiteralBase Base { get; }
    public IntegerLiteralWidth Width { get; private init; }
    public IntegerLiteralSignedness Signedness { get; }
    public bool IsHex => Base == IntegerLiteralBase.Hexadecimal;
    public bool IsUnsigned => Signedness == IntegerLiteralSignedness.Unsigned;
    public ulong UnsignedValue => Magnitude;
    public bool IsLong
    {
        get => Width == IntegerLiteralWidth.Bits64;
        init => Width = value ? IntegerLiteralWidth.Bits64 : IntegerLiteralWidth.Bits32;
    }

    public IntLiteralNode(TextSpan span, long value) : base(span)
    {
        Value = value;
        Sign = value < 0 ? IntegerLiteralSign.Negative : IntegerLiteralSign.Positive;
        Magnitude = value == long.MinValue ? 0x8000_0000_0000_0000UL : (ulong)Math.Abs(value);
        Base = IntegerLiteralBase.Decimal;
        Width = value is > int.MaxValue or < int.MinValue
            ? IntegerLiteralWidth.Bits64
            : IntegerLiteralWidth.Bits32;
        Signedness = IntegerLiteralSignedness.Signed;
    }

    public IntLiteralNode(TextSpan span, long value, bool isHex, bool isUnsigned, ulong unsignedValue)
        : this(
            span,
            isUnsigned ? unsignedValue : value == long.MinValue ? 0x8000_0000_0000_0000UL : (ulong)Math.Abs(value),
            !isUnsigned && value < 0 ? IntegerLiteralSign.Negative : IntegerLiteralSign.Positive,
            isHex ? IntegerLiteralBase.Hexadecimal : IntegerLiteralBase.Decimal,
            isUnsigned
                ? unsignedValue > uint.MaxValue ? IntegerLiteralWidth.Bits64 : IntegerLiteralWidth.Bits32
                : value is > int.MaxValue or < int.MinValue ? IntegerLiteralWidth.Bits64 : IntegerLiteralWidth.Bits32,
            isUnsigned ? IntegerLiteralSignedness.Unsigned : IntegerLiteralSignedness.Signed)
    {
    }

    public IntLiteralNode(
        TextSpan span,
        ulong magnitude,
        IntegerLiteralSign sign,
        IntegerLiteralBase literalBase,
        IntegerLiteralWidth width,
        IntegerLiteralSignedness signedness)
        : base(span)
    {
        Magnitude = magnitude;
        Sign = sign;
        Base = literalBase;
        Width = width;
        Signedness = signedness;
        Value = sign == IntegerLiteralSign.Negative
            ? magnitude == 0x8000_0000_0000_0000UL
                ? long.MinValue
                : -checked((long)magnitude)
            : unchecked((long)magnitude);
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a string literal.
/// STR:"hello"
/// </summary>
public sealed class StringLiteralNode : ExpressionNode
{
    public string Value { get; }

    /// <summary>
    /// True if this string originated from a triple-quote multiline literal (""" ... """).
    /// Used by the emitter to decide between verbatim and escaped string output.
    /// </summary>
    public bool IsMultiline { get; init; }

    /// <summary>
    /// True if this string is a UTF-8 string literal (e.g., "hello"u8 in C#).
    /// When emitting C#, the u8 suffix is appended.
    /// </summary>
    public bool IsUtf8 { get; init; }

    public StringLiteralNode(TextSpan span, string value) : base(span)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a boolean literal.
/// BOOL:true / BOOL:false
/// </summary>
public sealed class BoolLiteralNode : ExpressionNode
{
    public bool Value { get; }

    public BoolLiteralNode(TextSpan span, bool value) : base(span)
    {
        Value = value;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a conditional (ternary) expression.
/// (? condition thenExpr elseExpr)
/// </summary>
public sealed class ConditionalExpressionNode : ExpressionNode
{
    public ExpressionNode Condition { get; }
    public ExpressionNode WhenTrue { get; }
    public ExpressionNode WhenFalse { get; }

    public ConditionalExpressionNode(TextSpan span, ExpressionNode condition, ExpressionNode whenTrue, ExpressionNode whenFalse)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        WhenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
        WhenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a floating-point literal.
/// FLOAT:3.14
/// </summary>
public sealed class FloatLiteralNode : ExpressionNode
{
    public double Value { get; }

    /// <summary>
    /// When true, this literal represents a C# decimal (suffix m) rather than a double.
    /// </summary>
    public bool IsDecimal { get; }

    /// <summary>
    /// When true, this literal represents a C# single-precision <c>float</c> (suffix f)
    /// rather than a <c>double</c>. #774: preserved so a <c>float</c> literal is not
    /// silently widened to <c>double</c> (changing precision and overload resolution);
    /// the emitter recovers the exact single value and re-emits the <c>f</c> suffix.
    /// </summary>
    public bool IsSingle { get; init; }

    public FloatLiteralNode(TextSpan span, double value) : base(span)
    {
        Value = value;
    }

    public FloatLiteralNode(TextSpan span, double value, bool isDecimal) : base(span)
    {
        Value = value;
        IsDecimal = isDecimal;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a decimal literal.
/// DECIMAL:18.00M
/// </summary>
public sealed class DecimalLiteralNode : ExpressionNode
{
    public decimal Value { get; }

    public DecimalLiteralNode(TextSpan span, decimal value) : base(span)
    {
        Value = value;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a reference to a variable or identifier.
/// </summary>
public sealed class ReferenceNode : ExpressionNode
{
    public string Name { get; }

    public ReferenceNode(TextSpan span, string name) : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a unary operation (prefix operators like !, ~, -).
/// </summary>
public sealed class UnaryOperationNode : ExpressionNode
{
    public UnaryOperator Operator { get; }
    public ExpressionNode Operand { get; }

    public UnaryOperationNode(TextSpan span, UnaryOperator op, ExpressionNode operand)
        : base(span)
    {
        Operator = op;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Unary operators supported by Calor.
/// </summary>
public enum UnaryOperator
{
    Negate,         // - (unary minus)
    Not,            // ! (logical not)
    BitwiseNot,     // ~ (bitwise not)
    PreIncrement,   // ++x (prefix increment)
    PreDecrement,   // --x (prefix decrement)
    PostIncrement,  // x++ (postfix increment)
    PostDecrement,  // x-- (postfix decrement)
}

/// <summary>
/// Helper methods for UnaryOperator.
/// </summary>
public static class UnaryOperatorExtensions
{
    public static UnaryOperator? FromString(string? value)
    {
        return value?.ToUpperInvariant() switch
        {
            "NEG" or "NEGATE" or "-" => UnaryOperator.Negate,
            "NOT" or "!" => UnaryOperator.Not,
            "BNOT" or "BITWISENOT" or "~" => UnaryOperator.BitwiseNot,
            "INC" or "++" or "PRE-INC" => UnaryOperator.PreIncrement,
            "DEC" or "--" or "PRE-DEC" => UnaryOperator.PreDecrement,
            "POST-INC" => UnaryOperator.PostIncrement,
            "POST-DEC" => UnaryOperator.PostDecrement,
            _ => null
        };
    }

    public static string ToCSharpOperator(this UnaryOperator op)
    {
        return op switch
        {
            UnaryOperator.Negate => "-",
            UnaryOperator.Not => "!",
            UnaryOperator.BitwiseNot => "~",
            UnaryOperator.PreIncrement => "++",
            UnaryOperator.PreDecrement => "--",
            UnaryOperator.PostIncrement => "++",
            UnaryOperator.PostDecrement => "--",
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }
}

/// <summary>
/// Represents a fallback expression for unsupported C# constructs.
/// Emitted as §ERR{"TODO: feature"} /* C#: originalCode */ in Calor output.
/// </summary>
public sealed class FallbackExpressionNode : ExpressionNode
{
    /// <summary>
    /// The original C# code that could not be converted.
    /// </summary>
    public string OriginalCSharp { get; }

    /// <summary>
    /// The name of the unsupported feature (e.g., "implicit-new-with-args", "stackalloc").
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Optional suggestion for how to manually convert this construct.
    /// </summary>
    public string? Suggestion { get; }

    public FallbackExpressionNode(TextSpan span, string originalCSharp, string featureName, string? suggestion = null)
        : base(span)
    {
        OriginalCSharp = originalCSharp ?? throw new ArgumentNullException(nameof(originalCSharp));
        FeatureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
        Suggestion = suggestion;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an inline raw C# expression that passes through verbatim.
/// §CS{expr}
/// </summary>
public sealed class RawCSharpExpressionNode : ExpressionNode
{
    public string CSharpCode { get; }

    public RawCSharpExpressionNode(TextSpan span, string csharpCode)
        : base(span)
    {
        CSharpCode = csharpCode ?? throw new ArgumentNullException(nameof(csharpCode));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
