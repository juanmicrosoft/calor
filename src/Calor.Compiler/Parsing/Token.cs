using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Parsing;

/// <summary>
/// Token types recognized by the Calor lexer.
/// </summary>
public enum TokenKind
{
    // Structural
    SectionMarker,      // §
    OpenBracket,        // [  (used for array types)
    CloseBracket,       // ]  (used for array types)
    OpenBrace,          // {  (used for tag attributes)
    CloseBrace,         // }  (used for tag attributes)
    Equals,             // =

    // v2 syntax tokens
    Colon,              // :
    Exclamation,        // !
    Tilde,              // ~
    Hash,               // #
    Question,           // ?
    OpenParen,          // (
    CloseParen,         // )
    Arrow,              // →
    FatArrow,           // => (postcondition header or lambda)
    Semicolon,          // ; (re-evaluated for clauses)
    At,                 // @ (for C#-style attributes)
    Comma,              // , (for attribute arguments)

    // v2 operator symbols (for Lisp-style expressions)
    Plus,               // +
    Minus,              // -
    Star,               // *
    Slash,              // /
    Backslash,          // \
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
    Dot,                // .

    // v2 built-in aliases
    Print,              // §P = Console.WriteLine
    PrintF,             // §Pf = Console.Write
    ReadLine,           // §G = Console.ReadLine (renamed from Get to avoid conflict)
    DebugPrint,         // §D = Debug.WriteLine (renamed from Debug to avoid potential conflicts)

    // Canonical keywords and retained internal legacy kinds
    Module,
    EndModule,
    Func,
    EndFunc,
    In,
    Out,
    Effects,
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
    Do,
    EndDo,
    Break,
    Continue,
    Goto,
    Label,
    SemVer,             // semver "MAJOR.MINOR.PATCH" - module semantics-version declaration
    Bind,
    Let,
    From,
    To,
    Step,

    // Phase 3: Type System
    Match,
    EndMatch,
    Case,
    EndCase,    // §/K - closing case tag for block syntax

    // Phase 4: Contracts and Effects
    Requires,
    Ensures,
    Invariant,

    // Phase 5: Using Statements (.NET Interop)
    Using,
    Use,                // §USE - using statement open
    EndUse,             // §/USE - using statement close

    // Phase 6: Arrays and Collections
    Array,
    EndArray,
    Index,
    Length,
    Foreach,
    EndForeach,

    // Phase 6 Extended: Collections (List, Dictionary, HashSet)
    List,           // §LIST - List<T>
    EndList,        // §/LIST
    Dict,           // §DICT - Dictionary<K,V>
    EndDict,        // §/DICT
    HashSet,        // §HSET - HashSet<T>
    EndHashSet,     // §/HSET
    KeyValue,       // §KV - Dictionary key-value pair
    Push,           // §PUSH - Add to list/set
    Put,            // §PUT - Add/update dictionary entry
    Remove,         // §REM - Remove from collection
    SetIndex,       // §SETIDX - Set list element by index
    Clear,          // §CLR - Clear collection
    Insert,         // §INS - Insert at index
    Has,            // §HAS - Contains check
    Key,            // §KEY - ContainsKey mode
    Val,            // §VAL - ContainsValue mode
    EachKV,         // §EACHKV - Dictionary iteration
    EndEachKV,      // §/EACHKV
    Count,          // §CNT - Collection .Count property

    // Phase 7: Generics
    Record,
    Where,

    // Phase 8: Classes, Interfaces, Inheritance
    Class,
    EndClass,
    Interface,
    EndInterface,
    Implements,
    Extends,
    Method,
    EndMethod,
    This,
    EndThis,
    Base,
    EndBase,
    New,
    EndNew,
    FieldDef,

    // Phase 9: Properties and Constructors
    Property,
    EndProperty,
    Indexer,        // §IXER - Indexer definition
    EndIndexer,     // §/IXER - End indexer definition
    Get,
    EndGet,
    Set,
    EndSet,
    Init,
    EndInit,
    Constructor,
    EndConstructor,
    OperatorOverload,       // op
    EndOperatorOverload,    // §/OP
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
    EndEvent,
    EventAdd,
    EndEventAdd,
    EventRemove,
    EndEventRemove,
    Subscribe,
    Unsubscribe,

    // Phase 12: Async/Await
    Await,
    AsyncFunc,          // §AF - Async function
    EndAsyncFunc,       // §/AF - End async function
    AsyncMethod,        // §AMT - Async method
    EndAsyncMethod,     // §/AMT - End async method

