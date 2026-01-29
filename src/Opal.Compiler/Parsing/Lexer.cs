using Opal.Compiler.Diagnostics;

namespace Opal.Compiler.Parsing;

/// <summary>
/// Tokenizes OPAL source code.
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private readonly DiagnosticBag _diagnostics;

    private int _position;
    private int _line = 1;
    private int _column = 1;
    private int _tokenStart;
    private int _tokenLine;
    private int _tokenColumn;

    private static readonly Dictionary<string, TokenKind> Keywords = new(StringComparer.Ordinal)
    {
        // v1 verbose keywords
        ["MODULE"] = TokenKind.Module,
        ["END_MODULE"] = TokenKind.EndModule,
        ["FUNC"] = TokenKind.Func,
        ["END_FUNC"] = TokenKind.EndFunc,
        ["IN"] = TokenKind.In,
        ["OUT"] = TokenKind.Out,
        ["EFFECTS"] = TokenKind.Effects,
        ["BODY"] = TokenKind.Body,
        ["END_BODY"] = TokenKind.EndBody,
        ["CALL"] = TokenKind.Call,
        ["END_CALL"] = TokenKind.EndCall,
        ["ARG"] = TokenKind.Arg,
        ["RETURN"] = TokenKind.Return,
        // Phase 2: Control Flow
        ["FOR"] = TokenKind.For,
        ["END_FOR"] = TokenKind.EndFor,
        ["IF"] = TokenKind.If,
        ["END_IF"] = TokenKind.EndIf,
        ["ELSE"] = TokenKind.Else,
        ["ELSEIF"] = TokenKind.ElseIf,
        ["WHILE"] = TokenKind.While,
        ["END_WHILE"] = TokenKind.EndWhile,
        ["BIND"] = TokenKind.Bind,
        ["OP"] = TokenKind.Op,
        ["REF"] = TokenKind.Ref,
        // Phase 3: Type System
        ["TYPE"] = TokenKind.Type,
        ["END_TYPE"] = TokenKind.EndType,
        ["RECORD"] = TokenKind.Record,
        ["END_RECORD"] = TokenKind.EndRecord,
        ["FIELD"] = TokenKind.Field,
        ["MATCH"] = TokenKind.Match,
        ["END_MATCH"] = TokenKind.EndMatch,
        ["CASE"] = TokenKind.Case,
        ["SOME"] = TokenKind.Some,
        ["NONE"] = TokenKind.None,
        ["OK"] = TokenKind.Ok,
        ["ERR"] = TokenKind.Err,
        ["VARIANT"] = TokenKind.Variant,
        // Phase 4: Contracts and Effects
        ["REQUIRES"] = TokenKind.Requires,
        ["ENSURES"] = TokenKind.Ensures,
        ["INVARIANT"] = TokenKind.Invariant,
        // Phase 5: Using Statements
        ["USING"] = TokenKind.Using,
        // Phase 6: Arrays and Collections
        ["ARR"] = TokenKind.Array,
        ["END_ARR"] = TokenKind.EndArray,
        ["IDX"] = TokenKind.Index,
        ["LEN"] = TokenKind.Length,
        ["EACH"] = TokenKind.Foreach,
        ["END_EACH"] = TokenKind.EndForeach,
        // Phase 7: Generics
        ["TP"] = TokenKind.TypeParam,
        ["WHERE"] = TokenKind.Where,
        ["G"] = TokenKind.Generic,
        // Phase 8: Classes, Interfaces, Inheritance
        ["CLASS"] = TokenKind.Class,
        ["END_CLASS"] = TokenKind.EndClass,
        ["IFACE"] = TokenKind.Interface,
        ["END_IFACE"] = TokenKind.EndInterface,
        ["IMPL"] = TokenKind.Implements,
        ["EXT"] = TokenKind.Extends,
        ["METHOD"] = TokenKind.Method,
        ["END_METHOD"] = TokenKind.EndMethod,
        ["VIRTUAL"] = TokenKind.Virtual,
        ["OVERRIDE"] = TokenKind.Override,
        ["ABSTRACT"] = TokenKind.Abstract,
        ["SEALED"] = TokenKind.Sealed,
        ["THIS"] = TokenKind.This,
        ["BASE"] = TokenKind.Base,
        ["NEW"] = TokenKind.New,
        ["FLD"] = TokenKind.FieldDef,
        // Phase 9: Properties and Constructors
        ["PROP"] = TokenKind.Property,
        ["END_PROP"] = TokenKind.EndProperty,
        ["GET"] = TokenKind.Get,
        ["SET"] = TokenKind.Set,
        ["INIT"] = TokenKind.Init,
        ["CTOR"] = TokenKind.Constructor,
        ["END_CTOR"] = TokenKind.EndConstructor,
        ["ASSIGN"] = TokenKind.Assign,
        // Phase 10: Try/Catch/Finally
        ["TRY"] = TokenKind.Try,
        ["END_TRY"] = TokenKind.EndTry,
        ["CATCH"] = TokenKind.Catch,
        ["FINALLY"] = TokenKind.Finally,
        ["THROW"] = TokenKind.Throw,
        ["RETHROW"] = TokenKind.Rethrow,
        ["WHEN"] = TokenKind.When,
        // Phase 11: Lambdas, Delegates, Events
        ["LAM"] = TokenKind.Lambda,
        ["END_LAM"] = TokenKind.EndLambda,
        ["DEL"] = TokenKind.Delegate,
        ["END_DEL"] = TokenKind.EndDelegate,
        ["EVT"] = TokenKind.Event,
        ["SUB"] = TokenKind.Subscribe,
        ["UNSUB"] = TokenKind.Unsubscribe,
        // Phase 12: Async/Await
        ["ASYNC"] = TokenKind.Async,
        ["AWAIT"] = TokenKind.Await,
        // Phase 9: String Interpolation and Modern Operators
        ["INTERP"] = TokenKind.Interpolate,
        ["/INTERP"] = TokenKind.EndInterpolate,
        ["??"] = TokenKind.NullCoalesce,
        ["?."] = TokenKind.NullConditional,
        ["RANGE"] = TokenKind.RangeOp,
        ["^"] = TokenKind.IndexEnd,
        ["EXP"] = TokenKind.Expression,
        // Phase 10: Advanced Patterns
        ["WITH"] = TokenKind.With,
        ["/WITH"] = TokenKind.EndWith,
        ["PPOS"] = TokenKind.PositionalPattern,
        ["PPROP"] = TokenKind.PropertyPattern,
        ["PMATCH"] = TokenKind.PropertyMatch,
        ["PREL"] = TokenKind.RelationalPattern,
        ["PLIST"] = TokenKind.ListPattern,
        ["VAR"] = TokenKind.Var,
        ["REST"] = TokenKind.Rest,

        // v2 single-letter keywords (compact syntax)
        ["M"] = TokenKind.Module,           // §M = §MODULE
        ["F"] = TokenKind.Func,             // §F = §FUNC
        ["C"] = TokenKind.Call,             // §C = §CALL
        ["B"] = TokenKind.Bind,             // §B = §BIND
        ["R"] = TokenKind.Return,           // §R = §RETURN
        ["I"] = TokenKind.In,               // §I = §IN (input parameter)
        ["O"] = TokenKind.Out,              // §O = §OUT
        ["A"] = TokenKind.Arg,              // §A = §ARG
        ["E"] = TokenKind.Effects,          // §E = §EFFECTS (also used for else in context)
        ["L"] = TokenKind.For,              // §L = §LOOP (maps to FOR)
        ["W"] = TokenKind.Match,            // §W = §MATCH (sWitch)
        ["K"] = TokenKind.Case,             // §K = §CASE
        ["Q"] = TokenKind.Requires,         // §Q = §REQUIRES (preCondition)
        ["S"] = TokenKind.Ensures,          // §S = §ENSURES (poStcondition)
        ["T"] = TokenKind.Type,             // §T = §TYPE
        ["D"] = TokenKind.Record,           // §D = §RECORD (Data)
        ["V"] = TokenKind.Variant,          // §V = §VARIANT
        ["U"] = TokenKind.Using,            // §U = §USING
        // Phase 6: v2 closing tags for arrays
        ["/ARR"] = TokenKind.EndArray,      // §/ARR = §END_ARR
        ["/EACH"] = TokenKind.EndForeach,   // §/EACH = §END_EACH
        // Phase 8: v2 closing tags for classes
        ["/CLASS"] = TokenKind.EndClass,    // §/CLASS = §END_CLASS
        ["/IFACE"] = TokenKind.EndInterface, // §/IFACE = §END_IFACE
        ["/METHOD"] = TokenKind.EndMethod,  // §/METHOD = §END_METHOD
        // Phase 9: v2 closing tags for properties/constructors
        ["/PROP"] = TokenKind.EndProperty,  // §/PROP = §END_PROP
        ["/CTOR"] = TokenKind.EndConstructor, // §/CTOR = §END_CTOR
        // Phase 10: v2 closing tags for try/catch
        ["/TRY"] = TokenKind.EndTry,        // §/TRY = §END_TRY
        // Phase 11: v2 closing tags for lambdas/delegates
        ["/LAM"] = TokenKind.EndLambda,     // §/LAM = §END_LAM
        ["/DEL"] = TokenKind.EndDelegate,   // §/DEL = §END_DEL

        // v2 closing tags (§/X pattern)
        ["/M"] = TokenKind.EndModule,       // §/M = §END_MODULE
        ["/F"] = TokenKind.EndFunc,         // §/F = §END_FUNC
        ["/C"] = TokenKind.EndCall,         // §/C = §END_CALL
        ["/I"] = TokenKind.EndIf,           // §/I = §END_IF
        ["/L"] = TokenKind.EndFor,          // §/L = §END_LOOP
        ["/W"] = TokenKind.EndMatch,        // §/W = §END_MATCH
        ["/T"] = TokenKind.EndType,         // §/T = §END_TYPE
        ["/D"] = TokenKind.EndRecord,       // §/D = §END_RECORD
    };

    public Lexer(string source, DiagnosticBag diagnostics)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private char Current => Peek(0);
    private char Lookahead => Peek(1);

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _source.Length ? _source[index] : '\0';
    }

    private bool IsAtEnd => _position >= _source.Length;

    private void Advance()
    {
        if (!IsAtEnd)
        {
            if (Current == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _position++;
        }
    }

    private void StartToken()
    {
        _tokenStart = _position;
        _tokenLine = _line;
        _tokenColumn = _column;
    }

    private TextSpan CurrentSpan()
        => new(_tokenStart, _position - _tokenStart, _tokenLine, _tokenColumn);

    private string CurrentText()
        => _source[_tokenStart.._position];

    private Token MakeToken(TokenKind kind, object? value = null)
        => new(kind, CurrentText(), CurrentSpan(), value);

    public IEnumerable<Token> Tokenize()
    {
        while (!IsAtEnd)
        {
            var token = NextToken();
            if (token.Kind != TokenKind.Whitespace && token.Kind != TokenKind.Newline)
            {
                yield return token;
            }
        }

        StartToken();
        yield return MakeToken(TokenKind.Eof);
    }

    public List<Token> TokenizeAll()
        => Tokenize().ToList();

    private Token NextToken()
    {
        StartToken();

        return Current switch
        {
            '\0' => MakeToken(TokenKind.Eof),
            '§' => ScanSectionMarker(),
            '[' => ScanSingle(TokenKind.OpenBracket),
            ']' => ScanSingle(TokenKind.CloseBracket),
            '=' => ScanSingle(TokenKind.Equals),
            ':' => ScanColonOrTypedLiteral(),
            '!' => ScanSingle(TokenKind.Exclamation),
            '~' => ScanSingle(TokenKind.Tilde),
            '#' => ScanSingle(TokenKind.Hash),
            '?' => ScanSingle(TokenKind.Question),
            '"' => ScanStringLiteral(),
            '\r' or '\n' => ScanNewline(),
            ' ' or '\t' => ScanWhitespace(),
            _ when char.IsLetter(Current) || Current == '_' => ScanIdentifierOrTypedLiteral(),
            _ when char.IsDigit(Current) || (Current == '-' && char.IsDigit(Lookahead)) => ScanNumber(),
            _ => ScanError()
        };
    }

    private Token ScanSingle(TokenKind kind)
    {
        Advance();
        return MakeToken(kind);
    }

    private Token ScanColonOrTypedLiteral()
    {
        // Standalone colon (v2 syntax for positional attributes)
        Advance();
        return MakeToken(TokenKind.Colon);
    }

    private Token ScanSectionMarker()
    {
        Advance(); // consume §

        // Check for v2 closing tag pattern: §/X
        if (Current == '/')
        {
            Advance(); // consume '/'

            // Read the closing tag letter(s)
            while (char.IsLetterOrDigit(Current) || Current == '_')
            {
                Advance();
            }

            var text = CurrentText();
            var keyword = text.Length > 2 ? text[1..] : ""; // includes the /

            if (Keywords.TryGetValue(keyword, out var kind))
            {
                return MakeToken(kind);
            }

            // Unknown closing tag
            _diagnostics.ReportUnexpectedCharacter(CurrentSpan(), '/');
            return MakeToken(TokenKind.Error);
        }

        // Read the keyword that follows
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            Advance();
        }

        var fullText = CurrentText();
        var fullKeyword = fullText.Length > 1 ? fullText[1..] : "";

        if (Keywords.TryGetValue(fullKeyword, out var keywordKind))
        {
            return MakeToken(keywordKind);
        }

        // Unknown section keyword - report error but return as identifier
        _diagnostics.ReportUnexpectedCharacter(CurrentSpan(), '§');
        return MakeToken(TokenKind.Error);
    }

    private Token ScanIdentifierOrTypedLiteral()
    {
        while (char.IsLetterOrDigit(Current) || Current == '_' || Current == '.')
        {
            Advance();
        }

        var text = CurrentText();

        // Check for typed literals
        if (Current == ':')
        {
            return text.ToUpperInvariant() switch
            {
                "INT" => ScanTypedIntLiteral(),
                "STR" => ScanTypedStringLiteral(),
                "BOOL" => ScanTypedBoolLiteral(),
                "FLOAT" => ScanTypedFloatLiteral(),
                _ => MakeToken(TokenKind.Identifier)
            };
        }

        // v2: Support bare boolean literals
        if (text == "true")
        {
            return MakeToken(TokenKind.BoolLiteral, true);
        }
        if (text == "false")
        {
            return MakeToken(TokenKind.BoolLiteral, false);
        }

        return MakeToken(TokenKind.Identifier);
    }

    private Token ScanTypedIntLiteral()
    {
        Advance(); // consume ':'
        var valueStart = _position;

        if (Current == '-')
        {
            Advance();
        }

        while (char.IsDigit(Current))
        {
            Advance();
        }

        var valueText = _source[valueStart.._position];
        if (int.TryParse(valueText, out var value))
        {
            return MakeToken(TokenKind.IntLiteral, value);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "INT");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanTypedStringLiteral()
    {
        Advance(); // consume ':'

        if (Current != '"')
        {
            _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "STR");
            return MakeToken(TokenKind.Error);
        }

        return ScanStringLiteralValue();
    }

    private Token ScanTypedBoolLiteral()
    {
        Advance(); // consume ':'
        var valueStart = _position;

        while (char.IsLetter(Current))
        {
            Advance();
        }

        var valueText = _source[valueStart.._position].ToLowerInvariant();
        if (valueText == "true")
        {
            return MakeToken(TokenKind.BoolLiteral, true);
        }
        if (valueText == "false")
        {
            return MakeToken(TokenKind.BoolLiteral, false);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "BOOL");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanTypedFloatLiteral()
    {
        Advance(); // consume ':'
        var valueStart = _position;

        if (Current == '-')
        {
            Advance();
        }

        while (char.IsDigit(Current))
        {
            Advance();
        }

        if (Current == '.')
        {
            Advance();
            while (char.IsDigit(Current))
            {
                Advance();
            }
        }

        // Handle scientific notation
        if (Current is 'e' or 'E')
        {
            Advance();
            if (Current is '+' or '-')
            {
                Advance();
            }
            while (char.IsDigit(Current))
            {
                Advance();
            }
        }

        var valueText = _source[valueStart.._position];
        if (double.TryParse(valueText, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return MakeToken(TokenKind.FloatLiteral, value);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "FLOAT");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanStringLiteral()
    {
        return ScanStringLiteralValue();
    }

    private Token ScanStringLiteralValue()
    {
        Advance(); // consume opening quote

        var sb = new System.Text.StringBuilder();
        while (!IsAtEnd && Current != '"')
        {
            if (Current == '\\')
            {
                Advance();
                var escaped = Current switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => '\0'
                };

                if (escaped == '\0')
                {
                    _diagnostics.ReportInvalidEscapeSequence(CurrentSpan(), Current);
                }
                else
                {
                    sb.Append(escaped);
                }
                Advance();
            }
            else if (Current == '\n')
            {
                _diagnostics.ReportUnterminatedString(CurrentSpan());
                return MakeToken(TokenKind.Error);
            }
            else
            {
                sb.Append(Current);
                Advance();
            }
        }

        if (IsAtEnd)
        {
            _diagnostics.ReportUnterminatedString(CurrentSpan());
            return MakeToken(TokenKind.Error);
        }

        Advance(); // consume closing quote
        return MakeToken(TokenKind.StrLiteral, sb.ToString());
    }

    private Token ScanNumber()
    {
        if (Current == '-')
        {
            Advance();
        }

        while (char.IsDigit(Current))
        {
            Advance();
        }

        // Check for float
        if (Current == '.' && char.IsDigit(Lookahead))
        {
            Advance(); // consume '.'
            while (char.IsDigit(Current))
            {
                Advance();
            }

            var floatText = CurrentText();
            if (double.TryParse(floatText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floatValue))
            {
                return MakeToken(TokenKind.FloatLiteral, floatValue);
            }
        }

        var intText = CurrentText();
        if (int.TryParse(intText, out var intValue))
        {
            return MakeToken(TokenKind.IntLiteral, intValue);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "number");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanWhitespace()
    {
        while (Current is ' ' or '\t')
        {
            Advance();
        }
        return MakeToken(TokenKind.Whitespace);
    }

    private Token ScanNewline()
    {
        if (Current == '\r' && Lookahead == '\n')
        {
            Advance();
        }
        Advance();
        return MakeToken(TokenKind.Newline);
    }

    private Token ScanError()
    {
        _diagnostics.ReportUnexpectedCharacter(CurrentSpan(), Current);
        Advance();
        return MakeToken(TokenKind.Error);
    }
}
