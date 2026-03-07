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
        ["GOTO"] = TokenKind.Goto,          // §GOTO{label} = Goto
        ["LABEL"] = TokenKind.Label,        // §LABEL{label} = Label
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

        // Properties, Indexers, and Constructors
        ["PROP"] = TokenKind.Property,
        ["/PROP"] = TokenKind.EndProperty,
        ["IXER"] = TokenKind.Indexer,
        ["/IXER"] = TokenKind.EndIndexer,
        ["GET"] = TokenKind.Get,
        ["/GET"] = TokenKind.EndGet,
        ["SET"] = TokenKind.Set,
        ["/SET"] = TokenKind.EndSet,
        ["INIT"] = TokenKind.Init,
        ["CTOR"] = TokenKind.Constructor,
        ["/CTOR"] = TokenKind.EndConstructor,
        ["OP"] = TokenKind.OperatorOverload,
        ["/OP"] = TokenKind.EndOperatorOverload,
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
        ["/EVT"] = TokenKind.EndEvent,
        ["EADD"] = TokenKind.EventAdd,
        ["/EADD"] = TokenKind.EndEventAdd,
        ["EREM"] = TokenKind.EventRemove,
        ["/EREM"] = TokenKind.EndEventRemove,
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

        // Yield support
        ["YIELD"] = TokenKind.Yield,            // §YIELD = yield return
        ["YBRK"] = TokenKind.YieldBreak,        // §YBRK = yield break

        // LINQ Support
        ["ANON"] = TokenKind.AnonymousObject,   // §ANON = Anonymous object
        ["/ANON"] = TokenKind.EndAnonymousObject, // §/ANON

        // Unsafe/Low-Level
        ["SALLOC"] = TokenKind.StackAlloc,          // §SALLOC
        ["/SALLOC"] = TokenKind.EndStackAlloc,      // §/SALLOC
        ["UNSAFE"] = TokenKind.Unsafe,              // §UNSAFE
        ["/UNSAFE"] = TokenKind.EndUnsafe,          // §/UNSAFE
        ["FIXED"] = TokenKind.Fixed,                // §FIXED
        ["/FIXED"] = TokenKind.EndFixed,            // §/FIXED
        ["ADDR"] = TokenKind.AddressOf,             // §ADDR
        ["DEREF"] = TokenKind.Deref,                // §DEREF
        ["SIZEOF"] = TokenKind.SizeOf,              // §SIZEOF

        // Synchronization
        ["SYNC"] = TokenKind.SyncBlock,              // §SYNC
        ["/SYNC"] = TokenKind.EndSyncBlock,          // §/SYNC

        // Multidimensional Arrays
        ["ARR2D"] = TokenKind.Array2D,              // §ARR2D
        ["/ARR2D"] = TokenKind.EndArray2D,          // §/ARR2D
        ["IDX2D"] = TokenKind.Index2D,              // §IDX2D
        ["ROW"] = TokenKind.Row,                    // §ROW

        // Dependent Types: Refinement Types and Proof Obligations
        ["RTYPE"] = TokenKind.RefinedType,           // §RTYPE
        ["/RTYPE"] = TokenKind.EndRefinedType,       // §/RTYPE
        ["PROOF"] = TokenKind.Proof,                 // §PROOF
        ["ITYPE"] = TokenKind.IndexedType,           // §ITYPE
        ["/ITYPE"] = TokenKind.EndIndexedType,       // §/ITYPE

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
            '/' => ScanSlashOrComment(),
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
            '\'' => ScanCharLiteralOrSkip(),
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

    private Token ScanSlashOrComment()
    {
        if (Lookahead == '/')
        {
            // Line comment: skip to end of line
            while (Current != '\n' && Current != '\r' && Current != '\0')
                Advance();
            return NextToken(); // skip comment entirely, return next real token
        }
        Advance();
        return MakeToken(TokenKind.Slash);
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

            // Special handling for §/PP{CONDITION}: preprocessor conditional end (closing tag)
            if (keyword.Equals("/PP", StringComparison.Ordinal) && Current == '{')
            {
                return ScanPreprocessorCondition(TokenKind.EndPreprocessor);
            }

            // Unknown closing tag - provide helpful suggestions
            ReportUnknownSectionMarker(keyword);
            return MakeToken(TokenKind.Error);
        }

        // Check for §^ = IndexFromEnd
        if (Current == '^')
        {
            Advance(); // consume '^'
            return MakeToken(TokenKind.IndexEnd);
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
            _diagnostics.ReportError(CurrentSpan(), Diagnostics.DiagnosticCode.InvalidSectionOperator,
                "Invalid section operator '§?'. Expected '§??' (null-coalesce) or '§?.' (null-conditional).");
            return MakeToken(TokenKind.Error);
        }

        // Read the keyword that follows
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            Advance();
        }

        var fullText = CurrentText();
        var fullKeyword = fullText.Length > 1 ? fullText[1..] : "";

        // Special handling for §RAW: scan to §/RAW and capture everything as raw content
        if (fullKeyword.Equals("RAW", StringComparison.Ordinal))
        {
            return ScanRawBlock();
        }

        // Special handling for §CSHARP: scan to }§/CSHARP and capture everything as interop content
        if (fullKeyword.Equals("CSHARP", StringComparison.Ordinal))
        {
            return ScanCSharpInteropBlock();
        }

        // Special handling for §CS{expr}: inline C# expression with balanced braces
        if (fullKeyword.Equals("CS", StringComparison.Ordinal) && Current == '{')
        {
            return ScanRawCSharpExpression();
        }

        // Special handling for §GOTO{label}: goto statement with label content
        if (fullKeyword.Equals("GOTO", StringComparison.Ordinal) && Current == '{')
        {
            return ScanBraceContent(TokenKind.Goto);
        }

        // Special handling for §LABEL{label}: label definition with label content
        if (fullKeyword.Equals("LABEL", StringComparison.Ordinal) && Current == '{')
        {
            return ScanBraceContent(TokenKind.Label);
        }

        // Special handling for §PP{CONDITION}: preprocessor conditional start
        if (fullKeyword.Equals("PP", StringComparison.Ordinal) && Current == '{')
        {
            return ScanPreprocessorCondition(TokenKind.Preprocessor);
        }

        // Special handling for §/PP{CONDITION}: preprocessor conditional end (closing tag)
        if (fullKeyword.Equals("/PP", StringComparison.Ordinal) && Current == '{')
        {
            return ScanPreprocessorCondition(TokenKind.EndPreprocessor);
        }

        // Special handling for §PPE: preprocessor else
        if (fullKeyword.Equals("PPE", StringComparison.Ordinal))
        {
            return MakeToken(TokenKind.PreprocessorElse);
        }

        if (Keywords.TryGetValue(fullKeyword, out var keywordKind))
        {
            return MakeToken(keywordKind);
        }

        // Unknown section keyword - provide helpful suggestions
        ReportUnknownSectionMarker(fullKeyword);
        return MakeToken(TokenKind.Error);
    }

    /// <summary>
    /// Scans a raw C# passthrough block. Called after §RAW has been consumed.
    /// Captures everything until §/RAW as raw content.
    /// </summary>
    private Token ScanRawBlock()
    {
        // Skip optional whitespace/newline after §RAW
        if (Current == '\n') Advance();
        else if (Current == '\r' && Lookahead == '\n') { Advance(); Advance(); }

        var contentStart = _position;
        const string endMarker = "§/RAW";

        // Scan forward to find §/RAW
        while (!IsAtEnd)
        {
            if (Current == '§' && _position + endMarker.Length <= _source.Length
                && _source.Substring(_position, endMarker.Length) == endMarker)
            {
                // Found the end marker — capture content up to here
                var rawContent = _source[contentStart.._position];

                // Trim trailing newline from content if present
                if (rawContent.EndsWith("\r\n"))
                    rawContent = rawContent[..^2];
                else if (rawContent.EndsWith("\n"))
                    rawContent = rawContent[..^1];

                // Advance past §/RAW
                for (int i = 0; i < endMarker.Length; i++)
                    Advance();

                return MakeToken(TokenKind.RawCSharp, rawContent);
            }

            Advance();
        }

        // Reached end of file without finding §/RAW
        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedRawBlock,
            "Unterminated §RAW block: expected §/RAW before end of file.");
        return MakeToken(TokenKind.Error);
    }

    /// <summary>
    /// Scans a C# interop block. Called after §CSHARP has been consumed.
    /// Expects { immediately after, then captures everything until }§/CSHARP.
    /// </summary>
    private Token ScanCSharpInteropBlock()
    {
        // Expect opening brace
        if (Current != '{')
        {
            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedCSharpInteropBlock,
                "Expected '{' after §CSHARP.");
            return MakeToken(TokenKind.Error);
        }
        Advance(); // consume '{'

        // Skip optional whitespace/newline after {
        if (Current == '\n') Advance();
        else if (Current == '\r' && Lookahead == '\n') { Advance(); Advance(); }

        var contentStart = _position;
        const string endMarker = "}§/CSHARP";

        // Scan forward to find }§/CSHARP
        while (!IsAtEnd)
        {
            if (Current == '}' && _position + endMarker.Length <= _source.Length
                && _source.Substring(_position, endMarker.Length) == endMarker)
            {
                // Found the end marker — capture content up to here
                var rawContent = _source[contentStart.._position];

                // Trim trailing newline from content if present
                if (rawContent.EndsWith("\r\n"))
                    rawContent = rawContent[..^2];
                else if (rawContent.EndsWith("\n"))
                    rawContent = rawContent[..^1];

                // Advance past }§/CSHARP
                for (int i = 0; i < endMarker.Length; i++)
                    Advance();

                return MakeToken(TokenKind.CSharpInterop, rawContent);
            }

            Advance();
        }

        // Reached end of file without finding }§/CSHARP
        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedCSharpInteropBlock,
            "Unterminated §CSHARP block: expected }§/CSHARP before end of file.");
        return MakeToken(TokenKind.Error);
    }

    /// <summary>
    /// Scans an inline raw C# expression. Called after §CS has been consumed.
    /// Expects { immediately after, then captures everything until the matching } (tracking brace depth).
    /// </summary>
    private Token ScanRawCSharpExpression()
    {
        Advance(); // consume '{'
        var contentStart = _position;
        int depth = 1;

        while (!IsAtEnd && depth > 0)
        {
            // Skip braces inside string literals
            if (Current == '"')
            {
                Advance(); // consume opening "
                while (!IsAtEnd && Current != '"')
                {
                    if (Current == '\\') Advance(); // skip escape char
                    Advance();
                }
                if (!IsAtEnd) Advance(); // consume closing "
                continue;
            }
            // Skip braces inside char literals
            if (Current == '\'')
            {
                Advance(); // consume opening '
                while (!IsAtEnd && Current != '\'')
                {
                    if (Current == '\\') Advance(); // skip escape char
                    Advance();
                }
                if (!IsAtEnd) Advance(); // consume closing '
                continue;
            }
            if (Current == '{')
            {
                depth++;
            }
            else if (Current == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
            Advance();
        }

        if (IsAtEnd && depth > 0)
        {
            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedCSharpInteropBlock,
                "Unterminated §CS{ block: expected matching '}' before end of file.");
            return MakeToken(TokenKind.Error);
        }

        var content = _source[contentStart.._position];
        Advance(); // consume closing '}'
        return MakeToken(TokenKind.RawCSharpExpression, content);
    }

    /// <summary>
    /// Scans a preprocessor condition from inside braces: §PP{CONDITION} or §/PP{CONDITION}.
    /// Called after §PP or §/PP has been consumed and '{' is the current character.
    /// </summary>
    private Token ScanPreprocessorCondition(TokenKind kind)
    {
        Advance(); // consume '{'
        var contentStart = _position;
        int depth = 1;

        while (!IsAtEnd && depth > 0)
        {
            if (Current == '{')
            {
                depth++;
            }
            else if (Current == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
            Advance();
        }

        if (IsAtEnd && depth > 0)
        {
            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedCSharpInteropBlock,
                $"Unterminated preprocessor directive: expected matching '}}' before end of file.");
            return MakeToken(TokenKind.Error);
        }

        var condition = _source[contentStart.._position];
        Advance(); // consume closing '}'
        return MakeToken(kind, condition);
    }

    /// <summary>
    /// Scans simple brace content for section markers like §GOTO{label} or §LABEL{label}.
    /// Called after the keyword has been consumed and '{' is the current character.
    /// </summary>
    private Token ScanBraceContent(TokenKind kind)
    {
        Advance(); // consume '{'
        var contentStart = _position;

        while (!IsAtEnd && Current != '}')
        {
            Advance();
        }

        if (IsAtEnd)
        {
            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedCSharpInteropBlock,
                $"Unterminated section marker: expected matching '}}' before end of file.");
            return MakeToken(TokenKind.Error);
        }

        var content = _source[contentStart.._position];
        Advance(); // consume closing '}'
        return MakeToken(kind, content);
    }

    /// <summary>
    /// Reports an unknown section marker with helpful suggestions.
    /// </summary>
    private void ReportUnknownSectionMarker(string keyword)
    {
        // Special case: §CAST is a common mistake — casting uses Lisp syntax
        if (keyword.Equals("CAST", StringComparison.OrdinalIgnoreCase))
        {
            _diagnostics.ReportError(CurrentSpan(), Diagnostics.DiagnosticCode.UnknownSectionMarker,
                $"Unknown section marker '§{keyword}'. Calor uses Lisp syntax for casts: " +
                $"(cast TargetType expr). Example: (cast i32 myFloat)");
            return;
        }

        // Try to find a similar marker
        var suggestion = SectionMarkerSuggestions.FindSimilarMarker(keyword);

        if (suggestion != null)
        {
            var description = SectionMarkerSuggestions.MarkerDescriptions.TryGetValue(
                suggestion.TrimStart('/'), out var desc)
                ? $" ({desc})"
                : "";
            _diagnostics.ReportError(CurrentSpan(), Diagnostics.DiagnosticCode.UnknownSectionMarker,
                $"Unknown section marker '§{keyword}'. Did you mean '§{suggestion}'{description}?");
        }
        else
        {
            _diagnostics.ReportError(CurrentSpan(), Diagnostics.DiagnosticCode.UnknownSectionMarker,
                $"Unknown section marker '§{keyword}'. Common markers: {SectionMarkerSuggestions.GetCommonMarkers()}");
        }
    }

    // Design note: Dots are intentionally included in identifiers so that qualified
    // names like Math.PI, StringComparison.Ordinal, and System.Console are lexed as
    // single tokens. This produces ReferenceNode("Math.PI") rather than a
    // FieldAccessNode chain. The CSharpEmitter (Visit(ReferenceNode)) handles dots
    // by splitting on '.' and sanitizing each part, so the round-trip is lossless.
    // See DottedNameRoundTripTests for verification.
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
            // DECIMAL:digits or DEC:digits (decimal literal)
            if ((upperText == "DECIMAL" || upperText == "DEC") && (char.IsDigit(lookahead) || lookahead == '-' || lookahead == '.'))
            {
                return ScanTypedDecimalLiteral();
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

        // Check for hex prefix in typed literal: INT:0xFF
        if (Current == '0' && Lookahead is 'x' or 'X')
        {
            Advance(); // consume '0'
            Advance(); // consume 'x'/'X'
            while (IsHexDigit(Current))
            {
                Advance();
            }

            var hexValueText = _source[valueStart.._position];
            var hexPart = hexValueText.AsSpan(hexValueText.IndexOf('x') + 1);
            if (long.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var hexVal))
            {
                return MakeToken(TokenKind.IntLiteral,
                    new IntLiteralInfo(hexVal, IsHex: true, IsUnsigned: false, (ulong)hexVal));
            }

            // Try ulong for values > long.MaxValue (e.g., 0xcccccccccccccccd)
            if (ulong.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var uhexVal))
            {
                return MakeToken(TokenKind.IntLiteral,
                    new IntLiteralInfo(unchecked((long)uhexVal), IsHex: true, IsUnsigned: true, uhexVal));
            }

            _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "INT");
            return MakeToken(TokenKind.Error);
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

        // Fall back to long for values outside int range
        if (long.TryParse(valueText, out var longValue))
        {
            return MakeToken(TokenKind.IntLiteral, longValue);
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

    private Token ScanTypedDecimalLiteral()
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

        // Consume optional M/m suffix
        if (Current is 'M' or 'm')
        {
            Advance();
        }

        var valueText = _source[valueStart.._position].TrimEnd('M', 'm');
        if (decimal.TryParse(valueText, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return MakeToken(TokenKind.DecimalLiteral, value);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "DECIMAL");
        return MakeToken(TokenKind.Error);
    }

    /// <summary>
    /// Handles single-quote character: scans a char literal like 'a' or '\n',
    /// and returns it as a string literal token. If malformed, reports an error.
    /// </summary>
    private Token ScanCharLiteralOrSkip()
    {
        Advance(); // consume opening '
        if (Current == '\\')
        {
            Advance(); // consume backslash
            Advance(); // consume escape char
        }
        else if (Current != '\'' && Current != '\0' && Current != '\n')
        {
            Advance(); // consume the character
        }
        if (Current == '\'')
        {
            Advance(); // consume closing '
            return MakeToken(TokenKind.StrLiteral, _source[(_tokenStart + 1)..(_position - 1)]);
        }
        // Malformed — recover by continuing
        return MakeToken(TokenKind.Error);
    }

    private Token ScanStringLiteral()
    {
        // Detect triple-quote for multiline strings: """..."""
        if (Lookahead == '"' && Peek(2) == '"')
            return ScanMultilineStringLiteral();
        return ScanStringLiteralValue();
    }

    private Token ScanMultilineStringLiteral()
    {
        Advance(); // consume first "
        Advance(); // consume second "
        Advance(); // consume third "

        // Optionally skip a leading newline right after the opening triple-quote
        if (!IsAtEnd && Current == '\r')
            Advance();
        if (!IsAtEnd && Current == '\n')
            Advance();

        var sb = new System.Text.StringBuilder();
        while (!IsAtEnd)
        {
            // Check for closing triple-quote
            if (Current == '"' && Lookahead == '"' && Peek(2) == '"')
            {
                Advance(); // consume first "
                Advance(); // consume second "
                Advance(); // consume third "
                return MakeToken(TokenKind.StrLiteral, sb.ToString());
            }

            if (Current == '\\')
            {
                Advance();
                if (Current == 'u')
                {
                    AppendUnicodeEscape(sb);
                }
                else if (Current == 'U')
                {
                    AppendLongUnicodeEscape(sb);
                }
                else
                {
                    var escaped = Current switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '0' => '\0',
                        '\\' => '\\',
                        '"' => '"',
                        '$' => '$',
                        '\'' => '\'',
                        'a' => '\a',
                        'b' => '\b',
                        'f' => '\f',
                        'v' => '\v',
                        '/' => '/',
                        '{' => '{',
                        '}' => '}',
                        _ => '\x01' // sentinel for invalid escape
                    };

                    if (escaped == '\x01')
                    {
                        // Tolerate unknown escape sequences in triple-quoted strings —
                        // emit the literal character. Converted strings may contain \S, \N, etc.
                        sb.Append(Current);
                    }
                    else
                    {
                        sb.Append(escaped);
                    }
                    Advance();
                }
            }
            else
            {
                sb.Append(Current);
                Advance();
            }
        }

        _diagnostics.ReportUnterminatedString(CurrentSpan());
        return MakeToken(TokenKind.Error);
    }

    /// <summary>
    /// Parses a \uXXXX unicode escape sequence and appends the character to the builder.
    /// Assumes the 'u' has already been detected but NOT consumed.
    /// </summary>
    private void AppendUnicodeEscape(System.Text.StringBuilder sb)
    {
        Advance(); // consume 'u'
        var hex = new System.Text.StringBuilder(4);
        for (int i = 0; i < 4 && !IsAtEnd; i++)
        {
            if (Uri.IsHexDigit(Current))
            {
                hex.Append(Current);
                Advance();
            }
            else
            {
                break;
            }
        }
        if (hex.Length == 4 && int.TryParse(hex.ToString(), System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
        {
            sb.Append((char)codePoint);
        }
        else
        {
            // Emit the raw \uXXXX if we can't parse it
            sb.Append("\\u");
            sb.Append(hex);
        }
    }

    /// <summary>
    /// Parses a \UXXXXXXXX (8-digit) unicode escape sequence and appends the character to the builder.
    /// Assumes the 'U' has already been detected but NOT consumed.
    /// </summary>
    private void AppendLongUnicodeEscape(System.Text.StringBuilder sb)
    {
        Advance(); // consume 'U'
        var hex = new System.Text.StringBuilder(8);
        for (int i = 0; i < 8 && !IsAtEnd; i++)
        {
            if (Uri.IsHexDigit(Current))
            {
                hex.Append(Current);
                Advance();
            }
            else
            {
                break;
            }
        }
        if (hex.Length == 8 && long.TryParse(hex.ToString(), System.Globalization.NumberStyles.HexNumber, null, out var codePoint)
            && codePoint <= 0x10FFFF)
        {
            sb.Append(char.ConvertFromUtf32((int)codePoint));
        }
        else
        {
            // Emit the raw \UXXXXXXXX if we can't parse it
            sb.Append("\\U");
            sb.Append(hex);
        }
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
                if (Current == 'u')
                {
                    AppendUnicodeEscape(sb);
                }
                else if (Current == 'U')
                {
                    AppendLongUnicodeEscape(sb);
                }
                else
                {
                    var escaped = Current switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '0' => '\0',
                        '\\' => '\\',
                        '"' => '"',
                        '$' => '$',
                        '\'' => '\'',
                        'a' => '\a',
                        'b' => '\b',
                        'f' => '\f',
                        'v' => '\v',
                        '/' => '/',
                        '{' => '{',
                        '}' => '}',
                        _ => '\x01' // sentinel for invalid escape
                    };

                    if (escaped == '\x01')
                    {
                        // Tolerate unknown escape sequences — emit the literal character.
                        // Converted strings may contain \S, \N, etc. from doc comments.
                        sb.Append(Current);
                    }
                    else
                    {
                        sb.Append(escaped);
                    }
                    Advance();
                }
            }
            else if (Current == '$' && Lookahead == '{')
            {
                // Interpolation: ${...} — scan until matching } with brace-depth tracking
                sb.Append(Current); // $
                Advance();
                sb.Append(Current); // {
                Advance();

                int depth = 1;
                while (!IsAtEnd && depth > 0 && Current != '"')
                {
                    if (Current == '{')
                        depth++;
                    else if (Current == '}')
                        depth--;

                    if (depth > 0)
                    {
                        sb.Append(Current);
                        Advance();
                    }
                }

                if (!IsAtEnd && Current == '}')
                {
                    sb.Append(Current); // closing }
                    Advance();
                }
                // If we hit " before closing }, the ${...} was unmatched — already appended as literal
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

    private static bool IsHexDigit(char c)
        => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private Token ScanNumber()
    {
        if (Current == '-')
        {
            Advance();
        }

        // Check for hex literal: 0x or 0X prefix
        if (Current == '0' && Lookahead is 'x' or 'X')
        {
            Advance(); // consume '0'
            Advance(); // consume 'x'/'X'
            while (IsHexDigit(Current))
            {
                Advance();
            }

            // Consume optional U/UL suffix on hex literals
            bool hexUnsigned = false;
            if (Current is 'U' or 'u')
            {
                hexUnsigned = true;
                Advance();
                if (Current is 'L' or 'l') Advance();
            }
            else if (Current is 'L' or 'l')
            {
                Advance();
                if (Current is 'U' or 'u') { hexUnsigned = true; Advance(); }
            }

            var hexText = CurrentText();
            var hexPartStr = hexText.AsSpan(hexText.IndexOf('x') + 1);
            // Trim any suffix characters from the hex part
            hexPartStr = hexPartStr.TrimEnd("UuLl".AsSpan());
            if (long.TryParse(hexPartStr,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var hexValue))
            {
                return MakeToken(TokenKind.IntLiteral,
                    new IntLiteralInfo(hexValue, IsHex: true, hexUnsigned, (ulong)hexValue));
            }

            // Try ulong for values > long.MaxValue
            if (ulong.TryParse(hexPartStr,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var uhexValue))
            {
                return MakeToken(TokenKind.IntLiteral,
                    new IntLiteralInfo(unchecked((long)uhexValue), IsHex: true, IsUnsigned: true, uhexValue));
            }

            _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "hex number");
            return MakeToken(TokenKind.Error);
        }

        while (char.IsDigit(Current) || Current == '_')
        {
            Advance();
        }

        // Check for float
        if (Current == '.' && char.IsDigit(Lookahead))
        {
            Advance(); // consume '.'
            while (char.IsDigit(Current) || Current == '_')
            {
                Advance();
            }

            // Check for decimal suffix (M/m) on float
            if (Current is 'M' or 'm')
            {
                Advance(); // consume M/m
                var decText = CurrentText().TrimEnd('M', 'm').Replace("_", "");
                if (decimal.TryParse(decText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var decValue))
                {
                    return MakeToken(TokenKind.DecimalLiteral, decValue);
                }
            }

            var floatText = CurrentText().Replace("_", "");
            if (double.TryParse(floatText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floatValue))
            {
                return MakeToken(TokenKind.FloatLiteral, floatValue);
            }
        }

        // Check for decimal suffix on integers (42M, 100m)
        if (Current is 'M' or 'm')
        {
            Advance(); // consume M/m
            var decText = CurrentText().TrimEnd('M', 'm').Replace("_", "");
            if (decimal.TryParse(decText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var decValue))
            {
                return MakeToken(TokenKind.DecimalLiteral, decValue);
            }
        }

        // Check for unsigned/long suffix on integers (42U, 100UL, 50L, 50LU)
        bool isUnsignedSuffix = false;
        bool hasLongSuffix = false;
        if (Current is 'U' or 'u')
        {
            isUnsignedSuffix = true;
            Advance();
            if (Current is 'L' or 'l') { hasLongSuffix = true; Advance(); }
        }
        else if (Current is 'L' or 'l')
        {
            hasLongSuffix = true;
            Advance();
            if (Current is 'U' or 'u') { isUnsignedSuffix = true; Advance(); }
        }

        var intText = CurrentText().Replace("_", "");
        // Strip suffix characters for parsing
        var numericText = intText.TrimEnd('U', 'u', 'L', 'l');

        if (isUnsignedSuffix)
        {
            if (ulong.TryParse(numericText, out var ulVal))
            {
                return MakeToken(TokenKind.IntLiteral,
                    new IntLiteralInfo(unchecked((long)ulVal), IsHex: false, IsUnsigned: true, ulVal));
            }
        }
        else if (hasLongSuffix)
        {
            if (long.TryParse(numericText, out var lVal))
            {
                return MakeToken(TokenKind.IntLiteral, lVal);
            }
        }
        else
        {
            if (int.TryParse(numericText, out var intValue))
            {
                return MakeToken(TokenKind.IntLiteral, intValue);
            }

            // Fall back to long for values outside int range
            if (long.TryParse(numericText, out var longValue))
            {
                return MakeToken(TokenKind.IntLiteral, longValue);
            }
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
