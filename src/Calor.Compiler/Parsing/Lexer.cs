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
        ["NS"] = TokenKind.Namespace,        // §NS = Namespace scope

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
        ["/NS"] = TokenKind.EndNamespace,   // §/NS

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
        ["SEMVER"] = TokenKind.SemVer,      // §SEMVER{MAJOR.MINOR.PATCH} = semantics-version declaration (lexed by ScanSemVerDirective; listed here for the keyword registry / doc-drift check)
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
        ["/INIT"] = TokenKind.EndInit,
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
        ["when"] = TokenKind.When,       // Accept lowercase from legacy converter output

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
        ["PTYPE"] = TokenKind.TypePattern,
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
        ["CDIR"] = TokenKind.CompilerDirective,      // §CDIR{base64}

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

    /// <summary>
    /// Every §-keyword the lexer accepts, including forms handled outside the
    /// keyword dictionary: preprocessor conditionals (§PP / §/PP / §PPE),
    /// inline C# expressions (§CS), raw passthrough blocks (§RAW / §/RAW),
    /// and C# interop blocks (§CSHARP / §/CSHARP). Single source of truth for
    /// the docs-drift check (<c>calor self-check docs</c>).
    /// </summary>
    public static IReadOnlyCollection<string> KeywordNames { get; } =
        Keywords.Keys
            .Concat(["PP", "/PP", "PPE", "CS", "RAW", "/RAW", "CSHARP", "/CSHARP"])
            .ToArray();

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
            if (Current == '\r')
            {
                _line++;
                _column = 1;
            }
            else if (Current == '\n')
            {
                if (_position == 0 || _source[_position - 1] != '\r')
                {
                    _line++;
                }
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

    /// <summary>
    /// Phase 3 (RFC §4.1) — tokenize with INDENT/DEDENT markers derived
    /// from leading-whitespace deltas at line boundaries. Brackets / parens
    /// / braces suppress indent tracking (implicit line continuation).
    /// Mixed tabs+spaces in the leading whitespace of a single indented
    /// line emits <see cref="DiagnosticCode.MixedIndentation"/>.
    ///
    /// This method is opt-in. The plain <see cref="Tokenize"/> entry point
    /// is unchanged (Phase 1 compatibility).
    /// </summary>
    public IEnumerable<Token> TokenizeWithIndent()
    {
        // Collect raw tokens including Newline + Whitespace.
        var raw = new List<Token>();
        while (!IsAtEnd)
        {
            var token = NextToken();
            raw.Add(token);
        }
        StartToken();
        raw.Add(MakeToken(TokenKind.Eof));

        return PostProcessIndent(raw);
    }

    private IEnumerable<Token> PostProcessIndent(List<Token> raw)
    {
        var indentStack = new Stack<int>();
        indentStack.Push(0);

        int bracketDepth = 0;
        bool atLineStart = true;
        int currentIndent = 0;
        bool sawMixedIndent = false;
        bool currentIndentHasTab = false;

        // Aggregated fixable indentation issues (reported once per file, at
        // EOF, with one machine-applicable edit per offending line so a
        // single fix application heals the whole file).
        var tabIndentEdits = new List<TextEdit>();
        var widthEdits = new List<TextEdit>();
        Token? firstTabIndentTok = null;
        Token? firstWidthTok = null;
        var filePath = _diagnostics.CurrentFilePath ?? "";

        for (int i = 0; i < raw.Count; i++)
        {
            var tok = raw[i];

            if (atLineStart && tok.Kind == TokenKind.Whitespace)
            {
                // Measure indent. Tabs count as 1; spaces count as 1.
                // Mixed tabs+spaces in a single line's leading whitespace
                // triggers Calor0099.
                var text = tok.Text;
                bool hasTab = text.Contains('\t');
                bool hasSpace = text.Contains(' ');
                if (hasTab && hasSpace && !sawMixedIndent)
                {
                    sawMixedIndent = true;
                    var fix = new SuggestedFix(
                        "Replace leading whitespace with spaces (each tab becomes 2 spaces)",
                        TextEdit.Replace(filePath, tok.Span.Line, 1,
                            tok.Span.Line, text.Length + 1, ExpandIndentTabs(text)));
                    _diagnostics.ReportErrorWithFix(tok.Span, DiagnosticCode.MixedIndentation,
                        "Mixed tabs and spaces in leading whitespace. Use one or the other consistently.",
                        fix);
                }
                else if (hasTab && !hasSpace)
                {
                    // Tab-only indentation: tolerated (each tab counts as one
                    // column) but non-canonical and a frequent source of
                    // dedent mismatches. Collect one edit per line; reported
                    // once at EOF as Calor0008 with all edits in a single fix.
                    firstTabIndentTok ??= tok;
                    tabIndentEdits.Add(TextEdit.Replace(filePath, tok.Span.Line, 1,
                        tok.Span.Line, text.Length + 1, ExpandIndentTabs(text)));
                }
                currentIndentHasTab = hasTab;
                currentIndent = text.Length;
                continue; // do not emit whitespace
            }

            if (tok.Kind == TokenKind.Newline)
            {
                if (bracketDepth == 0)
                {
                    yield return tok;
                    atLineStart = true;
                    currentIndent = 0;
                    currentIndentHasTab = false;
                }
                else
                {
                    // Inside brackets: swallow newlines as implicit continuation.
                }
                continue;
            }

            // Skip whitespace mid-line.
            if (tok.Kind == TokenKind.Whitespace)
            {
                continue;
            }

            // First non-trivia token on a line: emit INDENT/DEDENT as needed
            // (only at bracketDepth == 0).
            if (atLineStart && tok.Kind != TokenKind.Eof && bracketDepth == 0)
            {
                int top = indentStack.Peek();
                if (currentIndent > top)
                {
                    indentStack.Push(currentIndent);
                    yield return new Token(TokenKind.Indent, "",
                        new TextSpan(tok.Span.Start, 0, tok.Span.Line, 1));
                }
                else
                {
                    int lastPopped = top;
                    while (indentStack.Peek() > currentIndent)
                    {
                        lastPopped = indentStack.Pop();
                        yield return new Token(TokenKind.Dedent, "",
                            new TextSpan(
                                tok.Span.Start,
                                0,
                                tok.Span.Line,
                                indentStack.Peek() + 1));
                    }
                    if (indentStack.Peek() != currentIndent)
                    {
                        if (!sawMixedIndent)
                        {
                            sawMixedIndent = true;
                            // Snap to the nearest enclosing level: either the
                            // level just below (stack top) or the one just
                            // above (last popped). Ties prefer the deeper
                            // level (an off-by-one dedent usually means the
                            // line was meant to stay inside the block).
                            int below = indentStack.Peek();
                            int target = (currentIndent - below) < (lastPopped - currentIndent)
                                ? below
                                : lastPopped;
                            var fix = new SuggestedFix(
                                $"Re-indent line to the enclosing indent level ({target} spaces)",
                                TextEdit.Replace(filePath, tok.Span.Line, 1,
                                    tok.Span.Line, currentIndent + 1, new string(' ', target)));
                            _diagnostics.ReportErrorWithFix(tok.Span, DiagnosticCode.MixedIndentation,
                                $"Dedent to column {currentIndent} does not match any enclosing indent level.",
                                fix);
                        }
                    }
                }

                // Non-standard indent width (e.g. 3- or 4-space levels). The
                // file still parses — indentation is stack-relative — but the
                // line is not at its canonical 2-spaces-per-level column.
                // Collect one edit per offending line (siblings included);
                // level count is width-independent, so applying all edits in
                // one pass heals the whole file. Reported once at EOF as
                // Calor0009. Tab-indented lines are handled by Calor0008 and
                // dedent mismatches by the Calor0099 fix above.
                if (!currentIndentHasTab && indentStack.Peek() == currentIndent)
                {
                    int canonical = 2 * (indentStack.Count - 1);
                    if (canonical != currentIndent)
                    {
                        firstWidthTok ??= tok;
                        widthEdits.Add(TextEdit.Replace(filePath, tok.Span.Line, 1,
                            tok.Span.Line, currentIndent + 1, new string(' ', canonical)));
                    }
                }
                atLineStart = false;
            }

            // Track bracket depth (() [] {}).
            switch (tok.Kind)
            {
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                case TokenKind.OpenBrace:
                    bracketDepth++;
                    break;
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                case TokenKind.CloseBrace:
                    if (bracketDepth > 0) bracketDepth--;
                    break;
            }

            if (tok.Kind == TokenKind.Eof)
            {
                // Report aggregated fixable indentation issues once per file.
                if (firstTabIndentTok is { } tabTok)
                {
                    _diagnostics.ReportWarningWithFix(tabTok.Span, DiagnosticCode.TabIndentation,
                        $"Leading whitespace uses tabs on {tabIndentEdits.Count} line(s). " +
                        "Calor indentation is canonically 2 spaces per level.",
                        new SuggestedFix(
                            "Replace tab indentation with spaces (each tab becomes 2 spaces)",
                            tabIndentEdits));
                }
                if (firstWidthTok is { } widthTok)
                {
                    _diagnostics.ReportWarningWithFix(widthTok.Span, DiagnosticCode.NonStandardIndentWidth,
                        $"Indentation step is not 2 spaces on {widthEdits.Count} line(s). " +
                        "Calor indentation is canonically 2 spaces per level.",
                        new SuggestedFix(
                            "Re-indent to 2 spaces per level",
                            widthEdits));
                }

                // Drain remaining indent stack.
                while (indentStack.Count > 1)
                {
                    indentStack.Pop();
                    yield return new Token(TokenKind.Dedent, "",
                        new TextSpan(
                            tok.Span.Start,
                            0,
                            tok.Span.Line,
                            indentStack.Peek() + 1));
                }
                // Phase 3 (indent-aware): always emit one final implicit
                // Dedent at EOF. This lets the outermost block (typically
                // §M{...}) terminate naturally in indent form without
                // requiring an explicit §/M. Closer-form code parsed via
                // TokenizeWithIndent will have already consumed its §/M
                // before this token; the trailing Dedent is harmless because
                // Parse() does not inspect tokens after ParseModule returns.
                yield return new Token(TokenKind.Dedent, "",
                    new TextSpan(tok.Span.Start, 0, tok.Span.Line, 1));
                yield return tok;
                yield break;
            }

            yield return tok;
        }
    }

    /// <summary>
    /// Expands leading-whitespace tabs to 2 spaces each (spaces preserved).
    /// Used to build machine-applicable indentation fixes.
    /// </summary>
    internal static string ExpandIndentTabs(string leadingWhitespace)
        => leadingWhitespace.Replace("\t", "  ", StringComparison.Ordinal);

    public List<Token> TokenizeWithIndentAll()
        => TokenizeWithIndent().ToList();

    /// <summary>
    /// Phase 1b (RFC §4.2) — parser-ready indent-aware token stream:
    /// <see cref="TokenizeWithIndent"/> output minus <see cref="TokenKind.Newline"/>
    /// and <see cref="TokenKind.Indent"/> tokens. The parser is newline-insensitive
    /// (every statement is anchored by a § marker) and treats <c>Indent</c> as
    /// decorative (every block-opener is already an explicit tag). Only
    /// <see cref="TokenKind.Dedent"/> is structurally significant — it signals
    /// block-end alongside the legacy explicit closers (§/F, §/M, etc.).
    ///
    /// This is the production entry point for the indent-aware compiler
    /// pipeline. Closer-form source still works because the parser's
    /// <c>ExpectBlockEnd</c> helper consumes Dedent followed by the
    /// explicit closer in sequence.
    /// </summary>
    public List<Token> TokenizeAllForParser()
        => TokenizeWithIndent()
            .Where(t => t.Kind != TokenKind.Newline && t.Kind != TokenKind.Indent)
            .ToList();

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
            '$' => ScanDollarString(),
            ';' => ScanSkipSemicolon(),
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
            // Only \n terminates (not bare \r) to handle embedded \r in doc comments
            while (Current != '\n' && Current != '\0')
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

        // Special handling for §SEMVER{MAJOR.MINOR.PATCH}: capture the version text
        // verbatim (a dotted triple would otherwise lex as float/int fragments) and
        // reject the bracket / brace-less / unterminated shapes right here.
        if (fullKeyword.Equals("SEMVER", StringComparison.Ordinal))
        {
            return ScanSemVerDirective();
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
        if (Current == '\r' && Lookahead == '\n')
        {
            Advance();
            Advance();
        }
        else if (Current == '\n')
        {
            Advance();
        }

        var contentStart = _position;
        const string endMarker = "§/RAW";
        var preprocessorStack = new List<RawPreprocessorFrame>();
        var preprocessorSymbols = new Dictionary<string, bool>(StringComparer.Ordinal);

        while (!IsAtEnd)
        {
            var regionActive = IsRawPreprocessorRegionActive(preprocessorStack);
            if (regionActive
                && preprocessorStack.Count == 0
                && MatchesAtCurrent(endMarker))
            {
                var rawContent = _source[contentStart.._position];
                for (int i = 0; i < endMarker.Length; i++)
                    Advance();
                return MakeToken(TokenKind.RawCSharp, rawContent);
            }

            if (IsAtCSharpPreprocessorDirective())
            {
                ProcessRawPreprocessorDirective(preprocessorStack, preprocessorSymbols);
                if (IsRawPreprocessorRegionActive(preprocessorStack))
                    SkipCSharpPreprocessorDirective();
                else
                    SkipDisabledPreprocessorDirectiveLine();
                continue;
            }
            if (regionActive && TrySkipCSharpLexicalConstruct())
                continue;
            if (!regionActive && TrySkipDisabledCSharpLexicalConstruct())
                continue;
            Advance();
        }

        // Reached end of file without finding §/RAW
        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedRawBlock,
            "Unterminated §RAW block: expected §/RAW before end of file.");
        return MakeToken(TokenKind.Error);
    }

    private enum PreprocessorTruth
    {
        False,
        True,
        Unknown
    }

    private readonly record struct RawPreprocessorFrame(
        bool ParentActive,
        bool BranchTaken,
        bool CurrentActive,
        bool SeenElse);

    private static bool IsRawPreprocessorRegionActive(
        IReadOnlyList<RawPreprocessorFrame> stack)
        => stack.Count == 0 || stack[^1].CurrentActive;

    private void ProcessRawPreprocessorDirective(
        List<RawPreprocessorFrame> stack,
        Dictionary<string, bool> symbols)
    {
        var (keyword, argument) = ReadRawPreprocessorDirective();
        var currentActive = IsRawPreprocessorRegionActive(stack);

        switch (keyword)
        {
            case "if":
            {
                var selected = EvaluatePreprocessorCondition(argument, symbols)
                    != PreprocessorTruth.False;
                stack.Add(new RawPreprocessorFrame(
                    currentActive,
                    selected,
                    currentActive && selected,
                    SeenElse: false));
                break;
            }
            case "elif" when stack.Count > 0:
            {
                var frame = stack[^1];
                var selected = !frame.SeenElse
                    && !frame.BranchTaken
                    && EvaluatePreprocessorCondition(argument, symbols)
                        != PreprocessorTruth.False;
                stack[^1] = frame with
                {
                    BranchTaken = frame.BranchTaken || selected,
                    CurrentActive = frame.ParentActive && selected
                };
                break;
            }
            case "else" when stack.Count > 0:
            {
                var frame = stack[^1];
                var selected = !frame.SeenElse && !frame.BranchTaken;
                stack[^1] = frame with
                {
                    BranchTaken = true,
                    CurrentActive = frame.ParentActive && selected,
                    SeenElse = true
                };
                break;
            }
            case "endif" when stack.Count > 0:
                stack.RemoveAt(stack.Count - 1);
                break;
            case "define" when currentActive:
            {
                var symbol = ReadPreprocessorSymbol(argument);
                if (symbol.Length > 0)
                    symbols[symbol] = true;
                break;
            }
            case "undef" when currentActive:
            {
                var symbol = ReadPreprocessorSymbol(argument);
                if (symbol.Length > 0)
                    symbols[symbol] = false;
                break;
            }
        }
    }

    private (string Keyword, string Argument) ReadRawPreprocessorDirective()
    {
        var cursor = _position + 1;
        while (cursor < _source.Length && _source[cursor] is ' ' or '\t')
            cursor++;

        var keywordStart = cursor;
        while (cursor < _source.Length && char.IsLetter(_source[cursor]))
            cursor++;
        var keyword = _source[keywordStart..cursor];

        while (cursor < _source.Length && _source[cursor] is ' ' or '\t')
            cursor++;
        var argumentStart = cursor;
        while (cursor < _source.Length && _source[cursor] is not '\r' and not '\n')
            cursor++;
        return (keyword, _source[argumentStart..cursor]);
    }

    private static string ReadPreprocessorSymbol(string argument)
    {
        var text = argument.AsSpan().TrimStart();
        var length = 0;
        while (length < text.Length
            && (char.IsLetterOrDigit(text[length]) || text[length] == '_'))
        {
            length++;
        }
        return text[..length].ToString();
    }

    private static PreprocessorTruth EvaluatePreprocessorCondition(
        string condition,
        IReadOnlyDictionary<string, bool> symbols)
    {
        var position = 0;
        return ParsePreprocessorOr(condition, symbols, ref position);
    }

    private static PreprocessorTruth ParsePreprocessorOr(
        string text,
        IReadOnlyDictionary<string, bool> symbols,
        ref int position)
    {
        var value = ParsePreprocessorAnd(text, symbols, ref position);
        while (MatchPreprocessorToken(text, "||", ref position))
        {
            value = OrPreprocessorTruth(
                value,
                ParsePreprocessorAnd(text, symbols, ref position));
        }
        return value;
    }

    private static PreprocessorTruth ParsePreprocessorAnd(
        string text,
        IReadOnlyDictionary<string, bool> symbols,
        ref int position)
    {
        var value = ParsePreprocessorEquality(text, symbols, ref position);
        while (MatchPreprocessorToken(text, "&&", ref position))
        {
            value = AndPreprocessorTruth(
                value,
                ParsePreprocessorEquality(text, symbols, ref position));
        }
        return value;
    }

    private static PreprocessorTruth ParsePreprocessorEquality(
        string text,
        IReadOnlyDictionary<string, bool> symbols,
        ref int position)
    {
        var value = ParsePreprocessorUnary(text, symbols, ref position);
        while (true)
        {
            if (MatchPreprocessorToken(text, "==", ref position))
            {
                value = EqualPreprocessorTruth(
                    value,
                    ParsePreprocessorUnary(text, symbols, ref position),
                    negate: false);
            }
            else if (MatchPreprocessorToken(text, "!=", ref position))
            {
                value = EqualPreprocessorTruth(
                    value,
                    ParsePreprocessorUnary(text, symbols, ref position),
                    negate: true);
            }
            else
            {
                return value;
            }
        }
    }

    private static PreprocessorTruth ParsePreprocessorUnary(
        string text,
        IReadOnlyDictionary<string, bool> symbols,
        ref int position)
    {
        SkipPreprocessorWhitespace(text, ref position);
        if (MatchPreprocessorToken(text, "!", ref position))
        {
            return ParsePreprocessorUnary(text, symbols, ref position) switch
            {
                PreprocessorTruth.True => PreprocessorTruth.False,
                PreprocessorTruth.False => PreprocessorTruth.True,
                _ => PreprocessorTruth.Unknown
            };
        }

        if (MatchPreprocessorToken(text, "(", ref position))
        {
            var parenthesized = ParsePreprocessorOr(text, symbols, ref position);
            _ = MatchPreprocessorToken(text, ")", ref position);
            return parenthesized;
        }

        var identifier = ReadPreprocessorIdentifier(text, ref position);
        if (identifier is "true" or "1")
            return PreprocessorTruth.True;
        if (identifier is "false" or "0")
            return PreprocessorTruth.False;
        if (identifier == "defined")
        {
            _ = MatchPreprocessorToken(text, "(", ref position);
            var symbol = ReadPreprocessorIdentifier(text, ref position);
            _ = MatchPreprocessorToken(text, ")", ref position);
            return symbols.TryGetValue(symbol, out var isDefined)
                ? isDefined ? PreprocessorTruth.True : PreprocessorTruth.False
                : PreprocessorTruth.Unknown;
        }
        if (identifier.Length == 0)
            return PreprocessorTruth.Unknown;
        return symbols.TryGetValue(identifier, out var value)
            ? value ? PreprocessorTruth.True : PreprocessorTruth.False
            : PreprocessorTruth.Unknown;
    }

    private static string ReadPreprocessorIdentifier(string text, ref int position)
    {
        SkipPreprocessorWhitespace(text, ref position);
        var start = position;
        while (position < text.Length
            && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position++;
        }
        return text[start..position];
    }

    private static bool MatchPreprocessorToken(
        string text,
        string token,
        ref int position)
    {
        SkipPreprocessorWhitespace(text, ref position);
        if (!text.AsSpan(position).StartsWith(token, StringComparison.Ordinal))
            return false;
        position += token.Length;
        return true;
    }

    private static void SkipPreprocessorWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
    }

    private static PreprocessorTruth AndPreprocessorTruth(
        PreprocessorTruth left,
        PreprocessorTruth right)
    {
        if (left == PreprocessorTruth.False || right == PreprocessorTruth.False)
            return PreprocessorTruth.False;
        return left == PreprocessorTruth.True && right == PreprocessorTruth.True
            ? PreprocessorTruth.True
            : PreprocessorTruth.Unknown;
    }

    private static PreprocessorTruth OrPreprocessorTruth(
        PreprocessorTruth left,
        PreprocessorTruth right)
    {
        if (left == PreprocessorTruth.True || right == PreprocessorTruth.True)
            return PreprocessorTruth.True;
        return left == PreprocessorTruth.False && right == PreprocessorTruth.False
            ? PreprocessorTruth.False
            : PreprocessorTruth.Unknown;
    }

    private static PreprocessorTruth EqualPreprocessorTruth(
        PreprocessorTruth left,
        PreprocessorTruth right,
        bool negate)
    {
        if (left == PreprocessorTruth.Unknown || right == PreprocessorTruth.Unknown)
            return PreprocessorTruth.Unknown;
        var equal = left == right;
        return equal != negate ? PreprocessorTruth.True : PreprocessorTruth.False;
    }

    private void SkipDisabledPreprocessorDirectiveLine()
    {
        while (!IsAtEnd && Current is not '\r' and not '\n')
            Advance();
    }

    private bool TrySkipDisabledCSharpLexicalConstruct()
    {
        var cursor = _position;
        if (!TryFindDisabledCSharpLexicalConstruct(ref cursor))
            return false;

        while (_position < cursor)
            Advance();
        return true;
    }

    private bool TryFindDisabledCSharpLexicalConstruct(ref int cursor)
    {
        if (cursor + 1 < _source.Length
            && _source[cursor] == '/'
            && _source[cursor + 1] == '/')
        {
            cursor += 2;
            while (cursor < _source.Length
                && _source[cursor] is not '\r' and not '\n')
            {
                cursor++;
            }
            return true;
        }

        if (cursor + 1 < _source.Length
            && _source[cursor] == '/'
            && _source[cursor + 1] == '*')
        {
            var end = _source.IndexOf("*/", cursor + 2, StringComparison.Ordinal);
            if (end < 0)
                return false;
            cursor = end + 2;
            return true;
        }

        if (_source[cursor] == '\'')
            return TryFindDisabledCSharpCharEnd(ref cursor);

        if (IsCSharpStringStartAt(cursor))
            return TryFindDisabledCSharpStringEnd(ref cursor);

        return false;
    }

    private bool TryFindDisabledCSharpCharEnd(ref int cursor)
    {
        cursor++;
        while (cursor < _source.Length)
        {
            if (_source[cursor] is '\r' or '\n')
                return false;
            if (_source[cursor] == '\\')
            {
                cursor += Math.Min(2, _source.Length - cursor);
                continue;
            }
            if (_source[cursor] == '\'')
            {
                cursor++;
                return true;
            }
            cursor++;
        }
        return false;
    }

    private bool IsCSharpStringStartAt(int position)
    {
        var cursor = position;
        while (cursor < _source.Length && _source[cursor] == '$')
            cursor++;
        if (cursor < _source.Length && _source[cursor] == '@')
        {
            cursor++;
            if (cursor < _source.Length && _source[cursor] == '$')
                cursor++;
        }
        return cursor < _source.Length && _source[cursor] == '"';
    }

    private bool TryFindDisabledCSharpStringEnd(ref int cursor)
    {
        var dollarCount = 0;
        while (cursor < _source.Length && _source[cursor] == '$')
        {
            dollarCount++;
            cursor++;
        }

        var verbatim = false;
        if (cursor < _source.Length && _source[cursor] == '@')
        {
            verbatim = true;
            cursor++;
            if (dollarCount == 0
                && cursor < _source.Length
                && _source[cursor] == '$')
            {
                dollarCount = 1;
                cursor++;
            }
        }

        if (cursor >= _source.Length || _source[cursor] != '"')
            return false;

        var quoteCount = CountRunAt(cursor, '"');
        if (quoteCount >= 3)
        {
            cursor += quoteCount;
            return TryFindDisabledCSharpRawStringEnd(
                ref cursor,
                quoteCount,
                dollarCount);
        }

        cursor++;
        return TryFindDisabledCSharpOrdinaryStringEnd(
            ref cursor,
            verbatim,
            dollarCount > 0);
    }

    private bool TryFindDisabledCSharpOrdinaryStringEnd(
        ref int cursor,
        bool verbatim,
        bool interpolated)
    {
        while (cursor < _source.Length)
        {
            if (!verbatim && _source[cursor] is '\r' or '\n')
                return false;
            if (verbatim
                && cursor + 1 < _source.Length
                && _source[cursor] == '"'
                && _source[cursor + 1] == '"')
            {
                cursor += 2;
                continue;
            }
            if (!verbatim && _source[cursor] == '\\')
            {
                cursor += Math.Min(2, _source.Length - cursor);
                continue;
            }
            if (_source[cursor] == '"')
            {
                cursor++;
                return true;
            }
            if (interpolated && _source[cursor] == '{')
            {
                if (cursor + 1 < _source.Length && _source[cursor + 1] == '{')
                {
                    cursor += 2;
                    continue;
                }
                cursor++;
                if (!TryFindDisabledCSharpInterpolationEnd(ref cursor, 1))
                    return false;
                continue;
            }
            if (interpolated
                && cursor + 1 < _source.Length
                && _source[cursor] == '}'
                && _source[cursor + 1] == '}')
            {
                cursor += 2;
                continue;
            }
            cursor++;
        }
        return false;
    }

    private bool TryFindDisabledCSharpRawStringEnd(
        ref int cursor,
        int quoteCount,
        int dollarCount)
    {
        while (cursor < _source.Length)
        {
            if (CountRunAt(cursor, '"') >= quoteCount)
            {
                cursor += quoteCount;
                return true;
            }
            if (dollarCount > 0
                && CountRunAt(cursor, '{') >= dollarCount)
            {
                cursor += dollarCount;
                if (!TryFindDisabledCSharpInterpolationEnd(
                    ref cursor,
                    dollarCount))
                {
                    return false;
                }
                continue;
            }
            cursor++;
        }
        return false;
    }

    private bool TryFindDisabledCSharpInterpolationEnd(
        ref int cursor,
        int closingBraceCount)
    {
        var depth = 1;
        while (cursor < _source.Length)
        {
            if (IsDisabledCSharpLexicalStartAt(cursor))
            {
                if (!TryFindDisabledCSharpLexicalConstruct(ref cursor))
                    return false;
                continue;
            }

            if (_source[cursor] == '{')
            {
                depth++;
                cursor++;
                continue;
            }
            if (_source[cursor] == '}')
            {
                if (depth == 1
                    && CountRunAt(cursor, '}') >= closingBraceCount)
                {
                    cursor += closingBraceCount;
                    return true;
                }
                depth--;
                cursor++;
                continue;
            }
            cursor++;
        }
        return false;
    }

    private bool IsDisabledCSharpLexicalStartAt(int position)
        => _source[position] == '\''
            || IsCSharpStringStartAt(position)
            || position + 1 < _source.Length
                && _source[position] == '/'
                && _source[position + 1] is '/' or '*';

    private int CountRunAt(int position, char character)
    {
        var count = 0;
        while (position + count < _source.Length
            && _source[position + count] == character)
        {
            count++;
        }
        return count;
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

        var contentStart = _position;
        const string endMarker = "}§/CSHARP";
        var depth = 1;

        while (!IsAtEnd)
        {
            if (TrySkipCSharpLexicalConstruct())
                continue;
            // The explicit marker is authoritative. Conditional C# branches can
            // contain mutually exclusive brace shapes, so counting braces across
            // every branch is not a valid way to decide whether the marker is at
            // the outer level.
            if (MatchesAtCurrent(endMarker))
            {
                var rawContent = _source[contentStart.._position];
                for (int i = 0; i < endMarker.Length; i++)
                    Advance();
                return MakeToken(TokenKind.CSharpInterop, rawContent);
            }

            if (Current == '{')
                depth++;
            else if (Current == '}')
                depth--;
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
            if (TrySkipCSharpLexicalConstruct())
                continue;
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

    private bool MatchesAtCurrent(string text)
        => _position + text.Length <= _source.Length
            && _source.AsSpan(_position, text.Length).SequenceEqual(text);

    private bool TrySkipCSharpLexicalConstruct()
    {
        if (Current == '/' && Lookahead == '/')
        {
            Advance();
            Advance();
            while (!IsAtEnd && Current is not '\r' and not '\n')
                Advance();
            return true;
        }

        if (Current == '/' && Lookahead == '*')
        {
            Advance();
            Advance();
            while (!IsAtEnd && !(Current == '*' && Lookahead == '/'))
                Advance();
            if (!IsAtEnd)
            {
                Advance();
                Advance();
            }
            return true;
        }

        if (Current == '\'')
        {
            Advance();
            while (!IsAtEnd)
            {
                if (Current == '\\')
                {
                    Advance();
                    if (!IsAtEnd)
                        Advance();
                    continue;
                }
                var closes = Current == '\'';
                Advance();
                if (closes)
                    break;
            }
            return true;
        }

        return TrySkipCSharpString();
    }

    private bool IsAtCSharpPreprocessorDirective()
    {
        if (Current != '#')
            return false;

        var cursor = _position - 1;
        while (cursor >= 0 && _source[cursor] is ' ' or '\t')
            cursor--;
        return cursor < 0 || _source[cursor] is '\r' or '\n';
    }

    private void SkipCSharpPreprocessorDirective()
    {
        while (!IsAtEnd && Current is not '\r' and not '\n')
        {
            if (TrySkipCSharpLexicalConstruct())
                continue;

            if (Current == '§')
                return;
            Advance();
        }
    }

    private bool TrySkipCSharpString()
    {
        var cursor = _position;
        var dollarCount = 0;
        var verbatim = false;

        while (cursor < _source.Length && _source[cursor] == '$')
        {
            dollarCount++;
            cursor++;
        }

        if (cursor < _source.Length && _source[cursor] == '@')
        {
            verbatim = true;
            cursor++;
            if (dollarCount == 0 && cursor < _source.Length && _source[cursor] == '$')
            {
                dollarCount = 1;
                cursor++;
            }
        }

        if (cursor >= _source.Length || _source[cursor] != '"')
            return false;

        while (_position < cursor)
            Advance();

        var quoteCount = CountRun('"');
        if (quoteCount >= 3)
        {
            SkipCSharpRawString(quoteCount, dollarCount);
            return true;
        }

        Advance();
        SkipCSharpOrdinaryString(verbatim, dollarCount > 0);
        return true;
    }

    private void SkipCSharpOrdinaryString(bool verbatim, bool interpolated)
    {
        var interpolationDepth = 0;
        while (!IsAtEnd)
        {
            if (interpolationDepth > 0)
            {
                if (TrySkipCSharpLexicalConstruct())
                    continue;
                if (Current == '{')
                {
                    interpolationDepth++;
                    Advance();
                    continue;
                }
                if (Current == '}')
                {
                    interpolationDepth--;
                    Advance();
                    continue;
                }
                Advance();
                continue;
            }

            if (verbatim && Current == '"' && Lookahead == '"')
            {
                Advance();
                Advance();
                continue;
            }
            if (!verbatim && Current == '\\')
            {
                Advance();
                if (!IsAtEnd)
                    Advance();
                continue;
            }
            if (interpolated && Current == '{')
            {
                if (Lookahead == '{')
                {
                    Advance();
                    Advance();
                }
                else
                {
                    interpolationDepth = 1;
                    Advance();
                }
                continue;
            }
            if (interpolated && Current == '}' && Lookahead == '}')
            {
                Advance();
                Advance();
                continue;
            }

            var closes = Current == '"';
            Advance();
            if (closes)
                return;
        }
    }

    private void SkipCSharpRawString(int quoteCount, int dollarCount)
    {
        for (var i = 0; i < quoteCount; i++)
            Advance();

        if (dollarCount == 0)
        {
            while (!IsAtEnd)
            {
                if (CountRun('"') >= quoteCount)
                {
                    for (var i = 0; i < quoteCount; i++)
                        Advance();
                    return;
                }
                Advance();
            }
            return;
        }

        var interpolationDepth = 0;
        while (!IsAtEnd)
        {
            if (interpolationDepth == 0)
            {
                if (CountRun('"') >= quoteCount)
                {
                    for (var i = 0; i < quoteCount; i++)
                        Advance();
                    return;
                }
                if (CountRun('{') >= dollarCount)
                {
                    for (var i = 0; i < dollarCount; i++)
                        Advance();
                    interpolationDepth = 1;
                    continue;
                }
                Advance();
                continue;
            }

            if (TrySkipCSharpLexicalConstruct())
                continue;
            if (Current == '{')
            {
                interpolationDepth++;
                Advance();
                continue;
            }
            if (Current == '}' && CountRun('}') >= dollarCount && interpolationDepth == 1)
            {
                for (var i = 0; i < dollarCount; i++)
                    Advance();
                interpolationDepth = 0;
                continue;
            }
            if (Current == '}')
                interpolationDepth--;
            Advance();
        }
    }

    private int CountRun(char character)
    {
        var count = 0;
        while (_position + count < _source.Length
            && _source[_position + count] == character)
        {
            count++;
        }
        return count;
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
    /// Scans the argument of <c>§SEMVER</c>. Only <c>§SEMVER{...}</c> closed on the
    /// same line is a well-formed directive: the token's Value is then the raw brace
    /// content and the parser validates it (Calor0700/0701/0702). Every other shape —
    /// the legacy bracket form <c>§SEMVER[...]</c>, a missing brace group, or a brace
    /// group not closed on its line — is reported here as Calor0702 and yields a
    /// <see cref="TokenKind.SemVer"/> token with a null Value so the parser does not
    /// report it a second time. Unlike <see cref="ScanBraceContent"/> this never scans
    /// past the end of the line, so an unterminated directive cannot swallow the
    /// statements that follow it.
    /// </summary>
    private Token ScanSemVerDirective()
    {
        if (Current == '{')
        {
            Advance(); // consume '{'
            var contentStart = _position;
            while (!IsAtEnd && Current != '}' && Current != '\n' && Current != '\r')
            {
                Advance();
            }

            if (!IsAtEnd && Current == '}')
            {
                var content = _source[contentStart.._position];
                Advance(); // consume '}'
                return MakeToken(TokenKind.SemVer, content);
            }

            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.SemanticsVersionInvalidDeclaration,
                "Unterminated §SEMVER directive: expected '}' on the same line, as in §SEMVER{MAJOR.MINOR.PATCH}.");
            return MakeToken(TokenKind.SemVer);
        }

        if (Current == '[')
        {
            while (!IsAtEnd && Current != ']' && Current != '\n' && Current != '\r')
            {
                Advance();
            }
            if (!IsAtEnd && Current == ']')
            {
                Advance(); // consume ']'
            }

            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.SemanticsVersionInvalidDeclaration,
                "Invalid §SEMVER declaration: the bracket form §SEMVER[...] is not accepted; use braces: §SEMVER{MAJOR.MINOR.PATCH}.");
            return MakeToken(TokenKind.SemVer);
        }

        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.SemanticsVersionInvalidDeclaration,
            "Invalid §SEMVER declaration: expected §SEMVER{MAJOR.MINOR.PATCH}.");
        return MakeToken(TokenKind.SemVer);
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
            var span = CurrentSpan();
            var filePath = _diagnostics.CurrentFilePath ?? "";
            var fix = new Diagnostics.SuggestedFix(
                $"Replace '§{keyword}' with '§{suggestion}'",
                Diagnostics.TextEdit.Replace(filePath, span.Line, span.Column,
                    span.Line, span.Column + span.Length,
                    $"§{suggestion}"));
            _diagnostics.ReportErrorWithFix(span, Diagnostics.DiagnosticCode.UnknownSectionMarker,
                $"Unknown section marker '§{keyword}'. Did you mean '§{suggestion}'{description}?",
                fix);
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
        // Skip typed literal detection inside attribute blocks ({...}) where colon is a separator
        // e.g., §SALLOC{dec:4} should parse as type=dec, size=4, not as DECIMAL literal 4
        var prevChar = _tokenStart > 0 ? _source[_tokenStart - 1] : '\0';
        if (Current == ':' && prevChar != '{' && prevChar != ':')
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
                // Extra check: make sure it's exactly "true" or "false", not an identifier
                // like "trueTestPermits..." — require word boundary after the literal
                var remaining = _source[(_position + 1)..];
                if (remaining.StartsWith("true") && (remaining.Length == 4 || !char.IsLetterOrDigit(remaining[4])))
                {
                    return ScanTypedBoolLiteral();
                }
                if (remaining.StartsWith("false") && (remaining.Length == 5 || !char.IsLetterOrDigit(remaining[5])))
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
            // SINGLE:digits (single-precision float literal, #774 width preservation)
            if (upperText == "SINGLE" && (char.IsDigit(lookahead) || lookahead == '-' || lookahead == '.'))
            {
                return ScanTypedFloatLiteral(isSingle: true);
            }
            // LONG:digits / UINT:digits / ULONG:digits (explicit int width/signedness, #774)
            if (upperText == "LONG" && (char.IsDigit(lookahead) || lookahead == '-'))
            {
                return ScanTypedIntLiteral(isUnsigned: false, isLong: true);
            }
            if (upperText == "UINT" && (char.IsDigit(lookahead) || lookahead == '-'))
            {
                return ScanTypedIntLiteral(isUnsigned: true, isLong: false);
            }
            if (upperText == "ULONG" && (char.IsDigit(lookahead) || lookahead == '-'))
            {
                return ScanTypedIntLiteral(isUnsigned: true, isLong: true);
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

    private Token ScanTypedIntLiteral(bool isUnsigned = false, bool isLong = false)
    {
        Advance(); // consume ':'
        var sign = IntegerLiteralSign.Positive;
        if (Current == '-')
        {
            sign = IntegerLiteralSign.Negative;
            Advance();
        }

        var literalBase = IntegerLiteralBase.Decimal;
        if (Current == '0' && Lookahead is 'x' or 'X')
        {
            literalBase = IntegerLiteralBase.Hexadecimal;
            Advance();
            Advance();
        }

        var digitsStart = _position;
        while (literalBase == IntegerLiteralBase.Hexadecimal
            ? IsHexDigit(Current) || Current == '_'
            : char.IsDigit(Current) || Current == '_')
        {
            Advance();
        }

        if (!TryParseIntegerMagnitude(_source[digitsStart.._position], literalBase, out var magnitude))
        {
            _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "integer");
            return MakeToken(TokenKind.Error);
        }

        if (isUnsigned && sign == IntegerLiteralSign.Negative)
        {
            _diagnostics.ReportUnsignedNegativeLiteral(CurrentSpan());
            return MakeToken(TokenKind.Error);
        }

        IntLiteralInfo? info;
        if (isUnsigned)
        {
            var maximum = isLong ? ulong.MaxValue : uint.MaxValue;
            info = magnitude <= maximum
                ? new IntLiteralInfo(
                    magnitude,
                    sign,
                    literalBase,
                    isLong ? IntegerLiteralWidth.Bits64 : IntegerLiteralWidth.Bits32,
                    IntegerLiteralSignedness.Unsigned)
                : null;
        }
        else if (isLong)
        {
            info = FitsSignedMagnitude(magnitude, sign, IntegerLiteralWidth.Bits64)
                ? new IntLiteralInfo(
                    magnitude,
                    sign,
                    literalBase,
                    IntegerLiteralWidth.Bits64,
                    IntegerLiteralSignedness.Signed)
                : null;
        }
        else
        {
            info = InferTypedSignedInteger(magnitude, sign, literalBase);
        }

        if (info is { } validInfo)
            return MakeToken(TokenKind.IntLiteral, validInfo);

        if (isLong && !isUnsigned)
        {
            _diagnostics.ReportSignedIntegerLiteralOverflow(CurrentSpan());
            return MakeToken(TokenKind.Error);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "integer");
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

    private Token ScanTypedFloatLiteral(bool isSingle = false)
    {
        Advance(); // consume ':'
        var valueStart = _position;

        if (Current == '-')
        {
            Advance();
        }

        while (char.IsDigit(Current) || Current == '_')
        {
            Advance();
        }

        if (Current == '.')
        {
            Advance();
            while (char.IsDigit(Current) || Current == '_')
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
            while (char.IsDigit(Current) || Current == '_')
            {
                Advance();
            }
        }

        var valueText = _source[valueStart.._position].Replace("_", "", StringComparison.Ordinal);
        if (double.TryParse(valueText, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            // #774: a SINGLE: literal carries its single-precision width so the C#
            // emitter re-emits the `f` suffix instead of silently widening to double.
            return isSingle
                ? MakeToken(TokenKind.FloatLiteral, new FloatLiteralInfo(value, IsSingle: true))
                : MakeToken(TokenKind.FloatLiteral, value);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), isSingle ? "SINGLE" : "FLOAT");
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

        while (char.IsDigit(Current) || Current == '_')
        {
            Advance();
        }

        if (Current == '.')
        {
            Advance();
            while (char.IsDigit(Current) || Current == '_')
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
            while (char.IsDigit(Current) || Current == '_')
            {
                Advance();
            }
        }

        // Consume optional M/m suffix
        if (Current is 'M' or 'm')
        {
            Advance();
        }

        var valueText = _source[valueStart.._position]
            .TrimEnd('M', 'm')
            .Replace("_", "", StringComparison.Ordinal);
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
        return ScanStringLiteralCore(delimiterLength: 3);
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
        return ScanStringLiteralCore(delimiterLength: 1);
    }

    private Token ScanStringLiteralCore(int delimiterLength)
    {
        for (var i = 0; i < delimiterLength; i++)
            Advance();

        var isMultiline = delimiterLength == 3;
        if (isMultiline)
        {
            if (!IsAtEnd && Current == '\r')
                Advance();
            if (!IsAtEnd && Current == '\n')
                Advance();
        }

        var text = new System.Text.StringBuilder();
        var parts = new List<InterpolatedStringTokenPart>();

        void FlushText()
        {
            if (text.Length == 0)
                return;
            parts.Add(new InterpolatedStringTextTokenPart(text.ToString()));
            text.Clear();
        }

        while (!IsAtEnd)
        {
            if (HasQuoteRun(delimiterLength))
            {
                for (var i = 0; i < delimiterLength; i++)
                    Advance();

                var isUtf8 = !IsAtEnd && Current == 'u' && Lookahead == '8';
                if (isUtf8)
                {
                    Advance();
                    Advance();
                }

                FlushText();
                if (parts.Any(part => part is InterpolatedStringExpressionTokenPart))
                {
                    if (isUtf8)
                    {
                        _diagnostics.ReportInterpolatedUtf8Literal(CurrentSpan());
                        return MakeToken(TokenKind.Error);
                    }

                    var value = string.Concat(parts.Select(part => part switch
                    {
                        InterpolatedStringTextTokenPart literal => literal.Text,
                        InterpolatedStringExpressionTokenPart expression => $"${{{expression.ExpressionText}}}",
                        _ => ""
                    }));
                    return MakeToken(
                        TokenKind.StrLiteral,
                        new StringLiteralInfo(value, parts, isMultiline, isUtf8));
                }

                var plainValue = parts.Count == 0
                    ? ""
                    : string.Concat(parts.Cast<InterpolatedStringTextTokenPart>().Select(part => part.Text));
                return MakeToken(TokenKind.StrLiteral, plainValue);
            }

            if (Current == '\\')
            {
                Advance();
                if (IsAtEnd)
                {
                    text.Append('\\');
                    break;
                }

                if (Current == 'u')
                {
                    AppendUnicodeEscape(text);
                    continue;
                }
                if (Current == 'U')
                {
                    AppendLongUnicodeEscape(text);
                    continue;
                }

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
                    _ => '\x01'
                };

                if (escaped == '\x01')
                {
                    text.Append('\\');
                    text.Append(Current);
                }
                else
                {
                    text.Append(escaped);
                }
                Advance();
                continue;
            }

            if (Current == '$' && Lookahead == '{')
            {
                var interpolationStart = _position;
                FlushText();
                Advance();
                Advance();
                var expression = ScanInterpolationExpressionText();
                if (expression == null)
                {
                    _position = interpolationStart + 2;
                    text.Append("${");
                    continue;
                }
                var intent = IsLiteralInterpolationPlaceholder(expression)
                    ? InterpolationPartIntent.LiteralPlaceholder
                    : InterpolationPartIntent.Expression;
                parts.Add(new InterpolatedStringExpressionTokenPart(expression, intent));
                continue;
            }

            if (!isMultiline && Current == '\n')
            {
                text.Append(' ');
                Advance();
                while (!IsAtEnd && Current != '"' && Current is ' ' or '\t')
                    Advance();
                continue;
            }

            text.Append(Current);
            Advance();
        }

        _diagnostics.ReportUnterminatedString(CurrentSpan());
        return MakeToken(TokenKind.Error);
    }

    private static bool IsLiteralInterpolationPlaceholder(string source)
    {
        var text = source.AsSpan().Trim();
        var position = 0;
        while (position < text.Length && char.IsDigit(text[position]))
            position++;
        if (position == 0)
            return false;

        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
        if (position < text.Length && text[position] == ',')
        {
            position++;
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
            if (position < text.Length && text[position] is '+' or '-')
                position++;
            var alignmentStart = position;
            while (position < text.Length && char.IsDigit(text[position]))
                position++;
            if (position == alignmentStart)
                return false;
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
        }

        if (position < text.Length && text[position] == ':')
        {
            position++;
            return position < text.Length
                && text[position..].IndexOfAny('{', '}') < 0;
        }

        return position == text.Length;
    }

    private bool HasQuoteRun(int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (Peek(i) != '"')
                return false;
        }
        return true;
    }

    private string? ScanInterpolationExpressionText()
    {
        var start = _position;
        var depth = 1;

        while (!IsAtEnd)
        {
            if (Current == '"' || Current == '\'')
            {
                SkipQuotedCalorInterpolationLiteral(Current);
                continue;
            }

            if (Current == '/' && Lookahead == '/')
            {
                Advance();
                Advance();
                while (!IsAtEnd && Current is not '\r' and not '\n')
                    Advance();
                continue;
            }

            if (Current == '/' && Lookahead == '*')
            {
                Advance();
                Advance();
                while (!IsAtEnd && !(Current == '*' && Lookahead == '/'))
                    Advance();
                if (!IsAtEnd)
                {
                    Advance();
                    Advance();
                }
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
                {
                    var value = _source[start.._position];
                    Advance();
                    return value;
                }
            }

            Advance();
        }

        return null;
    }

    private void SkipQuotedCalorInterpolationLiteral(char quote)
    {
        var quoteCount = quote == '"' && Lookahead == '"' && Peek(2) == '"' ? 3 : 1;
        for (var i = 0; i < quoteCount; i++)
            Advance();

        while (!IsAtEnd)
        {
            if (quoteCount == 3 && HasQuoteRun(3))
            {
                Advance();
                Advance();
                Advance();
                return;
            }
            if (quoteCount == 1 && Current == quote)
            {
                Advance();
                return;
            }
            if (Current == '\\')
            {
                Advance();
                if (!IsAtEnd)
                    Advance();
                continue;
            }
            Advance();
        }
    }

    private static bool IsHexDigit(char c)
        => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private Token ScanNumber()
    {
        var sign = IntegerLiteralSign.Positive;
        if (Current == '-')
        {
            sign = IntegerLiteralSign.Negative;
            Advance();
        }

        // Check for hex literal: 0x or 0X prefix
        if (Current == '0' && Lookahead is 'x' or 'X')
        {
            Advance(); // consume '0'
            Advance(); // consume 'x'/'X'
            var digitsStart = _position;
            while (IsHexDigit(Current) || Current == '_')
            {
                Advance();
            }

            // Consume optional U/UL suffix on hex literals
            bool hexUnsigned = false;
            bool hexLong = false;
            if (Current is 'U' or 'u')
            {
                hexUnsigned = true;
                Advance();
                if (Current is 'L' or 'l') { hexLong = true; Advance(); }
            }
            else if (Current is 'L' or 'l')
            {
                hexLong = true;
                Advance();
                if (Current is 'U' or 'u') { hexUnsigned = true; Advance(); }
            }

            if (!TryParseIntegerMagnitude(
                _source[digitsStart..(_position - GetIntegerSuffixLength(hexUnsigned, hexLong))],
                IntegerLiteralBase.Hexadecimal,
                out var magnitude))
            {
                _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "hex number");
                return MakeToken(TokenKind.Error);
            }

            if (hexUnsigned && sign == IntegerLiteralSign.Negative)
            {
                _diagnostics.ReportUnsignedNegativeLiteral(CurrentSpan());
                return MakeToken(TokenKind.Error);
            }

            var info = InferSuffixedInteger(
                magnitude,
                sign,
                IntegerLiteralBase.Hexadecimal,
                hexUnsigned,
                hexLong);
            if (info is { } validInfo)
                return MakeToken(TokenKind.IntLiteral, validInfo);

            if (hexLong && !hexUnsigned)
            {
                _diagnostics.ReportSignedIntegerLiteralOverflow(CurrentSpan());
                return MakeToken(TokenKind.Error);
            }

            _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "hex number");
            return MakeToken(TokenKind.Error);
        }

        while (char.IsDigit(Current) || Current == '_')
        {
            Advance();
        }

        // Check for scientific notation without decimal point (e.g., 1E-06, 2E+10)
        if (Current is 'E' or 'e' && Lookahead is '+' or '-' or (>= '0' and <= '9'))
        {
            Advance(); // consume E/e
            if (Current is '+' or '-')
            {
                Advance(); // consume sign
            }
            while (char.IsDigit(Current) || Current == '_')
            {
                Advance();
            }
            var sciText = CurrentText().Replace("_", "");
            if (double.TryParse(sciText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var sciValue))
            {
                return MakeToken(TokenKind.FloatLiteral, sciValue);
            }
        }

        // Check for float
        if (Current == '.' && char.IsDigit(Lookahead))
        {
            Advance(); // consume '.'
            while (char.IsDigit(Current) || Current == '_')
            {
                Advance();
            }

            // Check for scientific notation (e.g., 2.22E-16, 1.5e+10)
            if (Current is 'E' or 'e')
            {
                Advance(); // consume E/e
                if (Current is '+' or '-')
                {
                    Advance(); // consume sign
                }
                while (char.IsDigit(Current) || Current == '_')
                {
                    Advance();
                }
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

        var intText = CurrentText();
        var suffixLength = GetIntegerSuffixLength(isUnsignedSuffix, hasLongSuffix);
        var numericText = intText.AsSpan(0, intText.Length - suffixLength);
        if (sign == IntegerLiteralSign.Negative)
            numericText = numericText[1..];

        if (!TryParseIntegerMagnitude(numericText.ToString(), IntegerLiteralBase.Decimal, out var integerMagnitude))
        {
            _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "number");
            return MakeToken(TokenKind.Error);
        }

        if (isUnsignedSuffix && sign == IntegerLiteralSign.Negative)
        {
            _diagnostics.ReportUnsignedNegativeLiteral(CurrentSpan());
            return MakeToken(TokenKind.Error);
        }

        var integerInfo = InferSuffixedInteger(
            integerMagnitude,
            sign,
            IntegerLiteralBase.Decimal,
            isUnsignedSuffix,
            hasLongSuffix);
        if (integerInfo is { } validIntegerInfo)
            return MakeToken(TokenKind.IntLiteral, validIntegerInfo);

        if (hasLongSuffix && !isUnsignedSuffix)
        {
            _diagnostics.ReportSignedIntegerLiteralOverflow(CurrentSpan());
            return MakeToken(TokenKind.Error);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "number");
        return MakeToken(TokenKind.Error);
    }

    private static int GetIntegerSuffixLength(bool isUnsigned, bool isLong)
        => (isUnsigned ? 1 : 0) + (isLong ? 1 : 0);

    private static bool TryParseIntegerMagnitude(
        string source,
        IntegerLiteralBase literalBase,
        out ulong magnitude)
    {
        magnitude = 0;
        if (source.Length == 0
            || source[0] == '_'
            || source[^1] == '_')
        {
            return false;
        }

        var digits = source.Replace("_", "", StringComparison.Ordinal);
        var style = literalBase == IntegerLiteralBase.Hexadecimal
            ? System.Globalization.NumberStyles.AllowHexSpecifier
            : System.Globalization.NumberStyles.None;
        return ulong.TryParse(
            digits,
            style,
            System.Globalization.CultureInfo.InvariantCulture,
            out magnitude);
    }

    private static bool FitsSignedMagnitude(
        ulong magnitude,
        IntegerLiteralSign sign,
        IntegerLiteralWidth width)
    {
        var maximum = width == IntegerLiteralWidth.Bits32
            ? sign == IntegerLiteralSign.Negative ? 0x8000_0000UL : int.MaxValue
            : sign == IntegerLiteralSign.Negative ? 0x8000_0000_0000_0000UL : long.MaxValue;
        return magnitude <= maximum;
    }

    private static IntLiteralInfo? InferUnsuffixedInteger(
        ulong magnitude,
        IntegerLiteralSign sign,
        IntegerLiteralBase literalBase)
    {
        if (sign == IntegerLiteralSign.Negative)
        {
            if (FitsSignedMagnitude(magnitude, sign, IntegerLiteralWidth.Bits32))
            {
                return new IntLiteralInfo(
                    magnitude,
                    sign,
                    literalBase,
                    IntegerLiteralWidth.Bits32,
                    IntegerLiteralSignedness.Signed);
            }
            if (FitsSignedMagnitude(magnitude, sign, IntegerLiteralWidth.Bits64))
            {
                return new IntLiteralInfo(
                    magnitude,
                    sign,
                    literalBase,
                    IntegerLiteralWidth.Bits64,
                    IntegerLiteralSignedness.Signed);
            }
            return null;
        }

        if (magnitude <= int.MaxValue)
        {
            return new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                IntegerLiteralWidth.Bits32,
                IntegerLiteralSignedness.Signed);
        }

        if (literalBase == IntegerLiteralBase.Hexadecimal && magnitude <= uint.MaxValue)
        {
            return new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                IntegerLiteralWidth.Bits32,
                IntegerLiteralSignedness.Unsigned);
        }

        if (magnitude <= long.MaxValue)
        {
            return new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                IntegerLiteralWidth.Bits64,
                IntegerLiteralSignedness.Signed);
        }

        return new IntLiteralInfo(
            magnitude,
            sign,
            literalBase,
            IntegerLiteralWidth.Bits64,
            IntegerLiteralSignedness.Unsigned);
    }

    private static IntLiteralInfo? InferTypedSignedInteger(
        ulong magnitude,
        IntegerLiteralSign sign,
        IntegerLiteralBase literalBase)
    {
        if (FitsSignedMagnitude(magnitude, sign, IntegerLiteralWidth.Bits32))
        {
            return new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                IntegerLiteralWidth.Bits32,
                IntegerLiteralSignedness.Signed);
        }

        return FitsSignedMagnitude(magnitude, sign, IntegerLiteralWidth.Bits64)
            ? new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                IntegerLiteralWidth.Bits64,
                IntegerLiteralSignedness.Signed)
            : null;
    }

    private static IntLiteralInfo? InferSuffixedInteger(
        ulong magnitude,
        IntegerLiteralSign sign,
        IntegerLiteralBase literalBase,
        bool isUnsigned,
        bool isLong)
    {
        if (!isUnsigned && !isLong)
            return InferUnsuffixedInteger(magnitude, sign, literalBase);

        if (isUnsigned)
        {
            if (sign == IntegerLiteralSign.Negative)
                return null;
            var width = isLong || magnitude > uint.MaxValue
                ? IntegerLiteralWidth.Bits64
                : IntegerLiteralWidth.Bits32;
            return new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                width,
                IntegerLiteralSignedness.Unsigned);
        }

        if (magnitude <= long.MaxValue)
        {
            return new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                IntegerLiteralWidth.Bits64,
                IntegerLiteralSignedness.Signed);
        }

        return sign == IntegerLiteralSign.Negative
            && magnitude == 0x8000_0000_0000_0000UL
            ? new IntLiteralInfo(
                magnitude,
                sign,
                literalBase,
                IntegerLiteralWidth.Bits64,
                IntegerLiteralSignedness.Signed)
            : null;
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

    /// <summary>
    /// Handles C# interpolated strings ($"...") that weren't fully converted.
    /// Treats $" as a regular string opening.
    /// </summary>
    private Token ScanDollarString()
    {
        Advance(); // consume '$'
        if (Current == '"')
        {
            return ScanStringLiteralValue();
        }
        // Bare $ — skip it
        return MakeToken(TokenKind.Identifier, "$");
    }

    /// <summary>
    /// Skips semicolons from raw C# that leaked into converter output.
    /// </summary>
    private Token ScanSkipSemicolon()
    {
        Advance(); // consume ';'
        return NextToken(); // skip and return next real token
    }

    private Token ScanError()
    {
        // For high Unicode characters (surrogate pairs, private-use, CJK, etc.),
        // consume them as identifiers rather than reporting errors.
        // Converter output may embed icon glyphs in attributes.
        // Only tolerate chars above U+00FF (high-plane) to preserve error reporting
        // for common ASCII-adjacent symbols like ©, ®, etc.
        if (Current > '\u00FF' || char.IsHighSurrogate(Current) ||
            char.GetUnicodeCategory(Current) is System.Globalization.UnicodeCategory.PrivateUse)
        {
            var sb = new System.Text.StringBuilder();
            while (!IsAtEnd && (Current > '\u00FF' || char.IsHighSurrogate(Current) ||
                char.IsLowSurrogate(Current) ||
                char.GetUnicodeCategory(Current) is System.Globalization.UnicodeCategory.PrivateUse))
            {
                sb.Append(Current);
                Advance();
            }
            return MakeToken(TokenKind.Identifier, sb.ToString());
        }
        _diagnostics.ReportUnexpectedCharacter(CurrentSpan(), Current);
        Advance();
        return MakeToken(TokenKind.Error);
    }
}
