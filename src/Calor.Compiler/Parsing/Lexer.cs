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
    private readonly List<string> _docComments = new();
    private int _lastDocLine;

    private static readonly Dictionary<string, TokenKind> CanonicalKeywords = new(StringComparer.Ordinal)
    {
        ["module"] = TokenKind.Module,
        ["class"] = TokenKind.Class,
        ["record"] = TokenKind.Record,
        ["interface"] = TokenKind.Interface,
        ["func"] = TokenKind.Func,
        ["let"] = TokenKind.Let,
        ["var"] = TokenKind.Var,
        ["return"] = TokenKind.Return,
        ["set"] = TokenKind.Assign,
        ["if"] = TokenKind.If,
        ["else"] = TokenKind.Else,
        ["for"] = TokenKind.For,
        ["from"] = TokenKind.From,
        ["to"] = TokenKind.To,
        ["step"] = TokenKind.Step,
        ["each"] = TokenKind.Foreach,
        ["in"] = TokenKind.In,
        ["do"] = TokenKind.Do,
        ["while"] = TokenKind.While,
        ["break"] = TokenKind.Break,
        ["continue"] = TokenKind.Continue,
        ["goto"] = TokenKind.Goto,
        ["yield"] = TokenKind.Yield,
        ["match"] = TokenKind.Match,
        ["case"] = TokenKind.Case,
        ["using"] = TokenKind.Using,
        ["extends"] = TokenKind.Extends,
        ["implements"] = TokenKind.Implements,
        ["where"] = TokenKind.Where,
        ["new"] = TokenKind.New,
        ["this"] = TokenKind.This,
        ["base"] = TokenKind.Base,
        ["try"] = TokenKind.Try,
        ["catch"] = TokenKind.Catch,
        ["finally"] = TokenKind.Finally,
        ["throw"] = TokenKind.Throw,
        ["when"] = TokenKind.When,
        ["await"] = TokenKind.Await,
        ["with"] = TokenKind.With,
        ["enum"] = TokenKind.Enum,
        ["event"] = TokenKind.Event,
        ["delegate"] = TokenKind.Delegate,
        ["ctor"] = TokenKind.Constructor,
        ["property"] = TokenKind.Property,
        ["op"] = TokenKind.OperatorOverload,
        ["pp"] = TokenKind.Preprocessor,
        ["field"] = TokenKind.FieldDef,
        ["out"] = TokenKind.Out,
        ["get"] = TokenKind.Get,
        ["init"] = TokenKind.Init,
        ["requires"] = TokenKind.Requires,
        ["invariant"] = TokenKind.Invariant,
        ["assume"] = TokenKind.Assume,
        ["proof"] = TokenKind.Proof,
        ["unsafe"] = TokenKind.Unsafe,
        ["fixed"] = TokenKind.Fixed,
        ["lock"] = TokenKind.SyncBlock,
    };

    /// <summary>
    /// Every canonical word the lexer accepts, including counted raw frames.
    /// Single source of truth for
    /// the docs-drift check (<c>calor self-check docs</c>).
    /// </summary>
    public static IReadOnlyCollection<string> KeywordNames { get; } =
        CanonicalKeywords.Keys
            .Append("raw")
            .ToArray();

    public Lexer(string source, DiagnosticBag diagnostics)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Lexes a fragment of a larger source, such as the text of a string-interpolation hole,
    /// reporting spans relative to the enclosing source (0.25 F2, #906).
    /// </summary>
    internal Lexer(string source, DiagnosticBag diagnostics, int baseOffset, int line, int column)
        : this(source, diagnostics)
    {
        _baseOffset = baseOffset;
        _line = line;
        _column = column;
    }

    private readonly int _baseOffset;

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
        => new(_baseOffset + _tokenStart, _position - _tokenStart, _tokenLine, _tokenColumn);

    private string CurrentText()
        => _source[_tokenStart.._position];

    private Token MakeToken(TokenKind kind, object? value = null)
    {
        string? documentation = null;
        if (kind is not (TokenKind.Whitespace or TokenKind.Newline))
        {
            if (_docComments.Count > 0 && _tokenLine == _lastDocLine + 1)
                documentation = string.Join("\n", _docComments);
            _docComments.Clear();
        }
        return new Token(kind, CurrentText(), CurrentSpan(), value) { DocComment = documentation };
    }

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
        bool sawSpaceIndent = false;

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
                sawSpaceIndent |= hasSpace;
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
                                indentStack.Peek() + 1))
                        {
                            IndentationDepth = indentStack.Count - 1
                        };
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
                            tabIndentEdits, isSafeToApply: !sawMixedIndent && !sawSpaceIndent));
                }
                if (firstWidthTok is { } widthTok)
                {
                    _diagnostics.ReportWarningWithFix(widthTok.Span, DiagnosticCode.NonStandardIndentWidth,
                        $"Indentation step is not 2 spaces on {widthEdits.Count} line(s). " +
                        "Calor indentation is canonically 2 spaces per level.",
                        new SuggestedFix(
                            "Re-indent to 2 spaces per level",
                            widthEdits, isSafeToApply: !sawMixedIndent && tabIndentEdits.Count == 0));
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
                            indentStack.Peek() + 1))
                    {
                        IndentationDepth = indentStack.Count - 1
                    };
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

            yield return new Token(tok.Kind, tok.Text, tok.Span, tok.Value)
            {
                DocComment = tok.DocComment,
                IndentationDepth = indentStack.Count - 1
            };
            // Payload line breaks are opaque tokens, not Calor layout. Resume
            // layout at the next header when a frame itself ends on a line break.
            if (tok.Value is RawCSharpFrame && tok.Text[^1] is '\r' or '\n')
            {
                yield return new Token(TokenKind.Newline, "",
                    new TextSpan(tok.Span.End, 0, raw[i + 1].Span.Line, 1));
                atLineStart = true;
                currentIndent = 0;
                currentIndentHasTab = false;
            }
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
            '§' => ScanRetiredSectionMarker(),
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
            '@' => ScanEscapedIdentifier(),
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
            '∀' or '∃' => ScanRemovedQuantifier(),
            '`' => ScanRetiredBacktickIdentifier(),
            '\'' => ScanCharLiteral(),
            '$' => ScanRetiredDollarString(),
            ';' => ScanSingle(TokenKind.Semicolon),
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
        if (Current == '>')
        {
            Advance();
            return MakeToken(TokenKind.FatArrow);
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
            var commentStart = _position;
            var commentLine = _line;
            var lineStart = _source.LastIndexOf('\n', Math.Max(0, commentStart - 1)) + 1;
            var isDocumentation = Peek(2) == '/'
                && _source.AsSpan(lineStart, commentStart - lineStart).Trim().IsEmpty;
            // Line comment: skip to end of line
            // Only \n terminates (not bare \r) to handle embedded \r in doc comments
            while (Current != '\n' && Current != '\0')
                Advance();
            if (isDocumentation)
            {
                if (_lastDocLine + 1 != commentLine)
                    _docComments.Clear();
                _docComments.Add(_source[(commentStart + 3).._position].Trim());
                _lastDocLine = commentLine;
            }
            else
            {
                _docComments.Clear();
            }
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
            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.InvalidSectionOperator,
                "The ASCII arrow '->' is removed. Use '→' for a function signature; write implication as '!p || q'.");
            return MakeToken(TokenKind.Error);
        }
        // A sign is an operator, including without whitespace in a-1.
        Advance();
        return MakeToken(TokenKind.Minus);
    }

    /// <summary>
    /// Rejects a removed Unicode quantifier without rewriting its surrounding contract.
    /// </summary>
    private Token ScanRemovedQuantifier()
    {
        Advance();
        var span = CurrentSpan();
        _diagnostics.ReportError(span, DiagnosticCode.InvalidLispExpression,
            $"Legacy quantifier '{CurrentText()}' is removed. Migrate the full quantified contract; replacing its prefix alone is not safe.");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanDotOrNumber()
    {
        if (Lookahead == '.')
        {
            Advance();
            Advance();
            return MakeToken(TokenKind.RangeOp);
        }

        // Check for decimal number starting with .
        if (char.IsDigit(Lookahead))
        {
            return ScanNumber();
        }
        // Otherwise it's just a dot for member access
        Advance();
        return MakeToken(TokenKind.Dot);
    }

    private Token ScanRetiredBacktickIdentifier()
    {
        Advance();
        while (!IsAtEnd && Current is not '`' and not '\r' and not '\n')
            Advance();
        if (Current == '`')
            Advance();
        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.InvalidSectionOperator,
            "Backtick identifiers are removed. Use '@name' for a keyword collision.");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanRetiredDollarString()
    {
        Advance();
        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.InvalidSectionOperator,
            "Prefixed interpolated strings are removed. Use a plain string with '${expression}' holes.");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanRetiredSectionMarker()
    {
        Advance();
        if (Current == '/')
            Advance();
        while (char.IsLetterOrDigit(Current) || Current is '_' or '?' or '^')
            Advance();
        var marker = CurrentText();
        var guidance = marker switch
        {
            "§M" => "Use 'module Name'.",
            "§F" or "§MT" or "§AF" or "§AMT" => "Use 'func Name' with a '→' signature.",
            "§B" => "Use 'let' or 'var' with a type:name binding.",
            "§E" => "Use an '! effects' header.",
            "§Q" => "Use a 'requires expression' header.",
            "§S" => "Use an '=> expression' postcondition header.",
            "§SN" or "§DP" or "§BR" => "Use constrained '/// since', '/// deprecated' or '/// breaking' documentation.",
            "§RAW" or "§CS" or "§CSHARP" => "Use 'raw reason N' with the exact UTF-8 payload length.",
            _ when marker.StartsWith("§/", StringComparison.Ordinal) => "End blocks by dedenting; expressions need no closer tags.",
            _ => "Use canonical lowercase words and ordinary C# expression punctuation."
        };
        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnknownSectionMarker,
            $"Legacy section marker '{marker}' is removed. {guidance}");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanSingle(TokenKind kind)
    {
        Advance();
        return MakeToken(kind);
    }

    private Token ScanEscapedIdentifier()
    {
        Advance();
        if (Current == '"')
        {
            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.InvalidSectionOperator,
                "Verbatim string prefixes are removed. Use a plain string with escaped literal text.");
            return MakeToken(TokenKind.Error);
        }
        if (!char.IsLetter(Current) && Current != '_')
            return MakeToken(TokenKind.At);
        var start = _position;
        while (char.IsLetterOrDigit(Current) || Current == '_')
            Advance();
        return MakeToken(TokenKind.Identifier, _source[start.._position]);
    }

    private Token ScanColonOrTypedLiteral()
    {
        // Standalone colon (v2 syntax for positional attributes)
        Advance();
        return MakeToken(TokenKind.Colon);
    }

    // Design note: Dots are intentionally included in identifiers so that qualified
    // names like Math.PI, StringComparison.Ordinal, and System.Console are lexed as
    // single tokens. This produces ReferenceNode("Math.PI") rather than a
    // FieldAccessNode chain. The CSharpEmitter (Visit(ReferenceNode)) handles dots
    // by splitting on '.' and sanitizing each part, so the round-trip is lossless.
    // Exact this/base receivers stop before the dot and use native member access.
    // See DottedNameRoundTripTests for verification.
    private bool IsLineHead(int start)
    {
        for (var i = start - 1; i >= 0 && _source[i] is not '\r' and not '\n'; i--)
            if (_source[i] is not ' ' and not '\t')
                return false;
        return true;
    }

    private Token ScanRawFrame()
    {
        Token Fail(string message)
        {
            _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.InvalidRawFrame, message);
            return MakeToken(TokenKind.Error);
        }

        while (Current is ' ' or '\t')
            Advance();
        var reasonStart = _position;
        while (!IsAtEnd && !char.IsWhiteSpace(Current))
            Advance();
        var marker = _source[reasonStart.._position];
        if (marker.Length == 0 || !RawCSharpFrame.TryParseReason(marker, out var frame))
            return Fail("raw requires a valid reason marker followed by a UTF-8 byte length.");
        while (Current is ' ' or '\t')
            Advance();
        if (IsAtEnd || Current is '\r' or '\n')
            return Fail("Missing raw payload length: expected a nonnegative decimal UTF-8 byte count.");
        var length = 0;
        var digits = 0;
        while (Current is >= '0' and <= '9')
        {
            var digit = Current - '0';
            if (length > (int.MaxValue - digit) / 10)
                return Fail("Raw payload length overflows the supported UTF-8 byte count (2147483647).");
            length = length * 10 + digit;
            digits++;
            Advance();
        }
        if (digits == 0)
            return Fail("Invalid raw payload length: expected a nonnegative decimal UTF-8 byte count.");
        while (Current is ' ' or '\t')
            Advance();
        if (Current is not '\r' and not '\n')
            return Fail("Invalid raw header: the decimal byte length must be followed by a newline.");
        if (Current == '\r')
        {
            Advance();
            if (Current == '\n')
                Advance();
        }
        else
            Advance();

        var payloadStart = _position;
        var remaining = length;
        while (remaining > 0)
        {
            if (IsAtEnd)
                return Fail($"Truncated raw payload: missing {remaining} of {length} UTF-8 bytes.");
            var status = System.Text.Rune.DecodeFromUtf16(
                _source.AsSpan(_position), out var rune, out var consumed);
            if (status != System.Buffers.OperationStatus.Done)
                return Fail("Invalid Unicode in raw payload: an unpaired UTF-16 surrogate has no exact UTF-8 encoding.");
            if (rune.Utf8SequenceLength > remaining)
                return Fail("Raw payload length ends inside a UTF-8 code point.");
            remaining -= rune.Utf8SequenceLength;
            for (var i = 0; i < consumed; i++)
                Advance();
        }
        if (!IsAtEnd && Current is not '\r' and not '\n'
            && _source[_position - 1] is not '\r' and not '\n')
            return Fail("Raw payload must be followed by a newline or end of file, outside its byte count.");
        frame = frame with { Code = _source[payloadStart.._position] };
        if (frame.IsCompilerDirective)
        {
            if (!frame.TryValidateCompilerDirective(out var error))
                return Fail(error);
            return MakeToken(TokenKind.CompilerDirective, frame);
        }
        return MakeToken(marker == "statement" ? TokenKind.RawCSharp : TokenKind.CSharpInterop, frame);
    }

    private Token ScanIdentifierOrTypedLiteral()
    {
        while (char.IsLetterOrDigit(Current) || Current == '_' || Current == '.' && Lookahead != '.')
        {
            if (Current == '.' && _source.AsSpan(_tokenStart, _position - _tokenStart) is "this" or "base")
                break;
            Advance();
        }

        var text = CurrentText();

        if (text == "raw"
            && (IsAtEnd || Current is ' ' or '\t' or '\r' or '\n')
            && IsLineHead(_tokenStart))
        {
            return ScanRawFrame();
        }

        // Reject retired literal prefixes without capturing valid type:name annotations.
        if (Current == ':')
        {
            var upperText = text.ToUpperInvariant();
            var lookahead = Peek(1);
            var suffix = _source.AsSpan(_position + 1);
            var looksLikeLiteral = upperText switch
            {
                "STR" => lookahead == '"',
                "BOOL" => suffix.StartsWith("true", StringComparison.Ordinal)
                    && (suffix.Length == 4 || !char.IsLetterOrDigit(suffix[4]))
                    || suffix.StartsWith("false", StringComparison.Ordinal)
                    && (suffix.Length == 5 || !char.IsLetterOrDigit(suffix[5])),
                "INT" or "LONG" or "UINT" or "ULONG" => char.IsDigit(lookahead) || lookahead == '-',
                "FLOAT" or "SINGLE" or "DEC" or "DECIMAL" => char.IsDigit(lookahead) || lookahead is '-' or '.',
                _ => false
            };
            if (looksLikeLiteral)
            {
                Advance(); // consume the retired prefix's colon, not its value
                _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.InvalidTypedLiteral,
                    $"Typed literal prefix '{text}:' is removed. Use a bare literal with its C# numeric suffix when needed.");
                return MakeToken(TokenKind.Error);
            }

            // A declaration type:name still uses an ordinary colon.
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

        if (CanonicalKeywords.TryGetValue(text, out var keyword))
        {
            if (keyword == TokenKind.Preprocessor)
            {
                var lineEnd = _position;
                while (lineEnd < _source.Length && _source[lineEnd] is not ('\r' or '\n'))
                    lineEnd++;
                return MakeToken(keyword, _source[_position..lineEnd]);
            }
            return MakeToken(keyword);
        }

        return MakeToken(TokenKind.Identifier);
    }

    /// <summary>
    /// Scans one UTF-16 character using C# escape syntax, without crossing a line.
    /// </summary>
    private Token ScanCharLiteral()
    {
        Advance(); // consume opening '
        while (!IsAtEnd && Current is not ('\'' or '\r' or '\n'))
        {
            if (Current == '\\')
            {
                Advance();
                if (IsAtEnd || Current is '\r' or '\n')
                    break;
            }
            Advance();
        }
        if (Current == '\'')
        {
            Advance();
            var text = _source[_tokenStart.._position];
            var literal = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseToken(text);
            if (!literal.ContainsDiagnostics && literal.Value is char value && literal.Text.Length == text.Length)
                return MakeToken(TokenKind.CharLiteral, value);
        }

        _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.InvalidCharLiteral,
            "A character literal must contain exactly one UTF-16 character or a valid escape sequence, enclosed in single quotes.");
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
                FlushText();
                Advance();
                Advance();
                var (holeStart, holeLine, holeColumn) = (_baseOffset + _position, _line, _column);
                var expression = ScanInterpolationExpressionText();
                if (expression == null)
                {
                    _diagnostics.ReportError(CurrentSpan(), DiagnosticCode.UnterminatedString,
                        "Unterminated interpolation hole: close ${expression} with }, or use \\${ for literal text.");
                    return MakeToken(TokenKind.Error);
                }
                parts.Add(new InterpolatedStringExpressionTokenPart(
                    expression, InterpolationPartIntent.Expression, holeStart, holeLine, holeColumn));
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
            if (Current is 'f' or 'F')
                return ScanFloatingSuffix(isSingle: true);
            if (Current is 'd' or 'D')
                return ScanFloatingSuffix(isSingle: false);
            if (Current is 'M' or 'm')
                return ScanDecimalSuffix();
            var sciText = CurrentText().Replace("_", "");
            if (double.TryParse(sciText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var sciValue) && double.IsFinite(sciValue))
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

            if (Current is 'f' or 'F')
                return ScanFloatingSuffix(isSingle: true);
            if (Current is 'd' or 'D')
                return ScanFloatingSuffix(isSingle: false);

            if (Current is 'M' or 'm')
                return ScanDecimalSuffix();

            var floatText = CurrentText().Replace("_", "");
            if (double.TryParse(floatText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floatValue) && double.IsFinite(floatValue))
            {
                return MakeToken(TokenKind.FloatLiteral, floatValue);
            }
        }

        if (Current is 'f' or 'F')
            return ScanFloatingSuffix(isSingle: true);
        if (Current is 'd' or 'D')
            return ScanFloatingSuffix(isSingle: false);

        // Check for decimal suffix on integers (42M, 100m)
        if (Current is 'M' or 'm')
            return ScanDecimalSuffix();

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

    private Token ScanDecimalSuffix()
    {
        Advance();
        var text = CurrentText()[..^1].Replace("_", "", StringComparison.Ordinal);
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
            return MakeToken(TokenKind.DecimalLiteral, value);

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "decimal number");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanFloatingSuffix(bool isSingle)
    {
        Advance();
        var text = CurrentText()[..^1].Replace("_", "", StringComparison.Ordinal);
        if (isSingle && float.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && float.IsFinite(value))
        {
            return MakeToken(TokenKind.FloatLiteral, new FloatLiteralInfo(value, IsSingle: true));
        }
        if (!isSingle && double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var doubleValue) && double.IsFinite(doubleValue))
            return MakeToken(TokenKind.FloatLiteral, doubleValue);

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), isSingle ? "single-precision number" : "double-precision number");
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

    private IntLiteralInfo? InferSuffixedInteger(
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

        // Minus is now its own token. Leave this one contextual magnitude for
        // the parser to validate as an immediate unary-minus operand.
        var afterMinus = _source.AsSpan(0, _tokenStart).TrimEnd().EndsWith("-".AsSpan());
        return magnitude == 0x8000_0000_0000_0000UL
            && (sign == IntegerLiteralSign.Negative || afterMinus)
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
