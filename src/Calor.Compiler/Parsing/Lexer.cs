using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Parsing;

/// <summary>
/// Tokenizes Calor source code.
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
        // Single-letter keywords (compact syntax)
        ["M"] = TokenKind.Module,           // §M = Module
        ["F"] = TokenKind.Func,             // §F = Function
        ["C"] = TokenKind.Call,             // §C = Call
        ["B"] = TokenKind.Bind,             // §B = Bind
        ["R"] = TokenKind.Return,           // §R = Return
        ["I"] = TokenKind.In,               // §I = Input parameter
        ["O"] = TokenKind.Out,              // §O = Output
        ["A"] = TokenKind.Arg,              // §A = Argument
        ["E"] = TokenKind.Effects,          // §E = Effects
        ["L"] = TokenKind.For,              // §L = Loop
        ["W"] = TokenKind.Match,            // §W = Match (sWitch)
        ["K"] = TokenKind.Case,             // §K = Case
        ["Q"] = TokenKind.Requires,         // §Q = Requires (preCondition)
        ["S"] = TokenKind.Ensures,          // §S = Ensures (poStcondition)
        ["T"] = TokenKind.Type,             // §T = Type
        ["D"] = TokenKind.Record,           // §D = Record (Data)
        ["V"] = TokenKind.Variant,          // §V = Variant
        ["U"] = TokenKind.Using,            // §U = Using

        // Closing tags (§/X pattern)
        ["/M"] = TokenKind.EndModule,       // §/M
        ["/F"] = TokenKind.EndFunc,         // §/F
        ["/C"] = TokenKind.EndCall,         // §/C
        ["/I"] = TokenKind.EndIf,           // §/I
        ["/L"] = TokenKind.EndFor,          // §/L
        ["/W"] = TokenKind.EndMatch,        // §/W
        ["/K"] = TokenKind.EndCase,         // §/K - closing case tag
        ["/T"] = TokenKind.EndType,         // §/T
        ["/D"] = TokenKind.EndRecord,       // §/D

        // Control flow keywords
        ["IF"] = TokenKind.If,              // §IF = explicit if
        ["EI"] = TokenKind.ElseIf,          // §EI = ElseIf
        ["EL"] = TokenKind.Else,            // §EL = Else
        ["WH"] = TokenKind.While,           // §WH = While
        ["/WH"] = TokenKind.EndWhile,       // §/WH
        ["DO"] = TokenKind.Do,              // §DO = Do (do-while loop)
        ["/DO"] = TokenKind.EndDo,          // §/DO
        ["SW"] = TokenKind.Match,           // §SW = Switch/Match (alias)
        ["/SW"] = TokenKind.EndMatch,       // §/SW
        ["BK"] = TokenKind.Break,           // §BK = Break
        ["CN"] = TokenKind.Continue,        // §CN = Continue
        ["BODY"] = TokenKind.Body,          // §BODY - explicit body start (optional)
        ["END_BODY"] = TokenKind.EndBody,   // §END_BODY - explicit body end (optional)

        // Type system - Option/Result patterns
        ["SM"] = TokenKind.Some,            // §SM = Some
        ["NN"] = TokenKind.None,            // §NN = None
        ["OK"] = TokenKind.Ok,              // §OK = Ok (already short)
        ["ERR"] = TokenKind.Err,            // §ERR = Err (already short)
        ["FL"] = TokenKind.Field,           // §FL = Field
        ["IV"] = TokenKind.Invariant,       // §IV = Invariant

        // Using statement (block form)
        ["USE"] = TokenKind.Use,            // §USE = using statement open
        ["/USE"] = TokenKind.EndUse,        // §/USE = using statement close

        // Arrays and Collections
        ["ARR"] = TokenKind.Array,
        ["/ARR"] = TokenKind.EndArray,
        ["IDX"] = TokenKind.Index,
        ["LEN"] = TokenKind.Length,
        ["EACH"] = TokenKind.Foreach,
        ["/EACH"] = TokenKind.EndForeach,

        // Collections (List, Dictionary, HashSet)
        ["LIST"] = TokenKind.List,
        ["/LIST"] = TokenKind.EndList,
        ["DICT"] = TokenKind.Dict,
        ["/DICT"] = TokenKind.EndDict,
        ["HSET"] = TokenKind.HashSet,
        ["/HSET"] = TokenKind.EndHashSet,
        ["KV"] = TokenKind.KeyValue,
        ["PUSH"] = TokenKind.Push,
        ["ADD"] = TokenKind.Add,
        ["PUT"] = TokenKind.Put,
        ["REM"] = TokenKind.Remove,
        ["SETIDX"] = TokenKind.SetIndex,
        ["CLR"] = TokenKind.Clear,
        ["INS"] = TokenKind.Insert,
        ["HAS"] = TokenKind.Has,
        ["KEY"] = TokenKind.Key,
        ["VAL"] = TokenKind.Val,
        ["EACHKV"] = TokenKind.EachKV,
        ["/EACHKV"] = TokenKind.EndEachKV,
        ["CNT"] = TokenKind.Count,

        // Generics
        // Old syntax removed: ["TP"] = TokenKind.TypeParam (use <T> suffix instead)
        // Old syntax removed: ["G"] = TokenKind.Generic (use List<T> inline instead)
        ["WR"] = TokenKind.Where,           // §WR = Where (legacy, still supported)
        ["WHERE"] = TokenKind.Where,        // §WHERE = Where (new syntax)

        // Classes, Interfaces, Inheritance
        ["CL"] = TokenKind.Class,           // §CL = Class
        ["/CL"] = TokenKind.EndClass,       // §/CL
        ["IFACE"] = TokenKind.Interface,    // §IFACE (already 5 chars)
        ["/IFACE"] = TokenKind.EndInterface,
        ["IMPL"] = TokenKind.Implements,
        ["EXT"] = TokenKind.Extends,
        ["MT"] = TokenKind.Method,          // §MT = Method
        ["/MT"] = TokenKind.EndMethod,      // §/MT
        ["VR"] = TokenKind.Virtual,         // §VR = Virtual
        ["OV"] = TokenKind.Override,        // §OV = Override
        ["AB"] = TokenKind.Abstract,        // §AB = Abstract
        ["SD"] = TokenKind.Sealed,          // §SD = Sealed
        ["THIS"] = TokenKind.This,
        ["/THIS"] = TokenKind.EndThis,
        ["BASE"] = TokenKind.Base,
        ["/BASE"] = TokenKind.EndBase,
        ["NEW"] = TokenKind.New,
        ["/NEW"] = TokenKind.EndNew,
        ["FLD"] = TokenKind.FieldDef,

        // Properties and Constructors
        ["PROP"] = TokenKind.Property,
        ["/PROP"] = TokenKind.EndProperty,
        ["GET"] = TokenKind.Get,
        ["/GET"] = TokenKind.EndGet,
        ["SET"] = TokenKind.Set,
        ["/SET"] = TokenKind.EndSet,
        ["INIT"] = TokenKind.Init,
        ["CTOR"] = TokenKind.Constructor,
        ["/CTOR"] = TokenKind.EndConstructor,
        ["ASSIGN"] = TokenKind.Assign,
        ["DEFAULT"] = TokenKind.Default,

        // Try/Catch/Finally
        ["TR"] = TokenKind.Try,             // §TR = Try
        ["/TR"] = TokenKind.EndTry,         // §/TR = EndTry
        ["CA"] = TokenKind.Catch,           // §CA = Catch
        ["FI"] = TokenKind.Finally,         // §FI = Finally
        ["TH"] = TokenKind.Throw,           // §TH = Throw
        ["RT"] = TokenKind.Rethrow,         // §RT = Rethrow
        ["WHEN"] = TokenKind.When,

        // Lambdas, Delegates, Events
        ["LAM"] = TokenKind.Lambda,
        ["/LAM"] = TokenKind.EndLambda,
        ["DEL"] = TokenKind.Delegate,
        ["/DEL"] = TokenKind.EndDelegate,
        ["EVT"] = TokenKind.Event,
        ["SUB"] = TokenKind.Subscribe,
        ["UNSUB"] = TokenKind.Unsubscribe,

        // Async/Await
        ["ASYNC"] = TokenKind.Async,
        ["AWAIT"] = TokenKind.Await,
        ["AF"] = TokenKind.AsyncFunc,
        ["/AF"] = TokenKind.EndAsyncFunc,
        ["AMT"] = TokenKind.AsyncMethod,
        ["/AMT"] = TokenKind.EndAsyncMethod,

        // String Interpolation and Modern Operators
        ["INTERP"] = TokenKind.Interpolate,
        ["/INTERP"] = TokenKind.EndInterpolate,
        ["??"] = TokenKind.NullCoalesce,
        ["?."] = TokenKind.NullConditional,
        ["RANGE"] = TokenKind.RangeOp,
        ["^"] = TokenKind.IndexEnd,
        ["EXP"] = TokenKind.Expression,

        // Advanced Patterns
        ["WITH"] = TokenKind.With,
        ["/WITH"] = TokenKind.EndWith,
        ["PPOS"] = TokenKind.PositionalPattern,
        ["PPROP"] = TokenKind.PropertyPattern,
        ["PMATCH"] = TokenKind.PropertyMatch,
        ["PREL"] = TokenKind.RelationalPattern,
        ["PLIST"] = TokenKind.ListPattern,
        ["VAR"] = TokenKind.Var,
        ["REST"] = TokenKind.Rest,

        // Enums and Extensions
        ["EN"] = TokenKind.Enum,                // §EN = Enum (short form)
        ["ENUM"] = TokenKind.Enum,              // §ENUM = Enum (legacy)
        ["/EN"] = TokenKind.EndEnum,            // §/EN
        ["/ENUM"] = TokenKind.EndEnum,          // §/ENUM (legacy)
        ["EEXT"] = TokenKind.EnumExtension,     // §EEXT = Enum Extension (note: §EXT is for class inheritance)
        ["/EEXT"] = TokenKind.EndEnumExtension, // §/EEXT

        // Extended Features: Quick Wins
        ["EX"] = TokenKind.Example,             // §EX - Inline examples/tests
        ["TD"] = TokenKind.Todo,                // §TD = Todo
        ["FX"] = TokenKind.Fixme,               // §FX = Fixme
        ["HK"] = TokenKind.Hack,                // §HK = Hack

        // Extended Features: Core Features
        ["US"] = TokenKind.Uses,                // §US = Uses
        ["/US"] = TokenKind.EndUses,            // §/US
        ["UB"] = TokenKind.UsedBy,              // §UB = UsedBy
        ["/UB"] = TokenKind.EndUsedBy,          // §/UB
        ["AS"] = TokenKind.Assume,              // §AS = Assume

        // Extended Features: Enhanced Contracts
        ["CX"] = TokenKind.Complexity,          // §CX = Complexity
        ["SN"] = TokenKind.Since,               // §SN = Since
        ["DP"] = TokenKind.Deprecated,          // §DP = Deprecated
        ["BR"] = TokenKind.Breaking,            // §BR = Breaking
        ["XP"] = TokenKind.Experimental,        // §XP = Experimental
        ["SB"] = TokenKind.Stable,              // §SB = Stable

        // Extended Features: Future Extensions
        ["DC"] = TokenKind.Decision,            // §DC = Decision
        ["/DC"] = TokenKind.EndDecision,        // §/DC
        ["CHOSEN"] = TokenKind.Chosen,          // §CHOSEN - short enough
        ["REJECTED"] = TokenKind.Rejected,      // Keep for clarity
        ["REASON"] = TokenKind.Reason,          // Keep for clarity
        ["CT"] = TokenKind.Context,             // §CT = Context
        ["/CT"] = TokenKind.EndContext,         // §/CT
        ["VS"] = TokenKind.Visible,             // §VS = Visible
        ["/VS"] = TokenKind.EndVisible,         // §/VS
        ["HD"] = TokenKind.HiddenSection,       // §HD = Hidden
        ["/HD"] = TokenKind.EndHidden,          // §/HD
        ["FC"] = TokenKind.Focus,               // §FC = Focus
        ["FILE"] = TokenKind.FileRef,           // §FILE - keep for clarity
        ["PT"] = TokenKind.PropertyTest,        // §PT = Property test
        ["LK"] = TokenKind.Lock,                // §LK = Lock
        ["AU"] = TokenKind.AgentAuthor,         // §AU = Author
        ["TASK"] = TokenKind.TaskRef,           // §TASK - keep for clarity
        ["DATE"] = TokenKind.DateMarker,        // §DATE - keep for clarity

        // Built-in aliases for common operations
        ["P"] = TokenKind.Print,            // §P = Console.WriteLine
        ["Pf"] = TokenKind.PrintF,          // §Pf = Console.Write
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
            '{' => ScanSingle(TokenKind.OpenBrace),
            '}' => ScanSingle(TokenKind.CloseBrace),
            '(' => ScanSingle(TokenKind.OpenParen),
            ')' => ScanSingle(TokenKind.CloseParen),
            '=' => ScanEqualsOrOperator(),
            ':' => ScanColonOrTypedLiteral(),
            '!' => ScanBangOrOperator(),
            '~' => ScanSingle(TokenKind.Tilde),
            '#' => ScanSingle(TokenKind.Hash),
            '?' => ScanQuestionOrOperator(),
            '@' => ScanSingle(TokenKind.At),
            ',' => ScanSingle(TokenKind.Comma),
            '"' => ScanStringLiteral(),
            '\r' or '\n' => ScanNewline(),
            ' ' or '\t' => ScanWhitespace(),
            // v2 Lisp-style operator symbols
            '+' => ScanSingle(TokenKind.Plus),
            '*' => ScanStarOrOperator(),
            '/' => ScanSingle(TokenKind.Slash),
            '\\' => ScanSingle(TokenKind.Backslash),
            '%' => ScanSingle(TokenKind.Percent),
            '<' => ScanLessOrOperator(),
            '>' => ScanGreaterOrOperator(),
            '&' => ScanAmpOrOperator(),
            '|' => ScanPipeOrOperator(),
            '^' => ScanSingle(TokenKind.Caret),
            '.' => ScanDotOrNumber(),
            // Arrow: → or ->
            '→' => ScanSingle(TokenKind.Arrow),
            '-' => ScanMinusOrArrowOrNumber(),
            // Unicode quantifiers
            '∀' => ScanUnicodeQuantifier("forall"),
            '∃' => ScanUnicodeQuantifier("exists"),
            '`' => ScanBacktickIdentifier(),
            _ when char.IsLetter(Current) || Current == '_' => ScanIdentifierOrTypedLiteral(),
            _ when char.IsDigit(Current) => ScanNumber(),
            _ => ScanError()
        };
    }

    private Token ScanEqualsOrOperator()
    {
        Advance(); // consume '='
        if (Current == '=')
        {
            Advance(); // consume second '='
            return MakeToken(TokenKind.EqualEqual);
        }
        return MakeToken(TokenKind.Equals);
    }

    private Token ScanBangOrOperator()
    {
        Advance(); // consume '!'
        if (Current == '=')
        {
            Advance(); // consume '='
            return MakeToken(TokenKind.BangEqual);
        }
        return MakeToken(TokenKind.Exclamation);
    }

    private Token ScanQuestionOrOperator()
    {
        Advance(); // consume '?'
        if (Current == '.')
        {
            Advance(); // consume '.'
            return MakeToken(TokenKind.NullConditional);
        }
        if (Current == '?')
        {
            Advance(); // consume second '?'
            return MakeToken(TokenKind.NullCoalesce);
        }
        return MakeToken(TokenKind.Question);
    }

    private Token ScanStarOrOperator()
    {
        Advance(); // consume '*'
        if (Current == '*')
        {
            Advance(); // consume second '*'
            return MakeToken(TokenKind.StarStar);
        }
        return MakeToken(TokenKind.Star);
    }

    private Token ScanLessOrOperator()
    {
        Advance(); // consume '<'
        if (Current == '=')
        {
            Advance(); // consume '='
            return MakeToken(TokenKind.LessEqual);
        }
        if (Current == '<')
        {
            Advance(); // consume second '<'
            return MakeToken(TokenKind.LessLess);
        }
        return MakeToken(TokenKind.Less);
    }

    private Token ScanGreaterOrOperator()
    {
        Advance(); // consume '>'
        if (Current == '=')
        {
            Advance(); // consume '='
            return MakeToken(TokenKind.GreaterEqual);
        }
        if (Current == '>')
        {
            Advance(); // consume second '>'
            return MakeToken(TokenKind.GreaterGreater);
        }
        return MakeToken(TokenKind.Greater);
    }

    private Token ScanAmpOrOperator()
    {
        Advance(); // consume '&'
        if (Current == '&')
        {
            Advance(); // consume second '&'
            return MakeToken(TokenKind.AmpAmp);
        }
        return MakeToken(TokenKind.Amp);
    }

    private Token ScanPipeOrOperator()
    {
        Advance(); // consume '|'
        if (Current == '|')
        {
            Advance(); // consume second '|'
            return MakeToken(TokenKind.PipePipe);
        }
        return MakeToken(TokenKind.Pipe);
    }

    private Token ScanMinusOrArrowOrNumber()
    {
        // Check for -> arrow
        if (Lookahead == '>')
        {
            Advance(); // consume '-'
            Advance(); // consume '>'
            return MakeToken(TokenKind.Arrow);
        }
        // Check for negative number
        if (char.IsDigit(Lookahead))
        {
            return ScanNumber();
        }
        // Otherwise it's just minus operator
        Advance();
        return MakeToken(TokenKind.Minus);
    }

    /// <summary>
    /// Scans a Unicode quantifier symbol (∀ or ∃) and returns it as an identifier token
    /// with the corresponding keyword text.
    /// </summary>
    private Token ScanUnicodeQuantifier(string keywordText)
    {
        Advance(); // consume the Unicode character
        return new Token(TokenKind.Identifier, keywordText, CurrentSpan(), keywordText);
    }

    private Token ScanDotOrNumber()
    {
        // Check for decimal number starting with .
        if (char.IsDigit(Lookahead))
        {
            return ScanNumber();
        }
        // Otherwise it's just a dot for member access
        Advance();
        return MakeToken(TokenKind.Dot);
    }

    private Token ScanBacktickIdentifier()
    {
        Advance(); // consume opening backtick

        var sb = new System.Text.StringBuilder();
        while (!IsAtEnd && Current != '`')
        {
            if (Current == '\n')
            {
                _diagnostics.ReportUnterminatedString(CurrentSpan());
                return MakeToken(TokenKind.Error);
            }
            sb.Append(Current);
            Advance();
        }

        if (IsAtEnd)
        {
            _diagnostics.ReportUnterminatedString(CurrentSpan());
            return MakeToken(TokenKind.Error);
        }

        Advance(); // consume closing backtick
        return new Token(TokenKind.Identifier, sb.ToString(), CurrentSpan(), sb.ToString());
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

            // Unknown closing tag - provide helpful suggestions
            ReportUnknownSectionMarker(keyword);
            return MakeToken(TokenKind.Error);
        }

        // Check for special operators that start with '?'
        // §?? = NullCoalesce, §?. = NullConditional
        if (Current == '?')
        {
            Advance(); // consume first '?'
            if (Current == '?')
            {
                Advance(); // consume second '?'
                return MakeToken(TokenKind.NullCoalesce);
            }
            if (Current == '.')
            {
                Advance(); // consume '.'
                return MakeToken(TokenKind.NullConditional);
            }
            // Unknown §? pattern - report error
            _diagnostics.ReportUnexpectedCharacter(CurrentSpan(), '?');
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

        // Unknown section keyword - provide helpful suggestions
        ReportUnknownSectionMarker(fullKeyword);
        return MakeToken(TokenKind.Error);
    }

    /// <summary>
    /// Reports an unknown section marker with helpful suggestions.
    /// </summary>
    private void ReportUnknownSectionMarker(string keyword)
    {
        // Try to find a similar marker
        var suggestion = SectionMarkerSuggestions.FindSimilarMarker(keyword);

        if (suggestion != null)
        {
            var description = SectionMarkerSuggestions.MarkerDescriptions.TryGetValue(
                suggestion.TrimStart('/'), out var desc)
                ? $" ({desc})"
                : "";
            _diagnostics.ReportError(CurrentSpan(), Diagnostics.DiagnosticCode.UnexpectedCharacter,
                $"Unknown section marker '§{keyword}'. Did you mean '§{suggestion}'{description}?");
        }
        else
        {
            _diagnostics.ReportError(CurrentSpan(), Diagnostics.DiagnosticCode.UnexpectedCharacter,
                $"Unknown section marker '§{keyword}'. Common markers: {SectionMarkerSuggestions.GetCommonMarkers()}");
        }
    }

    private Token ScanIdentifierOrTypedLiteral()
    {
        while (char.IsLetterOrDigit(Current) || Current == '_' || Current == '.')
        {
            Advance();
        }

        var text = CurrentText();

        // Check for typed literals (INT:42, STR:"hello", BOOL:true, FLOAT:3.14)
        // Only treat as typed literal if the following value looks like a valid literal
        if (Current == ':')
        {
            var upperText = text.ToUpperInvariant();
            var lookahead = Peek(1);

            // INT:digits or INT:-digits
            if (upperText == "INT" && (char.IsDigit(lookahead) || lookahead == '-'))
            {
                return ScanTypedIntLiteral();
            }
            // STR:"string"
            if (upperText == "STR" && lookahead == '"')
            {
                return ScanTypedStringLiteral();
            }
            // BOOL:true or BOOL:false
            if (upperText == "BOOL" && (lookahead == 't' || lookahead == 'f'))
            {
                // Extra check: make sure it's actually "true" or "false", not an identifier
                var remaining = _source[(_position + 1)..];
                if (remaining.StartsWith("true") || remaining.StartsWith("false"))
                {
                    return ScanTypedBoolLiteral();
                }
            }
            // FLOAT:digits or FLOAT:-digits or FLOAT:.digits
            if (upperText == "FLOAT" && (char.IsDigit(lookahead) || lookahead == '-' || lookahead == '.'))
            {
                return ScanTypedFloatLiteral();
            }

            // Not a typed literal - return as identifier (colon is a separate token)
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
                    '0' => '\0',
                    '\\' => '\\',
                    '"' => '"',
                    _ => '\x01' // sentinel for invalid escape
                };

                if (escaped == '\x01')
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