    // Phase 9: String Interpolation and Modern Operators
    Interpolate,
    EndInterpolate,
    NullCoalesce,
    NullConditional,
    RangeOp,            // ..
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
    TypePattern,        // §PTYPE - type-test / typed-declaration pattern (#774)
    Var,
    Rest,

    // Extended Features Phase 1: Quick Wins
    Example,            // §EX - Inline examples/tests

    // Extended Features Phase 2: Core Features
    Assume,             // §ASSUME - Assumptions

    // Extended Features Phase 3: Enhanced Contracts
    Since,              // §SINCE - API versioning
    Deprecated,         // §DEPRECATED - Deprecation markers
    Breaking,           // §BREAKING - Breaking change markers

    // Enums and Extensions
    Enum,               // §EN - Enum definition
    EndEnum,            // §/EN
    EnumExtension,      // §EEXT - Enum extension methods (note: §EXT is for class inheritance)
    EndEnumExtension,   // §/EEXT

    // Extended Features Phase 4: Future Extensions
    PropertyTest,       // §PROPERTY - Property-based testing (renamed to avoid conflict with Property)

    // Yield support
    Yield,              // yield value / yield break
    YieldBreak,         // §YBRK - yield break

    // LINQ Support
    AnonymousObject,    // §ANON - Anonymous object creation
    EndAnonymousObject, // §/ANON - End anonymous object

    // Raw C# Passthrough
    RawCSharp,          // §RAW ... §/RAW - Raw C# content emitted verbatim
    RawCSharpExpression,  // §CS{...} - Inline C# expression
    CSharpInterop,      // §CSHARP{...}§/CSHARP - C# interop block (member-level)
    CompilerDirective,  // raw directive:<feature> N - validated nonconditional C# directive

    // Preprocessor Directives
    Preprocessor,       // pp; Value is exact physical-line remainder, excluding CR/LF
    EndPreprocessor,    // §/PP{CONDITION} - preprocessor conditional end
    PreprocessorElse,   // §PPE - preprocessor else

    // Unsafe/Low-Level
    StackAlloc,         // §SALLOC - stackalloc expression
    EndStackAlloc,      // §/SALLOC - end stackalloc initializer
    Unsafe,             // §UNSAFE - unsafe block
    EndUnsafe,          // §/UNSAFE - end unsafe block
    Fixed,              // §FIXED - fixed statement
    EndFixed,           // §/FIXED - end fixed block
    AddressOf,          // §ADDR - address-of (&)
    Deref,              // §DEREF - pointer dereference (*)
    SizeOf,             // §SIZEOF - sizeof expression

    // Multidimensional Arrays
    Array2D,            // §ARR2D - multidimensional array creation
    EndArray2D,         // §/ARR2D - end multidimensional array
    Index2D,            // §IDX2D - multidimensional array access
    Row,                // §ROW - row separator in array initializer

    // Dependent Types: Refinement Types and Proof Obligations
    RefinedType,        // §RTYPE - refinement type definition
    EndRefinedType,     // §/RTYPE - end refinement type
    Proof,              // §PROOF - proof obligation
    IndexedType,        // §ITYPE - indexed type definition (size-parameterized)
    EndIndexedType,     // §/ITYPE - end indexed type

    // Synchronization
    SyncBlock,          // §SYNC - lock statement
    EndSyncBlock,       // §/SYNC - end lock statement

    // Namespace topology
    Namespace,          // §NS{id:name} - lexical namespace scope
    EndNamespace,       // §/NS{id}

    // Typed Literals
    IntLiteral,         // INT:42
    StrLiteral,         // STR:"hello"
    CharLiteral,        // 'x'
    BoolLiteral,        // BOOL:true
    FloatLiteral,       // FLOAT:3.14
    DecimalLiteral,     // DECIMAL:18.00M or DEC:18.00 or 18.00m

    // Identifiers and values
    Identifier,

    // Special
    Newline,
    Whitespace,

    // Phase 3 (RFC §4.1) — indentation tokens emitted by the lexer's
    // indent post-pass when run via TokenizeWithIndent(). The plain
    // Tokenize() entry point does NOT emit these (Phase 1 compatibility).
    Indent,
    Dedent,

    Eof,
    Error
}

