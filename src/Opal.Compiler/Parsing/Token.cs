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
    OpenParen,          // (
    CloseParen,         // )
    Arrow,              // → or ->
    At,                 // @ (for C#-style attributes)
    Comma,              // , (for attribute arguments)

    // v2 operator symbols (for Lisp-style expressions)
    Plus,               // +
    Minus,              // -
    Star,               // *
    Slash,              // /
    Percent,            // %
    EqualEqual,         // ==
    BangEqual,          // !=
    Less,               // <
    LessEqual,          // <=
    Greater,            // >
    GreaterEqual,       // >=
    AmpAmp,             // &&
    PipePipe,           // ||
    Amp,                // &
    Pipe,               // |
    Caret,              // ^
    LessLess,           // <<
    GreaterGreater,     // >>
    StarStar,           // **

    // v2 built-in aliases
    Print,              // §P = Console.WriteLine
    PrintF,             // §Pf = Console.Write
    ReadLine,           // §G = Console.ReadLine (renamed from Get to avoid conflict)
    DebugPrint,         // §D = Debug.WriteLine (renamed from Debug to avoid potential conflicts)

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

    // Phase 5: Using Statements (.NET Interop)
    Using,

    // Phase 6: Arrays and Collections
    Array,
    EndArray,
    Index,
    Length,
    Foreach,
    EndForeach,

    // Phase 7: Generics
    TypeParam,
    Where,
    Generic,

    // Phase 8: Classes, Interfaces, Inheritance
    Class,
    EndClass,
    Interface,
    EndInterface,
    Implements,
    Extends,
    Method,
    EndMethod,
    Virtual,
    Override,
    Abstract,
    Sealed,
    This,
    Base,
    New,
    FieldDef,

    // Phase 9: Properties and Constructors
    Property,
    EndProperty,
    Get,
    Set,
    Init,
    Constructor,
    EndConstructor,
    BaseCall,
    EndBaseCall,
    Assign,

    // Phase 10: Try/Catch/Finally
    Try,
    EndTry,
    Catch,
    Finally,
    Throw,
    Rethrow,
    When,

    // Phase 11: Lambdas, Delegates, Events
    Lambda,
    EndLambda,
    Delegate,
    EndDelegate,
    Event,
    Subscribe,
    Unsubscribe,

    // Phase 12: Async/Await
    Async,
    Await,

    // Phase 9: String Interpolation and Modern Operators
    Interpolate,
    EndInterpolate,
    NullCoalesce,
    NullConditional,
    RangeOp,
    IndexEnd,
    Expression,

    // Phase 10: Advanced Patterns
    With,
    EndWith,
    PositionalPattern,
    PropertyPattern,
    PropertyMatch,
    RelationalPattern,
    ListPattern,
    Var,
    Rest,

    // Extended Features Phase 1: Quick Wins
    Example,            // §EX - Inline examples/tests
    Todo,               // §TODO - Structured todo items
    Fixme,              // §FIXME - Bug markers
    Hack,               // §HACK - Workaround markers

    // Extended Features Phase 2: Core Features
    Uses,               // §USES - Dependency declarations
    EndUses,            // §/USES
    UsedBy,             // §USEDBY - Reverse dependency tracking
    EndUsedBy,          // §/USEDBY
    Assume,             // §ASSUME - Assumptions

    // Extended Features Phase 3: Enhanced Contracts
    Complexity,         // §COMPLEXITY - Performance contracts
    Since,              // §SINCE - API versioning
    Deprecated,         // §DEPRECATED - Deprecation markers
    Breaking,           // §BREAKING - Breaking change markers
    Experimental,       // §EXPERIMENTAL - Experimental feature markers
    Stable,             // §STABLE - Stability markers

    // Extended Features Phase 4: Future Extensions
    Decision,           // §DECISION - Decision records
    EndDecision,        // §/DECISION
    Chosen,             // §CHOSEN - Chosen option in decision
    Rejected,           // §REJECTED - Rejected option in decision
    Reason,             // §REASON - Reason for decision
    Context,            // §CONTEXT - Context markers (also used in decisions)
    EndContext,         // §/CONTEXT
    Visible,            // §VISIBLE - Visible files in context
    EndVisible,         // §/VISIBLE
    HiddenSection,      // §HIDDEN - Hidden files in context (renamed to avoid C# keyword)
    EndHidden,          // §/HIDDEN
    Focus,              // §FOCUS - Focus target
    FileRef,            // §FILE - File reference (renamed to avoid conflict)
    PropertyTest,       // §PROPERTY - Property-based testing (renamed to avoid conflict with Property)
    Lock,               // §LOCK - Multi-agent locking
    AgentAuthor,        // §AUTHOR - Agent authorship tracking
    TaskRef,            // §TASK - Task reference
    DateMarker,         // §DATE - Date marker

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

    public bool IsKeyword => Kind is >= TokenKind.Module and <= TokenKind.DateMarker;

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
