namespace Opal.Compiler.Parsing;

/// <summary>
/// Token types recognized by the OPAL lexer.
/// </summary>
public enum TokenKind
{
    // Structural
    SectionMarker,      // §
    OpenBracket,        // [
    CloseBracket,       // ]
    Equals,             // =

    // v2 syntax tokens
    Colon,              // :
    Exclamation,        // !
    Tilde,              // ~
    Hash,               // #
    Question,           // ?

    // Keywords (recognized after §)
    Module,
    EndModule,
    Func,
    EndFunc,
    In,
    Out,
    Effects,
    Body,
    EndBody,
    Call,
    EndCall,
    Arg,
    Return,

    // Phase 2: Control Flow
    For,
    EndFor,
    If,
    EndIf,
    Else,
    ElseIf,
    While,
    EndWhile,
    Bind,
    Op,
    Ref,

    // Phase 3: Type System
    Type,
    EndType,
    Record,
    EndRecord,
    Field,
    Match,
    EndMatch,
    Case,
    Some,
    None,
    Ok,
    Err,
    Variant,

    // Phase 4: Contracts and Effects
    Requires,
    Ensures,
    Invariant,

    // Typed Literals
    IntLiteral,         // INT:42
    StrLiteral,         // STR:"hello"
    BoolLiteral,        // BOOL:true
    FloatLiteral,       // FLOAT:3.14

    // Identifiers and values
    Identifier,

    // Special
    Newline,
    Whitespace,
    Eof,
    Error
}

/// <summary>
/// Represents a single token from the OPAL source.
/// </summary>
public readonly struct Token : IEquatable<Token>
{
    public TokenKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public object? Value { get; }

    public Token(TokenKind kind, string text, TextSpan span, object? value = null)
    {
        Kind = kind;
        Text = text;
        Span = span;
        Value = value;
    }

    public bool IsKeyword => Kind is >= TokenKind.Module and <= TokenKind.Invariant;

    public bool IsLiteral => Kind is TokenKind.IntLiteral or TokenKind.StrLiteral
        or TokenKind.BoolLiteral or TokenKind.FloatLiteral;

    public bool IsTrivia => Kind is TokenKind.Whitespace or TokenKind.Newline;

    public override string ToString()
        => Value != null
            ? $"{Kind}({Value}) at {Span}"
            : $"{Kind}(\"{Text}\") at {Span}";

    public bool Equals(Token other)
        => Kind == other.Kind && Text == other.Text && Span == other.Span;

    public override bool Equals(object? obj)
        => obj is Token other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, Text, Span);

    public static bool operator ==(Token left, Token right) => left.Equals(right);
    public static bool operator !=(Token left, Token right) => !left.Equals(right);
}