/// <summary>
/// Represents a single token from the Calor source.
/// </summary>
public readonly struct Token : IEquatable<Token>
{
    public TokenKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public object? Value { get; }
    public string? DocComment { get; init; }
    internal int IndentationDepth { get; init; }

    public Token(TokenKind kind, string text, TextSpan span, object? value = null)
    {
        Kind = kind;
        Text = text;
        Span = span;
        Value = value;
    }

    public bool IsKeyword => Kind is >= TokenKind.Module and <= TokenKind.EndNamespace
        && Kind != TokenKind.RangeOp;

    public bool IsLiteral => Kind is TokenKind.IntLiteral or TokenKind.StrLiteral or TokenKind.CharLiteral
        or TokenKind.BoolLiteral or TokenKind.FloatLiteral or TokenKind.DecimalLiteral;

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

public enum IntegerLiteralSign
{
    Positive,
    Negative
}

public enum IntegerLiteralBase
{
    Decimal,
    Hexadecimal
}

public enum IntegerLiteralWidth
{
    Bits32,
    Bits64
}

public enum IntegerLiteralSignedness
{
    Signed,
    Unsigned
}

/// <summary>
/// Lossless integer literal metadata. The magnitude is kept separate from sign
/// so values such as <see cref="long.MinValue"/> and negative hexadecimal
/// literals never lose their source semantics while crossing the lexer/parser
/// boundary.
/// </summary>
internal readonly record struct IntLiteralInfo(
    ulong Magnitude,
    IntegerLiteralSign Sign,
    IntegerLiteralBase Base,
    IntegerLiteralWidth Width,
    IntegerLiteralSignedness Signedness,
    bool WidthInferred = false)
{
    public bool IsNegative => Sign == IntegerLiteralSign.Negative;
    public bool IsHex => Base == IntegerLiteralBase.Hexadecimal;
    public bool IsUnsigned => Signedness == IntegerLiteralSignedness.Unsigned;
    public bool IsLong => Width == IntegerLiteralWidth.Bits64;
    public ulong UnsignedValue => Magnitude;
    public long SignedValue => IsNegative
        ? Magnitude == 0x8000_0000_0000_0000UL
            ? long.MinValue
            : -checked((long)Magnitude)
        : unchecked((long)Magnitude);
}

/// <summary>
/// Metadata for floating-point literal tokens that carry single-vs-double width
/// (#774 faithful preservation). Stored in <see cref="Token.Value"/> for SINGLE: literals.
/// </summary>
internal readonly record struct FloatLiteralInfo(double Value, bool IsSingle);

internal sealed record RawCSharpFrame(
    string Code,
    string? FeatureName = null,
    string? Reason = null,
    Ast.InteropMemberKind MemberKind = Ast.InteropMemberKind.Other,
    bool IsCompilationUnitPassthrough = false,
    bool IsCompilerDirective = false)
{
    private static readonly System.Text.UTF8Encoding StrictMetadataUtf8 = new(false, true);

    internal static string NormalizeLineEndings(string source)
    {
        if (!source.Contains("\r\n", StringComparison.Ordinal))
            return source;
        var result = new System.Text.StringBuilder(source.Length);
        var cursor = 0;
        foreach (var token in ReadTokens(source))
        {
            if (token.Value is not RawCSharpFrame frame)
                continue;
            var payloadStart = token.Span.End - frame.Code.Length;
            result.Append(source[cursor..payloadStart].Replace("\r\n", "\n", StringComparison.Ordinal));
            result.Append(frame.Code);
            cursor = token.Span.End;
        }
        result.Append(source[cursor..].Replace("\r\n", "\n", StringComparison.Ordinal));
        return result.ToString();
    }

    internal static string MaskForAnalysis(string source)
    {
        var characters = source.ToCharArray();
        var tokens = new Lexer(source, new Diagnostics.DiagnosticBag()).TokenizeAll();
        foreach (var token in tokens)
            if (token.Value is RawCSharpFrame)
                for (var i = token.Span.Start; i < token.Span.End; i++)
                    if (characters[i] is not '\r' and not '\n')
                        characters[i] = ' ';
        return new string(characters);
    }

    internal static Token? FindAtOffset(string source, int offset)
    {
        var tokens = new Lexer(source, new Diagnostics.DiagnosticBag()).TokenizeAll();
        foreach (var token in tokens)
            if ((token.Value is RawCSharpFrame
                    || token.Kind == TokenKind.Error
                        && (token.Text == "raw"
                            || token.Text.StartsWith("raw ", StringComparison.Ordinal)
                            || token.Text.StartsWith("raw\t", StringComparison.Ordinal)))
                && offset >= token.Span.Start
                && (token.Kind == TokenKind.Error || offset < token.Span.End
                    || offset == token.Span.End && token.Text[^1] is not '\r' and not '\n'))
                return token;
        return null;
    }

    internal static List<Token> ReadTokens(string source)
    {
        var diagnostics = new Diagnostics.DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();
        var frameErrors = diagnostics.Errors
            .Where(d => d.Code == Diagnostics.DiagnosticCode.InvalidRawFrame).ToList();
        if (frameErrors.Count > 0)
            throw new FormatException(string.Join("\n", frameErrors.Select(d => d.Message)));
        return tokens;
    }

    internal static string RewriteOutsideFrames(string source, Func<string, string> rewrite)
    {
        return RewriteOutsideTokens(source,
            ReadTokens(source).Where(t => t.Value is RawCSharpFrame).ToList(), rewrite);
    }

    internal static string RewriteOutsideTokens(string source, IReadOnlyList<Token> tokens, Func<string, string> rewrite)
    {
        if (tokens.Count == 0)
            return rewrite(source);
        var prefix = "__CALOR_OPAQUE_FRAME_";
        while (source.Contains(prefix, StringComparison.Ordinal))
            prefix += "_";
        var masked = new System.Text.StringBuilder();
        var replacements = new List<(string Marker, string Text, bool EndsLine)>();
        var cursor = 0;
        foreach (var token in tokens)
        {
            var marker = prefix + replacements.Count + "__";
            var endsLine = token.Text[^1] is '\r' or '\n';
            masked.Append(source, cursor, token.Span.Start - cursor);
            masked.Append(marker);
            if (endsLine)
                masked.Append('\n');
            replacements.Add((marker, token.Text, endsLine));
            cursor = token.Span.End;
        }
        masked.Append(source, cursor, source.Length - cursor);
        var result = rewrite(masked.ToString());
        foreach (var (marker, text, endsLine) in replacements)
        {
            if (!result.Contains(marker, StringComparison.Ordinal))
                throw new InvalidOperationException("Source rewrite removed an opaque raw frame boundary.");
            result = result.Replace(
                endsLine && result.Contains(marker + "\n", StringComparison.Ordinal)
                    ? marker + "\n" : marker, text, StringComparison.Ordinal);
        }
        return result;
    }

    internal string ReasonMarker
    {
        get
        {
            if (IsCompilerDirective)
                return "directive:" + Uri.EscapeDataString(FeatureName!);
            if (!IsCompilationUnitPassthrough
                && Reason == null && MemberKind == Ast.InteropMemberKind.Other)
            {
                if (FeatureName == null)
                    return "member";
                var compact = Uri.EscapeDataString(FeatureName);
                if (compact.Length > 0 && compact is not ("statement" or "member" or "unit"))
                    return compact;
            }
            return $"{(IsCompilationUnitPassthrough ? "unit" : "member")}:" +
                $"{Encode(FeatureName)}:{MemberKind}:{Encode(Reason)}";
        }
    }

    private static string Encode(string? value) =>
        value == null ? "~" : Uri.EscapeDataString(value).Replace("~", "%7E", StringComparison.Ordinal);

    internal static void ValidateInteropReasons(
        IReadOnlyList<Token> tokens, Diagnostics.DiagnosticBag diagnostics)
    {
        foreach (var token in tokens)
        {
            if (token.Value is RawCSharpFrame { IsCompilerDirective: true }
                || token.Kind is not (TokenKind.RawCSharp or TokenKind.CSharpInterop))
                continue;
            if (token.Value is not RawCSharpFrame frame
                || string.IsNullOrWhiteSpace(frame.FeatureName) || string.IsNullOrWhiteSpace(frame.Reason))
                diagnostics.ReportError(token.Span, Diagnostics.DiagnosticCode.InvalidCSharpInteropReason,
                    "Raw C# passthrough requires nonempty feature and reason metadata. "
                    + "Write native Calor instead, or run calor convert on the original C# and retain its marked fallback. "
                    + "A marker alone does not establish converter origin or reason applicability.");
        }
    }

    private static bool TryDecodeMetadata(string encoded, out string? decoded)
    {
        decoded = null;
        if (encoded == "~")
            return true;
        for (var index = 0; index < encoded.Length; index++)
        {
            if (encoded[index] != '%')
                continue;
            var bytes = new List<byte>();
            while (index < encoded.Length && encoded[index] == '%')
            {
                if (index + 2 >= encoded.Length
                    || !byte.TryParse(encoded.AsSpan(index + 1, 2),
                        System.Globalization.NumberStyles.AllowHexSpecifier,
                        System.Globalization.CultureInfo.InvariantCulture, out var value))
                    return false;
                bytes.Add(value);
                index += 3;
            }
            try
            {
                _ = StrictMetadataUtf8.GetCharCount(bytes.ToArray());
            }
            catch (System.Text.DecoderFallbackException)
            {
                return false;
            }
            index--;
        }
        decoded = Uri.UnescapeDataString(encoded);
        try
        {
            _ = StrictMetadataUtf8.GetByteCount(decoded);
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
        return true;
    }

    internal static bool TryParseReason(string marker, out RawCSharpFrame frame)
    {
        frame = new RawCSharpFrame("");
        var parts = marker.Split(':');
        if (parts[0] == "directive" && parts.Length > 1)
        {
            if (parts.Length != 2)
                return false;
            var feature = Uri.UnescapeDataString(parts[1]);
            if (!IsCompilerDirectiveFeature(feature)
                || Uri.EscapeDataString(feature) != parts[1])
                return false;
            frame = frame with { FeatureName = feature, IsCompilerDirective = true };
            return true;
        }
        if (parts.Length == 1)
        {
            if (marker is "statement" or "member")
                return true;
            if (marker == "unit")
            {
                frame = frame with { IsCompilationUnitPassthrough = true };
                return true;
            }
            if (!TryDecodeMetadata(marker, out var compactFeature))
                return false;
            frame = frame with { FeatureName = compactFeature };
            return true;
        }
        if (parts.Length != 4 || parts[0] is not ("member" or "unit")
            || !Enum.TryParse<Ast.InteropMemberKind>(parts[2], out var kind)
            || !Enum.IsDefined(kind)
            || !TryDecodeMetadata(parts[1], out var interopFeature)
            || !TryDecodeMetadata(parts[3], out var reason))
            return false;
        frame = frame with
        {
            FeatureName = interopFeature,
            MemberKind = kind,
            Reason = reason,
            IsCompilationUnitPassthrough = parts[0] == "unit"
        };
        return true;
    }

    internal static string? GetCompilerDirectiveFeature(DirectiveTriviaSyntax directive) => directive switch
    {
        NullableDirectiveTriviaSyntax => "nullable-directive",
        PragmaWarningDirectiveTriviaSyntax or PragmaChecksumDirectiveTriviaSyntax => "pragma",
        WarningDirectiveTriviaSyntax => "warning-directive",
        ErrorDirectiveTriviaSyntax => "error-directive",
        LineDirectiveTriviaSyntax or LineSpanDirectiveTriviaSyntax => "line-directive",
        DefineDirectiveTriviaSyntax or UndefDirectiveTriviaSyntax => "symbol-directive",
        _ => null
    };

    private static bool IsCompilerDirectiveFeature(string feature) =>
        feature is "nullable-directive" or "pragma" or "warning-directive"
            or "error-directive" or "line-directive" or "symbol-directive";

    internal bool TryValidateCompilerDirective(out string error)
    {
        error = "A directive frame must contain only complete nonconditional C# directives matching its declared feature and comments or whitespace.";
        if (!IsCompilerDirective || FeatureName == null || !IsCompilerDirectiveFeature(FeatureName))
            return false;
        var root = SyntaxFactory.ParseCompilationUnit(Code);
        if (root.Members.Count != 0 || root.Usings.Count != 0
            || root.Externs.Count != 0 || root.AttributeLists.Count != 0
            || root.GetDiagnostics().Any(diagnostic => diagnostic.Id is not ("CS1029" or "CS1030")))
            return false;
        var directives = 0;
        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.GetStructure() is DirectiveTriviaSyntax directive)
            {
                if (GetCompilerDirectiveFeature(directive) != FeatureName)
                    return false;
                directives++;
            }
            else if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                && !trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                return false;
        }
        return directives > 0;
    }
}

internal abstract record InterpolatedStringTokenPart;

internal sealed record InterpolatedStringTextTokenPart(string Text)
    : InterpolatedStringTokenPart;

public enum InterpolationPartIntent
{
    Expression,
    LiteralPlaceholder
}

/// <summary>
/// A <c>${...}</c> hole inside a Calor string literal. <paramref name="Start"/>,
/// <paramref name="Line"/> and <paramref name="Column"/> locate the first character of
/// the hole's expression text in the enclosing source (0.25 F2, #906), so diagnostics
/// raised while the hole is reparsed point at the hole rather than at line 1.
/// </summary>
internal sealed record InterpolatedStringExpressionTokenPart(
    string ExpressionText,
    InterpolationPartIntent Intent,
    int Start = 0,
    int Line = 1,
    int Column = 1)
    : InterpolatedStringTokenPart;

internal sealed record StringLiteralInfo(
    string Value,
    IReadOnlyList<InterpolatedStringTokenPart> Parts,
    bool IsMultiline,
    bool IsUtf8);
